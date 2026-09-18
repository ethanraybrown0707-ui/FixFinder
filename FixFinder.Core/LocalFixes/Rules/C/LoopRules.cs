using System.Text.RegularExpressions;
using FixFinder.Core.Logic;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>for (i = 0; ...)</c> with <c>i</c> never declared.</summary>
/// <remarks>Declared in the loop only when nothing outside the loop uses it - otherwise it has to live longer.</remarks>
public sealed partial class CForCounter : ILocalFixRule
{
    public string Id => "c-for-counter";

    [GeneratedRegex(@"^'(?<name>\w+)': undeclared identifier$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^'(?<name>\w+)' undeclared")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var message = CCode.MsvcMessage(error, "C2065", MsvcMessage()) ?? CCode.GccMessage(error, GccMessage());

        if (message is null || CCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var name = message.Groups["name"].Value;
        var escaped = Regex.Escape(name);
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var header = new Regex($@"\bfor\s*(?<open>\()\s*(?<name>{escaped})\s*=(?!=)");

        for (var k = number - 1; k >= Math.Max(0, number - 16); k--)
        {
            if (header.Match(masked[k]) is not { Success: true } loop) continue;
            if (Brackets.ClosingParenthesis(masked[k], loop.Groups["open"].Index) is not { } close) return null;
            if (BodyEnd(masked, k, close + 1) is not { } end || number - 1 > end) return null;

            var (first, last) = CCode.EnclosingFunction(masked, k);
            var word = new Regex($@"(?<![\w.]){escaped}(?!\w)");

            for (var i = first + 1; i <= last; i++)
                if ((i < k || i > end) && word.IsMatch(masked[i])) return null;

            var column = loop.Groups["name"].Index;

            return LocalFix.ReplaceLine(
                Id, $"Declare {name} in the loop",
                $"`{name}` is the loop counter but is never declared. Nothing outside the loop uses it, so declaring it in the loop with `int` is all it needs.",
                source.Path, k + 1, source.Lines[k][..column] + "int " + source.Lines[k][column..]);
        }

        return null;
    }

    private static int? BodyEnd(IReadOnlyList<string> masked, int line, int after)
    {
        var rest = masked[line][after..];
        var brace = rest.IndexOf('{');

        if (brace < 0) return rest.Trim().Length > 0 ? line : line + 1 < masked.Count ? line + 1 : null;

        var depth = 0;

        for (var i = line; i < masked.Count; i++)
        {
            for (var c = i == line ? after + brace : 0; c < masked[i].Length; c++)
            {
                if (masked[i][c] == '{') depth++;
                else if (masked[i][c] == '}' && --depth == 0) return i;
            }
        }

        return null;
    }
}

/// <summary>AddressSanitizer's <c>stack-buffer-overflow</c> from a loop that runs past the end of an array.</summary>
public sealed partial class CArrayBoundLoop : ILocalFixRule
{
    public string Id => "c-array-bound-loop";

    [GeneratedRegex(@"(?<array>[A-Za-z_]\w*)\s*\[\s*(?<var>[A-Za-z_]\w*)\s*\]")]
    private static partial Regex IndexUse();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "gcc", ExceptionType: "stack-buffer-overflow" or "global-buffer-overflow" }) return null;
        if (CCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        foreach (Match use in IndexUse().Matches(masked[number - 1]))
        {
            var array = Regex.Escape(use.Groups["array"].Value);
            var variable = Regex.Escape(use.Groups["var"].Value);

            var declaration = masked
                .Select(text => Regex.Match(text, $@"^\s*(?:(?:static|const)\s+)*[A-Za-z_][\w ]*?\s\**{array}\s*\[\s*(?<size>\d+|[A-Z_][A-Z0-9_]*)\s*\]"))
                .FirstOrDefault(m => m.Success);

            if (declaration is null) continue;

            var size = declaration.Groups["size"].Value;
            var loop = new Regex($@"\bfor\s*\(\s*(?:(?:int|long|short|unsigned|size_t)\s+)*{variable}\s*=\s*0\s*;\s*{variable}\s*(?<op><=|<)\s*(?<bound>[^;]+?)\s*;");

            for (var k = number - 1; k >= Math.Max(0, number - 16); k--)
            {
                if (loop.Match(masked[k]) is not { Success: true } header) continue;

                var op = header.Groups["op"];
                var bound = header.Groups["bound"];
                if (op.Value == "<" && bound.Value == size) return null;

                var original = source.Lines[k];

                return LocalFix.ReplaceLine(
                    Id, $"Stop the loop at the end of {use.Groups["array"].Value}: < {size}",
                    $"`{use.Groups["array"].Value}` has {size} elements, so its indexes run from 0 to one less than {size}. The loop on " +
                    $"line {k + 1} runs while {use.Groups["var"].Value} {op.Value} {bound.Value}, which writes past the end of the " +
                    "array into whatever memory sits next to it.",
                    source.Path, k + 1, original[..op.Index] + "< " + size + original[(bound.Index + bound.Length)..]);
            }
        }

        return null;
    }
}

/// <summary><c>C4700: uninitialized local variable 'sum' used</c> - a total that starts from whatever was in memory.</summary>
public sealed partial class CUninitialisedAccumulator : ILocalFixRule
{
    public string Id => "c-uninitialised-accumulator";

    [GeneratedRegex(@"^uninitialized local variable '(?<name>\w+)' used$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CCode.MsvcMessage(context.Error, "C4700", MsvcMessage()) is not { } message || CCode.Locate(context) is not { } at) return null;

        var name = message.Groups["name"].Value;
        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        // Only a running total or product: the first use adds to it, and nothing sets it before that.
        var use = masked[at.Number - 1];
        var start = Regex.IsMatch(use, $@"(?<![\w.>]){Regex.Escape(name)}\s*(?:\+=|-=|\+\+|--)|(?:\+\+|--)\s*{Regex.Escape(name)}\b|(?<![\w.>]){Regex.Escape(name)}\s*=\s*{Regex.Escape(name)}\s*[+-]")
            ? "0"
            : Regex.IsMatch(use, $@"(?<![\w.>]){Regex.Escape(name)}\s*\*=|(?<![\w.>]){Regex.Escape(name)}\s*=\s*{Regex.Escape(name)}\s*\*") ? "1" : null;

        if (start is null) return null;

        var (function, _) = CCode.EnclosingFunction(masked, at.Number - 1);
        var declaration = new Regex($@"^(?<head>\s*(?:(?:unsigned|signed|long|short|const)\s+)*(?:int|long|short|float|double|size_t|char)\s+(?:[A-Za-z_]\w*\s*(?:=[^,;]+)?\s*,\s*)*{Regex.Escape(name)})(?<tail>\s*[,;].*)$");

        var found = Enumerable.Range(function, at.Number - 1 - function).Where(i => declaration.IsMatch(masked[i])).ToList();
        if (found is not [var index]) return null;

        var original = source.Lines[index];
        var match = declaration.Match(original);

        return LocalFix.ReplaceLine(
            Id, $"Start {name} from {start}",
            $"`{name}` is declared without a value, so it starts as whatever happened to be in that memory, and the {(start == "0" ? "total" : "product")} " +
            $"is built on top of it. The program can print the right answer by luck - on another run, or another machine, it will not. A " +
            $"{(start == "0" ? "running total starts from 0" : "running product starts from 1")}.",
            source.Path, index + 1, match.Groups["head"].Value + " = " + start + match.Groups["tail"].Value) with
        {
            ResolvesWarning = "C4700",
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
