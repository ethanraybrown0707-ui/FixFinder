using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Symbolic;
using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// Symbolic execution on top of abstract interpretation: the inputs that make a line fail, false alarms dropped once
/// every path has been followed, failures some path cannot avoid, loops bounded from the code, and loops that never end.
/// </summary>
public class SymbolicExecutionTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Python(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, "sample.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return AbstractChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText());
    }

    private async Task<IrProgram> ReadCSharp(string code)
    {
        var path = Path.Combine(_temp.Path, "Sample.cs");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return await CSharpFrontend.ReadAsync([path]);
    }

    private async Task<IReadOnlyList<AnalysisFinding>> CSharp(string code) => AbstractChecks.Run(await ReadCSharp(code), new SourceText());

    private static string Summary(IEnumerable<AnalysisFinding> findings) =>
        string.Join("; ", findings.Select(f => $"{f.CheckId} line {f.Span.Line} [{f.FoundBy}] {f.Message} / {f.Witness}"));

    [Fact]
    public async Task APossibleDivisionGetsTheInputThatMakesItFail()
    {
        if (await Python("def average(values):\n    total = 0\n    count = 0\n    for v in values:\n        total += v\n        count += 1\n    return total / count\n") is not { } findings) return;

        var division = Assert.Single(findings, f => f.CheckId == "analysis-division-by-zero");
        Assert.Equal("`values` is empty", division.Witness);
        Assert.Equal(SymbolicChecks.Confirmed, division.FoundBy);
    }

    [Fact]
    public async Task ANoneThatOnlySomePathsGiveIsNamedWithTheInputThatGivesIt()
    {
        var findings = await CSharp("class App\n{\n    static int NameLength(App person)\n    {\n        var name = person?.ToString();\n        return name.Length;\n    }\n}\n");

        var nullUse = Assert.Single(findings, f => f.CheckId == "analysis-null-used");
        Assert.Equal("`person` is null", nullUse.Witness);
    }

    [Fact]
    public async Task WhatSomeoneTypesIsTheWitnessForTopLevelCode()
    {
        if (await Python("count = int(input())\nprint(100 / count)\n") is not { } findings) return;

        var division = Assert.Single(findings, f => f.CheckId == "analysis-division-by-zero");
        Assert.Equal("the number typed at line 1 is 0", division.Witness);
    }

    [Fact]
    public async Task ADivisionGuardedByTheSameFlagTwiceIsNotAFalseAlarm()
    {
        const string code = "def scale(flag):\n    d = 0\n    if flag:\n        d = 2\n    if flag:\n        return 10 / d\n    return 0\n";
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task ANoneGuardedByTheSameConditionTwiceIsNotAFalseAlarm()
    {
        const string code = "def describe(kind):\n    label = None\n    if kind > 3:\n        label = 'big'\n    if kind > 5:\n        return label.upper()\n    return ''\n";
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task AFailureSomePathCannotAvoidIsReportedWhereTheAbstractValuesCouldNotSeeIt()
    {
        if (await Python("def gap(a, b):\n    if a == b:\n        return 1 / (a - b)\n    return 0\n") is not { } findings) return;

        var division = Assert.Single(findings);
        Assert.Equal(("analysis-division-by-zero", 3, SymbolicChecks.FoundBy, Confidence.Likely), (division.CheckId, division.Span.Line, division.FoundBy, division.Confidence));
        Assert.Contains("`a - b` is always 0", division.Message);
    }

    [Fact]
    public async Task AnIndexEqualToTheLengthIsCaughtInCSharp()
    {
        var findings = await CSharp("class App\n{\n    static int Last(int n)\n    {\n        var items = new int[n];\n        return items[n];\n    }\n}\n");

        var index = Assert.Single(findings, f => f.CheckId == "analysis-index-out-of-range");
        Assert.Equal(6, index.Span.Line);
        Assert.Equal(SymbolicChecks.FoundBy, index.FoundBy);
    }

    [Fact]
    public async Task ALoopWithAKnownBoundIsFollowedToTheEnd()
    {
        var program = await ReadCSharp("class App\n{\n    static int Sum()\n    {\n        int total = 0;\n        for (int i = 0; i < 10; i++) total += i;\n        return 100 / (total - 45);\n    }\n}\n");
        var function = program.Classes.Single().Methods.Single();

        var loop = Assert.Single(LoopBounds.Counted(function).Values);
        Assert.Equal(("i", 1), (loop.Counter, (int)loop.Step.Numerator));

        var report = new SymbolicExecutor(CfgBuilder.Build(function), SourceLanguage.CSharp, IrWalk.LocalNames(function, false), new HashSet<string>(),
            new Dictionary<string, IrType>()).Explore();

        Assert.True(report.Complete);
        Assert.True(report.Outcomes.Values.Single().Forced);
    }

    /// <summary>
    /// A list tested for truth before it is measured. True means it has items, so dividing by its length cannot fail on
    /// that path; the one symbol for its truth and the one for its length must never say "true, and empty".
    /// </summary>
    [Fact]
    public async Task AListThatIsTrueIsNeverEmpty()
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return;

        var path = Path.Combine(_temp.Path, "sample.py");
        await File.WriteAllTextAsync(path, "def share(items):\n    if items:\n        return 10 // len(items)\n    return 0\n");
        var function = (await PythonFrontend.ReadAsync([path], python)).Functions.Single(f => f.Name == "share");

        var report = new SymbolicExecutor(CfgBuilder.Build(function), SourceLanguage.Python, IrWalk.LocalNames(function), new HashSet<string>(),
            new Dictionary<string, IrType>()).Explore();

        var division = Assert.Single(report.Outcomes, outcome => outcome.Key.Item1 == "analysis-division-by-zero");
        Assert.False(division.Value.CanFail);
    }

    [Fact]
    public async Task ALoopWithNoBoundLeavesTheReportIncompleteSoNothingIsDropped()
    {
        var program = await ReadCSharp("class App\n{\n    static int Count(int[] items)\n    {\n        int n = 0;\n        foreach (var item in items) n++;\n        return n;\n    }\n}\n");
        var function = program.Classes.Single().Methods.Single();

        var report = new SymbolicExecutor(CfgBuilder.Build(function), SourceLanguage.CSharp, IrWalk.LocalNames(function, false), new HashSet<string>(),
            new Dictionary<string, IrType>()).Explore();

        Assert.False(report.Complete);
    }

    [Theory]
    [InlineData("n = 5\nwhile n > 0:\n    print(n)\n", 2)]
    [InlineData("answer = input()\nwhile answer != 'q':\n    line = input()\n", 2)]
    public async Task AWhileLoopWhoseConditionNeverChangesNeverEnds(string code, int line)
    {
        if (await Python(code) is not { } findings) return;

        var endless = Assert.Single(findings, f => f.CheckId == "analysis-loop-never-ends");
        Assert.Equal(line, endless.Span.Line);
    }

    [Fact]
    public async Task AForLoopThatStepsTheWrongVariableNeverEnds()
    {
        var findings = await CSharp("class App\n{\n    static void Run()\n    {\n        int j = 0;\n        for (int i = 0; i < 10; j++) System.Console.WriteLine(i);\n    }\n}\n");

        var endless = Assert.Single(findings, f => f.CheckId == "analysis-loop-never-ends");
        Assert.Contains("`i`", endless.Message);
    }

    [Theory]
    [InlineData("n = 5\nwhile n > 0:\n    n -= 1\n")]
    [InlineData("items = [1, 2]\nwhile items:\n    items.pop()\n")]
    [InlineData("running = True\nwhile running:\n    if input() == 'q':\n        break\n")]
    [InlineData("while True:\n    print('serving')\n")]
    [InlineData("n = 0\nwhile n > 0:\n    print(n)\n")]
    public async Task LoopsThatCanEndAreLeftAlone(string code)
    {
        if (await Python(code) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-loop-never-ends");
    }
}
