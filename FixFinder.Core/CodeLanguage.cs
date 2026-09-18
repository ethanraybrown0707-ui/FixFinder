using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Core;

/// <summary>
/// The language a program is written in, when the person running FixFinder has said so.
/// </summary>
/// <remarks>
/// Left to itself FixFinder reads every program's output with every parser it has and offers it to
/// every rule, because it cannot know in advance what a program is. Saying which language it is
/// narrows both: only that language's parsers read the output - plus the generic one, which reads any
/// <c>file:line</c> - and only that language's rules propose fixes. The file picker offers only its
/// files, and a file of another language is refused rather than run.
/// <para>
/// <see cref="Any"/> keeps the old behaviour, and is what an executable or a batch file needs unless
/// the person knows what it was built from.
/// </para>
/// </remarks>
public sealed record CodeLanguage(
    string Name,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<string> ParserIds,
    IReadOnlyList<string> RulePrefixes)
{
    public static CodeLanguage Any { get; } = new("Any language", [], [], []);

    public static CodeLanguage Python { get; } = new("Python", [".py", ".pyw"], ["python"], ["python-"]);

    public static CodeLanguage Java { get; } = new("Java", [".java", ".jar"], ["java"], ["java-"]);

    // C and C++ share their compilers, so they share parsers - and, because C++ programs make C's
    // mistakes too, the rules of both.
    public static CodeLanguage C { get; } = new("C", [".c"], ["gcc", "msvc"], ["c-", "cpp-"]);

    public static CodeLanguage Cpp { get; } = new("C++", [".cpp", ".cc", ".cxx", ".c++"], ["gcc", "msvc"], ["cpp-", "c-"]);

    // A C# compile error comes through the MSVC-style parser, which reads CS#### codes; a crash through the .NET one.
    public static CodeLanguage CSharp { get; } = new("C#", [".cs", ".csproj", ".sln"], ["csharp", "msvc"], ["csharp-"]);

    public static CodeLanguage JavaScript { get; } = new("JavaScript", [".js", ".mjs", ".cjs"], ["node"], ["js-"]);

    public static CodeLanguage Go { get; } = new("Go", [".go"], ["go"], ["go-"]);

    /// <summary>Every choice, in the order the window shows them.</summary>
    public static IReadOnlyList<CodeLanguage> All { get; } = [Any, Python, Java, C, Cpp, CSharp, JavaScript, Go];

    public bool IsAny => ParserIds.Count == 0;

    /// <summary>The parsers that read this language's output, and the generic one.</summary>
    public ParserRegistry Parsers() => IsAny
        ? new ParserRegistry()
        : new ParserRegistry(new ParserRegistry().Parsers.Where(p => p.LanguageId == "generic" || ParserIds.Contains(p.LanguageId)));

    /// <summary>Whether a rule proposes fixes for this language.</summary>
    /// <remarks>Logic patterns read every language, each only its own files, so they are never filtered out here.</remarks>
    public bool Reads(ILocalFixRule rule) =>
        IsAny || rule.Id.StartsWith("logic-", StringComparison.Ordinal) || RulePrefixes.Any(prefix => rule.Id.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>The language a source file is written in, or null for a file that could be anything, such as an .exe.</summary>
    public static CodeLanguage? Of(string path)
    {
        var extension = System.IO.Path.GetExtension(path);

        return All.FirstOrDefault(l => l.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Why a file cannot be run as this language, or null when it can: an .exe can be anything, but a
    /// .py file is Python whatever button is pressed.
    /// </summary>
    public string? Refuses(string path) =>
        IsAny || Of(path) is not { } actual || actual == this
            ? null
            : $"{System.IO.Path.GetFileName(path)} is a {actual.Name} file, but {Name} is selected.";

    /// <summary>The file picker's filter: this language's files first, then everything FixFinder can run.</summary>
    public string FileDialogFilter(string everything) => IsAny
        ? everything
        : $"{Name} files ({string.Join(";", Extensions.Select(e => "*" + e))})|{string.Join(";", Extensions.Select(e => "*" + e))}|{everything}";
}
