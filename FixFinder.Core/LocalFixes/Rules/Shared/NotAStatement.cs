using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// A line that works a value out and keeps it nowhere - <c>count == 0;</c> or <c>total + mark;</c> - which javac calls "not a
/// statement" and C# refuses with CS0201. == compares, so <c>count == 0;</c> was meant to give count the value: =. A sum on
/// its own was meant to be added to the name it starts with: +=.
/// </summary>
internal static partial class NotAStatement
{
    /// <summary><c>count == 0;</c> or <c>total + mark;</c> - a name, then == or an arithmetic operator, then a value, and nothing more.</summary>
    [GeneratedRegex(@"^(?<lead>\s*)(?<target>[A-Za-z_][\w.]*(?:\[[^\[\]]*\])?)\s*(?<op>==|[-+*/%](?![-+=]))\s*(?<value>[^;=]+?)\s*;(?<tail>\s*(?://.*)?)$")]
    private static partial Regex WorkedOutAndLost();

    /// <summary>The change for the line, with its title and explanation, or null where the line is not one of these.</summary>
    public static (string Changed, string Title, string Explanation)? For(string line)
    {
        if (WorkedOutAndLost().Match(line) is not { Success: true } statement) return null;

        var (target, op, value) = (statement.Groups["target"].Value, statement.Groups["op"].Value, statement.Groups["value"].Value);
        var given = op == "==" ? "=" : op + "=";
        var changed = $"{statement.Groups["lead"].Value}{target} {given} {value};{statement.Groups["tail"].Value}";

        return op == "=="
            ? (changed, $"Give {target} the value: {target} = {value};",
                $"== compares two values and keeps the answer nowhere, so the line does nothing. = gives {target} the value {value}.")
            : (changed, $"Add it to {target}: {target} {given} {value};",
                $"{target} {op} {value} works out a value and keeps it nowhere, so the line does nothing. {given} works it out and stores it in {target}.");
    }
}

/// <summary>javac's "not a statement" at <c>count == 0;</c>.</summary>
public sealed class JavaNotAStatement : ILocalFixRule
{
    public string Id => "java-not-a-statement";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "not a statement" || JavaCode.Locate(context) is not { } at || NotAStatement.For(at.Line) is not { } fix) return null;

        return LocalFix.ReplaceLine(Id, fix.Title, fix.Explanation, at.Source.Path, at.Number, fix.Changed);
    }
}

/// <summary>CS0201, "Only assignment, call, increment, decrement, await, and new object expressions can be used as a statement".</summary>
public sealed class CSharpNotAStatement : ILocalFixRule
{
    public string Id => "csharp-not-a-statement";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0201") || CSharpCode.Locate(context) is not { } at || NotAStatement.For(at.Line) is not { } fix) return null;

        return LocalFix.ReplaceLine(Id, fix.Title, fix.Explanation, at.Source.Path, at.Number, fix.Changed);
    }
}
