using System.Text.RegularExpressions;
using FixFinder.Core.Logic;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>SyntaxError: expected ':'</c> on a line that opens a block.</summary>
public sealed partial class PythonExpectedColon : ILocalFixRule
{
    public string Id => "python-expected-colon";

    [GeneratedRegex(@"^\s*(?<keyword>if|elif|else|for|while|def|class|try|except|finally|with|async|match|case)\b")]
    internal static partial Regex BlockOpener();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "python" || error.ExceptionType != "SyntaxError") return null;
        if (error.Message?.Trim() != "expected ':'") return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line || BlockOpener().Match(line) is not { Success: true } opener) return null;

        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);
        if (code.EndsWith(':')) return null;

        return LocalFix.ReplaceLine(
            Id,
            "Add the missing colon",
            $"A line that opens a block - this one starts with `{opener.Groups["keyword"].Value}` - has to end with a colon, " +
            $"and Python stopped at line {number} because it does not.",
            source.Path, number, code + ":" + tail);
    }
}

/// <summary><c>SyntaxError: Missing parentheses in call to 'print'. Did you mean print(...)?</c></summary>
public sealed partial class PythonPrintStatement : ILocalFixRule
{
    public string Id => "python-print-statement";

    [GeneratedRegex(@"^(?<indent>\s*)print\s+(?<args>(?!>>).+)$")]
    private static partial Regex PrintStatement();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "python" || error.ExceptionType != "SyntaxError") return null;
        if (error.Message?.StartsWith("Missing parentheses in call to 'print'", StringComparison.Ordinal) != true) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);
        if (PrintStatement().Match(code) is not { Success: true } statement) return null;

        var args = statement.Groups["args"].Value.Trim();

        // Python 2's trailing comma meant "no newline", which print() spells end=" ". Rewriting it
        // would be changing what the program prints, so it is left for a person to decide.
        if (args.EndsWith(',')) return null;

        return LocalFix.ReplaceLine(
            Id,
            "Call print with parentheses",
            "This is Python 2's print statement. In Python 3 print is a function, so its arguments go in " +
            "parentheses - which is what Python suggested itself.",
            source.Path, number, $"{statement.Groups["indent"].Value}print({args}){tail}");
    }
}

/// <summary><c>SyntaxError: invalid syntax. Maybe you meant '==' or ':=' instead of '='?</c></summary>
public sealed partial class PythonAssignmentInCondition : ILocalFixRule
{
    public string Id => "python-assignment-in-condition";

    [GeneratedRegex(@"^\s*(?:if|elif|while)\b")]
    private static partial Regex Condition();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "python" || error.ExceptionType != "SyntaxError") return null;
        if (error.Message?.Contains("Maybe you meant '==' or ':=' instead of '='?", StringComparison.Ordinal) != true) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line || !Condition().IsMatch(line)) return null;

        var masked = CodeText.Mask(line, Syntax.Python);
        var brackets = new Stack<int>();
        var hits = new List<int>();

        for (var i = 0; i < masked.Length; i++)
        {
            var c = masked[i];

            if (c is '(' or '[' or '{')
            {
                brackets.Push(i);
                continue;
            }

            if (c is ')' or ']' or '}')
            {
                if (brackets.Count > 0) brackets.Pop();
                continue;
            }

            if (c != '=') continue;

            var previous = i > 0 ? masked[i - 1] : ' ';
            var next = i + 1 < masked.Length ? masked[i + 1] : ' ';

            if ("=!<>:+-*/%&|^@~".Contains(previous) || next == '=') continue;

            // f(key=value) is a keyword argument, not the assignment Python is complaining about.
            if (brackets.TryPeek(out var open) && masked[open] == '(' && open > 0 && CodeText.IsWordChar(masked[open - 1]))
                continue;

            hits.Add(i);
        }

        // One bare = on the line, or it is not clear which one Python meant.
        if (hits.Count != 1) return null;

        return LocalFix.ReplaceLine(
            Id,
            "Compare with == instead of assigning with =",
            "A single = assigns; a condition needs == to compare. Python pointed at this one itself.",
            source.Path, number, line[..hits[0]] + "==" + line[(hits[0] + 1)..]);
    }
}

/// <summary><c>IndentationError: expected an indented block after 'if' statement on line 3</c></summary>
public sealed partial class PythonIndentedBlock : ILocalFixRule
{
    public string Id => "python-indented-block";

