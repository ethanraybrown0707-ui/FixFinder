using FixFinder.Core.Checking;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Reporting;

/// <summary>
/// A finding's parts in the words a reader takes them in - where it is, how it was found, what follows from it, where its
/// fix came from - the same in the window and in a saved report, so the two never say different things.
/// </summary>
public static class FindingText
{
    public static string Kind(Finding finding) => finding.Kind switch
    {
        FindingKind.Syntax => "Syntax",
        FindingKind.Runtime => "Runtime",
        FindingKind.Logic => "Logic",
        FindingKind.Performance => "Efficiency",
        _ => "Style",
    };

    /// <summary>The file the finding is in: for a notebook's code, the notebook, not the script FixFinder checked it as.</summary>
    public static string FileName(Finding finding) => Path.GetFileName(finding.InNotebook?.Notebook ?? finding.File);

    /// <summary>The line on its own: "Line 7", or "Cell 2, line 3" in a notebook; nothing for a finding about the whole file.</summary>
    public static string Line(Finding finding) => finding.InNotebook is { } place ? $"Cell {place.Cell}, line {place.Line}"
        : finding.Line is { } line ? $"Line {line}"
        : "";

    /// <summary>The file and the line: "Main.java  ·  line 7".</summary>
    public static string Location(Finding finding) => finding.InNotebook is { } place ? $"{FileName(finding)}  ·  cell {place.Cell}, line {place.Line}"
        : finding.Line is { } line ? $"{FileName(finding)}  ·  line {line}"
        : FileName(finding);

    /// <summary>Where a finding is, as the reader would look for it: a line of the file, or a line of a notebook's cell.</summary>
    public static string Where(Finding finding) => finding.InNotebook is { } place
        ? $"in cell {place.Cell} on line {place.Line}"
        : $"on line {finding.Line}";

    public static string Follows(Finding cause) => $"Follows from the problem {Where(cause)} - fixing that one should remove this.";

    public static string Explains(IReadOnlyList<Finding> consequences) => consequences.Count switch
    {
        0 => "",
        1 => $"The problem {Where(consequences[0])} looks like a consequence of this one, so fixing this may remove it too.",
        _ when consequences.All(consequence => consequence.InNotebook is null) =>
            $"The problems on lines {string.Join(", ", consequences.SkipLast(1).Select(consequence => consequence.Line))} and {consequences[^1].Line} " +
            "look like consequences of this one, so fixing this may remove them too.",
        _ => $"The problems {string.Join(", ", consequences.SkipLast(1).Select(Where))} and {Where(consequences[^1])} " +
             "look like consequences of this one, so fixing this may remove them too.",
    };

    public static string? FoundBy(Finding finding) => finding.FoundBy is { } technique ? $"Found by {technique}" : null;

    public static string? Witness(Finding finding) => finding.Witness is { } witness ? $"Fails when {witness}" : null;

    public static string? Confirmation(Finding finding) => finding.Confirmation is { } ran ? $"Confirmed: {ran}" : null;

    /// <summary>The heading of the table of what a line did: "What line 7 did, each time it ran".</summary>
    public static string StateHeading(Finding finding) => finding.State is not { } state ? ""
        : finding.InNotebook is { } place ? $"What line {state.Line} of cell {place.Cell} did, each time it ran"
        : $"What line {state.Line} did, each time it ran";

    public static string? FurtherReading(Finding finding) =>
        finding.FurtherReading is { } reading ? $"Search {reading.SiteName} for {reading.Term}" : null;

    public static string? Weakness(Finding finding) => finding.Weakness is { } weakness ? $"CWE-{weakness.Id}: {weakness.Title}" : null;

    /// <summary>Where the fix came from, so it can be checked rather than taken on trust; nothing when there is no fix.</summary>
    public static string? Origin(Finding finding) => finding.CameFrom is { } came && finding.Fix is not null
        ? came.HasLink ? $"Taken from {came.SourceName}: {came.Title}"
        : came.IsTheLanguagesOwn ? $"Suggested by {came.SourceName} itself, in {came.Title}"
        : $"Worked out by FixFinder's own rule `{came.Title}`"
        : null;

