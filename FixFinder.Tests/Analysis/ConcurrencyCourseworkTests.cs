using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// The concurrency a software engineering masters teaches, written correctly, and the classic mistakes made with it.
/// </summary>
/// <remarks>
/// A concurrency checker is at its most likely to be wrong on code that is correct but sophisticated: a counter kept
/// safe by an atomic rather than a lock, a lock taken with lock() and released in a finally rather than with the
/// synchronized keyword, a map whose merge is thread-safe by contract. Telling a postgraduate their correct lock-free
/// code has a race is the fastest way to be ignored, so every correct program here must draw no error or warning.
/// <para>
/// The planted mistakes beside them are what keep that honest - an analysis that reported nothing would leave every
/// correct program alone as well.
/// </para>
/// </remarks>
public class ConcurrencyCourseworkTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string> CorrectJava => new()
    {
        {
            "a counter kept safe by an atomic", """
            import java.util.concurrent.atomic.AtomicInteger;

            public class AtomicCounter implements Runnable {
                private final AtomicInteger count = new AtomicInteger();

                public void run() {
                    for (int i = 0; i < 1000; i++) {
                        count.incrementAndGet();
                    }
                }

                public static void main(String[] args) throws InterruptedException {
                    AtomicCounter counter = new AtomicCounter();
                    Thread first = new Thread(counter);
                    Thread second = new Thread(counter);
                    first.start();
                    second.start();
                    first.join();
                    second.join();
                    System.out.println(counter.count.get());
                }
            }
            """
        },
        {
            "a lock released in a finally", """
            import java.util.concurrent.locks.ReentrantLock;

            public class LockedCounter implements Runnable {
                private final ReentrantLock lock = new ReentrantLock();
                private int count = 0;

                public void run() {
                    for (int i = 0; i < 1000; i++) {
                        lock.lock();
                        try {
                            count++;
                        } finally {
                            lock.unlock();
                        }
                    }
                }

                public static void main(String[] args) throws InterruptedException {
                    LockedCounter counter = new LockedCounter();
                    Thread first = new Thread(counter);
                    Thread second = new Thread(counter);
                    first.start();
                    second.start();
                    first.join();
                    second.join();
                    System.out.println(counter.count);
                }
            }
            """
        },
        {
            "a bounded buffer with a monitor", """
            import java.util.LinkedList;

            public class Buffer {
                private final LinkedList<Integer> items = new LinkedList<>();

                public synchronized void put(int value) {
                    items.add(value);
                    notifyAll();
                }

                public synchronized int take() throws InterruptedException {
                    while (items.isEmpty()) {
                        wait();
                    }
                    return items.removeFirst();
                }

                public static void main(String[] args) throws InterruptedException {
                    Buffer buffer = new Buffer();
                    Thread producer = new Thread(() -> {
                        for (int i = 0; i < 5; i++) {
                            buffer.put(i);
                        }
                    });
                    producer.start();
                    for (int i = 0; i < 5; i++) {
                        System.out.println(buffer.take());
                    }
                    producer.join();
                }
            }
            """
        },
        {
            "two locks always taken in the same order", """
            public class Transfer {
                private final Object first = new Object();
                private final Object second = new Object();
                private int from = 100;
                private int to = 0;

                void move() {
                    synchronized (first) {
                        synchronized (second) {
                            from--;
                            to++;
                        }
                    }
                }

                void check() {
                    synchronized (first) {
                        synchronized (second) {
                            System.out.println(from + to);
                        }
                    }
                }

                public static void main(String[] args) throws InterruptedException {
                    Transfer transfer = new Transfer();
                    Thread mover = new Thread(transfer::move);
                    Thread checker = new Thread(transfer::check);
                    mover.start();
                    checker.start();
                    mover.join();
                    checker.join();
                }
            }
            """
        },
        {
            "a volatile flag to stop a thread", """
            public class Worker implements Runnable {
                private volatile boolean running = true;

                public void run() {
                    while (running) {
                        Thread.onSpinWait();
                    }
                }

                public void stop() {
                    running = false;
                }

                public static void main(String[] args) throws InterruptedException {
                    Worker worker = new Worker();
                    Thread thread = new Thread(worker);
                    thread.start();
                    worker.stop();
                    thread.join();
                }
            }
            """
        },
        {
            "a concurrent map merged into", """
            import java.util.concurrent.ConcurrentHashMap;

            public class WordCount implements Runnable {
                private final ConcurrentHashMap<String, Integer> counts = new ConcurrentHashMap<>();

                public void run() {
                    for (String word : new String[] { "a", "b", "a" }) {
                        counts.merge(word, 1, Integer::sum);
                    }
                }

                public static void main(String[] args) throws InterruptedException {
                    WordCount count = new WordCount();
                    Thread first = new Thread(count);
                    Thread second = new Thread(count);
                    first.start();
                    second.start();
                    first.join();
                    second.join();
                    System.out.println(count.counts);
                }
            }
            """
        },
    };

    public static TheoryData<string, string> CorrectPython => new()
    {
        {
            "a shared total updated under a lock", """
            import threading

            total = 0
            lock = threading.Lock()

            def add():
                global total
                for _ in range(1000):
                    with lock:
                        total += 1

            threads = [threading.Thread(target=add) for _ in range(2)]
            for thread in threads:
                thread.start()
            for thread in threads:
                thread.join()
            print(total)
            """
        },
        {
            "a producer and consumer sharing a queue", """
            import queue
            import threading

            work = queue.Queue()

            def producer():
                for item in range(5):
                    work.put(item)
                work.put(None)

            def consumer():
                while True:
                    item = work.get()
                    if item is None:
                        break
                    print(item)

            making = threading.Thread(target=producer)
            using = threading.Thread(target=consumer)
            making.start()
            using.start()
            making.join()
            using.join()
            """
        },
    };

    public static TheoryData<string, string, string> JavaMistakes => new()
    {
        {
            "a shared counter nobody guards", "analysis-lost-update", """
            public class Tally implements Runnable {
                private int count = 0;

                public void run() {
                    for (int i = 0; i < 1000; i++) {
                        count++;
                    }
                }

                public static void main(String[] args) throws InterruptedException {
                    Tally tally = new Tally();
                    Thread first = new Thread(tally);
                    Thread second = new Thread(tally);
                    first.start();
                    second.start();
                    first.join();
                    second.join();
                    System.out.println(tally.count);
                }
            }
            """
        },
        {
            "two locks taken in opposite orders", "analysis-lock-order", """
            public class Accounts {
                private final Object left = new Object();
                private final Object right = new Object();

                void forwards() {
                    synchronized (left) {
                        synchronized (right) {
                            System.out.println("forwards");
                        }
                    }
                }

                void backwards() {
                    synchronized (right) {
                        synchronized (left) {
                            System.out.println("backwards");
                        }
                    }
                }

                public static void main(String[] args) throws InterruptedException {
                    Accounts accounts = new Accounts();
                    Thread one = new Thread(accounts::forwards);
                    Thread two = new Thread(accounts::backwards);
                    one.start();
                    two.start();
                    one.join();
                    two.join();
                }
            }
            """
        },
    };

    public static TheoryData<string, string, string> PythonMistakes => new()
    {
        {
            "a thread run here instead of started", "analysis-run-not-start", """
            import threading

            def work():
                print("working")

            thread = threading.Thread(target=work)
            thread.run()
            """
        },
    };

    private static string ClassName(string code) => code.Split("public class ")[1].Split(' ', '{')[0];

    private async Task<IReadOnlyList<AnalysisFinding>?> JavaAsync(string code)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var path = Path.Combine(_temp.Path, ClassName(code) + ".java");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));

        return AbstractChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText());
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> PythonAsync(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, "threads.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));

        return AbstractChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText());
    }

    private void Show(string name, IEnumerable<AnalysisFinding> findings)
    {
        foreach (var finding in findings)
        {
            output.WriteLine($"{name}: line {finding.Span.Line} [{finding.Severity}] {finding.CheckId}: {finding.Message}");
        }
    }

    [Theory]
    [MemberData(nameof(CorrectJava))]
    public async Task CorrectConcurrentJavaIsLeftAlone(string name, string code)
    {
        if (await JavaAsync(code) is not { } found) return;

        var mistakes = found.Where(f => f.Severity != Severity.Suggestion).ToList();
        Show(name, mistakes);

        Assert.Empty(mistakes);
    }

    [Theory]
    [MemberData(nameof(CorrectPython))]
    public async Task CorrectConcurrentPythonIsLeftAlone(string name, string code)
    {
        if (await PythonAsync(code) is not { } found) return;

        var mistakes = found.Where(f => f.Severity != Severity.Suggestion).ToList();
        Show(name, mistakes);

        Assert.Empty(mistakes);
    }

    [Theory]
    [MemberData(nameof(JavaMistakes))]
    public async Task TheConcurrencyMistakeIsFoundInJava(string name, string check, string code)
    {
        if (await JavaAsync(code) is not { } found) return;

        Show(name, found);
        Assert.Contains(found, finding => finding.CheckId == check);
    }

    [Theory]
    [MemberData(nameof(PythonMistakes))]
    public async Task TheConcurrencyMistakeIsFoundInPython(string name, string check, string code)
    {
        if (await PythonAsync(code) is not { } found) return;

        Show(name, found);
        Assert.Contains(found, finding => finding.CheckId == check);
    }
}
