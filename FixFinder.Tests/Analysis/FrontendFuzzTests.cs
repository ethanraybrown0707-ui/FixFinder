using System.Text;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Mangled source, fed to the parsers FixFinder wrote itself.
/// </summary>
/// <remarks>
/// Python, Java, C# and Go are read by their own compilers' parsers, which have met every kind of broken program there
/// is. JavaScript, C and C++ are read by parsers written here, and those have only met the programs somebody thought to
/// try - one of them has already hung on a class member it did not expect. Students hand in code that is half written,
/// mid-edit or pasted wrong, so these have to survive anything.
/// <para>
/// The contract is the one the parsers set themselves: none of them throws, anywhere, so trouble is reported through
/// their Problem instead - and nothing may hang. An exception on any input is therefore a bug by their own design, and
/// so is a parse that does not come back. The analyses that run over whatever the parser made of it are held to the
/// same, because a half-read program is exactly what they will be given.
/// </para>
/// <para>
/// Every case comes from a fixed seed, so a failure names a case that can be run again exactly.
/// </para>
/// </remarks>
public class FrontendFuzzTests(ITestOutputHelper output) : IDisposable
{
    /// <summary>
    /// How many damaged programs each starting program is turned into. Sixty keeps the suite quick; set
    /// FIXFINDER_FUZZ_CASES for a deep sweep, and FIXFINDER_FUZZ_SEED to explore different damage from the same starts.
    /// </summary>
    private static readonly int CasesPerSeed =
        int.TryParse(Environment.GetEnvironmentVariable("FIXFINDER_FUZZ_CASES"), out var asked) && asked > 0 ? asked : 60;

    private static readonly int SeedOffset =
        int.TryParse(Environment.GetEnvironmentVariable("FIXFINDER_FUZZ_SEED"), out var offset) ? offset : 0;

    /// <summary>
    /// Far longer than any honest parse takes - they take milliseconds - so a parse still going after this is stuck, not
    /// slow. Generous on purpose: a real hang never ends, so a longer limit loses nothing in catching one, and a limit
    /// tuned to this machine's speed only turns a slower machine's ordinary pace into a failure.
    /// </summary>
    private static readonly TimeSpan LongestParse = TimeSpan.FromSeconds(30);

    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Pieces that, dropped into the middle of a program, are most likely to confuse a parser.</summary>
    private static readonly string[] Awkward =
    [
        "{", "}", "(", ")", "[", "]", "\"", "'", "`", "/*", "*/", "//", "\\", "\n", ";", ",", "<", ">", "...", "=>",
        "?.", "${", "/", "#", "#define X", "#if 1", "#endif", "#include <", "*", "&", "->", "::", "template <", "class ",
        "function", "=", "==", "!", "?", ":", "0x", "1e", ".", "\"unterminated", "'\\", "/[", "@", "\t\t", "é", "\0",
    ];

    private static readonly string[] JavaScript =
    [
        """
        const basket = ["apples", "bread"];
        function total(items) {
            let sum = 0;
            for (const item of items) { sum += item.price ?? 0; }
            return sum / items.length;
        }
        class Till {
            #count = 0;
            add(x) { this.#count++; return x?.price; }
            static make() { return new Till(); }
        }
        const pick = (a, b = 2) => `${a}-${b}`;
        const found = /ab+c/gi.test("abbc");
        async function load() { const r = await fetch("x"); return r?.body?.length; }
        """,
        """
        const counts = {};
        for (let i = 0; i <= words.length; i++) {
            counts[words[i]] = (counts[words[i]] || 0) + 1;
        }
        const { a, b: [c, ...rest] } = counts;
        label: while (true) { if (a) break label; else continue; }
        switch (typeof a) { case "string": x = a / 2; break; default: throw new Error("no"); }
        """,
    ];

