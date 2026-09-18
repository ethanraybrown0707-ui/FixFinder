using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Core;

/// <summary>The language a program is written in, when the person running FixFinder has said so.</summary>
public sealed record CodeLanguage(
    string Name,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<string> ParserIds,
    IReadOnlyList<string> RulePrefixes)
{
    public static CodeLanguage Any { get; } = new("Any language", [], [], []);

    public static CodeLanguage Python { get; } = new("Python", [".py", ".pyw"], ["python"], ["python-"]);

    public static CodeLanguage Java { get; } = new("Java", [".java", ".jar"], ["java"], ["java-"]);

    public static CodeLanguage C { get; } = new("C", [".c"], ["gcc", "msvc"], ["c-", "cpp-"]);

    public static CodeLanguage Cpp { get; } = new("C++", [".cpp", ".cc", ".cxx", ".c++"], ["gcc", "msvc"], ["cpp-", "c-"]);

    public static CodeLanguage CSharp { get; } = new("C#", [".cs", ".csproj", ".sln"], ["csharp", "msvc"], ["csharp-"]);

    public static CodeLanguage JavaScript { get; } = new("JavaScript", [".js", ".mjs", ".cjs"], ["node"], ["js-"]);

    public static CodeLanguage Go { get; } = new("Go", [".go"], ["go"], ["go-"]);

    public static IReadOnlyList<CodeLanguage> All { get; } = [Any, Python, Java, C, Cpp, CSharp, JavaScript, Go];

    public bool IsAny => ParserIds.Count == 0;

    public ParserRegistry Parsers() => IsAny
        ? new ParserRegistry()
        : new ParserRegistry(new ParserRegistry().Parsers.Where(p => p.LanguageId == "generic" || ParserIds.Contains(p.LanguageId)));

    public bool Reads(ILocalFixRule rule) =>
        IsAny || rule.Id.StartsWith("logic-", StringComparison.Ordinal) || RulePrefixes.Any(prefix => rule.Id.StartsWith(prefix, StringComparison.Ordinal));

    public static CodeLanguage? Of(string path)
    {
        var extension = System.IO.Path.GetExtension(path);

        return All.FirstOrDefault(l => l.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    public string? Refuses(string path) =>
        IsAny || Of(path) is not { } actual || actual == this
            ? null
            : $"{System.IO.Path.GetFileName(path)} is a {actual.Name} file, but {Name} is selected.";

    public string FileDialogFilter(string everything) => IsAny
        ? everything
        : $"{Name} files ({string.Join(";", Extensions.Select(e => "*" + e))})|{string.Join(";", Extensions.Select(e => "*" + e))}|{everything}";
}
