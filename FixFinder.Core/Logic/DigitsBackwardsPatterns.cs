using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// A number written in another base by taking its remainder by the base and dividing it, again and again - each digit put
/// after the ones before it, result = result + alphabet[i]. The remainder gives the last digit first, so the digits come
/// out backwards: 31 in base 16 as F1, where 1F is right. QuixBugs' to_base is this mistake, in Python and in Java. The
/// change puts each digit in front of the ones before it - result = alphabet[i] + result - as QuixBugs' correct programs do.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: a loop divides a number by the base - num = num // b, num /= base, intdiv,
/// [math]::Floor, num ~/= base - and adds to a text, after what it holds, a digit picked by that number's remainder by the
/// base, or by a name given it in the loop: result + alphabet[i], result += DIGITS[num % base], $result .= ..., result ..
/// DIGITS:sub(...), trim(written) // digits(...), !result ^ ..., result.push(...). For Elixir and Erlang the text is given
/// along to the function calling itself with the number divided: digits(div(num, base), base, written &lt;&gt; ...). A text
/// turned round afterwards - result[::-1], reverse, strrev - is left alone.
/// </remarks>
public static partial class DigitsBackwardsPatterns
{
    /// <summary>How one language joins text, and how it puts a piece in front: the joins - +, .., //, ^, ., &lt;&gt;, ++ - and the join a compound adding writes, += or .=.</summary>
    /// <param name="Joins">The operators joining two texts, as the language writes them.</param>
    /// <param name="Compound">What a compound adding - result += x, $result .= x - is written as joined in front: + or a dot.</param>
    /// <param name="Calls">Whether the text is given along to the function calling itself, as Elixir and Erlang go on.</param>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, string[] Joins, string Compound = "+", bool Calls = false, bool IgnoreCase = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, ["+"]),
        new("java", CodePattern.Files(".java"), Syntax.CLike, ["+"]),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, ["+"]),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, ["+"]),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, ["+"]),
        new("go", CodePattern.Files(".go"), Syntax.CLike, ["+"]),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, ["+"]),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, ["+"]),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, [".."]),
        new("php", CodePattern.Files(".php"), Syntax.CLike, ["."], Compound: "."),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, ["."], Compound: "."),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, ["+"], IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, ["+"]),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, ["+"]),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, ["//"], IgnoreCase: true),
        new("fsharp", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp, ["+"]),
        new("ocaml", CodePattern.Files(".ml"), Syntax.OCaml, ["^"]),
        new("elixir", CodePattern.Files(".ex", ".exs"), Syntax.Python, ["<>"], Calls: true),
        new("erlang", CodePattern.Files(".erl"), Syntax.Erlang, ["++"], Calls: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-digits-written-backwards", dialect.Files, dialect.Syntax,
            (id, source, masked) => DigitsBackwards(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>What every remainder written has in it: %, mod, rem - a file without one has nothing to read.</summary>
    [GeneratedRegex(@"%|\bmod\b|\brem\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemainderWritten();

    /// <summary>A line giving a name a value: result = ..., result += ..., $result .= ..., result &lt;- ..., result := ... - the name and how.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<target>[$]?[A-Za-z_]\w*)\s*(?<assign>\+=|\.=|:=|<-|=)(?!=)\s*")]
    private static partial Regex Given();

    /// <summary>A piece added by a method: result.push(...), builder.append(...), builder.Append(...).</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<target>[A-Za-z_]\w*)\s*\.\s*(?<method>push|append|Append)\s*\(")]
    private static partial Regex Pushed();

    /// <summary>
    /// A character picked out: alphabet[i], digits.[i], charAt, substring, Substring, substr, sub, String.at, lists:nth,
    /// String.make, string(, str(, chr(, valueOf, to_string - or Fortran's digits(i + 1:i + 1).
    /// </summary>
    [GeneratedRegex(@"\[|\b(?:charAt|substring|Substring|substr|sub|at|nth|make|string|str|chr|valueOf|toString|to_string|ToString)\s*\(|\w\s*\([^():]*:[^():]*\)")]
    private static partial Regex PicksACharacter();

    /// <summary>A text turned round: result[::-1], reversed(, .reverse, Reverse, strrev, .rev(), lists:reverse, String.reverse.</summary>
    [GeneratedRegex(@"\[\s*::\s*-1\s*\]|\breversed?\b|\bReverse\b|\bstrrev\b|\.rev\s*\(")]
    private static partial Regex TurnedRound();

    private static IEnumerable<LogicFinding> DigitsBackwards(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        if (!masked.Any(line => RemainderWritten().IsMatch(line))) yield break;
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;

        for (var line = 0; line < masked.Count; line++)
        {
            // The change is cut from the line as written: masking blanks what is quoted.
            var asWritten = source.Lines[line];
            if ((dialect.Calls ? GivenAlong(masked, line, asWritten, dialect, options) : AddedInALoop(masked, line, asWritten, dialect, options)) is not { } adding) continue;

            // A text turned round afterwards is the other way of writing it.
            var text = Regex.Escape(adding.Text.TrimStart('$', '!'));
            if (Enumerable.Range(line + 1, masked.Count - line - 1).Any(at => TurnedRound().IsMatch(masked[at]) && Regex.IsMatch(masked[at], $@"(?<![\w$@.])[$!]?{text}(?!\w)", options))) continue;

            var written = source.Lines[line];
            var changed = written[..adding.Start] + adding.InFront + written[adding.End..];
            var told = $"Line {line + 1} puts each digit after the ones before it in `{adding.Text}` - but the remainder by the base gives the last " +
                       $"digit first, then the one before it, so the digits come out backwards: 31 in base 16 as F1, where 1F is right. Each " +
                       "digit goes in front of the ones found before it.";
            yield return new LogicFinding(id, line + 1, told,
                LocalFix.ReplaceLine(id, $"Put each digit in front: {adding.InFront.Trim()}",
                    "The remainder is the number's last digit; dividing by the base drops it, and the next remainder is the digit before it - so " +
                    "each one found goes in front of those found already.",
                    source.Path, line + 1, changed));
        }
    }

    /// <summary>The adding, where it is on the line, and the same with the digit in front: the text's name, the span replaced, and what replaces it.</summary>
    private sealed record Adding(string Text, int Start, int End, string InFront);

    /// <summary>
    /// A line in a loop adding a digit after a text - result = result + x, result += x, $result .= x, result.push(x) - where
    /// the loop divides a number by the base and the digit is picked by that number's remainder by the base.
    /// </summary>
    private static Adding? AddedInALoop(IReadOnlyList<string> masked, int line, string asWritten, Dialect dialect, RegexOptions options)
    {
        var text = masked[line];
        string target;
        string piece;
        int pieceAt;
        Func<string, (int Start, int End, string InFront)> rewrite;

        if (Pushed().Match(text) is { Success: true } pushed)
        {
            var open = pushed.Index + pushed.Length - 1;
            if (Brackets.ClosingParenthesis(text, open) is not { } close || text[(close + 1)..].Trim().TrimEnd(';').Length > 0) return null;
            target = pushed.Groups["target"].Value;
            piece = text[(open + 1)..close];
            pieceAt = open + 1;
            var method = pushed.Groups["method"];
            var front = method.Value == "Append" ? "Insert" : "insert";
            rewrite = written => (method.Index, close + 1, $"{front}(0, {written[(open + 1)..close]})");
        }
        else if (Given().Match(text) is { Success: true } given)
        {
            target = given.Groups["target"].Value;
            var assign = given.Groups["assign"].Value;
            var rest = text[given.Length..].TrimEnd();
            if (rest.EndsWith(';')) rest = rest[..^1].TrimEnd();
            var restAt = given.Length;

            if (assign is "+=" or ".=")
            {
                if (assign == ".=" && dialect.Compound != ".") return null;
                piece = rest;
                pieceAt = restAt;
                var join = dialect.Compound == "." ? "." : "+";
                rewrite = written => (given.Groups["assign"].Index, restAt + rest.Length, $"= {written.Substring(restAt, rest.Length)} {join} {target}");
            }
            else
            {
                // result = result + x, written = trim(written) // x, result := !result ^ x, result <- result + x.
                var operand = $@"^(?<operand>(?:trim\s*\(\s*)?[!$]?{Regex.Escape(target.TrimStart('$'))}(?:\s*\))?)\s*(?<join>{string.Join("|", dialect.Joins.Select(Regex.Escape))})\s*";
                if (Regex.Match(rest, operand, options) is not { Success: true } joined) return null;
                piece = rest[joined.Length..];
                pieceAt = restAt + joined.Length;
                var operandText = joined.Groups["operand"].Value;
                var joinText = joined.Groups["join"].Value;
                rewrite = written => (restAt, restAt + rest.Length, $"{written.Substring(pieceAt, piece.Length)} {joinText} {operandText}");
            }
        }
        else return null;

        if (piece.Trim().Length == 0 || Regex.IsMatch(piece, $@"(?<![\w$@.])[$!]?{Regex.Escape(target.TrimStart('$'))}(?!\w)", options)) return null;

        // A digit's text picked out - a sum of digits, total += num % 10, adds a number, and its order does not matter.
        if (!PicksACharacter().IsMatch(piece)) return null;

        // A remainder near the line, before the loop around it is walked, which costs more.
        if (!Enumerable.Range(Math.Max(0, line - 10), Math.Min(masked.Count, line + 11) - Math.Max(0, line - 10)).Any(at => RemainderWritten().IsMatch(masked[at]))) return null;
        if (Reaching.InnermostLoop(masked, 0, line) is not { } loop) return null;
        var body = BlockUnder(masked, loop.Opening);

        // The loop divides a number by the base, and the digit is that number's remainder by it - or a name given it.
        foreach (var (number, divisor) in Divisions(masked, body, options))
        {
            if (!PicksTheRemainder(masked, body, line, piece, number, divisor, options)) continue;
            var (start, end, inFront) = rewrite(asWritten);
            return new Adding(target, start, end, inFront);
        }

        return null;
    }

    /// <summary>
    /// Elixir's and Erlang's text given along: a call of the function the line is in with the number divided by the base and
    /// the text with a digit after it - digits(div(num, base), base, written &lt;&gt; ...), digits(Num div Base, Base, Written ++ [...]).
    /// </summary>
    private static Adding? GivenAlong(IReadOnlyList<string> masked, int line, string asWritten, Dialect dialect, RegexOptions options)
    {
        var text = masked[line];
        var join = Regex.Escape(dialect.Joins[0]);
        foreach (Match call in Regex.Matches(text, @"(?<function>[a-z_]\w*[?!]?)\s*\("))
        {
            var open = call.Index + call.Length - 1;
            if (Brackets.ClosingParenthesis(text, open) is not { } close) continue;
            var arguments = TopLevelArguments(text, open + 1, close);
            var divided = arguments.Select(argument => Regex.Match(text[argument.Start..argument.End],
                @"\bdiv\s*\(\s*(?<number>[A-Za-z_]\w*)\s*,\s*(?<divisor>[A-Za-z_]\w*)\s*\)|(?<number>[A-Za-z_]\w*)\s+div\s+(?<divisor>[A-Za-z_]\w*)")).FirstOrDefault(match => match.Success);
            if (divided is null) continue;
            var number = divided.Groups["number"].Value;
            var divisor = divided.Groups["divisor"].Value;

            foreach (var (start, end) in arguments)
            {
                var argument = text[start..end];
                if (Regex.Match(argument, $@"^\s*(?<text>[A-Za-z_]\w*)\s*{join}\s*(?<piece>.+?)\s*$") is not { Success: true } joined) continue;
                if (!Remainder(joined.Groups["piece"].Value, number, divisor, options) || !PicksACharacter().IsMatch(joined.Groups["piece"].Value)) continue;
                var textName = joined.Groups["text"].Value;
                var pieceStart = start + joined.Groups["piece"].Index;
                var pieceEnd = start + joined.Groups["piece"].Index + joined.Groups["piece"].Length;
                var textStart = start + joined.Groups["text"].Index;
                return new Adding(textName, textStart, pieceEnd, $"{asWritten[pieceStart..pieceEnd]} {dialect.Joins[0]} {textName}");
            }
        }

        return null;
    }

    /// <summary>The arguments between a call's brackets, split at its commas at the top level, each with where it starts and ends.</summary>
    private static List<(int Start, int End)> TopLevelArguments(string text, int start, int end)
    {
        var arguments = new List<(int, int)>();
        var depth = 0;
        var from = start;
        for (var at = start; at < end; at++)
        {
            if (text[at] is '(' or '[' or '{') depth++;
            else if (text[at] is ')' or ']' or '}') depth--;
            else if (text[at] == ',' && depth == 0)
            {
                arguments.Add((from, at));
                from = at + 1;
            }
        }

        arguments.Add((from, end));
        return arguments;
    }

    /// <summary>The lines of the block under a heading - those more indented than it, until one that is not.</summary>
    private static List<int> BlockUnder(IReadOnlyList<string> masked, int opening)
    {
        var lines = new List<int>();
        var indent = CodeText.Indentation(masked[opening]).Length;
        for (var at = opening + 1; at < masked.Count; at++)
        {
            if (masked[at].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[at]).Length <= indent) break;
            lines.Add(at);
        }

        return lines;
    }

    /// <summary>
    /// Each number the loop divides by a base and gives back to itself: num = num // b, num /= base, num ~/= base, num =
    /// intdiv(num, base), num = [math]::Floor(num / base), num := !num / radix, num &lt;- num / radix.
    /// </summary>
    private static IEnumerable<(string Number, string Divisor)> Divisions(IReadOnlyList<string> masked, List<int> body, RegexOptions options)
    {
        foreach (var line in body)
        {
            var text = masked[line];
            if (Regex.Match(text, @"^\s*(?<number>[$]?[A-Za-z_]\w*)\s*(?://=|/=|~/=)\s*(?<divisor>[$]?[A-Za-z_]\w*)\s*;?\s*$") is { Success: true } compound)
            {
                yield return (compound.Groups["number"].Value, compound.Groups["divisor"].Value);
                continue;
            }

            if (Given().Match(text) is not { Success: true } given || given.Groups["assign"].Value is "+=" or ".=") continue;
            var number = given.Groups["target"].Value;
            var name = Regex.Escape(number.TrimStart('$'));
            var rest = text[given.Length..];
            var divided = Regex.Match(rest, $@"(?<![\w$@.])[$!]?{name}\s*(?://|/|~/|\bdiv\b)\s*(?<divisor>[$]?[A-Za-z_]\w*)(?!\w)|\b(?:intdiv|div)\s*\(\s*[$]?{name}\s*,\s*(?<divisor>[$]?[A-Za-z_]\w*)\s*\)", options);
            if (divided.Success) yield return (number, divided.Groups["divisor"].Value);
        }
    }

    /// <summary>Whether the text takes the number's remainder by the base: num % b, num mod b, Num rem Base, mod(num, base), rem(num, base).</summary>
    private static bool Remainder(string text, string number, string divisor, RegexOptions options)
    {
        var n = Regex.Escape(number.TrimStart('$'));
        var d = Regex.Escape(divisor.TrimStart('$'));
        return Regex.IsMatch(text, $@"(?<![\w$@.])[$!]?{n}\s*(?:%|\bmod\b|\brem\b)\s*[$]?{d}(?!\w)|\b(?:mod|rem)\s*\(\s*[$!]?{n}\s*,\s*[$]?{d}\s*\)", options);
    }

    /// <summary>Whether the piece added is the digit the remainder picks: the remainder in it, or a name the loop gave the remainder before the line - i = num % b.</summary>
    private static bool PicksTheRemainder(IReadOnlyList<string> masked, List<int> body, int line, string piece, string number, string divisor, RegexOptions options)
    {
        if (Remainder(piece, number, divisor, options)) return true;
        foreach (var at in body.Where(at => at < line))
        {
            if (Regex.Match(masked[at], @"^\s*(?:(?:const|let|var|local|my|int)\s+)?(?<name>[$]?[A-Za-z_]\w*)\s*(?::=|=)(?!=)\s*(?<value>.+)$") is not { Success: true } given) continue;
            if (!Remainder(given.Groups["value"].Value, number, divisor, options)) continue;
            if (Regex.IsMatch(piece, $@"(?<![\w$@.])[$]?{Regex.Escape(given.Groups["name"].Value.TrimStart('$'))}(?!\w)", options)) return true;
        }

        return false;
    }
}
