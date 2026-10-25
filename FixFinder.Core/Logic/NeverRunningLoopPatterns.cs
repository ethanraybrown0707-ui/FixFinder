using System.Globalization;
using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A loop meant to count down, written the way that counts up, so it runs no times at all - and nothing in it can be tested:
/// range(10, 0) in Python, (10..1).each in Ruby, 10..0 in Rust, 10 to 1 in Scala, for i = 10, 1 in Lua, for i in 10 .. 1 and
/// for i = 10 to 1 in F#, and (10..1) in Perl. Each was run, counting the times round, and each counted 0, with nothing said by
/// python -W error, ruby -w, rustc, Scala CLI, Lua, F# Interactive or perl -w. Fortran's do i = 10, 1 counted 0 too, and
/// gfortran -Wall warns that it "will be executed zero times" - the warning and this check are one finding, which gives the
/// change. Elixir's 10..1 is not here, as it counts down, and warns. The change counts down: range(10, 0, -1), 10.downto(1),
/// (1..=10).rev(), 10 to 1 by -1, for i = 10, 1, -1, do i = 10, 1, -1, 10 .. -1 .. 1 or downto, reverse(1..10).
/// </summary>
public static partial class NeverRunningLoopPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Loop("logic-python-loop-never-runs", CodePattern.Files(".py", ".pyw"), Syntax.Python, PythonRange(), PythonChange),
        Loop("logic-ruby-loop-never-runs", CodePattern.Files(".rb"), Syntax.Python, RubyRange(), RubyChange),
        Loop("logic-rust-loop-never-runs", CodePattern.Files(".rs"), Syntax.CLike, RustRange(), RustChange),
        Loop("logic-scala-loop-never-runs", CodePattern.Files(".scala", ".sc"), Syntax.Scala, ScalaRange(), ScalaChange),
        Loop("logic-lua-loop-never-runs", CodePattern.Files(".lua"), Syntax.Lua, LuaRange(), LuaChange),
        Loop("logic-fsharp-loop-never-runs", CodePattern.Files(".fsx"), Syntax.FSharp, FSharpRange(), FSharpChange),
        Loop("logic-perl-loop-never-runs", CodePattern.Files(".pl", ".pm"), Syntax.Perl, PerlRange(), PerlChange),
        Loop("logic-fortran-loop-never-runs", CodePattern.Files(".f90", ".f95", ".f03", ".f08"), Syntax.Fortran, FortranRange(), LuaChange),
    ];

    /// <summary>do i = 10, 1 - two whole numbers, the first larger, and no step.</summary>
    [GeneratedRegex(@"^\s*(?:\w+\s*:\s*)?do\s+[A-Za-z_]\w*\s*=\s*(?<range>(?<start>-?\d+)\s*,\s*(?<stop>-?\d+))\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FortranRange();

    private static CodePattern Loop(string id, IReadOnlySet<string> extensions, Syntax syntax, Regex range, Func<Match, string?> change) =>
        new(id, extensions, syntax, (patternId, source, masked) => NeverRuns(patternId, source, masked, range, change), Severity.Error, Confidence.Certain);

    private static double Number(Match match, string group) => double.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    /// <summary>for i in range(10, 0): - two whole numbers, the first larger, and no step.</summary>
    [GeneratedRegex(@"\bfor\s+[A-Za-z_]\w*\s+in\s+(?<range>range\(\s*(?<start>-?\d+)\s*,\s*(?<stop>-?\d+)\s*\))")]
    private static partial Regex PythonRange();

    private static string? PythonChange(Match loop) => $"range({loop.Groups["start"].Value}, {loop.Groups["stop"].Value}, -1)";

    /// <summary>(10..1).each or for i in 10..1 - a range whose first number is the larger.</summary>
    [GeneratedRegex(@"(?<range>\(\s*(?<start>-?\d+)\s*(?<dots>\.\.\.?)\s*(?<stop>-?\d+)\s*\)(?=\s*\.\s*(?:each|map|each_with_index|select|sum|to_a)\b)|(?<=\bfor\s+[a-z_]\w*\s+in\s+)\(?\s*(?<start>-?\d+)\s*(?<dots>\.\.\.?)\s*(?<stop>-?\d+)\s*\)?)")]
    private static partial Regex RubyRange();

    private static string? RubyChange(Match loop)
    {
        var last = loop.Groups["dots"].Value == "..." ? Number(loop, "stop") + 1 : Number(loop, "stop");
        return $"{loop.Groups["start"].Value}.downto({last.ToString(CultureInfo.InvariantCulture)})";
    }

    /// <summary>for i in 10..0 or 10..=1, with or without brackets, and no .rev().</summary>
    [GeneratedRegex(@"\bfor\s+\w+\s+in\s+(?<range>\(?\s*(?<start>-?\d+)\s*\.\.(?<inclusive>=?)\s*(?<stop>-?\d+)(?:\s*\))?)(?![)\s]*\.)")]
    private static partial Regex RustRange();

    private static string? RustChange(Match loop)
    {
        var first = loop.Groups["inclusive"].Value == "=" ? Number(loop, "stop") : Number(loop, "stop") + 1;
        return $"({first.ToString(CultureInfo.InvariantCulture)}..={loop.Groups["start"].Value}).rev()";
    }

    /// <summary>10 to 1 or 10 until 0, with no by after it.</summary>
    [GeneratedRegex(@"(?<range>(?<![\w.])(?<start>-?\d+)\s+(?<kind>to|until)\s+(?<stop>-?\d+))(?!\s+by\b)(?![\w.])")]
    private static partial Regex ScalaRange();

    private static string? ScalaChange(Match loop) => $"{loop.Groups["start"].Value} {loop.Groups["kind"].Value} {loop.Groups["stop"].Value} by -1";

    /// <summary>for i = 10, 1 do - two numbers, the first larger, and no step.</summary>
    [GeneratedRegex(@"\bfor\s+[A-Za-z_]\w*\s*=\s*(?<range>(?<start>-?\d+)\s*,\s*(?<stop>-?\d+))\s+do\b")]
    private static partial Regex LuaRange();

    private static string? LuaChange(Match loop) => $"{loop.Groups["start"].Value}, {loop.Groups["stop"].Value}, -1";

    /// <summary>for i in 10 .. 1 do, or for i = 10 to 1 do.</summary>
    [GeneratedRegex(@"\bfor\s+[a-z_]\w*\s+in\s+(?<range>(?<start>-?\d+)\s*\.\.\s*(?<stop>-?\d+))\s+do\b|\bfor\s+[a-z_]\w*\s*=\s*(?<range>(?<start>-?\d+)\s+(?<to>to)\s+(?<stop>-?\d+))\s+do\b")]
    private static partial Regex FSharpRange();

    private static string? FSharpChange(Match loop) => loop.Groups["to"].Success
        ? $"{loop.Groups["start"].Value} downto {loop.Groups["stop"].Value}"
        : $"{loop.Groups["start"].Value} .. -1 .. {loop.Groups["stop"].Value}";

    /// <summary>for my $i (10..1) or foreach (10..1) - a range in a for whose first number is the larger.</summary>
    [GeneratedRegex(@"\b(?:for|foreach)\b(?:\s+my)?(?:\s+\$\w+)?\s*\(\s*(?<range>(?<start>-?\d+)\s*\.\.\s*(?<stop>-?\d+))\s*\)")]
    private static partial Regex PerlRange();

    private static string? PerlChange(Match loop) => $"reverse({loop.Groups["stop"].Value}..{loop.Groups["start"].Value})";

    private static IEnumerable<LogicFinding> NeverRuns(string id, SourceFile source, IReadOnlyList<string> masked, Regex range, Func<Match, string?> change)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match loop in range.Matches(masked[index]))
            {
                if (Number(loop, "start") <= Number(loop, "stop")) continue;

                var line = source.Lines[index];
                var rangeGroup = loop.Groups["range"];
                var written = line.Substring(rangeGroup.Index, rangeGroup.Length).Trim();
                var changed = change(loop);

                yield return new LogicFinding(id, index + 1,
                    $"{written} counts up from {loop.Groups["start"].Value}, and {loop.Groups["start"].Value} is already past " +
                    $"{loop.Groups["stop"].Value} - so it holds nothing, the loop runs no times at all, and nothing inside it can be tested",
                    changed is null ? null : LocalFix.ReplaceLine(id, $"Count down: {changed}",
                        $"{changed} goes from {loop.Groups["start"].Value} down towards {loop.Groups["stop"].Value}, one step at a time.",
                        source.Path, index + 1, line[..rangeGroup.Index] + changed + line[(rangeGroup.Index + rangeGroup.Length)..]));
            }
        }
    }
}
