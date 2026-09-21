using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>What makes checking a proposed fix quicker, and the proof that it answers exactly as before.</summary>
public class CheckSpeedTests : IDisposable
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
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, body.ReplaceLineEndings("\n"));
        return path;
    }

    [Fact]
    public void TheEnvironmentIsReadFromSetOutput()
    {
        var environment = Toolchains.ReadEnvironment(
            "=C:=C:\\work\r\nINCLUDE=C:\\VC\\include;C:\\Kits\\ucrt\r\nLIB=C:\\VC\\lib\r\nPath=C:\\VC\\bin;C:\\Windows\r\nODD=a=b\r\n");

        Assert.NotNull(environment);
        Assert.Equal("C:\\VC\\bin;C:\\Windows", environment["PATH"]);
        Assert.Equal("a=b", environment["ODD"]);
        Assert.DoesNotContain(environment.Keys, key => key.StartsWith('='));
    }

    [Fact]
    public void OutputWithoutACompilerEnvironmentIsNotOne() =>
        Assert.Null(Toolchains.ReadEnvironment("Path=C:\\Windows\r\nTEMP=C:\\Temp\r\n"));

    [Fact]
    public async Task MsvcReportsTheSameWithItsEnvironmentSetUpOnce()
    {
        if (Toolchains.FindMsvc() is null || Toolchains.MsvcEnvironment() is null) return;

        var source = Write("broken.c", "#include <stdio.h>\nint main(void) {\n    int x = 1\n    printf(\"%d\", y);\n    return 0;\n}\n");

        using var msvcOnly = Toolchains.WithoutGnu();

        var reports = new List<(int? Exit, List<string> Errors, string Program)>();

        foreach (var reuse in new[] { true, false })
        {
            var folder = Path.Combine(_temp.Path, reuse ? "captured" : "script");
            Directory.CreateDirectory(folder);

            var copy = Path.Combine(folder, "broken.c");
            File.Copy(source, copy);

            var spec = CompileCheck.Native(copy, _temp.Path, folder, reuse)!;
            var registry = new ParserRegistry();
            var run = await new TargetRunner(registry).RunAsync(spec, CancellationToken.None);

            reports.Add((run.ExitCode, CompileCheck.ErrorsIn(registry, run.Lines).Select(LocalFixEngine.KeyOf).ToList(), spec.ExecutablePath));
        }

        Assert.EndsWith("cl.exe", reports[0].Program, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("cmd.exe", reports[1].Program);
        Assert.NotEmpty(reports[1].Errors);
        Assert.Equal(reports[1].Exit, reports[0].Exit);
        Assert.Equal(reports[1].Errors, reports[0].Errors);
    }

    private static TargetSpec Spec(string program, string arguments, string folder) => new()
    {
        ExecutablePath = program,
        Arguments = arguments,
        WorkingDirectory = folder,
    };

    private static byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    [Fact]
    public void TheSameCheckInAnotherFolderHasTheSameKey()
    {
        var original = Write("src/app.py", "x = 1\n");

        var first = CompileCheck.KeyFor(Spec("python.exe", "-m py_compile \"C:\\temp\\aaa\\app.py\"", "C:\\temp\\aaa"), "C:\\temp\\aaa", original, Bytes("x = 2\n"));
        var second = CompileCheck.KeyFor(Spec("python.exe", "-m py_compile \"C:\\temp\\bbb\\app.py\"", "C:\\temp\\bbb"), "C:\\temp\\bbb", original, Bytes("x = 2\n"));
        var different = CompileCheck.KeyFor(Spec("python.exe", "-m py_compile \"C:\\temp\\ccc\\app.py\"", "C:\\temp\\ccc"), "C:\\temp\\ccc", original, Bytes("x = 3\n"));

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void ChangingAHeaderBesideACFileChangesItsKey()
    {
        var original = Write("c/app.c", "#include \"util.h\"\nint main(void) { return twice(1); }\n");
        var header = Write("c/util.h", "int twice(int x);\n");
        var spec = Spec("gcc.exe", "-o check.exe app.c", "C:\\temp\\aaa");

        var before = CompileCheck.KeyFor(spec, "C:\\temp\\aaa", original, File.ReadAllBytes(original));

        File.WriteAllText(header, "int twice(int x, int y);\n");
        File.SetLastWriteTimeUtc(header, DateTime.UtcNow.AddMinutes(1));

        var after = CompileCheck.KeyFor(spec, "C:\\temp\\aaa", original, File.ReadAllBytes(original));

        Assert.NotNull(before);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void AnEnvironmentIsPartOfTheKey()
    {
        var original = Write("c2/app.c", "int main(void) { return 0; }\n");
        var plain = Spec("cl.exe", "app.c", "C:\\temp\\aaa");
        var set = new TargetSpec
        {
            ExecutablePath = "cl.exe", Arguments = "app.c", WorkingDirectory = "C:\\temp\\aaa",
            ExtraEnvironment = new Dictionary<string, string> { ["INCLUDE"] = "C:\\other" },
        };

        Assert.NotEqual(
            CompileCheck.KeyFor(plain, "C:\\temp\\aaa", original, File.ReadAllBytes(original)),
            CompileCheck.KeyFor(set, "C:\\temp\\aaa", original, File.ReadAllBytes(original)));
    }

    [Theory]
    [InlineData("cs/app.cs", "#:package Humanizer@2.14.1\nConsole.WriteLine(1);\n")]
    [InlineData("parent/app.c", "#include \"../shared.h\"\nint main(void) { return 0; }\n")]
    public void ACheckThatReadsFilesItCannotListIsNeverRemembered(string name, string body)
    {
        var original = Write(name, body);

        Assert.Null(CompileCheck.KeyFor(Spec("tool.exe", "x", "C:\\temp\\aaa"), "C:\\temp\\aaa", original, Bytes(body)));
    }

    private static ParsedError Error(string? file) => new()
    {
        LanguageId = "gcc",
        Confidence = 90,
        RawText = "error",
        FirstLineSequence = 0,
        ExceptionType = file is null ? "link error" : "compile error",
        Message = "something",
        Frames = file is null ? [] : [new ErrorFrame { Order = 0, File = file, Line = 3, RawLine = "" }],
    };

    [Fact]
    public void OnlyWhatTheCompilerSaidAboutTheFileIsRemembered()
    {
        const string copy = "C:\\temp\\aaa\\app.c";

        Assert.True(CompileCheck.WorthRemembering(new CheckResult(true, 0, [], []), copy));
        Assert.True(CompileCheck.WorthRemembering(new CheckResult(true, 1, [], [Error(copy)]), copy));

        Assert.False(CompileCheck.WorthRemembering(CheckResult.NotRun, copy));
        Assert.False(CompileCheck.WorthRemembering(new CheckResult(true, 1, [], []), copy));
        Assert.False(CompileCheck.WorthRemembering(new CheckResult(true, 1, [], [Error(null)]), copy));
        Assert.False(CompileCheck.WorthRemembering(new CheckResult(true, 1, [], [Error(copy), Error("C:\\elsewhere\\util.h")]), copy));
    }

    [Fact]
    public async Task TheSameCheckIsAnsweredFromMemoryTheSecondTime()
    {
        if (TargetFactory.FindOnPath("python") is null && TargetFactory.FindOnPath("py") is null) return;

        var file = Write("again/again.py", "print('hello')\n");
        var source = SourceFile.Read(file)!;
        IReadOnlyList<string> lines = [$"print('{Guid.NewGuid():N}')"];

        var first = await CompileCheck.RunAsync(source, lines, null, CancellationToken.None);
        var second = await CompileCheck.RunAsync(source, lines, null, CancellationToken.None);

        Assert.True(first.Clean);
        Assert.Same(first, second);
    }
}
