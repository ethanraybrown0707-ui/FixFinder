using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

internal static partial class JavaCode
{
    public static bool IsCompileError(ParsedError error) =>
        error.LanguageId == "java" && error.ExceptionType == "compile error";

    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (!IsCompileError(context.Error)) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!source.Path.EndsWith(".java", StringComparison.OrdinalIgnoreCase) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    public static int? CaretColumn(LocalFixContext context, string line) =>
        CodeText.Caret(context.Output, context.Error) is { } caret && caret.Echo == line && caret.Column <= line.Length
            ? caret.Column
            : null;

    /// <summary>A note javac prints under the caret, such as <c>first type: char</c>.</summary>
    public static string? Note(LocalFixContext context, string label)
    {
        var output = context.Output;
        var start = -1;

        for (var i = 0; i < output.Count && start < 0; i++)
            if (output[i].Sequence == context.Error.FirstLineSequence) start = i;

        if (start < 0) return null;

        for (var i = start + 1; i < output.Count && i <= start + 8; i++)
        {
            if (DiagnosticLine().IsMatch(output[i].Text)) break;

            if (Regex.Match(output[i].Text, $@"^\s+{Regex.Escape(label)}:\s+(?<value>.+?)\s*$") is { Success: true } note)
                return note.Groups["value"].Value;
        }

        return null;
    }

    [GeneratedRegex(@"\.java:\d+: (?:error|warning):")]
    private static partial Regex DiagnosticLine();

    [GeneratedRegex(@"^cannot find symbol \(symbol:\s+(?<kind>variable|method|class)\s+(?<name>[A-Za-z_$][\w$]*)(?<arguments>\([^)]*\))?(?:,\s*location:\s*(?<location>.+))?\)$")]
    public static partial Regex CannotFindSymbol();

    [GeneratedRegex(@"^variable (?<receiver>[\w$]+) of type (?<type>.+)$")]
    public static partial Regex VariableLocation();

    public static readonly Dictionary<string, string> WrapperTypes = new(StringComparer.Ordinal)
    {
        ["int"] = "Integer", ["long"] = "Long", ["double"] = "Double", ["float"] = "Float",
        ["boolean"] = "Boolean", ["char"] = "Character", ["short"] = "Short", ["byte"] = "Byte",
    };

    public static readonly Dictionary<string, string> ParseMethods = new(StringComparer.Ordinal)
    {
        ["int"] = "Integer.parseInt", ["long"] = "Long.parseLong", ["double"] = "Double.parseDouble", ["float"] = "Float.parseFloat",
    };

    public static bool IsCollection(string type) =>
        Regex.IsMatch(type, @"^(?:java\.util\.)?(?:List|ArrayList|LinkedList|Vector|Stack|Set|HashSet|TreeSet|LinkedHashSet|Collection|Queue|Deque|ArrayDeque|PriorityQueue|Map|HashMap|TreeMap|LinkedHashMap)\b");

    public static string WithoutTypeArguments(string type) => type.IndexOf('<') is var generic and > 0 ? type[..generic] : type;

    /// <summary>The first and last line (0-based) of the method around a line: the block one level inside a class.</summary>
    public static (int First, int Last) EnclosingMethod(IReadOnlyList<string> masked, int index)
    {
        var depths = Brackets.BraceDepths(masked);

        var first = index;
        while (first > 0 && depths[first] > 1) first--;

        var last = index;
        while (last + 1 < masked.Count && depths[last + 1] > 1) last++;

        return (first, last);
    }

    /// <summary>The last line of a loop body that starts after <paramref name="after"/> on <paramref name="line"/>.</summary>
    public static int? BodyEnd(IReadOnlyList<string> masked, int line, int after)
    {
        var rest = masked[line][after..];
        var brace = rest.IndexOf('{');

        if (brace < 0) return rest.Trim().Length > 0 ? line : line + 1 < masked.Count ? line + 1 : null;

        var depth = 0;

        for (var i = line; i < masked.Count; i++)
        {
            for (var c = i == line ? after + brace : 0; c < masked[i].Length; c++)
            {
                if (masked[i][c] == '{') depth++;
                else if (masked[i][c] == '}' && --depth == 0) return i;
            }
        }

        return null;
    }

    /// <summary>The file and line of a runtime exception's first frame in the program's own code.</summary>
    public static (SourceFile Source, int Number, string Line)? AtRuntime(LocalFixContext context, string type)
    {
        if (context.Error is not { LanguageId: "java" } error || error.ExceptionType != type) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!source.Path.EndsWith(".java", StringComparison.OrdinalIgnoreCase) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    /// <summary>A type written so it compiles here: its simple name when imported, its full name otherwise.</summary>
    public static string QualifiedName(SourceFile source, string package, string name) =>
        source.Lines.Any(l => Regex.IsMatch(l, $@"^\s*import\s+{Regex.Escape(package)}\.(?:{Regex.Escape(name)}|\*)\s*;"))
            ? name
            : $"{package}.{name}";

    /// <summary>Every Java file in the program's folder, the one the error named first.</summary>
    public static IEnumerable<SourceFile> SourceFiles(LocalFixContext context)
    {
        if (context.SourceRoot is not { } root || !Directory.Exists(root)) yield break;

        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 6, IgnoreInaccessible = true };

        foreach (var path in Directory.EnumerateFiles(root, "*.java", options).Take(300))
            if (SourceFile.Read(path) is { } source) yield return source;
    }
}
