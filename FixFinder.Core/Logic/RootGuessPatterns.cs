using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A square root by Newton's method - each guess the average of the last and the number over it, (guess + x / guess) / 2 -
/// that stops when the guess is near the number itself, abs(x - guess), where it is the guess's square that comes near
/// the number. The guess heads for the square root, which stays as far from the number as the root is from it - 0.586 for
/// 2 - so the test is never passed, and the loop goes on for ever. QuixBugs' sqrt is this mistake, in Python and in Java.
/// The change tests the guess's square, as QuixBugs' correct programs do.
/// </summary>
/// <remarks>
/// Said only when the file shows both: abs(x - guess), or abs(guess - x), however the language writes it - abs, Math.abs,
/// Math.Abs, math.Abs, fabs, std::abs, [Math]::Abs, Float.abs, (x - guess).abs() - and, within ten lines of it, the step
/// that makes the next guess from the same two names: (guess + x / guess) / 2, 0.5 * (guess + x / guess), (x / guess +
/// guess) * 0.5, OCaml's (guess +. x /. guess) /. 2.0. A walk halving the way to a target, (position + target) / 2, is
/// not this step, and testing the guess's square, its cube or two guesses in a row is left alone.
/// </remarks>
public static partial class RootGuessPatterns
{
    /// <summary>How one language writes the guess's square, given the guess's name and whether the subtraction had spaces round it.</summary>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, Func<string, bool, string> Squared, bool IgnoreCase = false);

    private static string Times(string guess, bool spaced) => spaced ? $"{guess} * {guess}" : $"{guess}*{guess}";

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, (guess, spaced) => spaced ? $"{guess} ** 2" : $"{guess}**2"),
        new("java", CodePattern.Files(".java"), Syntax.CLike, Times),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, Times),
        new("c", CodePattern.Files(".c"), Syntax.CLike, Times),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, Times),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, Times),
        new("go", CodePattern.Files(".go"), Syntax.CLike, Times),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, Times),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, Times),
        new("php", CodePattern.Files(".php"), Syntax.CLike, Times),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, Times),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, Times),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, Times, IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, Times),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, Times),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08"), Syntax.Fortran, Times, IgnoreCase: true),
        new("elixir", CodePattern.Files(".ex", ".exs"), Syntax.Python, Times),
        new("erlang", CodePattern.Files(".erl"), Syntax.Erlang, Times),
        new("fsharp", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp, Times),
        new("ocaml", CodePattern.Files(".ml"), Syntax.OCaml, (guess, spaced) => spaced ? $"{guess} *. {guess}" : $"{guess}*.{guess}"),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-root-guess-not-squared", dialect.Files, dialect.Syntax,
            (id, source, masked) => GuessesNotSquared(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>
    /// The distance between two names, its absolute value taken: abs(x - guess), Math.abs(x-approx), [Math]::Abs($x - $guess),
    /// Float.abs (x -. guess), (x - guess).abs() - the names in the groups named left and right, the subtraction in the group named minus.
    /// </summary>
    [GeneratedRegex(@"(?:\b(?:Math\.abs|Math\.Abs|math\.Abs|math\.abs|std::f?abs|fabs|abs|Float\.abs|abs_float)|\[Math\]::Abs)\s*\(\s*(?<left>\$?[A-Za-z_]\w*)(?<minus>\s*-\.?\s*)(?<right>\$?[A-Za-z_]\w*)\s*\)|\(\s*(?<left>\$?[A-Za-z_]\w*)(?<minus>\s*-\.?\s*)(?<right>\$?[A-Za-z_]\w*)\s*\)\s*\.\s*abs\b", RegexOptions.IgnoreCase)]
    private static partial Regex Distance();

    private static IEnumerable<LogicFinding> GuessesNotSquared(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var comparison = dialect.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        for (var line = 0; line < masked.Count; line++)
        {
            // abs on the line first - the regex starting at every bracket is slow over a whole file.
            if (masked[line].IndexOf("abs", StringComparison.OrdinalIgnoreCase) < 0) continue;

            foreach (Match distance in Distance().Matches(masked[line]))
            {
                var (left, right) = (distance.Groups["left"].Value, distance.Groups["right"].Value);
                if (left.Equals(right, comparison)) continue;

                // Which of the two is the guess: the one the step averages with the number over it.
                var guessOnTheRight = StepNear(masked, line, number: left, guess: right, dialect.IgnoreCase);
                if (!guessOnTheRight && !StepNear(masked, line, number: right, guess: left, dialect.IgnoreCase)) continue;

                var guess = distance.Groups[guessOnTheRight ? "right" : "left"];
                var number = guessOnTheRight ? left : right;
                var written = source.Lines[line];
                var spaced = distance.Groups["minus"].Value.Contains(' ');
                var squared = dialect.Squared(written.Substring(guess.Index, guess.Length), spaced);
                var changed = written[..guess.Index] + squared + written[(guess.Index + guess.Length)..];

                var told = $"Line {line + 1} stops when `{guess.Value}` is near `{number}` itself - but each guess averages the last with `{number}` over it, " +
                           $"which heads for the square root of `{number}`, so it is the guess's square that comes near `{number}`. The root is not near the " +
                           "number unless the number is near 1 - for 2, the root 1.414 stays 0.586 away - so the test is never passed and it goes on for ever.";
                yield return new LogicFinding(id, line + 1, told,
                    LocalFix.ReplaceLine(id, $"Test the guess's square: {squared}",
                        $"Newton's method for a square root is done when the guess times itself is within the tolerance of the number.",
                        source.Path, line + 1, changed));
                break;
            }
        }
    }

    /// <summary>
    /// Whether the step that makes the next guess - (guess + number / guess) / 2, 0.5 * (guess + number / guess), (number /
    /// guess + guess) * 0.5, with OCaml's +. /. *. - is written within ten lines of the test.
    /// </summary>
    private static bool StepNear(IReadOnlyList<string> masked, int line, string number, string guess, bool ignoreCase)
    {
        var (n, g) = (Regex.Escape(number), Regex.Escape(guess));
        var average = $@"(?:\(\s*{g}\s*\+\.?\s*{n}\s*/\.?\s*{g}\s*\)|\(\s*{n}\s*/\.?\s*{g}\s*\+\.?\s*{g}\s*\))";
        var half = @"(?:/\.?\s*2(?:\.0*)?[dDfF]?(?![\w.])|\*\.?\s*0?\.50*[dDfF]?(?![\w.]))";
        var step = new Regex($@"{average}\s*{half}|(?<![\w.])0?\.50*[dDfF]?\s*\*\.?\s*{average}", ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        for (var near = Math.Max(0, line - 10); near < Math.Min(masked.Count, line + 11); near++)
        {
            if (step.IsMatch(masked[near])) return true;
        }

        return false;
    }
}
