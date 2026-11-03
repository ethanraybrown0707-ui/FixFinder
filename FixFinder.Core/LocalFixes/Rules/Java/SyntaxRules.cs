using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>';' expected</c>, placed exactly where javac's caret points.</summary>
public sealed class JavaMissingSemicolon : ILocalFixRule
{
    public string Id => "java-missing-semicolon";

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (!JavaCode.IsCompileError(error) || error.Message != "';' expected") return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        string corrected;

        if (CodeText.Caret(context.Output, error) is { } caret && caret.Echo == line && caret.Column <= line.Length)
        {
            corrected = line[..caret.Column] + ";" + line[caret.Column..];
        }
        else
        {
            var (code, tail) = CodeText.SplitComment(line, Syntax.CLike);
            if (code.Length == 0 || code.EndsWith(';')) return null;

            corrected = code + ";" + tail;
        }

        return LocalFix.ReplaceLine(
            Id,
            "Add the missing semicolon",
            $"javac expected a `;` at the end of the statement on line {number}.",
            source.Path, number, corrected);
    }
}

/// <summary>
/// <c>')' expected</c>, or <c>')' or ',' expected</c>, on a statement that opens more round brackets than it closes:
/// <c>System.out.println("total: " + (2 + 3);</c> - the brackets left open are closed where the statement ends, before its <c>;</c>.
/// </summary>
public sealed class JavaMissingClosingParenthesis : ILocalFixRule
{
    public string Id => "java-missing-closing-parenthesis";

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (!JavaCode.IsCompileError(error) || error.Message is not ("')' expected" or "')' or ',' expected")) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var (code, tail) = CodeText.SplitComment(line, Syntax.CLike);
        var masked = CodeText.Mask(code, Syntax.CLike);
        var leftOpen = masked.Count(c => c == '(') - masked.Count(c => c == ')');

        // Only a statement on one line that ends with its semicolon, and leaves round brackets - and only those - open.
        if (leftOpen < 1 || !code.TrimEnd().EndsWith(';') || masked.Count(c => c is '[' or '{') != masked.Count(c => c is ']' or '}')) return null;

        var end = code.TrimEnd().Length - 1;
        var corrected = code[..end] + new string(')', leftOpen) + code[end..] + tail;

        return LocalFix.ReplaceLine(
            Id,
            leftOpen == 1 ? "Close the bracket left open" : $"Close the {leftOpen} brackets left open",
            $"The statement on line {number} opens {(leftOpen == 1 ? "a round bracket it never closes" : $"{leftOpen} round brackets it never closes")}, " +
            "so javac reached its `;` still inside it. Each `(` needs its `)`; here they all go where the statement ends.",
            source.Path, number, corrected);
    }
}