    private static readonly string[] C =
    [
        """
        #include <stdio.h>
        #include <stdlib.h>
        #define SIZE 3
        struct point { int x; int y; };
        typedef int (*compare)(const void *, const void *);
        static int total_of(const int *lines, int count) {
            int total = 0;
            for (int i = 0; i < count; i++) { total += lines[i]; }
            return total;
        }
        int main(void) {
            int *lines = malloc(SIZE * sizeof(int));
            if (lines == NULL) return 1;
            struct point p = { .x = 1, .y = 2 };
            lines[0] = p.x ? 1 : 0;
            printf("%d\n", total_of(lines, SIZE));
            free(lines);
            return 0;
        }
        """,
        """
        #include <iostream>
        #include <vector>
        template <typename T> T biggest(const std::vector<T>& items) {
            T best = items[0];
            for (auto& item : items) if (item > best) best = item;
            return best;
        }
        class Stock { public: int *levels = new int[3]; ~Stock() { delete[] levels; } };
        int main() {
            std::vector<int> v{3, 1, 2};
            auto f = [&](int x) -> int { return x * 2; };
            std::cout << biggest(v) << f(4) << std::endl;
            return 0;
        }
        """,
    ];

    /// <summary>One of five kinds of damage, done to a program at a place the seed picks.</summary>
    private static (string Source, string What) Mangle(string source, Random random)
    {
        if (source.Length == 0) return (Awkward[random.Next(Awkward.Length)], "empty, then a piece added");

        var at = random.Next(source.Length);
        var length = random.Next(1, Math.Min(40, source.Length - at) + 1);

        return random.Next(5) switch
        {
            0 => (source.Remove(at, length), $"{length} characters cut at {at}"),
            1 => (source.Insert(at, source.Substring(at, length)), $"{length} characters doubled at {at}"),
            2 => (source.Insert(at, Awkward[random.Next(Awkward.Length)]), $"a piece dropped in at {at}"),
            3 => (source[..at], $"cut off after {at} characters"),
            _ => (Swapped(source, random), "two stretches swapped"),
        };
    }

    private static string Swapped(string source, Random random)
    {
        var first = random.Next(source.Length);
        var second = random.Next(source.Length);
        var (a, b) = (Math.Min(first, second), Math.Max(first, second));
        var length = Math.Min(20, Math.Min(b - a, source.Length - b));

        if (length <= 0) return source;

        return source[..a] + source.Substring(b, length) + source.Substring(a + length, b - a - length) +
               source.Substring(a, length) + source[(b + length)..];
    }

    private async Task<List<string>> AttackAsync(string[] seeds, string extension, Func<IReadOnlyList<string>, Task<IrProgram>> read, int seed)
    {
        seed += SeedOffset;
        var random = new Random(seed);
        var failures = new List<string>();
        var index = 0;

        // How long each case took, so a reader that is slow but not yet stuck shows up before it becomes a timeout on a
        // slower machine - which is how a pathological input first shows itself.
        var slowest = new List<(double Milliseconds, int Case, string What)>();

        // Each starting program twice: with Unix line endings and with Windows ones, set here rather than inherited. The
        // programs are written inside this file, and a fresh Windows checkout gives it CRLF where this copy has LF - so
        // left to the checkout, the same seed damaged different programs on different machines, and "seed 1972, case 7"
        // named a different program on CI than here. Setting the endings makes the seed mean the same thing everywhere,
        // and doing both covers what students on Windows actually hand in.
        var starts = seeds.SelectMany(program => new[] { program.ReplaceLineEndings("\n"), program.ReplaceLineEndings("\r\n") });

        foreach (var original in starts)
        {
            var source = original;

            for (var round = 0; round < CasesPerSeed; round++, index++)
            {
                // Damage piles up across a run, the way a half-finished edit does, rather than always starting clean.
                if (random.Next(4) == 0) source = original;

                var (mangled, what) = Mangle(source, random);
                source = mangled;

                var file = Path.Combine(_temp.Path, $"case{index}{extension}");
                await File.WriteAllTextAsync(file, source, Encoding.UTF8);

                var clock = System.Diagnostics.Stopwatch.StartNew();
                var trouble = await TryAsync(file, read);
                clock.Stop();

                slowest.Add((clock.Elapsed.TotalMilliseconds, index, what));

                if (trouble is null) continue;

                var shown = source.Length > 160 ? source[..160] + "…" : source;
                failures.Add($"seed {seed}, case {index} ({what}): {trouble}\n    {shown.Replace("\n", "\n    ")}");
            }
        }

        foreach (var (milliseconds, slowCase, what) in slowest.OrderByDescending(s => s.Milliseconds).Take(3))
        {
            output.WriteLine($"slowest: seed {seed}, case {slowCase} ({what}) took {milliseconds:0} ms");
        }

        return failures;
    }

