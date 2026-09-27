using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// Deadlock as cycles in the lock-order graph: two locks or four, taken in one function or through calls, in Java, C#
/// and Python. Every deadlock is checked beside its repair - the same program with the locks taken in one agreed order -
/// which must draw nothing, and beside the look-alikes that cannot deadlock: a lock around both places, the main thread
/// on its own, a lock the thread already holds.
/// </summary>
public class LockOrderTests : IDisposable
{
    private static readonly string[] DeadlockChecks = ["analysis-lock-order", "analysis-lock-cycle", "analysis-lock-reacquired"];

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
        return Deadlocks(AbstractChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>> CSharp(string code)
    {
        var path = Path.Combine(_temp.Path, "Sample.cs");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Deadlocks(AbstractChecks.Run(await CSharpFrontend.ReadAsync([path]), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Python(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, "sample.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Deadlocks(AbstractChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText()));
    }

    private static List<AnalysisFinding> Deadlocks(IEnumerable<AnalysisFinding> findings) =>
        findings.Where(f => DeadlockChecks.Contains(f.CheckId)).ToList();

    private static string Summary(IEnumerable<AnalysisFinding> findings) =>
        string.Join("; ", findings.Select(f => $"{f.CheckId} line {f.Span.Line}: {f.Message}"));

    /// <summary>The whole message on failure - Assert.Contains cuts a long one short at the point that matters.</summary>
    private static void Says(string expected, AnalysisFinding finding) =>
        Assert.True(finding.Message.Contains(expected, StringComparison.Ordinal), $"expected \"{expected}\" in: {finding.Message}");

    private const string ThreeLocksInACircle = """
        public class Circle {
            private final Object first = new Object();
            private final Object second = new Object();
            private final Object third = new Object();

            void one() {
                synchronized (first) {
                    synchronized (second) {
                        System.out.println("one");
                    }
                }
            }

            void two() {
                synchronized (second) {
                    synchronized (third) {
                        System.out.println("two");
                    }
                }
            }

            void three() {
                synchronized (third) {
                    synchronized (first) {
                        System.out.println("three");
                    }
                }
            }
        }
        """;

    /// <summary>No two places take the same two locks in opposite orders, so only the graph sees this one.</summary>
    [Fact]
    public async Task ThreeLocksTakenRoundACircleCanDeadlock()
    {
        if (await Java(ThreeLocksInACircle) is not { } findings) return;

        Assert.True(findings.All(f => f.CheckId == "analysis-lock-cycle"), Summary(findings));
        Assert.Equal([8, 16, 24], findings.Select(f => f.Span.Line));

        Says("This takes `second` while holding `first`", findings[0]);
        Says("line 16 takes `third` while holding `second`", findings[0]);
        Says("line 24 takes `first` while holding `third`", findings[0]);
        Says("three threads", findings[0]);
    }

    [Fact]
    public async Task TakingTheLocksInOneAgreedOrderEndsTheCircle()
    {
        // A fresh Windows checkout can give the raw string CRLF line endings, which a pattern spanning two lines would miss.
        var original = ThreeLocksInACircle.ReplaceLineEndings("\n");
        var repaired = original.Replace("synchronized (third) {\n            synchronized (first) {", "synchronized (first) {\n            synchronized (third) {",
            StringComparison.Ordinal);
        Assert.NotEqual(original, repaired);

        if (await Java(repaired) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>Four locks, in C#, with each hand-over in a different method: a cycle is found however long it is.</summary>
    [Fact]
    public async Task FourLocksTakenRoundACircleCanDeadlock()
    {
        const string code = """
            class Stations
            {
                static readonly object north = new();
                static readonly object east = new();
                static readonly object south = new();
                static readonly object west = new();

                static void One() { lock (north) { lock (east) { } } }
                static void Two() { lock (east) { lock (south) { } } }
                static void Three() { lock (south) { lock (west) { } } }
                static void Four() { lock (west) { lock (north) { } } }
            }
            """;

        var findings = await CSharp(code);

        Assert.True(findings.All(f => f.CheckId == "analysis-lock-cycle"), Summary(findings));
        Assert.Equal([8, 9, 10, 11], findings.Select(f => f.Span.Line));
        Says("four threads", findings[0]);

        var repaired = await CSharp(code.Replace("lock (west) { lock (north) { } }", "lock (north) { lock (west) { } }", StringComparison.Ordinal));
        Assert.True(repaired.Count == 0, Summary(repaired));
    }

    /// <summary>A lock taken around both places lets only one thread in at a time, so the inner locks never meet in opposite hands.</summary>
    [Fact]
    public async Task ALockAroundBothPlacesPreventsTheDeadlock()
    {
        const string code = """
            class Gated
            {
                static readonly object gate = new();
                static readonly object a = new();
                static readonly object b = new();

                static void Forwards() { lock (gate) { lock (a) { lock (b) { } } } }
                static void Backwards() { lock (gate) { lock (b) { lock (a) { } } } }
            }
            """;

        var findings = await CSharp(code);

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>A C# using closes a resource and locks nothing, however much using (a) looks like lock (a).</summary>
    [Fact]
    public async Task ResourcesClosedInOppositeOrdersAreNotLocks()
    {
        const string code = """
            using System.IO;
            class Files
            {
                static void Forwards(Stream a, Stream b) { using (a) { using (b) { } } }
                static void Backwards(Stream a, Stream b) { using (b) { using (a) { } } }
            }
            """;

        var findings = await CSharp(code);

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>Java's locks can be taken again by the thread holding them, so taking a lock already held is no new edge.</summary>
    [Fact]
    public async Task TakingALockAlreadyHeldAgainIsNotAnOppositeOrder()
    {
        const string code = """
            public class Again {
                private final Object a = new Object();
                private final Object b = new Object();

                void nested() {
                    synchronized (a) {
                        synchronized (b) {
                            synchronized (a) {
                                System.out.println("still fine");
                            }
                        }
                    }
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>The second lock is taken in a helper, so the order only shows by following the call made while holding the first.</summary>
    [Fact]
    public async Task ALockTakenInsideACalledMethodCountsAsTakenByTheCaller()
    {
        const string code = """
            public class Relay {
                private final Object a = new Object();
                private final Object b = new Object();

                void one() {
                    synchronized (a) {
                        helper();
                    }
                }

                void helper() {
                    synchronized (b) {
                        System.out.println("b");
                    }
                }

                void two() {
                    synchronized (b) {
                        synchronized (a) {
                            System.out.println("a");
                        }
                    }
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        Assert.True(findings.All(f => f.CheckId == "analysis-lock-order"), Summary(findings));
        Assert.Equal([7, 19], findings.Select(f => f.Span.Line));
        Says("This calls `helper()` while holding `a`, and that call takes `b` at line 12, but line 19 takes `a` while holding `b`", findings[0]);
    }

    private const string Transfers = """
        public class Bank {
            static void transfer(Object from, Object to) {
                synchronized (from) {
                    synchronized (to) {
                        System.out.println("moved");
                    }
                }
            }

            public static void main(String[] args) {
                Object alice = new Object();
                Object bob = new Object();
                new Thread(() -> transfer(alice, bob)).start();
                new Thread(() -> transfer(bob, alice)).start();
            }
        }
        """;

    /// <summary>
    /// The lock order comes from the arguments: transfer always takes from, then to, and two threads moving money in
    /// opposite directions pass the same two accounts the other way round.
    /// </summary>
    [Fact]
    public async Task TheSameMethodCalledWithItsLocksSwappedCanDeadlock()
    {
        if (await Java(Transfers) is not { } findings) return;

        Assert.True(findings.All(f => f.CheckId == "analysis-lock-order"), Summary(findings));
        Assert.Equal([13, 14], findings.Select(f => f.Span.Line));
        Says("calls `transfer(alice, bob)`, which takes `bob` while holding `alice` at line 4", findings[0]);
        Says("line 14 calls `transfer(bob, alice)`, which takes `alice` while holding `bob`", findings[0]);
    }

    /// <summary>The main thread making both transfers one after the other can never be in both at once.</summary>
    [Fact]
    public async Task OneThreadMakingBothTransfersInTurnCannotDeadlock()
    {
        var inTurn = Transfers
            .Replace("new Thread(() -> transfer(alice, bob)).start();", "transfer(alice, bob);", StringComparison.Ordinal)
            .Replace("new Thread(() -> transfer(bob, alice)).start();", "transfer(bob, alice);", StringComparison.Ordinal);
        Assert.NotEqual(Transfers, inTurn);

        if (await Java(inTurn) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    private const string Philosophers = """
        import threading

        fork_1 = threading.Lock()
        fork_2 = threading.Lock()
        fork_3 = threading.Lock()

        def philosopher(left, right):
            with left:
                with right:
                    print("eating")

        seats = [
            threading.Thread(target=philosopher, args=(fork_1, fork_2)),
            threading.Thread(target=philosopher, args=(fork_2, fork_3)),
            threading.Thread(target=philosopher, args=(fork_3, fork_1)),
        ]
        for seat in seats:
            seat.start()
        """;

    /// <summary>The dining philosophers: each thread takes its left fork, then its right, and the last one's right is the first one's left.</summary>
    [Fact]
    public async Task PhilosophersEachTakingTheirLeftForkFirstCanDeadlock()
    {
        if (await Python(Philosophers) is not { } findings) return;

        Assert.True(findings.All(f => f.CheckId == "analysis-lock-cycle"), Summary(findings));
        Assert.Equal([13, 14, 15], findings.Select(f => f.Span.Line));
        Says("starts a thread running `philosopher`, which takes `fork_2` while holding `fork_1` at line 9", findings[0]);
    }

    /// <summary>The textbook repair: the last philosopher reaches for the lower-numbered fork first, and the circle is broken.</summary>
    [Fact]
    public async Task APhilosopherTakingTheLowerForkFirstBreaksTheCircle()
    {
        var repaired = Philosophers.Replace("args=(fork_3, fork_1)", "args=(fork_1, fork_3)", StringComparison.Ordinal);
        Assert.NotEqual(Philosophers, repaired);

        if (await Python(repaired) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    private const string Account = """
        import threading

        class Account:
            def __init__(self):
                self.lock = threading.Lock()
                self.balance = 0

            def deposit(self, amount):
                with self.lock:
                    self.balance += amount

            def deposit_twice(self, amount):
                with self.lock:
                    self.deposit(amount)
        """;

    /// <summary>A threading.Lock is not reentrant: the thread holding it waits for ever to take it again, with no second thread needed.</summary>
    [Fact]
    public async Task APythonLockTakenAgainByTheThreadHoldingItDeadlocks()
    {
        if (await Python(Account) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal("analysis-lock-reacquired", found.CheckId);
        Assert.Equal(14, found.Span.Line);
        Assert.Equal(Severity.Error, found.Severity);
        Says("that call takes `self.lock` again at line 9", found);
    }

    [Fact]
    public async Task AnRLockCanBeTakenAgainByTheThreadHoldingIt()
    {
        if (await Python(Account.Replace("threading.Lock()", "threading.RLock()", StringComparison.Ordinal)) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>A helper told the caller already holds the lock does not take it again, so only a lock taken on every run counts.</summary>
    [Fact]
    public async Task ALockTheCalleeOnlySometimesTakesIsNotReportedAsTakenAgain()
    {
        const string code = """
            import threading

            class Account:
                def __init__(self):
                    self.lock = threading.Lock()
                    self.balance = 0

                def deposit(self, amount, locked=False):
                    if locked:
                        self.balance += amount
                    else:
                        with self.lock:
                            self.balance += amount

                def deposit_twice(self, amount):
                    with self.lock:
                        self.deposit(amount, locked=True)
            """;
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>with on a file or a connection is not a lock, and nesting two of them in both orders deadlocks nothing.</summary>
    [Fact]
    public async Task PythonWithOnThingsThatAreNotLocksIsLeftAlone()
    {
        const string code = """
            import sqlite3

            first = sqlite3.connect("first.db")
            second = sqlite3.connect("second.db")

            def forwards():
                with first:
                    with second:
                        pass

            def backwards():
                with second:
                    with first:
                        pass
            """;
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }
}
