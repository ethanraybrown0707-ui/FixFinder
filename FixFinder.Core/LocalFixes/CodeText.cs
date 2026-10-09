using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes;

/// <summary>Which comment and string rules a line of code follows.</summary>
public enum Syntax
{
    Python,

    CLike,

    /// <summary>C's comments, and Scala's triple-quoted strings, which run on over as many lines as they need.</summary>
    Scala,

    /// <summary>
    /// OCaml's: comments between (* and *), and no comment to the end of a line. A quote is a character only as 'a' or
    /// '\n' - 'a on its own is a type variable, and x' is a name, as OCaml lets a name end in a quote.
    /// </summary>
    OCaml,
}

/// <summary>Small, exact readings of source lines, shared by every rule.</summary>
public static partial class CodeText
{
    [GeneratedRegex(@"[A-Za-z_$][A-Za-z0-9_$]*")]
    private static partial Regex WordPattern();

    public static string Indentation(string line) => line[..(line.Length - line.TrimStart().Length)];

    public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

    public static string Mask(string line, Syntax syntax, ref string? open)
    {
        var chars = line.ToCharArray();
        var i = 0;

        while (i < chars.Length)
        {
            if (open is { } delimiter)
            {
                var close = delimiter switch { "/*" => "*/", "(*" => "*)", _ => delimiter };
                var at = line.IndexOf(close, i, StringComparison.Ordinal);
                var stop = at < 0 ? chars.Length : at + close.Length;

                for (var k = i; k < stop; k++) chars[k] = ' ';

                if (at >= 0) open = null;
                i = stop;
                continue;
            }

            var c = line[i];
            var next = i + 1 < line.Length ? line[i + 1] : '\0';
            var commentsLikeC = syntax is Syntax.CLike or Syntax.Scala;

            if ((syntax == Syntax.Python && c == '#') || (commentsLikeC && c == '/' && next == '/'))
            {
                for (var k = i; k < chars.Length; k++) chars[k] = ' ';
                break;
            }

            if ((commentsLikeC && c == '/' && next == '*') || (syntax == Syntax.OCaml && c == '(' && next == '*'))
            {
                chars[i] = ' ';
                chars[i + 1] = ' ';
                open = syntax == Syntax.OCaml ? "(*" : "/*";
                i += 2;
                continue;
            }

            var tripleQuoted = syntax == Syntax.Python ? c is '"' or '\'' : syntax == Syntax.Scala && c == '"';

            if (tripleQuoted && i + 2 < line.Length && line[i + 1] == c && line[i + 2] == c)
            {
                chars[i] = ' ';
                chars[i + 1] = ' ';
                chars[i + 2] = ' ';
                open = new string(c, 3);
                i += 3;
                continue;
            }

            if (syntax == Syntax.OCaml && c == '\'')
            {
                // A character between quotes is masked; a quote that ends a name, or starts a type variable, is code.
                var characterEnd = i > 0 && IsWordChar(line[i - 1]) ? -1 : OCamlCharacterEnd(line, i);

                if (characterEnd > i)
                {
                    for (var k = i + 1; k < characterEnd; k++) chars[k] = ' ';
                    i = characterEnd + 1;
                }
                else
                {
                    i++;
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                var j = i + 1;

                while (j < line.Length && line[j] != c)
                {
                    if (line[j] == '\\') j++;
                    j++;
                }

                for (var k = i + 1; k < Math.Min(j, chars.Length); k++) chars[k] = ' ';

                i = Math.Min(j + 1, chars.Length);
                continue;
            }

            i++;
        }

        return new string(chars);
    }

    /// <summary>
    /// Where the closing quote of an OCaml character starting at <paramref name="start"/> is - 'a', '\n', '\'', '\065',
    /// '\x41' - or -1 when the quote starts no character, as in the type variable 'a.
    /// </summary>
    private static int OCamlCharacterEnd(string line, int start)
    {
        if (start + 2 < line.Length && line[start + 1] != '\\' && line[start + 2] == '\'') return start + 2;
        if (start + 1 >= line.Length || line[start + 1] != '\\') return -1;

        var closing = line.IndexOf('\'', start + 3);
        return closing > start && closing - start <= 5 ? closing : -1;
    }

    public static string Mask(string line, Syntax syntax)
    {
        string? open = null;
        return Mask(line, syntax, ref open);
    }

    public static IReadOnlyList<string> MaskAll(IReadOnlyList<string> lines, Syntax syntax)
    {
        string? open = null;
        var masked = new string[lines.Count];

        for (var i = 0; i < lines.Count; i++) masked[i] = Mask(lines[i], syntax, ref open);

        return masked;
    }

    public static (string Code, string Tail) SplitComment(string line, Syntax syntax)
    {
        var end = CommentStart(line, syntax) ?? line.Length;
        var code = line[..end].TrimEnd();

        return (code, line[code.Length..]);
    }

    private static int? CommentStart(string line, Syntax syntax)
    {
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (syntax == Syntax.OCaml && c == '\'')
            {
                var characterEnd = i > 0 && IsWordChar(line[i - 1]) ? -1 : OCamlCharacterEnd(line, i);
                if (characterEnd > i) i = characterEnd;
                continue;
            }

            if (c is '"' or '\'')
            {
                var j = i + 1;

                while (j < line.Length && line[j] != c)
                {
                    if (line[j] == '\\') j++;
                    j++;
                }

                i = j;
                continue;
            }

            if (syntax == Syntax.Python && c == '#') return i;
            if (syntax is Syntax.CLike or Syntax.Scala && c == '/' && i + 1 < line.Length && line[i + 1] is '/' or '*') return i;
            if (syntax == Syntax.OCaml && c == '(' && i + 1 < line.Length && line[i + 1] == '*') return i;
        }

        return null;
    }

