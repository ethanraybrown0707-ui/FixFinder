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
        Assert.Contains("Java  ·  checked 3 October 2026 at 14:05</p>", html, StringComparison.Ordinal);
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
    public void TheExplanationIsTheOneTheWindowShows()
    {
        var finding = Found("';' expected") with { Explanation = "The beginner wording." };

        Assert.Contains("The beginner wording.", Page([finding]).ToHtml(), StringComparison.Ordinal);
    }

    /// <summary>The report's layout: summary, cause, most likely fix, the exact code, why FixFinder thinks this, then learn more.</summary>
    [Fact]
    public void AFindingReadsThisProgramFirstAndTheLanguageAfter()
    {
        var traced = Found("member is None on line 10 - it was made None on line 9", kind: FindingKind.Runtime) with
        {
            Found = "`member` became None on line 9.",
            Explanation = "`member` became None on line 9. Square brackets pick one item out of a list.",
            RootCause = new RootCause
            {
                Name = "member",
                Value = "None",
                CrashLine = 10,
                IntroducedLine = 9,
                IntroducedCode = "member = find_member(members, 2)",
                Steps = ["`member` became None on line 9."],
                Evidence = ["`member` held None when line 10 ran."],
                LinesInvolved = [9],
                WhatToDo = "Decide what `find_member` should give back.",
            },
        };

        var html = Page([traced]).ToHtml();
        var order = new[]
        {
            "<p class=\"summary\">Crash on line 10. Root cause introduced on line 9.</p>",
            "<h4>Cause</h4>\n<p><code>member</code> became None on line 9.</p>",
            "<h4>Most likely fix</h4>\n<p>Decide what <code>find_member</code> should give back.</p>",
            "<h4>Why FixFinder thinks this</h4>",
            "<li class=\"passed\">✓ <code>member</code> held None when line 10 ran.</li>",
            "<h4 class=\"part\">Learn more</h4>",
            "<h4>What this kind of mistake is</h4>\n<p>Square brackets pick one item out of a list.</p>",
            "<h4>Why it matters</h4>",
        };

        var at = order.Select(part => html.IndexOf(part, StringComparison.Ordinal)).ToList();
        Assert.All(at.Zip(order), each => Assert.True(each.First >= 0, $"not on the page: {each.Second}"));
        Assert.Equal(at.OrderBy(index => index), at);
    }

    /// <summary>One fold to a variable, each value with the line that gave it - the one the program stopped on open - and no script.</summary>
    [Fact]
    public void EachVariableOfTheTimelineOpensToItsValues()
    {
        var stopped = Found("best is None on line 10", kind: FindingKind.Runtime) with
        {
            RootCause = new RootCause
            {
                Name = "best",
                Value = "None",
                CrashLine = 10,
                IntroducedLine = 8,
                IntroducedCode = "best = None",
                Steps = ["`best` was set to None on line 8."],
                Evidence = ["`best` held None when line 10 ran."],
                LinesInvolved = [8],
                WhatToDo = "Give `best` a value.",
            },
            Timeline =
            [
                new VariableHistory("total", [new VariableChange(2, "0", null), new VariableChange(5, "55", null)], Cut: false),
                new VariableHistory("best", [new VariableChange(8, "None", null)], Cut: false),
                new VariableHistory("people", [new VariableChange(null, "0", 8)], Cut: false),
            ],
        };

        var html = Page([stopped]).ToHtml();

        Assert.Contains("<h4>Variable timeline</h4>", html, StringComparison.Ordinal);
        Assert.Contains("<details class=\"timeline\"><summary><code>total</code> - 2 values</summary>", html, StringComparison.Ordinal);
        Assert.Contains("<tr><td>line 5</td><td><code>55</code></td></tr>", html, StringComparison.Ordinal);
        Assert.Contains("<details class=\"timeline\" open><summary><code>best</code> - 1 value</summary>", html, StringComparison.Ordinal);
        Assert.Contains("<tr><td>the call on line 8</td><td><code>0</code></td></tr>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
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
