using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>ArrayIndexOutOfBoundsException: Index 3 out of bounds for length 3</c> from a loop written with <c>&lt;=</c>.</summary>
public sealed partial class JavaOffByOneLoop : ILocalFixRule
{
    public string Id => "java-off-by-one-loop";

    [GeneratedRegex(@"(?:Index|index)\s*:?\s*(?<index>\d+)\D+?(?:length|Size|size)\s*:?\s*(?<length>\d+)")]
    private static partial Regex Bounds();

    [GeneratedRegex(@"\[\s*(?<var>[A-Za-z_$][\w$]*)\s*\]|\.(?:get|charAt|set|remove)\(\s*(?<var>[A-Za-z_$][\w$]*)\s*[,)]")]
    private static partial Regex IndexUse();

    [GeneratedRegex(@"\bfor\s*\(\s*(?:int\s+|long\s+|var\s+)?(?<var>[A-Za-z_$][\w$]*)\s*=[^;]*;\s*\k<var>\s*(?<op><=)\s*(?<bound>[^;]+?)\s*;")]
    private static partial Regex Loop();

    [GeneratedRegex(@"(?:\.length|\.size\(\)|\.length\(\))$")]
    private static partial Regex LengthBound();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "java") return null;
        if (error.ExceptionType is not ("java.lang.ArrayIndexOutOfBoundsException" or
            "java.lang.StringIndexOutOfBoundsException" or "java.lang.IndexOutOfBoundsException")) return null;
        if (Bounds().Match(error.Message ?? "") is not { Success: true } bounds) return null;
        if (bounds.Groups["index"].Value != bounds.Groups["length"].Value) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (number < 1 || number > masked.Count) return null;

        var indexes = IndexUse().Matches(masked[number - 1]).Select(m => m.Groups["var"].Value).ToHashSet(StringComparer.Ordinal);
        if (indexes.Count == 0) return null;

        for (var k = number - 1; k >= Math.Max(0, number - 16); k--)
        {
            if (Loop().Match(masked[k]) is not { Success: true } loop || !indexes.Contains(loop.Groups["var"].Value)) continue;

            var bound = loop.Groups["bound"].Value.Trim();
            if (!LengthBound().IsMatch(bound) && bound != bounds.Groups["length"].Value) return null;

            var op = loop.Groups["op"];
            var line = source.Lines[k];
            var variable = loop.Groups["var"].Value;

            return LocalFix.ReplaceLine(
                Id,
                "Stop the loop at the last element: < instead of <=",
                $"Index {bounds.Groups["index"].Value} is one past the end of something with {bounds.Groups["length"].Value} " +
                $"elements - the valid indexes stop at {int.Parse(bounds.Groups["length"].Value) - 1}. The loop on line {k + 1} " +
                $"keeps going while {variable} <= {bound}, which reaches the length itself; < stops one before it.",
                source.Path, k + 1, line[..op.Index] + "<" + line[(op.Index + 2)..]);
        }

        return null;
    }
}

/// <summary><c>ConcurrentModificationException</c> from removing items inside a for-each over the same list.</summary>
public sealed class JavaRemoveInForEach : ILocalFixRule
{
    public string Id => "java-remove-in-for-each";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "java", ExceptionType: "java.util.ConcurrentModificationException" }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (BraceRules.RemoveInLoop(source.Lines, masked, number - 1, java: true) is not { } loop) return null;

        return new LocalFix
        {
            RuleId = Id,
            Title = "Remove them with removeIf instead of inside the loop",
            Explanation = "A for-each loop walks the list with an iterator, and removing from the list underneath it breaks that walk. " +
                          "`removeIf` does the whole loop-and-remove itself, safely.",
            File = source.Path,
            StartLine = loop.Start + 1,
            RemoveCount = loop.Count,
            NewLines = [loop.Line],
        };
    }
}

/// <summary><c>java.lang.ArithmeticException: / by zero</c>.</summary>
public sealed class JavaDivisionGuard : ILocalFixRule
{
    public string Id => "java-division-guard";

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error is not { LanguageId: "java", ExceptionType: "java.lang.ArithmeticException" } || !(error.Message ?? "").Contains("by zero")) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source || source.Line(number) is not { } line) return null;
        if (Guards.GuardDivision(line, Syntax.CLike, python: false) is not { } guarded) return null;

        return LocalFix.ReplaceLine(Id, "Give an answer for when the divisor is zero",
            "Whole-number division by zero has no answer, so Java throws ArithmeticException. Checking the divisor first and giving 0 in " +
            "that case keeps the program going - change the 0 to whatever an empty case should give.",
            source.Path, number, guarded);
    }
}

/// <summary><c>java.lang.StackOverflowError</c> from a method that counts down and never stops.</summary>
public sealed partial class JavaRecursionBaseCase : ILocalFixRule
{
    public string Id => "java-recursion-base-case";

    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|static|final)\s+)*void\s+(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)")]
    private static partial Regex Header();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "java", ExceptionType: "java.lang.StackOverflowError" }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Recursion.CountingDown(masked, number - 1, Header(), python: false) is not var (header, parameter)) return null;
        if (!masked[header].Contains('{')) return null;

        var inner = CodeText.Indentation(source.Lines[header]) + Logic.BraceBlocks.IndentStep(source.Lines);

        return LocalFix.Insert(Id, $"Stop when {parameter} reaches 0: if ({parameter} <= 0) return;",
            $"The method calls itself with `{parameter} - 1` every time and nothing ever stops it, so the calls pile up until Java runs out " +
            $"of stack. A base case at the top - stop once `{parameter}` reaches 0 - ends the chain.",
            source.Path, header + 2, [$"{inner}if ({parameter} <= 0) return;"]);
    }
}

/// <summary>javac's <c>[fallthrough] possible fall-through into case</c>, reported on the case it falls into.</summary>
public sealed class JavaFallthroughBreak : ILocalFixRule
{
    public string Id => "java-fallthrough-break";

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error is not { LanguageId: "java", ExceptionType: "compile warning" } || !(error.Message ?? "").StartsWith("[fallthrough]", StringComparison.Ordinal)) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var previous = number - 1;
        while (previous >= 1 && (source.Line(previous) ?? "").Trim() is var text && (text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal))) previous--;
        if (previous < 1 || Regex.IsMatch(source.Line(previous)!, @"^\s*(?:case\b.*|default\s*):\s*$")) return null;

        return new LocalFix
        {
            RuleId = Id,
            Title = "End the case with break",
            Explanation = "Without `break`, a case carries straight on into the next one, so choosing it also runs the next case's statements.",
            File = source.Path, StartLine = previous + 1, RemoveCount = 0,
            NewLines = [$"{CodeText.Indentation(source.Line(previous)!)}break;"],
            ResolvesWarning = "[fallthrough]",
        };
    }
}