/// <summary>
/// <c>missing return statement</c> at the end of a method that works its value out into a variable of the type it returns and
/// never hands it back: <c>return doubled;</c> after <c>int doubled = x * 2;</c> as its last line, or, after a loop that adds up
/// <c>int sum = 0;</c>, <c>return sum;</c> - when sum is the only variable of that type its body declares.
/// </summary>
public sealed partial class JavaMissingReturn : ILocalFixRule
{
    public string Id => "java-missing-return";

    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|static|final|synchronized)\s+)*(?<type>[\w$.]+(?:<[^()]*>)?(?:\[\])*)\s+(?<name>[\w$]+)\s*\((?<parameters>[^()]*)\)\s*(?:throws\s+[\w$., ]+)?\s*\{\s*$")]
    private static partial Regex MethodHeader();

    [GeneratedRegex(@"^(?<indent>\s*)(?:(?:final\s+)?(?<declared>[\w$.]+(?:<[^()]*>)?(?:\[\])*)\s+)?(?<variable>[\w$]+)\s*(?:[-+*/%]?=)(?!=).*;\s*$")]
    private static partial Regex GivenAValue();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "missing return statement" || JavaCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (masked[number - 1].Trim() != "}") return null;

        // The method the brace closes: up the file to the brace it matches, on the method's own line.
        var depth = 0;
        int? headerLine = null;
        for (var lineAbove = number - 1; lineAbove >= 0 && headerLine is null; lineAbove--)
        {
            depth += masked[lineAbove].Count(c => c == '}') - masked[lineAbove].Count(c => c == '{');
            if (depth == 0) headerLine = lineAbove;
        }

        if (headerLine is not { } header || MethodHeader().Match(masked[header]) is not { Success: true } method) return null;

        var returned = method.Groups["type"].Value;
        if (returned == "void") return null;

        // The method's own body - its lines indented least, not those inside a loop or an if of it - and its last line.
        var bodyLines = Enumerable.Range(header + 1, number - 2 - header).Where(lineIndex => masked[lineIndex].Trim().Length > 0).ToList();
        if (bodyLines.Count == 0) return null;

        var bodyIndent = bodyLines.Min(lineIndex => CodeText.Indentation(masked[lineIndex]).Length);
        if (bodyIndent <= CodeText.Indentation(masked[header]).Length) return null;

        var lastLine = bodyLines[^1];
        if (CodeText.Indentation(masked[lastLine]).Length != bodyIndent) return null;

        var variable = KeptInLastLine(masked, lastLine, returned, header, method.Groups["parameters"].Value)
                       ?? OnlyOneOfItsType(masked, bodyLines, bodyIndent, returned);
        if (variable is null) return null;

        var methodName = method.Groups["name"].Value;

        return LocalFix.Insert(
            Id, $"Return {variable} from {methodName}",
            $"`{methodName}` says it gives back {("aeiouAEIOU".Contains(returned[0]) ? "an" : "a")} {returned}, and works one out in `{variable}` - " +
            $"but never hands it back. A method that returns a value has to end by returning one; `return {variable};` gives back " +
            "the value it worked out.",
            source.Path, lastLine + 2, [CodeText.Indentation(source.Lines[lastLine]) + $"return {variable};"]);
    }

    /// <summary>The variable the method's last line gives a value - <c>int doubled = x * 2;</c> - when it is of the type returned.</summary>
    private static string? KeptInLastLine(IReadOnlyList<string> masked, int lastLine, string returned, int header, string parameters)
    {
        if (GivenAValue().Match(masked[lastLine]) is not { Success: true } given) return null;

        var variable = given.Groups["variable"].Value;
        var declaredAs = given.Groups["declared"].Success ? given.Groups["declared"].Value : DeclaredType(variable, masked, header, lastLine, parameters);

        return declaredAs == returned || declaredAs == "var" ? variable : null;
    }

    /// <summary>The one variable of the type returned that the method's own body declares - int sum = 0; - or null when there are none or several.</summary>
    private static string? OnlyOneOfItsType(IReadOnlyList<string> masked, IReadOnlyList<int> bodyLines, int bodyIndent, string returned)
    {
        var declared = bodyLines
            .Where(lineIndex => CodeText.Indentation(masked[lineIndex]).Length == bodyIndent)
            .Select(lineIndex => Regex.Match(masked[lineIndex], $@"^\s*(?:final\s+)?{Regex.Escape(returned)}\s+(?<variable>[\w$]+)\s*[=;]"))
            .Where(declaration => declaration.Success)
            .Select(declaration => declaration.Groups["variable"].Value)
            .ToList();

        return declared is [var only] ? only : null;
    }

    /// <summary>The type a variable was declared with in the method, or among its parameters; null when it is neither.</summary>
    private static string? DeclaredType(string variable, IReadOnlyList<string> masked, int header, int lastLine, string parameters)
    {
        var declaration = $@"(?:^|[(,]|\s)(?:final\s+)?(?<type>[\w$.]+(?:<[^()]*>)?(?:\[\])*)\s+{Regex.Escape(variable)}\s*(?:[=;,)]|$)";

        if (Regex.Match(parameters, declaration) is { Success: true } parameter) return parameter.Groups["type"].Value;

        for (var lineIndex = header + 1; lineIndex < lastLine; lineIndex++)
            if (Regex.Match(masked[lineIndex], declaration) is { Success: true } local) return local.Groups["type"].Value;

        return null;
    }
}

