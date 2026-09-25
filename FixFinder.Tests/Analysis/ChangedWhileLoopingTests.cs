using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A collection changed while a loop walks it, found by meaning rather than by how it is written: through another name
/// for the same list, and inside a function or method the loop calls - which needs that function's effect summary. Each
/// is checked beside the versions that are fine: walking a copy, leaving the loop at once, a helper that only reads.
/// </summary>
public class ChangedWhileLoopingTests(ITestOutputHelper output) : IDisposable
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

        var path = Path.Combine(_temp.Path, "loops.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Changes(AbstractChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Java(string code)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var path = Path.Combine(_temp.Path, code.Split("public class ")[1].Split(' ', '{')[0] + ".java");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Changes(AbstractChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>> CSharp(string code)
    {
        var path = Path.Combine(_temp.Path, "Program.cs");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Changes(AbstractChecks.Run(await CSharpFrontend.ReadAsync([path]), new SourceText()));
    }

    private List<AnalysisFinding> Changes(IEnumerable<AnalysisFinding> findings)
    {
        var changed = findings.Where(f => f.CheckId == ChangedWhileLooping.Rule).ToList();
        foreach (var finding in changed) output.WriteLine($"line {finding.Span.Line}: {finding.Message}");
        return changed;
    }

    private static string Summary(IEnumerable<AnalysisFinding> findings) => string.Join("; ", findings.Select(f => $"line {f.Span.Line}: {f.Message}"));

    private static void Says(string expected, AnalysisFinding finding) =>
        Assert.True(finding.Message.Contains(expected, StringComparison.Ordinal), $"expected \"{expected}\" in: {finding.Message}");

    [Fact]
    public async Task RemovingThroughAnotherNameForTheListSkipsItems()
    {
        const string code = """
            marks = [40, 55, 70, 30]
            passed = marks
            for mark in marks:
                if mark < 50:
                    passed.remove(mark)
            print(passed)
            """;
        if (await Python(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(5, found.Span.Line);
        Says("`passed.remove(mark)` removes from `marks` (`passed` is the same list as `marks`) while the for loop is walking over it, so the loop skips some of the items", found);
    }

    private const string DropHelper = """
        def drop(values, value):
            values.remove(value)

        marks = [40, 55, 70, 30]
        for mark in marks:
            if mark < 50:
                drop(marks, mark)
        print(marks)
        """;

    /// <summary>The loop only calls drop(); what drop() does to the list it is given is in drop()'s effect summary.</summary>
    [Fact]
    public async Task AFunctionTheLoopCallsRemovingFromTheListSkipsItems()
    {
        if (await Python(DropHelper) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(7, found.Span.Line);
        Says("`drop(marks, mark)` removes from `marks` at line 2 while the for loop is walking over it", found);
    }

    [Fact]
    public async Task WalkingACopyLetsTheLoopChangeTheList()
    {
        if (await Python(DropHelper.Replace("for mark in marks:", "for mark in list(marks):", StringComparison.Ordinal)) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task LeavingTheLoopRightAfterTheChangeIsFine()
    {
        var leaving = DropHelper.ReplaceLineEndings("\n").Replace("        drop(marks, mark)\n", "        drop(marks, mark)\n        break\n", StringComparison.Ordinal);
        Assert.NotEqual(DropHelper.ReplaceLineEndings("\n"), leaving);

        if (await Python(leaving) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task AFunctionThatOnlyReadsTheListChangesNothing()
    {
        const string code = """
            def show(values, value):
                print(len(values), value)

            marks = [40, 55, 70, 30]
            for mark in marks:
                show(marks, mark)
            """;
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>The loop walks self.items and calls a method that removes from self.items - the same object's field.</summary>
    [Fact]
    public async Task AMethodOnTheSameObjectRemovingFromTheFieldSkipsItems()
    {
        const string code = """
            class Basket:
                def __init__(self):
                    self.items = ["apple", "pear", "plum"]

                def discard(self, item):
                    self.items.remove(item)

                def empty(self):
                    for item in self.items:
                        self.discard(item)
            """;
        if (await Python(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(10, found.Span.Line);
        Says("`self.discard(item)` removes from `self.items` at line 6", found);
    }

    [Fact]
    public async Task ADictionaryChangedThroughAnotherNameStopsTheLoop()
    {
        const string code = """
            ages = {"ann": 30, "bob": 17}
            people = ages
            for name in ages:
                if ages[name] < 18:
                    people.pop(name)
            """;
        if (await Python(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Says("(`people` is the same dictionary as `ages`)", found);
        Says("RuntimeError: dictionary changed size during iteration", found);
    }

    /// <summary>Adding what a set already holds changes nothing, so adding to a set is not claimed to change it.</summary>
    [Fact]
    public async Task AddingToASetWhileWalkingItIsNotClaimed()
    {
        const string code = """
            seen = {1, 2, 3}
            same = seen
            for value in seen:
                same.add(value)
            """;
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task InJavaRemovingThroughAnotherNameCanThrow()
    {
        const string code = """
            import java.util.ArrayList;
            import java.util.List;

            public class Marks {
                public static void main(String[] args) {
                    List<Integer> marks = new ArrayList<>(List.of(40, 55, 70, 30));
                    List<Integer> failing = marks;
                    for (int mark : marks) {
                        if (mark < 50) {
                            failing.remove(Integer.valueOf(mark));
                        }
                    }
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(10, found.Span.Line);
        Says("can throw ConcurrentModificationException", found);
    }

    /// <summary>A field the loop walks, and a method of the same object that removes from it.</summary>
    [Fact]
    public async Task InJavaAMethodRemovingFromTheFieldTheLoopWalksCanThrow()
    {
        const string code = """
            import java.util.ArrayList;
            import java.util.List;

            public class Basket {
                private final List<String> items = new ArrayList<>();

                void discard(String item) {
                    items.remove(item);
                }

                void emptyAll() {
                    for (String item : items) {
                        discard(item);
                    }
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(13, found.Span.Line);
        Says("`discard(item)` removes from `items` at line 8", found);
    }

    [Fact]
    public async Task InCSharpAddingThroughAnotherNameThrows()
    {
        const string code = """
            using System.Collections.Generic;

            class Program
            {
                static void Main()
                {
                    var names = new List<string> { "ann", "bob" };
                    var alias = names;
                    foreach (var name in names)
                    {
                        alias.Add(name + "!");
                    }
                }
            }
            """;

        var found = Assert.Single(await CSharp(code));
        Assert.Equal(11, found.Span.Line);
        Says("throws InvalidOperationException", found);
    }
}
