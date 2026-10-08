using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Checking;

/// <summary>
/// A finding about a notebook's code, worded as the notebook is read: a line counted within its cell, as Jupyter counts
/// it; a line of another cell named with that cell; and the notebook named wherever the script its code was checked as
/// would be.
/// </summary>
/// <remarks>
/// Every check works on the script, so what one says of a line is said of the script's line. That is put right here, on
/// the way to the reader, rather than in each check. Only what is said of the code changes. What the program said when it
/// stopped - "Expecting value: line 1 column 1" from reading a JSON file - is about its data and stays as it was said, as
/// does code quoted between backticks, and a line of what the program printed, which is counted in its output.
/// </remarks>
public static partial class NotebookWording
{
    /// <summary>
    /// "line 12", "lines 4-6", "lines 3, 5 and 9", "line 3 or 7" - but not a place in data, which has a column after it,
    /// nor a line of printed output, which has what was printed after it.
    /// </summary>
    [GeneratedRegex(@"\b(?<word>[Ll]ines?) (?<numbers>\d+(?:(?:-|, | or | and )\d+)*)(?!\d)(?! ?,? column)(?!, ""| was ""| should have been "")")]
    private static partial Regex LineMention();

    [GeneratedRegex(@"\d+|-|, | or | and ")]
    private static partial Regex NumberOrJoin();

    [GeneratedRegex(@"`[^`\n]*`")]
    private static partial Regex QuotedCode();

    /// <summary>What a line of the script that is none of the notebook's is called, when something names one.</summary>
    private const string FixFindersOwnLine = "a line FixFinder added to run the notebook";

    /// <summary>
    /// The finding as the notebook is read. A finding in the notebook's code has every line it names put in cell terms;
    /// a finding in another of the program's files - a module the notebook imports - only where it names the script.
    /// </summary>
    public static Finding AsInTheNotebook(Finding finding, NotebookScript notebook)
    {
        var ownCell = finding.InNotebook?.Cell;
        var theProgramSaid = finding.Kind == FindingKind.Runtime && finding.Error?.Message is { Length: > 0 } message ? message : null;

        string Reworded(string text) => Reword(text, notebook, ownCell, theProgramSaid);

        var reworded = finding with
        {
            Title = Reworded(finding.Title),
            Explanation = Reworded(finding.Explanation),
            Found = finding.Found is { } found ? Reworded(found) : null,
            WhyItMatters = Reworded(finding.WhyItMatters),
            SuggestedFix = Reworded(finding.SuggestedFix),
            FixCheckedBy = finding.FixCheckedBy is { } checkedBy ? Reworded(checkedBy) : null,
            Witness = finding.Witness is { } witness ? Reworded(witness) : null,
            Confirmation = finding.Confirmation is { } confirmation ? Reworded(confirmation) : null,
            FixChanges = finding.FixChanges?.Select(Reworded).ToList(),
            Verified = finding.Verified.WasTested
                ? finding.Verified with { Steps = [.. finding.Verified.Steps.Select(step => step with { Detail = Reworded(step.Detail) })] }
                : finding.Verified,
        };

        if (ownCell is not { } cell) return reworded;

        return reworded with
        {
            // The table is of the finding's own line; one of a line in another cell could not be headed truthfully.
            State = finding.State is { } state && notebook.PlaceOf(state.Line) is { } stateAt && stateAt.Cell == cell
                ? state with { Line = stateAt.Line }
                : null,
            Change = finding.Change is { } change && finding.Fix is { } fix ? InItsCell(change, fix, notebook) : null,
        };
    }

