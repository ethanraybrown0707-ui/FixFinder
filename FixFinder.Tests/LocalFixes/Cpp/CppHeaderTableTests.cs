using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>The C++ side of the standard header table: what the rule does with it, and whether every entry in it is true.</summary>
public class CppHeaderTableTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string name, string body)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, body);
        return path;
    }

    private static ParsedError Msvc(string message, string code, string file, int line) => new()
    {
        LanguageId = "msvc",
        Confidence = 90,
        RawText = message,
        FirstLineSequence = 0,
        ExceptionType = "compile error",
        ErrorCode = code,
        Message = message,
        Frames = [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = "" }],
    };

    private LocalFixContext Context(ParsedError error, params ParsedError[] others) => new()
    {
        Error = error,
        Others = others,
        SourceRoot = _temp.Path,
        FromBuild = true,
    };

    [Fact]
    public void CoutAndEndlBothComeFromIostream()
    {
        var file = Write("app.cpp", "int main() {\n    std::cout << \"hello\" << std::endl;\n    return 0;\n}\n");

        var fix = new CMissingStandardHeader().Propose(Context(
            Msvc("'cout': is not a member of 'std'", "C2039", file, 2),
            Msvc("'cout': undeclared identifier", "C2065", file, 2),
            Msvc("'endl': is not a member of 'std'", "C2039", file, 2)))!;

        Assert.Equal(1, fix.StartLine);
        Assert.Equal(["#include <iostream>"], fix.NewLines);
    }

    [Fact]
    public void AStandardContainerGetsItsOwnHeaderAfterTheOthers()
    {
        var file = Write("app.cpp", "#include <iostream>\nint main() {\n    std::vector<int> values = {1, 2, 3};\n    std::cout << values.size();\n}\n");

        var fix = new CMissingStandardHeader().Propose(Context(Msvc("'vector': is not a member of 'std'", "C2039", file, 3)))!;

        Assert.Equal(2, fix.StartLine);
        Assert.Equal(["#include <vector>"], fix.NewLines);
    }

    [Fact]
    public void ACFunctionCalledFromCppGetsTheCHeader()
    {
        var file = Write("app.cpp", "int main() {\n    printf(\"hello\");\n    return 0;\n}\n");

        var fix = new CMissingStandardHeader().Propose(Context(Msvc("'printf': identifier not found", "C3861", file, 2)))!;

        Assert.Equal(["#include <stdio.h>"], fix.NewLines);
    }

    [Fact]
    public void AStdNameInACFileIsNotLookedUp()
    {
        var file = Write("app.c", "int main(void) {\n    std::cout << 1;\n}\n");

        Assert.Null(new CMissingStandardHeader().Propose(Context(Msvc("'cout': is not a member of 'std'", "C2039", file, 2))));
    }

    [Fact]
    public void AHeaderAlreadyIncludedIsNotAddedAgain()
    {
        var file = Write("app.cpp", "#include <vector>\nint main() {\n    std::vector<int> v;\n}\n");

        Assert.Null(new CMissingStandardHeader().Propose(Context(Msvc("'vector': is not a member of 'std'", "C2039", file, 3))));
    }

    [Fact]
    public void AMemberOfYourOwnTypeIsNotAHeaderQuestion()
    {
        var file = Write("app.cpp", "struct Parcel { int weight; };\nint main() {\n    Parcel p;\n    return p.wieght;\n}\n");

        Assert.Null(new CMissingStandardHeader().Propose(Context(Msvc("'wieght': is not a member of 'Parcel'", "C2039", file, 4))));
    }

    [Fact]
    public void EveryHeaderTheCppTableNamesIsKnownAsStandard() =>
        Assert.All(CStandardLibrary.CppTable.Values.Distinct(), header => Assert.Contains(header, CStandardLibrary.Headers));

    private (ParsedError Error, LocalFixContext Context) Gpp(string fixture, string source)
    {
        var file = Write("app.cpp", source);

        var text = File.ReadAllText(Path.Combine(Fixtures.Root, "StackTraces", "gcc", fixture))
            .Replace("{FILE_ESCAPED}", file.Replace("\\", "\\\\"), StringComparison.Ordinal)
            .Replace("{FILE_FORWARD}", file.Replace('\\', '/'), StringComparison.Ordinal)
            .Replace("{FILE}", file, StringComparison.Ordinal);

        var output = text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n')
            .Select((line, i) => new CapturedLine(i, StreamKind.StdErr, line, TimeSpan.Zero))
            .ToList();

        var error = new ParserRegistry().Parse(output);
        Assert.NotNull(error);

        return (error!, new LocalFixContext { Error = error!, Output = output, SourceRoot = _temp.Path, FromBuild = true });
    }

    [Fact]
    public void GppsFixItAddsIostreamForCout()
    {
        var (error, context) = Gpp("live-mingw-cpp-cout.txt", "int main() {\n    std::cout << \"hello\" << std::endl;\n    return 0;\n}\n");

        Assert.Equal("'cout' is not a member of 'std'", error.Message);
        Assert.Equal(["#include <iostream>", "int main() {"], new CompilerFixIt().Propose(context)!.NewLines);
    }

    [Fact]
    public void GppsFixItAddsVector()
    {
        var (error, context) = Gpp(
            "live-mingw-cpp-vector.txt",
            "#include <iostream>\nint main() {\n    std::vector<int> values = {1, 2, 3};\n    std::cout << values.size() << std::endl;\n    return 0;\n}\n");

        Assert.Equal("'vector' is not a member of 'std'", error.Message);
        Assert.Equal(["#include <vector>", "int main() {"], new CompilerFixIt().Propose(context)!.NewLines);
    }

    private (string Folder, Dictionary<string, string> Headers) WriteTable()
    {
        var folder = Path.Combine(_temp.Path, "table");
        Directory.CreateDirectory(folder);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var group in CStandardLibrary.CppTable.GroupBy(entry => entry.Value).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var name = $"table{index++}.cpp";
            var body = new StringBuilder().Append($"#include <{group.Key}>\n");

            foreach (var entry in group.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                body.Append(entry.Key == "chrono"
                    ? "namespace chrono_is_declared = std::chrono;\n"
                    : $"using std::{entry.Key};\n");
            }

            body.Append("int main() { return 0; }\n");

            File.WriteAllText(Path.Combine(folder, name), body.ToString());
            headers[name] = group.Key;
        }

        return (folder, headers);
    }

    [Fact]
    public void EveryCppTableEntryIsDeclaredByItsHeaderUnderGpp()
    {
        if (Toolchains.FindGnu(cpp: true) is not { } gnu) return;

        var (folder, headers) = WriteTable();
        var (exit, output) = Run(gnu.Program, "-std=c++17 -fsyntax-only " + string.Join(" ", headers.Keys), folder);

        Assert.True(exit == 0, Describe(gnu.Name, output, headers));
    }

    [Fact]
    public void EveryCppTableEntryIsDeclaredByItsHeaderUnderMsvc()
    {
        if (Toolchains.FindMsvc() is not { SetupScript: { } vcvarsall }) return;

        var (folder, headers) = WriteTable();
        var batch = Path.Combine(folder, "check.cmd");

        File.WriteAllText(batch, string.Join("\r\n",
        [
            "@echo off",
            $"call \"{vcvarsall}\" x64 >nul",
            $"cd /d \"{folder}\"",
            $"cl /nologo /EHsc /std:c++17 /Zs {string.Join(" ", headers.Keys)}",
            "exit /b %errorlevel%",
            "",
        ]));

        var (exit, output) = Run("cmd.exe", $"/c \"{batch}\"", folder);

        Assert.True(exit == 0, Describe("MSVC", output, headers));
    }

    private static (int Exit, string Output) Run(string program, string arguments, string folder)
    {
        using var process = Process.Start(new ProcessStartInfo(program, arguments)
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        })!;

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(300_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{program} did not finish checking the table within five minutes.");
        }

        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    private static string Describe(string compiler, string output, Dictionary<string, string> headers)
    {
        var failures = output.ReplaceLineEndings("\n").Split('\n')
            .Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase))
            .Select(line => Regex.Match(line, @"table\d+\.cpp") is { Success: true } file && headers.TryGetValue(file.Value, out var header)
                ? $"<{header}>: {line.Trim()}"
                : line.Trim());

        return $"{compiler} rejected entries in the C++ header table:\n{string.Join("\n", failures)}";
    }
}
