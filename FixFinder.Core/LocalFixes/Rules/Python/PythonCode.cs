using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

internal static partial class PythonCode
{
    public static bool Raised(LocalFixContext context, string type) =>
        context.Error.LanguageId == "python" && context.Error.ExceptionType == type;

    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        return source.Line(number) is { } line ? (source, number, line) : null;
    }

    public static int ReceiverStart(string masked, int dot)
    {
        var i = dot - 1;

        while (i >= 0)
        {
            var c = masked[i];

            if (c is ')' or ']')
            {
                i = Brackets.Opening(masked, i);
                if (i < 0) return -1;
                i--;
                continue;
            }

            if (c is '"' or '\'')
            {
                var open = i > 0 ? masked.LastIndexOf(c, i - 1) : -1;
                if (open < 0) return -1;
                i = open - 1;
                continue;
            }

            if (CodeText.IsWordChar(c) || c == '.')
            {
                i--;
                continue;
            }

            break;
        }

        return i + 1 < dot ? i + 1 : -1;
    }

    private static readonly string[] BareBefore = ["if", "elif", "while", "return", "=", "(", ",", "not", "and", "or", "["];
    private static readonly string[] BareAfter = [":", ")", ",", "]", "and ", "or ", "if ", "else "];

    public static bool NeedsParentheses(string line, int start, int end)
    {
        var before = line[..start].TrimEnd();
        var after = line[end..].TrimStart();

        var bareBefore = before.Length == 0 || BareBefore.Any(token => before.EndsWith(token, StringComparison.Ordinal));
        var bareAfter = after.Length == 0 || after.StartsWith('#') || BareAfter.Any(token => after.StartsWith(token, StringComparison.Ordinal));

        return !(bareBefore && bareAfter);
    }

    [GeneratedRegex(@"^\s+File\s+"".+"",\s+line\s+(?<line>\d+)")]
    private static partial Regex FileLine();

    [GeneratedRegex(@"^\s*[~^]+\s*$")]
    private static partial Regex Underline();

    public static (int Start, int End)? UnderlineSpan(ParsedError error, int number, string line)
    {
        var raw = error.RawText.Split('\n').Select(text => text.TrimEnd('\r')).ToList();
        var at = raw.FindLastIndex(text => FileLine().Match(text) is { Success: true } m && m.Groups["line"].Value == number.ToString());

        if (at < 0 || at + 2 >= raw.Count || !Underline().IsMatch(raw[at + 2])) return null;

        var echo = raw[at + 1];
        var echoed = echo.Trim();
        var offset = line.IndexOf(echoed, StringComparison.Ordinal);

        if (echoed.Length == 0 || offset < 0) return null;

        var underline = raw[at + 2].TrimEnd();
        var first = underline.Length - underline.TrimStart().Length;
        var echoStart = echo.Length - echo.TrimStart().Length;

        var start = offset + (first - echoStart);
        var end = offset + (underline.Length - echoStart);

        return start >= 0 && end <= line.Length && end > start ? (start, end) : null;
    }

    [GeneratedRegex(@"^-?\d+(?<fraction>\.\d*)?$")]
    public static partial Regex NumberLiteral();

    [GeneratedRegex(@"^[rRbBuUfF]{0,2}[""']")]
    public static partial Regex StringLiteral();

    public static LocalFix? WithImport(
        string rule, string title, string explanation, SourceFile source, int number, string module, string name, Func<string, string> change)
    {
        var lines = source.Lines;

        if (lines.Any(l => Regex.IsMatch(l, $@"^import\s+{Regex.Escape(module)}\s*(?:#.*)?$")))
            return LocalFix.ReplaceLine(rule, title, explanation, source.Path, number, change($"{module}.{name}"));

        var from = Enumerable.Range(0, number - 1)
            .Select(i => (Index: i, Match: Regex.Match(lines[i], $@"^from\s+{Regex.Escape(module)}\s+import\s+(?<names>[A-Za-z_][\w\s,]*?)\s*(?:#.*)?$")))
            .FirstOrDefault(x => x.Match.Success);

        if (from.Match is { Success: true } existing)
        {
            var changed = change(name);
            if (existing.Groups["names"].Value.Split(',').Any(n => n.Trim() == name))
                return LocalFix.ReplaceLine(rule, title, explanation, source.Path, number, changed);

            var start = from.Index + 1;
            return Span(rule, title, explanation, source, start, number, [lines[from.Index].TrimEnd() + ", " + name], changed);
        }

        var insertAt = ImportInsertionLine(lines);
        if (insertAt > number) return null;

        return Span(rule, title, explanation, source, insertAt, number, [$"from {module} import {name}"], change(name), insert: true);
    }

    private static LocalFix Span(
        string rule, string title, string explanation, SourceFile source, int start, int number, IReadOnlyList<string> head, string changed, bool insert = false)
    {
        var lines = source.Lines;
        var middle = lines.Skip(start - 1 + (insert ? 0 : 1)).Take(number - start - (insert ? 0 : 1));

        return new LocalFix
        {
            RuleId = rule, Title = title, Explanation = explanation, File = source.Path,
            StartLine = start, RemoveCount = number - start + 1, NewLines = [.. head, .. middle, changed],
        };
    }

    public static List<(int Start, string Text)>? CallArguments(string line, string masked, int open)
    {
        var close = Brackets.Closing(masked, open);
        if (close < 0) return null;

        var result = new List<(int, string)>();
        var depth = 0;
        var start = open + 1;

        for (var i = open + 1; i <= close; i++)
        {
            var c = masked[i];

            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}' && i < close) depth--;

            if ((c == ',' && depth == 0) || i == close)
            {
                var text = line[start..i];
                if (text.Trim().Length > 0) result.Add((start + (text.Length - text.TrimStart().Length), text.Trim()));
                start = i + 1;
            }
        }

        return result;
    }

    public static bool IsSingleOperand(string expression) =>
        Regex.IsMatch(expression, @"^[A-Za-z_][\w.]*(?:\([^()]*\)|\[[^\[\]]*\])*$");

    [GeneratedRegex(@"^\s*(?:async\s+)?def\s+(?<name>[A-Za-z_]\w*)\s*\((?<parameters>.*)\)\s*(?:->[^:]*)?:")]
    public static partial Regex FunctionHeader();

    [GeneratedRegex(@"^\s*(?:async\s+)?def\s")]
    public static partial Regex FunctionKeyword();

    public static int EnclosingHeader(IReadOnlyList<string> masked, int index, Regex header)
    {
        var indent = CodeText.Indentation(masked[index]).Length;

        for (var i = index - 1; i >= 0 && indent > 0; i--)
        {
            if (masked[i].Trim().Length == 0) continue;

            var own = CodeText.Indentation(masked[i]).Length;
            if (own >= indent) continue;

            if (header.IsMatch(masked[i])) return i;
            indent = own;
        }

        return -1;
    }

    public static (int First, int End) BlockBody(IReadOnlyList<string> lines, int header)
    {
        var indent = CodeText.Indentation(lines[header]).Length;
        var end = header + 1;

        while (end < lines.Count && (lines[end].Trim().Length == 0 || CodeText.Indentation(lines[end]).Length > indent)) end++;
        while (end > header + 1 && lines[end - 1].Trim().Length == 0) end--;

        return (header + 1, end);
    }

    public static int FirstStatement(IReadOnlyList<string> lines, int first, int end)
    {
        while (first < end && lines[first].Trim().Length == 0) first++;
        if (first >= end) return first;

        var opening = lines[first].TrimStart();
        var quote = opening.StartsWith("\"\"\"", StringComparison.Ordinal) ? "\"\"\"" : opening.StartsWith("'''", StringComparison.Ordinal) ? "'''" : null;

        if (quote is null) return first;
        if (opening.Length > 3 && opening[3..].Contains(quote, StringComparison.Ordinal)) return first + 1;

        var close = first + 1;
        while (close < end && !lines[close].Contains(quote, StringComparison.Ordinal)) close++;

        return close + 1;
    }

    public static List<string> SplitParameters(string parameters) => parameters
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(p => Regex.Match(p, @"^\*{0,2}(?<name>[A-Za-z_]\w*)").Groups["name"].Value)
        .Where(name => name.Length > 0)
        .ToList();

    public static int TopLevelIndexOf(string masked, string op)
    {
        var depth = 0;

        for (var i = 0; i + op.Length <= masked.Length; i++)
        {
            var c = masked[i];

            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (depth == 0 && string.CompareOrdinal(masked, i, op, 0, op.Length) == 0) return i;
        }

        return -1;
    }

    public static List<int> AssignmentLines(IReadOnlyList<string> masked, string name) =>
        Enumerable.Range(0, masked.Count)
            .Where(i => Regex.IsMatch(masked[i], $@"^\s*{Regex.Escape(name)}\s*(?::[^=]*)?=(?!=)"))
            .ToList();

    public static int? UnderlineColumn(ParsedError error, int number, string fileLine)
    {
        var raw = error.RawText.Split('\n').Select(text => text.TrimEnd('\r')).ToList();
        var at = raw.FindLastIndex(text => FileLine().Match(text) is { Success: true } m && m.Groups["line"].Value == number.ToString());

        if (at < 0 || at + 2 >= raw.Count || !Underline().IsMatch(raw[at + 2])) return null;

        var echo = raw[at + 1];
        var echoed = echo.Trim();
        var offset = fileLine.IndexOf(echoed, StringComparison.Ordinal);

        if (echoed.Length == 0 || offset < 0) return null;

        var underline = raw[at + 2];
        var start = underline.Length - underline.TrimStart().Length;
        var echoStart = echo.Length - echo.TrimStart().Length;

        return offset + (start - echoStart);
    }

    [GeneratedRegex(@"^(?:import|from)\s+[A-Za-z_.]")]
    private static partial Regex ImportLine();

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
