using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>Where a new top-level import goes, and how this file indents.</summary>
internal static partial class PythonLayout
{
    [GeneratedRegex(@"^(?:import|from)\s+[A-Za-z_.]")]
    private static partial Regex ImportLine();

    /// <summary>The 1-based line a new import is inserted in front of.</summary>
    /// <remarks>
    /// After the shebang, the encoding line, leading comments, the module docstring, and every import
    /// already at the top - so it lands with the others, and never above <c>from __future__</c>,
    /// which Python requires to come first.
    /// </remarks>
    public static int ImportInsertionLine(IReadOnlyList<string> lines)
    {
        var i = 0;

        while (i < lines.Count && (lines[i].StartsWith('#') || lines[i].Trim().Length == 0)) i++;

        if (i < lines.Count && DocstringQuote(lines[i]) is { } quote)
        {
            var opening = lines[i].TrimStart();
            var afterOpening = opening[(opening.IndexOf(quote, StringComparison.Ordinal) + 3)..];

            if (afterOpening.Contains(quote, StringComparison.Ordinal))
            {
                i++;
            }
            else
            {
                i++;
                while (i < lines.Count && !lines[i].Contains(quote, StringComparison.Ordinal)) i++;
                i++;
            }
        }

        var lastImport = -1;
        var depth = 0;

        for (var k = Math.Min(i, lines.Count); k < lines.Count; k++)
        {
            var line = lines[k];

            if (depth > 0)
            {
                depth += line.Count(c => c == '(') - line.Count(c => c == ')');
                lastImport = k;
                continue;
            }

            if (ImportLine().IsMatch(line))
            {
                depth = line.Count(c => c == '(') - line.Count(c => c == ')');
                lastImport = k;
                continue;
            }

            if (line.Trim().Length == 0 || line.StartsWith('#')) continue;

            break;
        }

        return (lastImport >= 0 ? lastImport + 1 : Math.Min(i, lines.Count)) + 1;
    }

    private static string? DocstringQuote(string line)
    {
        var trimmed = line.TrimStart().TrimStart('r', 'R', 'u', 'U');
        if (trimmed.StartsWith("\"\"\"", StringComparison.Ordinal)) return "\"\"\"";
        if (trimmed.StartsWith("'''", StringComparison.Ordinal)) return "'''";
        return null;
    }

    /// <summary>One level of indentation as this file writes it: a tab, or its smallest step of spaces.</summary>
    public static string IndentUnit(IReadOnlyList<string> lines)
    {
        if (lines.Any(line => line.StartsWith('\t'))) return "\t";

        var steps = lines
            .Where(line => line.Trim().Length > 0)
            .Select(line => line.Length - line.TrimStart(' ').Length)
            .Where(width => width > 0)
            .ToList();

        return new string(' ', steps.Count > 0 ? Math.Min(steps.Min(), 8) : 4);
    }
}

/// <summary><c>NameError: name 'math' is not defined. Did you forget to import 'math'?</c></summary>
/// <remarks>
/// Python 3.13 names the module itself, and only for modules in the standard library, so there is
/// nothing to look up: the answer is the import it named.
/// </remarks>
public sealed partial class PythonForgottenImport : ILocalFixRule
{
    public string Id => "python-forgotten-import";

    [GeneratedRegex(@"Did you forget to import '(?<module>[A-Za-z_][A-Za-z0-9_]*)'\?")]
    private static partial Regex HintPattern();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "python" || error.ExceptionType != "NameError") return null;
        if (HintPattern().Match(error.Message ?? "") is not { Success: true } hint) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var module = hint.Groups["module"].Value;

        // Python suggests these because they exist, not because anybody means them: `this` prints the
        // Zen of Python and `antigravity` opens a web browser. A `this.name` in a method is Java.
        if (module is "this" or "antigravity") return null;

        return LocalFix.Insert(
            Id,
            $"Add import {module}",
            $"Python said so itself: `{module}` is a standard module that was used without being imported. " +
            "The import goes at the top of the file, with any others.",
            source.Path,
            PythonLayout.ImportInsertionLine(source.Lines),
            [$"import {module}"]);
    }
}

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
            source.Path, number, headerIndent + PythonLayout.IndentUnit(source.Lines) + line.TrimStart());
    }
}