    [GeneratedRegex(@"expected an indented block after .+? on line (?<header>\d+)")]
    private static partial Regex Expected();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "python" || error.ExceptionType != "IndentationError") return null;
        if (Expected().Match(error.Message ?? "") is not { Success: true } expected) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var headerNumber = int.Parse(expected.Groups["header"].Value);

        if (headerNumber >= number || source.Line(headerNumber) is not { } header || source.Line(number) is not { } line)
            return null;

        var headerIndent = CodeText.Indentation(header);

        // Already indented further than the header: something else is wrong with this line.
        if (CodeText.Indentation(line).Length > headerIndent.Length) return null;

        return LocalFix.ReplaceLine(
            Id,
            $"Indent line {number} into the block",
            $"Line {headerNumber} opens a block, and the line after it has to be indented to belong to it. " +
            "Only the line Python named is moved; if more of the lines below belong in the block, indent those too.",
            source.Path, number, headerIndent + PythonCode.IndentUnit(source.Lines) + line.TrimStart());
    }
}

/// <summary><c>else if</c>, which Python spells <c>elif</c>.</summary>
public sealed partial class PythonElseIf : ILocalFixRule
{
    public string Id => "python-else-if";

    [GeneratedRegex(@"^(?<indent>\s*)else\s*if\b")]
    private static partial Regex ElseIf();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "SyntaxError" }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line || ElseIf().Match(line) is not { Success: true } match) return null;

        return LocalFix.ReplaceLine(
            Id, "Write else if as elif",
            "Python has no `else if`. The word for \"otherwise, if\" is `elif`.",
            source.Path, number, match.Groups["indent"].Value + "elif" + line[match.Length..]);
    }
}

/// <summary><c>else age &lt; 18:</c> - a condition on an else, which is what elif is for.</summary>
public sealed partial class PythonElseWithCondition : ILocalFixRule
{
    public string Id => "python-else-with-condition";

    [GeneratedRegex(@"^(?<indent>\s*)else\s+(?!if\b)(?<condition>[^:]+?)\s*:?\s*$")]
    private static partial Regex ElseCondition();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "SyntaxError" }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);
        if (ElseCondition().Match(code) is not { Success: true } match) return null;

        var condition = match.Groups["condition"].Value;

        return LocalFix.ReplaceLine(
            Id, "Use elif for an else with a condition",
            $"`else` never takes a condition - it runs when nothing above it did. A branch that runs when `{condition}` " +
            "is `elif`.",
            source.Path, number, $"{match.Groups["indent"].Value}elif {condition}:{tail}");
    }
}

/// <summary><c>=&gt;</c>, <c>=&lt;</c> and <c>&lt;&gt;</c>, which Python writes <c>&gt;=</c>, <c>&lt;=</c> and <c>!=</c>.</summary>
public sealed partial class PythonArrowOperator : ILocalFixRule
{
    public string Id => "python-comparison-operator";

    [GeneratedRegex(@"(?<![=<>!])(?<op>=>|=<|<>)(?![=<>])")]
    private static partial Regex Operator();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "SyntaxError" }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var hits = Operator().Matches(CodeText.Mask(line, Syntax.Python));
        if (hits.Count != 1) return null;

        var op = hits[0].Groups["op"];
        var right = op.Value switch { "=>" => ">=", "=<" => "<=", _ => "!=" };

        return LocalFix.ReplaceLine(
            Id, $"Write {op.Value} as {right}",
            $"Python spells this comparison `{right}`. The equals sign always comes second.",
            source.Path, number, line[..op.Index] + right + line[(op.Index + op.Length)..]);
    }
}

/// <summary>A <c>//</c> comment, from JavaScript, C or Java, where Python uses <c>#</c>.</summary>
public sealed partial class PythonSlashComment : ILocalFixRule
{
    public string Id => "python-slash-comment";

    [GeneratedRegex(@"^(?<indent>\s*)//\s?")]
    private static partial Regex Slashes();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "SyntaxError" }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line || Slashes().Match(line) is not { Success: true } match) return null;

        return LocalFix.ReplaceLine(
            Id, "Start the comment with #",
            "In Python a comment starts with `#`. `//` is whole-number division, so Python tried to read the comment as code.",
            source.Path, number, match.Groups["indent"].Value + "# " + line[match.Length..]);
    }
}

/// <summary><c>lambda x: return x * x</c> - a lambda is already the value it returns.</summary>
public sealed partial class PythonLambdaReturn : ILocalFixRule
{
    public string Id => "python-lambda-return";

    [GeneratedRegex(@"\blambda\b[^:]*:\s*(?<return>return\s+)")]
    private static partial Regex LambdaReturn();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "SyntaxError" }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var hits = LambdaReturn().Matches(CodeText.Mask(line, Syntax.Python));
        if (hits.Count != 1) return null;

        var keyword = hits[0].Groups["return"];

        return LocalFix.ReplaceLine(
            Id, "Take return out of the lambda",
            "A lambda is a single expression, and its value is what it returns - so it never contains `return`.",
            source.Path, number, line[..keyword.Index] + line[(keyword.Index + keyword.Length)..]);
    }
}

