using System.Reflection;
using System.Text;
using FixFinder.Core.Checking;
using FixFinder.Core.Engine;

namespace FixFinder.Core.Reporting;

/// <summary>
/// A check's report as one web page, to keep, print or hand in: what was checked and how, the comparison with the last
/// check, the notes, and every finding with all the window shows of it.
/// </summary>
/// <remarks>
/// The page stands on its own: its style is in it, it runs no script and fetches nothing, so it opens the same on any
/// computer, offline, and shows nothing it was not written with. Everything taken from the program or the check is
/// escaped as text, so code that happens to look like HTML is shown, never run.
/// </remarks>
public sealed record ReportPage
{
    /// <summary>The program as the reader knows it: the file they chose.</summary>
    public required string Program { get; init; }

    public required string Language { get; init; }

    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>How it was built and run, as the check explained it, or null for code that was only read.</summary>
    public string? HowItRan { get; init; }

    public required string SyntaxSummary { get; init; }

    public required string LogicSummary { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = [];

    public required IReadOnlyList<Finding> Findings { get; init; }

    /// <summary>This check beside the last check of the same program, when there was one.</summary>
    public CheckComparison? SinceLastCheck { get; init; }

    /// <summary>What the program printed, line by line, as the window showed it.</summary>
    public IReadOnlyList<string> Output { get; init; } = [];

    /// <summary>More printed lines than this, and the page keeps the first of them and says how many there were.</summary>
    public const int MostOutputLines = 400;

    /// <summary>A name to save the page as: "Marks.java - FixFinder report.html".</summary>
    public string SuggestedFileName => $"{Program} - FixFinder report.html";

    public string ToHtml()
    {
        var page = new StringBuilder();
        var problems = Findings.Where(finding => finding.Kind != FindingKind.Performance).ToList();
        var efficiency = Findings.Where(finding => finding.Kind == FindingKind.Performance).ToList();
        var links = FindingLinks.Of(Findings);

        page.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<meta name=\"generator\" content=\"FixFinder ").Append(Text(Version)).Append("\">\n")
            .Append("<title>").Append(Text($"FixFinder report - {Program}")).Append("</title>\n")
            .Append("<style>").Append(Style).Append("</style>\n</head>\n<body>\n<main>\n");

        page.Append("<header>\n<p class=\"brand\">FixFinder report</p>\n<h1>").Append(Text(Program)).Append("</h1>\n")
            .Append("<p class=\"meta\">").Append(Text($"{Language}  ·  checked {CheckedAt:d MMMM yyyy} at {CheckedAt:HH:mm}"))
            .Append("</p>\n</header>\n");

        page.Append("<section class=\"summary\">\n<h2>Summary</h2>\n<dl>\n")
            .Append("<dt>Syntax</dt><dd>").Append(Prose(SyntaxSummary)).Append("</dd>\n")
            .Append("<dt>Logic</dt><dd>").Append(Prose(LogicSummary)).Append("</dd>\n");
        if (HowItRan is { Length: > 0 } how) page.Append("<dt>How it was checked</dt><dd>").Append(Prose(how)).Append("</dd>\n");
        page.Append("<dt>Found</dt><dd>").Append(Text(Counted(problems, efficiency.Count))).Append("</dd>\n</dl>\n</section>\n");

        if (SinceLastCheck is { } comparison) AppendComparison(page, comparison);

        if (Notes.Count > 0)
        {
            page.Append("<section class=\"notes\">\n<h2>Notes</h2>\n<ul>\n");
            foreach (var note in Notes) page.Append("<li>").Append(Prose(note)).Append("</li>\n");
            page.Append("</ul>\n</section>\n");
        }

        page.Append("<section class=\"findings\">\n<h2>Problems <span class=\"count\">").Append(problems.Count).Append("</span></h2>\n");
        if (problems.Count == 0) page.Append("<p class=\"empty\">No problems found: nothing in the code looks like a mistake.</p>\n");
        foreach (var finding in problems) AppendFinding(page, finding, links);
        page.Append("</section>\n");

        if (efficiency.Count > 0)
        {
            page.Append("<section class=\"findings\">\n<h2>Efficiency <span class=\"count\">").Append(efficiency.Count).Append("</span></h2>\n")
                .Append("<p class=\"intro\">Ways the program could do less work as its data grows. None of them is a mistake.</p>\n");
            foreach (var finding in efficiency) AppendFinding(page, finding, links);
            page.Append("</section>\n");
        }

        if (Output.Count > 0)
        {
            page.Append("<section class=\"output\">\n<h2>What it printed</h2>\n<pre>");
            page.Append(Text(string.Join("\n", Output.Take(MostOutputLines))));
            page.Append("</pre>\n");
            if (Output.Count > MostOutputLines) page.Append("<p class=\"hint\">").Append(Text($"The first {MostOutputLines} of the {Output.Count} lines it printed.")).Append("</p>\n");
            page.Append("</section>\n");
        }

        page.Append("<footer>\n<p>").Append(Text($"Made by FixFinder {Version}. There is no AI in it: every finding comes from a compiler, a run of " +
                                                  "the program, a rule or an analysis, as each one says. FixFinder changes no files - a fix is for you to make."))
            .Append("</p>\n</footer>\n</main>\n</body>\n</html>\n");

        return page.ToString();
    }

