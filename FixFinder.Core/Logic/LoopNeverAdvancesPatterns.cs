using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// A loop that runs while a counter is below a bound - while i &lt; len(marks) - and nothing inside it moves the counter on,
/// changes the bound or leaves the loop: once its condition is true it stays true, and the loop never ends. Python's and the
/// C family's have checks of their own; this is the same check in each other language's own words, from the table below.
/// </summary>
/// <remarks>
/// Said only when the counter was given a number before the loop - i = 0 - so a loop over a value something else keeps
/// changing, or one meant to wait, is left alone. The change moves the counter on at the end of each pass, except in Rust,
/// whose let i = 0 cannot be changed at all - the counter has to be made mut as well, so the change is left to its writer.
/// </remarks>
public static partial class LoopNeverAdvancesPatterns
{
    /// <summary>How one language writes what this check reads.</summary>
    /// <param name="Loop">A while loop on a counter and a bound: "name", "op" and "bound".</param>
    /// <param name="Exits">What leaves a loop or its function early in the language.</param>
    /// <param name="Step">The counter moved on by one, as a line of the language, with {0} for the name: i += 1, $i++;.</param>
    /// <param name="Down">The counter moved back by one, for a loop that runs while it is above its bound.</param>
    private sealed record Dialect(string Id, IReadOnlySet<string> Files, Syntax Syntax, Regex Loop, string Exits, string? Step, string? Down);

    private const string Name = @"(?<name>[$]?[A-Za-z_]\w*)";
    private const string Op = @"(?<op><=|<|>=|>|-lt|-le|-gt|-ge|\.lt\.|\.le\.|\.gt\.|\.ge\.)";

