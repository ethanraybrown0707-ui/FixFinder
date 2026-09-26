using System.Text.Json;
using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;

namespace FixFinder.Tests;

/// <summary>
/// The CWE classification: every rule classified is one FixFinder really reports, every link is to the entry's own
/// page, and a rule with no entry that fits it exactly is left unclassified rather than given the nearest one.
/// </summary>
public class WeaknessesTests
{
    public static TheoryData<string, string, int> Classified => new()
    {
        { "analysis-sql-injection", "shop.py", 89 },
        { "analysis-command-injection", "run.py", 78 },
        { "analysis-code-injection", "calc.py", 95 },
        { "analysis-loop-can-get-stuck", "search.py", 835 },
        { "analysis-division-by-zero", "mean.py", 369 },
        { "analysis-null-used", "Main.java", 476 },
        { "analysis-null-used", "Orders.cs", 476 },
        { "analysis-null-used", "main.go", 476 },
        { "analysis-data-race", "Bank.java", 362 },
        { "analysis-stale-read", "Worker.java", 820 },
        { "analysis-lock-cycle", "Bank.java", 833 },
        { "analysis-lock-reacquired", "bank.py", 764 },
        { "analysis-run-not-start", "Worker.java", 572 },
        { "analysis-run-not-start", "worker.py", 572 },
        { "analysis-resource-not-closed", "Report.java", 772 },
        { "analysis-use-after-free", "list.c", 416 },
        { "analysis-dangling-pointer", "counter.c", 562 },
        { "analysis-unassigned-after-error", "parse.py", 457 },
        { "analysis-never-true", "grade.py", 570 },
        { "analysis-always-true", "grade.py", 571 },
    };

    [Theory]
    [MemberData(nameof(Classified))]
    public void EachAnalysisIsClassifiedUnderItsEntry(string rule, string file, int expected)
    {
        var weakness = Weaknesses.For(rule, file);

        Assert.NotNull(weakness);
        Assert.Equal(expected, weakness.Id);
        Assert.Equal($"https://cwe.mitre.org/data/definitions/{expected}.html", weakness.Url);
    }

    /// <summary>
    /// CWE-129 is an index that comes in from outside unchecked; FixFinder's rule also reports one worked out one step too
    /// far inside the function (CWE-193) and cannot tell which it is, so it says neither.
    /// </summary>
    [Fact]
    public void AnIndexOutsideTheListIsLeftUnclassified()
    {
        Assert.Null(Weaknesses.For("analysis-index-out-of-range", "marks.py"));
        Assert.Null(Weaknesses.For("analysis-index-out-of-range", "marks.c"));
    }

    /// <summary>CWE-476 is a NULL pointer. Python's None and JavaScript's null are objects, not pointers.</summary>
    [Fact]
    public void NothingIsANullPointerOnlyWhereTheLanguageHasThem()
    {
        Assert.Null(Weaknesses.For("analysis-null-used", "orders.py"));
        Assert.Null(Weaknesses.For("analysis-null-used", "orders.js"));
        Assert.Equal("NULL Pointer Dereference", Weaknesses.For("analysis-null-used", "orders.c")!.Title);
    }

    /// <summary>CWE-584 is a return in a finally block; the rule also reports break and continue, so it is not given 584.</summary>
    [Fact]
    public void ARuleWithNoExactEntryIsLeftUnclassified()
    {
        Assert.Null(Weaknesses.For("analysis-finally-overrides", "load.py"));
        Assert.Null(Weaknesses.For("a-rule-that-does-not-exist", "load.py"));
    }

    /// <summary>A classification of a rule nothing reports would be dead weight at best, and a typo at worst.</summary>
    [Fact]
    public void EveryClassifiedRuleIsOneFixFinderReports()
    {
        foreach (var rule in Weaknesses.ClassifiedRules())
            Assert.True(Guidebook.HasRule(rule), $"no guide - and so no finding - for {rule}");
    }

    [Fact]
    public void EveryEntryLinksToItsOwnPage()
    {
        foreach (var weakness in Weaknesses.Every())
        {
            Assert.Equal($"https://cwe.mitre.org/data/definitions/{weakness.Id}.html", weakness.Url);
            Assert.False(string.IsNullOrWhiteSpace(weakness.Title));
        }
    }

    [Fact]
    public void TheJsonSaysWhichEntryAFindingIs()
    {
        var finding = new Finding
        {
            Kind = FindingKind.Logic,
            Severity = Severity.Warning,
            Confidence = Confidence.Likely,
            File = "shop.py",
            Line = 6,
            Title = "SQL built from what the user typed",
            Explanation = "explanation",
            WhyItMatters = "why",
            SuggestedFix = "fix",
            CorrectedExample = "",
            RuleId = "analysis-sql-injection",
        };

        using var read = JsonDocument.Parse(DiagnosticLines.Json([finding]));
        Assert.Equal("CWE-89", read.RootElement[0].GetProperty("cwe").GetString());
    }
}
