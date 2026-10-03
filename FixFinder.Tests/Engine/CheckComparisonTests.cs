using FixFinder.Core.Checking;
using FixFinder.Core.Engine;

namespace FixFinder.Tests;

/// <summary>
/// A check beside the last check of the same program: each finding new or still there, those gone counted - and not
/// counted as fixed when only a run could have found them and the program did not run.
/// </summary>
public class CheckComparisonTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Program(params string[] lines)
    {
        var path = Path.Combine(_temp.Path, "Marks.java");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static Finding Found(string file, int line, string title, FindingKind kind = FindingKind.Logic, string rule = "", Severity severity = Severity.Warning) => new()
    {
        Kind = kind,
        Severity = severity,
        Confidence = Confidence.Likely,
        File = file,
        Line = line,
        Title = title,
        Explanation = "What is wrong.",
        WhyItMatters = "Why it matters.",
        SuggestedFix = "How to fix it.",
        CorrectedExample = "",
        RuleId = rule,
    };

    private static readonly DateTimeOffset Earlier = new(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Earlier.AddMinutes(12);

    /// <summary>Lines put in above a mistake move it down; it is the same mistake, still there.</summary>
    [Fact]
    public void AFindingWhoseLineMovedIsStillThere()
    {
        var file = Program("int total = 0;", "int average = total / count;");
        var last = CheckRecord.Of(file, [Found(file, 2, "Division by zero", rule: "analysis-division-by-zero")], Earlier, ran: true);

        File.WriteAllLines(file, ["// marks", "int total = 0;", "int average = total / count;"]);
        var moved = Found(file, 3, "Division by zero", rule: "analysis-division-by-zero");

        var comparison = CheckComparison.Of(last, [moved], ranNow: true)!;

        Assert.Equal(SinceLastCheck.StillThere, comparison.StatusOf(moved));
        Assert.Equal(0, comparison.FixedCount);
    }

    [Fact]
    public void AFindingWhoseLineChangedIsFixedAndAnotherOneIsNew()
    {
        var file = Program("int average = total / count;", "System.out.println(average)");
        var division = Found(file, 1, "Division by zero", rule: "analysis-division-by-zero");
        var last = CheckRecord.Of(file, [division], Earlier, ran: true);

        File.WriteAllLines(file, ["int average = count == 0 ? 0 : total / count;", "System.out.println(average)"]);
        var semicolon = Found(file, 2, "';' expected", FindingKind.Syntax, severity: Severity.Error);

        var comparison = CheckComparison.Of(last, [semicolon], ranNow: false, lastFindings: [division])!;

        Assert.Equal(SinceLastCheck.New, comparison.StatusOf(semicolon));
        Assert.Equal(1, comparison.FixedCount);
        Assert.Equal([division], comparison.Fixed);
    }

    /// <summary>The same mistake twice is two findings: one still there does not make the other still there too.</summary>
    [Fact]
    public void EachFindingIsMatchedOnce()
    {
        var file = Program("x = a / b;", "x = a / b;");
        var last = CheckRecord.Of(file, [Found(file, 1, "Division by zero", rule: "r")], Earlier, ran: true);

        var first = Found(file, 1, "Division by zero", rule: "r");
        var second = Found(file, 2, "Division by zero", rule: "r");
        var comparison = CheckComparison.Of(last, [first, second], ranNow: true)!;

        Assert.Equal(1, comparison.StillThereCount);
        Assert.Equal(1, comparison.NewCount);
    }

    /// <summary>A crash is found only by a run: with the program not run this time, the crash found last time is not known to be fixed.</summary>
    [Fact]
    public void WhatOnlyARunFindsIsNotCountedFixedWhenTheProgramDidNotRun()
    {
        var file = Program("int[] marks = new int[3];", "marks[3] = 1;", "System.out.println(marks)");
        var crash = Found(file, 2, "ArrayIndexOutOfBoundsException", FindingKind.Runtime, severity: Severity.Error);
        var last = CheckRecord.Of(file, [crash], Earlier, ran: true);

        var semicolon = Found(file, 3, "';' expected", FindingKind.Syntax, severity: Severity.Error);
        var comparison = CheckComparison.Of(last, [semicolon], ranNow: false, lastFindings: [crash])!;

        Assert.Equal(0, comparison.FixedCount);
        Assert.Equal(1, comparison.NotCheckedCount);
        Assert.Empty(comparison.Fixed);
        Assert.Equal("Since the last check, 12 minutes ago: 0 fixed, 1 new and 0 still there. 1 more found last time was not looked for, " +
                     "as the program did not run this time.", comparison.Summary(Later));
    }

    [Fact]
    public void TwoChecksThatFoundNothingSaySo()
    {
        var file = Program("print(1 + 1)");
        var last = CheckRecord.Of(file, [], Earlier, ran: true);

        Assert.Equal("Since the last check, 12 minutes ago: still nothing found.", CheckComparison.Of(last, [], ranNow: true)!.Summary(Later));
    }

    [Fact]
    public void WithNoLastCheckThereIsNothingToCompare()
    {
        var file = Program("int x = 1;");

        Assert.Null(CheckComparison.Of(null, [Found(file, 1, "Unused")], ranNow: true));
    }

    /// <summary>A record from before findings were fingerprinted has only counts, which say nothing of which finding is which.</summary>
    [Fact]
    public void ARecordWithCountsButNoFingerprintsIsNotCompared()
    {
        var file = Program("int x = 1;");
        var old = new CheckRecord(Earlier, file, 1, 0, 0);

        Assert.Null(CheckComparison.Of(old, [Found(file, 1, "Unused")], ranNow: true));
    }

    /// <summary>The history file holds a hash of each finding's line, never the code on it.</summary>
    [Fact]
    public void TheHistoryKeepsFingerprintsAndNotCode()
    {
        var file = Program("int secretTotal = 4 / divisorOfMarks;");
        var history = new CheckHistory();
        var path = Path.Combine(_temp.Path, "history.json");

        history.Record(CheckRecord.Of(file, [Found(file, 1, "Division by zero", rule: "analysis-division-by-zero")], Earlier, ran: true), path);
        var written = File.ReadAllText(path);
        var read = CheckHistory.Load(path).LastTime(file)!;

        Assert.DoesNotContain("secretTotal", written, StringComparison.Ordinal);
        Assert.DoesNotContain("divisorOfMarks", written, StringComparison.Ordinal);
        Assert.Single(read.Fingerprints);
        Assert.StartsWith(CheckRecord.CodePrefix, read.Fingerprints[0], StringComparison.Ordinal);
        Assert.True(read.Ran);
    }
}
