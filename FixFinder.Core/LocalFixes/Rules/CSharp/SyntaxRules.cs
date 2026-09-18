using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using FixFinder.Core.Logic;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>CS1002: ; expected</c>, placed exactly where Roslyn points.</summary>
public sealed class CSharpMissingSemicolon : ILocalFixRule
{
    public string Id => "csharp-missing-semicolon";

    public LocalFix? Propose(LocalFixContext context)
    {
        var commaExpected = CSharpCode.HasErrorCode(context, "CS1003") && (context.Error.Message ?? "").Contains("',' expected", StringComparison.Ordinal);
        if (!CSharpCode.HasErrorCode(context, "CS1002") && !commaExpected || CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, index) = at;
        if (index < 0) return null;

        var code = line[..index].TrimEnd();
        if (code.Length == 0 || code.EndsWith(';') || code.EndsWith('{') || code.EndsWith('}')) return null;

        // "',' expected" also means a declaration that runs on into the next line: int x = 3 followed by a new statement.
        if (commaExpected && (code.EndsWith(',') || line[index..].Trim().Length > 0 || !StartsStatement(source, number))) return null;

        return LocalFix.ReplaceLine(
            Id, "Add the missing semicolon",
            $"Every C# statement ends with a semicolon, and the one on line {number} does not.",
            source.Path, number, code + ";" + line[code.Length..]);
    }

    private static bool StartsStatement(SourceFile source, int number)
    {
        for (var next = number + 1; next <= source.Count; next++)
        {
            var text = source.Line(next)!.Trim();
            if (text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal)) continue;

            return System.Text.RegularExpressions.Regex.IsMatch(text, @"^(?:[A-Za-z_]\w*\s*[.(=\[]|[A-Za-z_][\w<>\[\],?]*\s+[A-Za-z_]\w*\s*[=;(]|(?:if|for|foreach|while|return|var|switch|try|do)\b)");
        }

        return false;
    }
}

/// <summary><c>CS1513: } expected</c> - the file ends with a block still open.</summary>
public sealed class CSharpMissingClosingBrace : ILocalFixRule
{
    public string Id => "csharp-missing-closing-brace";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS1513") || context.Read(context.Frame?.File) is not { } source) return null;
        if (!source.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var unclosed = new Stack<int>();
        var stray = 0;

        for (var i = 0; i < masked.Count; i++)
        {
            foreach (var c in masked[i])
            {
                if (c == '{') unclosed.Push(i);
                else if (c == '}' && !unclosed.TryPop(out _)) stray++;
            }
        }

        if (stray > 0 || unclosed.Count != 1) return null;

        var opener = unclosed.Pop();
        var last = source.Count - 1;
        while (last > 0 && source.Lines[last].Trim().Length == 0) last--;

        return LocalFix.Insert(
            Id, $"Close the block opened on line {opener + 1}",
            $"The `{{` on line {opener + 1} is never closed - the file ends first.",
            source.Path, last + 2, [CodeText.Indentation(source.Lines[opener]) + "}"]);
    }
}

/// <summary><c>elif</c> from Python, which C# writes <c>else if</c>.</summary>
public sealed partial class CSharpElif : ILocalFixRule
{
    public string Id => "csharp-elif";

    [GeneratedRegex(@"(?<![\w.])elif(?!\w)")]
    private static partial Regex Elif();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context) || CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var hits = Elif().Matches(CodeText.Mask(line, Syntax.CLike));
        if (hits.Count == 0) return null;

        var corrected = line;
        foreach (var hit in hits.Reverse()) corrected = corrected[..hit.Index] + "else if" + corrected[(hit.Index + hit.Length)..];

        return LocalFix.ReplaceLine(Id, "Write elif as else if", "C# has no `elif`; it is `else if`.", source.Path, number, corrected);
    }
}

/// <summary><c>System.out.println</c> from Java, which C# writes <c>Console.WriteLine</c>.</summary>
public sealed partial class CSharpJavaPrint : ILocalFixRule
{
    public string Id => "csharp-java-print";

