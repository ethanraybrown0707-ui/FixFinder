using System.Text.RegularExpressions;
using FixFinder.Core.Logic;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the guard rules share: finding a division on a line, and the language's way of writing "if it is zero, use 0".</summary>
internal static partial class Guards
{
    [GeneratedRegex(@"(?<a>[A-Za-z_][\w.]*(?:\([^()]*\)|\[[^\]]*\])?|\d+(?:\.\d+)?|\))\s*(?<op>//|/|%)(?![/=*])\s*(?<b>[A-Za-z_][\w.]*(?:\(\s*[^()]*\)|\[[^\]]*\])?)")]
    private static partial Regex Division();

    /// <summary>The line with its one division by a name or a call rewritten to give 0 when the divisor is 0, or null.</summary>
    public static string? GuardDivision(string line, Syntax syntax, bool python)
    {
        var masked = CodeText.Mask(line, syntax);
        var divisions = Division().Matches(masked).ToList();
        if (divisions.Count == 0 || divisions.Select(d => d.Groups["b"].Value).Distinct().Count() != 1) return null;

        var division = divisions[0];
        var start = division.Groups["a"].Index;

        if (division.Groups["a"].Value == ")")
        {
            start = Py.MatchBack(masked, division.Groups["a"].Index);
            if (start < 0) return null;
        }

        var end = division.Groups["b"].Index + division.Groups["b"].Length;
        var expression = line[start..end];
        var divisor = line.Substring(division.Groups["b"].Index, division.Groups["b"].Length);

        var guarded = python ? $"({expression} if {divisor} else 0)" : $"({divisor} == 0 ? 0 : {expression})";

        return line[..start] + guarded + line[end..];
    }

    private static readonly (string Wrong, string Right)[] Typography =
    [
        ("\u201C", "\""), ("\u201D", "\""), ("\u201E", "\""), ("\u2018", "'"), ("\u2019", "'"), ("\u201A", "'"),
        ("\u2013", "-"), ("\u2014", "-"), ("\u2212", "-"), ("\u2026", "..."), ("\u00A0", " "), ("\u200B", ""),
    ];

    /// <summary>The line with curly quotes, long dashes and hidden spaces from a word processor turned into the characters code uses.</summary>
    public static string? StraightenTypography(string line)
    {
        var straightened = Typography.Aggregate(line, (text, pair) => text.Replace(pair.Wrong, pair.Right, StringComparison.Ordinal));
        return straightened == line ? null : straightened;
    }

    public static LocalFix? StraightenFix(string id, SourceFile source, int number) =>
        source.Line(number) is { } line && StraightenTypography(line) is { } straightened
            ? LocalFix.ReplaceLine(id, "Retype the curly quotes and dashes as plain ones",
                "The line has characters that a word processor or a web page put in - curly quotes, a long dash or a hidden space. They look " +
                "like code but are different characters, and no compiler reads them. The plain keyboard versions are.",
                source.Path, number, straightened)
            : null;
}

/// <summary><c>ZeroDivisionError: division by zero</c> - the divisor was 0 when the line ran.</summary>
public sealed class PythonDivisionGuard : ILocalFixRule
{
    public string Id => "python-division-guard";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "ZeroDivisionError") || Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Guards.GuardDivision(line, Syntax.Python, python: true) is not { } guarded) return null;

        return LocalFix.ReplaceLine(Id, "Give an answer for when the divisor is zero",
            "Dividing by zero has no answer, so Python stops. This happens when a count or a list is empty. Checking the divisor first " +
            "and giving 0 in that case keeps the program going - change the 0 to whatever an empty case should give.",
            source.Path, number, guarded);
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

/// <summary><c>KeyError: 'pear'</c> read with square brackets.</summary>
public sealed partial class PythonMissingKeyGet : ILocalFixRule
{
    public string Id => "python-missing-key-get";

    [GeneratedRegex(@"(?<![\w.])(?<dictionary>[A-Za-z_][\w.]*)\[(?<key>[^\[\]]+)\](?!\s*(?:[-+*/%]|//)?=(?!=))")]
    private static partial Regex Subscript();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "KeyError") || Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);
        if (Regex.IsMatch(masked, @"^\s*del\b")) return null;

        var key = (context.Error.Message ?? "").Trim();
        var reads = Subscript().Matches(masked)
            .Where(m => line.Substring(m.Groups["key"].Index, m.Groups["key"].Length).Trim() == key || Subscript().Matches(masked).Count == 1)
            .ToList();
        if (reads is not [var read]) return null;

        var dictionary = read.Groups["dictionary"].Value;
        var keyText = line.Substring(read.Groups["key"].Index, read.Groups["key"].Length).Trim();

        return LocalFix.ReplaceLine(Id, $"Read it with get, which gives None for a missing key: {dictionary}.get({keyText})",
            $"`{dictionary}[{keyText}]` stops the program when the key is not there. `{dictionary}.get({keyText})` gives None instead - or a default " +
            $"of your choosing, `{dictionary}.get({keyText}, 0)`. If the key should always be there, the real fix is wherever it was meant to be added.",
            source.Path, number, line[..read.Index] + $"{dictionary}.get({keyText})" + line[(read.Index + read.Length)..]);
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

/// <summary><c>invalid character '\u201C' (U+201C)</c> - curly quotes or dashes pasted into Python.</summary>
public sealed class PythonSmartQuotes : ILocalFixRule
{
    public string Id => "python-smart-quotes";