    /// <summary>A tested stage's mark: passed, failed, could not tell, or not reached.</summary>
    public static string Mark(StageResult result) => result switch
    {
        StageResult.Passed => "✓",
        StageResult.Failed => "✕",
        StageResult.Inconclusive => "?",
        _ => "–",
    };

    /// <summary>The lines that decide the value that goes wrong, numbered, with their shared indent taken off - or nothing.</summary>
    public static string Slice(Finding finding)
    {
        if (finding.Slice is not { Count: > 1 } lines) return "";

        var shown = LinesOf(finding, lines);
        if (shown.Count < 2) return "";

        var indent = shown.Where(s => s.Text.Length > 0).Select(s => s.Text.Length - s.Text.TrimStart().Length).DefaultIfEmpty(0).Min();
        var width = shown.Max(s => s.Number).ToString().Length;

        // In a notebook the lines are numbered in their cells, so a cell is named wherever the lines leave the finding's own.
        var namesCells = shown.Any(s => s.Cell != finding.InNotebook?.Cell);
        var rendered = new List<string>();
        int? cellNamed = null;

        foreach (var (cell, number, text) in shown)
        {
            if (namesCells && cell is { } current && current != cellNamed)
            {
                rendered.Add($"cell {current}");
                cellNamed = current;
            }

            rendered.Add($"{number.ToString().PadLeft(width)}  {(text.Length >= indent ? text[indent..] : text.TrimStart())}");
        }

        return string.Join("\n", rendered);
    }

    /// <summary>
    /// The slice's lines as the reader has them: numbered in the file - or, for a notebook's code, numbered in their cells
    /// and written as the notebook has them, without any line FixFinder put in itself to run the notebook.
    /// </summary>
    private static List<(int? Cell, int Number, string Text)> LinesOf(Finding finding, IReadOnlyList<int> lines)
    {
        if (finding.InNotebook is not null && NotebookScript.Of(finding.File) is { } notebook)
        {
            return [.. lines
                .Select(line => (Place: notebook.PlaceOf(line), Code: notebook.CodeOf(line)))
                .Where(line => line.Place is not null && line.Code is not null)
                .Select(line => ((int?)line.Place!.Cell, line.Place.Line, line.Code!.TrimEnd()))];
        }

        string[] source;
        try
        {
            source = File.ReadAllLines(finding.File);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }

        return [.. lines.Where(line => line >= 1 && line <= source.Length).Select(line => ((int?)null, line, source[line - 1].TrimEnd()))];
    }
}

/// <summary>
/// Which finding follows from which, as Core works it out on each finding's CausedBy: looked up across the whole report, the
/// one place every finding can be seen at once.
/// </summary>
public sealed class FindingLinks
{
    private readonly Dictionary<string, Finding> _byId;
    private readonly Dictionary<string, IReadOnlyList<Finding>> _followers;

    private FindingLinks(IReadOnlyList<Finding> findings)
    {
        _byId = findings.GroupBy(finding => finding.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        _followers = findings.Where(finding => finding.CausedBy is not null && finding.Line is > 0)
            .GroupBy(finding => finding.CausedBy!.RootId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Finding>)[.. group.OrderBy(finding => finding.Line)], StringComparer.Ordinal);
    }

    public static FindingLinks Of(IReadOnlyList<Finding> findings) => new(findings);

    /// <summary>The finding this one follows from, when it is in the report and has a line to point at.</summary>
    public Finding? FollowsFrom(Finding finding) =>
        finding.CausedBy is { } cause && _byId.TryGetValue(cause.RootId, out var root) && root.Line is not null ? root : null;

    /// <summary>The findings that follow from this one, in the order of their lines.</summary>
    public IReadOnlyList<Finding> LeadsTo(Finding finding) => _followers.GetValueOrDefault(finding.Id, []);
}