    private void AppendComparison(StringBuilder page, CheckComparison comparison)
    {
        page.Append("<section class=\"since\">\n<h2>Since the last check</h2>\n<p>").Append(Text(comparison.Summary(CheckedAt))).Append("</p>\n");

        if (comparison.Fixed.Count > 0)
        {
            page.Append("<ul class=\"fixed\">\n");
            foreach (var finding in comparison.Fixed)
                page.Append("<li><span class=\"tag fixed\">Fixed</span> ").Append(Prose(finding.Title))
                    .Append(" <span class=\"where\">").Append(Text(FindingText.Location(finding))).Append("</span></li>\n");
            page.Append("</ul>\n");
        }

        page.Append("</section>\n");
    }

    private void AppendFinding(StringBuilder page, Finding finding, FindingLinks links)
    {
        var severity = finding.Severity.ToString().ToLowerInvariant();

        page.Append("<article class=\"finding ").Append(severity).Append("\">\n<p class=\"tags\">")
            .Append("<span class=\"tag ").Append(severity).Append("\">").Append(Text(finding.Severity.ToString())).Append("</span> ")
            .Append("<span class=\"tag\">").Append(Text(finding.Confidence.ToString())).Append("</span> ")
            .Append("<span class=\"tag\">").Append(Text(FindingText.Kind(finding))).Append("</span>");

        if (SinceLastCheck?.StatusOf(finding) is { } status)
            page.Append(" <span class=\"tag ").Append(status == Engine.SinceLastCheck.New ? "new\">New since the last check" : "still\">Still there").Append("</span>");

        // In the order the window's card has them, so the page reads as the window does.
        page.Append(" <span class=\"where\">").Append(Text(FindingText.Location(finding))).Append("</span></p>\n")
            .Append("<h3>").Append(Prose(finding.Title)).Append("</h3>\n");

        foreach (var said in new[] { FindingText.FoundBy(finding), FindingText.Witness(finding), FindingText.Confirmation(finding) })
            if (said is not null) page.Append("<p class=\"found\">").Append(Prose(said)).Append("</p>\n");

        Section(page, "What is wrong", finding.Explanation);

        if (links.LeadsTo(finding) is { Count: > 0 } consequences) page.Append("<p class=\"relation\">").Append(Prose(FindingText.Explains(consequences))).Append("</p>\n");
        if (links.FollowsFrom(finding) is { } cause) page.Append("<p class=\"relation\">").Append(Prose(FindingText.Follows(cause))).Append("</p>\n");

        Section(page, "Why FixFinder is this sure", Evidence.For(finding));
        Section(page, "Why it matters", finding.WhyItMatters);

        if (FindingText.Slice(finding) is { Length: > 0 } slice) page.Append("<h4>The lines that decide it</h4>\n<pre>").Append(Text(slice)).Append("</pre>\n");

        Section(page, "How to fix it", finding.SuggestedFix);

        if (finding.State is { Rows.Count: > 0 } state)
        {
            page.Append("<h4>").Append(Text(FindingText.StateHeading(finding))).Append("</h4>\n<table>\n<thead><tr><th>Time</th>");
            foreach (var column in state.Columns) page.Append("<th>").Append(Text(column)).Append("</th>");
            page.Append("</tr></thead>\n<tbody>\n");

            foreach (var row in state.Rows)
            {
                page.Append(row.IsWhereItFailed ? "<tr class=\"failed\">" : "<tr>").Append("<td>").Append(row.Number).Append("</td>");
                foreach (var value in row.Values) page.Append("<td><code>").Append(Text(value)).Append("</code></td>");
                page.Append("</tr>\n");
            }

            page.Append("</tbody>\n</table>\n");
            if (state.WasCut) page.Append("<p class=\"hint\">").Append(Text(state.CutNote)).Append("</p>\n");
        }

        if (FindingText.FurtherReading(finding) is { } reading && finding.FurtherReading?.Url is { } readingUrl)
            page.Append("<h4>Read more about this</h4>\n<p>").Append(Link(reading, readingUrl)).Append("</p>\n");
        if (FindingText.Weakness(finding) is { } weakness) page.Append("<p>").Append(Link(weakness, finding.Weakness!.Url)).Append("</p>\n");

        if (FindingText.Origin(finding) is { } origin)
        {
            page.Append("<h4>Where this fix came from</h4>\n<p>")
                .Append(finding.CameFrom is { HasLink: true, Url: { } originUrl } ? Link(origin, originUrl) : Prose(origin)).Append("</p>\n");
        }

        if (finding.Verified.WasTested)
        {
            page.Append("<h4>Verification</h4>\n<p class=\"verified\">").Append(Prose(finding.Verified.Summary)).Append("</p>\n<ul class=\"steps\">\n");
            foreach (var step in finding.Verified.Steps.OrderBy(step => step.Stage))
                page.Append("<li class=\"").Append(step.Result.ToString().ToLowerInvariant()).Append("\">")
                    .Append(Text(FindingText.Mark(step.Result))).Append(' ').Append(Prose(step.Detail)).Append("</li>\n");
            page.Append("</ul>\n");
        }

        if (finding.Change is { Lines.Count: > 0 } change)
        {
            page.Append("<h4>").Append(Text($"What changes  ·  {change.Summary}")).Append("</h4>\n<pre class=\"change\">");
            foreach (var line in change.Lines)
            {
                var (kind, mark) = line.Kind switch
                {
                    ChangeKind.Removed => ("removed", "-"),
                    ChangeKind.Added => ("added", "+"),
                    _ => ("context", " "),
                };

                page.Append("<span class=\"").Append(kind).Append("\">").Append(Text($"{line.Number,5} {mark} {line.Text}")).Append("</span>\n");
            }

            page.Append("</pre>\n");
        }

        if (finding.CorrectedExample.Trim().Length > 0)
        {
            page.Append("<h4>").Append(finding.ExampleIsFromYourCode ? "Corrected code, from your file" : "An example of corrected code").Append("</h4>\n")
                .Append("<pre>").Append(Text(finding.CorrectedExample)).Append("</pre>\n");
        }

        if (finding.FixChanges is { Count: > 0 } fixChanges)
        {
            page.Append("<h4>What the fix changes</h4>\n<ul>\n");
            foreach (var fixChange in fixChanges) page.Append("<li>").Append(Prose(fixChange)).Append("</li>\n");
            page.Append("</ul>\n");
        }

        if (finding.FixCheckedBy is { Length: > 0 } checkedBy) page.Append("<p class=\"checked\">").Append(Prose(checkedBy)).Append("</p>\n");

        page.Append("</article>\n");
    }

