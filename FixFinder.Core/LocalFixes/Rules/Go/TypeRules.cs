using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>mismatched types untyped string and int</c> - a number, or a byte, added to a string.</summary>
public sealed partial class GoStringPlusNumber : ILocalFixRule
{
    public string Id => "go-string-plus-number";

    [GeneratedRegex(@"^invalid operation: (?<expression>.+) \(mismatched types (?:untyped )?(?<left>\w+) and (?:untyped )?(?<right>\w+)\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var (left, right) = (message.Groups["left"].Value, message.Groups["right"].Value);
        var number = left == "string" ? right : right == "string" ? left : null;
        if (number is null || !GoCode.NumericTypes.Contains(number)) return null;

        var expression = message.Groups["expression"].Value;
        if (!expression.Contains('+')) return null;

        var parts = expression.Split('+').Select(p => p.Trim()).ToList();
        var operand = parts.Where(p => !p.StartsWith('"')).ToList();
        if (operand is not [var value]) return null;

        var (source, lineNumber, line) = at;
        var index = line.IndexOf(value, StringComparison.Ordinal);
        if (index < 0 || line.IndexOf(value, index + 1, StringComparison.Ordinal) >= 0) return null;

        string wrapped, how;

        if (number is "byte" or "rune")
        {
            (wrapped, how) = ($"string({value})", $"`{value}` is a single {number} - a character's code - and Go will not add it to a string. `string({value})` makes it text.");
        }
        else if (number == "int" && GoCode.HasImport(source, "strconv"))
        {
            (wrapped, how) = ($"strconv.Itoa({value})", $"Go never turns a number into text by itself. `strconv.Itoa({value})` does it.");
        }
        else if (GoCode.HasImport(source, "fmt"))
        {
            (wrapped, how) = ($"fmt.Sprint({value})", $"Go never turns a number into text by itself. `fmt.Sprint({value})` does it, whatever kind of number it is.");
        }
        else
        {
            return null;
        }

        return LocalFix.ReplaceLine(Id, $"Turn {value} into text with {wrapped[..wrapped.IndexOf('(')]}", how, source.Path, lineNumber, line[..index] + wrapped + line[(index + value.Length)..]);
    }
}

/// <summary><c>name[0] == "A"</c> - a byte compared with a string.</summary>
public sealed partial class GoByteComparedWithString : ILocalFixRule
{
    public string Id => "go-byte-compared-with-string";

    [GeneratedRegex(@"^invalid operation: .+ (?:==|!=) .+ \(mismatched types (?:byte|rune) and untyped string\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var single = CppCode.StringLiterals(at.Line)
            .Where(l => l.Quote == '"' && Regex.IsMatch(at.Line[(l.Start + 1)..(l.End - 1)], @"^(?:[^""\\]|\\.)$"))
            .Where(l => Regex.IsMatch(at.Line[..l.Start].TrimEnd(), @"(?:==|!=)$") || Regex.IsMatch(at.Line[l.End..].TrimStart(), @"^(?:==|!=)"))
            .ToList();

        if (single is not [var (start, end, _)]) return null;

        var inner = at.Line[(start + 1)..(end - 1)];
        var character = "'" + (inner == "'" ? "\\'" : inner) + "'";

        return LocalFix.ReplaceLine(
            Id, $"Compare with the character {character}",
            $"Indexing a string gives one byte, and {at.Line[start..end]} is a string - Go will not compare the two. A single character is " +
            $"written in single quotes: {character}.",
            at.Source.Path, at.Number, at.Line[..start] + character + at.Line[end..]);
    }
}

/// <summary><c>cannot use count (variable of type int) as float64 value</c> - a number of one kind where another is wanted.</summary>
public sealed partial class GoNumericConversion : ILocalFixRule
{
    public string Id => "go-numeric-conversion";

