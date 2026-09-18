using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

internal static partial class CppCode
{
    public static bool IsCpp(SourceFile source) =>
        Path.GetExtension(source.Path).ToLowerInvariant() is ".cpp" or ".cc" or ".cxx" or ".c++" or ".hpp" or ".hh" or ".hxx";

    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context) =>
        CCode.IsCompiler(context.Error) && CCode.Locate(context) is { } at && IsCpp(at.Source) ? at : null;

    [GeneratedRegex(@"^\s*using\s+namespace\s+std\s*;")]
    private static partial Regex UsingStd();

    [GeneratedRegex(@"^'(?<name>[A-Za-z_]\w*)' was not declared in this scope")]
    private static partial Regex GccUndeclared();

    [GeneratedRegex(@"^'(?<name>[A-Za-z_]\w*)': (?:undeclared identifier|identifier not found)$")]
    private static partial Regex MsvcUndeclared();

    [GeneratedRegex(@"^(?:return|delete|new|else|case|throw|goto|sizeof|typedef|using|co_return|co_yield|operator|public|private|protected)$")]
    private static partial Regex NotAType();

    public static bool UsesStd(SourceFile source) =>
        CodeText.MaskAll(source.Lines, Syntax.CLike).Any(line => UsingStd().IsMatch(line));

    public static string StdQualified(SourceFile source, string name) => UsesStd(source) ? name : "std::" + name;

    public static bool HasStdString(SourceFile source) =>
        new[] { "string", "iostream", "sstream", "fstream", "ostream", "istream" }.Any(header => CCode.Includes(source, header));

    public static string? UndeclaredName(ParsedError error) =>
        (CCode.GccMessage(error, GccUndeclared()) ??
         (CCode.IsMsvc(error, "C2065", "C3861") ? MsvcUndeclared().Match(error.Message ?? "") is { Success: true } m ? m : null : null))
        ?.Groups["name"].Value;

    public static List<int> UnqualifiedUses(string masked, string word) =>
        Regex.Matches(masked, $@"(?<![\w.:])(?<!->){Regex.Escape(word)}(?!\w)").Select(m => m.Index).ToList();

    public static string JoinWithAnd(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list.SkipLast(1)) + " and " + list[^1];
    }

    public static (string Type, bool Pointer, bool Reference, int Line)? VariableDeclaration(IReadOnlyList<string> masked, int index, string name)
    {
        var (first, _) = CCode.EnclosingFunction(masked, index);
        var pattern = new Regex(
            $@"(?<=(?:^|[;({{,])\s*)(?<type>(?:(?:const|unsigned|signed|long|short|static)\s+)*[A-Za-z_][\w:]*(?:\s*<[^;()]*>)?)(?<marks>\s*[*&]+\s*|\s+){Regex.Escape(name)}\s*(?=[=;,)\[({{])");

        for (var i = index; i >= first; i--)
        {
            foreach (var match in pattern.Matches(masked[i]).Reverse())
            {
                var type = match.Groups["type"].Value.Trim();
                if (NotAType().IsMatch(type)) continue;

                var marks = match.Groups["marks"].Value;
                return (Regex.Replace(type, @"^(?:const|static)\s+", ""), marks.Contains('*'), marks.Contains('&'), i);
            }
        }

        return null;
    }

    public static List<(int Start, int End, char Quote)> StringLiterals(string line)
    {
        var found = new List<(int, int, char)>();

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '/' && i + 1 < line.Length && line[i + 1] is '/' or '*') break;
            if (c is not ('"' or '\'')) continue;
            if (c == '\'' && i > 0 && char.IsLetterOrDigit(line[i - 1])) continue;

            var start = i;
            for (i++; i < line.Length && line[i] != c; i++)
                if (line[i] == '\\') i++;

            if (i >= line.Length) break;
            found.Add((start, i + 1, c));
        }

        return found;
    }

    public static List<(int Start, int End)> SplitTopLevel(string masked, int from, int to, char separator)
    {
        var parts = new List<(int, int)>();
        var depth = 0;
        var start = from;

        for (var i = from; i < to; i++)
        {
            if (masked[i] is '(' or '[' or '{') depth++;
            else if (masked[i] is ')' or ']' or '}') depth--;
            else if (masked[i] == separator && depth == 0)
            {
                if (separator == '+' && ((i + 1 < to && masked[i + 1] is '+' or '=') || (i > from && masked[i - 1] == '+'))) continue;

                parts.Add((start, i));
                start = i + 1;
            }
        }

        parts.Add((start, to));
        return parts;
    }

    [GeneratedRegex(@"^'(?:[^']*?\s)?(?<class>\w+)::(?<member>~?\w+)\([^']*' marked 'override', but does not override$")]
    private static partial Regex GccOverride();

    [GeneratedRegex(@"^'(?<class>\w+)::(?<member>~?\w+)': method with override specifier 'override' did not override any base class methods$")]
    private static partial Regex MsvcOverride();

    [GeneratedRegex(@"^\s*(?:class|struct)\s+\w+\s*(?:final\s*)?:(?!:)(?<bases>[^{]*)")]
    private static partial Regex BaseList();

    [GeneratedRegex(@"^\s*(?<access>(?:(?:public|protected|private|virtual)\s+)*)(?<name>[A-Za-z_][\w:]*)")]
    private static partial Regex Base();

    public static Match? OverrideError(ParsedError error) =>
        CCode.GccMessage(error, GccOverride()) ?? CCode.MsvcMessage(error, "C3668", MsvcOverride());

    public static int ClassHeader(IReadOnlyList<string> masked, string name)
    {
        var depths = Brackets.BraceDepths(masked);
        var pattern = new Regex($@"^\s*(?:class|struct)\s+{Regex.Escape(name)}\b(?!\s*;)");
        var found = Enumerable.Range(0, masked.Count).Where(i => depths[i] == 0 && pattern.IsMatch(masked[i])).ToList();

        return found is [var only] ? only : -1;
    }

    public static (int Open, int Close)? ClassBraces(IReadOnlyList<string> masked, int header)
    {
        var depth = 0;
        var open = -1;

        for (var i = header; i < masked.Count; i++)
        {
            foreach (var c in masked[i])
            {
                if (c == '{')
                {
                    if (depth++ == 0) open = i;
                }
                else if (c == '}' && --depth == 0 && open >= 0)
                {
                    return (open, i);
                }
            }

            if (open < 0 && i > header + 2) return null;
        }

        return null;
    }

    public static List<int> MemberLines(IReadOnlyList<string> masked, int header)
    {
        if (ClassBraces(masked, header) is not { } body) return [];

        var depths = Brackets.BraceDepths(masked);
        var level = depths[body.Open] + 1;

        return Enumerable.Range(body.Open + 1, Math.Max(0, body.Close - body.Open - 1)).Where(i => depths[i] == level).ToList();
    }

    public static List<int> LinesDeclaring(IReadOnlyList<string> masked, int header, string member) =>
        MemberLines(masked, header).Where(i => Regex.IsMatch(masked[i], $@"(?<![\w:~.>]){Regex.Escape(member)}\s*\(")).ToList();

    public static List<(string Name, int Column, bool Specified)> BaseClasses(string maskedHeader)
    {
        if (BaseList().Match(maskedHeader) is not { Success: true } list) return [];

        var group = list.Groups["bases"];
        var bases = new List<(string, int, bool)>();

        foreach (var (start, end) in SplitTopLevel(maskedHeader, group.Index, group.Index + group.Length, ','))
        {
            if (Base().Match(maskedHeader[start..end]) is not { Success: true } one) continue;

            var access = one.Groups["access"].Value;
            bases.Add((one.Groups["name"].Value.Split("::")[^1], start + one.Groups["name"].Index,
                Regex.IsMatch(access, @"\b(?:public|protected|private)\b")));
        }

        return bases;
    }

    public static IEnumerable<SourceFile> SourceFiles(LocalFixContext context)
    {
        if (context.SourceRoot is not { } root || !Directory.Exists(root)) yield break;

        foreach (var path in Directory.EnumerateFiles(root).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".cpp" or ".cc" or ".cxx" or ".c++").Take(200))
            if (SourceFile.Read(path) is { } source) yield return source;
    }
}