    private static void Section(StringBuilder page, string heading, string text)
    {
        if (text.Trim().Length == 0) return;
        page.Append("<h4>").Append(Text(heading)).Append("</h4>\n");
        foreach (var paragraph in text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            page.Append("<p>").Append(Prose(paragraph)).Append("</p>\n");
    }

    /// <summary>How many of each, as the window's tabs count them: "2 errors, 1 warning and no suggestions; 1 way to do less work".</summary>
    private static string Counted(IReadOnlyList<Finding> problems, int efficiency)
    {
        string Of(int count, string thing) => count switch { 0 => $"no {thing}s", 1 => $"1 {thing}", _ => $"{count} {thing}s" };

        var counts = $"{Of(problems.Count(f => f.Severity == Severity.Error), "error")}, {Of(problems.Count(f => f.Severity == Severity.Warning), "warning")} " +
                     $"and {Of(problems.Count(f => f.Severity == Severity.Suggestion), "suggestion")}";
        return efficiency == 0 ? counts : $"{counts}; {(efficiency == 1 ? "1 way" : $"{efficiency} ways")} to do less work";
    }


    /// <summary>
    /// Text made safe to show: the five characters that mean something in HTML are escaped, and nothing else - the page is
    /// UTF-8, so a · or an é is written as itself.
    /// </summary>
    private static string Text(string text) => new StringBuilder(text)
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;")
        .ToString();

    /// <summary>
    /// Text written as the findings write it, escaped, with what is between backticks shown as code - when the backticks
    /// pair up, as the window shows them.
    /// </summary>
    internal static string Prose(string text)
    {
        var parts = text.Split('`');
        if (parts.Length % 2 == 0) return Text(text);

        var written = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
            written.Append(i % 2 == 1 ? $"<code>{Text(parts[i])}</code>" : Text(parts[i]));
        return written.ToString();
    }

    /// <summary>A link, only to a web page: any other kind of address is shown as text, never made something to click.</summary>
    internal static string Link(string text, string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? $"<a href=\"{Text(uri.AbsoluteUri)}\" rel=\"noopener noreferrer\">{Prose(text)}</a>"
            : Prose(text);

    private static string Version =>
        typeof(ReportPage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    /// <summary>FixFinder's own colours, light and dark, as its window has them; printed, always light.</summary>
    private const string Style = """

        :root { color-scheme: light dark; --page: #F3F5F9; --card: #FFFFFF; --subtle: #F7F9FC; --border: #E2E7EF; --text: #16202E;
          --secondary: #4F5B6E; --hint: #7A8599; --accent: #1A6DFF; --error: #D92D20; --error-soft: #FEF3F2; --warning: #C4570B;
          --warning-soft: #FFF5EB; --suggestion: #1A6DFF; --suggestion-soft: #EEF4FF; --success: #12823B; --success-soft: #ECFDF3; }
        @media (prefers-color-scheme: dark) {
          :root { --page: #11161F; --card: #1A212C; --subtle: #212A37; --border: #2C3646; --text: #E7ECF3; --secondary: #B2BDCD;
            --hint: #8493A8; --accent: #5B9BFF; --error: #FF8A80; --error-soft: #2E1A1C; --warning: #F4B267; --warning-soft: #2C2318;
            --suggestion: #5B9BFF; --suggestion-soft: #1B2942; --success: #6FD18F; --success-soft: #16281F; }
        }
        * { box-sizing: border-box; }
        body { margin: 0; background: var(--page); color: var(--text); font: 15px/1.55 "Segoe UI", system-ui, sans-serif; }
        main { max-width: 900px; margin: 0 auto; padding: 32px 20px 48px; }
        h1 { font-size: 28px; margin: 2px 0 4px; word-break: break-word; }
        h2 { font-size: 19px; margin: 0 0 12px; }
        h2 .count { color: var(--hint); font-weight: normal; }
        h3 { font-size: 17px; margin: 6px 0 4px; }
        h4 { font-size: 13px; text-transform: uppercase; letter-spacing: .04em; color: var(--secondary); margin: 16px 0 4px; }
        p { margin: 4px 0; }
        .brand { color: var(--accent); font-weight: 600; margin: 0; }
        .meta, .hint, .intro, .where { color: var(--secondary); }
        .hint { font-size: 13px; }
        section { background: var(--card); border: 1px solid var(--border); border-radius: 10px; padding: 18px 20px; margin: 16px 0; }
        section.findings { background: transparent; border: none; padding: 0; }
        dl { display: grid; grid-template-columns: max-content 1fr; gap: 6px 16px; margin: 0; }
        dt { color: var(--secondary); }
        dd { margin: 0; }
        ul { margin: 6px 0; padding-left: 22px; }
        article { background: var(--card); border: 1px solid var(--border); border-left: 4px solid var(--border); border-radius: 10px;
          padding: 14px 18px; margin: 0 0 14px; break-inside: avoid; }
        article.error { border-left-color: var(--error); }
        article.warning { border-left-color: var(--warning); }
        article.suggestion { border-left-color: var(--suggestion); }
        .tags { font-size: 12.5px; margin: 0; }
        .tag { display: inline-block; padding: 1px 8px; border-radius: 999px; background: var(--subtle); border: 1px solid var(--border); margin-right: 2px; }
        .tag.error { color: var(--error); background: var(--error-soft); }
        .tag.warning { color: var(--warning); background: var(--warning-soft); }
        .tag.suggestion { color: var(--suggestion); background: var(--suggestion-soft); }
        .tag.new { color: var(--warning); background: var(--warning-soft); }
        .tag.still { color: var(--secondary); }
        .tag.fixed { color: var(--success); background: var(--success-soft); }
        .relation, .found { font-style: italic; color: var(--secondary); }
        .checked { background: var(--success-soft); border: 1px solid var(--border); border-radius: 8px; padding: 7px 10px; margin-top: 12px; }
        code, pre { font-family: "Cascadia Mono", Consolas, monospace; font-size: 13px; }
        code { background: var(--subtle); border-radius: 4px; padding: 0 4px; }
        pre { background: var(--subtle); border: 1px solid var(--border); border-radius: 8px; padding: 10px 12px; overflow-x: auto; white-space: pre-wrap; word-break: break-word; }
        pre.change .removed { color: var(--error); }
        pre.change .added { color: var(--success); }
        pre.change .context { color: var(--secondary); }
        table { border-collapse: collapse; font-size: 13px; margin: 4px 0; }
        th, td { border: 1px solid var(--border); padding: 3px 10px; text-align: left; }
        tr.failed td { background: var(--error-soft); }
        .steps { list-style: none; padding-left: 4px; }
        .steps .passed { color: var(--success); }
        .steps .failed { color: var(--error); }
        .verified { font-weight: 600; }
        a { color: var(--accent); }
        footer { color: var(--hint); font-size: 13px; margin-top: 24px; }
        @media print {
          :root { color-scheme: light; --page: #FFFFFF; --card: #FFFFFF; --subtle: #F7F9FC; --border: #D5DCE6; --text: #000000; --secondary: #333B47;
            --hint: #555E6B; --accent: #1A4FB3; --error: #B42318; --warning: #9A4508; --suggestion: #1A4FB3; --success: #0E6B30; }
          main { max-width: none; padding: 0; }
          section, article { break-inside: avoid; }
        }

        """;
}
