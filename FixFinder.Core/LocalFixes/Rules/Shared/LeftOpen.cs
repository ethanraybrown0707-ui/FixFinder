namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// Blocks and text left open, found as a program is laid out - for the languages whose rules close them: which { is never
/// closed, where its } goes, and where a line's text ends.
/// </summary>
internal static class LeftOpen
{
    /// <summary>
    /// The lines whose { is never closed, in order. Each } at the start of a line closes the open { at its own indentation - the
    /// line that opened it is indented as it is - so the { inside it still open are the ones never closed; a } at no open {'s
    /// indentation closes the latest. A line opens a block when its code ends with {.
    /// </summary>
    public static List<int> NeverClosedBraces(IReadOnlyList<string> masked)
    {
        var open = new List<int>();
        var neverClosed = new List<int>();

        for (var index = 0; index < masked.Count; index++)
        {
            var code = masked[index].Trim();

            if (code.StartsWith('}') && open.Count > 0)
            {
                var indent = CodeText.Indentation(masked[index]).Length;
                var closes = open.FindLastIndex(opener => CodeText.Indentation(masked[opener]).Length == indent);
                if (closes < 0) closes = open.Count - 1;

                neverClosed.AddRange(open.Skip(closes + 1));
                open.RemoveRange(closes, open.Count - closes);
            }

            if (code.EndsWith('{')) open.Add(index);
        }

        return [.. neverClosed.Concat(open).Order()];
    }

    /// <summary>
    /// Where the block opened on <paramref name="opener"/> ends by its indentation: after the last line indented further than it,
    /// before the first line - not blank - that is not. The line number, from 1, that its close goes before.
    /// </summary>
    public static int BlockEndsBefore(IReadOnlyList<string> lines, IReadOnlyList<string> masked, int opener)
    {
        var indent = CodeText.Indentation(lines[opener]).Length;
        var back = Enumerable.Range(opener + 1, Math.Max(0, lines.Count - opener - 1))
            .FirstOrDefault(index => masked[index].Trim().Length > 0 && CodeText.Indentation(lines[index]).Length <= indent, -1);

        var last = (back < 0 ? lines.Count : back) - 1;
        while (last > opener && lines[last].Trim().Length == 0) last--;
        return last + 2;
    }

    /// <summary>
    /// The line with its text closed, when a quote on it opens text that the line does not close: the same quote at the line's
    /// end - before the ) of each bracket still open before the quote, when the line ends with them. Null when every quote on it
    /// closes. <paramref name="comment"/> outside text starts a comment - # in Ruby and PowerShell, -- in Lua; <paramref name="escape"/>
    /// - a backslash, or PowerShell's backtick - takes the character after it as it is.
    /// </summary>
    public static string? ClosedText(string line, Syntax syntax, char escape, string comment = "#")
    {
        var quote = '\0';
        var opened = -1;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];

            if (quote == '\0')
            {
                if (string.CompareOrdinal(line, index, comment, 0, comment.Length) == 0) return null;
                if (character is '"' or '\'')
                {
                    quote = character;
                    opened = index;
                }

                continue;
            }

            if (character == escape) index++;
            else if (character == quote) quote = '\0';
        }

        if (quote == '\0') return null;

        var brackets = CodeText.Mask(line[..opened], syntax);
        var open = brackets.Count(character => character == '(') - brackets.Count(character => character == ')');
        var text = line.TrimEnd();
        var closeAt = text.Length;

        for (var bracket = 0; bracket < open && closeAt > opened + 1 && text[closeAt - 1] == ')'; bracket++) closeAt--;
        return text[..closeAt] + quote + text[closeAt..];
    }
}
