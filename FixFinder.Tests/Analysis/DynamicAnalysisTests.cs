using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Dynamic;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// Dynamic instrumentation, trace compression and the hybrid of the two with static analysis: a predicted failure is run
/// with the inputs that should cause it, and only a run that fails exactly as predicted makes the finding certain.
/// </summary>
public class DynamicAnalysisTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Confirmed(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, "sample.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        var findings = AbstractChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText());
        return await Confirmation.ConfirmAsync(findings, python, CancellationToken.None);
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3, 4, 5, 6, 7, 5, 6, 7, 5, 6, 7, 5, 8 }, "1-4, (5-7)×3, 5, 8")]
    [InlineData(new[] { 1, 2, 3, 4, 3, 4, 3, 4, 3, 2, 3, 4, 3, 4, 3, 4, 3, 2, 5 }, "1, (2, (3-4)×3, 3)×2, 2, 5")]
    [InlineData(new[] { 4, 9, 2 }, "4, 9, 2")]
    [InlineData(new int[0], "")]
    public void ALoopFoldsIntoOneGroupWithACount(int[] lines, string expected) =>
        Assert.Equal(expected, TraceCompression.Compress(lines));

    [Fact]
    public void ALongTraceIsCutShort()
    {
        var lines = Enumerable.Range(0, 400).Select(i => i * 2).ToList();

        Assert.EndsWith("…", TraceCompression.Compress(lines));
    }

    [Fact]
    public async Task APossibleDivisionIsRunWithItsWitnessAndBecomesCertain()
    {
        const string code = "def average(values):\n    total = 0\n    count = 0\n    print('adding up')\n    for v in values:\n        total += v\n        count += 1\n    return total / count\n\n\nprint(average([2, 4, 6]))\n";
        if (await Confirmed(code) is not { } findings) return;

        var division = Assert.Single(findings);
        Assert.Equal(Confidence.Certain, division.Confidence);
        Assert.Equal("Running `average(values=[])` stopped with ZeroDivisionError: division by zero on line 8, where `total` was 0 and `count` was 0. " +
                     "Lines it ran: 1-5, 8.", division.Confirmation);
    }

    [Fact]
    public async Task TopLevelCodeIsRunWithWhatShouldBeTyped()
    {
        if (await Confirmed("count = int(input())\nprint(100 / count)\n") is not { } findings) return;

        var division = Assert.Single(findings);
        Assert.Equal(Confidence.Certain, division.Confidence);
        Assert.StartsWith("Running the program, typing `0` stopped with ZeroDivisionError", division.Confirmation);
    }

    [Fact]
    public async Task ARunThatDoesNotFailAsPredictedChangesNothing()
    {
        const string code = "def share(total, people):\n    count = len(people)\n    return total // count\n";
        if (await Confirmed(code) is not { } findings) return;

        var division = Assert.Single(findings);
        Assert.Null(division.Confirmation);
        Assert.Equal(Confidence.Possible, division.Confidence);
    }

    [Fact]
    public async Task AMethodIsNotCalledWithoutItsObject()
    {
        const string code = "class Stats:\n    def average(self, values):\n        count = 0\n        for v in values:\n            count += 1\n        return 10 / count\n";
        if (await Confirmed(code) is not { } findings) return;

        Assert.Null(Assert.Single(findings).Confirmation);
    }
}
