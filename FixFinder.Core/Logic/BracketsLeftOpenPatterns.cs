using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A check of a string's brackets that counts the ones left open - one more for each (, one fewer for each ) - says no when
/// the count goes below 0, and at the end says yes whatever the count is. A ) with nothing open is caught; a ( never closed
/// is not: (( comes out as valid. QuixBugs' is_valid_parenthesization is this mistake, in Python and in Java. The change
/// asks at the end that nothing is left open - return depth == 0 - as QuixBugs' correct programs do.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: a loop counts up and down - depth += 1, depth++, depth = depth - 1 - and gives
/// back no when the count is below 0, and the function's next and last statement after the loop gives back yes - return
/// True, true, 1, $true, balanced = .true. - with nothing between asking the count. For F#, OCaml, Elixir and Erlang, which
/// walk the brackets one call at a time, the clause for the end of the list gives back true - | [] -&gt; true,
/// balanced([], Depth) -&gt; true - in a function calling itself with the count one up and one down.
/// </remarks>
public static partial class BracketsLeftOpenPatterns
{
    /// <summary>How one language is read, and how it asks that the count is 0.</summary>
    /// <param name="IsZero">The test of the count, with {count} in it.</param>
    /// <param name="Clauses">Whether the language walks the brackets one call at a time, its end of the list a clause of its own.</param>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, string IsZero, bool Clauses = false, bool IgnoreCase = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, "{count} == 0"),
        new("java", CodePattern.Files(".java"), Syntax.CLike, "{count} == 0"),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, "{count} == 0"),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, "{count} === 0"),
        new("c", CodePattern.Files(".c"), Syntax.CLike, "{count} == 0"),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, "{count} == 0"),
        new("go", CodePattern.Files(".go"), Syntax.CLike, "{count} == 0"),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, "{count} == 0"),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, "{count} == 0"),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, "{count} == 0"),
        new("php", CodePattern.Files(".php"), Syntax.CLike, "{count} === 0"),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, "{count} == 0"),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, "{count} -eq 0", IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, "{count} == 0"),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, "{count} == 0"),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, "{count} == 0", IgnoreCase: true),
        new("fsharp", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp, "{count} = 0", Clauses: true),
        new("ocaml", CodePattern.Files(".ml"), Syntax.OCaml, "{count} = 0", Clauses: true),
        new("elixir", CodePattern.Files(".ex", ".exs"), Syntax.Python, "{count} == 0", Clauses: true),
        new("erlang", CodePattern.Files(".erl"), Syntax.Erlang, "{count} =:= 0", Clauses: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-brackets-left-open", dialect.Files, dialect.Syntax,
            (id, source, masked) => BracketsLeftOpen(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>A yes given back: return True, return true;, return 1, return $true, a last true, balanced = .true. - the yes in the group named yes.</summary>
    [GeneratedRegex(@"^\s*(?:return\s+|(?<result>[A-Za-z_]\w*)\s*=\s*)?(?<yes>True|true|1|\$true|\.true\.)\s*;?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GivesYes();

    /// <summary>An opening bracket quoted: '(', "(", '[', "{", Erlang's $( - the text a bracket check compares with.</summary>
    [GeneratedRegex(@"(['""])[(\[{]\1|\$[(\[{]")]
    private static partial Regex OpeningBracket();

    /// <summary>The count below 0: depth &lt; 0, $depth -lt 0, depth == -1, depth .lt. 0 - with the count in the group named count.</summary>
    [GeneratedRegex(@"(?<![\w$@.])(?<count>[$]?[A-Za-z_]\w*)\s*(?:<\s*0|-lt\s+0|==\s*-\s*1|\.lt\.\s*0)(?![\w.])", RegexOptions.IgnoreCase)]
    private static partial Regex BelowZero();

    private static IEnumerable<LogicFinding> BracketsLeftOpen(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        // Brackets, as written: '(' "(" '[' "{" $( - masking blanks what is quoted, so the lines as written are read. A
        // count of steps up and down that must never go below ground is written the same way, and is right to end in yes.
        if (!source.Lines.Any(text => OpeningBracket().IsMatch(text))) yield break;
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var findings = dialect.Clauses ? EndClauses(masked, dialect, options) : LoopsEndingYes(masked, source.Lines, options);
        foreach (var (line, count, yes) in findings)
        {
            var text = source.Lines[line];
            var asked = dialect.IsZero.Replace("{count}", count);
            var changed = text[..yes.Index] + asked + text[(yes.Index + yes.Length)..];
            var told = $"Line {line + 1} says yes whatever `{count}` is - but `{count}` counts the brackets still open, one more for each opening " +
                       $"one and one fewer for each closing one. A closing bracket with nothing open makes it go below 0, which is caught; an " +
                       $"opening one never closed leaves it above 0 at the end, which is not - (( is said to be valid.";
            yield return new LogicFinding(id, line + 1, told,
                LocalFix.ReplaceLine(id, $"Ask that nothing is left open: {asked}",
                    $"At the end every bracket opened must have been closed: `{count}` is 0.",
                    source.Path, line + 1, changed));
        }
    }

    /// <summary>
    /// A loop counting a name up and down and giving back no when it is below 0, whose function then gives back yes as its
    /// next and last statement: the line of the yes, the count, and the yes as written.
    /// </summary>
    private static IEnumerable<(int Line, string Count, Group Yes)> LoopsEndingYes(IReadOnlyList<string> masked, IReadOnlyList<string> written, RegexOptions options)
    {
        for (var line = 0; line < masked.Count; line++)
        {
            // A 0 or a 1 with a < or -lt on the line, before the regex that tries every name on it.
            var text = masked[line];
            if (!(text.Contains('0') || text.Contains('1')) || !(text.Contains('<') || text.Contains("-lt", StringComparison.OrdinalIgnoreCase) || text.Contains("==", StringComparison.Ordinal))) continue;
            if (BelowZero().Match(text) is not { Success: true } below) continue;

            // A no given back on its line or the two after - read before the loop is walked, which costs more.
            if (!Enumerable.Range(line, 3).Where(at => at < masked.Count).Any(at => GivesNo().IsMatch(masked[at]))) continue;
            var count = below.Groups["count"].Value;
            if (Reaching.InnermostLoop(masked, 0, line) is not { } loop) continue;

            // The loop's own lines, and the first statement after them.
            var indent = CodeText.Indentation(masked[loop.Heading]).Length;
            var after = loop.Opening + 1;
            while (after < masked.Count && (masked[after].Trim().Length == 0 || CodeText.Indentation(masked[after]).Length > indent)) after++;
            if (after < masked.Count && Regex.IsMatch(masked[after], @"^\s*(?:\}|end(?:\s+do)?)\s*$", RegexOptions.IgnoreCase) && CodeText.Indentation(masked[after]).Length == indent) after++;
            while (after < masked.Count && masked[after].Trim().Length == 0) after++;
            if (after >= masked.Count) continue;

            // The cheap tests first: a yes after the loop, a no below 0, an opening bracket compared with - then the counting.
            if (GivesYes().Match(masked[after]) is not { Success: true } yes) continue;
            var body = Enumerable.Range(loop.Opening + 1, after - loop.Opening - 1).ToList();
            if (!GivesNoBelowZero(masked, line, body)) continue;
            if (!new[] { loop.Heading }.Concat(body).Any(at => OpeningBracket().IsMatch(written[at]))) continue;
            if (!CountsUpAndDown(masked, body, count, options)) continue;

            // The yes is the function's last statement: what follows it is the function's end.
            var next = after + 1;
            while (next < masked.Count && masked[next].Trim().Length == 0) next++;
            if (next < masked.Count && CodeText.Indentation(masked[next]).Length >= CodeText.Indentation(masked[after]).Length &&
                !Regex.IsMatch(masked[next], @"^\s*(?:\}|end\b)", RegexOptions.IgnoreCase)) continue;

            // Fortran's function gives back what is put in its name: balanced = .true. - put there in the loop too.
            if (yes.Groups["result"].Success && !body.Any(at => Regex.IsMatch(masked[at], $@"^\s*{Regex.Escape(yes.Groups["result"].Value)}\s*=", options))) continue;

            yield return (after, count, yes.Groups["yes"]);
            line = after;
        }
    }

    /// <summary>Whether the loop counts the name up and down: depth += 1, depth++, ++depth, depth = depth + 1, and the same with -.</summary>
    private static bool CountsUpAndDown(IReadOnlyList<string> masked, List<int> body, string count, RegexOptions options)
    {
        var name = Regex.Escape(count.TrimStart('$'));
        bool Moved(char sign) => body.Any(at => Regex.IsMatch(masked[at],
            $@"(?<![\w$@.])[$]?{name}\s*{Regex.Escape(sign.ToString())}=\s*1\b|(?<![\w$@.])[$]?{name}\s*{Regex.Escape($"{sign}{sign}")}|{Regex.Escape($"{sign}{sign}")}\s*[$]?{name}\b|(?<![\w$@.])[$]?{name}\s*=\s*[$]?{name}\s*{Regex.Escape(sign.ToString())}\s*1\b", options));
        return Moved('+') && Moved('-');
    }

    /// <summary>A no given back: return False, return false;, return 0, return $false, balanced = .false.</summary>
    [GeneratedRegex(@"\breturn\s+(?:False|false|0|\$false)\b|=\s*\.false\.", RegexOptions.IgnoreCase)]
    private static partial Regex GivesNo();

    /// <summary>Whether the count below 0 gives back no, in the loop: return False on its line or the next, return 0 if, balanced = .false. then return.</summary>
    private static bool GivesNoBelowZero(IReadOnlyList<string> masked, int line, List<int> body) =>
        Enumerable.Range(line, 3).Where(at => at == line || body.Contains(at)).Any(at => GivesNo().IsMatch(masked[at]));

    /// <summary>
    /// The clause for the end of the list giving back true - | [] -&gt; true, defp balanced?([], depth), do: true,
    /// balanced([], Depth) -&gt; true; - in a function the file calls with the count one up and one down: the line, the
    /// count, and the true as written.
    /// </summary>
    private static IEnumerable<(int Line, string Count, Group Yes)> EndClauses(IReadOnlyList<string> masked, Dialect dialect, RegexOptions options)
    {
        for (var line = 0; line < masked.Count; line++)
        {
            var text = masked[line];
            string? count = null;
            Group? yes = null;

            // F# and OCaml: | [] -> true, in a match inside let rec balanced depth brackets =.
            if (Regex.Match(text, @"^\s*\|\s*\[\s*\]\s*->\s*(?<yes>true)\s*$") is { Success: true } arm)
            {
                for (var above = line - 1; above >= 0; above--)
                {
                    if (Regex.Match(masked[above], @"^\s*let\s+rec\s+(?<function>[A-Za-z_]\w*)\s+(?<names>(?:[A-Za-z_]\w*\s+)+)=") is not { Success: true } heading) continue;
                    var function = Regex.Escape(heading.Groups["function"].Value);
                    count = heading.Groups["names"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(name => CalledUpAndDown(masked, $@"\b{function}\s+\(\s*{Regex.Escape(name)}\s*"));
                    break;
                }

                yes = arm.Groups["yes"];
            }

            // Elixir and Erlang: balanced([], Depth) -> true;  defp balanced?([], depth), do: true.
            else if (Regex.Match(text, @"^\s*(?:defp?\s+)?(?<function>[A-Za-z_]\w*[?!]?)\s*\(\s*\[\s*\]\s*,\s*(?<count>[A-Za-z_]\w*)\s*\)\s*(?:,\s*do:|->)\s*(?<yes>true)\s*;?\s*$") is { Success: true } clause)
            {
                var function = Regex.Escape(clause.Groups["function"].Value);
                var name = Regex.Escape(clause.Groups["count"].Value);
                if (CalledUpAndDown(masked, $@"\b{function}\s*\([^,()]*,\s*{name}\s*")) count = clause.Groups["count"].Value;
                yes = clause.Groups["yes"];
            }

            if (count is not null && yes is not null) yield return (line, count, yes);
        }
    }

    /// <summary>Whether the file calls the function with the count one up and one down: balanced (depth + 1), balanced(Rest, Depth - 1).</summary>
    private static bool CalledUpAndDown(IReadOnlyList<string> masked, string call) =>
        masked.Any(text => Regex.IsMatch(text, call + @"\+\s*1\b")) && masked.Any(text => Regex.IsMatch(text, call + @"-\s*1\b"));
}