    public LocalFix? Propose(LocalFixContext context) =>
        Py.Is(context, "SyntaxError") && (context.Error.Message ?? "").Contains("invalid character", StringComparison.Ordinal) &&
        Py.Locate(context) is { } at
            ? Guards.StraightenFix(Id, at.Source, at.Number)
            : null;
}

/// <summary><c>illegal character: '\u201C'</c> in javac.</summary>
public sealed class JavaSmartQuotes : ILocalFixRule
{
    public string Id => "java-smart-quotes";

    public LocalFix? Propose(LocalFixContext context) =>
        Java.IsCompileError(context.Error) && (context.Error.Message ?? "").StartsWith("illegal character", StringComparison.Ordinal) &&
        context.Frame is { Line: { } number } frame && context.Read(frame.File) is { } source
            ? Guards.StraightenFix(Id, source, number)
            : null;
}

/// <summary><c>CS1056: Unexpected character '\u201C'</c>.</summary>
public sealed class CSharpSmartQuotes : ILocalFixRule
{
    public string Id => "csharp-smart-quotes";

    public LocalFix? Propose(LocalFixContext context) =>
        Cs.Is(context, "CS1056", "CS1010", "CS1039") && Cs.Locate(context) is { } at
            ? Guards.StraightenFix(Id, at.Source, at.Number)
            : null;
}

/// <summary>gcc's <c>stray '\342' in program</c> - the first byte of a curly quote.</summary>
public sealed class CSmartQuotes : ILocalFixRule
{
    public string Id => "c-smart-quotes";

    public LocalFix? Propose(LocalFixContext context) =>
        context.Error.LanguageId == "gcc" && (context.Error.Message ?? "").StartsWith("stray ", StringComparison.Ordinal) &&
        context.Frame is { Line: { } number } frame && context.Read(frame.File) is { } source
            ? Guards.StraightenFix(Id, source, number)
            : null;
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

/// <summary>gcc's <c>this statement may fall through</c>, reported on the case's last statement.</summary>
public sealed class CFallthroughBreak : ILocalFixRule
{
    public string Id => "c-fallthrough-break";

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error is not { LanguageId: "gcc", ExceptionType: "compile warning" } || !(error.Message ?? "").StartsWith("this statement may fall through", StringComparison.Ordinal)) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source || source.Line(number) is not { } line) return null;

        var end = number;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        while (end <= masked.Count && !masked[end - 1].TrimEnd().EndsWith(';') && !masked[end - 1].TrimEnd().EndsWith('}')) end++;
        if (end > masked.Count) return null;

        return new LocalFix
        {
            RuleId = Id,
            Title = "End the case with break",
            Explanation = "Without `break`, a case carries straight on into the next one, so choosing it also runs the next case's statements.",
            File = source.Path, StartLine = end + 1, RemoveCount = 0,
            NewLines = [$"{CodeText.Indentation(line)}break;"],
            ResolvesWarning = "this statement may fall through",
        };
    }
}

/// <summary>What the base-case rules share: a function that calls itself with its number one smaller, and never stops.</summary>
internal static partial class Recursion
{
    /// <summary>The line of the function's header and the parameter it counts down, when the function calls itself as name(n - 1).</summary>
    public static (int Header, string Parameter)? CountingDown(IReadOnlyList<string> masked, int errorLine, Regex header, bool python)
    {
        for (var k = errorLine - 1; k >= 0; k--)
        {
            if (header.Match(masked[k]) is not { Success: true } match) continue;

            var name = Regex.Escape(match.Groups["name"].Value);
            var parameters = match.Groups["parameters"].Value.Split(',').Select(p => Regex.Match(p.Split('=', ':')[0].Trim(), @"(?<name>[A-Za-z_]\w*)\s*$").Groups["name"].Value).ToList();

            var end = python ? PythonBlocks.Body(masked, k).End : NativeCourse.BlockEnd(masked, k) ?? masked.Count;
            var body = string.Join("\n", Enumerable.Range(k + 1, Math.Max(0, end - k - 1)).Select(i => masked[i]));

            if (Regex.IsMatch(body, python ? @"\breturn\s+\S" : @"\breturn\s+[^;\s]")) return null;
            if (Regex.IsMatch(body, @"(?:^|\n)\s*if\b")) return null;

            foreach (var parameter in parameters.Where(p => p.Length > 0))
            {
                if (Regex.IsMatch(body, $@"(?<![\w.]){name}\s*\((?:[^()]*,\s*)?{Regex.Escape(parameter)}\s*-\s*1\s*[,)]")) return (k, parameter);
            }

            return null;
        }

        return null;
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
        if (!Py.Is(context, "RecursionError") || Py.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        if (Recursion.CountingDown(masked, number - 1, Header(), python: true) is not var (header, parameter)) return null;

        var inner = CodeText.Indentation(source.Lines[header]) + PythonLayout.IndentUnit(source.Lines);

        return LocalFix.Insert(Id, $"Stop when {parameter} reaches 0: if {parameter} <= 0: return",
            $"The function calls itself with `{parameter} - 1` every time and nothing ever stops it, so it goes on past 0 into the negative " +
            $"numbers until Python gives up. A base case at the top - stop once `{parameter}` reaches 0 - ends the chain of calls.",
            source.Path, header + 2, [$"{inner}if {parameter} <= 0:", $"{inner}{PythonLayout.IndentUnit(source.Lines)}return"]);
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