/// <summary><c>reached end of file while parsing</c> - a block never closed.</summary>
public sealed class JavaMissingClosingBrace : ILocalFixRule
{
    public string Id => "java-missing-closing-brace";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "reached end of file while parsing" || JavaCode.Locate(context) is not { Source: var source }) return null;

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
        while (last > 0 && source.Lines[last].Trim().Length == 0) last--;

        return LocalFix.Insert(
            Id, $"Close the block opened on line {opener + 1}",
            $"The `{{` on line {opener + 1} is never closed - the file ends first.",
            source.Path, last + 2, [CodeText.Indentation(source.Lines[opener]) + "}"]);
    }
}

/// <summary><c>unclosed string literal</c>.</summary>
public sealed class JavaUnclosedString : ILocalFixRule
{
    public string Id => "java-unclosed-string";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "unclosed string literal" || JavaCode.Locate(context) is not { } at) return null;
        if (BraceRules.CloseString(at.Line, Syntax.CLike) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, "Close the string", BraceRules.CloseStringExplanation, at.Source.Path, at.Number, corrected);
    }
}

/// <summary><c>'else' without 'if'</c> - from <c>if (x); {</c>.</summary>
public sealed class JavaIfSemicolon : ILocalFixRule
{
    public string Id => "java-if-semicolon";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "'else' without 'if'" || JavaCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (BraceRules.IfSemicolon(at.Source.Lines, masked, at.Number - 1) is not { } fix) return null;

        return LocalFix.ReplaceLine(Id, BraceRules.IfSemicolonTitle, BraceRules.IfSemicolonExplanation, at.Source.Path, fix.Line + 1, fix.Corrected);
    }
}

/// <summary><c>elif</c> from Python, which Java writes <c>else if</c>.</summary>
public sealed partial class JavaElif : ILocalFixRule
{
    public string Id => "java-elif";

    [GeneratedRegex(@"(?<![\w$.])elif(?=\s*\()")]
    private static partial Regex Elif();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var hits = Elif().Matches(CodeText.Mask(line, Syntax.CLike));
        if (hits.Count == 0) return null;

        return LocalFix.ReplaceLine(
            Id, "Write elif as else if", "Java has no `elif` - that is Python. In Java it is `else if`.",
            source.Path, number, CCode.ReplaceEach(line, hits, _ => "else if"));
    }
}

/// <summary><c>foreach (...)</c> from C#, and <c>for (String n in names)</c> from C# and Python - Java writes <c>for (String n :
/// names)</c>.</summary>
public sealed partial class JavaForEach : ILocalFixRule
{
    public string Id => "java-for-each";

    [GeneratedRegex(@"(?<![\w$.])foreach(?=\s*\()")]
    private static partial Regex Foreach();

    [GeneratedRegex(@"\b(?:for|foreach)\s*\(\s*(?:final\s+)?[\w$<>\[\],.? ]*?[\w$>\]]\s+[A-Za-z_$][\w$]*\s+(?<in>in)\s+")]
    private static partial Regex In();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);

        var foreachHits = Foreach().Matches(masked);
        var inHit = In().Match(masked);

        if (foreachHits.Count == 0 && !inHit.Success) return null;

        var corrected = line;
        if (inHit.Success) corrected = corrected[..inHit.Groups["in"].Index] + ":" + corrected[(inHit.Groups["in"].Index + 2)..];
        corrected = CCode.ReplaceEach(corrected, foreachHits, _ => "for");

        return LocalFix.ReplaceLine(
            Id, "Write the loop as for (type item : items)",
            "Java's loop over every element is `for (Type item : items)` - the keyword is `for`, not `foreach`, and a colon stands where other languages write `in`.",
            source.Path, number, corrected);
    }
}

/// <summary>Words from other languages javac cannot find: <c>True</c>, <c>None</c>, <c>bool</c>, <c>print</c>,
/// <c>Console.WriteLine</c>.</summary>
public sealed partial class JavaForeignWord : ILocalFixRule
{
    public string Id => "java-foreign-word";

