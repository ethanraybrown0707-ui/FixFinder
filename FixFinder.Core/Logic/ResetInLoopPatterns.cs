using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// A total set back to 0 as the first thing a loop does on every pass - for each mark: total = 0, total += mark - so after the
/// loop it holds only the last item's part: 80 for marks of 60, 70 and 80, where 210 was meant. One check, read in each
/// language's own words: a loop's body is found by its braces where the language has them, and by its indentation where it
/// ends with end, end do or nothing at all. The change takes the reset out when the total was given the same value before
/// the loop, and otherwise moves it there, so the total is started once.
/// </summary>
public static partial class ResetInLoopPatterns
{
    /// <summary>How one language writes the loops this check reads.</summary>
    /// <param name="Id">The check's id for the language.</param>
    /// <param name="Header">A loop's first line: for, foreach, while, .each do, do i = 1, n.</param>
    /// <param name="Braced">Whether the loop's body is between braces; otherwise it is the lines indented under its first.</param>
    private sealed record Dialect(string Id, IReadOnlySet<string> Files, Syntax Syntax, Regex Header, bool Braced);

    private static readonly Dialect[] Dialects =
    [
        new("logic-reset-in-loop", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++", ".java", ".cs", ".js", ".mjs", ".cjs"), Syntax.CLike,
            new(@"^\s*(?:for|foreach|while)\s*\(.*\)\s*\{?\s*$"), Braced: true),
        new("logic-go-reset-in-loop", CodePattern.Files(".go"), Syntax.CLike, new(@"^\s*for\b.*\{\s*$"), Braced: true),
        new("logic-rust-reset-in-loop", CodePattern.Files(".rs"), Syntax.CLike, new(@"^\s*(?:for|while)\b.*\{\s*$|^\s*loop\s*\{\s*$"), Braced: true),
        new("logic-php-reset-in-loop", CodePattern.Files(".php"), Syntax.CLike, new(@"^\s*(?:for|foreach|while)\s*\(.*\)\s*\{?\s*$"), Braced: true),
        new("logic-perl-reset-in-loop", CodePattern.Files(".pl", ".pm"), Syntax.Perl, new(@"^\s*(?:for|foreach|while)\b.*\{\s*$"), Braced: true),
        new("logic-dart-reset-in-loop", CodePattern.Files(".dart"), Syntax.CLike, new(@"^\s*(?:for|while)\s*\(.*\)\s*\{?\s*$"), Braced: true),
        new("logic-powershell-reset-in-loop", CodePattern.Files(".ps1"), Syntax.Python, new(@"^\s*(?:for|foreach|while)\s*\(.*\)\s*\{?\s*$", RegexOptions.IgnoreCase), Braced: true),
        new("logic-scala-reset-in-loop", CodePattern.Files(".scala", ".sc"), Syntax.Scala, new(@"^\s*(?:for|while)\b.*(?:\bdo|\{)\s*$"), Braced: false),
        new("logic-ruby-reset-in-loop", CodePattern.Files(".rb"), Syntax.Python, new(@"^\s*(?:(?:for|while|until)\b.*|.*\bdo\s*(?:\|[^|]*\|)?)\s*$"), Braced: false),
        new("logic-lua-reset-in-loop", CodePattern.Files(".lua"), Syntax.Lua, new(@"^\s*(?:for|while)\b.*\bdo\s*$"), Braced: false),
        new("logic-fortran-reset-in-loop", CodePattern.Files(".f90", ".f95", ".f03", ".f08"), Syntax.Fortran, new(@"^\s*do\b", RegexOptions.IgnoreCase), Braced: false),
        new("logic-fsharp-reset-in-loop", CodePattern.Files(".fsx", ".fs"), Syntax.FSharp, new(@"^\s*(?:for|while)\b.*\bdo\s*$"), Braced: false),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
        [.. Dialects.Select(dialect => new CodePattern(dialect.Id, dialect.Files, dialect.Syntax, (id, source, masked) => ResetInLoop(id, source, masked, dialect)))];

    /// <summary>A name set back to 0, 0.0, 1 or empty text, as a whole line: total = 0, $total = 0;, total &lt;- 0.</summary>
    [GeneratedRegex(@"^\s*(?<name>\$?[A-Za-z_]\w*)\s*(?:=|<-)\s*(?<value>0|0\.0|0[dD]0|1|""\s*""|'\s*')\s*;?\s*$")]
    private static partial Regex SetBack();

    private static IEnumerable<LogicFinding> ResetInLoop(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var lines = source.Lines;

        for (var header = 0; header < masked.Count; header++)
        {
            if (!dialect.Header.IsMatch(masked[header]) || Body(masked, header, dialect.Braced) is not (var first, var last, var close)) continue;
            if (SetBack().Match(masked[first]) is not { Success: true } reset) continue;

            var name = reset.Groups["name"].Value;
            var value = reset.Groups["value"].Value;
            var n = Regex.Escape(name);
            var body = Enumerable.Range(first + 1, Math.Max(0, last - first)).Select(lineIndex => masked[lineIndex]).ToList();

            // Built up in the loop - total += mark, total = total + mark, total <- total + mark - and given nothing else there.
            if (!body.Any(text => Regex.IsMatch(text, $@"(?<![\w$.]){n}\s*(?:\+=|-=|\*=|\+\+|--)|(?<![\w$.]){n}\s*(?:=|<-)\s*{n}\s*[+\-*]"))) continue;
            if (body.Any(text => Regex.IsMatch(text, $@"(?<![\w$.]){n}\s*(?:=|<-)(?!=)(?!\s*{n}\b)"))) continue;

            // Used after the loop, before the code it is in ends - read in the code as written, as Perl's print "$total" and
            // Ruby's "#{total}" use it inside text.
            if (!AfterTheLoop(masked, header, close).Any(lineIndex => Regex.IsMatch(lines[lineIndex], $@"(?<![\w$.]){n}(?![\w$])"))) continue;

            var message = $"`{name}` is set back to {value} at the start of every pass, so after the loop it only holds the last item's part";

            // Given the same value before the loop, the reset only has to go.
            if (ValueBefore(masked, header, name) == value)
            {
                yield return new LogicFinding(id, first + 1, message, new LocalFix
                {
                    RuleId = id,
                    Title = $"Take out the {name} = {value} inside the loop",
                    Explanation =
                        $"`{name}` is already {value} before the loop, and `{lines[first].Trim()}` inside it sets it back on every pass, throwing " +
                        $"away what the passes before it added. Without it, `{name}` keeps building up across every pass.",
                    File = source.Path, StartLine = first + 1, RemoveCount = 1, NewLines = [],
                });
                continue;
            }

            yield return new LogicFinding(id, first + 1, message, new LocalFix
            {
                RuleId = id,
                Title = $"Start {name} once, before the loop",
                Explanation =
                    $"`{lines[first].Trim()}` is inside the loop, so every pass throws away what the passes before it added and starts again. " +
                    $"Set once before the loop, `{name}` keeps building up across every pass.",
                File = source.Path, StartLine = header + 1, RemoveCount = first - header + 1,
                NewLines = [CodeText.Indentation(lines[header]) + lines[first].Trim(), lines[header], .. lines.Skip(header + 1).Take(first - header - 1)],
            });
        }
    }

    /// <summary>
    /// A loop's body: its first statement, its last line, and the line that closes it - the brace that matches its own, after
    /// a { on a line of its own if that is where it is; or, without braces, the lines indented under its first line.
    /// </summary>
    private static (int First, int Last, int Close)? Body(IReadOnlyList<string> masked, int header, bool braced)
    {
        var headerIndent = CodeText.Indentation(masked[header]).Length;

        if (braced)
        {
            if (Brackets.BlockEnd(masked, header) is not { } close) return null;

            var first = NextCode(masked, header + 1);
            if (first >= 0 && masked[first].Trim() == "{") first = NextCode(masked, first + 1);
            return first < 0 || first >= close ? null : (first, close - 1, close);
        }

        var firstIndented = NextCode(masked, header + 1);
        if (firstIndented < 0 || CodeText.Indentation(masked[firstIndented]).Length <= headerIndent) return null;

        var lastIndented = firstIndented;
        for (var lineIndex = firstIndented + 1; lineIndex < masked.Count; lineIndex++)
        {
            if (masked[lineIndex].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[lineIndex]).Length <= headerIndent) break;
            lastIndented = lineIndex;
        }

        return (firstIndented, lastIndented, lastIndented);
    }

