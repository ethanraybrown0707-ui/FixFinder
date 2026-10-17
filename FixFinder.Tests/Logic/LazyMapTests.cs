using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A map on a line of its own, run for what its function does: found in Dart, Python and C#, with a loop that runs it, and left
/// alone where its result is used or its function only works a value out.
/// </summary>
public class LazyMapPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string fileName, string code, string patternId)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == patternId).ToList();
    }

    [Theory]
    [InlineData("main.dart", "logic-dart-map-never-run", "void main() {\n  var marks = [70, 80, 90];\n  marks.map((mark) => print(mark));\n}\n", new[] { "  for (var mark in marks) print(mark);" })]
    [InlineData("main.dart", "logic-dart-map-never-run", "void main() {\n  var marks = [70, 80, 90];\n  marks.map((mark) { print(mark); });\n}\n", new[] { "  for (var mark in marks) { print(mark); }" })]
    [InlineData("main.dart", "logic-dart-map-never-run", "void main() {\n  var total = 0;\n  marks.map((mark) => total += mark);\n}\n", new[] { "  for (var mark in marks) total += mark;" })]
    [InlineData("main.py", "logic-python-map-never-run", "def show():\n    pass\nmap(print, marks)\n", new[] { "for mark in marks:", "    print(mark)" })]
    [InlineData("main.py", "logic-python-map-never-run", "marks = [70]\nmark = 0\nmap(print, marks)\n", new[] { "for item in marks:", "    print(item)" })]
    [InlineData("main.py", "logic-python-map-never-run", "marks = [70]\n\nmap(lambda m: print(m * 2), marks)\n", new[] { "for m in marks:", "    print(m * 2)" })]
    [InlineData("main.cs", "logic-csharp-select-never-run", "var marks = new[] { 70, 80, 90 };\n\nmarks.Select(mark => { Console.WriteLine(mark); return mark; });\n", new[] { "foreach (var mark in marks) { Console.WriteLine(mark); }" })]
    public void AMapRunForWhatItDoesIsFoundAndBecomesALoop(string fileName, string patternId, string code, string[] changedTo)
    {
        var found = Assert.Single(Scan(fileName, code, patternId));
        Assert.Equal(3, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines);
    }

    [Theory]
    // Its result used; a map with nothing to do but work a value out; and forEach, which runs straight away.
    [InlineData("main.dart", "logic-dart-map-never-run", "void main() {\n  var marks = [70, 80];\n  var doubled = marks.map((mark) => mark * 2).toList();\n  marks.map((mark) => mark * 2);\n  marks.forEach((mark) => print(mark));\n}\n")]
    [InlineData("main.py", "logic-python-map-never-run", "marks = [70, 80]\nwords = list(map(str, marks))\nmap(str, marks)\nfor mark in marks:\n    print(mark)\n")]
    [InlineData("main.cs", "logic-csharp-select-never-run", "var marks = new[] { 70, 80 };\nvar doubled = marks.Select(mark => mark * 2).ToList();\nforeach (var mark in marks) { Console.WriteLine(mark); }\n")]
    public void AMapWhoseResultIsUsedOrThatOnlyWorksAValueOutIsLeftAlone(string fileName, string patternId, string code) =>
        Assert.Empty(Scan(fileName, code, patternId));
}

/// <summary>
/// The mistake and its change run with each language's own toolchain, as FixFinder Learn runs a lesson: the mistake prints only
/// done, and the change each mark before it. A language this computer cannot run is passed over, and said to be.
/// </summary>
public class LazyMapLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string, string, string, int> Mistakes => new()
    {
        { "Dart", "main.dart", "logic-dart-map-never-run", "void main() {\n  var marks = [70, 80, 90];\n  marks.map((mark) => print(mark));\n  print(\"done\");\n}\n", 3 },
        { "Python", "main.py", "logic-python-map-never-run", "marks = [70, 80, 90]\nmap(print, marks)\nprint(\"done\")\n", 2 },
        {
            "C#", "main.cs", "logic-csharp-select-never-run",
            "var marks = new[] { 70, 80, 90 };\nmarks.Select(mark => { Console.WriteLine(mark); return mark; });\nConsole.WriteLine(\"done\");\n", 2
        },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakePrintsOnlyDoneAndTheChangeEachMarkFirst(string languageName, string fileName, string patternId, string code, int line)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);

        var language = CodeLanguage.All.Single(each => each.Name == languageName);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == patternId);
        Assert.Equal(line, found.Line);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(found.Fix!.StartLine - 1, found.Fix.RemoveCount);
        lines.InsertRange(found.Fix.StartLine - 1, found.Fix.NewLines);
        var changed = string.Join("\n", lines) + "\n";

        var wrong = await SnippetRunner.RunAsync(language, code);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(language, changed);
        output.WriteLine($"the mistake: {wrong.Said(languageName)}\nthe change:  {right.Said(languageName)}");

        Assert.True(wrong.Shows(Behaviour.Printing("done")), wrong.Said(languageName));
        Assert.True(right.Shows(Behaviour.Printing("70\n80\n90\ndone")), right.Said(languageName));
    }
}