/// <summary><c>'[' was never closed</c>, on a line that plainly ends where the bracket should.</summary>
/// <remarks>
/// Only when the line is complete apart from the bracket. A list spread over several lines that is
/// missing its closer at the bottom would take a <c>]</c> on its first line and leave the rest as
/// stray expressions that still parse - so a following line indented further, or a line ending in
/// a comma or an operator, is a refusal.
/// </remarks>
public sealed partial class PythonUnclosedBracket : ILocalFixRule
{
    public string Id => "python-unclosed-bracket";

    [GeneratedRegex(@"^'(?<open>[\[({])' was never closed$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "SyntaxError" } error) return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var open = message.Groups["open"].Value[0];
        var close = open switch { '[' => ']', '(' => ')', _ => '}' };

        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);
        var masked = CodeText.Mask(code, Syntax.Python);

        if (masked.Count(c => c == open) - masked.Count(c => c == close) != 1) return null;
        if (code.Length == 0 || ",+-*/%=([{\\".Contains(code[^1]) || code.TrimEnd().EndsWith(open)) return null;

        for (var k = number; k < source.Count; k++)
        {
            if (source.Lines[k].Trim().Length == 0) continue;
            if (CodeText.Indentation(source.Lines[k]).Length > CodeText.Indentation(line).Length) return null;
            break;
        }

        var corrected = code.EndsWith(':') ? code[..^1] + close + ":" : code + close;

        return LocalFix.ReplaceLine(
            Id, $"Close the {open} on line {number}",
            $"The `{open}` on line {number} is never closed. The line is otherwise complete, so the `{close}` goes at its end.",
            source.Path, number, corrected + tail);
    }
}

/// <summary>
/// Syntax carried over from JavaScript, Java or C: <c>&amp;&amp;</c>, <c>||</c>, <c>!</c>,
/// <c>x++</c>, <c>let</c>/<c>var</c>/<c>const</c>, <c>new</c> and <c>catch</c>.
/// </summary>
/// <remarks>
/// Every one of these is a plain token swap with one Python spelling, and they often come together
/// on one line - <c>if (x &gt; 1 &amp;&amp; !done)</c> - so everything recognised on the line is
/// rewritten at once. Inside strings and comments nothing is touched.
/// </remarks>
public sealed class PythonForeignSyntax : ILocalFixRule
{
    public string Id => "python-foreign-syntax";

    private sealed record Change(Regex Pattern, Func<Match, string> Replacement, string Explanation);

    private static readonly Change[] Changes =
    [
        new(new Regex(@"\s*&&\s*"), _ => " and ", "`&&` is `and`"),
        new(new Regex(@"\s*\|\|\s*"), _ => " or ", "`||` is `or`"),
        new(new Regex(@"(?<![!=<>])!(?!=)\s*(?=[A-Za-z_(])"), _ => "not ", "`!` is `not`"),
        new(new Regex(@"^(?<indent>\s*)(?<target>[A-Za-z_][\w.]*(?:\[[^\]]*\])?)\s*\+\+\s*$"),
            m => $"{m.Groups["indent"].Value}{m.Groups["target"].Value} += 1", "there is no `++` - `+= 1` adds one"),
        new(new Regex(@"^(?<indent>\s*)(?<target>[A-Za-z_][\w.]*(?:\[[^\]]*\])?)\s*--\s*$"),
            m => $"{m.Groups["indent"].Value}{m.Groups["target"].Value} -= 1", "there is no `--` - `-= 1` takes one away"),
        new(new Regex(@"^(?<indent>\s*)(?:let|var|const)\s+(?=[A-Za-z_])"),
            m => m.Groups["indent"].Value, "a variable needs no `let`, `var` or `const` - assigning to it is enough"),
        new(new Regex(@"(?<![\w.])new\s+(?=[A-Za-z_][\w.]*\s*\()"), _ => "", "objects are made by calling the class, without `new`"),
        new(new Regex(@"^(?<indent>\s*)catch\b"), m => m.Groups["indent"].Value + "except", "the handler after `try` is `except`, not `catch`"),
    ];

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "SyntaxError") || PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);
        var applied = new List<string>();

        foreach (var change in Changes)
        {
            var hits = change.Pattern.Matches(CodeText.Mask(code, Syntax.Python)).Reverse().ToList();
            if (hits.Count == 0) continue;

            foreach (var hit in hits) code = code[..hit.Index] + change.Replacement(hit) + code[(hit.Index + hit.Length)..];

            applied.Add(change.Explanation);
        }

        if (applied.Count == 0) return null;

        return LocalFix.ReplaceLine(
            Id, "Write this line the way Python writes it",
            $"This is how another language says it. In Python, {string.Join("; ", applied)}.",
            source.Path, number, code + tail);
    }
}

