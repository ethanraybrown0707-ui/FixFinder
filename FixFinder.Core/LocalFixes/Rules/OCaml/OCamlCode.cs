using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the OCaml rules share: reading what OCaml said, and the exact text of the code it pointed at.</summary>
internal static partial class OCamlCode
{
    /// <summary>
    /// The two types of a type error, quoted as OCaml 5.2 quotes them or not, as earlier OCamls wrote them: "This expression
    /// has type "float" but an expression was expected of type "int"".
    /// </summary>
    [GeneratedRegex(@"has type\s+""?(?<found>[\w.' ]+?)""?\s+but an expression was expected of type\s+""?(?<expected>[\w.' ]+?)""?(?:\s|$)")]
    public static partial Regex TypeMismatch();

    public static bool IsOCaml(SourceFile source) => OCamlProgram.IsOCaml(source.Path);

    /// <summary>The message of an OCaml compile error, when it matches.</summary>
    public static Match? CompileMessage(ParsedError error, Regex message) =>
        error.LanguageId == "ocaml" && error.ExceptionType == "compile error" && message.Match(error.Message ?? "") is { Success: true } match ? match : null;

    /// <summary>Whether an OCaml program stopped with one of these exceptions.</summary>
    public static bool Raised(ParsedError error, params string[] exceptions) =>
        error.LanguageId == "ocaml" && exceptions.Contains(error.ExceptionType ?? "", StringComparer.Ordinal);

    /// <summary>The OCaml file, line number and line an error is at, or null when it is at none that can be read.</summary>
    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!IsOCaml(source) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    /// <summary>
    /// What OCaml pointed at - the characters its message names, on one line - with where it starts and ends in the line;
    /// null when the place runs over several lines, or past the end of the line.
    /// </summary>
    public static (int Start, int End, string Text)? Pointed(ParsedError error, string line)
    {
        if (error.RawText.Split('\n')[0].Contains(", lines ", StringComparison.Ordinal)) return null;
        if (OCamlCompileParser.SpanOf(error) is not { } span || span.End > line.Length || span.Start >= span.End) return null;

        return (span.Start, span.End, line[span.Start..span.End]);
    }
}
