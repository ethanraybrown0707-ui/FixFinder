using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;

namespace FixFinder.Tests;

/// <summary>
/// Checking again after an edit looks again only at what the edit could have changed - and never gives a different
/// answer from looking at everything afresh. Every test compares the two.
/// </summary>
public class AnalysisCacheTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Program = """
        def half(values):
            return len(values) // 2


        def middle(values):
            count = half(values)
            return 100 / count


        def shout(text):
            return text.upper()


        print(middle([1, 2, 3, 4]))
        print(shout("hi"))
        """;

    /// <summary>What the analyses find in this code, with and without the cache, or null when Python is not installed.</summary>
    private async Task<(IReadOnlyList<string> Cached, IReadOnlyList<string> Fresh)?> CheckAsync(string code, AnalysisCache cache)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, "numbers.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));

        var program = await PythonFrontend.ReadAsync([path], python);
        var cached = AbstractChecks.Run(program, new SourceText(), cache);
        var fresh = AbstractChecks.Run(program, new SourceText());

        return (Described(cached), Described(fresh));
    }

    private static List<string> Described(IReadOnlyList<AnalysisFinding> findings) =>
        findings.Select(f => $"{f.CheckId} {f.Span.Line}:{f.Span.Column} {f.Confidence} {f.Message}").ToList();

    [Fact]
    public async Task AnUnchangedProgramIsTakenWhollyFromTheCache()
    {
        var cache = new AnalysisCache();
        if (await CheckAsync(Program, cache) is not { } first) return;

        Assert.Equal(0, cache.LastRun.Reused);
        Assert.Contains(first.Cached, f => f.StartsWith("analysis-division-by-zero 7:", StringComparison.Ordinal));

        var second = (await CheckAsync(Program, cache))!.Value;

        Assert.Equal(0, cache.LastRun.Analysed);
        Assert.True(cache.LastRun.Reused >= 3, $"reused {cache.LastRun.Reused}");
        Assert.Equal(first.Fresh, second.Cached);
    }

    /// <summary>
    /// Making half never return 0 takes away middle's division by zero, so middle - which calls it - is analysed again,
    /// while shout, which neither calls nor is called by either, is reused.
    /// </summary>
    [Fact]
    public async Task ChangingAFunctionAnalysesItAndWhatCallsItAgain()
    {
        var cache = new AnalysisCache();
        if (await CheckAsync(Program, cache) is null) return;

        var edited = Program.Replace("return len(values) // 2", "return len(values) // 2 + 1");
        var after = (await CheckAsync(edited, cache))!.Value;

        Assert.Equal(after.Fresh, after.Cached);
        Assert.DoesNotContain(after.Cached, f => f.StartsWith("analysis-division-by-zero", StringComparison.Ordinal));
        Assert.Equal(1, cache.LastRun.Reused);
    }

    /// <summary>A line added above a function moves every line number below it, so nothing below is reused with the old ones.</summary>
    [Fact]
    public async Task ALineAddedAboveMovesEverythingBelowItOn()
    {
        var cache = new AnalysisCache();
        if (await CheckAsync(Program, cache) is null) return;

        var after = (await CheckAsync("# numbers\n" + Program, cache))!.Value;

        Assert.Equal(after.Fresh, after.Cached);
        Assert.Contains(after.Cached, f => f.StartsWith("analysis-division-by-zero 8:", StringComparison.Ordinal));
        Assert.Equal(0, cache.LastRun.Reused);
    }

    /// <summary>Code at the top level is part of what every function's analysis depends on.</summary>
    [Fact]
    public async Task ChangingCodeOutsideEveryFunctionAnalysesEverythingAgain()
    {
        var cache = new AnalysisCache();
        if (await CheckAsync(Program, cache) is null) return;

        var after = (await CheckAsync(Program.Replace("print(shout(\"hi\"))", "LIMIT = 10\nprint(shout(\"hi\"))"), cache))!.Value;

        Assert.Equal(after.Fresh, after.Cached);
        Assert.Equal(0, cache.LastRun.Reused);
    }

    /// <summary>A function changed below the others leaves the ones above it, which neither call it nor move, as they were.</summary>
    [Fact]
    public async Task AFunctionThatCallsNothingChangedIsReused()
    {
        var cache = new AnalysisCache();
        if (await CheckAsync(Program, cache) is null) return;

        var after = (await CheckAsync(Program.Replace("return text.upper()", "return text.lower()"), cache))!.Value;

        Assert.Equal(after.Fresh, after.Cached);
        Assert.Equal(2, cache.LastRun.Reused);
    }
}