/// <summary><c>except ValueError, e:</c> - Python 2's way of naming the exception.</summary>
public sealed partial class PythonExceptComma : ILocalFixRule
{
    public string Id => "python-except-comma";

    [GeneratedRegex(@"^(?<head>\s*except\s+)(?<type>[\w.]+)\s*,\s*(?<name>[A-Za-z_]\w*)\s*:(?<rest>.*)$")]
    private static partial Regex ExceptComma();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "SyntaxError") || context.Error.Message != "multiple exception types must be parenthesized") return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (ExceptComma().Match(line) is not { Success: true } match) return null;

        var type = match.Groups["type"].Value;
        var name = match.Groups["name"].Value;

        // `except ValueError, TypeError:` meant two types; `except ValueError, e:` meant a name.
        var isType = char.IsUpper(name[0]) || name.EndsWith("Error", StringComparison.Ordinal) || name.EndsWith("Exception", StringComparison.Ordinal);

        var corrected = isType
            ? $"{match.Groups["head"].Value}({type}, {name}):{match.Groups["rest"].Value}"
            : $"{match.Groups["head"].Value}{type} as {name}:{match.Groups["rest"].Value}";

        return LocalFix.ReplaceLine(
            Id, isType ? "Put the exception types in brackets" : $"Name the exception with as {name}",
            isType
                ? "Catching more than one exception type needs them in brackets, as a tuple."
                : $"`except {type}, {name}:` is Python 2. Python 3 names the caught exception with `as`.",
            source.Path, number, corrected);
    }
}

/// <summary><c>raise "something went wrong"</c> - only an exception can be raised, not a string.</summary>
public sealed partial class PythonRaiseString : ILocalFixRule
{
    public string Id => "python-raise-string";

    [GeneratedRegex(@"^(?<indent>\s*)raise\s+(?<value>[rRbBuUfF]{0,2}(?<q>[""']).*\k<q>)$")]
    private static partial Regex RaiseString();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || context.Error.Message != "exceptions must derive from BaseException") return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);

        if (RaiseString().Match(code) is not { Success: true } match) return null;

        return LocalFix.ReplaceLine(
            Id, "Raise an Exception carrying the message",
            "`raise` needs an exception object. A string on its own was allowed in very old Python; now the message goes inside one.",
            source.Path, number, $"{match.Groups["indent"].Value}raise Exception({match.Groups["value"].Value}){tail}");
    }
}

/// <summary><c>print &gt;&gt;sys.stderr, "oops"</c> - Python 2's way of printing somewhere else.</summary>
public sealed partial class PythonPrintRedirect : ILocalFixRule
{
    public string Id => "python-print-redirect";

    [GeneratedRegex(@"^(?<indent>\s*)print\s*>>\s*(?<stream>[\w.]+)\s*,\s*(?<args>.+?)$")]
    private static partial Regex Redirect();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") ||
            context.Error.Message?.StartsWith("unsupported operand type(s) for >>: 'builtin_function_or_method'", StringComparison.Ordinal) != true)
            return null;

        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);

        if (Redirect().Match(code) is not { Success: true } match) return null;

        return LocalFix.ReplaceLine(
            Id, "Print to the stream with file=",
            "`print >>stream, text` is Python 2. In Python 3 print is a function, and where it writes is its `file` argument.",
            source.Path, number,
            $"{match.Groups["indent"].Value}print({match.Groups["args"].Value}, file={match.Groups["stream"].Value}){tail}");
    }
}

/// <summary><c>def greet:</c> - a function needs its brackets even when it takes nothing.</summary>
public sealed partial class PythonDefWithoutParentheses : ILocalFixRule
{
    public string Id => "python-def-without-parentheses";

    [GeneratedRegex(@"^(?<head>\s*(?:async\s+)?def\s+[A-Za-z_]\w*)\s*:(?<rest>.*)$")]
    private static partial Regex Def();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "SyntaxError") || context.Error.Message != "expected '('") return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Def().Match(line) is not { Success: true } match) return null;

        return LocalFix.ReplaceLine(
            Id, "Add the brackets after the function name",
            "A function definition always has brackets for its parameters, even when there are none.",
            source.Path, number, $"{match.Groups["head"].Value}():{match.Groups["rest"].Value}");
    }
}

/// <summary><c>f"Hello {name"</c> - an f-string with a brace that never closes.</summary>
public sealed partial class PythonFStringBrace : ILocalFixRule
{
    public string Id => "python-fstring-brace";

