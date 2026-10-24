using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// A second comparison written with nothing on its left - age &gt; 12 &amp;&amp; &lt; 20 - as it is said in words. A comparison
/// needs a value on each side, and what the second compares is the first's left side, so the change writes it again:
/// age &gt; 12 &amp;&amp; age &lt; 20. javac, the C# compiler, Python and Node each stop at the operator with nothing before it.
/// </summary>
internal static partial class MissingLeftSide
{
    /// <summary>
    /// A comparison, then &amp;&amp; or || and an operator straight after it - with &amp;&amp; and || as Java, C# and JavaScript
    /// write them.
    /// </summary>
    [GeneratedRegex(@"(?<left>[A-Za-z_$][\w$.]*(?:\[[^\[\]]*\]|\(\))?)\s*(?:<=|>=|===|!==|==|!=|<|>)\s*[^&|()<>=!]+?\s*(?:&&|\|\|)\s*(?=(?:<=|>=|===|!==|==|!=|<|>)(?!=))")]
    private static partial Regex Braced();

    /// <summary>The same with Python's and and or.</summary>
    [GeneratedRegex(@"(?<left>[A-Za-z_][\w.]*(?:\[[^\[\]]*\]|\(\))?)\s*(?:<=|>=|==|!=|<|>)\s*[^()<>=!]+?\s+(?:and|or)\s+(?=(?:<=|>=|==|!=|<|>)(?!=))")]
    private static partial Regex Spoken();

    /// <summary>
    /// The line with each comparison that has no left side given the one before it - one at a time, as in
    /// age &gt; 12 &amp;&amp; &lt; 20 &amp;&amp; != 15 the third takes its left side from the second once that has one - or null
    /// where none is missing.
    /// </summary>
    public static string? Repeated(string line, Syntax syntax, bool spoken)
    {
        var changed = line;
        for (var pass = 0; pass < 10; pass++)
        {
            if ((spoken ? Spoken() : Braced()).Match(CodeText.Mask(changed, syntax)) is not { Success: true } comparison) break;

            var at = comparison.Index + comparison.Length;
            changed = changed[..at] + comparison.Groups["left"].Value + " " + changed[at..];
        }

        return changed == line ? null : changed;
    }

    public const string Explanation =
        "A comparison needs a value on each side, so the second one here has nothing to compare. It is meant to compare the " +
        "same value as the first, so that value is written again.";
}

/// <summary>javac's "illegal start of type" or "illegal start of expression" at <c>if (age &gt; 12 &amp;&amp; &lt; 20)</c>.</summary>
public sealed class JavaRepeatLeftSide : ILocalFixRule
{
    public string Id => "java-repeat-left-side";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!(context.Error.Message ?? "").StartsWith("illegal start of", StringComparison.Ordinal) || JavaCode.Locate(context) is not { } at) return null;
        if (MissingLeftSide.Repeated(at.Line, Syntax.CLike, spoken: false) is not { } changed) return null;

        return LocalFix.ReplaceLine(Id, "Give the second comparison its left side", MissingLeftSide.Explanation, at.Source.Path, at.Number, changed);
    }
}

/// <summary>CS1525, "Invalid expression term '&lt;'", at <c>if (age &gt; 12 &amp;&amp; &lt; 20)</c>.</summary>
public sealed partial class CSharpRepeatLeftSide : ILocalFixRule
{
    public string Id => "csharp-repeat-left-side";

    [GeneratedRegex(@"^Invalid expression term '(?:<=|>=|==|!=|<|>)'")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS1525") || !Message().IsMatch(context.Error.Message ?? "") || CSharpCode.Locate(context) is not { } at) return null;
        if (MissingLeftSide.Repeated(at.Line, Syntax.CLike, spoken: false) is not { } changed) return null;

        return LocalFix.ReplaceLine(Id, "Give the second comparison its left side", MissingLeftSide.Explanation, at.Source.Path, at.Number, changed);
    }
}

/// <summary>Python's "invalid syntax" at <c>if age &gt; 12 and &lt; 20:</c>.</summary>
public sealed class PythonRepeatLeftSide : ILocalFixRule
{
    public string Id => "python-repeat-left-side";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "SyntaxError") || !(context.Error.Message ?? "").StartsWith("invalid syntax", StringComparison.Ordinal) ||
            PythonCode.Locate(context) is not { } at) return null;
        if (MissingLeftSide.Repeated(at.Line, Syntax.Python, spoken: true) is not { } changed) return null;

        return LocalFix.ReplaceLine(Id, "Give the second comparison its left side", MissingLeftSide.Explanation, at.Source.Path, at.Number, changed);
    }
}

/// <summary>Node's "Unexpected token '&lt;'" at <c>if (age &gt; 12 &amp;&amp; &lt; 20)</c>.</summary>
public sealed partial class JsRepeatLeftSide : ILocalFixRule
{
    public string Id => "js-repeat-left-side";

    [GeneratedRegex(@"^Unexpected token '(?:<=|>=|===|!==|==|!=|<|>)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "SyntaxError", Message()) is null || JavaScriptCode.Locate(context) is not { } at) return null;
        if (MissingLeftSide.Repeated(at.Line, Syntax.CLike, spoken: false) is not { } changed) return null;

        return LocalFix.ReplaceLine(Id, "Give the second comparison its left side", MissingLeftSide.Explanation, at.Source.Path, at.Number, changed);
    }
}
