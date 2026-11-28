using FixFinder.Core.Checking;
using FixFinder.Core.Engine;
using FixFinder.Core.Reporting;
using FixFinder.Core.Teaching;

namespace FixFinder.Tests;

/// <summary>
/// The kinds of mistake a person keeps making: the history keeps the idea behind each finding and a fingerprint of it -
/// never its code - and a check says which of its own kinds were made again and again in the last 30 days, in this program
/// or in others, with the habit that stops it, in the window and in the saved report. A mistake counts once
/// however many checks found it.
/// </summary>
public class RecurringMistakesTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private const string LoopOneShort = "logic-off-by-one-length";
    private const string DividingByZero = "analysis-division-by-zero";

    private static Finding Found(string rule, string file, int line = 4) => new()
    {
        Kind = FindingKind.Logic,
        Severity = Severity.Warning,
        Confidence = Confidence.Likely,
        File = file,
        Line = line,
        Title = rule,
        Explanation = "",
        WhyItMatters = "",
        SuggestedFix = "",
        CorrectedExample = "",
        RuleId = rule,
    };

    private static CheckRecord Check(string file, DateTimeOffset when, params string[] rules) =>
        CheckRecord.Of(file, rules.Select(rule => Found(rule, file)), when);

    [Fact]
    public void ACheckKeepsTheIdeaBehindEachFindingAndNotItsCode()
    {
        var check = Check(@"C:\uni\grades.py", Now, LoopOneShort, DividingByZero, "some-rule-of-no-idea");

        Assert.Equal(["OFFBYONE", "DIVZERO", "GENERAL"], check.Concepts);
    }

    [Fact]
    public void AKindOfMistakeInOtherProgramsLatelyIsSaidWithTheHabitThatStopsIt()
    {
        var history = new[]
        {
            Check(@"C:\uni\marks.py", Now.AddDays(-3), LoopOneShort),
            Check(@"C:\uni\marks.py", Now.AddDays(-2), LoopOneShort),
            Check(@"C:\uni\scores.py", Now.AddDays(-10), LoopOneShort, DividingByZero),
            Check(@"C:\uni\grades.py", Now.AddDays(-1), DividingByZero),
            Check(@"C:\uni\old.py", Now.AddDays(-40), DividingByZero),
        };

        var recurring = RecurringMistakes.Of(history, Check(@"C:\uni\grades.py", Now, LoopOneShort, DividingByZero));

        Assert.Equal([("OFFBYONE", 3), ("DIVZERO", 2)], recurring.Select(mistake => (mistake.Concept.Code, mistake.Times)));
        Assert.Equal([@"C:\uni\marks.py", @"C:\uni\scores.py"], recurring[0].OtherPrograms);
        Assert.Equal("Going one step too far has turned up 3 times in the last 30 days, in 3 of your programs: this one, marks.py and scores.py. " +
                     "What stops it: When a loop uses positions, check its first and last pass by hand: does it start at the first item, and " +
                     "stop at the last one?", RecurringMistakes.Said(recurring[0]));
        Assert.StartsWith("Dividing by zero has turned up twice in the last 30 days, in 2 of your programs: this one and scores.py. What stops it: ",
                          RecurringMistakes.Said(recurring[1]));
    }

    /// <summary>
    /// A slip made again in the same program - on another line, after the last was put right - is another mistake of the
    /// kind; the same one, still there in the next check, is not. Three of them in one program are said.
    /// </summary>
    [Fact]
    public void ASlipMadeAgainAndAgainInOneProgramIsSaidOnceItIsTheThirdOfItsKind()
    {
        var program = Path.Combine(_temp.Path, "grades.py");
        File.WriteAllLines(program, ["for index in range(len(marks) - 1):", "    total += marks[index]", "", "for index in range(len(names) - 1):",
            "    print(names[index])", "", "for index in range(len(ages) - 1):", "    print(ages[index])"]);
        CheckRecord CheckAt(DateTimeOffset when, params int[] lines) => CheckRecord.Of(program, lines.Select(line => Found(LoopOneShort, program, line)), when);

        var history = new[] { CheckAt(Now.AddDays(-6), 1), CheckAt(Now.AddDays(-5), 1), CheckAt(Now.AddDays(-2), 4) };

        Assert.Empty(RecurringMistakes.Of(history, CheckAt(Now, 4)));

        var recurring = Assert.Single(RecurringMistakes.Of(history, CheckAt(Now, 7)));
        Assert.Equal((3, 0), (recurring.Times, recurring.OtherPrograms.Count));
        Assert.StartsWith("Going one step too far has turned up 3 times in this program in the last 30 days. What stops it: ", RecurringMistakes.Said(recurring));
    }

    /// <summary>A mistake taught only as reading an error is no habit, however often it turns up.</summary>
    [Fact]
    public void TheGeneralIdeaIsNotSaid()
    {
        var history = new[] { Check(@"C:\uni\marks.py", Now.AddDays(-1), "some-rule-of-no-idea"), Check(@"C:\uni\scores.py", Now.AddDays(-1), "some-rule-of-no-idea") };

        Assert.Empty(RecurringMistakes.Of(history, Check(@"C:\uni\grades.py", Now, "some-rule-of-no-idea")));
    }

    [Fact]
    public void ProgramsOfTheSameNameAreToldApartByTheirFolders()
    {
        var history = new[] { Check(@"C:\uni\week3\main.py", Now.AddDays(-7), LoopOneShort), Check(@"C:\uni\week4\main.py", Now.AddDays(-1), LoopOneShort) };

        var recurring = Assert.Single(RecurringMistakes.Of(history, Check(@"C:\uni\week5\grades.py", Now, LoopOneShort)));

        Assert.StartsWith(@"Going one step too far has turned up 3 times in the last 30 days, in 3 of your programs: this one, week3\main.py and week4\main.py.",
                          RecurringMistakes.Said(recurring));
    }

    [Fact]
    public void TheSavedReportSaysWhatKeepsTurningUp()
    {
        var said = RecurringMistakes.Said(new RecurringMistake(Concepts.DivisionByZero, 2, [@"C:\uni\scores.py"]));

        var page = new ReportPage
        {
            Program = "grades.py", Language = "Python", CheckedAt = Now, SyntaxSummary = "No syntax errors", LogicSummary = "1 possible mistake in the code",
            Findings = [], Recurring = [said],
        }.ToHtml();

        Assert.Contains("<section class=\"recurring\">\n<h2>What keeps turning up</h2>\n<p>Dividing by zero has turned up twice in the last 30 days, " +
                        "in 2 of your programs: this one and scores.py.", page, StringComparison.Ordinal);
    }
}
