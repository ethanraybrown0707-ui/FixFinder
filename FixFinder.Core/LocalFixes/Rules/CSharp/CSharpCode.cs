using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

internal static partial class CSharpCode
{
    public static bool HasErrorCode(LocalFixContext context, params string[] codes) =>
        context.Error.LanguageId == "msvc" &&
        context.Error.ErrorCode is { } code &&
        code.StartsWith("CS", StringComparison.Ordinal) &&
        (codes.Length == 0 || codes.Contains(code));

    /// <summary>The C# file, line number, line text and 0-based column of the error, or null.</summary>
    public static (SourceFile Source, int Number, string Line, int Index)? Locate(LocalFixContext context)
    {
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!source.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || source.Line(number) is not { } line) return null;

        var index = frame.Column is { } column ? Math.Clamp(column - 1, 0, line.Length) : -1;

        return (source, number, line, index);
    }

    public static readonly HashSet<string> Keywords = new(
        ("abstract as base bool break byte case catch char checked class const continue decimal default delegate do " +
         "double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface " +
         "internal is lock long namespace new null object operator out override params private protected public readonly " +
         "ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint " +
         "ulong unchecked unsafe ushort using virtual void volatile while var async await dynamic nameof record get set " +
         "init value yield when where").Split(' '),
        StringComparer.Ordinal);

    /// <summary>Where an expression starting at <paramref name="start"/> ends: a semicolon, comma or unmatched bracket.</summary>
    public static int ExpressionEnd(string masked, int start)
    {
        var depth = 0;

        for (var i = start; i < masked.Length; i++)
        {
            var c = masked[i];

            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0) return i;
                depth--;
            }
            else if (c is ';' or ',' && depth == 0) return i;
        }

        return masked.Length;
    }

    [GeneratedRegex(@"[+\-*/%<>=!&|?]")]
    public static partial Regex OperatorCharacter();

    public static bool HasNoOperators(string expression) => !OperatorCharacter().IsMatch(CodeText.Mask(expression, Syntax.CLike));

    public static int TypeDeclarationLine(IReadOnlyList<string> masked, string name) =>
        Enumerable.Range(0, masked.Count)
            .FirstOrDefault(i => Regex.IsMatch(masked[i], $@"\b(?:class|struct|record|interface|enum)\s+{Regex.Escape(name)}\b"), -1);

    /// <summary>The lines one level inside a type's braces.</summary>
    public static IEnumerable<int> MemberLines(IReadOnlyList<string> masked, int declaration)
    {
        var depths = Brackets.BraceDepths(masked);
        var inside = depths[declaration] + 1;

        for (var i = declaration + 1; i < masked.Count; i++)
        {
            if (i == declaration + 1 && masked[i].Trim() == "{") continue;
            if (depths[i] < inside) yield break;
            if (depths[i] == inside) yield return i;
        }
    }

    /// <summary>True when an expression needs brackets before a member access is added to its end.</summary>
    public static bool NeedsBrackets(string masked)
    {
        var depth = 0;

        foreach (var c in masked)
        {
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (depth == 0 && "+-*/%<>=!&|?: ".Contains(c)) return true;
        }

        return false;
    }
}
