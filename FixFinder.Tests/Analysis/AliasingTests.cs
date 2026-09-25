using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Two names for one object: after b = a, a change made through b is a change to a. Checked in both directions - that
/// the change is seen through the other name, and that nothing is seen once the names part: one given a new value, or
/// sharing the object on only one of two ways through the code.
/// </summary>
public class AliasingTests(ITestOutputHelper output) : IDisposable
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

        var path = Path.Combine(_temp.Path, "aliases.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        var findings = AbstractChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText());
        foreach (var finding in findings) output.WriteLine($"line {finding.Span.Line} {finding.CheckId}: {finding.Message}");
        return findings;
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Java(string code)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var path = Path.Combine(_temp.Path, code.Split("public class ")[1].Split(' ', '{')[0] + ".java");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        var findings = AbstractChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText());
        foreach (var finding in findings) output.WriteLine($"line {finding.Span.Line} {finding.CheckId}: {finding.Message}");
        return findings;
    }

    private static string Summary(IEnumerable<AnalysisFinding> findings) =>
        string.Join("; ", findings.Select(f => $"{f.CheckId} line {f.Span.Line}: {f.Message}"));

    /// <summary>The list is emptied through b, and a is the same list.</summary>
    [Fact]
    public async Task ClearingOneNameEmptiesTheOther()
    {
        const string code = """
            objects = [1, 2, 3]
            a = objects
            b = a
            b.clear()
            print(a[0])
            """;
        if (await Python(code) is not { } findings) return;

        var found = Assert.Single(findings, f => f.Span.Line == 5 && f.CheckId == "analysis-index-out-of-range");
        Assert.Contains("`a` has 0 items here", found.Message, StringComparison.Ordinal);
        Assert.Contains("`b` and `objects` are the same list as `a`, so a change made through any of them is made to `a` too", found.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANameGivenANewListNoLongerSharesTheOld()
    {
        const string code = """
            a = [1, 2, 3]
            b = a
            b = []
            b.clear()
            print(a[2])
            """;
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>b is a on one way through and a new list on the other, so clearing b may or may not empty a - nothing certain.</summary>
    [Fact]
    public async Task SharingOnOnlyOneWayThroughIsNotSharing()
    {
        const string code = """
            import sys

            a = [1, 2, 3]
            if len(sys.argv) > 1:
                b = a
            else:
                b = [9]
            b.clear()
            print(a[0])
            """;
        if (await Python(code) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-index-out-of-range");
    }

    /// <summary>+= extends the shared list in place; what it holds afterwards is not guessed at, and a[1] is not called missing.</summary>
    [Fact]
    public async Task ExtendingASharedListInPlaceIsNotMistakenForANewList()
    {
        const string code = """
            a = [1]
            b = a
            b += [2]
            print(a[1])
            """;
        if (await Python(code) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-index-out-of-range");
    }

    /// <summary>Items added through the other name are there: popping from a after appending through b is fine.</summary>
    [Fact]
    public async Task AddingThroughOneNameFillsTheOther()
    {
        const string code = """
            a = []
            b = a
            b.append(1)
            print(a.pop())
            """;
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task InJavaTooClearingOneNameEmptiesTheOther()
    {
        const string code = """
            import java.util.ArrayList;
            import java.util.List;

            public class Shared {
                public static void main(String[] args) {
                    List<Integer> a = new ArrayList<>();
                    a.add(1);
                    List<Integer> b = a;
                    b.clear();
                    System.out.println(a.get(0));
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        var found = Assert.Single(findings, f => f.Span.Line == 10);
        Assert.Contains("`b` is the same list as `a`, so a change made through `b` is made to `a` too", found.Message, StringComparison.Ordinal);
    }
}