    private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
    {
        ["True"] = "true", ["False"] = "false", ["None"] = "null", ["NULL"] = "null", ["nil"] = "null", ["Null"] = "null",
    };

    private static readonly Dictionary<string, string> Types = new(StringComparer.Ordinal)
    {
        ["bool"] = "boolean", ["Bool"] = "boolean", ["str"] = "String", ["Int"] = "int",
    };

    [GeneratedRegex(@"(?<![\w$.])Console\s*\.\s*(?<method>WriteLine|Write)\s*\(")]
    private static partial Regex ConsoleCall();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at) return null;
        if (JavaCode.CannotFindSymbol().Match(context.Error.Message ?? "") is not { Success: true } symbol) return null;

        var (source, number, line) = at;
        var kind = symbol.Groups["kind"].Value;
        var name = symbol.Groups["name"].Value;
        var masked = CodeText.Mask(line, Syntax.CLike);

        if (kind == "variable" && Values.TryGetValue(name, out var value))
            return Words(source, number, line, masked, name, value, $"Java writes `{name}` as `{value}`, in lower case.");

        if (kind == "class" && Types.TryGetValue(name, out var type))
            return Words(source, number, line, masked, name, type, $"`{name}` is not a Java type. Java calls it `{type}`.");

        if (kind == "variable" && name == "Console" && ConsoleCall().Matches(masked) is { Count: 1 } calls)
        {
            var call = calls[0];
            var replacement = call.Groups["method"].Value == "WriteLine" ? "System.out.println(" : "System.out.print(";

            return LocalFix.ReplaceLine(
                Id, $"Print with {replacement.TrimEnd('(')}",
                "`Console.WriteLine` is C#. Java prints to the console with `System.out.println`.",
                source.Path, number, line[..call.Index] + replacement + line[(call.Index + call.Length)..]);
        }

        if (kind == "method" && name is "print" or "println" or "printf" or "puts")
        {
            var prints = Regex.Matches(masked, $@"(?<![\w$.]){name}\s*\(");
            if (prints.Count != 1) return null;

            if (CodeText.MaskAll(source.Lines, Syntax.CLike).Any(text => Regex.IsMatch(text, $@"\b(?:void|static|public|private|protected)\b[^;=]*\b{name}\s*\(")))
                return null;

            var target = name == "printf" ? "System.out.printf" : "System.out.println";

            return LocalFix.ReplaceLine(
                Id, $"Print with {target}",
                $"`{name}(...)` on its own is not Java - printing goes through `System.out`.",
                source.Path, number, line[..prints[0].Index] + target + line[(prints[0].Index + name.Length)..]);
        }

        return null;
    }

    private LocalFix? Words(SourceFile source, int number, string line, string masked, string name, string right, string explanation)
    {
        var hits = Regex.Matches(masked, $@"(?<![\w$.]){Regex.Escape(name)}(?![\w$])");
        if (hits.Count == 0) return null;

        return LocalFix.ReplaceLine(Id, $"Write {name} as {right}", explanation, source.Path, number, CCode.ReplaceEach(line, hits, _ => right));
    }
}

/// <summary><c>illegal character: '\u201C'</c> in javac.</summary>
public sealed class JavaSmartQuotes : ILocalFixRule
{
    public string Id => "java-smart-quotes";

    public LocalFix? Propose(LocalFixContext context) =>
        JavaCode.IsCompileError(context.Error) && (context.Error.Message ?? "").StartsWith("illegal character", StringComparison.Ordinal) &&
        context.Frame is { Line: { } number } frame && context.Read(frame.File) is { } source
            ? Guards.StraightenFix(Id, source, number)
            : null;
}

/// <summary><c>modifier private not allowed here</c>.</summary>
public sealed partial class JavaIllegalModifier : ILocalFixRule
{
    public string Id => "java-illegal-modifier";