    [GeneratedRegex(@"System\.out\.(?<method>println|print)\s*\(")]
    private static partial Regex JavaPrint();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context) || CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var hits = JavaPrint().Matches(CodeText.Mask(line, Syntax.CLike));
        if (hits.Count != 1) return null;

        var replacement = hits[0].Groups["method"].Value == "println" ? "Console.WriteLine(" : "Console.Write(";

        return LocalFix.ReplaceLine(
            Id, $"Write it as {replacement.TrimEnd('(')}",
            "`System.out.println` is Java. C# writes to the console with `Console.WriteLine`.",
            source.Path, number, line[..hits[0].Index] + replacement + line[(hits[0].Index + hits[0].Length)..]);
    }
}

/// <summary><c>if x &gt; 5 {</c> - in C# the condition needs its brackets.</summary>
public sealed partial class CSharpConditionParentheses : ILocalFixRule
{
    public string Id => "csharp-condition-parentheses";

    [GeneratedRegex(@"^(?<lead>\s*(?:\}\s*)?(?:else\s+)?)(?<keyword>if|while|switch)\s+(?<condition>[^({\s][^{]*?)\s*(?<brace>\{?)$")]
    private static partial Regex Condition();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS1003", "CS1525", "CS1026") || CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.CLike);

        if (Condition().Match(code) is not { Success: true } match) return null;

        var brace = match.Groups["brace"].Length > 0 ? " {" : "";

        return LocalFix.ReplaceLine(
            Id, $"Put the {match.Groups["keyword"].Value} condition in brackets",
            "In C# the condition of `if`, `while` and `switch` always goes in brackets.",
            source.Path, number, $"{match.Groups["lead"].Value}{match.Groups["keyword"].Value} ({match.Groups["condition"].Value}){brace}{tail}");
    }
}

/// <summary><c>CS1012: Too many characters in character literal</c> - text in single quotes.</summary>
public sealed class CSharpCharLiteralString : ILocalFixRule
{
    public string Id => "csharp-char-literal-string";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS1012") || CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, index) = at;
        if (index < 0 || index >= line.Length || line[index] != '\'') return null;

        var close = line.IndexOf('\'', index + 1);
        if (close < 0) return null;

        var content = line[(index + 1)..close];
        if (content.Contains('"') || content.Contains('\\')) return null;

        return LocalFix.ReplaceLine(
            Id, "Put the text in double quotes",
            "Single quotes hold one character in C#. Text - a string - goes in double quotes.",
            source.Path, number, line[..index] + "\"" + content + "\"" + line[(close + 1)..]);
    }
}

/// <summary><c>CS0230</c>: <c>foreach (item in items)</c> needs a type, and <c>var</c> is enough.</summary>
public sealed partial class CSharpForeachType : ILocalFixRule
{
    public string Id => "csharp-foreach-type";

    [GeneratedRegex(@"foreach\s*\(\s*(?<name>[A-Za-z_]\w*)\s+in\b")]
    private static partial Regex Foreach();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0230") || CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        if (Foreach().Match(CodeText.Mask(line, Syntax.CLike)) is not { Success: true } match) return null;

        var name = match.Groups["name"];

        return LocalFix.ReplaceLine(
            Id, $"Declare {name.Value} with var",
            "A foreach loop declares its variable, so it needs a type - `var` lets C# work it out.",
            source.Path, number, line[..name.Index] + "var " + line[name.Index..]);
    }
}

/// <summary><c>CS8641: 'else' cannot start a statement</c> - from <c>if (x);</c>.</summary>
public sealed class CSharpIfSemicolon : ILocalFixRule
{
    public string Id => "csharp-if-semicolon";

    public LocalFix? Propose(LocalFixContext context)
    {
        var recognised = CSharpCode.HasErrorCode(context, "CS8641") || CSharpCode.HasErrorCode(context, "CS1525") && context.Error.Message == "Invalid expression term 'else'";
        if (!recognised || CSharpCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);

        // Roslyn places the error at the end of the token before the else - the closing brace, a line above it.
        var elseLine = Enumerable.Range(at.Number - 1, 3).FirstOrDefault(i => i < masked.Count && Regex.IsMatch(masked[i], @"\belse\b"), -1);
        if (elseLine < 0 || BraceRules.IfSemicolon(at.Source.Lines, masked, elseLine) is not { } fix) return null;

        return LocalFix.ReplaceLine(Id, BraceRules.IfSemicolonTitle, BraceRules.IfSemicolonExplanation, at.Source.Path, fix.Line + 1, fix.Corrected);
    }
}

