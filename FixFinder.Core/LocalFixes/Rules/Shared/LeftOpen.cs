using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// Blocks, brackets and text left open, found as a program is laid out - for the languages whose rules close them: which { is
/// never closed, where its } goes, where a line's text ends, and where each of its brackets was meant to close.
/// </summary>
internal static partial class LeftOpen
{
    /// <summary>How one language writes what is closed: its quotes, where its comments start, and what ends a statement.</summary>
    /// <param name="Quotes">The characters text can open with: <c>"</c>, or <c>'"</c> for Dart.</param>
    /// <param name="Comment">What starts a comment to the end of the line: <c>//</c>, <c>#</c>, <c>%</c>.</param>
    /// <param name="Endings">What can end a statement: <c>;</c>, or <c>.,;</c> for Erlang, or nothing.</param>
    /// <param name="ClosesAtLineEnd">Whether text can be closed at the end of a line with nothing after it, as F#'s <c>printfn "hello</c> is.</param>
    public sealed record Writing(IReadOnlySet<string> Files, Syntax Syntax, string Quotes, string Comment, string Endings, bool ClosesAtLineEnd);

    /// <summary>A name or constant after a comma at the end of text - , PHP_EOL or , $name - meant to follow the text, not be in it.</summary>
    [GeneratedRegex(@",\s*(?:\$[A-Za-z_]\w*|[A-Z][A-Z0-9_]+)\s*$")]
    private static partial Regex CodeAfterTheText();

    /// <summary>
    /// The line with its text closed before the statement's ending - ; in C#, . in Erlang - and before the brackets the text sits
    /// inside; and, when the text ends in , PHP_EOL or , $name, before that comma. Null when no text is left open on the line, or
    /// the place for the closing quote cannot be told.
    /// </summary>
    public static string? TextClosedBeforeTheEnding(string line, Writing writing)
    {
        if (OpenQuote(line, writing) is not { } quote) return null;

        var before = CodeText.Mask(line[..quote], writing.Syntax);
        var open = before.Count(c => c == '(') - before.Count(c => c == ')');
        if (open < 0) return null;

        var rest = line[(quote + 1)..].TrimEnd();
        var cut = rest.Length;
        var ended = cut > 0 && writing.Endings.Contains(rest[cut - 1]);
        if (ended) cut--;
        if (!ended && open == 0 && !writing.ClosesAtLineEnd) return null;

        for (var bracket = 0; bracket < open; bracket++)
        {
            while (cut > 0 && rest[cut - 1] == ' ') cut--;
            if (cut == 0 || rest[cut - 1] != ')') return null;
            cut--;
        }

        if (CodeAfterTheText().Match(rest[..cut]) is { Success: true } code) cut = code.Index;

        return line[..(quote + 1)] + rest[..cut] + line[quote] + rest[cut..];
    }

    /// <summary>Where on a line text opens that the line never closes, in the language's own quotes and comments.</summary>
    private static int? OpenQuote(string line, Writing writing)
    {
        for (var position = 0; position < line.Length; position++)
        {
            var character = line[position];

            if (writing.Comment.Length > 0 && string.CompareOrdinal(line, position, writing.Comment, 0, writing.Comment.Length) == 0) return null;
            if (!writing.Quotes.Contains(character)) continue;

            var start = position;
            for (position++; position < line.Length && line[position] != character; position++)
                if (line[position] == '\\') position++;

            if (position >= line.Length) return start;
        }

        return null;
    }

    /// <summary>
    /// The line with its round and square brackets closed: one that a different closer comes before is closed just before it
    /// - [(2 + 3] becomes [(2 + 3)] - and those still open at the end are closed before the statement's ending, or before the
    /// { that opens a block, as in if (x > 1 {. Null when every bracket is closed, a closer has nothing to close, or the line
    /// opens a block that runs on - fn mark ->, do - whose brackets close lines below.
    /// </summary>
    public static string? BracketsClosed(string line, Writing writing)
    {
        var masked = CodeText.Mask(CodeText.SplitComment(line, writing.Syntax).Code, writing.Syntax);
        if (Regex.IsMatch(masked, @"(?:->|\bdo|\bthen|\bof)\s*$")) return null;

        var opened = new Stack<(char Opener, int At)>();
        var closers = new List<(int At, char Closer)>();

        for (var position = 0; position < masked.Length; position++)
        {
            var character = masked[position];
            if (character is '(' or '[' or '{') { opened.Push((character, position)); continue; }
            if (character is not (')' or ']' or '}')) continue;

            while (opened.Count > 0 && Closer(opened.Peek().Opener) != character && opened.Peek().Opener != '{')
                closers.Add((position, Closer(opened.Pop().Opener)));

            if (opened.Count == 0 || Closer(opened.Peek().Opener) != character) return null;
            opened.Pop();
        }

        // Still open at the end: a { that ends the line opens a block, and the brackets inside it close before it.
        var end = masked.TrimEnd().Length;
        if (opened.Count > 0 && opened.Peek().Opener == '{' && opened.Peek().At == end - 1)
        {
            end = opened.Pop().At;
            while (end > 0 && masked[end - 1] == ' ') end--;
        }
        else if (end > 0 && writing.Endings.Contains(masked[end - 1]))
        {
            end--;
        }

        while (opened.Count > 0)
        {
            var (opener, _) = opened.Pop();
            if (opener == '{') return null;
            closers.Add((end, Closer(opener)));
        }

        if (closers.Count == 0) return null;

        var corrected = line;
        foreach (var (at, closer) in closers.OrderByDescending(each => each.At))
            corrected = corrected[..at] + closer + corrected[at..];
        return corrected;
    }

    private static char Closer(char opener) => opener switch { '(' => ')', '[' => ']', _ => '}' };

    /// <summary>
    /// The line the error names, or the nearest line above it, that the repair closes - a compiler can name the line where it
    /// gave up, after the one left open: PHP names the end of the file, rustc the brace that came too soon.
    /// </summary>
    public static (SourceFile Source, int Number, string Corrected)? NearestRepaired(LocalFixContext context, Writing writing, Func<string, Writing, string?> repair)
    {
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!writing.Files.Contains(Path.GetExtension(source.Path).ToLowerInvariant())) return null;

        for (var lineNumber = Math.Min(number, source.Count); lineNumber >= Math.Max(1, number - MostLinesAbove); lineNumber--)
        {
            if (source.Line(lineNumber) is { } line && repair(line, writing) is { } corrected) return (source, lineNumber, corrected);
        }

        return null;
    }

    /// <summary>How far above the line an error names the line left open is looked for.</summary>
    private const int MostLinesAbove = 15;

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
