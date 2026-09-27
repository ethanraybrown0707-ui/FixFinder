using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Checking;

/// <summary>What one line of a shown change is: the code as it stands, code the fix takes out, or code it puts in.</summary>
public enum ChangeKind
{
    Context,
    Removed,
    Added,
}

/// <summary>
/// One line of a before-and-after, and the part of it that actually changed.
/// </summary>
/// <remarks>
/// A whole line coloured red against a whole line coloured green says "something here is different" and leaves the
/// reader to find it. <see cref="HighlightStart"/> and <see cref="HighlightLength"/> say where the difference is, so
/// `&lt;=` becoming `&lt;` can be shown as the one character it is.
/// </remarks>
public sealed record ChangeLine
{
    public required ChangeKind Kind { get; init; }

    /// <summary>The line's number in the file. Added lines carry the number they will have once the fix is applied.</summary>
    public required int Number { get; init; }

    public required string Text { get; init; }

    public int HighlightStart { get; init; }

    public int HighlightLength { get; init; }

    /// <summary>Whether only part of this line changed, rather than the whole of it.</summary>
    public bool HasHighlight => HighlightLength > 0;

    public string Before => HasHighlight ? Text[..HighlightStart] : Text;
    public string Changed => HasHighlight ? Text.Substring(HighlightStart, HighlightLength) : "";
    public string After => HasHighlight ? Text[(HighlightStart + HighlightLength)..] : "";
}

/// <summary>
/// The user's code beside the code a fix would leave, built from the fix itself rather than from anything said about it.
/// </summary>
/// <remarks>
/// A <see cref="LocalFix"/> is a structural edit - a file, a line to start at, how many lines to take out, and the lines
/// to put there - so what changed is not a matter of opinion and does not have to be inferred from an error message.
/// That also settles the honest case: where there is no fix there is nothing to show, and <see cref="From"/> returns
/// null rather than a plausible-looking guess at what the fix might have been.
/// </remarks>
public sealed record CodeChange
{
    /// <summary>How many unchanged lines are shown either side, so the change can be placed in the file.</summary>
    public const int Surrounding = 2;

    /// <summary>More lines than this and a reader wants it folded away until they ask for it.</summary>
    public const int LongEnoughToFold = 12;

    public required IReadOnlyList<ChangeLine> Lines { get; init; }

    public required string File { get; init; }

    public int RemovedCount => Lines.Count(l => l.Kind == ChangeKind.Removed);

    public int AddedCount => Lines.Count(l => l.Kind == ChangeKind.Added);

    public bool IsLong => Lines.Count > LongEnoughToFold;

    /// <summary>A one-line summary of the size of the change, for a header the reader can fold.</summary>
    public string Summary => (RemovedCount, AddedCount) switch
    {
        (0, 0) => "No change",
        (0, var added) => added == 1 ? "1 line added" : $"{added} lines added",
        (var removed, 0) => removed == 1 ? "1 line removed" : $"{removed} lines removed",
        (1, 1) => "1 line changed",
        var (removed, added) => $"{removed} lines replaced by {added}",
    };

    /// <summary>
    /// The change a fix makes to a file, or null when there is nothing real to show.
    /// </summary>
    public static CodeChange? From(LocalFix fix, SourceFile? source)
    {
        if (source is null) return null;

        var removed = Enumerable.Range(fix.StartLine, fix.RemoveCount)
            .Select(number => (Number: number, Text: source.Line(number)))
            .Where(line => line.Text is not null)
            .Select(line => (line.Number, Text: line.Text!))
            .ToList();

        var added = fix.NewLines;

        // A fix that removes nothing and adds nothing is not a change anybody needs to see, and neither is one whose
        // lines are already exactly what the file says - which happens when a rule offers the line it just read.
        if (removed.Count == 0 && added.Count == 0) return null;
        if (removed.Count == added.Count && removed.Select(r => r.Text).SequenceEqual(added)) return null;

        var lines = new List<ChangeLine>();

        for (var number = Math.Max(1, fix.StartLine - Surrounding); number < fix.StartLine; number++)
        {
            if (source.Line(number) is { } text) lines.Add(new ChangeLine { Kind = ChangeKind.Context, Number = number, Text = text });
        }

        // The common case is one line becoming one line, and there the exact characters that differ can be marked.
        // Anything larger is shown line by line: the parts that line up are not reliably the parts that mean the same.
        var (removedMark, addedMark) = removed.Count == 1 && added.Count == 1
            ? Difference(removed[0].Text, added[0])
            : ((Start: 0, Length: 0), (Start: 0, Length: 0));

        foreach (var (number, text) in removed)
        {
            lines.Add(new ChangeLine
            {
                Kind = ChangeKind.Removed,
                Number = number,
                Text = text,
                HighlightStart = removedMark.Start,
                HighlightLength = removedMark.Length,
            });
        }

        for (var index = 0; index < added.Count; index++)
        {
            lines.Add(new ChangeLine
            {
                Kind = ChangeKind.Added,
                Number = fix.StartLine + index,
                Text = added[index],
                HighlightStart = addedMark.Start,
                HighlightLength = addedMark.Length,
            });
        }

        var after = fix.StartLine + fix.RemoveCount;

        for (var number = after; number < after + Surrounding; number++)
        {
            if (source.Line(number) is { } text) lines.Add(new ChangeLine { Kind = ChangeKind.Context, Number = number, Text = text });
        }

        return new CodeChange { Lines = lines, File = fix.File };
    }

    /// <summary>
    /// Where two versions of one line differ: what is the same at the start and at the end is stripped away, and what
    /// is left in the middle is the change. Where that lands inside a word the whole word is taken, because half an
    /// identifier marked out reads as a typo in the marking rather than as the edit.
    /// </summary>
    private static ((int Start, int Length) Removed, (int Start, int Length) Added) Difference(string before, string after)
    {
        var start = 0;
        while (start < before.Length && start < after.Length && before[start] == after[start]) start++;

        var tail = 0;
        while (tail < before.Length - start && tail < after.Length - start &&
               before[before.Length - tail - 1] == after[after.Length - tail - 1]) tail++;

        while (start > 0 && (IsWord(Char(before, start - 1)) && IsWord(Char(before, start)) ||
                             IsWord(Char(after, start - 1)) && IsWord(Char(after, start)))) start--;

        while (tail > 0 &&
               (IsWord(Char(before, before.Length - tail)) && IsWord(Char(before, before.Length - tail - 1)) ||
                IsWord(Char(after, after.Length - tail)) && IsWord(Char(after, after.Length - tail - 1)))) tail--;

        var removedLength = before.Length - tail - start;
        var addedLength = after.Length - tail - start;

        // Nothing in common at all: marking the whole of both lines says no more than colouring them does.
        if (removedLength >= before.Length && addedLength >= after.Length) return ((0, 0), (0, 0));

        return ((start, Math.Max(0, removedLength)), (start, Math.Max(0, addedLength)));
    }

    private static char Char(string text, int index) => index >= 0 && index < text.Length ? text[index] : ' ';

    private static bool IsWord(char character) => char.IsLetterOrDigit(character) || character == '_';
}
