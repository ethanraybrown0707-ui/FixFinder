using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// The example each guide shows - the code written the right way - read by the logic checks: a logic check's guide's example, in
/// every kind of file the check reads that the example is written for, holds nothing that check or any other finds, and nor does
/// the example of any guide to an error a language's tools report.
/// </summary>
/// <remarks>
/// A guide that shows, as the way to write it, code its own check calls a mistake contradicts itself - and shows that the check
/// would find a mistake in a program put right as the guide says. These tests found two: fgets read and cut at its newline
/// before strcmp, and a number's line read and thrown away before nextLine, were both still found. One guide can serve a check
/// that reads several languages and show only one of them; a file of a language the example is plainly not written in - as
/// PastedCode.LanguageOf tells - is passed over.
/// </remarks>
public class GuideExampleTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string ExampleFor(string checkId, string extension) =>
        Guidebook.For("example" + extension, FindingKind.Logic, checkId).Example ?? "";

    /// <summary>Whether code written in one language is code a file of another can hold: C in a C++ file, JavaScript in a TypeScript one.</summary>
    private static bool Holds(CodeLanguage file, CodeLanguage written) =>
        file == written || (file == CodeLanguage.Cpp && written == CodeLanguage.C) || (file == CodeLanguage.TypeScript && written == CodeLanguage.JavaScript);

    /// <summary>Each logic check, and each kind of file it reads that its guide's example is written for.</summary>
    public static TheoryData<string, string> EveryCheckOnItsOwnGuidesExample()
    {
        var cases = new TheoryData<string, string>();
        var checksAndFiles = LogicPatterns.All
            .SelectMany(check => check.Extensions.Select(extension => (check.Id, Extension: extension)))
            .Distinct()
            .OrderBy(pair => pair.Id, StringComparer.Ordinal).ThenBy(pair => pair.Extension, StringComparer.Ordinal);

        foreach (var (checkId, extension) in checksAndFiles)
        {
            var example = ExampleFor(checkId, extension);
            if (example.Trim().Length == 0) continue;

            var fileLanguage = CodeLanguage.All.FirstOrDefault(language => !language.IsAny && language.Extensions.Contains(extension));
            var writtenIn = PastedCode.LanguageOf(example);
            if (fileLanguage is not null && writtenIn is not null && !Holds(fileLanguage, writtenIn)) continue;

            cases.Add(checkId, extension);
        }
        return cases;
    }

    /// <summary>The example saved as a file of this kind - Main.java for Java, whose file is named for its class.</summary>
    private SourceFile Saved(string example, string extension)
    {
        var path = Path.Combine(_temp.Path, (extension == ".java" ? "Main" : "example") + extension);
        File.WriteAllText(path, example.ReplaceLineEndings("\n") + "\n");
        return SourceFile.Read(path)!;
    }

    private static string Listed(IEnumerable<LogicFinding> found) => string.Join("; ", found.Select(finding => $"line {finding.Line} {finding.PatternId}: {finding.Message}"));

    [Theory]
    [MemberData(nameof(EveryCheckOnItsOwnGuidesExample))]
    public void AGuidesExampleIsNotFoundByTheCheckItExplains(string checkId, string extension)
    {
        var source = Saved(ExampleFor(checkId, extension), extension);

        var found = LogicPatterns.All.Where(check => check.Id == checkId && check.Extensions.Contains(extension)).SelectMany(check => check.Find(source)).ToList();

        Assert.True(found.Count == 0, Listed(found));
    }

    [Theory]
    [MemberData(nameof(EveryCheckOnItsOwnGuidesExample))]
    public void AGuidesExampleHoldsNoMistakeAnyCheckFinds(string checkId, string extension)
    {
        var found = LogicPatterns.Scan(Saved(ExampleFor(checkId, extension), extension)).Where(finding => finding.Kind != FindingKind.Style).ToList();

        Assert.True(found.Count == 0, Listed(found));
    }

    /// <summary>
    /// Each language's guides to the errors its tools report, by a name for the table, and the kind of file its examples are
    /// written as - for C and C++, which share theirs, the one the example's own code says.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (IReadOnlyList<GuideEntry> Table, string? Extension)> ErrorGuideTables =
        new Dictionary<string, (IReadOnlyList<GuideEntry>, string?)>
        {
            ["Python"] = (PythonGuides.All, ".py"), ["Java"] = (JavaGuides.All, ".java"), ["Java's analyses"] = (AnalysisGuides.Java, ".java"),
            ["C#"] = (CSharpGuides.All, ".cs"), ["C#'s analyses"] = (AnalysisGuides.CSharp, ".cs"), ["C and C++"] = (NativeGuides.All, null),
            ["C and C++'s analyses"] = (AnalysisGuides.Native, null), ["JavaScript"] = (JavaScriptGuides.All, ".js"),
            ["JavaScript's analyses"] = (AnalysisGuides.JavaScript, ".js"), ["Go"] = (GoGuides.All, ".go"), ["Go's analyses"] = (AnalysisGuides.Go, ".go"),
            ["Scala"] = (ScalaGuides.All, ".scala"), ["OCaml"] = (OCamlGuides.All, ".ml"), ["Rust"] = (RustGuides.All, ".rs"), ["Ruby"] = (RubyGuides.All, ".rb"),
            ["PHP"] = (PhpGuides.All, ".php"), ["Lua"] = (LuaGuides.All, ".lua"), ["Perl"] = (PerlGuides.All, ".pl"), ["Dart"] = (DartGuides.All, ".dart"),
            ["Fortran"] = (FortranGuides.All, ".f90"), ["PowerShell"] = (PowerShellGuides.All, ".ps1"), ["Elixir"] = (ElixirGuides.All, ".exs"),
            ["Erlang"] = (ErlangGuides.All, ".erl"), ["F#"] = (FSharpGuides.All, ".fsx"), ["TypeScript"] = (TypeScriptGuides.All, ".ts"),
        };

    /// <summary>Each error guide that shows an example: its table, and its number in the table.</summary>
    public static TheoryData<string, int> EveryErrorGuidesExample()
    {
        var guides = new TheoryData<string, int>();
        foreach (var (name, (table, _)) in ErrorGuideTables)
            for (var number = 1; number <= table.Count; number++)
                if (table[number - 1].Guide.Example.Trim().Length > 0)
                    guides.Add(name, number);
        return guides;
    }

    [Theory]
    [MemberData(nameof(EveryErrorGuidesExample))]
    public void AnErrorGuidesExampleHoldsNoMistakeAnyCheckFinds(string table, int number)
    {
        var (guides, extension) = ErrorGuideTables[table];
        var example = guides[number - 1].Guide.Example;

        var found = LogicPatterns.Scan(Saved(example, extension ?? (PastedCode.LanguageOf(example) == CodeLanguage.Cpp ? ".cpp" : ".c")))
            .Where(finding => finding.Kind != FindingKind.Style)
            .ToList();

        Assert.True(found.Count == 0, Listed(found));
    }
}