    /// <summary>What went wrong with one case, or null when the parser and the analyses behaved.</summary>
    private static async Task<string?> TryAsync(string file, Func<IReadOnlyList<string>, Task<IrProgram>> read)
    {
        var (program, readingTrouble) = await TimedAsync(() => read([file]), "reading it");
        if (readingTrouble is not null) return readingTrouble;

        var (_, analysingTrouble) = await TimedAsync(() =>
        {
            AbstractChecks.Run(program!, new SourceText());
            PerformanceChecks.Run(program!, new SourceText());
            return Task.FromResult(true);
        }, "the analyses of what it read");

        return analysingTrouble;
    }

    /// <summary>
    /// Runs one piece of work, and says what went wrong if it threw or never came back.
    /// </summary>
    /// <remarks>
    /// The clock starts when the work does, not when it is handed over. The suite runs these alongside tests that
    /// compile and run whole programs, and on a busy CI runner a job that takes a millisecond can wait a long time for a
    /// thread to run on: counted from the hand-over, two such jobs once looked like ten-second hangs (CI run on 33b1c7b,
    /// 2026-09-25) when the same programs, reproduced exactly, finished in a millisecond here. Waiting for a thread is the
    /// machine being busy, not the reader being stuck.
    /// </remarks>
    private static async Task<(T? Value, string? Trouble)> TimedAsync<T>(Func<Task<T>> work, string what)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = Task.Run(() =>
        {
            started.TrySetResult();
            return work();
        });

        await started.Task;

        try
        {
            return (await running.WaitAsync(LongestParse), null);
        }
        catch (TimeoutException)
        {
            return (default, $"{what} was still going {LongestParse.TotalSeconds:0} seconds after it started");
        }
        catch (Exception ex)
        {
            return (default, $"{what} threw {ex.GetType().Name}: {ex.Message}{Where(ex)}");
        }
    }

    /// <summary>The innermost line of FixFinder's own code the exception came from, so a failure says where to look.</summary>
    private static string Where(Exception ex) =>
        (ex.StackTrace ?? "").Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Contains("FixFinder.Core", StringComparison.Ordinal)) is { } frame
            ? $"\n    at {frame}"
            : "";

    private void Report(List<string> failures)
    {
        foreach (var failure in failures) output.WriteLine(failure);

        Assert.True(failures.Count == 0,
            $"{failures.Count} mangled programs broke the reader:\n\n{string.Join("\n\n", failures.Take(5))}");
    }

    [Fact]
    public async Task TheJavaScriptReaderSurvivesAnythingItIsGiven()
    {
        Report(await AttackAsync(JavaScript, ".js", files => JavaScriptFrontend.ReadAsync(files), seed: 20260925));
    }

    [Fact]
    public async Task TheCReaderSurvivesAnythingItIsGiven()
    {
        Report(await AttackAsync([C[0]], ".c", files => CFrontend.ReadAsync(files), seed: 1972));
    }


    [Fact]
    public async Task TheCppReaderSurvivesAnythingItIsGiven()
    {
        Report(await AttackAsync([C[1]], ".cpp", files => CFrontend.ReadAsync(files), seed: 1985));
    }
}