    /// <summary>What is said, with each line of the script it names put as the notebook has it, and the notebook named for the script.</summary>
    private static string Reword(string text, NotebookScript notebook, int? ownCell, string? theProgramSaid)
    {
        if (text.Length == 0) return text;

        var scriptName = Path.GetFileName(notebook.Script);
        var kept = KeptAsSaid(text, theProgramSaid);

        var reworded = LineMention().Replace(text, mention =>
        {
            if (kept.Any(span => mention.Index < span.End && span.Start < mention.Index + mention.Length)) return mention.Value;

            var after = text.AsSpan(mention.Index + mention.Length);
            var ofTheScript = after.StartsWith(" of " + scriptName, StringComparison.OrdinalIgnoreCase);

            // A line of another file keeps its number, and so does a line a finding outside the notebook's code names
            // without a file, which is a line of the file that finding is in.
            if (!ofTheScript && (after.StartsWith(" of ", StringComparison.Ordinal) || ownCell is null)) return mention.Value;

            return InCells(mention, notebook, ownCell, alwaysNameTheCell: ofTheScript) ?? mention.Value;
        });

        return reworded
            .Replace(notebook.Script, notebook.Notebook, StringComparison.OrdinalIgnoreCase)
            .Replace(scriptName, Path.GetFileName(notebook.Notebook), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The lines one mention names, as lines of cells - or null when a number in it is no line of the script at all, and
    /// so not something to put in cell terms.
    /// </summary>
    private static string? InCells(Match mention, NotebookScript notebook, int? ownCell, bool alwaysNameTheCell)
    {
        var word = mention.Groups["word"].Value;
        var parts = NumberOrJoin().Matches(mention.Groups["numbers"].Value).Select(part => part.Value).ToList();
        var numbers = parts.Where((_, index) => index % 2 == 0).Select(int.Parse).ToList();
        var joins = parts.Where((_, index) => index % 2 == 1).ToList();

        if (numbers.Any(number => number < 1 || number > notebook.LineCount)) return null;

        var places = numbers.Select(notebook.PlaceOf).ToList();

        // All in one cell: the numbers become the cell's own, with the cell named unless it is the finding's.
        if (places.All(place => place is not null) && places.Select(place => place!.Cell).Distinct().Count() == 1)
        {
            var cell = places[0]!.Cell;
            var lines = string.Concat(places.Select((place, index) => (index > 0 ? joins[index - 1] : "") + place!.Line));

            return alwaysNameTheCell || cell != ownCell ? $"{word} {lines} of cell {cell}" : $"{word} {lines}";
        }

        // Lines of several cells: each named with its own cell.
        var named = new StringBuilder();

        for (var index = 0; index < places.Count; index++)
        {
            if (index > 0) named.Append(joins[index - 1] == "-" ? " to " : joins[index - 1]);

            named.Append(places[index] switch
            {
                null => FixFindersOwnLine,
                { } place when alwaysNameTheCell || place.Cell != ownCell => $"line {place.Line} of cell {place.Cell}",
                { } place => $"line {place.Line}",
            });
        }

        return char.IsUpper(word[0]) ? char.ToUpperInvariant(named[0]) + named.ToString(1, named.Length - 1) : named.ToString();
    }

    /// <summary>Stretches of what is said that are quoted rather than FixFinder's own words: code, and what the program said when it stopped.</summary>
    private static List<(int Start, int End)> KeptAsSaid(string text, string? theProgramSaid)
    {
        var kept = QuotedCode().Matches(text).Select(quoted => (quoted.Index, quoted.Index + quoted.Length)).ToList();

        if (theProgramSaid is not null)
        {
            for (var at = text.IndexOf(theProgramSaid, StringComparison.Ordinal); at >= 0;
                 at = text.IndexOf(theProgramSaid, at + theProgramSaid.Length, StringComparison.Ordinal))
            {
                kept.Add((at, at + theProgramSaid.Length));
            }
        }

        return kept;
    }

    /// <summary>
    /// A change as the cell it is in has it: its lines numbered in that cell, the lines around it that belong to another
    /// cell or to FixFinder left out, and each unchanged line written as the notebook has it. Null when the cell cannot be told.
    /// </summary>
    private static CodeChange? InItsCell(CodeChange change, LocalFix fix, NotebookScript notebook)
    {
        // A change that adds lines at the end of a cell starts on the blank line after it, which belongs to no cell.
        var start = notebook.PlaceOf(fix.StartLine) ?? (notebook.PlaceOf(fix.StartLine - 1) is { } last ? last with { Line = last.Line + 1 } : null);
        if (start is null) return null;

        var lines = new List<ChangeLine>();

        foreach (var line in change.Lines)
        {
            if (line.Kind == ChangeKind.Added)
            {
                lines.Add(line with { Number = start.Line + (line.Number - fix.StartLine) });
                continue;
            }

            if (notebook.PlaceOf(line.Number) is not { } place || place.Cell != start.Cell) continue;

            lines.Add(line.Kind == ChangeKind.Context && notebook.CodeOf(line.Number) is { } asInTheNotebook
                ? line with { Number = place.Line, Text = asInTheNotebook }
                : line with { Number = place.Line });
        }

        return change with { Lines = lines };
    }
}