    [GeneratedRegex(@"^modifier (?<modifiers>[\w,]+) not allowed here$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var (source, number, line) = at;
        var modifiers = message.Groups["modifiers"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var masked = CodeText.Mask(line, Syntax.CLike);

        var hits = modifiers.Select(m => Regex.Match(masked, $@"(?<![\w$]){Regex.Escape(m)}\s+")).ToList();
        if (hits.Any(h => !h.Success)) return null;

        var corrected = CCode.ReplaceEach(line, hits, _ => "");
        var named = string.Join(" and ", modifiers.Select(m => $"`{m}`"));

        return LocalFix.ReplaceLine(
            Id, $"Remove {string.Join(" and ", modifiers)}",
            $"Java does not allow {named} on this declaration. A top-level class is either `public` or has no modifier - `private`, " +
            "`protected` and `static` only mean something for a member inside another class.",
            source.Path, number, corrected);
    }
}

/// <summary><c>integer number too large</c> - a long literal without its <c>L</c>.</summary>
public sealed partial class JavaLongLiteral : ILocalFixRule
{
    public string Id => "java-long-literal";

    [GeneratedRegex(@"(?<![\w.$])(?<digits>\d[\d_]*)(?![\w.$])")]
    private static partial Regex Digits();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!(context.Error.Message ?? "").StartsWith("integer number too large", StringComparison.Ordinal) || JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var hits = Digits().Matches(CodeText.Mask(line, Syntax.CLike))
            .Where(m => long.TryParse(m.Groups["digits"].Value.Replace("_", ""), out var value) && value > int.MaxValue)
            .ToList();

        if (hits is not [var hit]) return null;

        var end = hit.Index + hit.Length;

        return LocalFix.ReplaceLine(
            Id, $"Write it as a long: {hit.Value}L",
            "A whole number written in Java is an `int`, and an int stops at 2,147,483,647. An `L` on the end makes it a `long`.",
            source.Path, number, line[..end] + "L" + line[end..]);
    }
}

/// <summary><c>constructor Dog in class Dog cannot be applied to given types</c> - because the "constructor" says <c>void</c>.</summary>
public sealed partial class JavaConstructorReturnType : ILocalFixRule
{
    public string Id => "java-constructor-return-type";

    [GeneratedRegex(@"^constructor (?<cls>[\w$]+) in class (?<owner>[\w$]+) cannot be applied to given types;?$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (message.Groups["cls"].Value != message.Groups["owner"].Value) return null;

        var source = at.Source;
        var cls = Regex.Escape(message.Groups["cls"].Value);
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var methods = Enumerable.Range(0, masked.Count)
            .Select(i => (Index: i, Match: Regex.Match(masked[i], $@"^(?<lead>\s*(?:(?:public|protected|private)\s+)?)(?<void>void\s+){cls}\s*\(")))
            .Where(x => x.Match.Success)
            .ToList();

        if (methods is not [var (index, match)]) return null;

        var original = source.Lines[index];
        var remove = match.Groups["void"];

        return LocalFix.ReplaceLine(
            Id, $"Remove void from the {message.Groups["cls"].Value} constructor",
            $"A constructor has no return type. With `void` in front, `{message.Groups["cls"].Value}(...)` is an ordinary method that happens to " +
            "share the class's name - so there is no constructor that takes these arguments.",
            source.Path, index + 1, original[..remove.Index] + original[(remove.Index + remove.Length)..]);
    }
}

/// <summary><c>class Main is public, should be declared in a file named Main.java</c></summary>
public sealed partial class JavaPublicClassName : ILocalFixRule
{
    public string Id => "java-public-class-name";

    [GeneratedRegex(@"^class (?<name>[A-Za-z_$][\w$]*) is public, should be declared in a file named \k<name>\.java$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^[A-Za-z_$][\w$]*$")]
    private static partial Regex Identifier();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (!JavaCode.IsCompileError(error)) return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var name = message.Groups["name"].Value;
        var fileName = Path.GetFileNameWithoutExtension(source.Path);

        if (!Identifier().IsMatch(fileName) || JavaTypes.Keywords.Contains(fileName)) return null;

        var mentions = CodeText.MaskAll(source.Lines, Syntax.CLike)
            .Sum(text => Regex.Matches(text, $@"(?<![\w$]){Regex.Escape(name)}(?![\w$])").Count);

