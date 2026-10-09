using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the Scala rules share: reading what Scala said, and finding the line of code it is about.</summary>
internal static partial class ScalaCode
{
    /// <summary>The types a beginner's numbers and text are, whose name in a message can be taken at its word.</summary>
    public static readonly HashSet<string> SimpleTypes = new(StringComparer.Ordinal) { "Int", "Long", "Short", "Byte", "Double", "Float", "Char", "Boolean", "String" };

    public static readonly HashSet<string> NumberTypes = new(StringComparer.Ordinal) { "Int", "Long", "Short", "Byte", "Double", "Float" };

    public static bool IsScala(SourceFile source) => ScalaProgram.IsScala(source.Path);

    /// <summary>The message of a Scala compile error, when it matches - for errors from Scala's compilers alone.</summary>
    public static Match? CompileMessage(ParsedError error, Regex message) =>
        error.LanguageId == "scala" && error.ExceptionType == "compile error" && message.Match(error.Message ?? "") is { Success: true } match ? match : null;

    /// <summary>Whether a crash is the JVM exception named - <c>java.lang.ArithmeticException</c> - in a Scala program.</summary>
    public static bool Crashed(ParsedError error, params string[] exceptionTypes) =>
        error.LanguageId == "scala" && exceptionTypes.Contains(error.ExceptionType ?? "", StringComparer.Ordinal);

    /// <summary>The Scala file, line number and line of code an error is at, or null when it is at none that can be read.</summary>
    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!IsScala(source) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    /// <summary>
    /// Where in a line a name stands as a whole word, outside strings and comments: at the column given when it is there,
    /// else the one place it is - or -1 when it is at none, or at several with nothing to choose between them.
    /// </summary>
    public static int WordAt(string line, string name, int? column)
    {
        var masked = CodeText.Mask(line, Syntax.Scala);
        var places = Regex.Matches(masked, $@"(?<![\w$]){Regex.Escape(name)}(?![\w$])").Select(match => match.Index).ToList();

        if (column is { } at && places.Contains(at - 1)) return at - 1;
        return places.Count == 1 ? places[0] : -1;
    }
}
