using FixFinder.Core.Checking;
using FixFinder.Core.Engine;

namespace FixFinder.Tests;

/// <summary>
/// What was found last time, and what that lets the report say. A count on its own says little: five problems is good
/// news or bad news depending on what it was before.
/// </summary>
public class CheckHistoryTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string File => Path.Combine(_temp.Path, "history.json");

    private static Finding Of(Severity severity) => new()
    {
        Kind = FindingKind.Logic,
        Severity = severity,
        Confidence = Confidence.Likely,
        File = @"C:\work\thing.py",
        Line = 3,
        Title = "Title",
        Explanation = "what is wrong",
        WhyItMatters = "why",
        SuggestedFix = "fix",
        CorrectedExample = "",
    };

    private static CheckRecord Record(string file, int errors, int warnings, int suggestions = 0, DateTimeOffset? when = null) =>
        CheckRecord.Of(
            file,
            Enumerable.Repeat(Of(Severity.Error), errors)
                .Concat(Enumerable.Repeat(Of(Severity.Warning), warnings))
                .Concat(Enumerable.Repeat(Of(Severity.Suggestion), suggestions)),
            when);

    [Fact]
    public void ACheckIsRememberedAndFoundAgainByItsFile()
    {
        var history = new CheckHistory();
        Assert.True(history.Record(Record(@"C:\work\one.py", 2, 1), File));

        var read = CheckHistory.Load(File);
        var last = read.LastTime(@"C:\work\one.py");

        Assert.NotNull(last);
        Assert.Equal(2, last.Errors);
        Assert.Equal(1, last.Warnings);
        Assert.Equal(3, last.Problems);
    }

    /// <summary>A suggestion is not a problem, so it never counts as one.</summary>
    [Fact]
    public void SuggestionsAreCountedButAreNotProblems()
    {
        var check = Record(@"C:\work\one.py", 1, 0, suggestions: 4);

        Assert.Equal(4, check.Suggestions);
        Assert.Equal(1, check.Problems);
    }

    [Fact]
    public void AFileNobodyHasCheckedBeforeHasNothingToCompareWith()
    {
        var history = new CheckHistory();
        history.Record(Record(@"C:\work\one.py", 1, 0), File);

        Assert.Null(CheckHistory.Load(File).LastTime(@"C:\work\other.py"));
        Assert.Null(CheckHistory.Since(null, Record(@"C:\work\other.py", 1, 0)));
    }

    [Fact]
    public void TheMostRecentCheckOfAFileIsTheOneCompared()
    {
        var history = new CheckHistory();
        var now = DateTimeOffset.Now;

        history.Record(Record(@"C:\work\one.py", 9, 0, when: now.AddHours(-5)), File);
        history.Record(Record(@"C:\work\one.py", 4, 0, when: now.AddHours(-1)), File);

        Assert.Equal(4, CheckHistory.Load(File).LastTime(@"C:\work\one.py")!.Errors);
    }

    [Theory]
    [InlineData(5, 2, "fewer")]
    [InlineData(2, 5, "more")]
    [InlineData(3, 3, "same")]
    public void TheComparisonSaysWhichWayThingsWent(int before, int now, string expected)
    {
        var when = DateTimeOffset.Now;

        var said = CheckHistory.Since(
            Record(@"C:\work\one.py", before, 0, when: when.AddHours(-3)),
            Record(@"C:\work\one.py", now, 0, when: when));

        Assert.NotNull(said);
        Assert.Contains(expected, said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GoingFromSomeProblemsToNoneIsWorthSayingProperly()
    {
        var when = DateTimeOffset.Now;

        var said = CheckHistory.Since(
            Record(@"C:\work\one.py", 3, 0, when: when.AddDays(-1)),
            Record(@"C:\work\one.py", 0, 0, when: when));

        Assert.NotNull(said);
        Assert.Contains("gone", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AHistoryLongerThanWeKeepDropsTheOldestFirst()
    {
        var history = new CheckHistory();
        var start = DateTimeOffset.Now.AddDays(-30);

        for (var i = 0; i < CheckHistory.MostKept + 25; i++)
        {
            history.Record(Record($@"C:\work\file{i}.py", 1, 0, when: start.AddMinutes(i)), File);
        }

        var read = CheckHistory.Load(File);

        Assert.Equal(CheckHistory.MostKept, read.Checks.Count);
        Assert.Null(read.LastTime(@"C:\work\file0.py"));
        Assert.NotNull(read.LastTime($@"C:\work\file{CheckHistory.MostKept + 24}.py"));
    }

    [Fact]
    public void AHistoryFileThatCannotBeReadIsTreatedAsNoHistory()
    {
        System.IO.File.WriteAllText(File, "{ this is not json");

        Assert.Empty(CheckHistory.Load(File).Checks);
    }

    /// <summary>The history holds counts and times. Somebody's source sitting in a file like this is a liability.</summary>
    [Fact]
    public void NoCodeIsEverWrittenIntoTheHistory()
    {
        var history = new CheckHistory();
        history.Record(Record(@"C:\work\one.py", 1, 1, 1), File);

        var written = System.IO.File.ReadAllText(File);

        Assert.DoesNotContain("what is wrong", written, StringComparison.Ordinal);
        Assert.DoesNotContain("Title", written, StringComparison.Ordinal);
        Assert.DoesNotContain("fix", written, StringComparison.Ordinal);
    }
}
