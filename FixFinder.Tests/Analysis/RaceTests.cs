using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// Races found from happens-before and locksets: a value read before join() says the threads changing it have finished,
/// and data kept under a lock in one place but not another. Each is checked beside its repair, which must draw nothing,
/// and beside the look-alikes that are not races: a join done by a helper, a loop that waits by checking on the thread.
/// </summary>
public class RaceTests : IDisposable
{
    private static readonly string[] RaceChecks = [Races.ReadBeforeJoinRule, Races.DataRaceRule];

    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Java(string code)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var path = Path.Combine(_temp.Path, code.Split("public class ")[1].Split(' ', '{')[0] + ".java");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return RacesIn(AbstractChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>> CSharp(string code)
    {
        var path = Path.Combine(_temp.Path, "Program.cs");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return RacesIn(AbstractChecks.Run(await CSharpFrontend.ReadAsync([path]), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Python(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, "threads.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return RacesIn(AbstractChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText()));
    }

    private static List<AnalysisFinding> RacesIn(IEnumerable<AnalysisFinding> findings) =>
        findings.Where(f => RaceChecks.Contains(f.CheckId)).ToList();

    private static string Summary(IEnumerable<AnalysisFinding> findings) =>
        string.Join("; ", findings.Select(f => $"{f.CheckId} line {f.Span.Line}: {f.Message}"));

    private static void Says(string expected, AnalysisFinding finding) =>
        Assert.True(finding.Message.Contains(expected, StringComparison.Ordinal), $"expected \"{expected}\" in: {finding.Message}");

    private const string PrintedBeforeJoin = """
        public class Tally {
            static int count = 0;

            public static void main(String[] args) throws InterruptedException {
                Thread counter = new Thread(() -> {
                    for (int i = 0; i < 1000; i++) {
                        synchronized (Tally.class) {
                            count++;
                        }
                    }
                });
                counter.start();
                System.out.println(count);
                counter.join();
            }
        }
        """;

    /// <summary>Printed between start() and join(): nothing makes the print wait for the counting to finish.</summary>
    [Fact]
    public async Task AValueReadBeforeJoinMayNotBeFinished()
    {
        if (await Java(PrintedBeforeJoin) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(Races.ReadBeforeJoinRule, found.CheckId);
        Assert.Equal(13, found.Span.Line);
        Says("`count` is read here while the thread started on line 12 may still be changing it at line 8", found);
        Says("finished after `counter.join()` on line 14", found);
    }

    [Fact]
    public async Task AValueReadAfterJoinIsFinished()
    {
        // A fresh Windows checkout can give the raw string CRLF line endings, which a pattern spanning two lines would miss.
        var original = PrintedBeforeJoin.ReplaceLineEndings("\n");
        var repaired = original
            .Replace("System.out.println(count);\n", "", StringComparison.Ordinal)
            .Replace("counter.join();", "counter.join();\n        System.out.println(count);", StringComparison.Ordinal);
        Assert.NotEqual(original, repaired);

        if (await Java(repaired) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>The read is in a method called before join(), so it is only found by following the call into it.</summary>
    [Fact]
    public async Task AGetterCalledBeforeJoinReadsWhatTheThreadsAreChanging()
    {
        const string code = """
            public class Counter implements Runnable {
                private int count = 0;

                public synchronized void run() {
                    for (int i = 0; i < 1000; i++) {
                        count++;
                    }
                }

                public synchronized int getCount() {
                    return count;
                }

                public static void main(String[] args) throws InterruptedException {
                    Counter counter = new Counter();
                    Thread first = new Thread(counter);
                    Thread second = new Thread(counter);
                    first.start();
                    second.start();
                    System.out.println(counter.getCount());
                    first.join();
                    second.join();
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(20, found.Span.Line);
        Says("`counter.getCount()` reads `count` (line 11)", found);
    }

    /// <summary>Each thread holds a lock, but not the same one, so nothing stops both adding at once.</summary>
    [Fact]
    public async Task TwoThreadsChangingOneValueUnderDifferentLocksRace()
    {
        const string code = """
            public class Split {
                private static final Object left = new Object();
                private static final Object right = new Object();
                private static int total = 0;

                public static void main(String[] args) throws InterruptedException {
                    Thread adder = new Thread(() -> {
                        synchronized (left) {
                            total += 1;
                        }
                    });
                    Thread doubler = new Thread(() -> {
                        synchronized (right) {
                            total += 2;
                        }
                    });
                    adder.start();
                    doubler.start();
                    adder.join();
                    doubler.join();
                    System.out.println(total);
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(Races.DataRaceRule, found.CheckId);
        Assert.Equal(14, found.Span.Line);
        Says("`total` is changed here holding `right`, but line 9 changes it while holding `left` on another thread - different locks do not keep each other out", found);

        var repaired = await Java(code.Replace("synchronized (right)", "synchronized (left)", StringComparison.Ordinal));
        Assert.True(repaired is null || repaired.Count == 0, Summary(repaired ?? []));
    }

    private const string Account = """
        public class Account {
            private int balance = 0;

            public synchronized void deposit(int amount) {
                balance += amount;
            }

            public int getBalance() {
                return balance;
            }

            public static void main(String[] args) throws InterruptedException {
                Account account = new Account();
                Thread saver = new Thread(() -> account.deposit(10));
                Thread checker = new Thread(() -> System.out.println(account.getBalance()));
                saver.start();
                checker.start();
                saver.join();
                checker.join();
            }
        }
        """;

    /// <summary>deposit() holds the account's lock; getBalance(), called on another thread, reads the balance without it.</summary>
    [Fact]
    public async Task ReadingWithoutTheLockTheWriterHoldsIsARace()
    {
        if (await Java(Account) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(9, found.Span.Line);
        Says("`balance` is read here with no lock held, but line 5 changes it while holding `this` on another thread", found);
        Says("this read can see an out-of-date value", found);
    }

    [Fact]
    public async Task ReadingUnderTheSameLockIsNotARace()
    {
        if (await Java(Account.Replace("public int getBalance()", "public synchronized int getBalance()", StringComparison.Ordinal)) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    private const string PythonTotal = """
        import threading

        total = 0
        lock = threading.Lock()

        def add():
            global total
            for _ in range(1000):
                with lock:
                    total += 1

        worker = threading.Thread(target=add)
        worker.start()
        print(total)
        worker.join()
        """;

    [Fact]
    public async Task APythonTotalPrintedBeforeJoinMayNotBeFinished()
    {
        if (await Python(PythonTotal) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(14, found.Span.Line);
        Says("`total` is read here while the thread started on line 13 may still be changing it at line 10", found);
    }

    [Fact]
    public async Task APythonTotalPrintedAfterJoinIsFinished()
    {
        var repaired = PythonTotal.ReplaceLineEndings("\n").Replace("print(total)\nworker.join()", "worker.join()\nprint(total)", StringComparison.Ordinal);
        Assert.NotEqual(PythonTotal.ReplaceLineEndings("\n"), repaired);

        if (await Python(repaired) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>A helper waits for the threads, so this code cannot tell when they finish - and says nothing it cannot back.</summary>
    [Fact]
    public async Task ThreadsJoinedByAHelperAreNotReportedAsUnfinished()
    {
        const string code = """
            import threading

            total = 0
            lock = threading.Lock()

            def add():
                global total
                with lock:
                    total += 1

            def wait_for(threads):
                for thread in threads:
                    thread.join()

            threads = [threading.Thread(target=add) for _ in range(2)]
            for thread in threads:
                thread.start()
            wait_for(threads)
            print(total)
            """;
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>A loop checking on the thread while it runs is watching progress on purpose.</summary>
    [Fact]
    public async Task ALoopThatWaitsByCheckingOnTheThreadIsLeftAlone()
    {
        var polling = PythonTotal.ReplaceLineEndings("\n").Replace("print(total)\n", "while worker.is_alive():\n    print(\"so far\", total)\n", StringComparison.Ordinal);
        Assert.NotEqual(PythonTotal.ReplaceLineEndings("\n"), polling);

        if (await Python(polling) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>One thread adds under the lock and the other adds without it - so the lock protects nothing.</summary>
    [Fact]
    public async Task APythonUpdateWithoutTheLockOthersHoldRaces()
    {
        const string code = """
            import threading

            total = 0
            lock = threading.Lock()

            def careful():
                global total
                for _ in range(1000):
                    with lock:
                        total += 1

            def careless():
                global total
                for _ in range(1000):
                    total += 1

            threads = [threading.Thread(target=careful), threading.Thread(target=careless)]
            for thread in threads:
                thread.start()
            for thread in threads:
                thread.join()
            print(total)
            """;
        if (await Python(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(Races.DataRaceRule, found.CheckId);
        Assert.Equal(15, found.Span.Line);
        Says("`total` is changed here with no lock held, but line 10 changes it while holding `lock` on another thread", found);
        Says("one of them can be lost", found);

        var repaired = await Python(code.ReplaceLineEndings("\n").Replace("        total += 1\n\nthreads", "        with lock:\n            total += 1\n\nthreads", StringComparison.Ordinal));
        Assert.True(repaired is null || repaired.Count == 0, Summary(repaired ?? []));
    }

    /// <summary>
    /// A latch orders the thread's write before main's read as surely as join() would. Synchronisers like it are not
    /// followed, so where one is used, no race is claimed - silence rather than a guess.
    /// </summary>
    [Fact]
    public async Task AReadAfterWaitingOnALatchIsNotClaimedAsARace()
    {
        const string code = """
            import java.util.concurrent.CountDownLatch;

            public class Ready {
                static int result = 0;

                public static void main(String[] args) throws InterruptedException {
                    CountDownLatch done = new CountDownLatch(1);
                    Thread worker = new Thread(() -> {
                        result = 42;
                        done.countDown();
                    });
                    worker.start();
                    done.await();
                    System.out.println(result);
                    worker.join();
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task AReadAfterWaitingOnAPythonEventIsNotClaimedAsARace()
    {
        const string code = """
            import threading

            result = 0
            ready = threading.Event()

            def work():
                global result
                result = 42
                ready.set()

            worker = threading.Thread(target=work)
            worker.start()
            ready.wait()
            print(result)
            worker.join()
            """;
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    private const string CSharpTask = """
        using System;
        using System.Threading.Tasks;

        class Program
        {
            static readonly object gate = new();

            static void Main()
            {
                int total = 0;
                var task = Task.Run(() =>
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        lock (gate) { total++; }
                    }
                });
                Console.WriteLine(total);
                task.Wait();
            }
        }
        """;

    [Fact]
    public async Task ACSharpTotalReadBeforeWaitMayNotBeFinished()
    {
        var findings = await CSharp(CSharpTask);

        var found = Assert.Single(findings);
        Assert.Equal(18, found.Span.Line);
        Says("`total` is read here while the thread started on line 11 may still be changing it at line 15", found);
        Says("after `task.Wait()` on line 19", found);

        var repaired = await CSharp(CSharpTask.ReplaceLineEndings("\n").Replace("Console.WriteLine(total);\n        task.Wait();", "task.Wait();\n        Console.WriteLine(total);",
            StringComparison.Ordinal));
        Assert.True(repaired.Count == 0, Summary(repaired));
    }
}
