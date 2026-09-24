using FixFinder.Core;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// Work that grows with the data. Most of these check that nothing is said: a performance warning nobody can act on
/// is worse than silence, so the pattern has to be one whose cost is visible in the shape of the code.
/// </summary>
public class PerformanceCheckTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Returns nothing at all when Python is not installed, so the assertions are skipped rather than wrong.</summary>
    private async Task<IReadOnlyList<AnalysisFinding>?> CheckAsync(string code, string name = "shop.py")
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, name);
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));

        return PerformanceChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText());
    }

    [Fact]
    public async Task SearchingAListInsideALoopIsWorthSaying()
    {
        if (await CheckAsync("""
            def matching(people, names):
                found = []
                for person in people:
                    if person in names:
                        found.append(person)
                return found
            """) is not { } found) return;

        var finding = Assert.Single(found, f => f.CheckId == "analysis-repeated-search");

        Assert.Equal(FindingKind.Performance, finding.Kind);
        Assert.Equal(Severity.Suggestion, finding.Severity);
        Assert.Contains("names", finding.Message);
    }

    [Fact]
    public async Task TheSameSearchOutsideALoopIsNotWorthSaying()
    {
        if (await CheckAsync("""
            def matching(person, names):
                if person in names:
                    return person
                return None
            """) is not { } found) return;

        Assert.Empty(found);
    }

    /// <summary>
    /// A list the loop itself builds is a different list on every pass, so it is not the same search repeated - and
    /// putting it in a set would change what the program does, not how long it takes.
    /// </summary>
    [Fact]
    public async Task ACollectionTheLoopChangesIsLeftAlone()
    {
        if (await CheckAsync("""
            def unique(people):
                seen = []
                for person in people:
                    if person not in seen:
                        seen.append(person)
                return seen
            """) is not { } found) return;

        Assert.Empty(found);
    }

    [Fact]
    public async Task ACollectionAssignedInsideTheLoopIsLeftAlone()
    {
        if (await CheckAsync("""
            def matching(people):
                found = []
                for person in people:
                    names = lookup(person)
                    if person in names:
                        found.append(person)
                return found
            """) is not { } found) return;

        Assert.Empty(found);
    }

    [Fact]
    public async Task ASearchNestedDeeperInsideTheLoopStillCounts()
    {
        if (await CheckAsync("""
            def matching(people, names):
                found = []
                for person in people:
                    if person.active:
                        if person.name in names:
                            found.append(person)
                return found
            """) is not { } found) return;

        Assert.Single(found, f => f.CheckId == "analysis-repeated-search");
    }

    [Fact]
    public async Task APlainLoopWithNoSearchInItSaysNothing()
    {
        if (await CheckAsync("""
            def total(prices):
                running = 0
                for price in prices:
                    running += price
                return running
            """) is not { } found) return;

        Assert.Empty(found);
    }

    /// <summary>A performance finding is never an error: the program is right, it is only doing more work than it needs.</summary>
    [Fact]
    public async Task NothingHereIsEverReportedAsAnError()
    {
        if (await CheckAsync("""
            def matching(people, names):
                for person in people:
                    if person in names:
                        print(person)
            """) is not { } found) return;

        Assert.All(found, finding => Assert.Equal(Severity.Suggestion, finding.Severity));
    }
}
