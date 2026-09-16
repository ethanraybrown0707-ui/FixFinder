using System.Collections.Concurrent;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// Checking several proposed fixes at once, and still giving the answer the rules would give one at a time.
/// </summary>
/// <remarks>
/// No compiler is involved: each rule proposes a change to a real file, and a stand-in check takes as
/// long as the case says and passes or fails as the case says. That is what lets a later check finish
/// first on purpose, which is the one ordering a real compiler cannot be relied on to produce.
/// </remarks>
public class LocalFixOrderTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task TheEarliestRuleWinsEvenWhenALaterCheckFinishesFirst(int checksAtOnce)
    {
        var compiler = new Compiler(new()
        {
            ["first"] = (Delay: 300, Passes: true),
            ["second"] = (Delay: 10, Passes: true),
        });

        var found = await Find(compiler, checksAtOnce, "first", "second");

        Assert.Equal("local:first", found?.Candidate.Id);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task ARefusedFixLetsTheNextRuleWin(int checksAtOnce)
    {
        var compiler = new Compiler(new()
        {
            ["first"] = (Delay: 50, Passes: false),
            ["second"] = (Delay: 200, Passes: true),
            ["third"] = (Delay: 10, Passes: true),
        });

        var found = await Find(compiler, checksAtOnce, "first", "second", "third");

        Assert.Equal("local:second", found?.Candidate.Id);
    }

    [Fact]
    public async Task ChecksLeftRunningAreStoppedOnceAFixIsAccepted()
    {
        var log = new List<string>();
        var compiler = new Compiler(new()
        {
            ["first"] = (Delay: 50, Passes: true),
            ["second"] = (Delay: 60_000, Passes: true),
        });

        var found = await Find(compiler, 2, log, "first", "second");

        Assert.Equal("local:first", found?.Candidate.Id);
        Assert.Contains("second", compiler.Stopped);
        Assert.Contains("second: check stopped - an earlier rule's fix was accepted", log);
    }

    [Fact]
    public async Task NoMoreChecksRunAtOnceThanAllowed()
    {
        var names = new[] { "one", "two", "three", "four", "five", "six" };
        var compiler = new Compiler(names.ToDictionary(n => n, _ => (Delay: 40, Passes: false)));

        var found = await Find(compiler, 2, names);

        Assert.Null(found);
        Assert.Equal(names.Length, compiler.Started.Count);
        Assert.InRange(compiler.MostAtOnce, 1, 2);
    }

    [Fact]
    public async Task RulesPastTheWinnerAreNotAskedBeyondTheChecksAlreadyRunning()
    {
        var names = new[] { "one", "two", "three", "four" };
        var compiler = new Compiler(names.ToDictionary(n => n, _ => (Delay: 10, Passes: true)));

        await Find(compiler, 2, names);

        Assert.DoesNotContain("three", compiler.Started);
        Assert.DoesNotContain("four", compiler.Started);
    }

    private Task<LocalFixFound?> Find(Compiler compiler, int checksAtOnce, params string[] rules) =>
        Find(compiler, checksAtOnce, null, rules);

    private async Task<LocalFixFound?> Find(Compiler compiler, int checksAtOnce, List<string>? log, params string[] rules)
    {
        var file = Path.Combine(_temp.Path, "app.py");
        await File.WriteAllTextAsync(file, "x = 1\nprint(x)\n");

        var context = new LocalFixContext
        {
            Error = new ParsedError
            {
                LanguageId = "python",
                Confidence = 90,
                RawText = "NameError: name 'y' is not defined",
                FirstLineSequence = 0,
                ExceptionType = "NameError",
                Message = "name 'y' is not defined",
                Frames = [new ErrorFrame { Order = 0, File = file, Line = 2, RawLine = "" }],
            },
            SourceRoot = _temp.Path,
        };

        return await LocalFixEngine.FindAsync(
            context,
            rules.Select(id => (ILocalFixRule)new ReplaceFirstLine(id, file)).ToList(),
            compiler.CheckAsync,
            checksAtOnce,
            log is null ? null : new Action<string>(log.Add),
            CancellationToken.None);
    }

    /// <summary>A rule whose change says which rule made it, so the check can tell them apart.</summary>
    private sealed class ReplaceFirstLine(string id, string file) : ILocalFixRule
    {
        public string Id => id;

        public LocalFix? Propose(LocalFixContext context) =>
            LocalFix.ReplaceLine(id, $"Use {id}", $"Because {id}.", file, 1, $"x = '{id}'");
    }

    /// <summary>Stands in for the compiler: waits, then passes or fails, as each rule's case says.</summary>
    private sealed class Compiler(Dictionary<string, (int Delay, bool Passes)> plan)
    {
        private readonly object _gate = new();
        private int _running;

        public int MostAtOnce { get; private set; }

        public ConcurrentQueue<string> Started { get; } = new();

        public ConcurrentQueue<string> Stopped { get; } = new();

        public async Task<CheckResult> CheckAsync(SourceFile source, IReadOnlyList<string> lines, CancellationToken ct)
        {
            var id = plan.Keys.Single(k => lines[0] == $"x = '{k}'");
            Started.Enqueue(id);

            lock (_gate) MostAtOnce = Math.Max(MostAtOnce, ++_running);

            try
            {
                await Task.Delay(plan[id].Delay, ct);

                return plan[id].Passes
                    ? new CheckResult(true, 0, [], [])
                    : new CheckResult(true, 1, [], [Broken]);
            }
            catch (OperationCanceledException)
            {
                Stopped.Enqueue(id);
                return CheckResult.NotRun;
            }
            finally
            {
                lock (_gate) _running--;
            }
        }

        private static ParsedError Broken { get; } = new()
        {
            LanguageId = "python",
            Confidence = 90,
            RawText = "SyntaxError: invalid syntax",
            FirstLineSequence = 0,
            ExceptionType = "SyntaxError",
            Message = "invalid syntax",
            Frames = [],
        };
    }
}