/// <summary><c>TypeError: can only concatenate str (not "int") to str</c></summary>
/// <remarks>
/// Python 3.11 and later underline the expression that failed: <c>~</c> under each operand and
/// <c>^</c> under the operator. That pins both operands exactly - and which one to convert depends on
/// what the left one is.
/// <para>
/// <b>Text on the left means joining text</b>: <c>"Total: " + total</c> wants <c>str(total)</c>.
/// <b>A value on the left and a number on the right means arithmetic</b>: <c>age + 1</c>, with
/// <c>age</c> read by <c>input()</c>, wants <c>int(age) + 1</c>. Converting the 1 instead turns 18
/// into "181", which is the worst kind of fix: the kind that runs. Anything else - two names, say -
/// could be either, and is refused.
/// </para>
/// </remarks>
public sealed partial class PythonStrConcatenation : ILocalFixRule
{
    public string Id => "python-str-concatenation";

    [GeneratedRegex(@"^can only concatenate str \(not ""(?<type>\w+)""\) to str$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^\s+File\s+"".+"",\s+line\s+(?<line>\d+)")]
    private static partial Regex FileLine();

    [GeneratedRegex(@"^\s*~+\^+~+\s*$")]
    private static partial Regex Underline();

    /// <summary>A string literal, with any prefix, or an explicit str(...).</summary>
    [GeneratedRegex(@"^[rRbBuUfF]{0,2}[""']|^str\(")]
    private static partial Regex Text();

    [GeneratedRegex(@"^-?\d+(?<fraction>\.\d*)?$")]
    private static partial Regex Number();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "python" || error.ExceptionType != "TypeError") return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var raw = error.RawText.Split('\n').Select(text => text.TrimEnd('\r')).ToList();
        var at = raw.FindLastIndex(text => FileLine().Match(text) is { Success: true } m && m.Groups["line"].Value == number.ToString());

        if (at < 0 || at + 2 >= raw.Count) return null;

        var echo = raw[at + 1];
        var underline = raw[at + 2];

        if (!Underline().IsMatch(underline)) return null;

        var echoed = echo.Trim();
        var offset = line.IndexOf(echoed, StringComparison.Ordinal);
        if (echoed.Length == 0 || offset < 0) return null;

        var echoStart = echo.Length - echo.TrimStart().Length;
        int Column(int printed) => offset + (printed - echoStart);

        var leftStart = underline.IndexOf('~');
        var operatorStart = underline.IndexOf('^');
        var operatorEnd = underline.LastIndexOf('^') + 1;
        var rightEnd = underline.TrimEnd().Length;

        if (Column(leftStart) < 0 || Column(rightEnd) > line.Length) return null;
        if (line[Column(operatorStart)..Column(operatorEnd)].Trim() != "+") return null;

        var (leftAt, left) = Operand(line, Column(leftStart), Column(operatorStart), isLeft: true);
        var (rightAt, right) = Operand(line, Column(operatorEnd), Column(rightEnd), isLeft: false);

        if (left.Length == 0 || right.Length == 0) return null;

        // An underline that stops inside a name is not one to act on: wrapping half of `total`
        // produces `str(tota)l`, which a compile check cannot tell from a fix.
        if (CutsAName(line, leftAt, left) || CutsAName(line, rightAt, right)) return null;

        if (Text().IsMatch(left))
        {
            if (Text().IsMatch(right)) return null;

            return LocalFix.ReplaceLine(
                Id,
                $"Convert {right} to a string before joining it",
                $"The left side of + is text and `{right}` is an {message.Groups["type"].Value}; Python will only join text " +
                "to more text. str() makes the text form of it. An f-string does the same job if you prefer it.",
                source.Path, number, line[..rightAt] + $"str({right})" + line[(rightAt + right.Length)..]);
        }

        if (Number().Match(right) is { Success: true } numeric)
        {
            var convert = numeric.Groups["fraction"].Success ? "float" : "int";

            return LocalFix.ReplaceLine(
                Id,
                $"Turn {left} into a number before adding {right}",
                $"`{left}` holds text - `input()` always returns text, for one - and `{right}` is a number, so Python " +
                $"will not add them. `{convert}()` reads the number out of the text. If `{left}` really is meant to stay " +
                $"text, join `str({right})` instead.",
                source.Path, number, line[..leftAt] + $"{convert}({left})" + line[(leftAt + left.Length)..]);
        }

        return null;
    }

    private static bool CutsAName(string line, int at, string operand) =>
        (at > 0 && CodeText.IsWordChar(line[at - 1]) && CodeText.IsWordChar(operand[0])) ||
        (at + operand.Length < line.Length && CodeText.IsWordChar(line[at + operand.Length]) && CodeText.IsWordChar(operand[^1]));

    /// <summary>
    /// The operand under a stretch of underline, and where it starts. The underline can take in a
    /// bracket of the call around it, so any bracket the operand did not open is given back.
    /// </summary>
    private static (int At, string Operand) Operand(string line, int start, int end, bool isLeft)
    {
        var span = line[start..end];
        var operand = span.Trim();

        if (isLeft)
        {
            while (operand.StartsWith('(') && operand.Count(c => c == '(') > operand.Count(c => c == ')')) operand = operand[1..].TrimStart();
        }
        else
        {
            while (operand.EndsWith(')') && operand.Count(c => c == ')') > operand.Count(c => c == '(')) operand = operand[..^1].TrimEnd();
        }

        var at = operand.Length == 0 ? start : start + span.IndexOf(operand, StringComparison.Ordinal);

        return (at, operand);
    }
}