        if (mentions != 1 || source.Line(number) is not { } line) return null;
        if (CodeText.ReplaceWord(line, name, fileName, Syntax.CLike) is not { } corrected) return null;

        return LocalFix.ReplaceLine(
            Id,
            $"Rename class {name} to {fileName}",
            $"A public class has to live in a file with its own name. The file is {fileName}.java, so the class " +
            $"becomes {fileName}; renaming the file to {name}.java would work just as well.",
            source.Path, number, corrected);
    }
}

/// <summary><c>Main method not found in class App</c> - <c>main(String args)</c>, or a <c>main</c> that is not static.</summary>
public sealed partial class JavaMainSignature : ILocalFixRule
{
    public string Id => "java-main-signature";

    [GeneratedRegex(@"Error: Main method (?<problem>not found|is not static) in class (?<class>[\w$.]+)")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<head>\s*public\s+(?<static>static\s+)?void\s+main\s*\(\s*)(?<parameter>(?:final\s+)?String\s*(?:\[\s*\]|\.\.\.)?\s*[A-Za-z_]\w*(?:\s*\[\s*\])?)(?<tail>\s*\).*)$")]
    private static partial Regex Main();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Message().Match(context.Error.RawText) is not { Success: true } message) return null;

        var name = message.Groups["class"].Value.Split('.')[^1];
        if (context.Read(name + ".java") is not { } source) return null;

        var mains = Enumerable.Range(0, source.Count).Where(i => Main().IsMatch(source.Lines[i])).ToList();
        if (mains is not [var index]) return null;

        var match = Main().Match(source.Lines[index]);
        var parameter = match.Groups["parameter"].Value;
        var isArray = Regex.IsMatch(parameter, @"\[\s*\]|\.\.\.");
        var isStatic = match.Groups["static"].Success;

        if (isArray && isStatic) return null;

        var argument = Regex.Match(parameter, @"(?<name>[A-Za-z_]\w*)(?:\s*\[\s*\])?$").Groups["name"].Value;
        var head = match.Groups["head"].Value;
        if (!isStatic) head = head.Replace("public ", "public static ", StringComparison.Ordinal);

        return LocalFix.ReplaceLine(
            Id, "Declare main as Java looks for it: public static void main(String[] args)",
            "Java starts a program at a method called main that takes the command-line arguments as an array, `String[] args` - and, " +
            "before Java 25, only at one that is also `static`, so it can be called before any object exists; from Java 25 a main that " +
            "takes nothing at all will do too. A main of any other shape compiles, because it is a legal method, and then is not " +
            "found when the program is run. `public static void main(String[] args)` is found by every Java.",
            source.Path, index + 1, head + $"String[] {argument}" + match.Groups["tail"].Value);
    }
}

/// <summary><c>call to super must be first statement in constructor</c>.</summary>
public sealed partial class JavaSuperFirst : ILocalFixRule
{
    public string Id => "java-super-first";

    [GeneratedRegex(@"^call to (?<call>super|this) must be first statement in constructor$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var call = number - 1;
        var keyword = message.Groups["call"].Value;

        if (!Regex.IsMatch(masked[call], $@"^\s*{keyword}\s*\(.*\)\s*;\s*$")) return null;

        var open = -1;
        var depth = 0;

        for (var i = call - 1; i >= 0 && open < 0; i--)
        {
            for (var c = masked[i].Length - 1; c >= 0; c--)
            {
                if (masked[i][c] == '}') depth++;
                else if (masked[i][c] == '{' && depth-- == 0)
                {
                    open = i;
                    break;
                }
            }
        }

        if (open < 0 || masked[open][(masked[open].LastIndexOf('{') + 1)..].Trim().Length > 0) return null;

        var first = open + 1;
        if (first >= call) return null;

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Move {keyword}(...) to the top of the constructor",
            Explanation = $"A constructor has to call `{keyword}(...)` before anything else - the object is not built until it has. " +
                          "The statements that came before it now come straight after.",
            File = source.Path,
            StartLine = first + 1,
            RemoveCount = call - first + 1,
            NewLines = [source.Lines[call], .. source.Lines.Skip(first).Take(call - first)],
        };
    }
}