    public static HashSet<string> Identifiers(IEnumerable<string> maskedLines)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in maskedLines)
            foreach (Match match in WordPattern().Matches(line))
                if (match.Index == 0 || !char.IsDigit(line[match.Index - 1]))
                    words.Add(match.Value);

        return words;
    }

    public static int Distance(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return 0;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return 1;

        var d = new int[a.Length + 1, b.Length + 1];

        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;

                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);

                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }

        return d[a.Length, b.Length];
    }

    public static string? Nearest(string wrong, IEnumerable<string> candidates, IReadOnlyList<string>? preferred = null)
    {
        if (wrong.Length < 3) return null;

        var limit = wrong.Length <= 4 ? 1 : 2;

        var closest = new List<string>();
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            if (candidate == wrong || Math.Abs(candidate.Length - wrong.Length) > limit) continue;

            var distance = Distance(wrong, candidate);
            if (distance > limit) continue;

            if (distance < bestDistance)
            {
                closest = [candidate];
                bestDistance = distance;
            }
            else if (distance == bestDistance)
            {
                closest.Add(candidate);
            }
        }

        if (closest.Count == 1) return closest[0];

        return preferred?.FirstOrDefault(closest.Contains);
    }

    public static string? ReplaceWord(string line, string word, string replacement, Syntax syntax, int? nearColumn = null)
    {
        var masked = Mask(line, syntax);
        var hits = new List<int>();

        for (var at = masked.IndexOf(word, StringComparison.Ordinal); at >= 0;
             at = masked.IndexOf(word, at + 1, StringComparison.Ordinal))
        {
            var before = at > 0 ? masked[at - 1] : ' ';
            var after = at + word.Length < masked.Length ? masked[at + word.Length] : ' ';

            if (!IsWordChar(before) && !IsWordChar(after)) hits.Add(at);
        }

        int chosen;

        if (hits.Count == 1)
        {
            chosen = hits[0];
        }
        else if (hits.Count > 1 && nearColumn is { } column)
        {
            var near = hits.Where(h => h >= column - 1 && h <= column + 1).ToList();
            if (near.Count != 1) return null;
            chosen = near[0];
        }
        else
        {
            return null;
        }

        return line[..chosen] + replacement + line[(chosen + word.Length)..];
    }

    public static (string Echo, int Column)? Caret(IReadOnlyList<CapturedLine> output, ParsedError error)
    {
        var index = -1;

        for (var i = 0; i < output.Count; i++)
        {
            if (output[i].Sequence != error.FirstLineSequence) continue;

            index = i;
            break;
        }

        if (index < 0 || index + 2 >= output.Count) return null;

        var caret = output[index + 2].Text.TrimEnd();
        if (caret.Trim() != "^") return null;

        return (output[index + 1].Text, caret.Length - 1);
    }
}
