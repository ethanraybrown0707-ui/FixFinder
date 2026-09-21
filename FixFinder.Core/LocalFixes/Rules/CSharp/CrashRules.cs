using System.Reflection;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>IndexOutOfRangeException</c> or <c>ArgumentOutOfRangeException</c> from a loop running while <c>i &lt;=
/// values.Length</c>.</summary>
public sealed partial class CSharpOffByOneLoop : ILocalFixRule
{
    public string Id => "csharp-off-by-one-loop";

    [GeneratedRegex(@"\[\s*(?<var>[A-Za-z_]\w*)\s*\]")]
    private static partial Regex IndexUse();

    [GeneratedRegex(@"\bfor\s*\(\s*(?:int\s+|var\s+|long\s+)?(?<var>[A-Za-z_]\w*)\s*=[^;]*;\s*\k<var>\s*(?<op><=)\s*(?<bound>[^;]+?)\s*;")]
    private static partial Regex Loop();

    [GeneratedRegex(@"\.(?:Length|Count)(?:\(\))?$")]
    private static partial Regex LengthBound();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "csharp" || error.ExceptionType is not ("System.IndexOutOfRangeException" or "System.ArgumentOutOfRangeException")) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (number < 1 || number > masked.Count) return null;

        var indexes = IndexUse().Matches(masked[number - 1]).Select(m => m.Groups["var"].Value).ToHashSet(StringComparer.Ordinal);
        if (indexes.Count == 0) return null;

        for (var k = number - 1; k >= Math.Max(0, number - 16); k--)
        {
            if (Loop().Match(masked[k]) is not { Success: true } loop || !indexes.Contains(loop.Groups["var"].Value)) continue;
            if (!LengthBound().IsMatch(loop.Groups["bound"].Value.Trim())) return null;

            var op = loop.Groups["op"];

            return LocalFix.ReplaceLine(
                Id, "Stop the loop at the last element: < instead of <=",
                $"The valid indexes run from 0 to one less than `{loop.Groups["bound"].Value.Trim()}`. The loop on line {k + 1} runs while {loop.Groups["var"].Value} <= it, which reaches one past the end.",
                source.Path, k + 1, source.Lines[k][..op.Index] + "<" + source.Lines[k][(op.Index + 2)..]);
        }

        return null;
    }
}

/// <summary><c>InvalidOperationException: Collection was modified</c> from removing items inside a foreach over the same list.</summary>
public sealed class CSharpRemoveInForEach : ILocalFixRule
{
    public string Id => "csharp-remove-in-foreach";

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error.LanguageId != "csharp" || error.ExceptionType != "System.InvalidOperationException") return null;
        if (!(error.Message ?? "").StartsWith("Collection was modified", StringComparison.Ordinal)) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (BraceRules.RemoveInLoop(source.Lines, masked, number - 1, java: false) is not { } loop) return null;

        return new LocalFix
        {
            RuleId = Id,
            Title = "Remove them with RemoveAll instead of inside the loop",
            Explanation = "A foreach walks the list with an enumerator, and changing the list underneath it breaks that walk. " +
                          "`RemoveAll` does the whole loop-and-remove itself, safely.",
            File = source.Path, StartLine = loop.Start + 1, RemoveCount = loop.Count, NewLines = [loop.Line],
        };
    }
}

/// <summary><c>System.DivideByZeroException: Attempted to divide by zero.</c></summary>
public sealed class CSharpDivisionGuard : ILocalFixRule
{
    public string Id => "csharp-division-guard";

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error is not { LanguageId: "csharp", ExceptionType: "System.DivideByZeroException" }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source || source.Line(number) is not { } line) return null;
        if (Guards.GuardDivision(line, Syntax.CLike, python: false) is not { } guarded) return null;

        return LocalFix.ReplaceLine(Id, "Give an answer for when the divisor is zero",
            "Whole-number division by zero has no answer, so .NET throws DivideByZeroException. Checking the divisor first and giving 0 in " +
            "that case keeps the program going - change the 0 to whatever an empty case should give.",
            source.Path, number, guarded);
    }
}

/// <summary><c>KeyNotFoundException: The given key 'pear' was not present in the dictionary.</c></summary>
public sealed partial class CSharpMissingKeyDefault : ILocalFixRule
{
    public string Id => "csharp-missing-key-default";

    [GeneratedRegex(@"(?<![\w.])(?<dictionary>[A-Za-z_][\w.]*)\[(?<key>[^\[\]]+)\](?!\s*(?:[-+*/%]|\?\?)?=(?!=))")]
    private static partial Regex Indexer();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error is not { LanguageId: "csharp", ExceptionType: "System.Collections.Generic.KeyNotFoundException" }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source || source.Line(number) is not { } line) return null;

        var reads = Indexer().Matches(CodeText.Mask(line, Syntax.CLike)).ToList();
        if (reads is not [var read]) return null;

        var dictionary = read.Groups["dictionary"].Value;
        var key = line.Substring(read.Groups["key"].Index, read.Groups["key"].Length).Trim();

        return LocalFix.ReplaceLine(Id, $"Read it with GetValueOrDefault: {dictionary}.GetValueOrDefault({key})",
            $"`{dictionary}[{key}]` throws when the key is missing. `GetValueOrDefault` gives the type's default - 0, null or false - " +
            "instead. If the key should always be there, the real fix is wherever it was meant to be added.",
            source.Path, number, line[..read.Index] + $"{dictionary}.GetValueOrDefault({key})" + line[(read.Index + read.Length)..]);
    }
}

/// <summary><c>InvalidOperationException: Sequence contains no elements</c> from First, Last, Single, Max, Min or Average.</summary>
public sealed partial class CSharpEmptySequenceDefault : ILocalFixRule
{
    public string Id => "csharp-empty-sequence-default";

    [GeneratedRegex(@"\.(?<method>First|Last|Single|Max|Min|Average)\(\s*\)")]
    private static partial Regex Call();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error is not { LanguageId: "csharp", ExceptionType: "System.InvalidOperationException" }) return null;
        if (!(error.Message ?? "").Contains("contains no", StringComparison.Ordinal)) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source || source.Line(number) is not { } line) return null;

        var calls = Call().Matches(CodeText.Mask(line, Syntax.CLike)).ToList();
        if (calls is not [var call]) return null;

        var method = call.Groups["method"].Value;
        var replacement = method is "First" or "Last" or "Single" ? $".{method}OrDefault()" : $".DefaultIfEmpty().{method}()";

        return LocalFix.ReplaceLine(Id, $"Allow an empty sequence: {replacement.TrimStart('.')}",
            $"`{method}()` needs at least one item and throws when there are none. `{replacement.TrimStart('.')}` gives the type's default " +
            "instead, so an empty list is handled rather than crashing.",
            source.Path, number, line[..call.Index] + replacement + line[(call.Index + call.Length)..]);
    }
}
