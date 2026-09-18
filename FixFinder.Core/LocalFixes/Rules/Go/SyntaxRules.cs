using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>missing import path</c> - <c>import fmt</c> without its quotes.</summary>
public sealed partial class GoImportQuotes : ILocalFixRule
{
    public string Id => "go-import-quotes";

    [GeneratedRegex(@"^(?:syntax error: )?missing import path")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var import = Regex.Match(at.Line, @"^(?<lead>\s*(?:import\s+)?)(?<path>[a-z][\w/.]*)\s*$");
        if (!import.Success || import.Groups["path"].Value == "import") return null;

        return LocalFix.ReplaceLine(
            Id, $"Quote the import: \"{import.Groups["path"].Value}\"",
            "An import path is a string, so it goes in double quotes: `import \"fmt\"`.",
            at.Source.Path, at.Number, $"{import.Groups["lead"].Value}\"{import.Groups["path"].Value}\"");
    }
}

/// <summary><c>syntax error: unexpected semicolon or newline before {</c> - the brace on the line below.</summary>
public sealed partial class GoBraceOnNextLine : ILocalFixRule
{
    public string Id => "go-brace-on-next-line";

    [GeneratedRegex(@"^syntax error: unexpected semicolon or newline before \{$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (line.Trim() != "{" || number < 2) return null;

        var (code, tail) = CodeText.SplitComment(source.Lines[number - 2], Syntax.CLike);
        if (code.Trim().Length == 0) return null;

        return new LocalFix
        {
            RuleId = Id, Title = "Put the { at the end of the line above",
            Explanation =
                "Go puts an invisible semicolon at the end of a line like this one, which ends the statement before the `{` below it can " +
                "start its block. The opening brace always goes on the same line.",
            File = source.Path, StartLine = number - 1, RemoveCount = 2, NewLines = [code.TrimEnd() + " {" + tail],
        };
    }
}

/// <summary><c>syntax error: unexpected keyword else</c> - <c>else</c> on the line after the closing brace.</summary>
public sealed partial class GoElseOnNextLine : ILocalFixRule
{
    public string Id => "go-else-on-next-line";

    [GeneratedRegex(@"^syntax error: unexpected (?:keyword )?else\b")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var k = number - 2;
        while (k >= 0 && source.Lines[k].Trim().Length == 0) k--;
        if (k < 0 || source.Lines[k].Trim() != "}" || !line.TrimStart().StartsWith("else", StringComparison.Ordinal)) return null;

        return new LocalFix
        {
            RuleId = Id, Title = "Put else on the same line as the }",
            Explanation =
                "Go ends a line after `}` with an invisible semicolon, which finishes the `if` - so an `else` on the next line has nothing " +
                "to belong to. It goes on the same line as the closing brace: `} else {`.",
            File = source.Path, StartLine = k + 1, RemoveCount = number - k,
            NewLines = [CodeText.Indentation(source.Lines[k]) + "} " + line.TrimStart()],
        };
    }
}

/// <summary><c>while x &lt; 3 {</c> - Go has only <c>for</c>.</summary>
public sealed partial class GoWhile : ILocalFixRule
{
    public string Id => "go-while";

    [GeneratedRegex(@"^(?<lead>\s*)while\s+(?<condition>.+?)\s*\{\s*$")]
    private static partial Regex Loop();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "go", ExceptionType: "compile error" } || GoCode.Locate(context) is not { } at) return null;
        if (!(context.Error.Message ?? "").StartsWith("syntax error", StringComparison.Ordinal)) return null;
        if (Loop().Match(at.Line) is not { Success: true } loop) return null;

        return LocalFix.ReplaceLine(
            Id, "Write the loop with for",
            "Go has no `while` - `for` does both jobs. With only a condition, `for x < 3 {` is exactly a while loop.",
            at.Source.Path, at.Number, $"{loop.Groups["lead"].Value}for {loop.Groups["condition"].Value} {{");
    }
}

/// <summary><c>for (i := 0; i &lt; 3; i++) {</c> - Go's for has no brackets.</summary>
public sealed partial class GoForParentheses : ILocalFixRule
{
    public string Id => "go-for-parentheses";

