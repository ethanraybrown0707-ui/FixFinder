using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// The example each logic check's guide shows - the code written the right way - read by that same check, in every kind of file
/// the check reads that the example is written for: the check finds nothing in it.
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

    [Theory]
    [MemberData(nameof(EveryCheckOnItsOwnGuidesExample))]
    public void AGuidesExampleIsNotFoundByTheCheckItExplains(string checkId, string extension)
    {
        var path = Path.Combine(_temp.Path, "example" + extension);
        File.WriteAllText(path, ExampleFor(checkId, extension).ReplaceLineEndings("\n") + "\n");
        var source = SourceFile.Read(path)!;

        var found = LogicPatterns.All.Where(check => check.Id == checkId && check.Extensions.Contains(extension)).SelectMany(check => check.Find(source)).ToList();

        Assert.True(found.Count == 0, string.Join("; ", found.Select(finding => $"line {finding.Line}: {finding.Message}")));
    }
}