    private static readonly Dialect[] Dialects =
    [
        new("logic-ruby-loop-never-advances", CodePattern.Files(".rb"), Syntax.Python,
            new($@"^\s*while\s+{Name}\s*{Op}\s*(?<bound>.*\S)\s*$"), @"break|return|exit|raise", "{0} += 1", "{0} -= 1"),
        new("logic-php-loop-never-advances", CodePattern.Files(".php"), Syntax.CLike,
            new($@"^\s*while\s*\(\s*{Name}\s*{Op}\s*(?<bound>[^{{]*[^{{\s])\s*\)\s*\{{\s*$"), @"break|return|exit|die|throw", "{0}++;", "{0}--;"),
        new("logic-lua-loop-never-advances", CodePattern.Files(".lua"), Syntax.Lua,
            new($@"^\s*while\s+{Name}\s*{Op}\s*(?<bound>.*\S)\s+do\s*$"), @"break|return|error|goto", "{0} = {0} + 1", "{0} = {0} - 1"),
        new("logic-go-loop-never-advances", CodePattern.Files(".go"), Syntax.CLike,
            new($@"^\s*for\s+{Name}\s*{Op}\s*(?<bound>[^{{;]*[^{{;\s])\s*\{{\s*$"), @"break|return|panic|goto|os\.Exit|log\.Fatal\w*", "{0}++", "{0}--"),
        new("logic-rust-loop-never-advances", CodePattern.Files(".rs"), Syntax.CLike,
            new($@"^\s*while\s+{Name}\s*{Op}\s*(?<bound>[^{{]*[^{{\s])\s*\{{\s*$"), @"break|return|panic!|std::process::exit|process::exit", null, null),
        new("logic-powershell-loop-never-advances", CodePattern.Files(".ps1"), Syntax.Python,
            new($@"^\s*while\s*\(\s*{Name}\s*{Op}\s*(?<bound>[^{{]*[^{{\s])\s*\)\s*\{{\s*$", RegexOptions.IgnoreCase), @"break|return|exit|throw", "{0}++", "{0}--"),
        new("logic-perl-loop-never-advances", CodePattern.Files(".pl", ".pm"), Syntax.Perl,
            new($@"^\s*while\s*\(\s*{Name}\s*{Op}\s*(?<bound>[^{{]*[^{{\s])\s*\)\s*\{{\s*$"), @"last|return|exit|die|goto", "{0}++;", "{0}--;"),
        new("logic-dart-loop-never-advances", CodePattern.Files(".dart"), Syntax.CLike,
            new($@"^\s*while\s*\(\s*{Name}\s*{Op}\s*(?<bound>[^{{]*[^{{\s])\s*\)\s*\{{\s*$"), @"break|return|throw|exit", "{0}++;", "{0}--;"),
        new("logic-scala-loop-never-advances", CodePattern.Files(".scala", ".sc"), Syntax.Scala,
            new($@"^\s*while\s*\(\s*{Name}\s*{Op}\s*(?<bound>[^{{]*[^{{\s])\s*\)\s*\{{\s*$"), @"return|throw|sys\.exit|break", "{0} += 1", "{0} -= 1"),
        new("logic-fsharp-loop-never-advances", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp,
            new($@"^\s*while\s+{Name}\s*{Op}\s*(?<bound>.*\S)\s+do\s*$"), @"failwith|raise|exit|reraise", "{0} <- {0} + 1", "{0} <- {0} - 1"),
        new("logic-fortran-loop-never-advances", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran,
            new($@"^\s*do\s+while\s*\(\s*{Name}\s*{Op}\s*(?<bound>.*\S)\s*\)\s*$", RegexOptions.IgnoreCase), @"exit|return|stop|error\s+stop|go\s*to", "{0} = {0} + 1", "{0} = {0} - 1"),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
        [.. Dialects.Select(dialect => new CodePattern(dialect.Id, dialect.Files, dialect.Syntax,
            (id, source, masked) => NeverAdvances(id, source, masked, dialect), Severity.Error, Confidence.Likely))];

    private static IEnumerable<LogicFinding> NeverAdvances(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var caseless = dialect.Syntax == Syntax.Fortran ? RegexOptions.IgnoreCase : RegexOptions.None;

        for (var header = 0; header < masked.Count; header++)
        {
            if (dialect.Loop.Match(masked[header]) is not { Success: true } loop || Body(masked, header) is not var (first, end) || end <= first) continue;

            var name = loop.Groups["name"].Value;
            var bound = Regex.Replace(loop.Groups["bound"].Value, @"\s+do$", "");
            var body = Enumerable.Range(first, end - first).Select(line => masked[line]).ToList();

            // Anything in the loop that moves the counter on, or leaves the loop, and it is not this mistake.
            if (body.Any(line => Changes(line, name, caseless) || Regex.IsMatch(line, $@"(?<![\w$.])(?:{dialect.Exits})(?![\w])", caseless))) continue;

            // Nor when the bound is something the loop changes.
            var bounds = Regex.Matches(bound, @"(?<![\w.])[$@]?[A-Za-z_]\w*").Select(match => match.Value).Distinct().ToList();
            if (bounds.Any(bound => body.Any(line => Changes(line, bound, caseless) || ChangesTheList(line, bound)))) continue;

            // A counter: given a number before the loop.
            if (!Enumerable.Range(0, header).Any(line => Regex.IsMatch(masked[line], $@"(?<![\w$.]){Regex.Escape(name)}\s*(?::=|=)\s*-?\d+\s*[;,]?\s*$", caseless))) continue;

            var op = loop.Groups["op"].Value.ToLowerInvariant();
            var upwards = op is "<" or "<=" or "-lt" or "-le" or ".lt." or ".le.";
            var said = $"nothing in the loop changes `{name}`, so once `{source.Lines[header].Trim()}` is true it stays true and the loop never ends";

            if ((upwards ? dialect.Step : dialect.Down) is not { } stepping)
            {
                yield return new LogicFinding(id, header + 1, said, null) { Severity = Severity.Error, Confidence = Confidence.Likely };
                continue;
            }

            var step = string.Format(stepping, name);
            var inner = Enumerable.Range(first, end - first).Select(line => source.Lines[line]).FirstOrDefault(line => line.Trim().Length > 0) is { } within
                ? CodeText.Indentation(within)
                : CodeText.Indentation(source.Lines[header]) + "    ";

            yield return new LogicFinding(id, header + 1, said,
                LocalFix.Insert(id, $"Move {name} on at the end of each pass: {step.TrimEnd(';')}",
                    $"The loop keeps going while `{name} {loop.Groups["op"].Value} {bound.Trim()}`, and nothing inside it changes `{name}` " +
                    $"or the bound, so it can only stop by never starting. `{step}` at the end of each pass moves it towards the end.",
                    source.Path, end + 1, [$"{inner}{step}"]))
            {
                Severity = Severity.Error,
                Confidence = Confidence.Likely,
            };
        }
    }

    /// <summary>Whether the line gives the name a value: =, +=, ++, &lt;-, or a pass by reference.</summary>
    private static bool Changes(string line, string name, RegexOptions caseless)
    {
        var word = Regex.Escape(name);
        return Regex.IsMatch(line, $@"(?<![\w$.]){word}\s*(?:[-+*/%]?=|<-|\.\.?=)(?!=)|(?:\+\+|--)\s*{word}\b|(?<![\w$.]){word}\s*(?:\+\+|--)|[&\\]\s*{word}\b|\b(?:inc|dec)\s*\(\s*{word}\b", caseless);
    }

    /// <summary>Whether the line adds to or takes from the list a bound is the length of: marks.push(...), table.insert(marks, ...).</summary>
    private static bool ChangesTheList(string line, string list) =>
        Regex.IsMatch(line, $@"(?<![\w$.]){Regex.Escape(list)}\s*(?:\.|->)\s*(?:push|pop|append|shift|unshift|insert|remove|add|clear|delete|truncate|<<)|" +
                            $@"\b(?:push|pop|shift|unshift|splice|array_push|array_pop|array_shift|table\.insert|table\.remove)\s*\(\s*[@$]?{Regex.Escape(list.TrimStart('$', '@'))}\b|" +
                            $@"(?<![\w$.]){Regex.Escape(list)}\s*<<");

    /// <summary>The loop's body: the lines inside its braces, or - for a loop closed by end - the lines further in than its first.</summary>
    private static (int First, int End)? Body(IReadOnlyList<string> masked, int header)
    {
        if (masked[header].TrimEnd().EndsWith('{'))
            return Brackets.BlockEnd(masked, header) is { } close ? (header + 1, close) : null;

        var indent = CodeText.Indentation(masked[header]).Length;
        var end = header + 1;
        while (end < masked.Count && (masked[end].Trim().Length == 0 || CodeText.Indentation(masked[end]).Length > indent)) end++;
        while (end > header + 1 && masked[end - 1].Trim().Length == 0) end--;
        return (header + 1, end);
    }
}