    [GeneratedRegex(@"(?<![\w])(?:[fF][rR]?|[rR][fF])(?<q>[""'])(?<body>[^""']*)\k<q>")]
    private static partial Regex FString();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "SyntaxError") || context.Error.Message != "f-string: expecting '}'") return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;

        var unclosed = FString().Matches(line).Where(m =>
        {
            var body = m.Groups["body"].Value.Replace("{{", "").Replace("}}", "");
            return body.Count(c => c == '{') == body.Count(c => c == '}') + 1 && body.LastIndexOf('{') > body.LastIndexOf('}');
        }).ToList();

        if (unclosed.Count != 1) return null;

        var body = unclosed[0].Groups["body"];
        var end = body.Index + body.Length;

        return LocalFix.ReplaceLine(
            Id, "Close the brace in the f-string",
            "Each `{` in an f-string opens a value to put in, and needs a `}` to end it.",
            source.Path, number, line[..end] + "}" + line[end..]);
    }
}

/// <summary><c>unterminated string literal</c> - a missing closing quote, or an apostrophe that ended the string early.</summary>
public sealed class PythonUnterminatedString : ILocalFixRule
{
    public string Id => "python-unterminated-string";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "SyntaxError") || context.Error.Message?.StartsWith("unterminated string literal", StringComparison.Ordinal) != true) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var singles = line.Count(c => c == '\'');
        var doubles = line.Count(c => c == '"');

        // 'It's here' - the apostrophe ended the string. Double quotes around it let the apostrophe stay.
        if (singles == 3 && doubles == 0)
        {
            var first = line.IndexOf('\'');
            var last = line.LastIndexOf('\'');

            return LocalFix.ReplaceLine(
                Id, "Put the text in double quotes",
                "The apostrophe inside the text was read as the end of the string. Wrapping the text in double quotes lets it contain one.",
                source.Path, number, line[..first] + "\"" + line[(first + 1)..last] + "\"" + line[(last + 1)..]);
        }

        char quote;
        if (doubles == 1 && singles % 2 == 0) quote = '"';
        else if (singles == 1 && doubles % 2 == 0) quote = '\'';
        else return null;

        var at_ = line.IndexOf(quote);
        var before = line[..at_];
        var after = line[(at_ + 1)..].TrimEnd();
        var trailing = line[(at_ + 1 + after.Length)..];

        var maskedBefore = CodeText.Mask(before, Syntax.Python);
        var open = maskedBefore.Count(c => c is '(' or '[' or '{') - maskedBefore.Count(c => c is ')' or ']' or '}');

        var closers = 0;
        while (open > 0 && after.Length - closers > 0 && after[after.Length - 1 - closers] is ')' or ']' or '}')
        {
            closers++;
            open--;
        }

        var content = after[..^closers];
        if (content.Length == 0) return null;

        return LocalFix.ReplaceLine(
            Id, "Close the string",
            "The text starts with a quote that never ends. The closing quote goes after the text, before the brackets that close around it.",
            source.Path, number, before + quote + content + quote + after[^closers..] + trailing);
    }
}

/// <summary><c>unmatched ')'</c> - one closing bracket too many, at the end of the line.</summary>
public sealed partial class PythonUnmatchedClosing : ILocalFixRule
{
    public string Id => "python-unmatched-closing";

    [GeneratedRegex(@"^unmatched '(?<close>[)\]}])'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "SyntaxError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var close = message.Groups["close"].Value[0];
        var open = close switch { ')' => '(', ']' => '[', _ => '{' };

        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);
        var masked = CodeText.Mask(code, Syntax.Python);

        if (!code.EndsWith(close) || masked.Count(c => c == close) - masked.Count(c => c == open) != 1) return null;

        return LocalFix.ReplaceLine(
            Id, $"Remove the extra {close}",
            $"There is one more `{close}` than `{open}` on this line, and the last one closes nothing.",
            source.Path, number, code[..^1] + tail);
    }
}

