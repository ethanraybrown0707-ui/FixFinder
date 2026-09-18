using System.Text.RegularExpressions;
using FixFinder.Core.Logic;

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