/// <summary><c>exception NumberFormatException has already been caught</c> - catch clauses in the wrong order.</summary>
public sealed partial class JavaCatchOrder : ILocalFixRule
{
    public string Id => "java-catch-order";

    [GeneratedRegex(@"^exception (?<type>[\w$.]+) has already been caught$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (BraceRules.SwapCatch(at.Source.Lines, masked, at.Number - 1) is not { } swap) return null;

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Catch {message.Groups["type"].Value} first",
            Explanation = BraceRules.CatchOrderExplanation,
            File = at.Source.Path,
            StartLine = swap.Start + 1,
            RemoveCount = swap.Count,
            NewLines = swap.Lines,
        };
    }
}

/// <summary><c>variable x is already defined in method main</c> - a second declaration where an assignment was meant.</summary>
public sealed partial class JavaRedefinition : ILocalFixRule
{
    public string Id => "java-redefinition";

    [GeneratedRegex(@"^variable (?<name>[\w$]+) is already defined in (?:method|constructor) ")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var (source, number, line) = at;
        var name = Regex.Escape(message.Groups["name"].Value);
        var redeclared = Regex.Match(line, $@"^(?<lead>\s*)(?<type>(?!return\b)[\w$<>\[\],.? ]+?)\s+{name}\s*=\s*(?<value>.+?)\s*;(?<tail>\s*(?://.*)?)$");
        if (!redeclared.Success || redeclared.Groups["type"].Value.Contains("final")) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var (first, _) = JavaCode.EnclosingMethod(masked, number - 1);
        var type = Regex.Replace(Regex.Escape(redeclared.Groups["type"].Value.Trim()), @"(\\ )+", @"\s+");
        var earlier = new Regex($@"(?<![\w$.]){type}\s+{name}\s*[=;,]");

        var original = Enumerable.Range(first, Math.Max(0, number - 1 - first)).LastOrDefault(i => earlier.IsMatch(masked[i]), -1);
        if (original < 0) return null;

        var variable = message.Groups["name"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Assign to {variable} instead of declaring it again",
            $"`{variable}` is already declared on line {original + 1}. Writing the type again declares a second variable with the same name, " +
            "which one method cannot have; without the type, the line gives the existing one a new value.",
            source.Path, number, $"{redeclared.Groups["lead"].Value}{variable} = {redeclared.Groups["value"].Value};{redeclared.Groups["tail"].Value}");
    }
}

/// <summary><c>interface expected here</c> and <c>no interface expected here</c> - <c>extends</c> and <c>implements</c> swapped.</summary>
public sealed partial class JavaExtendsImplements : ILocalFixRule
{
    public string Id => "java-extends-implements";

    private static readonly HashSet<string> JdkInterfaces =
    [
        "Runnable", "Comparable", "Comparator", "Iterable", "Iterator", "Collection", "List", "Set", "Map", "Queue", "Deque",
        "AutoCloseable", "Closeable", "Serializable", "Cloneable", "Callable", "Supplier", "Consumer", "Function", "Predicate", "CharSequence",
    ];

    [GeneratedRegex(@"\bimplements\s+(?<name>[A-Z][\w$]*)(?:<[^>]*>)?\s*(?:\{|$)")]
    private static partial Regex SingleImplements();

    [GeneratedRegex(@"\bextends\s+(?<name>[A-Z][\w$]*)(?:<[^>]*>)?\s*(?:\{|$)")]
    private static partial Regex SingleExtends();

