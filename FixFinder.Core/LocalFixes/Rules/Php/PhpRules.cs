using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the PHP rules share: whether an error is PHP's, and the line it is on.</summary>
internal static class PhpCode
{
    public static bool IsPhp(SourceFile source) => Path.GetExtension(source.Path).Equals(".php", StringComparison.OrdinalIgnoreCase);

    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Error.LanguageId != "php" || context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!IsPhp(source) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }
}

/// <summary>
/// A parse error for the start of the next statement - "unexpected token "echo"", "unexpected variable "$x"" - on a line
/// that follows one with no semicolon at its end: PHP read the two as one statement, and only noticed at the second. The
/// semicolon goes at the end of the line before.
/// </summary>
public sealed partial class PhpMissingSemicolon : ILocalFixRule
{
    public string Id => "php-missing-semicolon";

    [GeneratedRegex(@"^syntax error, unexpected (?:token|variable|identifier|double-quoted string|integer)")]
    private static partial Regex Message();

    /// <summary>A line that can end without a semicolon: one that opens or closes a block, goes on, or is only PHP's opening tag.</summary>
    [GeneratedRegex(@"(?:[;{}(\[,:.]|<\?php|\?>)\s*$")]
    private static partial Regex EndsWithoutOne();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "ParseError" || !Message().IsMatch(context.Error.Message ?? "") || PhpCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        var before = at.Number - 2;
        while (before >= 0 && masked[before].Trim().Length == 0) before--;
        if (before < 0 || EndsWithoutOne().IsMatch(masked[before]) || masked[before].TrimStart().StartsWith("//", StringComparison.Ordinal)) return null;

        var line = at.Source.Lines[before];
        var code = CodeText.SplitComment(line, Syntax.CLike);

        return LocalFix.ReplaceLine(Id, "Put a ; at the end of the line before",
            "Each statement in PHP ends with a semicolon. Without one, PHP reads on into the next line as part of the same statement, and " +
            $"only notices on line {at.Number}, where the next statement starts.",
            at.Source.Path, before + 1, code.Code.TrimEnd() + ";" + code.Tail);
    }
}

/// <summary>
/// "Undefined variable $totl": PHP gives null in its place and carries on. When the file gives a value to one variable
/// whose name is within two letters of it - and only one - the name is changed to that one, on the line PHP names.
/// </summary>
public sealed partial class PhpNearestVariable : ILocalFixRule
{
    public string Id => "php-nearest-variable";

    /// <summary>How many letters apart a name and the one it is taken for may be.</summary>
    private const int MostLettersApart = 2;

    [GeneratedRegex(@"^Undefined variable \$(?<name>\w+)$")]
    private static partial Regex Message();

