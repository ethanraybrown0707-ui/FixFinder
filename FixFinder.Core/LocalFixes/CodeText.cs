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

    /// <summary>
    /// Lua's: -- to the end of a line, and --[[ ]] comments and [[ ]] strings - with as many = between the brackets as they
    /// like, [==[ ]==] - that run on over as many lines as they need.
    /// </summary>
    Lua,

    /// <summary>Perl's: # to the end of a line - but not the # of $#marks, the last position of @marks.</summary>
    Perl,

    /// <summary>Fortran's: ! to the end of a line - which is why if (mark != 40) is read as if (mark, and a comment.</summary>
    Fortran,

    /// <summary>
    /// Erlang's: % to the end of a line, text in double quotes and atoms in single ones - but $% and $" are characters, and code.
    /// </summary>
    Erlang,

    /// <summary>
    /// F#'s: OCaml's comments between (* and *) and its quotes - 'a' is a character, 'a a type variable and x' a name - and
    /// also // to the end of a line, text in """ that runs on over as many lines as it needs, and verbatim text, @"C:\marks",
    /// whose backslashes are only backslashes. (*) is the multiplication operator, not the start of a comment.
    /// </summary>
    FSharp,
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

            if (syntax == Syntax.Lua && (c == '[' || (c == '-' && next == '-')))
            {
                var bracketAt = c == '-' ? i + 2 : i;

                if (LuaLongBracket(line, bracketAt) is { } longBracket)
                {
                    var openingEnd = bracketAt + longBracket.OpeningLength;
                    for (var k = i; k < openingEnd; k++) chars[k] = ' ';

                    open = longBracket.Closing;
                    i = openingEnd;
                    continue;
                }

                if (c == '-')
                {
                    for (var k = i; k < chars.Length; k++) chars[k] = ' ';
                    break;
                }
            }

            // An Erlang character - $a, $%, $", $\n - is code, whatever character it is.
            if (syntax == Syntax.Erlang && c == '$')
            {
                i += next == '\\' ? 3 : 2;
                continue;
            }

            if ((syntax == Syntax.Python && c == '#') || (syntax == Syntax.Perl && IsPerlComment(line, i)) || (syntax == Syntax.Fortran && c == '!') ||
                (syntax == Syntax.Erlang && c == '%') || ((commentsLikeC || syntax == Syntax.FSharp) && c == '/' && next == '/'))
            {
                for (var k = i; k < chars.Length; k++) chars[k] = ' ';
                break;
            }

            if ((commentsLikeC && c == '/' && next == '*') || OpensMLComment(line, i, syntax))
            {
                chars[i] = ' ';
                chars[i + 1] = ' ';
                open = syntax is Syntax.OCaml or Syntax.FSharp ? "(*" : "/*";
                i += 2;
                continue;
            }

            // F#'s verbatim text, @"C:\marks": a backslash is a backslash, and "" is a quote inside it.
            if (syntax == Syntax.FSharp && c == '@' && next == '"')
            {
                var j = i + 2;

                while (j < line.Length && (line[j] != '"' || (j + 1 < line.Length && line[j + 1] == '"')))
                    j += line[j] == '"' ? 2 : 1;

                for (var k = i + 2; k < Math.Min(j, chars.Length); k++) chars[k] = ' ';

                i = Math.Min(j + 1, chars.Length);
                continue;
            }

            var tripleQuoted = syntax == Syntax.Python ? c is '"' or '\'' : syntax is Syntax.Scala or Syntax.FSharp && c == '"';

            if (tripleQuoted && i + 2 < line.Length && line[i + 1] == c && line[i + 2] == c)
            {
                chars[i] = ' ';
                chars[i + 1] = ' ';
                chars[i + 2] = ' ';
                open = new string(c, 3);
                i += 3;
                continue;
            }

            if (syntax is Syntax.OCaml or Syntax.FSharp && c == '\'')
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

    /// <summary>
    /// The Lua long bracket that opens at <paramref name="start"/> - [[, or [ with = signs and another [ - as how many
    /// characters it takes and what closes it: ]] or ] with as many = and another ]. Null when no long bracket opens there.
    /// </summary>
    private static (int OpeningLength, string Closing)? LuaLongBracket(string line, int start)
    {
        if (start >= line.Length || line[start] != '[') return null;

        var equalsSigns = 0;
        while (start + 1 + equalsSigns < line.Length && line[start + 1 + equalsSigns] == '=') equalsSigns++;

        var secondBracket = start + 1 + equalsSigns;
        if (secondBracket >= line.Length || line[secondBracket] != '[') return null;

        return (equalsSigns + 2, "]" + new string('=', equalsSigns) + "]");
    }

    /// <summary>Whether (* at <paramref name="at"/> opens an OCaml or F# comment - in F#, (*) is the multiplication operator.</summary>
    private static bool OpensMLComment(string line, int at, Syntax syntax) =>
        syntax is Syntax.OCaml or Syntax.FSharp && line[at] == '(' && at + 1 < line.Length && line[at + 1] == '*' &&
        !(syntax == Syntax.FSharp && at + 2 < line.Length && line[at + 2] == ')');

    /// <summary>Whether the # at <paramref name="at"/> starts a Perl comment, rather than being the # of $#marks.</summary>
    private static bool IsPerlComment(string line, int at) => line[at] == '#' && (at == 0 || line[at - 1] != '$');

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

            if (syntax is Syntax.OCaml or Syntax.FSharp && c == '\'')
            {
                var characterEnd = i > 0 && IsWordChar(line[i - 1]) ? -1 : OCamlCharacterEnd(line, i);
                if (characterEnd > i) i = characterEnd;
                continue;
            }

            if (syntax == Syntax.Erlang && c == '$')
            {
                i += i + 1 < line.Length && line[i + 1] == '\\' ? 2 : 1;
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
            if (OpensMLComment(line, i, syntax)) return i;
            if (syntax == Syntax.FSharp && c == '/' && i + 1 < line.Length && line[i + 1] == '/') return i;
            if (syntax == Syntax.Lua && c == '-' && i + 1 < line.Length && line[i + 1] == '-') return i;
            if (syntax == Syntax.Perl && IsPerlComment(line, i)) return i;
            if (syntax == Syntax.Fortran && c == '!') return i;
            if (syntax == Syntax.Erlang && c == '%') return i;
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
