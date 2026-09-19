using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>Threads and the memory model: lost updates, stale reads, lock order, wait and notify, and run() for start().</summary>
public class ConcurrencyTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Java(string code)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var path = Path.Combine(_temp.Path, Name(code) + ".java");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return AbstractChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText());
    }

    private static string Name(string code) => code.Split("public class ")[1].Split(' ', '{')[0];

    private async Task<IReadOnlyList<AnalysisFinding>> CSharp(string code)
    {
        var path = Path.Combine(_temp.Path, "Sample.cs");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return AbstractChecks.Run(await CSharpFrontend.ReadAsync([path]), new SourceText());
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Python(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, "sample.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return AbstractChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText());
    }

    private static string Summary(IEnumerable<AnalysisFinding> findings) =>
        string.Join("; ", findings.Select(f => $"{f.CheckId} line {f.Span.Line}: {f.Message}"));

    private const string SharedCounter = """
        public class Counter implements Runnable {
            private int count;
            public void run() {
                for (int i = 0; i < 1000; i++) {
                    count++;
                }
            }
            public static void main(String[] args) {
                Counter counter = new Counter();
                new Thread(counter).start();
                new Thread(counter).start();
            }
        }
        """;

    [Fact]
    public async Task ARunnableSharedByTwoThreadsLosesUpdates()
    {
        if (await Java(SharedCounter) is not { } findings) return;

        var lost = Assert.Single(findings, f => f.CheckId == "analysis-lost-update");
        Assert.Equal(5, lost.Span.Line);
        Assert.Equal(Concurrency.FoundBy, lost.FoundBy);
    }

    [Theory]
    [InlineData("synchronized (this) {\n                        count++;\n                    }")]
    [InlineData("increment();")]
    public async Task AnUpdateUnderALockIsLeftAlone(string update)
    {
        var code = SharedCounter.Replace("count++;", update).Replace("    public static void main", "    synchronized void increment() { count++; }\n    public static void main");
        if (await Java(code) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-lost-update");
    }

    [Fact]
    public async Task EachThreadWithItsOwnRunnableSharesOnlyStaticFields()
    {
        var code = SharedCounter
            .Replace("Counter counter = new Counter();\n        new Thread(counter).start();\n        new Thread(counter).start();",
                "for (int t = 0; t < 2; t++) {\n            new Thread(new Counter()).start();\n        }");
        if (await Java(code) is not { } own) return;
        Assert.DoesNotContain(own, f => f.CheckId == "analysis-lost-update");

        var shared = await Java(code.Replace("private int count;", "private static int count;"));
        Assert.Single(shared!, f => f.CheckId == "analysis-lost-update");
    }

    [Fact]
    public async Task AFlagAThreadPollsMustBeVolatile()
    {
        const string code = """
            public class Worker implements Runnable {
                private boolean running = true;
                public void run() {
                    int n = 0;
                    while (running) {
                        n++;
                    }
                }
                public void stop() {
                    running = false;
                }
                public static void main(String[] args) {
                    new Thread(new Worker()).start();
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        var stale = Assert.Single(findings, f => f.CheckId == "analysis-stale-read");
        Assert.Equal(5, stale.Span.Line);

        Assert.DoesNotContain(await Java(code.Replace("private boolean", "private volatile boolean")) ?? [], f => f.CheckId == "analysis-stale-read");
    }

    [Fact]
    public async Task LocksTakenInOppositeOrdersCanDeadlock()
    {
        const string code = """
            public class Bank {
                private final Object a = new Object();
                private final Object b = new Object();
                void one() {
                    synchronized (a) {
                        synchronized (b) {
                            System.out.println("one");
                        }
                    }
                }
                void two() {
                    synchronized (b) {
                        synchronized (a) {
                            System.out.println("two");
                        }
                    }
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        var orders = findings.Where(f => f.CheckId == "analysis-lock-order").ToList();
        Assert.Equal([6, 13], orders.Select(f => f.Span.Line));
    }

    [Theory]
    [InlineData("void take() throws InterruptedException {\n        while (!full) wait();\n    }", "analysis-wait-without-lock", 4)]
    [InlineData("synchronized void take() throws InterruptedException {\n        if (!full) wait();\n    }", "analysis-wait-not-in-loop", 4)]
    public async Task WaitNeedsItsLockAndALoop(string method, string check, int line)
    {
        if (await Java($"public class Box {{\n    private boolean full;\n    {method}\n}}\n") is not { } findings) return;

        var found = Assert.Single(findings, f => f.CheckId == check);
        Assert.Equal(line, found.Span.Line);
    }

    [Fact]
    public async Task WaitWithItsLockInALoopIsLeftAlone()
    {
        if (await Java("public class Box {\n    private boolean full;\n    synchronized void take() throws InterruptedException {\n        while (!full) wait();\n    }\n}\n") is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task AHelperOnlyCalledUnderTheLockHoldsItToo()
    {
        const string code = "public class Pipe {\n    private int in, out;\n    synchronized void receive() throws InterruptedException {\n        awaitSpace();\n    }\n" +
                            "    private void awaitSpace() throws InterruptedException {\n        while (in == out) {\n            notifyAll();\n            wait(1000);\n        }\n    }\n}\n";
        if (await Java(code) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-wait-without-lock");
    }

    [Fact]
    public async Task RunOnAThreadRunsItHere()
    {
        const string code = "public class Start {\n    static void go() {\n        Thread t = new Thread(() -> System.out.println(\"hi\"));\n        t.run();\n    }\n}\n";
        if (await Java(code) is not { } findings) return;

        Assert.Equal(4, Assert.Single(findings, f => f.CheckId == "analysis-run-not-start").Span.Line);
    }

    [Theory]
    [InlineData("total += values[i];", true)]
    [InlineData("lock (gate) { total += values[i]; }", false)]
    [InlineData("Interlocked.Add(ref total, values[i]);", false)]
    public async Task AParallelLoopBodyThatAddsToACapturedTotalLosesUpdates(string body, bool lost)
    {
        var code = "using System.Threading;\nusing System.Threading.Tasks;\nclass App\n{\n    static readonly object gate = new();\n    static int Sum(int[] values)\n    {\n        int total = 0;\n" +
                   $"        Parallel.For(0, values.Length, i => {{ {body} }});\n        return total;\n    }}\n}}\n";
        var findings = await CSharp(code);

        Assert.Equal(lost, findings.Any(f => f.CheckId == "analysis-lost-update"));
    }

    [Theory]
    [InlineData("    counter += 1\n", true)]
    [InlineData("    with lock:\n        counter += 1\n", false)]
    public async Task PythonThreadsThatAddToAGlobalLoseUpdates(string update, bool lost)
    {
        var code = "import threading\n\ncounter = 0\nlock = threading.Lock()\n\ndef work():\n    global counter\n" + update +
                   "\nfor _ in range(4):\n    t = threading.Thread(target=work)\n    t.start()\n";
        if (await Python(code) is not { } findings) return;

        Assert.Equal(lost, findings.Any(f => f.CheckId == "analysis-lost-update"));
    }

    [Fact]
    public async Task APythonThreadRunInsteadOfStartedIsFound()
    {
        if (await Python("import threading\n\ndef work():\n    print('working')\n\nt = threading.Thread(target=work)\nt.run()\n") is not { } findings) return;

        Assert.Equal(7, Assert.Single(findings, f => f.CheckId == "analysis-run-not-start").Span.Line);
    }
}
