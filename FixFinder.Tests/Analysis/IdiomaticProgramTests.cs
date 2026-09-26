using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Programs written the way each language is really written, beyond the textbook algorithms: Python dataclasses,
/// properties, abstract classes, generators and heapq; Java records, streams, Optional and try-with-resources; C#
/// records, LINQ and pattern matching; JavaScript classes with private fields and async functions; C that grows,
/// hashes and frees its own memory; C++ templates, RAII and structured bindings; Go goroutines behind a mutex.
/// </summary>
/// <remarks>
/// Every one is correct, so every error or warning on any of them is a false alarm - and every file of every one
/// must be read in full. Each program lives in Fixtures/Programs as one text, every file starting with a line
/// ==> name &lt;==. Running these found a dozen false alarms and misreadings when they were written; they stay here so
/// none comes back.
/// </remarks>
public class IdiomaticProgramTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string Folder => Path.Combine(Fixtures.Root, "Programs");

    public static TheoryData<string> Programs
    {
        get
        {
            var programs = new TheoryData<string>();
            foreach (var file in Directory.GetFiles(Folder, "*.txt").Order(StringComparer.Ordinal)) programs.Add(Path.GetFileNameWithoutExtension(file));
            return programs;
        }
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task IdiomaticCodeIsReadWholeAndLeftAlone(string name)
    {
        var files = Write(name, await File.ReadAllTextAsync(Path.Combine(Folder, name + ".txt")));
        if (await ReadAsync(files) is not { } program) return;

        foreach (var problem in program.Problems) output.WriteLine($"problem: {problem}");
        Assert.Empty(program.Problems);

        var mistakes = AbstractChecks.Run(program, new SourceText()).Where(f => f.Severity != Severity.Suggestion).ToList();
        foreach (var finding in mistakes)
            output.WriteLine($"{Path.GetFileName(finding.Span.File)}:{finding.Span.Line} [{finding.Severity}] {finding.CheckId}: {finding.Message}");

        Assert.Empty(mistakes);
    }

    /// <summary>The program read by its language's reader - or null when the tools that reader needs are not installed.</summary>
    private static async Task<IrProgram?> ReadAsync(List<string> files) => Path.GetExtension(files[0]) switch
    {
        ".py" => PythonFrontend.FindInterpreter() is { } python ? await PythonFrontend.ReadAsync(files, python) : null,
        ".java" => JavaFrontend.FindTools() is { } tools ? await JavaFrontend.ReadAsync(files, tools.Javac, tools.Java) : null,
        ".cs" => await CSharpFrontend.ReadAsync(files),
        ".js" or ".mjs" => await JavaScriptFrontend.ReadAsync(files),
        ".c" or ".cpp" => await CFrontend.ReadAsync(files),
        ".go" => GoFrontend.FindGo() is { } go ? await GoFrontend.ReadAsync(files, go) : null,
        var other => throw new ArgumentException($"no reader for {other} files"),
    };

    /// <summary>Writes a program's files into a folder of its own, and gives back their paths in the order they are written.</summary>
    private List<string> Write(string name, string program)
    {
        var files = new List<string>();
        string? path = null;
        var lines = new List<string>();

        void Finish()
        {
            if (path is null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Join('\n', lines) + "\n");
            files.Add(path);
        }

        foreach (var line in program.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith("==> ", StringComparison.Ordinal) && line.EndsWith(" <==", StringComparison.Ordinal))
            {
                Finish();
                path = Path.Combine(_temp.Path, name, line["==> ".Length..^" <==".Length]);
                lines.Clear();
                continue;
            }

            lines.Add(line);
        }

        Finish();
        return files;
    }
}
