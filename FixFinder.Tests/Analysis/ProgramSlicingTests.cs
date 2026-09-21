using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Slicing;

namespace FixFinder.Tests;

/// <summary>Backward slices: the lines that decide a value, and none of the lines that do not.</summary>
public class ProgramSlicingTests : IDisposable
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

    private async Task<IrFunction> CSharpMethod(string body)
    {
        var path = Path.Combine(_temp.Path, "Sample.cs");
        await File.WriteAllTextAsync(path, ("class App\n{\n" + body + "\n}\n").ReplaceLineEndings("\n"));
        return (await CSharpFrontend.ReadAsync([path])).Classes.Single().Methods.First(m => m.Name is "F" or "Share");
    }

    [Fact]
    public async Task ADivisionIsDecidedByItsCounterAndTheLoopNotByTheTotal()
    {
        const string code = "def average(values):\n    total = 0\n    count = 0\n    print('adding up')\n    for v in values:\n        total += v\n        count += 1\n    return total / count\n";
        if (await Python(code) is not { } findings) return;

        var division = Assert.Single(findings, f => f.CheckId == "analysis-division-by-zero");
        Assert.Equal([1, 3, 5, 7, 8], division.Slice);
    }

    [Fact]
    public async Task ANullIsDecidedByTheBranchThatMightHaveReplacedIt()
    {
        const string code = "def label(score):\n    message = None\n    bonus = score * 2\n    if score > 90:\n        message = 'top'\n    return message.upper()\n";
        if (await Python(code) is not { } findings) return;

        var nullUse = Assert.Single(findings, f => f.CheckId == "analysis-null-used");
        Assert.Equal([1, 2, 4, 5, 6], nullUse.Slice);
    }

    [Fact]
    public async Task AValueChangedInsideATryReachesItsHandler()
    {
        var method = await CSharpMethod("    static int F(int a)\n    {\n        int d = 1;\n        try\n        {\n            d = 0;\n            Work();\n        }\n        catch (System.Exception)\n        {\n            return 10 / d;\n        }\n        return d;\n    }\n    static void Work() { }");
        var graph = CfgBuilder.Build(method);
        var division = graph.Blocks.SelectMany(b => b.Terminator is Leave { Value: Binary found } ? [found] : Array.Empty<Binary>()).Single();

        var lines = new ProgramSlicer(graph).LinesDeciding(division.Span, ["d"]);

        Assert.Contains(5, lines);
        Assert.Contains(8, lines);
    }

    [Fact]
    public async Task AFieldWrittenThroughThisIsTheSameVariable()
    {
        var method = await CSharpMethod("    private int _count;\n    int Share(int total)\n    {\n        this._count = total - 1;\n        return total / _count;\n    }");
        var graph = CfgBuilder.Build(method);
        var division = graph.Blocks.SelectMany(b => b.Terminator is Leave { Value: Binary found } ? [found] : Array.Empty<Binary>()).Single();

        Assert.Equal([4, 6, 7], new ProgramSlicer(graph).LinesDeciding(division.Span, ["_count"]));
    }
}
