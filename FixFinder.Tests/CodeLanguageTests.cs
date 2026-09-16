using FixFinder.Core;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// Saying which language a program is in: what it narrows, and what it must never lose.
/// </summary>
/// <remarks>
/// Choosing a language may only ever set aside what could not have applied to that language's
/// programs anyway. So beyond checking what each choice keeps, these pin down that no rule belongs
/// to nothing - a rule no button reaches would be a fix that silently stopped being offered - and
/// that a real run gives the same answer either way.
/// </remarks>
public class CodeLanguageTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void EveryRuleBelongsToAtLeastOneLanguage()
    {
        var unreached = LocalFixEngine.Rules
            .Where(rule => !CodeLanguage.All.Where(l => !l.IsAny).Any(l => l.Reads(rule)))
            .Select(rule => rule.Id)
            .ToList();

        Assert.Empty(unreached);
    }

    [Theory]
    [InlineData("Python", "python-", "java-")]
    [InlineData("Java", "java-", "js-")]
    [InlineData("C#", "csharp-", "c-")]
    [InlineData("C", "c-", "csharp-")]
    [InlineData("C++", "cpp-", "go-")]
    [InlineData("JavaScript", "js-", "java-")]
    [InlineData("Go", "go-", "python-")]
    public void ALanguageKeepsItsOwnRulesAndNoOthers(string name, string own, string other)
    {
        var language = CodeLanguage.All.Single(l => l.Name == name);
        var kept = LocalFixEngine.Rules.Where(language.Reads).ToList();

        Assert.NotEmpty(kept);
        Assert.Equal(LocalFixEngine.Rules.Count(r => r.Id.StartsWith(own, StringComparison.Ordinal)),
            kept.Count(r => r.Id.StartsWith(own, StringComparison.Ordinal)));
        Assert.DoesNotContain(kept, r => r.Id.StartsWith(other, StringComparison.Ordinal));
    }

    [Fact]
    public void AnyLanguageKeepsEverything()
    {
        Assert.All(LocalFixEngine.Rules, rule => Assert.True(CodeLanguage.Any.Reads(rule)));
        Assert.Equal(new Core.Parsing.ParserRegistry().Parsers.Count, CodeLanguage.Any.Parsers().Parsers.Count);
    }

    [Theory]
    [InlineData("Python", new[] { "python", "generic" })]
    [InlineData("Java", new[] { "java", "generic" })]
    [InlineData("C#", new[] { "csharp", "msvc", "generic" })]
    [InlineData("C", new[] { "gcc", "msvc", "generic" })]
    [InlineData("JavaScript", new[] { "node", "generic" })]
    [InlineData("Go", new[] { "go", "generic" })]
    public void ALanguageReadsOutputWithItsOwnParsersAndTheGenericOne(string name, string[] parsers)
    {
        var language = CodeLanguage.All.Single(l => l.Name == name);

        Assert.Equal(parsers.Order(), language.Parsers().Parsers.Select(p => p.LanguageId).Distinct().Order());
    }

    [Theory]
    [InlineData("app.py", "Python")]
    [InlineData("Main.java", "Java")]
    [InlineData("Program.cs", "C#")]
    [InlineData("main.cpp", "C++")]
    [InlineData("main.c", "C")]
    [InlineData("index.mjs", "JavaScript")]
    [InlineData("main.go", "Go")]
    public void ASourceFileIsKnownByItsExtension(string file, string language) =>
        Assert.Equal(language, CodeLanguage.Of(file)?.Name);

    [Fact]
    public void AFileOfAnotherLanguageIsRefusedButAnExecutableIsNot()
    {
        Assert.Contains("is a Python file, but Java is selected", CodeLanguage.Java.Refuses(@"C:\work\app.py"));
        Assert.Null(CodeLanguage.Java.Refuses(@"C:\work\Main.java"));
        Assert.Null(CodeLanguage.Java.Refuses(@"C:\work\built.exe"));
        Assert.Null(CodeLanguage.Any.Refuses(@"C:\work\app.py"));
    }

    [Fact]
    public void ThePickerOffersTheLanguagesFilesFirst()
    {
        var filter = CodeLanguage.Go.FileDialogFilter("Everything (*.*)|*.*");

        Assert.StartsWith("Go files (*.go)|*.go|", filter);
        Assert.EndsWith("Everything (*.*)|*.*", filter);
        Assert.Equal("Everything (*.*)|*.*", CodeLanguage.Any.FileDialogFilter("Everything (*.*)|*.*"));
    }

    [Fact]
    public async Task APythonProgramGetsTheSameAnswerWithPythonChosenAsWithAnyLanguage()
    {
        if (TargetFactory.FindOnPath("python") is null && TargetFactory.FindOnPath("py") is null) return;

        var file = Path.Combine(_temp.Path, "app.py");
        // A mistake only a rule fixes - Python suggests nothing for it itself - so the rules chosen by the language decide the answer.
        await File.WriteAllTextAsync(file, "count = 3\nif count > 2\n    print(count)\n");

        async Task<SessionOutcome> Run(CodeLanguage language)
        {
            using var http = new FixFinderHttpClient();
            var session = new FixFinderSession(http, new FixSourceRegistry()) { Language = language };

            return await session.RunAsync(TargetFactory.FromFile(file), new SearchBudget(Cache: CacheMode.CacheOnly), CancellationToken.None);
        }

        var any = await Run(CodeLanguage.Any);
        var python = await Run(CodeLanguage.Python);

        Assert.Equal(any.Result, python.Result);
        Assert.Equal(any.Headline, python.Headline);
        Assert.Equal(any.Error?.Summary, python.Error?.Summary);
        Assert.Equal(any.Best?.Id, python.Best?.Id);
        Assert.Equal("local:python-expected-colon", python.Best?.Id);
    }
}