/// <summary><c>CS1056: Unexpected character '\u201C'</c>.</summary>
public sealed class CSharpSmartQuotes : ILocalFixRule
{
    public string Id => "csharp-smart-quotes";

    public LocalFix? Propose(LocalFixContext context) =>
        CSharpCode.HasErrorCode(context, "CS1056", "CS1010", "CS1039") && CSharpCode.Locate(context) is { } at
            ? Guards.StraightenFix(Id, at.Source, at.Number)
            : null;
}

/// <summary><c>CS1520: Method must have a return type</c>, when what it returns says which.</summary>
public sealed partial class CSharpMissingReturnType : ILocalFixRule
{
    public string Id => "csharp-missing-return-type";

    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|internal|static|virtual|override|sealed|async)\s+)*(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*\{?\s*$")]
    private static partial Regex Header();

    [GeneratedRegex(@"\breturn\b\s*(?<value>[^;]*);")]
    private static partial Regex Return();

    [GeneratedRegex(@"\bclass\s+(?<name>\w+)")]
    private static partial Regex Class();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS1520") || CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, _, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Header().Match(masked[number - 1]) is not { Success: true } header) return null;

        var name = header.Groups["name"].Value;

        // A constructor whose name does not match its class gets the same error, and a return type is not its fix.
        var owner = Enumerable.Range(0, number).Reverse().Select(i => Class().Match(masked[i])).FirstOrDefault(m => m.Success);
        if (owner is not null && CodeText.Distance(owner.Groups["name"].Value, name) <= 2) return null;

        if (Body(masked, number - 1) is not { } body) return null;

        var parameters = header.Groups["parameters"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[1], p => p[0], StringComparer.Ordinal);

        var returned = body.SelectMany(text => Return().Matches(text)).Select(m => m.Groups["value"].Value.Trim()).ToList();

        var types = returned.All(value => value.Length == 0)
            ? ["void"]
            : returned.Select(value => TypeOf(value, parameters)).Distinct().ToList();

        if (types is not [{ } type]) return null;

        var column = header.Groups["name"].Index;
        var line = source.Lines[number - 1];

        return LocalFix.ReplaceLine(
            Id, $"Give {name} the return type {type}",
            type == "void"
                ? $"Every C# method says what it returns. `{name}` returns nothing, which is written `void`."
                : $"Every C# method says what it returns, and everything `{name}` returns is {(type == "int" ? "an" : "a")} `{type}`.",
            source.Path, number, line[..column] + type + " " + line[column..]);
    }

    private static List<string>? Body(IReadOnlyList<string> masked, int header)
    {
        var depth = 0;
        var started = false;
        var lines = new List<string>();

        for (var i = header; i < masked.Count && i < header + 200; i++)
        {
            lines.Add(masked[i]);

            foreach (var c in masked[i])
            {
                if (c == '{') { depth++; started = true; }
                else if (c == '}') depth--;
            }

            if (started && depth == 0) return lines;
        }

        return null;
    }

    private static string? TypeOf(string value, IReadOnlyDictionary<string, string> parameters)
    {
        if (Regex.IsMatch(value, @"^-?\d+$")) return "int";
        if (Regex.IsMatch(value, @"^-?\d+\.\d+$")) return "double";
        if (value is "true" or "false") return "bool";
        if (Regex.IsMatch(value, @"^""[^""]*""$")) return "string";

        // Arithmetic on parameters of one numeric type is that type.
        if (!Regex.IsMatch(value, @"^[\w\s+\-*/%()]+$")) return null;

        var types = Regex.Matches(value, @"[A-Za-z_]\w*").Select(m => parameters.GetValueOrDefault(m.Value)).Distinct().ToList();

        return types is [{ } only] && only is "int" or "long" or "double" or "float" or "decimal" ? only : null;
    }
}

/// <summary><c>CS0542</c>: <c>public void Dog(string name)</c> inside <c>class Dog</c> - a constructor with a return type.</summary>
public sealed partial class CSharpConstructorReturnType : ILocalFixRule
{
    public string Id => "csharp-constructor-return-type";

