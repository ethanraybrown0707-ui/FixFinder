using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>Mistakes whose shape is the same in C, Java and C#, whatever each compiler calls them: an <c>if (x);</c> that
/// swallowed its block, catch clauses in the wrong order, a string that never closes, and a list changed inside the loop walking
/// it.</summary>
internal static partial class BraceRules
{
    public static (int Line, string Corrected)? IfSemicolon(IReadOnlyList<string> lines, IReadOnlyList<string> masked, int elseLine)
    {
        var at = Regex.Match(masked[elseLine], @"\belse\b");
        if (!at.Success) return null;

        var row = elseLine;
        var col = at.Index - 1;

        while (true)
        {
            while (col >= 0 && char.IsWhiteSpace(masked[row][col])) col--;
            if (col >= 0) break;
            if (--row < 0) return null;
            col = masked[row].Length - 1;
        }

        if (masked[row][col] != '}') return null;

        var depth = 0;
        var found = false;

        while (row >= 0 && !found)
        {
            for (; col >= 0; col--)
            {
                if (masked[row][col] == '}') depth++;
                else if (masked[row][col] == '{' && --depth == 0)
                {
                    found = true;
                    break;
                }
            }

            if (found) break;
            if (--row >= 0) col = masked[row].Length - 1;
        }

        if (!found) return null;

        var head = masked[row][..col].TrimEnd();
        var headRow = row;

        if (head.Length == 0)
        {
            headRow = row - 1;
            while (headRow >= 0 && masked[headRow].Trim().Length == 0) headRow--;
            if (headRow < 0) return null;
            head = masked[headRow].TrimEnd();
        }

        if (!IfWithEmptyBody().IsMatch(head)) return null;

        var semicolon = head.Length - 1;
        return (headRow, lines[headRow][..semicolon] + lines[headRow][(semicolon + 1)..]);
    }

    [GeneratedRegex(@"\bif\s*\(.*\)\s*;$")]
    private static partial Regex IfWithEmptyBody();

    public const string IfSemicolonTitle = "Remove the semicolon after the if";

    public const string IfSemicolonExplanation =
        "The `;` straight after `if (...)` is an empty statement, and it is the whole of the if. The block after it then always runs, " +
        "and the `else` has no `if` left to belong to.";

    [GeneratedRegex(@"^\s*(?:\}\s*)?catch\b")]
    private static partial Regex CatchLine();

    public static (int Start, int Count, List<string> Lines)? SwapCatch(IReadOnlyList<string> lines, IReadOnlyList<string> masked, int second)
    {
        if (!CatchLine().IsMatch(masked[second])) return null;

        var depths = Brackets.BraceDepths(masked);
        var first = -1;

        for (var i = second - 1; i >= 0 && i >= second - 80; i--)
        {
            if (depths[i] < depths[second]) return null;
            if (depths[i] == depths[second] && CatchLine().IsMatch(masked[i]))
            {
                first = i;
                break;
            }
        }

        if (first < 0) return null;

        int end;

        if (masked[second].TrimStart().StartsWith('}'))
        {
            end = Enumerable.Range(second + 1, masked.Count - second - 1)
                .FirstOrDefault(i => depths[i] == depths[second] && masked[i].TrimStart().StartsWith('}'), -1);
        }
        else
        {
            end = Enumerable.Range(second + 1, masked.Count - second - 1)
                .FirstOrDefault(i => depths[i] > depths[second] && depths[i + 1] == depths[second], -1);
            if (end >= 0) end++;
        }

        if (end < 0) return null;

        List<string> swapped = [.. lines.Skip(second).Take(end - second), .. lines.Skip(first).Take(second - first)];
        return (first, end - first, swapped);
    }

    public const string CatchOrderExplanation =
        "Catch clauses are tried from the top, and the one before it already catches everything this one would - so this one could " +
        "never run. The more specific clause has to come first.";

    public static string? CloseString(string line, Syntax syntax)
    {
        if (OpenQuote(line) is not { } quote) return null;

        var before = CodeText.Mask(line[..quote], syntax);
        var open = before.Count(c => c == '(') - before.Count(c => c == ')');
        if (open < 0) return null;

        var rest = line[(quote + 1)..].TrimEnd();
        var cut = rest.Length;
        var semicolon = cut > 0 && rest[cut - 1] == ';';
        if (semicolon) cut--;
        if (!semicolon && open == 0) return null;

        for (var k = 0; k < open; k++)
        {
            while (cut > 0 && rest[cut - 1] == ' ') cut--;
            if (cut == 0 || rest[cut - 1] != ')') return null;
            cut--;
        }

        return line[..(quote + 1)] + rest[..cut] + "\"" + rest[cut..];
    }

    private static int? OpenQuote(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '/' && i + 1 < line.Length && line[i + 1] is '/' or '*') return null;
            if (c is not ('"' or '\'')) continue;

            var start = i;
            for (i++; i < line.Length && line[i] != c; i++)
                if (line[i] == '\\') i++;

