using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>ZeroDivisionError: division by zero</c> - the divisor was 0 when the line ran.</summary>
public sealed class PythonDivisionGuard : ILocalFixRule
{
    public string Id => "python-division-guard";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "ZeroDivisionError") || PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Guards.GuardDivision(line, Syntax.Python, python: true) is not { } guarded) return null;

        return LocalFix.ReplaceLine(Id, "Give an answer for when the divisor is zero",
            "Dividing by zero has no answer, so Python stops. This happens when a count or a list is empty. Checking the divisor first " +
            "and giving 0 in that case keeps the program going - change the 0 to whatever an empty case should give.",
            source.Path, number, guarded);
    }
}

/// <summary><c>RecursionError: maximum recursion depth exceeded</c> from a function that counts down and never stops.</summary>
public sealed partial class PythonRecursionBaseCase : ILocalFixRule
{
    public string Id => "python-recursion-base-case";

    [GeneratedRegex(@"^\s*def\s+(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)")]
    private static partial Regex Header();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "RecursionError") || PythonCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        if (Recursion.CountingDown(masked, number - 1, Header(), python: true) is not var (header, parameter)) return null;

        var inner = CodeText.Indentation(source.Lines[header]) + PythonCode.IndentUnit(source.Lines);

        return LocalFix.Insert(Id, $"Stop when {parameter} reaches 0: if {parameter} <= 0: return",
            $"The function calls itself with `{parameter} - 1` every time and nothing ever stops it, so it goes on past 0 into the negative " +
            $"numbers until Python gives up. A base case at the top - stop once `{parameter}` reaches 0 - ends the chain of calls.",
            source.Path, header + 2, [$"{inner}if {parameter} <= 0:", $"{inner}{PythonCode.IndentUnit(source.Lines)}return"]);
    }
}

/// <summary>
/// <c>list index out of range</c> in a loop over <c>range(len(marks) + 1)</c> - one position more than the list has: the
/// positions of a list of three are 0, 1 and 2, and <c>range(len(marks))</c> goes over exactly those.
/// </summary>
public sealed partial class PythonOffByOneRange : ILocalFixRule
{
    public string Id => "python-off-by-one-range";

    [GeneratedRegex(@"^(?:list|tuple|string) index out of range$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<indent>\s*)for\s+(?<index>[A-Za-z_]\w*)\s+in\s+range\(\s*(?:0\s*,\s*)?len\(\s*(?<items>[A-Za-z_][\w.]*)\s*\)(?<more>\s*\+\s*1)\s*\)\s*:")]
    private static partial Regex LoopOneTooFar();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "IndexError") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        var indent = CodeText.Indentation(line).Length;

        // The loop the line is in: the nearest line above it that is indented less, if it is a loop over one position too many.
        for (var lineAbove = number - 2; lineAbove >= 0; lineAbove--)
        {
            if (masked[lineAbove].Trim().Length == 0 || CodeText.Indentation(masked[lineAbove]).Length >= indent) continue;
            if (LoopOneTooFar().Match(masked[lineAbove]) is not { Success: true } loop) return null;

            var items = loop.Groups["items"].Value;
            var position = loop.Groups["index"].Value;
            if (!Regex.IsMatch(masked[number - 1], $@"(?<![\w.]){Regex.Escape(items)}\s*\[\s*{Regex.Escape(position)}\s*\]")) return null;

            var more = loop.Groups["more"];
            var corrected = source.Lines[lineAbove][..more.Index] + source.Lines[lineAbove][(more.Index + more.Length)..];

            return LocalFix.ReplaceLine(
                Id, $"Loop over range(len({items})) on line {lineAbove + 1}",
                $"The positions of `{items}` run from 0 to one less than how many items it has, and `range(len({items}) + 1)` goes one " +
                $"further - so the last pass asks for `{items}[{position}]` at a position it does not have. `range(len({items}))` goes over " +
                "exactly its positions.",
                source.Path, lineAbove + 1, corrected);
        }

        return null;
    }
}