    [GeneratedRegex(@"^(?<lead>\s*)for\s*\((?<clauses>[^;{}]*;[^;{}]*;[^{}]*)\)\s*\{\s*$")]
    private static partial Regex Loop();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "go", ExceptionType: "compile error" } || GoCode.Locate(context) is not { } at) return null;
        if (Loop().Match(at.Line) is not { Success: true } loop) return null;

        return LocalFix.ReplaceLine(
            Id, "Remove the brackets around the for clauses",
            "Go writes `for` like C does but without the brackets: `for i := 0; i < 3; i++ {`.",
            at.Source.Path, at.Number, $"{loop.Groups["lead"].Value}for {loop.Groups["clauses"].Value.Trim()} {{");
    }
}

/// <summary><c>more than one character in rune literal</c> - a string written in single quotes.</summary>
public sealed partial class GoRuneLiteral : ILocalFixRule
{
    public string Id => "go-rune-literal";

    [GeneratedRegex(@"^more than one character in rune literal$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var multi = CppCode.StringLiterals(at.Line).Where(l => l.Quote == '\'' && Regex.Replace(at.Line[(l.Start + 1)..(l.End - 1)], @"\\.", "x").Length > 1).ToList();
        if (multi.Count == 0) return null;

        var corrected = at.Line;
        foreach (var (start, end, _) in Enumerable.Reverse(multi))
            corrected = corrected[..start] + "\"" + at.Line[(start + 1)..(end - 1)].Replace("\"", "\\\"") + "\"" + corrected[end..];

        return LocalFix.ReplaceLine(
            Id, "Put the text in double quotes",
            "Single quotes in Go hold one character - a rune, like `'A'`. A string goes in double quotes.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary><c>cannot use assignment x = 5 as value</c> - <c>=</c> in an <c>if</c> where <c>==</c> was meant.</summary>
public sealed partial class GoAssignmentInCondition : ILocalFixRule
{
    public string Id => "go-assignment-in-condition";

    [GeneratedRegex(@"^syntax error: cannot use assignment .+ as value$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var masked = CodeText.Mask(at.Line, Syntax.CLike);
        if (Regex.Match(masked, @"^\s*(?:\}\s*else\s+)?(?:if|for)\s+(?<condition>[^{]+)\{") is not { Success: true } condition) return null;

        var group = condition.Groups["condition"];
        var equals = Regex.Matches(masked.Substring(group.Index, group.Length), @"(?<![=!<>:+\-*/%&|^])=(?!=)").ToList();
        if (equals is not [var only]) return null;

        var index = group.Index + only.Index;

        return LocalFix.ReplaceLine(
            Id, "Compare with == instead of =",
            "`=` stores a value; `==` compares two. An `if` needs a comparison, so it is `==`.",
            at.Source.Path, at.Number, at.Line[..index] + "==" + at.Line[(index + 1)..]);
    }
}

/// <summary><c>newline in string</c> - a string with no closing quote.</summary>
public sealed partial class GoUnclosedString : ILocalFixRule
{
    public string Id => "go-unclosed-string";

    [GeneratedRegex(@"^newline in string$|^string literal not terminated$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;
        if (BraceRules.CloseString(at.Line, Syntax.CLike) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, "Close the string", BraceRules.CloseStringExplanation, at.Source.Path, at.Number, corrected);
    }
}

/// <summary><c>unexpected newline in argument list; possibly missing comma or )</c> - a call left open.</summary>
public sealed partial class GoMissingClosingParen : ILocalFixRule
{
    public string Id => "go-missing-closing-paren";

    [GeneratedRegex(@"^syntax error: unexpected newline in argument list; possibly missing comma or \)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var (code, tail) = CodeText.SplitComment(at.Line, Syntax.CLike);
        var masked = CodeText.Mask(code, Syntax.CLike);
        if (masked.Count(c => c == '(') - masked.Count(c => c == ')') != 1) return null;

        var trimmed = code.TrimEnd();

        return LocalFix.ReplaceLine(
            Id, "Add the missing closing bracket",
            $"A `(` on line {at.Number} is never closed before the line ends.",
            at.Source.Path, at.Number, trimmed + ")" + code[trimmed.Length..] + tail);
    }
}

/// <summary><c>unexpected EOF, expected }</c> - a block never closed.</summary>
public sealed partial class GoMissingClosingBrace : ILocalFixRule
{
    public string Id => "go-missing-closing-brace";

    [GeneratedRegex(@"^syntax error: unexpected EOF, expected \}$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is null) return null;
        if (context.Read(context.Frame?.File) is not { } source || !GoCode.IsGo(source)) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var unclosed = new Stack<int>();

        for (var i = 0; i < masked.Count; i++)
        {
            foreach (var c in masked[i])
            {
                if (c == '{') unclosed.Push(i);
                else if (c == '}' && !unclosed.TryPop(out _)) return null;
            }
        }

        if (unclosed.Count != 1) return null;

        var opener = unclosed.Pop();
        var last = source.Count - 1;
        while (last > opener && source.Lines[last].Trim().Length == 0) last--;

        return LocalFix.Insert(
            Id, $"Close the block opened on line {opener + 1}",
            $"The `{{` on line {opener + 1} is never closed, so the file ends inside it.",
            source.Path, last + 2, [CodeText.Indentation(source.Lines[opener]) + "}"]);
    }
}

/// <summary><c>non-declaration statement outside function body</c> on a lone <c>}</c> - or on <c>Func main()</c>.</summary>
public sealed partial class GoOutsideFunction : ILocalFixRule
{
    public string Id => "go-outside-function";

    [GeneratedRegex(@"^syntax error: non-declaration statement outside function body$")]
    private static partial Regex Message();

    private static readonly string[] TopLevel = ["func", "type", "var", "const", "import", "package"];

    private static readonly Dictionary<string, string> Borrowed = new(StringComparer.Ordinal)
    {
        ["function"] = "func", ["def"] = "func", ["fn"] = "func", ["fun"] = "func", ["Function"] = "func", ["class"] = "type",
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (line.Trim() == "}")
        {
            var depth = 0;
            var stray = new List<int>();

            for (var i = 0; i < masked.Count; i++)
            {
                foreach (var c in masked[i])
                {
                    if (c == '{') depth++;
                    else if (c == '}' && depth == 0) stray.Add(i);
                    else if (c == '}') depth--;
                }
            }

            if (stray is not [var extra] || extra != number - 1) return null;

            return CCode.RemoveLine(
                Id, $"Remove the extra }} on line {number}",
                $"The file has one more `}}` than `{{`. The one on line {number} closes nothing - every block is already closed before it.",
                source.Path, number);
        }

        var first = Regex.Match(masked[number - 1], @"^(?<word>[A-Za-z_]\w*)\b");
        if (!first.Success) return null;

        var word = first.Groups["word"].Value;
        if (GoCode.Keywords.Contains(word)) return null;

        var right = Borrowed.GetValueOrDefault(word) ?? CodeText.Nearest(word, TopLevel);
        if (right is null) return null;

        return LocalFix.ReplaceLine(
            Id, $"Change {word} to {right}",
            Borrowed.ContainsKey(word) ? $"`{word}` is how another language declares that. Go writes `{right}`." : $"Go keywords are lower case: `{right}`, not `{word}`.",
            source.Path, number, right + line[word.Length..]);
    }
}

/// <summary><c>undefined: console</c>, <c>System</c>, <c>Console</c> - how other languages print.</summary>
public sealed partial class GoForeignPrint : ILocalFixRule
{
    public string Id => "go-foreign-print";

    [GeneratedRegex(@"^undefined: (?<name>console|System|Console)$")]
    private static partial Regex Undefined();

    [GeneratedRegex(@"^""fmt"" imported and not used$")]
    private static partial Regex UnusedFmt();

    [GeneratedRegex(@"^(?<lead>\s*)(?<call>console\s*\.\s*log|System\s*\.\s*out\s*\.\s*println|System\s*\.\s*out\s*\.\s*print|Console\s*\.\s*WriteLine|Console\s*\.\s*Write)\s*\((?<args>.*)\)\s*;?\s*$")]
    private static partial Regex Statement();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error.LanguageId != "go" || (Undefined().IsMatch(error.Message ?? "") is false && UnusedFmt().IsMatch(error.Message ?? "") is false)) return null;

        var target = Undefined().IsMatch(error.Message ?? "") ? error : GoCode.OtherErrors(context, Undefined()).Select(x => x.Error).FirstOrDefault();
        if (target is null || GoCode.LocateError(context, target) is not { } at || !GoCode.HasImport(at.Source, "fmt")) return null;

        if (Statement().Match(CodeText.Mask(at.Line, Syntax.CLike)) is not { Success: true } statement) return null;

        var call = Regex.Replace(statement.Groups["call"].Value, @"\s+", "");
        var newline = call is "console.log" or "System.out.println" or "Console.WriteLine";
        var args = at.Line.Substring(statement.Groups["args"].Index, statement.Groups["args"].Length);
        var language = call.StartsWith("console", StringComparison.Ordinal) ? "JavaScript" : call.StartsWith("System", StringComparison.Ordinal) ? "Java" : "C#";

        return LocalFix.ReplaceLine(
            Id, newline ? "Print with fmt.Println" : "Print with fmt.Print",
            $"`{call}` is {language}. Go prints with the `fmt` package: `fmt.Println` ends the line, `fmt.Print` does not.",
            at.Source.Path, at.Number, $"{statement.Groups["lead"].Value}fmt.{(newline ? "Println" : "Print")}({args})");
    }
}

/// <summary><c>undefined: null</c>, <c>True</c>, <c>None</c> - other languages' words for Go's <c>nil</c> and <c>true</c>.</summary>
public sealed partial class GoForeignWord : ILocalFixRule
{
    public string Id => "go-foreign-word";

    [GeneratedRegex(@"^undefined: (?<name>null|NULL|None|undefined|True|False|nullptr)$")]
    private static partial Regex Message();

    private static readonly Dictionary<string, (string Right, string From)> Words = new(StringComparer.Ordinal)
    {
        ["null"] = ("nil", "Java, C# and JavaScript"), ["NULL"] = ("nil", "C"), ["None"] = ("nil", "Python"),
        ["undefined"] = ("nil", "JavaScript"), ["nullptr"] = ("nil", "C++"), ["True"] = ("true", "Python"), ["False"] = ("false", "Python"),
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var word = message.Groups["name"].Value;
        var (right, from) = Words[word];
        var hits = JavaScriptCode.UnqualifiedUses(CodeText.Mask(at.Line, Syntax.CLike), word);
        if (hits.Count == 0) return null;

        var corrected = at.Line;
        foreach (var index in Enumerable.Reverse(hits)) corrected = corrected[..index] + right + corrected[(index + word.Length)..];

        return LocalFix.ReplaceLine(Id, $"Write {word} as {right}", $"`{word}` is {from}. In Go it is `{right}`.", at.Source.Path, at.Number, corrected);
    }
}

/// <summary><c>undefined: count</c> on <c>count = 5</c> - a first assignment that needed <c>:=</c> to declare the variable.</summary>
public sealed partial class GoShortDeclare : ILocalFixRule
{
    public string Id => "go-short-declare";

    [GeneratedRegex(@"^undefined: (?<name>[A-Za-z_]\w*)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var name = Regex.Escape(message.Groups["name"].Value);
        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (GoCode.EnclosingFunction(masked, at.Number - 1) is not { } function) return null;

        var word = new Regex($@"(?<![\w.]){name}(?!\w)");
        var first = Enumerable.Range(function.Header + 1, function.Close - function.Header).FirstOrDefault(i => word.IsMatch(masked[i]), -1);
        if (first < 0) return null;

        var assignment = Regex.Match(masked[first], $@"^(?<lead>\s*){name}\s*(?<op>=)(?!=)\s*\S");
        if (!assignment.Success) return null;

        var op = assignment.Groups["op"].Index;
        var original = at.Source.Lines[first];

        return LocalFix.ReplaceLine(
            Id, $"Declare {message.Groups["name"].Value} with :=",
            $"`=` gives an existing variable a new value, and `{message.Groups["name"].Value}` does not exist yet. `:=` declares it and gives it " +
            "its first value in one go - Go works out the type from the value.",
            at.Source.Path, first + 1, original[..op] + ":=" + original[(op + 1)..]);
    }
}

/// <summary><c>no new variables on left side of :=</c> - declaring again what already exists.</summary>
public sealed partial class GoRedeclared : ILocalFixRule
{
    public string Id => "go-redeclared";

    [GeneratedRegex(@"^no new variables on left side of :=$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var hits = Regex.Matches(CodeText.Mask(at.Line, Syntax.CLike), ":=").ToList();
        if (hits is not [var hit]) return null;

        return LocalFix.ReplaceLine(
            Id, "Assign with = instead of :=",
            "`:=` declares new variables, and everything on its left already exists. `=` gives the existing variable its new value.",
            at.Source.Path, at.Number, at.Line[..hit.Index] + "=" + at.Line[(hit.Index + 2)..]);
    }
}

/// <summary><c>function main is undeclared in the main package</c> - <c>Main</c> or <c>mian</c>.</summary>
public sealed partial class GoMainName : ILocalFixRule
{
    public string Id => "go-main-name";

    [GeneratedRegex(@"^\s*func\s+(?<name>\w+)\s*\(\s*\)\s*\{")]
    private static partial Regex Function();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "go", Message: "function main is undeclared in the main package" }) return null;

        var functions = new List<(SourceFile Source, int Index, Group Name)>();

        foreach (var source in GoCode.SourceFiles(context))
        {
            var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

            for (var i = 0; i < masked.Count; i++)
            {
                if (Function().Match(masked[i]) is not { Success: true } function) continue;
                if (function.Groups["name"].Value == "main") return null;

                functions.Add((source, i, function.Groups["name"]));
            }
        }

        if (CodeText.Nearest("main", functions.Select(f => f.Name.Value)) is not { } wrong) return null;
        if (functions.Where(f => f.Name.Value == wrong).ToList() is not [var (file, index, name)]) return null;

        var line = file.Lines[index];

        return LocalFix.ReplaceLine(
            Id, $"Rename {wrong} to main",
            $"A Go program starts at a function called exactly `main`, in lower case, and there is none - `{wrong}` is a letter away.",
            file.Path, index + 1, line[..name.Index] + "main" + line[(name.Index + name.Length)..]);
    }
}

/// <summary><c>package command-line-arguments is not a main package</c> - a program whose file says <c>package app</c>.</summary>
public sealed partial class GoPackageMain : ILocalFixRule
{
    public string Id => "go-package-main";

    [GeneratedRegex(@"is not a main package$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.LanguageId != "go" || !Message().IsMatch(context.Error.Message ?? "")) return null;

        var programs = GoCode.SourceFiles(context)
            .Where(source => source.Lines.Any(line => Regex.IsMatch(line, @"^\s*func\s+main\s*\(\s*\)")))
            .Select(source => (Source: source, Index: Enumerable.Range(0, source.Count).FirstOrDefault(i => Regex.IsMatch(source.Lines[i], @"^\s*package\s+\w+"), -1)))
            .Where(x => x.Index >= 0 && !Regex.IsMatch(x.Source.Lines[x.Index], @"^\s*package\s+main\b"))
            .ToList();

        if (programs is not [var (file, index)]) return null;

        var package = Regex.Match(file.Lines[index], @"^(?<lead>\s*package\s+)(?<name>\w+)");

        return LocalFix.ReplaceLine(
            Id, "Declare package main",
            $"Only `package main` can be run as a program - `package {package.Groups["name"].Value}` is a library for other code to import, even " +
            "with a `main` function in it.",
            file.Path, index + 1, package.Groups["lead"].Value + "main" + file.Lines[index][(package.Index + package.Length)..]);
    }
}
