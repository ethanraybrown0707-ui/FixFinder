using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Protocols as state machines, followed along every way through a function: a thread is made, then started - once -
/// and only then can it be waited for. Starting it again, or waiting for it before it starts, is found; making a new
/// thread each time round is not.
/// </summary>
public class StateMachineTests(ITestOutputHelper output) : IDisposable
{
    private static readonly string[] ThreadChecks = ["analysis-thread-started-twice", "analysis-join-before-start"];

    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Python(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, "threads.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Threads(AbstractChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Java(string code)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var path = Path.Combine(_temp.Path, code.Split("public class ")[1].Split(' ', '{')[0] + ".java");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Threads(AbstractChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>> CSharp(string code)
    {
        var path = Path.Combine(_temp.Path, "Program.cs");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Threads(AbstractChecks.Run(await CSharpFrontend.ReadAsync([path]), new SourceText()));
    }

    private List<AnalysisFinding> Threads(IEnumerable<AnalysisFinding> findings)
    {
        var found = findings.Where(f => ThreadChecks.Contains(f.CheckId)).ToList();
        foreach (var finding in found) output.WriteLine($"line {finding.Span.Line} {finding.CheckId} [{finding.Confidence}]: {finding.Message}");
        return found;
    }

    private static string Summary(IEnumerable<AnalysisFinding> findings) => string.Join("; ", findings.Select(f => $"line {f.Span.Line}: {f.Message}"));

    private static void Says(string expected, AnalysisFinding finding) =>
        Assert.True(finding.Message.Contains(expected, StringComparison.Ordinal), $"expected \"{expected}\" in: {finding.Message}");

    [Fact]
    public async Task StartingAThreadASecondTimeFails()
    {
        const string code = """
            import threading

            def work():
                print("working")

            worker = threading.Thread(target=work)
            worker.start()
            worker.join()
            worker.start()
            """;
        if (await Python(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(9, found.Span.Line);
        Assert.Equal(Confidence.Certain, found.Confidence);
        Says("it was already started on line 7", found);
        Says("RuntimeError: threads can only be started once", found);
    }

    private const string StartedInALoop = """
        import threading

        def work():
            print("working")

        worker = threading.Thread(target=work)
        for attempt in range(3):
            worker.start()
            worker.join()
        """;

    /// <summary>Made once, before the loop, and started every time round it: the second time round, it is already started.</summary>
    [Fact]
    public async Task AThreadMadeBeforeALoopAndStartedInItCanBeStartedTwice()
    {
        if (await Python(StartedInALoop) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(8, found.Span.Line);
        Assert.Equal(Confidence.Likely, found.Confidence);
        Says("can start `worker` a second time", found);
    }

    [Fact]
    public async Task ANewThreadEachTimeRoundIsFine()
    {
        var repaired = StartedInALoop.ReplaceLineEndings("\n")
            .Replace("worker = threading.Thread(target=work)\nfor attempt in range(3):\n", "for attempt in range(3):\n    worker = threading.Thread(target=work)\n", StringComparison.Ordinal);
        Assert.NotEqual(StartedInALoop.ReplaceLineEndings("\n"), repaired);

        if (await Python(repaired) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task JoiningAThreadBeforeStartingItFails()
    {
        const string code = """
            import threading

            def work():
                print("working")

            worker = threading.Thread(target=work)
            worker.join()
            worker.start()
            """;
        if (await Python(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(7, found.Span.Line);
        Says("RuntimeError: cannot join thread before it is started", found);
    }

    /// <summary>A class of the program's own that is a thread starts and joins like one.</summary>
    [Fact]
    public async Task AThreadOfTheProgramsOwnClassCanOnlyBeStartedOnce()
    {
        const string code = """
            import threading

            class Worker(threading.Thread):
                def run(self):
                    print("working")

            worker = Worker()
            worker.start()
            worker.start()
            """;
        if (await Python(code) is not { } findings) return;

        Assert.Equal(9, Assert.Single(findings).Span.Line);
    }

    [Fact]
    public async Task InJavaStartingTwiceThrowsAndJoiningFirstWaitsForNothing()
    {
        const string code = """
            public class Twice {
                public static void main(String[] args) throws InterruptedException {
                    Thread worker = new Thread(() -> System.out.println("working"));
                    worker.join();
                    worker.start();
                    worker.start();
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        Assert.Equal(2, findings.Count);
        var joined = Assert.Single(findings, f => f.CheckId == "analysis-join-before-start");
        Assert.Equal(4, joined.Span.Line);
        Assert.Equal(Severity.Warning, joined.Severity);
        Says("returns at once without waiting for anything", joined);

        var twice = Assert.Single(findings, f => f.CheckId == "analysis-thread-started-twice");
        Assert.Equal(6, twice.Span.Line);
        Says("IllegalThreadStateException", twice);
    }

    [Fact]
    public async Task InCSharpStartingTwiceThrows()
    {
        const string code = """
            using System;
            using System.Threading;

            class Program
            {
                static void Main()
                {
                    var worker = new Thread(() => Console.WriteLine("working"));
                    worker.Start();
                    worker.Join();
                    worker.Start();
                }
            }
            """;

        var found = Assert.Single(await CSharp(code));
        Assert.Equal(11, found.Span.Line);
        Says("ThreadStateException", found);
    }
}