            if (i >= line.Length) return c == '"' ? start : null;
        }

        return null;
    }

    public const string CloseStringExplanation =
        "The string has no closing `\"`, so everything after it on the line was read as part of the text.";

    [GeneratedRegex(@"^(?<indent>\s*)for\s*\(\s*(?:final\s+)?[\w$<>\[\],.? ]+?\s+(?<var>[\w$]+)\s*:\s*(?<list>[\w$.]+)\s*\)\s*(?<brace>\{)?\s*$")]
    private static partial Regex JavaForEach();

    [GeneratedRegex(@"^(?<indent>\s*)foreach\s*\(\s*[\w<>\[\],.? ]+?\s+(?<var>\w+)\s+in\s+(?<list>[\w.]+)\s*\)\s*(?<brace>\{)?\s*$")]
    private static partial Regex CSharpForEach();

    [GeneratedRegex(@"^(?<indent>\s*)for\s*\(\s*(?:const\s+)?(?<type>[\w:<>]+?)\s*&{0,2}\s*(?<var>\w+)\s*:\s*(?<list>\w+)\s*\)\s*(?<brace>\{)?\s*$")]
    private static partial Regex CppRangeFor();

    [GeneratedRegex(@"^if\s*(?<open>\()")]
    private static partial Regex If();

    public static (int Start, int Count, string Line)? RemoveInLoop(IReadOnlyList<string> lines, IReadOnlyList<string> masked, int near, bool java) =>
        RemoveInLoop(lines, masked, near, java ? "java" : "csharp");

    public static (int Start, int Count, string Line)? RemoveInLoop(IReadOnlyList<string> lines, IReadOnlyList<string> masked, int near, string language)
    {
        var header = language switch
        {
            "java" => JavaForEach(),
            "csharp" => CSharpForEach(),
            _ => CppRangeFor(),
        };
        var h = Enumerable.Range(0, 4).Select(d => near - d).FirstOrDefault(i => i >= 0 && i < masked.Count && header.IsMatch(masked[i]), -1);
        if (h < 0) return null;

        var loop = header.Match(masked[h]);
        var variable = loop.Groups["var"].Value;
        var list = loop.Groups["list"].Value;

        var brace = loop.Groups["brace"].Success ? h : h + 1 < masked.Count && masked[h + 1].Trim() == "{" ? h + 1 : -1;
        var body = new List<int>();
        int end;

        if (brace < 0)
        {
            if (h + 1 >= masked.Count) return null;
            body.Add(h + 1);
            end = h + 1;
        }
        else
        {
            var depth = 0;
            end = -1;

            for (var i = brace; i < masked.Count && end < 0; i++)
            {
                foreach (var c in masked[i])
                {
                    if (c == '{') depth++;
                    else if (c == '}') depth--;
                }

                if (i == brace) continue;
                if (depth == 0) end = i;
                else body.Add(i);
            }

            if (end < 0 || masked[end].Trim() != "}") return null;
        }

        var code = body.Where(i => masked[i].Trim().Length > 0).ToList();
        if (code.Count == 0 || If().Match(masked[code[0]].TrimStart()) is not { Success: true } condition) return null;

        var ifLine = masked[code[0]];
        var open = ifLine.Length - ifLine.TrimStart().Length + condition.Groups["open"].Index;
        if (Brackets.ClosingParenthesis(ifLine, open) is not { } close) return null;

        var after = ifLine[(close + 1)..].Trim();
        var l = Regex.Escape(list);
        var v = Regex.Escape(variable);

        var removal = new Regex(language switch
        {
            "java" => $@"^{l}\s*\.\s*remove\s*\(\s*{v}\s*\)\s*;$",
            "csharp" => $@"^{l}\s*\.\s*Remove\s*\(\s*{v}\s*\)\s*;$",
            _ => $@"^{l}\s*\.\s*erase\s*\(\s*(?:std\s*::\s*)?find\s*\(\s*{l}\s*\.\s*begin\s*\(\s*\)\s*,\s*{l}\s*\.\s*end\s*\(\s*\)\s*,\s*{v}\s*\)\s*\)\s*;$",
        });

        var shaped = code.Count switch
        {
            1 => removal.IsMatch(after),
            3 => after == "{" && removal.IsMatch(masked[code[1]].Trim()) && masked[code[2]].Trim() == "}",
            4 => after.Length == 0 && masked[code[1]].Trim() == "{" && removal.IsMatch(masked[code[2]].Trim()) && masked[code[3]].Trim() == "}",
            _ => false,
        };

        if (!shaped) return null;

        var test = lines[code[0]][(open + 1)..close].Trim();
        var call = language switch
        {
            "java" => $"{list}.removeIf({variable} -> {test});",
            "csharp" => $"{list}.RemoveAll({variable} => {test});",
            _ => $"{list}.erase(std::remove_if({list}.begin(), {list}.end(), [&]({loop.Groups["type"].Value} {variable}) {{ return {test}; }}), {list}.end());",
        };

        return (h, end - h + 1, loop.Groups["indent"].Value + call);
    }
}
