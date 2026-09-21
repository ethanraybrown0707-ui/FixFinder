using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;

namespace FixFinder.Tests;

/// <summary>What the analysis finds is explained in the words and code of the language it was found in.</summary>
public class AnalysisGuideTests
{
    [Theory]
    [InlineData("App.py", "Using something that can be None", "if found is None:")]
    [InlineData("App.java", "Using something that can be null", "message.toUpperCase()")]
    [InlineData("App.cs", "Using something that can be null", "?? \"none\"")]
    [InlineData("App.go", "Reading something through a nil pointer", "node == nil")]
    [InlineData("App.js", "Using something that is null or undefined", "people.find")]
    [InlineData("App.c", "Going through a pointer that can be NULL", "numbers == NULL")]
    [InlineData("App.cpp", "Going through a pointer that can be NULL", "numbers == NULL")]
    public void NullIsNamedTheWayTheLanguageNamesIt(string file, string title, string example)
    {
        var guide = Guidebook.For(file, FindingKind.Runtime, "analysis-null-used");

        Assert.Equal(title, guide.Title);
        Assert.Contains(example, guide.Example);
    }

    [Theory]
    [InlineData("App.java", "ArithmeticException")]
    [InlineData("App.cs", "DivideByZeroException")]
    [InlineData("App.py", "Dividing by zero stops the program")]
    [InlineData("App.go", "integer divide by zero")]
    [InlineData("App.c", "undefined behaviour")]
    public void TheErrorIsTheOneTheLanguageRaises(string file, string error)
    {
        Assert.Contains(error, Guidebook.For(file, FindingKind.Runtime, "analysis-division-by-zero").WhyItMatters);
    }

    [Theory]
    [InlineData("App.java", "analysis-division-by-zero", "analysis-null-used", "analysis-index-out-of-range", "analysis-empty-collection", "analysis-not-a-number",
        "analysis-never-true", "analysis-always-true", "analysis-loop-never-runs", "analysis-assert-always-fails", "analysis-loop-never-ends")]
    [InlineData("App.cs", "analysis-division-by-zero", "analysis-null-used", "analysis-index-out-of-range", "analysis-empty-collection", "analysis-not-a-number",
        "analysis-never-true", "analysis-always-true", "analysis-loop-never-runs", "analysis-loop-never-ends")]
    public void EveryCheckTheLanguageCanFailHasAnExampleInThatLanguage(string file, params string[] checks)
    {
        foreach (var check in checks)
        {
            var example = Guidebook.For(file, FindingKind.Logic, check).Example ?? "";
            Assert.True(example.Contains(';') || example.Contains('{'), $"{check} in {file} has no example in that language: {example}");
        }
    }

    [Theory]
    [InlineData("analysis-use-after-free", "free(n)")]
    [InlineData("analysis-double-free", "buffer = NULL")]
    [InlineData("analysis-memory-leak", "free(numbers)")]
    [InlineData("analysis-dangling-pointer", "malloc(sizeof(int))")]
    [InlineData("analysis-uninitialised-read", "int total = 0")]
    public void WhatTheMemoryChecksFindIsExplainedInC(string check, string example)
    {
        var guide = Guidebook.For("App.c", FindingKind.Runtime, check);

        Assert.Contains(example, guide.Example);
        Assert.NotEqual("", guide.Title);
    }
}
