using FixFinder.Core.Checking;
using FixFinder.Core.Engine;
using FixFinder.Core.Reporting;

namespace FixFinder.Tests;

/// <summary>
/// The report as a page to keep: everything the window shows of a finding, in the explanation depth chosen, with whatever
/// came from the program or the check shown as text - never run - and nothing fetched from anywhere.
/// </summary>
public class ReportPageTests
{
    private static Finding Found(string title, Severity severity = Severity.Error, FindingKind kind = FindingKind.Syntax) => new()
    {
        Kind = kind,
        Severity = severity,
        Confidence = Confidence.Certain,
        File = @"C:\coursework\Marks.java",
        Line = 7,
        Title = title,
        Explanation = "The student wording, with `total` in it.",
        WhyItMatters = "Nothing runs.",
        SuggestedFix = "Put a semicolon at the end.",
        CorrectedExample = "int total = 0;",
        ExampleIsFromYourCode = true,
    };

    private static ReportPage Page(IReadOnlyList<Finding> findings, CheckComparison? since = null, IReadOnlyList<string>? output = null) => new()
    {
        Program = "Marks.java",
        Language = "Java",
        CheckedAt = new DateTimeOffset(2026, 10, 3, 14, 5, 0, TimeSpan.Zero),
        Level = ExplanationLevel.Student,
        HowItRan = "Building it with the javac of Java 25.0.4.1 (Eclipse's own Java), then running it with java.",
        SyntaxSummary = "1 error stops it building",
        LogicSummary = "No logic mistakes found",
        Notes = ["A note about <the run>."],
        Findings = findings,
        SinceLastCheck = since,
        Output = output ?? [],
    };

    [Fact]
    public void EveryPartOfTheReportIsOnThePage()
    {
        var html = Page([Found("';' expected"), Found("A loop that does more work than it needs", Severity.Suggestion, FindingKind.Performance)]).ToHtml();

        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("<title>FixFinder report - Marks.java</title>", html, StringComparison.Ordinal);
        Assert.Contains("Java  ·  checked 3 October 2026 at 14:05  ·  explained for a student", html, StringComparison.Ordinal);
        Assert.Contains("1 error stops it building", html, StringComparison.Ordinal);
        Assert.Contains("Building it with the javac of Java 25.0.4.1", html, StringComparison.Ordinal);
        Assert.Contains("1 error, no warnings and no suggestions; 1 way to do less work", html, StringComparison.Ordinal);
        Assert.Contains("<h3>&#39;;&#39; expected</h3>", html, StringComparison.Ordinal);
        Assert.Contains("<h4>Corrected code, from your file</h4>", html, StringComparison.Ordinal);
        Assert.Contains("Marks.java  ·  line 7", html, StringComparison.Ordinal);
        Assert.Contains("<h2>Efficiency <span class=\"count\">1</span></h2>", html, StringComparison.Ordinal);
    }

    /// <summary>A program's text that looks like HTML - a title or a line of output - is shown as it is, never taken for the page's own.</summary>
    [Fact]
    public void WhatCameFromTheProgramIsShownAsTextNeverRun()
    {
        var html = Page([Found("<script>alert('x')</script>")], output: ["<img src=x onerror=alert(1)>"]).ToHtml();

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("A note about &lt;the run&gt;.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageFetchesNothing()
    {
        var html = Page([Found("';' expected")]).ToHtml();

        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" src=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url(", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WhatIsBetweenBackticksIsShownAsCode()
    {
        Assert.Equal("Use <code>total</code> here", ReportPage.Prose("Use `total` here"));
        Assert.Equal("An odd ` backtick stays", ReportPage.Prose("An odd ` backtick stays"));
    }

    /// <summary>Only a web page is linked: an address of any other kind is shown as text, not something to click.</summary>
    [Fact]
    public void OnlyWebAddressesBecomeLinks()
    {
        Assert.Equal("<a href=\"https://docs.oracle.com/javase/\" rel=\"noopener noreferrer\">Java&#39;s documentation</a>",
            ReportPage.Link("Java's documentation", "https://docs.oracle.com/javase/"));
        Assert.Equal("Run me", ReportPage.Link("Run me", "javascript:alert(1)"));
        Assert.Equal("Open me", ReportPage.Link("Open me", @"file:///C:/Windows/System32/calc.exe"));
    }

    [Fact]
    public void TheExplanationIsInTheDepthChosen()
    {
        var finding = Found("';' expected") with
        {
            Explanations = Explained.Of("The student wording.", beginner: "The beginner wording.", technical: "The technical wording."),
        };

        var page = Page([finding]);

        Assert.Contains("The beginner wording.", (page with { Level = ExplanationLevel.Beginner }).ToHtml(), StringComparison.Ordinal);
        Assert.Contains("The technical wording.", (page with { Level = ExplanationLevel.Technical }).ToHtml(), StringComparison.Ordinal);
        Assert.DoesNotContain("The beginner wording.", page.ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheComparisonWithTheLastCheckIsOnThePage()
    {
        var stillThere = Found("';' expected");
        var fixedOne = Found("cannot find symbol");
        var since = new CheckComparison(new DateTimeOffset(2026, 10, 3, 13, 55, 0, TimeSpan.Zero),
            new Dictionary<string, SinceLastCheck> { [stillThere.Id] = SinceLastCheck.StillThere }, 1, 0, [fixedOne]);

        var html = Page([stillThere], since).ToHtml();

        Assert.Contains("Since the last check, 10 minutes ago: 1 fixed, 0 new and 1 still there.", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tag still\">Still there</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tag fixed\">Fixed</span> cannot find symbol", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongOutputIsCutAndSaysSo()
    {
        var output = Enumerable.Range(1, ReportPage.MostOutputLines + 50).Select(number => $"line {number}").ToList();

        var html = Page([], output: output).ToHtml();

        Assert.Contains($"line {ReportPage.MostOutputLines}</pre>", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"line {ReportPage.MostOutputLines + 1}", html, StringComparison.Ordinal);
        Assert.Contains($"The first {ReportPage.MostOutputLines} of the {ReportPage.MostOutputLines + 50} lines it printed.", html, StringComparison.Ordinal);
    }
}
