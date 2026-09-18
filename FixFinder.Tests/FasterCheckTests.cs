using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// The rule every quicker check lives by: compared first, trusted after agreeing, set aside after one disagreement.
/// </summary>
/// <remarks>
/// Stand-ins for both ways of running a check, so what is pinned down is only the rule: which answer is given,
/// when the quicker way is believed, and when it is not even asked.
/// </remarks>
public class FasterCheckTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static CheckResult Result(int exit, params string[] lines) => new(
        true, exit,
        lines.Select((text, i) => new CapturedLine(i, StreamKind.StdErr, text, TimeSpan.Zero)).ToList(),
        []);

    private sealed class Ways(CheckResult usual, CheckResult? faster)
    {
        public int UsualRuns;
        public int FasterRuns;

        public Task<CheckResult> Usual(CancellationToken _)
        {
            Interlocked.Increment(ref UsualRuns);
            return Task.FromResult(usual);
        }

        public Task<CheckResult?> Faster(string copy, string folder, CancellationToken _)
        {
            Interlocked.Increment(ref FasterRuns);
            Assert.True(File.Exists(copy));
            return Task.FromResult(faster);
        }
    }

    private async Task<CheckResult> Run(FasterCheck.Trust trust, Ways ways)
    {
        var folder = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, "App.java");
        await File.WriteAllTextAsync(copy, "class App { }\n");

        return await FasterCheck.RunAsync("test", trust, ways.Usual, ways.Faster, copy, folder, everyLine: true, CancellationToken.None);
    }

    [Fact]
    public async Task WhileBeingComparedTheUsualAnswerIsGivenAndBothRun()
    {
        var trust = new FasterCheck.Trust();
        var usual = Result(1, "App.java:1: error: ';' expected");
        var ways = new Ways(usual, Result(1, "App.java:1: error: ';' expected"));

        var answer = await Run(trust, ways);

        Assert.Same(usual, answer);
        Assert.Equal(1, ways.UsualRuns);
        Assert.Equal(1, ways.FasterRuns);
        Assert.False(trust.Trusted);
    }

    [Fact]
    public async Task AfterAgreeingTwiceOnlyTheQuickerWayRuns()
    {
        var trust = new FasterCheck.Trust();
        var faster = Result(0);
        var ways = new Ways(Result(0), faster);

        await Run(trust, ways);
        await Run(trust, ways);
        Assert.True(trust.Trusted);

        var answer = await Run(trust, ways);

        Assert.Same(faster, answer);
        Assert.Equal(2, ways.UsualRuns);
        Assert.Equal(3, ways.FasterRuns);
    }

    [Fact]
    public async Task OneDisagreementSetsTheQuickerWayAside()
    {
        var trust = new FasterCheck.Trust();
        var ways = new Ways(Result(1, "App.java:1: error: ';' expected"), Result(0));

        await Run(trust, ways);
        await Run(trust, ways);

        Assert.True(trust.Refused);
        Assert.Equal(2, ways.UsualRuns);
        Assert.Equal(1, ways.FasterRuns);
    }

    [Fact]
    public async Task ATrustedQuickerWayThatCannotAnswerLeavesItToTheUsualWay()
    {
        var trust = new FasterCheck.Trust();
        trust.Record(true);
        trust.Record(true);

        var usual = Result(0);
        var answer = await Run(trust, new Ways(usual, faster: null));

        Assert.Same(usual, answer);
    }

    [Fact]
    public async Task OutputWithAnythingOutsideAsciiIsAlwaysCheckedTheUsualWay()
    {
        var trust = new FasterCheck.Trust();
        trust.Record(true);
        trust.Record(true);

        var usual = Result(1, "App.java:3: error: cannot find symbol café");
        var ways = new Ways(usual, Result(1, "App.java:3: error: cannot find symbol café"));

        var answer = await Run(trust, ways);

        Assert.Same(usual, answer);
        Assert.False(trust.Refused);
    }

    [Fact]
    public void FoldersAreTakenOutBeforeOutputIsCompared()
    {
        var usual = Result(1, "C:\\one\\App.java:3: error: cannot find symbol");
        var faster = Result(1, "C:\\two\\App.java:3: error: cannot find symbol");

        Assert.True(FasterCheck.Agree(usual, "C:\\one", faster, "C:\\two", everyLine: true, out _));
        Assert.False(FasterCheck.Agree(usual, "C:\\one", Result(1, "C:\\two\\App.java:4: error: cannot find symbol"), "C:\\two", everyLine: true, out var difference));
        Assert.Contains("output differs", difference);
    }

    // ------------------------------------------------------------------ javac kept running

    [Fact]
    public void JavacOutputIsSplitIntoLinesTheWayAProcessIs()
    {
        var lines = JavaCompileServer.Lines("one\r\ntwo\nthree\rfour\n"u8.ToArray(), System.Text.Encoding.UTF8);

        Assert.Equal(["one", "two", "three", "four"], lines.Select(l => l.Text));
        Assert.All(lines, l => Assert.Equal(StreamKind.StdErr, l.Stream));
    }

    [Fact]
    public void AJdkWithNoJavaBesideJavacIsNotRunThatWay() =>
        Assert.Null(JavaCompileServer.For(Path.Combine(_temp.Path, "bin", "javac.exe")));

    public static TheoryData<string, string, string> JavaPrograms => new()
    {
        { "semicolon", "';' expected", "public class App {\n    public static void main(String[] args) {\n        int x = 1\n    }\n}\n" },
        { "symbol", "cannot find symbol", "public class App {\n    public static void main(String[] args) {\n        java.util.List<String> names = new java.util.ArrayList<>();\n        System.out.println(nmes);\n    }\n}\n" },
        { "neighbour", "incompatible types", "public class App {\n    public static void main(String[] args) {\n        Helper.go(1);\n    }\n}\n" },
        { "notes", "[unchecked] unchecked call", "import java.util.*;\npublic class App {\n    public static void main(String[] args) {\n        List raw = new ArrayList();\n        raw.add(\"x\");\n        List<String> typed = raw;\n    }\n}\n" },
        { "file-name", "should be declared in a file named", "public class Other {\n}\n" },
        { "many", "120 errors", "public class App {\n    void f() {\n" + string.Concat(Enumerable.Range(0, 120).Select(i => $"        int v{i} = undefined{i};\n")) + "    }\n}\n" },
        { "clean", "clean", "public class App {\n    public static void main(String[] args) {\n        System.out.println(\"ok\");\n    }\n}\n" },
    };

    [Theory]
    [MemberData(nameof(JavaPrograms))]
    public async Task RunningJavacSaysWhatJavacSays(string name, string expected, string program)
    {
        if (Toolchains.FindJavac() is not { } javac || JavaCompileServer.For(javac.Program) is not { } server) return;

        var original = Path.Combine(_temp.Path, name + "-source");
        Directory.CreateDirectory(original);
        File.WriteAllText(Path.Combine(original, "App.java"), program);
        File.WriteAllText(Path.Combine(original, "Helper.java"), "public class Helper {\n    static void go(String s) { }\n}\n");

        async Task<(CheckResult Result, string Folder)> Process()
        {
            var folder = Path.Combine(_temp.Path, name + "-process");
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, "App.java");
            File.Copy(Path.Combine(original, "App.java"), copy);

            var spec = new TargetSpec
            {
                ExecutablePath = javac.Program,
                Arguments = string.Join(" ", CompileCheck.JavacArguments(copy, original, folder).Select(a => a.StartsWith('-') ? a : $"\"{a}\"")),
                WorkingDirectory = folder,
                Timeout = TimeSpan.FromMinutes(2),
            };

            var registry = new ParserRegistry();
            var run = await new TargetRunner(registry).RunAsync(spec, CancellationToken.None);

            return (new CheckResult(true, run.ExitCode, run.Lines, CompileCheck.ErrorsIn(registry, run.Lines)), folder);
        }

        async Task<(CheckResult? Result, string Folder)> Running()
        {
            var folder = Path.Combine(_temp.Path, name + "-running");
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, "App.java");
            File.Copy(Path.Combine(original, "App.java"), copy);

            if (await server.CompileAsync(CompileCheck.JavacArguments(copy, original, folder), TimeSpan.FromMinutes(2), CancellationToken.None) is not { } reply)
                return (null, folder);

            var lines = JavaCompileServer.Lines(reply.Output, System.Text.Encoding.UTF8);
            return (new CheckResult(true, reply.ExitCode, lines, CompileCheck.ErrorsIn(new ParserRegistry(), lines)), folder);
        }

        var process = await Process();
        var running = await Running();

        Assert.True(process.Result.Ran);
        Assert.NotNull(running.Result);
        Assert.True(FasterCheck.Agree(process.Result, process.Folder, running.Result, running.Folder, everyLine: true, out var difference), difference);

        if (expected == "clean") Assert.True(process.Result.Clean);
        else Assert.Contains(process.Result.Lines, line => line.Text.Contains(expected, StringComparison.Ordinal));
    }
}
