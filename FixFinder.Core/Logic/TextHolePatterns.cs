using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A value meant to be put into text, in text that puts nothing in - each run with its language's own toolchain, which said
/// nothing of either: C#'s "Total: {total}" without the $ that makes it interpolated printed Total: {total}, and Ruby's
/// 'Hello, #{name}' in single quotes printed Hello, #{name}.
/// </summary>
public static partial class TextHolePatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-csharp-text-without-dollar", CodePattern.Files(".cs"), Syntax.CLike, TextWithoutDollar),
        new CodePattern("logic-ruby-interpolation-in-single-quotes", CodePattern.Files(".rb"), Syntax.Python, InterpolationInSingleQuotes),
    ];

    [GeneratedRegex(@"\{(?<name>[A-Za-z_]\w*)(?:\.\w+)*(?::[^{}""\s]*)?\}")]
    private static partial Regex NameInBraces();

    [GeneratedRegex(@"#\{\s*(?<name>[A-Za-z_]\w*)[^}]*\}")]
    private static partial Regex RubyHole();

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
    private static IEnumerable<LogicFinding> TextWithoutDollar(string id, SourceFile source, IReadOnlyList<string> masked)
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

            for (var open = code.IndexOf('"'); open >= 0; open = code.IndexOf('"', open + 1))
            {
                var close = code.IndexOf('"', open + 1);
                if (close < 0) break;

                var start = open;
                open = close;

                if (start > 0 && code[start - 1] is '$' or '@') continue;

                var after = code[(close + 1)..].TrimStart();
                if (after.Length == 0 || after[0] is not (')' or ';' or '+')) continue;

                var names = NameInBraces().Matches(line[(start + 1)..close]).Select(found => found.Groups["name"].Value).ToList();
                if (names.Count == 0 || !names.All(declared.Contains)) continue;

                if (Regex.Match(code[..start], @"\b(?<name>[A-Za-z_]\w*)\s*=\s*$") is { Success: true } assigned &&
                    templates.Contains(assigned.Groups["name"].Value)) continue;

                yield return new LogicFinding(id, index + 1,
                    $"the text \"{line[(start + 1)..close]}\" has {{{names[0]}}} in it but no $ before its opening quote - so C# prints " +
                    $"{{{names[0]}}} as it is written, braces and all, not the value of {names[0]}",
                    LocalFix.ReplaceLine(id, "Make the text interpolated: put $ before the quote",
                        $"Only interpolated text - text with a $ before its opening quote - has each {{name}} in it replaced by that name's " +
                        $"value. Without the $, {{{names[0]}}} is just text, and is printed exactly as it is written.",
                        source.Path, index + 1, line[..start] + "$" + line[start..]));
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
        var usedNames = masked.SelectMany(code => Regex.Matches(code, @"[A-Za-z_]\w*")).Select(found => found.Value).ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < masked.Count; index++)
        {
            var line = source.Lines[index];
            var code = masked[index];

            for (var open = code.IndexOf('\''); open >= 0; open = code.IndexOf('\'', open + 1))
            {
                var close = code.IndexOf('\'', open + 1);
                if (close < 0) break;

                var start = open;
                open = close;

                var text = line[(start + 1)..close];
                var names = RubyHole().Matches(text).Select(found => found.Groups["name"].Value).ToList();
                if (names.Count == 0 || !names.All(usedNames.Contains)) continue;

                var hole = RubyHole().Match(text).Value;
                var fix = text.Contains('"') || text.Contains('\\')
                    ? null
                    : LocalFix.ReplaceLine(id, "Put the text in double quotes",
                        $"Only text in double quotes has each #{{...}} in it replaced by the value of what is between the braces. In single " +
                        $"quotes, {hole} is just text, and is printed exactly as it is written.",
                        source.Path, index + 1, line[..start] + "\"" + text + "\"" + line[(close + 1)..]);

                yield return new LogicFinding(id, index + 1,
                    $"the text '{text}' is in single quotes, so Ruby prints {hole} as it is written - not the value of {names[0]}: only " +
                    "double quotes put a value into text",
                    fix);
                break;
            }
        }
    }
}