    [GeneratedRegex(@"^cannot use (?<name>[A-Za-z_]\w*) \(variable of type (?<from>\w+)\) as (?<to>\w+) value in ")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var (from, to) = (message.Groups["from"].Value, message.Groups["to"].Value);
        if (!GoCode.NumericTypes.Contains(from) || !GoCode.NumericTypes.Contains(to)) return null;

        var name = message.Groups["name"].Value;
        var hits = JavaScriptCode.UnqualifiedUses(CodeText.Mask(at.Line, Syntax.CLike), name)
            .Where(i => !Regex.IsMatch(at.Line[..i], @"\bvar\s+$"))
            .ToList();

        if (hits is not [var index]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Convert {name} with {to}({name})",
            $"Go never converts between number types by itself, even from `{from}` to `{to}` - it has to be asked: `{to}({name})`.",
            at.Source.Path, at.Number, at.Line[..index] + $"{to}({name})" + at.Line[(index + name.Length)..]);
    }
}

/// <summary><c>too many return values ...</summary>
public sealed partial class GoMissingReturnType : ILocalFixRule
{
    public string Id => "go-missing-return-type";

    [GeneratedRegex(@"^too many return values$")]
    private static partial Regex Message();

    [GeneratedRegex(@"have \((?<have>[^)]+)\)\s+want \(\)")]
    private static partial Regex Types();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;
        if (Types().Match(context.Error.RawText) is not { Success: true } types) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (GoCode.EnclosingFunction(masked, at.Number - 1) is not { } function) return null;

        var header = Regex.Match(masked[function.Header], @"^func\s+(?:\([^)]*\)\s*)?\w+\s*\([^)]*(?<close>\))\s*\{\s*$");
        if (!header.Success) return null;

        var have = types.Groups["have"].Value.Trim();
        var result = have.Contains(',') ? $"({have})" : have;
        var close = header.Groups["close"].Index + 1;
        var original = at.Source.Lines[function.Header];

        return LocalFix.ReplaceLine(
            Id, $"Declare that it returns {result}",
            $"A Go function says what it returns after its brackets, and this one says nothing - yet it returns {(have.Contains(',') ? "values" : "a value")} of type `{have}`.",
            at.Source.Path, function.Header + 1, original[..close] + " " + result + original[close..]);
    }
}

/// <summary><c>not enough return values ...</summary>
public sealed partial class GoNotEnoughReturnValues : ILocalFixRule
{
    public string Id => "go-not-enough-return-values";

    [GeneratedRegex(@"^not enough return values$")]
    private static partial Regex Message();

    [GeneratedRegex(@"have \((?<have>[^)]*)\)\s+want \((?<want>[^)]+)\)")]
    private static partial Regex Types();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;
        if (Types().Match(context.Error.RawText) is not { Success: true } types) return null;

        var have = types.Groups["have"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var want = types.Groups["want"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (have.Length >= want.Length || !want[^have.Length..].SequenceEqual(have)) return null;

        var returned = Regex.Match(at.Line, @"^(?<lead>\s*return\s+)(?<values>.+)$");
        if (!returned.Success) return null;

        var zeros = want[..(want.Length - have.Length)].Select(GoCode.ZeroValue).ToList();

        return LocalFix.ReplaceLine(
            Id, $"Return {string.Join(", ", zeros)} as well",
            $"The function returns `({types.Groups["want"].Value})`, and every `return` has to give all of them. On this path there is no real " +
            $"{string.Join(" or ", want[..zeros.Count])} to give, so it returns the zero value - which is what callers ignore when the error is not nil.",
            at.Source.Path, at.Number, returned.Groups["lead"].Value + string.Join(", ", zeros) + ", " + returned.Groups["values"].Value);
    }
}

/// <summary><c>assignment mismatch: 1 variable but strconv.Atoi returns 2 values</c>.</summary>
public sealed partial class GoTwoValues : ILocalFixRule
{
    public string Id => "go-two-values";

