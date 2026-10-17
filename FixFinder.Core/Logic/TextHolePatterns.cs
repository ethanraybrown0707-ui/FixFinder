using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A value meant to be put into text, in text that puts nothing in - each run with its language's own toolchain, which said
/// nothing of the text: C#'s and F#'s "Total: {total}" with no $ before it printed Total: {total}, Scala's "Hello, $name" with
/// no s before it printed Hello, $name, and Ruby's 'Hello, #{name}', PHP's and Perl's 'Hello, $name' in single quotes printed
/// what was between the quotes, as it was written.
/// </summary>
public static partial class TextHolePatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-csharp-text-without-dollar", CodePattern.Files(".cs"), Syntax.CLike, CSharpTextWithoutDollar),
        new CodePattern("logic-ruby-interpolation-in-single-quotes", CodePattern.Files(".rb"), Syntax.Python, InterpolationInSingleQuotes),
        new CodePattern("logic-php-variable-in-single-quotes", CodePattern.Files(".php"), Syntax.CLike,
            (id, source, masked) => VariableInSingleQuotes(id, source, masked, "PHP", "\"\\")),
        new CodePattern("logic-perl-variable-in-single-quotes", CodePattern.Files(".pl", ".pm"), Syntax.Perl,
            (id, source, masked) => VariableInSingleQuotes(id, source, masked, "Perl", "\"\\@")),
        new CodePattern("logic-fsharp-text-without-dollar", CodePattern.Files(".fsx"), Syntax.FSharp, FSharpTextWithoutDollar),
        new CodePattern("logic-scala-text-without-s", CodePattern.Files(".scala", ".sc"), Syntax.Scala, ScalaTextWithoutS),
    ];

    [GeneratedRegex(@"\{(?<name>[A-Za-z_]\w*)(?:\.\w+)*(?::[^{}""\s]*)?\}")]
    private static partial Regex NameInBraces();

    [GeneratedRegex(@"#\{\s*(?<name>[A-Za-z_]\w*)[^}]*\}")]
    private static partial Regex RubyHole();

    [GeneratedRegex(@"\$(?<name>[A-Za-z_]\w*)")]
    private static partial Regex DollarName();

    /// <summary>Each piece of text between a pair of <paramref name="quote"/> marks on a masked line: where it opens and closes.</summary>
    private static IEnumerable<(int Open, int Close)> Quoted(string code, char quote)
    {
        for (var open = code.IndexOf(quote); open >= 0; open = code.IndexOf(quote, open + 1))
        {
            var close = code.IndexOf(quote, open + 1);
            if (close < 0) yield break;

            yield return (open, close);
            open = close;
        }
    }

    /// <summary>Every name the code uses outside its text and comments - the names a value can be put into text from.</summary>
    private static HashSet<string> NamesInCode(IReadOnlyList<string> masked) =>
        masked.SelectMany(code => Regex.Matches(code, @"[A-Za-z_]\w*")).Select(found => found.Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The names a C# file declares - a type and then the name, as a variable, a parameter or a loop's variable: int total =,
    /// string name), var mark in marks.
    /// </summary>
    private static HashSet<string> DeclaredNames(IReadOnlyList<string> masked) =>
        masked.SelectMany(code => Regex.Matches(code,
                @"\b(?!(?:return|else|new|throw|case|await|yield|in|is|as|out|ref|using|namespace|class|struct|record|enum|interface)\b)" +
                @"[A-Za-z_][\w<>\[\],?.]*\s+(?<name>[A-Za-z_]\w*)\s*(?:[=;,)]|\bin\b)"))
            .Select(found => found.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The names that hold text something else fills in - string.Format(template, ...), template.Replace(...), or a call that
    /// is given the text and then the values for it, Console.WriteLine(template, total).
    /// </summary>
    private static HashSet<string> TemplateNames(IReadOnlyList<string> masked) =>
        masked.SelectMany(code => Regex.Matches(code, @"Format\(\s*(?<name>[A-Za-z_]\w*)\b|\b(?<name>[A-Za-z_]\w*)\.Replace\(|\(\s*(?<name>[A-Za-z_]\w*)\s*,"))
            .Select(found => found.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// "Total: {total}" with no $ before it, where the file declares every name in braces and the text is used as it is: not
    /// followed by values for it to be formatted with, as in Console.WriteLine("{0}", total), not an attribute's route, as in
    /// [HttpGet("{id}")], and not kept in a name that is filled in later. Without $, C# puts no value into text, and the braces
    /// are printed as they are written.
    /// </summary>
    private static IEnumerable<LogicFinding> CSharpTextWithoutDollar(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var declared = DeclaredNames(masked);
        var templates = TemplateNames(masked);

        for (var index = 0; index < masked.Count; index++)
        {
            var line = source.Lines[index];
            var code = masked[index];

            // An attribute's route; raw text; and a line with interpolated text already on it, whose braces can hold quotes of
            // their own - $"{(passed ? "yes" : "no")} now" - so its quotes cannot be paired one after another.
            if (code.TrimStart().StartsWith('[') || code.Contains("\"\"\"", StringComparison.Ordinal) ||
                Regex.IsMatch(code, @"(?:\$@?|@\$)""")) continue;

            foreach (var (open, close) in Quoted(code, '"'))
            {
                if (open > 0 && code[open - 1] == '@') continue;

                var after = code[(close + 1)..].TrimStart();
                if (after.Length == 0 || after[0] is not (')' or ';' or '+')) continue;

                var text = line[(open + 1)..close];
                var named = NameInBraces().Matches(text).Select(found => found.Groups["name"].Value).ToList();
                if (named.Count == 0 || !named.All(declared.Contains)) continue;

                if (Regex.Match(code[..open], @"\b(?<name>[A-Za-z_]\w*)\s*=\s*$") is { Success: true } assigned &&
                    templates.Contains(assigned.Groups["name"].Value)) continue;

                yield return new LogicFinding(id, index + 1,
                    $"the text \"{text}\" has {{{named[0]}}} in it but no $ before its opening quote - so C# prints {{{named[0]}}} as it is " +
                    $"written, braces and all, not the value of {named[0]}",
                    LocalFix.ReplaceLine(id, "Make the text interpolated: put $ before the quote",
                        $"Only interpolated text - text with a $ before its opening quote - has each {{name}} in it replaced by that name's " +
                        $"value. Without the $, {{{named[0]}}} is just text, and is printed exactly as it is written.",
                        source.Path, index + 1, line[..open] + "$" + line[open..]));
                break;
            }
        }
    }

    /// <summary>
    /// "Hello, {name}" with no $ before it, where the program uses name: F#, like C#, puts a value into text only when the text
    /// is interpolated. Text with a % in it is a printf format, whose values follow it, and is left alone; so is a line that has
    /// interpolated, verbatim or triple-quoted text already, whose quotes cannot be paired one after another.
    /// </summary>
    private static IEnumerable<LogicFinding> FSharpTextWithoutDollar(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var names = NamesInCode(masked);

        for (var index = 0; index < masked.Count; index++)
        {
            var line = source.Lines[index];
            var code = masked[index];

            if (code.Contains("$\"", StringComparison.Ordinal) || code.Contains("@\"", StringComparison.Ordinal) ||
                code.Contains("\"\"\"", StringComparison.Ordinal)) continue;

            foreach (var (open, close) in Quoted(code, '"'))
            {
                var text = line[(open + 1)..close];
                if (text.Contains('%') || code[(close + 1)..].TrimStart().StartsWith(',')) continue;

                var named = NameInBraces().Matches(text).Select(found => found.Groups["name"].Value).ToList();
                if (named.Count == 0 || !named.All(names.Contains)) continue;

                yield return new LogicFinding(id, index + 1,
                    $"the text \"{text}\" has {{{named[0]}}} in it but no $ before its opening quote - so F# prints {{{named[0]}}} as it is " +
                    $"written, braces and all, not the value of {named[0]}",
                    LocalFix.ReplaceLine(id, "Make the text interpolated: put $ before the quote",
                        $"Only interpolated text - text with a $ before its opening quote - has each {{name}} in it replaced by that name's " +
                        $"value. Without the $, {{{named[0]}}} is just text, and is printed exactly as it is written.",
                        source.Path, index + 1, line[..open] + "$" + line[open..]));
                break;
            }
        }
    }

    /// <summary>
    /// "Hello, $name" or "Total: ${marks.sum}" with no s before it, where every $ names something the program uses: Scala puts a
    /// value into text only through an interpolator - s"...", f"..." - and plain text writes $name as it is. A line with
    /// triple-quoted text is left alone, as its quotes cannot be paired one after another.
    /// </summary>
    private static IEnumerable<LogicFinding> ScalaTextWithoutS(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var names = NamesInCode(masked);

        for (var index = 0; index < masked.Count; index++)
        {
            var line = source.Lines[index];
            var code = masked[index];

            if (code.Contains("\"\"\"", StringComparison.Ordinal)) continue;

            foreach (var (open, close) in Quoted(code, '"'))
            {
                if (open > 0 && CodeText.IsWordChar(code[open - 1])) continue;

                var text = line[(open + 1)..close];
                var named = Regex.Matches(text, @"\$(?:\{\s*)?(?<name>[A-Za-z_]\w*)").Select(found => found.Groups["name"].Value).ToList();
                if (named.Count == 0 || !named.All(names.Contains) || text.Count(character => character == '$') != named.Count) continue;

                yield return new LogicFinding(id, index + 1,
                    $"the text \"{text}\" has ${named[0]} in it but no s before its opening quote - so Scala prints ${named[0]} as it is " +
                    $"written, not the value of {named[0]}",
                    LocalFix.ReplaceLine(id, "Put s before the quote",
                        $"Only text with an interpolator before its opening quote - s\"...\" - has each $name and ${{...}} in it replaced by " +
                        $"its value. Without the s, ${named[0]} is just text, and is printed exactly as it is written.",
                        source.Path, index + 1, line[..open] + "s" + line[open..]));
                break;
            }
        }
    }

    /// <summary>
    /// '...#{name}...' in single quotes, where every name in #{} is one the program uses: Ruby puts a value into text only in
    /// double quotes - in single quotes, #{name} is written as it is. The quotes become double ones when the text holds no " or \
    /// of its own, which double quotes would read differently.
    /// </summary>
    private static IEnumerable<LogicFinding> InterpolationInSingleQuotes(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var names = NamesInCode(masked);

        for (var index = 0; index < masked.Count; index++)
        {
            var line = source.Lines[index];

            foreach (var (open, close) in Quoted(masked[index], '\''))
            {
                var text = line[(open + 1)..close];
                var named = RubyHole().Matches(text).Select(found => found.Groups["name"].Value).ToList();
                if (named.Count == 0 || !named.All(names.Contains)) continue;

                var hole = RubyHole().Match(text).Value;
                var fix = text.Contains('"') || text.Contains('\\')
                    ? null
                    : LocalFix.ReplaceLine(id, "Put the text in double quotes",
                        $"Only text in double quotes has each #{{...}} in it replaced by the value of what is between the braces. In single " +
                        $"quotes, {hole} is just text, and is printed exactly as it is written.",
                        source.Path, index + 1, line[..open] + "\"" + text + "\"" + line[(close + 1)..]);

                yield return new LogicFinding(id, index + 1,
                    $"the text '{text}' is in single quotes, so Ruby prints {hole} as it is written - not the value of {named[0]}: only " +
                    "double quotes put a value into text",
                    fix);
                break;
            }
        }
    }

    /// <summary>
    /// 'Hello, $name' in single quotes, where the program has a $name: PHP and Perl put a variable's value only into text in
    /// double quotes - in single quotes, $name is written as it is. The quotes become double ones when every $ in the text is
    /// a variable the program has, and the text holds none of <paramref name="readDifferently"/> - the characters double
    /// quotes read differently: " and \, and in Perl @, which puts an array into the text.
    /// </summary>
    private static IEnumerable<LogicFinding> VariableInSingleQuotes(
        string id, SourceFile source, IReadOnlyList<string> masked, string language, string readDifferently)
    {
        var variables = masked.SelectMany(code => DollarName().Matches(code)).Select(found => found.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < masked.Count; index++)
        {
            var line = source.Lines[index];

            foreach (var (open, close) in Quoted(masked[index], '\''))
            {
                var text = line[(open + 1)..close];
                var named = DollarName().Matches(text).Select(found => found.Groups["name"].Value).ToList();
                if (named.Count == 0 || !named.All(variables.Contains)) continue;

                var onlyVariables = text.Count(character => character == '$') == named.Count;
                var fix = !onlyVariables || text.Any(readDifferently.Contains)
                    ? null
                    : LocalFix.ReplaceLine(id, "Put the text in double quotes",
                        $"Only text in double quotes has each variable in it replaced by its value: \"{text}\" holds the value of ${named[0]}.",
                        source.Path, index + 1, line[..open] + "\"" + text + "\"" + line[(close + 1)..]);

                yield return new LogicFinding(id, index + 1,
                    $"the text '{text}' is in single quotes, so {language} prints ${named[0]} as it is written - the word ${named[0]}, not " +
                    "its value: only double quotes put a variable's value into text",
                    fix);
                break;
            }
        }
    }
}