    [GeneratedRegex(@"^'(?<name>\w+)': member names cannot be the same as their enclosing type$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0542") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var name = message.Groups["name"].Value;
        var method = Regex.Match(line, $@"^\s*(?:(?:public|protected|internal|private)\s+)?(?<void>void\s+){Regex.Escape(name)}\s*\(");
        if (!method.Success) return null;

        var remove = method.Groups["void"];

        return LocalFix.ReplaceLine(
            Id, $"Remove void from the {name} constructor",
            $"A constructor has no return type. With `void` in front, `{name}(...)` is an ordinary method - and C# does not allow a " +
            "method to share its class's name.",
            source.Path, number, line[..remove.Index] + line[(remove.Index + remove.Length)..]);
    }
}

/// <summary><c>CS0128</c> / <c>CS0136</c>: a variable declared a second time where an assignment was meant.</summary>
public sealed partial class CSharpRedefinition : ILocalFixRule
{
    public string Id => "csharp-redefinition";

    [GeneratedRegex(@"^A local (?:variable or function|or parameter) named '(?<name>\w+)' (?:is already defined in this scope|cannot be declared in this scope)")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0128", "CS0136") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var name = Regex.Escape(message.Groups["name"].Value);
        var redeclared = Regex.Match(line, $@"^(?<lead>\s*)(?!return\b)[\w<>\[\],.?]+\s+{name}\s*=\s*(?<value>.+?)\s*;(?<tail>\s*(?://.*)?)$");
        if (!redeclared.Success || Regex.IsMatch(line, @"^\s*(?:const|using)\b")) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var (first, _) = JavaCode.EnclosingMethod(masked, number - 1);
        var earlier = new Regex($@"(?<![\w.])[\w<>\[\],.?]+\s+{name}\s*[=;]");

        var original = Enumerable.Range(first, Math.Max(0, number - 1 - first)).LastOrDefault(i => earlier.IsMatch(masked[i]), -1);
        if (original < 0) return null;

        var variable = message.Groups["name"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Assign to {variable} instead of declaring it again",
            $"`{variable}` is already declared on line {original + 1}. Writing the type again declares a second variable with the same name; " +
            "without it, the line gives the existing one a new value.",
            source.Path, number, $"{redeclared.Groups["lead"].Value}{variable} = {redeclared.Groups["value"].Value};{redeclared.Groups["tail"].Value}");
    }
}

/// <summary><c>CS0163: Control cannot fall through from one case label to another</c> - a missing <c>break</c>.</summary>
public sealed partial class CSharpSwitchFallThrough : ILocalFixRule
{
    public string Id => "csharp-switch-fall-through";

    [GeneratedRegex(@"^\s*(?:case\b.+|default)\s*:\s*$")]
    private static partial Regex Label();

    [GeneratedRegex(@"^\s*(?:break|return|throw|continue|goto)\b")]
    private static partial Regex Exit();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0163") || CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, _, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var label = number - 1;
        if (!Label().IsMatch(masked[label])) return null;

        var depths = Brackets.BraceDepths(masked);
        var last = -1;

        for (var i = label + 1; i < masked.Count; i++)
        {
            if (depths[i] < depths[label]) break;
            if (depths[i] == depths[label] && (Label().IsMatch(masked[i]) || masked[i].TrimStart().StartsWith('}'))) break;
            if (masked[i].Trim().Length > 0) last = i;
        }

        if (last < 0 || depths[last] != depths[label] || !masked[last].TrimEnd().EndsWith(';') || Exit().IsMatch(masked[last])) return null;

        return LocalFix.Insert(
            Id, "End the case with break",
            "In C# one `case` cannot run on into the next - every section has to end with `break`, `return` or `throw`. This one runs its " +
            "statements and then has nowhere to go.",
            source.Path, last + 2, [CodeText.Indentation(source.Lines[last]) + "break;"]);
    }
}

/// <summary><c>CS0160: A previous catch clause already catches all exceptions of this or of a super type</c>.</summary>
public sealed class CSharpCatchOrder : ILocalFixRule
{
    public string Id => "csharp-catch-order";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0160") || CSharpCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (BraceRules.SwapCatch(at.Source.Lines, masked, at.Number - 1) is not { } swap) return null;

        return new LocalFix
        {
            RuleId = Id, Title = "Put the more specific catch first", Explanation = BraceRules.CatchOrderExplanation,
            File = at.Source.Path, StartLine = swap.Start + 1, RemoveCount = swap.Count, NewLines = swap.Lines,
        };
    }
}