    [GeneratedRegex(@"^assignment mismatch: 1 variable but (?<call>[\w.]+) returns 2 values$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var assignment = Regex.Match(at.Line, @"^(?<lead>\s*)(?<name>[A-Za-z_]\w*)\s*(?<op>:=|=)\s*(?<rest>.+)$");
        if (!assignment.Success) return null;

        return LocalFix.ReplaceLine(
            Id, $"Take both results: {assignment.Groups["name"].Value}, _",
            $"`{message.Groups["call"].Value}` returns two things - the value and an error - and both have to be taken. `_` throws the error away, " +
            $"which is only right when it cannot fail; for input that can be wrong, name it `err` and check `if err != nil`.",
            at.Source.Path, at.Number, $"{assignment.Groups["lead"].Value}{assignment.Groups["name"].Value}, _ {assignment.Groups["op"].Value} {assignment.Groups["rest"].Value}");
    }
}

/// <summary><c>append(items, 3) (value of type []int) is not used</c> - the result of append thrown away.</summary>
public sealed partial class GoAppendNotUsed : ILocalFixRule
{
    public string Id => "go-append-not-used";

    [GeneratedRegex(@"^append\((?<slice>[A-Za-z_][\w.]*),.*\) \(value of type [^)]+\) is not used$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var slice = message.Groups["slice"].Value;
        var statement = Regex.Match(at.Line, $@"^(?<lead>\s*)(?<call>append\(\s*{Regex.Escape(slice)}\s*,.+\))\s*$");
        if (!statement.Success) return null;

        return LocalFix.ReplaceLine(
            Id, $"Store the result: {slice} = append(...)",
            "`append` does not change the slice it is given - it returns a new one with the values added, and that has to be stored back.",
            at.Source.Path, at.Number, $"{statement.Groups["lead"].Value}{slice} = {statement.Groups["call"].Value}");
    }
}

/// <summary><c>Square does not implement Shape (method Area has pointer receiver)</c>.</summary>
public sealed partial class GoPointerReceiver : ILocalFixRule
{
    public string Id => "go-pointer-receiver";

    [GeneratedRegex(@"^cannot use (?<type>\w+)\{.*does not implement (?<interface>\w+) \(method (?<method>\w+) has pointer receiver\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var type = message.Groups["type"].Value;
        var hits = Regex.Matches(CodeText.Mask(at.Line, Syntax.CLike), $@"(?<![\w&.]){Regex.Escape(type)}\s*\{{").ToList();
        if (hits is not [var hit]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Use a pointer: &{type}{{...}}",
            $"`{message.Groups["method"].Value}` is defined on `*{type}` - a pointer - so only a pointer to a `{type}` has it, and only a pointer " +
            $"satisfies `{message.Groups["interface"].Value}`. `&{type}{{...}}` makes the value and hands over its address.",
            at.Source.Path, at.Number, at.Line[..hit.Index] + "&" + at.Line[hit.Index..]);
    }
}

/// <summary><c>json: Unmarshal(non-pointer main.Config)</c> - decoding into a copy, which the result can never reach.</summary>
public sealed partial class GoUnmarshalPointer : ILocalFixRule
{
    public string Id => "go-unmarshal-pointer";

    [GeneratedRegex(@"^(?:json|xml|yaml): Unmarshal\(non-pointer [\w.\[\]*]+\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "go" } error || !Message().IsMatch(error.Message ?? "")) return null;
        if (GoCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (GoCode.EnclosingFunction(masked, at.Number - 1) is not { } function) return null;

        var found = Enumerable.Range(function.Header, at.Number - function.Header)
            .Select(i => (Index: i, Match: Regex.Match(masked[i], @"\b(?:json|xml)\.Unmarshal\s*\((?<data>(?:[^()]|\([^()]*\))*?),\s*(?<target>[A-Za-z_]\w*)\s*\)")))
            .Where(x => x.Match.Success)
            .ToList();

        if (found is not [var call]) return null;

        var target = call.Match.Groups["target"];
        var line = source.Lines[call.Index];

        return LocalFix.ReplaceLine(
            Id, $"Decode into {target.Value} itself: &{target.Value}",
            $"Go passes `{target.Value}` by value, so `Unmarshal` would get a copy and fill in the copy - and `{target.Value}` would stay empty. " +
            $"It refuses instead. `&{target.Value}` passes a pointer, so it fills in the variable itself.",
            source.Path, call.Index + 1, line[..target.Index] + "&" + line[target.Index..]);
    }
}