/// <summary><c>unexpected indent</c> - a line, or a run of lines, indented further than anything opened.</summary>
public sealed class PythonUnexpectedIndent : ILocalFixRule
{
    public string Id => "python-unexpected-indent";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "IndentationError") || context.Error.Message != "unexpected indent") return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var previous = number - 2;
        while (previous >= 0 && source.Lines[previous].Trim().Length == 0) previous--;

        var target = previous >= 0 ? CodeText.Indentation(source.Lines[previous]) : "";

        if (previous >= 0 && CodeText.SplitComment(source.Lines[previous], Syntax.Python).Code.EndsWith(':')) return null;

        var current = CodeText.Indentation(line);
        if (!current.StartsWith(target, StringComparison.Ordinal) || current.Length <= target.Length) return null;

        var extra = current.Length - target.Length;
        var end = number - 1;

        while (end + 1 < source.Count)
        {
            var next = source.Lines[end + 1];
            if (next.Trim().Length > 0 && !CodeText.Indentation(next).StartsWith(current, StringComparison.Ordinal)) break;
            end++;
        }

        while (end > number - 1 && source.Lines[end].Trim().Length == 0) end--;

        var lines = source.Lines.Skip(number - 1).Take(end - number + 2)
            .Select(text => text.Trim().Length == 0 ? text : text[extra..])
            .ToList();

        return new LocalFix
        {
            RuleId = Id,
            Title = lines.Count == 1 ? $"Line up line {number} with the line before" : $"Line up lines {number}-{end + 1} with the line before",
            Explanation = "Indentation means \"inside the block the line above opened\", and the line above opens no block, so these lines go back level with it.",
            File = source.Path,
            StartLine = number,
            RemoveCount = lines.Count,
            NewLines = lines,
        };
    }
}

/// <summary><c>unindent does not match any outer indentation level</c> - a line dedented to a level nothing is at.</summary>
public sealed class PythonUnindentMismatch : ILocalFixRule
{
    public string Id => "python-unindent-mismatch";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "IndentationError") || context.Error.Message != "unindent does not match any outer indentation level") return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var width = CodeText.Indentation(line).Length;

        string? outer = null;

        for (var k = number - 2; k >= 0; k--)
        {
            var text = source.Lines[k];
            if (text.Trim().Length == 0) continue;

            var indentation = CodeText.Indentation(text);
            if (indentation.Length < width && (outer is null || indentation.Length > outer.Length)) outer = indentation;
        }

        if (outer is null) return null;

        var end = number - 1;
        while (end + 1 < source.Count && source.Lines[end + 1].Trim().Length > 0 && CodeText.Indentation(source.Lines[end + 1]).Length == width) end++;

        var lines = source.Lines.Skip(number - 1).Take(end - number + 2).Select(text => outer + text.TrimStart()).ToList();

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Line up line {number} with the block it belongs to",
            Explanation = $"Line {number} is indented {width} spaces, which matches no block above it. The nearest level it could be closing back to is {outer.Length}.",
            File = source.Path,
            StartLine = number,
            RemoveCount = lines.Count,
            NewLines = lines,
        };
    }
}

/// <summary><c>inconsistent use of tabs and spaces</c> - tabs turned into the spaces the rest of the file uses.</summary>
public sealed class PythonTabsAndSpaces : ILocalFixRule
{
    public string Id => "python-tabs-and-spaces";

    private const int MaxSpan = 80;

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TabError") || context.Read(context.Frame?.File) is not { } source) return null;

        var widths = source.Lines
            .Where(text => text.Trim().Length > 0)
            .Select(CodeText.Indentation)
            .Where(indentation => indentation.Length > 0 && !indentation.Contains('\t'))
            .Select(indentation => indentation.Length)
            .ToList();

        var unit = new string(' ', widths.Count > 0 ? widths.Min() : 4);

        var tabbed = Enumerable.Range(0, source.Count).Where(i => CodeText.Indentation(source.Lines[i]).Contains('\t')).ToList();
        if (tabbed.Count == 0 || tabbed[^1] - tabbed[0] > MaxSpan) return null;

        var lines = Enumerable.Range(tabbed[0], tabbed[^1] - tabbed[0] + 1).Select(i =>
        {
            var text = source.Lines[i];
            var indentation = CodeText.Indentation(text);
            return indentation.Replace("\t", unit) + text[indentation.Length..];
        }).ToList();

        return new LocalFix
        {
            RuleId = Id,
            Title = "Indent with spaces throughout",
            Explanation = $"Some lines are indented with tabs and others with spaces, and Python will not guess how wide a tab is. Each tab becomes {unit.Length} spaces, as the rest of the file indents.",
            File = source.Path,
            StartLine = tabbed[0] + 1,
            RemoveCount = lines.Count,
            NewLines = lines,
        };
    }
}

/// <summary><c>invalid character '\u201C' (U+201C)</c> - curly quotes or dashes pasted into Python.</summary>
public sealed class PythonSmartQuotes : ILocalFixRule
{
    public string Id => "python-smart-quotes";

    public LocalFix? Propose(LocalFixContext context) =>
        PythonCode.Raised(context, "SyntaxError") && (context.Error.Message ?? "").Contains("invalid character", StringComparison.Ordinal) &&
        PythonCode.Locate(context) is { } at
            ? Guards.StraightenFix(Id, at.Source, at.Number)
            : null;
}

