using FixFinder.Core;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Recognition;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Reporting;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// A Python function that works out the same answers again and again - fib(n - 1) + fib(n - 2) - is called with 5 up to
/// 25, its calls counted as it is and kept with functools.cache, and the counts are a finding on the Efficiency tab; a
/// function that prints, calls another, calls itself once or belongs to a class is not tried.
/// </summary>
public class RepeatedCallsTrialTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Fibonacci = """
        def fib(n):
            if n < 2:
                return n
            return fib(n - 1) + fib(n - 2)


        print(fib(20))
        """;

    private async Task<(IrProgram Program, string Path)?> ReadAsync(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;
        var path = Path.Combine(_temp.Path, "numbers.py");
        await File.WriteAllTextAsync(path, code);
        return (await PythonFrontend.ReadAsync([path], python), path);
    }

    /// <summary>
    /// fib is called 15, 177, 1,973, 21,891 and 242,785 times for 5, 10, 15, 20 and 25 - as plain Python counts it too - and
    /// 6, 11, 16, 21 and 26 times kept with the cache, one for each number from 0 up, with the same answers both ways.
    /// </summary>
    [Fact]
    public async Task ARecursionThatRepeatsItsCallsIsCountedBothWays()
    {
        if (await ReadAsync(Fibonacci) is not ({ } program, { } path) || PythonFrontend.FindInterpreter() is not { } python) return;

        var found = Assert.Single(await AlgorithmTrial.TryRepeatedCallsAsync(program, path, python, CancellationToken.None));

        Assert.Equal((FindingKind.Performance, Severity.Suggestion, Confidence.Certain, 1), (found.Kind, found.Severity, found.Confidence, found.Line));
        Assert.Equal("`fib` works out the same answers again and again", found.Title);
        Assert.Equal("Called with 5, 10, 15, 20 and 25, `fib` was called 15, 177, 1,973, 21,891 and 242,785 times, counting the first " +
                     "call; kept with `@functools.cache`, 6, 11, 16, 21 and 26 times, and it gave back the same answers both ways: 5, 55, " +
                     "610, 6765 and 75025.", found.Found);
        Assert.Equal("calls counted", Evidence.Source(found));
    }

    /// <summary>A function that prints, one that calls another, one that calls itself once, and a method are not tried.</summary>
    [Fact]
    public async Task OnlyAFunctionThatCallsNothingButItselfTwiceIsTried()
    {
        const string code = """
            def noisy(n):
                print(n)
                if n < 2:
                    return n
                return noisy(n - 1) + noisy(n - 2)

            def merge_sort(items):
                if len(items) < 2:
                    return items
                middle = len(items) // 2
                return merge(merge_sort(items[:middle]), merge_sort(items[middle:]))

            def factorial(n):
                if n < 2:
                    return 1
                return n * factorial(n - 1)

            class Numbers:
                def fib(self, n):
                    if n < 2:
                        return n
                    return self.fib(n - 1) + self.fib(n - 2)

            def ways(steps):
                if steps < 0:
                    return 0
                if steps == 0:
                    return 1
                return ways(steps - 1) + ways(steps - 2) + ways(steps - 3)
            """;
        if (await ReadAsync(code) is not ({ } program, { } path)) return;

        Assert.Equal(["ways"], AlgorithmTrial.RepeatedCallCandidates(program, path).Select(function => function.Name));
    }

    /// <summary>
    /// Three calls down - ways(steps - 1) + ways(steps - 2) + ways(steps - 3) - are counted too, and the cache's answers are
    /// compared with the function's own.
    /// </summary>
    [Fact]
    public async Task ThreeCallsDownAreCountedTooAndTheAnswersCompared()
    {
        const string code = """
            def ways(steps):
                if steps < 0:
                    return 0
                if steps == 0:
                    return 1
                return ways(steps - 1) + ways(steps - 2) + ways(steps - 3)
            """;
        if (await ReadAsync(code) is not ({ } program, { } path) || PythonFrontend.FindInterpreter() is not { } python) return;

        var found = Assert.Single(await AlgorithmTrial.TryRepeatedCallsAsync(program, path, python, CancellationToken.None));
        Assert.StartsWith("Called with 5, 10, 15", found.Found);
        Assert.Contains("and it gave back the same answers both ways", found.Found);
    }

    /// <summary>A check of the program puts it on the Efficiency tab, where it takes nothing off the health.</summary>
    [Fact]
    public async Task ACheckListsItUnderEfficiency()
    {
        if (await ReadAsync(Fibonacci) is not (_, { } path)) return;

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Python }.CheckAsync(TargetFactory.FromFile(path));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == AlgorithmTrial.RepeatedCallsRule);
        Assert.Equal(FindingKind.Performance, found.Kind);
        Assert.Equal(100, HealthScore.Of(report.Findings.Where(finding => finding.RuleId == AlgorithmTrial.RepeatedCallsRule)).Score);
    }
}