    /// <summary>The lines after a loop that are still in the code the loop is in: those indented at least as far as its first line.</summary>
    private static IEnumerable<int> AfterTheLoop(IReadOnlyList<string> masked, int header, int close)
    {
        var headerIndent = CodeText.Indentation(masked[header]).Length;

        for (var lineIndex = close + 1; lineIndex < masked.Count; lineIndex++)
        {
            if (masked[lineIndex].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[lineIndex]).Length < headerIndent) yield break;
            yield return lineIndex;
        }
    }

    /// <summary>
    /// The value the name was last given above the loop, as written - int total = 0;, total := 0, let mut total = 0;, my $total
    /// = 0; - or null when it was given none there.
    /// </summary>
    private static string? ValueBefore(IReadOnlyList<string> masked, int header, string name)
    {
        for (var lineIndex = header - 1; lineIndex >= 0; lineIndex--)
        {
            if (Regex.Match(masked[lineIndex], $@"(?<![\w$.]){Regex.Escape(name)}(?:\s*:\s*[\w<>\[\]]+)?\s*(?::=|=|<-)(?!=)\s*(?<value>[^;]+?)\s*;?\s*$")
                is { Success: true } given)
                return given.Groups["value"].Value;
        }

        return null;
    }

    private static int NextCode(IReadOnlyList<string> masked, int from)
    {
        for (var lineIndex = from; lineIndex < masked.Count; lineIndex++)
            if (masked[lineIndex].Trim().Length > 0) return lineIndex;

        return -1;
    }
}