/// <summary><c>except ValueError e:</c> - Python names the exception with <c>as</c>.</summary>
public sealed partial class PythonExceptAs : ILocalFixRule
{
    public string Id => "python-except-as";

    [GeneratedRegex(@"^(?<lead>\s*except\s+)(?<types>[A-Za-z_][\w.]*|\([^()]*\))\s+(?<name>[A-Za-z_]\w*)\s*:(?<tail>.*)$")]
    private static partial Regex Clause();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "SyntaxError") || PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Clause().Match(line) is not { Success: true } clause || clause.Groups["name"].Value == "as") return null;

        var name = clause.Groups["name"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Name the exception with as {name}",
            "Python names the exception it caught with `as`: `except ValueError as e:`. With only a space between them, as in Java or C#, it is not Python.",
            source.Path, number, $"{clause.Groups["lead"].Value}{clause.Groups["types"].Value} as {name}:{clause.Groups["tail"].Value}");
    }
}

/// <summary><c>[n for n in xs if n &gt; 0 else 0]</c> - a conditional value goes before the <c>for</c>.</summary>
public sealed partial class PythonComprehensionCondition : ILocalFixRule
{
    public string Id => "python-comprehension-condition";

    [GeneratedRegex(@"^\s*(?<expr>.+?)\s+for\s+(?<target>.+?)\s+in\s+(?<iterable>.+?)\s+if\s+(?<condition>.+?)\s+else\s+(?<alternative>.+?)\s*$")]
    private static partial Regex Misplaced();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "SyntaxError") || PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);

        for (var open = masked.IndexOf('['); open >= 0; open = masked.IndexOf('[', open + 1))
        {
            var close = Brackets.Closing(masked, open);
            if (close < 0) return null;

            var inside = masked[(open + 1)..close];
            if (Misplaced().Match(inside) is not { Success: true } m) continue;

            string Part(string group) => line.Substring(open + 1 + m.Groups[group].Index, m.Groups[group].Length);

            var rewritten = $"{Part("expr")} if {Part("condition")} else {Part("alternative")} for {Part("target")} in {Part("iterable")}";

            return LocalFix.ReplaceLine(
                Id, "Put the if/else before the for",
                "In a comprehension, an `if` after the `for` filters elements out and cannot have an `else`. To pick a value for every element, " +
                "the `a if condition else b` comes first: `[a if condition else b for x in items]`.",
                source.Path, number, line[..(open + 1)] + rewritten + line[close..]);
        }

        return null;
    }
}

/// <summary><c>global count = 0</c> - global declares a name; it cannot assign in the same statement.</summary>
public sealed partial class PythonGlobalAssignment : ILocalFixRule
{
    public string Id => "python-global-assignment";

    [GeneratedRegex(@"^(?<indent>\s*)global\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<value>.+?)$")]
    private static partial Regex GlobalAssign();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "SyntaxError") || PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);

        if (GlobalAssign().Match(code) is not { Success: true } match) return null;

        var indent = match.Groups["indent"].Value;
        var name = match.Groups["name"].Value;
        var assignment = $"{indent}{name} = {match.Groups["value"].Value}{tail}";

        return new LocalFix
        {
            RuleId = Id,
            Title = indent.Length == 0 ? $"Assign {name} without global" : $"Declare {name} global, then assign it",
            Explanation = indent.Length == 0
                ? "At the top level of a file every name is already global, so `global` has nothing to do - and it can never assign."
                : "`global` only declares that the name is the module's; the assignment has to be its own statement.",
            File = source.Path,
            StartLine = number,
            RemoveCount = 1,
            NewLines = indent.Length == 0 ? [assignment] : [$"{indent}global {name}", assignment],
        };
    }
}

/// <summary><c>cannot access local variable 'count'</c> in a function nested inside the one that owns <c>count</c>.</summary>
public sealed partial class PythonNonlocal : ILocalFixRule
{
    public string Id => "python-nonlocal";

    [GeneratedRegex(@"(?:cannot access local variable|local variable) '(?<name>[A-Za-z_]\w*)'")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "UnboundLocalError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Frame is not { Line: { } number, Symbol: { Length: > 0 } symbol } frame || context.Read(frame.File) is not { } source) return null;

        var function = Regex.Escape(symbol[(symbol.LastIndexOf('.') + 1)..]);
        var name = message.Groups["name"].Value;
        var word = Regex.Escape(name);
        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);

        var inner = Enumerable.Range(0, masked.Count).Where(i => Regex.IsMatch(masked[i], $@"^\s+(?:async\s+)?def\s+{function}\s*\(")).ToList();
        if (inner is not [var def]) return null;

        var outer = PythonCode.EnclosingHeader(masked, def, PythonCode.FunctionKeyword());
        if (outer < 0) return null;

