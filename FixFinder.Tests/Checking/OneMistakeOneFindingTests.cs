using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A crash and the analysis that proved it are one mistake, so one finding: each crash is tied to the check that finds the same
/// mistake by its exception and - where one exception means more than one mistake - by its message, as Python 3.13, Java 21 and
/// .NET 10 were seen to write it; and the finding kept has the input that fails, the lines that decide it, the run that
/// confirmed it and what the language itself said.
/// </summary>
public class OneMistakeOneFindingTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ParsedError Crash(string language, string type, string? message) => new()
    {
        LanguageId = language, Confidence = 90, RawText = "", FirstLineSequence = 0, Frames = [], ExceptionType = type, Message = message,
    };

    [Theory]
    [InlineData("marks.py", "python", "IndexError", "pop from empty list", "analysis-empty-collection")]
    [InlineData("marks.py", "python", "IndexError", "pop from an empty deque", "analysis-empty-collection")]
    [InlineData("marks.py", "python", "IndexError", "list index out of range", "analysis-index-out-of-range")]
    [InlineData("Marks.java", "java", "java.util.EmptyStackException", null, "analysis-empty-collection")]
    [InlineData("Marks.java", "java", "java.util.NoSuchElementException", null, "analysis-empty-collection")]
    [InlineData("Marks.java", "java", "java.util.NoSuchElementException", "No value present", null)]
    [InlineData("Marks.scala", "scala", "java.util.NoSuchElementException", "None.get", "logic-scala-option-get")]
    [InlineData("Marks.cs", "csharp", "System.InvalidOperationException", "Stack empty.", "analysis-empty-collection")]
    [InlineData("Marks.cs", "csharp", "System.InvalidOperationException", "Sequence contains no elements", "analysis-empty-collection")]
    [InlineData("Marks.cs", "csharp", "System.InvalidOperationException", "Collection was modified; enumeration operation may not execute.", null)]
    public void ACrashIsTiedToTheCheckForTheSameMistakeByItsNameAndMessage(string file, string language, string type, string? message, string? family)
    {
        var finding = FindingFactory.FromError(Crash(language, type, message), FindingKind.Runtime, Severity.Error, Confidence.Certain, Path.Combine(@"C:\work", file));

        Assert.Equal(family, finding.Family);
    }

    [Fact]
    public void APopFromAnEmptyListIsExplainedAsThatNotAsAnIndexPastTheEnd()
    {
        var popped = Guidebook.For(@"C:\work\marks.py", FindingKind.Runtime, error: Crash("python", "IndexError", "pop from empty list"));
        var pastTheEnd = Guidebook.For(@"C:\work\marks.py", FindingKind.Runtime, error: Crash("python", "IndexError", "list index out of range"));

        Assert.StartsWith("pop() takes an item off a list", popped.Explanation, StringComparison.Ordinal);
        Assert.StartsWith("The items in a list are numbered from 0", pastTheEnd.Explanation, StringComparison.Ordinal);
    }

    /// <summary>
    /// take(False) pops from an empty list: Python stops the run there, and the analysis proves it with the input that does it.
    /// The two are one finding, with the input, the run that confirmed it, the lines that decide it and Python's own words.
    /// </summary>
    [Fact]
    public async Task ACrashTheAnalysisProvedIsOneFindingWithTheInputThePathAndThePythonError()
    {
        if (TargetFactory.FindOnPath("python") is null)
        {
            output.WriteLine("Not run on this computer: no Python.");
            return;
        }

        var file = Path.Combine(_temp.Path, "take.py");
        File.WriteAllText(file, "def take(flag):\n    items = []\n    if flag:\n        items.append(1)\n    return items.pop()\n\n\nprint(take(True))\nprint(take(False))\n");
        var launch = TargetFactory.FromFile(file, LiveAllowance.For(file, TimeSpan.FromMinutes(2)));
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Python }.CheckAsync(launch);
        foreach (var each in report.Findings) output.WriteLine($"{each.Line}: {each.RuleId}: {each.Found} | {each.Witness} | {each.Confirmation}");

        var finding = Assert.Single(report.Findings);
        Assert.Equal((5, "analysis-empty-collection", Confidence.Certain), (finding.Line, finding.RuleId, finding.Confidence));
        Assert.Equal("On one way through the code `items` is always empty here - for example when `flag` is False - so `items.pop()` fails with IndexError.", finding.Found);
        Assert.Equal("`flag` is False", finding.Witness);
        Assert.StartsWith("Running `take(flag=False)` stopped with IndexError: pop from empty list on line 5", finding.Confirmation);
        Assert.Equal([2, 5], finding.Slice);
        Assert.Equal(("IndexError", "pop from empty list"), (finding.Error?.ShortExceptionType, finding.Error?.Message));
    }
}