    public LocalFix? Propose(LocalFixContext context)
    {
        var message = context.Error.Message;
        if (message is not ("interface expected here" or "no interface expected here") || JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = masked[number - 1];

        if (Regex.IsMatch(code, @"\binterface\s")) return null;

        if (message == "interface expected here")
        {
            if (Regex.IsMatch(code, @"\bextends\b") || SingleImplements().Match(code) is not { Success: true } implements) return null;

            var name = implements.Groups["name"].Value;
            if (!masked.Any(text => Regex.IsMatch(text, $@"\bclass\s+{Regex.Escape(name)}\b"))) return null;

            return LocalFix.ReplaceLine(
                Id, $"extends {name}, not implements",
                $"`{name}` is a class, and a class is inherited from with `extends`. `implements` is only for interfaces.",
                source.Path, number, line[..implements.Index] + "extends" + line[(implements.Index + "implements".Length)..]);
        }

        if (Regex.IsMatch(code, @"\bimplements\b") || SingleExtends().Match(code) is not { Success: true } extends) return null;

        var interfaceName = extends.Groups["name"].Value;
        var isInterface = JdkInterfaces.Contains(interfaceName) || masked.Any(text => Regex.IsMatch(text, $@"\binterface\s+{Regex.Escape(interfaceName)}\b"));
        if (!isInterface) return null;

        return LocalFix.ReplaceLine(
            Id, $"implements {interfaceName}, not extends",
            $"`{interfaceName}` is an interface, and a class takes on an interface with `implements`. `extends` is for inheriting from a class.",
            source.Path, number, line[..extends.Index] + "implements" + line[(extends.Index + "extends".Length)..]);
    }
}

/// <summary><c>if (x = 5)</c> - an assignment where a comparison was meant.</summary>
public sealed partial class JavaAssignmentInCondition : ILocalFixRule
{
    public string Id => "java-assignment-in-condition";

    [GeneratedRegex(@"^incompatible types: (?<from>[\w.$<>\[\]]+) cannot be converted to boolean$")]
    private static partial Regex Message();

    [GeneratedRegex(@"\b(?:if|while)\s*(?<open>\()")]
    private static partial Regex Condition();

    [GeneratedRegex(@"(?<![=!<>+\-*/%&|^])=(?!=)")]
    private static partial Regex Assignment();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true }) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);

        if (Condition().Matches(masked) is not { Count: 1 } conditions) return null;

        var open = conditions[0].Groups["open"].Index;
        if (Brackets.ClosingParenthesis(masked, open) is not { } close) return null;

        var assignments = Assignment().Matches(masked[(open + 1)..close]);
        if (assignments.Count != 1) return null;

        var at_ = open + 1 + assignments[0].Index;

        return LocalFix.ReplaceLine(
            Id, "Compare with == instead of assigning with =",
            "A single `=` assigns a value. A condition compares, and comparing is `==`.",
            source.Path, number, line[..at_] + "==" + line[(at_ + 1)..]);
    }
}

/// <summary><c>for (i = 0; ...)</c> with <c>i</c> never declared.</summary>
public sealed partial class JavaForCounter : ILocalFixRule
{
    public string Id => "java-for-counter";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at) return null;
        if (JavaCode.CannotFindSymbol().Match(context.Error.Message ?? "") is not { Success: true } symbol || symbol.Groups["kind"].Value != "variable") return null;

        var (source, number, _) = at;
        var name = symbol.Groups["name"].Value;
        var escaped = Regex.Escape(name);
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var header = new Regex($@"\bfor\s*(?<open>\()\s*(?<name>{escaped})\s*=(?!=)");

        for (var k = number - 1; k >= Math.Max(0, number - 16); k--)
        {
            if (header.Match(masked[k]) is not { Success: true } loop) continue;
            if (Brackets.ClosingParenthesis(masked[k], loop.Groups["open"].Index) is not { } close) return null;
            if (JavaCode.BodyEnd(masked, k, close + 1) is not { } end || number - 1 > end) return null;

            var (first, last) = JavaCode.EnclosingMethod(masked, k);
            var word = new Regex($@"(?<![\w$.]){escaped}(?![\w$])");

            for (var i = first; i <= last; i++)
                if ((i < k || i > end) && word.IsMatch(masked[i])) return null;

            var column = loop.Groups["name"].Index;

            return LocalFix.ReplaceLine(
                Id, $"Declare {name} in the loop",
                $"`{name}` is the loop counter but is never declared. Nothing outside the loop uses it, so declaring it in the loop with `int` is all it needs.",
                source.Path, k + 1, source.Lines[k][..column] + "int " + source.Lines[k][column..]);
        }

        return null;
    }
}