        var (first, end) = PythonCode.BlockBody(lines, def);
        if (number - 1 < first || number - 1 >= end) return null;
        if (Enumerable.Range(first, end - first).Any(i => Regex.IsMatch(masked[i], $@"^\s*(?:global|nonlocal)\b.*\b{word}\b"))) return null;

        // The outer function has to own it: an assignment in its body, outside this inner function.
        var (outerFirst, outerEnd) = PythonCode.BlockBody(lines, outer);
        var owned = Enumerable.Range(outerFirst, outerEnd - outerFirst)
            .Where(i => i < def || i >= end)
            .Any(i => Regex.IsMatch(masked[i], $@"^\s+{word}\s*(?::[^=]*)?=(?!=)"));

        if (!owned) return null;

        var at = PythonCode.FirstStatement(lines, first, end);
        if (at >= end) return null;

        var outerName = PythonCode.FunctionHeader().Match(masked[outer]).Groups["name"].Value;
        var innerName = PythonCode.FunctionHeader().Match(masked[def]).Groups["name"].Value;

        return LocalFix.Insert(
            Id, $"Add nonlocal {name}",
            $"`{name}` belongs to `{outerName}`, the function around this one. Assigning to it inside `{innerName}` - `{name} += 1` counts - " +
            $"makes it a new local variable of `{innerName}`, which has no value yet. `nonlocal {name}` says to use `{outerName}`'s.",
            source.Path, at + 1, [CodeText.Indentation(lines[at]) + "nonlocal " + name]);
    }
}

/// <summary><c>'await' outside async function</c> - the function has to be <c>async def</c>.</summary>
public sealed partial class PythonAwaitOutsideAsync : ILocalFixRule
{
    public string Id => "python-await-outside-async";

    [GeneratedRegex(@"^(?<lead>\s*)def\s")]
    private static partial Regex PlainDef();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "SyntaxError") || context.Error.Message?.Trim() != "'await' outside async function") return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);

        var def = PythonCode.EnclosingHeader(masked, number - 1, PythonCode.FunctionKeyword());
        if (def < 0 || PlainDef().Match(source.Lines[def]) is not { Success: true } plain) return null;

        var name = PythonCode.FunctionHeader().Match(masked[def]).Groups["name"].Value;
        var lead = plain.Groups["lead"].Length;

        return LocalFix.ReplaceLine(
            Id, $"Make {name} async",
            $"`await` only works inside an `async def`, so `{name}` has to be one. Whatever calls it then has to `await` it as well, " +
            $"or start it with `asyncio.run({name}())`.",
            source.Path, def + 1, source.Lines[def][..lead] + "async " + source.Lines[def][lead..]);
    }
}

/// <summary><c>import from math sqrt</c>, which Python writes <c>from math import sqrt</c>.</summary>
public sealed partial class PythonImportFromBackwards : ILocalFixRule
{
    public string Id => "python-import-from-backwards";

    [GeneratedRegex(@"^(?<indent>\s*)import\s+from\s+(?<module>[\w.]+)\s+(?<names>[\w\s,*]+?)$")]
    private static partial Regex Backwards();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "SyntaxError") || PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);

        if (Backwards().Match(code) is not { Success: true } match) return null;

        return LocalFix.ReplaceLine(
            Id, "Write it as from ... import ...",
            "Importing names out of a module is written `from module import names`.",
            source.Path, number, $"{match.Groups["indent"].Value}from {match.Groups["module"].Value} import {match.Groups["names"].Value}{tail}");
    }
}

/// <summary><c>from .helpers import greet</c> in a script run directly, where there is no package to be relative to.</summary>
public sealed partial class PythonRelativeImportInScript : ILocalFixRule
{
    public string Id => "python-relative-import";

    [GeneratedRegex(@"^(?<head>\s*from\s+)\.(?<module>[A-Za-z_]\w*)(?<rest>\s+import\b.*)$")]
    private static partial Regex RelativeImport();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "ImportError") || context.Error.Message?.StartsWith("attempted relative import with no known parent package", StringComparison.Ordinal) != true) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (RelativeImport().Match(line) is not { Success: true } match) return null;

        var module = match.Groups["module"].Value;

        // Only when the module is really there beside the script; otherwise dropping the dot names nothing.
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(source.Path)!, module + ".py"))) return null;

        return LocalFix.ReplaceLine(
            Id, $"Import {module} without the dot",
            $"The dot means \"from this package\", and a script run on its own is not in one. `{module}.py` is right beside it, " +
            "so it can be imported by name.",
            source.Path, number, $"{match.Groups["head"].Value}{module}{match.Groups["rest"].Value}");
    }
}
