using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

internal static partial class CCode
{
    public static bool IsCompiler(ParsedError error) =>
        error.LanguageId is "msvc" or "gcc" && error.ErrorCode?.StartsWith("CS", StringComparison.Ordinal) != true;

    public static bool IsMsvc(ParsedError error, params string[] codes) =>
        error.LanguageId == "msvc" && codes.Contains(error.ErrorCode);

    public static Match? MsvcMessage(ParsedError error, string code, Regex message) =>
        error.LanguageId == "msvc" && error.ErrorCode == code && message.Match(error.Message ?? "") is { Success: true } m ? m : null;

    public static Match? GccMessage(ParsedError error, Regex message) =>
        error.LanguageId == "gcc" && message.Match(error.Message ?? "") is { Success: true } m ? m : null;

    public static bool IsNative(SourceFile source) =>
        Path.GetExtension(source.Path).ToLowerInvariant() is ".c" or ".cpp" or ".cc" or ".cxx" or ".c++" or ".h" or ".hpp";

    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!IsNative(source) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    public static bool Includes(SourceFile source, string header) =>
        source.Lines.Any(line => IncludeLine().Match(line) is { Success: true } m &&
                                 m.Groups["header"].Value.Equals(header, StringComparison.OrdinalIgnoreCase));

    public static (int Header, int End) EnclosingFunction(IReadOnlyList<string> masked, int index)
    {
        var depths = Brackets.BraceDepths(masked);

        var start = index;
        while (start > 0 && depths[start] > 0) start--;
        if (masked[start].Trim() == "{" && start > 0) start--;

        var end = index;
        while (end + 1 < masked.Count && depths[end + 1] > 0) end++;

        return (start, end);
    }

    public static string ReplaceEach(string line, IEnumerable<Match> hits, Func<Match, string> replacement)
    {
        foreach (var hit in hits.OrderByDescending(h => h.Index))
            line = line[..hit.Index] + replacement(hit) + line[(hit.Index + hit.Length)..];

        return line;
    }

    public static LocalFix RemoveLine(string ruleId, string title, string explanation, string file, int line) => new()
    {
        RuleId = ruleId, Title = title, Explanation = explanation, File = file,
        StartLine = line, RemoveCount = 1, NewLines = [],
    };

    [GeneratedRegex(@"^\s*#\s*include\s*[<""](?<header>[^>""]+)[>""]")]
    public static partial Regex IncludeLine();

    public static bool SameFile(LocalFixContext context, ParsedError error, SourceFile source) =>
        context.Resolve((error.CulpritFrame ?? error.Frames.FirstOrDefault())?.File) is { } path &&
        string.Equals(path, source.Path, StringComparison.OrdinalIgnoreCase);

    public static bool IsC(SourceFile source) =>
        Path.GetExtension(source.Path).Equals(".c", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^\s*#\d+\s+0x[0-9a-fA-F]+\s+in\s+\S+\s+(?<file>.+?):(?<line>\d+)")]
    public static partial Regex SanitizerFrame();

    public static (SourceFile Source, int Number)? FrameAfter(LocalFixContext context, Regex section)
    {
        var output = context.Output.Select(l => l.Text).ToList();
        var start = output.FindIndex(l => section.IsMatch(l));
        if (start < 0) return null;

        foreach (var line in output.Skip(start + 1).TakeWhile(l => l.Trim().Length > 0))
        {
            if (SanitizerFrame().Match(line) is not { Success: true } frame || context.Read(frame.Groups["file"].Value.Trim()) is not { } source) continue;
            if (!IsNative(source)) continue;

            return (source, int.Parse(frame.Groups["line"].Value));
        }

        return null;
    }

    public static LocalFix WithHeader(string rule, string title, string explanation, SourceFile source, int number, string header, string changed)
    {
        if (Includes(source, header))
            return LocalFix.ReplaceLine(rule, title, explanation, source.Path, number, changed);

        var lines = source.Lines;
        var last = Enumerable.Range(0, number - 1).LastOrDefault(i => IncludeLine().IsMatch(lines[i]), -1);
        var start = last + 2;

        return new LocalFix
        {
            RuleId = rule, Title = title, Explanation = explanation, File = source.Path,
            StartLine = start, RemoveCount = number - start + 1,
            NewLines = [$"#include <{header}>", .. lines.Skip(start - 1).Take(number - start), changed],
        };
    }

    public static bool DeclaredAsArray(IReadOnlyList<string> masked, int index, string name)
    {
        var (header, _) = EnclosingFunction(masked, index);
        var array = new Regex($@"\b{Regex.Escape(name)}\s*\[[^\]]*\]");
        var pointer = new Regex($@"\*\s*{Regex.Escape(name)}\b");
        var depths = Brackets.BraceDepths(masked);

        var scope = Enumerable.Range(header, Math.Max(0, index - header)).Concat(Enumerable.Range(0, masked.Count).Where(i => depths[i] == 0));

        return scope.Any(i => array.IsMatch(masked[i])) && !scope.Any(i => pointer.IsMatch(masked[i]) && !array.IsMatch(masked[i]));
    }
}