    [GeneratedRegex(@"\$(?<name>[A-Za-z_]\w*)\s*(?:=(?!=)|[-+*/.%]=)|function\s+\w+\s*\((?<parameters>[^)]*)\)|\bforeach\s*\([^)]*\bas\s+\$(?<loop>\w+)")]
    private static partial Regex GivenAValue();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "Warning" || Message().Match(context.Error.Message ?? "") is not { Success: true } undefined) return null;
        if (PhpCode.Locate(context) is not { } at) return null;

        var wrong = undefined.Groups["name"].Value;
        var named = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match given in GivenAValue().Matches(string.Join("\n", CodeText.MaskAll(at.Source.Lines, Syntax.CLike))))
        {
            if (given.Groups["name"].Success) named.Add(given.Groups["name"].Value);
            if (given.Groups["loop"].Success) named.Add(given.Groups["loop"].Value);
            foreach (Match parameter in Regex.Matches(given.Groups["parameters"].Value, @"\$(\w+)")) named.Add(parameter.Groups[1].Value);
        }

        var near = named.Where(name => name != wrong && CodeText.Distance(name, wrong) <= MostLettersApart).ToList();
        if (near is not [var right]) return null;

        var places = new Regex($@"\${Regex.Escape(wrong)}(?!\w)").Matches(CodeText.Mask(at.Line, Syntax.CLike));
        if (places is not [var place]) return null;

        var corrected = at.Line[..place.Index] + "$" + right + at.Line[(place.Index + place.Length)..];

        return LocalFix.ReplaceLine(Id, $"Change ${wrong} to ${right}",
            $"${wrong} is given no value anywhere, so PHP used null in its place. ${right}, a letter or two apart, is the variable the file gives a value to.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>DivisionByZeroError: the one division on the line it names is given an answer for when the divisor is zero.</summary>
public sealed partial class PhpDivisionGuard : ILocalFixRule
{
    public string Id => "php-division-guard";

    [GeneratedRegex(@"(?<dividend>\$?[A-Za-z_][\w>-]*(?:\([^()]*\))?|\d+|\([^()]*\))\s*/(?![/=*])\s*(?<divisor>\([^()]*\)|\$?[A-Za-z_][\w>-]*(?:\([^()]*\))?)")]
    private static partial Regex Division();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ShortExceptionType != "DivisionByZeroError" || PhpCode.Locate(context) is not { } at) return null;
        if (Division().Matches(CodeText.Mask(at.Line, Syntax.CLike)) is not [var division]) return null;

        var divisor = at.Line.Substring(division.Groups["divisor"].Index, division.Groups["divisor"].Length);
        var expression = at.Line.Substring(division.Index, division.Length);
        var corrected = at.Line[..division.Index] + $"({divisor} == 0 ? 0 : {expression})" + at.Line[(division.Index + division.Length)..];

        return LocalFix.ReplaceLine(Id, "Give an answer for when the divisor is zero",
            "Dividing by zero has no answer, so PHP stops the program with DivisionByZeroError. Checking the divisor first and giving 0 " +
            "in that case keeps the program going - change the 0 to whatever an empty case should give.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// "Undefined array key 3" from a loop <c>for ($i = 0; $i &lt;= count($marks); $i++)</c>: the loop takes one step more
/// than the array has positions. The change is the logic check's too.
/// </summary>
public sealed partial class PhpLoopToCount : ILocalFixRule
{
    public string Id => "php-loop-to-count";

    /// <summary>How far above the warning's line a loop's heading is looked for.</summary>
    private const int LinesAboveLooked = 6;

    [GeneratedRegex(@"^Undefined array key \d+$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "Warning" || !Message().IsMatch(context.Error.Message ?? "") || PhpCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);

        for (var index = at.Number - 1; index >= Math.Max(0, at.Number - 1 - LinesAboveLooked); index--)
        {
            if (PhpLoops.LoopToCount().Match(masked[index]) is { Success: true } loop) return PhpLoops.StrictlyBelowFix(Id, at.Source, index + 1, loop);
        }

        return null;
    }
}

/// <summary>The change the warning's rule and the logic check both make to a loop to &lt;= count($x), so the two are one fix.</summary>
internal static partial class PhpLoops
{
    [GeneratedRegex(@"\bfor\s*\(\s*\$(?<variable>\w+)\s*=\s*0\s*;\s*\$\k<variable>\s*(?<comparison><=)\s*count\(\s*\$(?<array>\w+)\s*\)\s*;")]
    public static partial Regex LoopToCount();

    public static LocalFix StrictlyBelowFix(string id, SourceFile source, int lineNumber, Match loop)
    {
        var line = source.Lines[lineNumber - 1];
        var comparison = loop.Groups["comparison"];
        var array = loop.Groups["array"].Value;

        return LocalFix.ReplaceLine(id, "Stop the loop below the count: <",
            $"${array}'s positions run from 0 to count(${array}) - 1, so the loop has to stop while ${loop.Groups["variable"].Value} is still below count(${array}).",
            source.Path, lineNumber, line[..comparison.Index] + "<" + line[(comparison.Index + comparison.Length)..]);
    }
}