/// <summary>
/// <c>UnboundLocalError: cannot access local variable 'count' where it is not associated with a value</c>
/// </summary>
/// <remarks>
/// Assigning to a name anywhere in a function makes it local for the whole function, so
/// <c>count += 1</c> reads a local nothing was assigned to. When a module-level <c>count</c>
/// exists and the function never sets its own, the module one is what was meant, and
/// <c>global count</c> says so.
/// </remarks>
public sealed partial class PythonUnboundGlobal : ILocalFixRule
{
    public string Id => "python-unbound-global";

    [GeneratedRegex(@"(?:cannot access local variable|local variable) '(?<name>[A-Za-z_]\w*)'")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "python" || error.ExceptionType != "UnboundLocalError") return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Frame is not { Line: { } number, Symbol: { Length: > 0 } function } frame) return null;
        if (context.Read(frame.File) is not { } source) return null;

        var name = message.Groups["name"].Value;
        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);

        var definitions = Enumerable.Range(0, lines.Count)
            .Where(i => Regex.IsMatch(masked[i], $@"^def\s+{Regex.Escape(function)}\s*\(.*\)\s*(->[^:]*)?:\s*$"))
            .ToList();

        if (definitions.Count != 1) return null;

        var def = definitions[0];
        var bodyEnd = def + 1;

        while (bodyEnd < lines.Count && (lines[bodyEnd].Trim().Length == 0 || char.IsWhiteSpace(lines[bodyEnd][0]))) bodyEnd++;

        if (number - 1 <= def || number - 1 >= bodyEnd) return null;

        var body = Enumerable.Range(def + 1, bodyEnd - def - 1).Select(i => masked[i]).ToList();
        var word = Regex.Escape(name);

        if (body.Any(text => Regex.IsMatch(text, $@"^\s*(?:global|nonlocal)\b.*\b{word}\b"))) return null;

        // A plain assignment that does not read the name means the function may well want its own.
        if (body.Any(text => Regex.IsMatch(text, $@"^\s*{word}\s*=(?!=)") && Regex.Matches(text, $@"\b{word}\b").Count == 1)) return null;

        var moduleLevel = Enumerable.Range(0, lines.Count)
            .Where(i => i < def || i >= bodyEnd)
            .Any(i => Regex.IsMatch(masked[i], $@"^{word}\s*(?::[^=]*)?=(?!=)"));

        if (!moduleLevel) return null;

        var first = def + 1;
        while (first < bodyEnd && lines[first].Trim().Length == 0) first++;
        if (first >= bodyEnd) return null;

        var indent = CodeText.Indentation(lines[first]);
        var insertAt = first;

        // After a docstring, not in front of it - a docstring has to be the first statement.
        var opening = lines[first].TrimStart();
        var quote = opening.StartsWith("\"\"\"", StringComparison.Ordinal) ? "\"\"\"" : opening.StartsWith("'''", StringComparison.Ordinal) ? "'''" : null;

        if (quote is not null)
        {
            var closesOnSameLine = opening.Length > 3 && opening[3..].Contains(quote, StringComparison.Ordinal);
            insertAt = first + 1;

            if (!closesOnSameLine)
            {
                while (insertAt < bodyEnd && !lines[insertAt].Contains(quote, StringComparison.Ordinal)) insertAt++;
                insertAt++;
            }
        }

        return LocalFix.Insert(
            Id,
            $"Declare {name} as global in {function}",
            $"{function} assigns to `{name}`, and assigning anywhere in a function makes the name local to all of it - " +
            $"so line {number} reads a local that was never set. There is a `{name}` at module level, and " +
            $"`global {name}` makes the function use that one.",
            source.Path, insertAt + 1, [$"{indent}global {name}"]);
    }
}
