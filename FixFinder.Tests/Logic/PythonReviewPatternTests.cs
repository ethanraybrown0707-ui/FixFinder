using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// except A or B, and a main that is made and never called: found with their changes, and left alone where the except
/// brackets its kinds or main is called anywhere outside itself.
/// </summary>
public class PythonReviewPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string code, string check)
    {
        var path = Path.Combine(_temp.Path, "app.py");
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == check).ToList();
    }

    [Theory]
    [InlineData("try:\n    n = int(text)\nexcept ValueError or TypeError:\n    n = 0\n", "except (ValueError, TypeError):")]
    [InlineData("try:\n    n = int(text)\nexcept ValueError or TypeError or KeyError as error:  # all three\n    n = 0\n", "except (ValueError, TypeError, KeyError) as error:  # all three")]
    public void ExceptWithOrIsFoundAndCatchesEachKind(string code, string changedTo)
    {
        var found = Assert.Single(Scan(code, "logic-python-except-or"));

        Assert.Equal(3, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    [InlineData("try:\n    n = int(text)\nexcept (ValueError, TypeError):\n    n = 0\n")]
    [InlineData("try:\n    n = int(text)\nexcept ValueError:\n    n = 0\n")]
    public void ExceptWithItsKindsBracketedIsLeftAlone(string code) => Assert.Empty(Scan(code, "logic-python-except-or"));

    [Fact]
    public void MainMadeAndNeverCalledIsFoundAndCalledAtTheEnd()
    {
        var found = Assert.Single(Scan("import math\n\n\ndef main():\n    print(math.pi)\n", "logic-python-main-never-called"));

        Assert.Equal(4, found.Line);
        Assert.Equal(6, found.Fix!.StartLine);
        Assert.Equal(["", "", "if __name__ == \"__main__\":", "    main()"], found.Fix.NewLines);
    }

    [Theory]
    [InlineData("def main():\n    print(1)\n\n\nmain()\n")]
    [InlineData("def main():\n    print(1)\n\n\nif __name__ == \"__main__\":\n    main()\n")]
    // Called from another function, which is itself called; and no main at all.
    [InlineData("def main():\n    print(1)\n\n\ndef start():\n    main()\n\n\nstart()\n")]
    [InlineData("def show():\n    print(1)\n")]
    public void MainThatIsCalledIsLeftAlone(string code) => Assert.Empty(Scan(code, "logic-python-main-never-called"));
}

/// <summary>Both mistakes, run with Python the way FixFinder Learn runs a lesson, and the checks' own changes run the same way.</summary>
public class PythonReviewLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task MainNeverCalledPrintsNothingAndTheChangeRunsIt()
    {
        const string code = "def main():\n    print(\"Hello\")\n";
        var python = CodeLanguage.All.Single(each => each.Name == "Python");
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "app.py");
        File.WriteAllText(path, code);

        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == "logic-python-main-never-called");
        var changed = code + string.Join("\n", found.Fix!.NewLines) + "\n";

        var wrong = await SnippetRunner.RunAsync(python, code);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(python, changed);
        Assert.True(wrong.Shows(Behaviour.Printing("")), wrong.Said("Python"));
        Assert.True(right.Shows(Behaviour.Printing("Hello")), right.Said("Python"));
    }

    [Fact]
    public async Task ExceptWithOrLetsTheSecondKindStopTheProgramAndTheChangeCatchesIt()
    {
        const string code = "def read(text):\n    try:\n        return int(text)\n    except ValueError or TypeError:\n        return 0\n\n\nprint(read(None))\n";
        var python = CodeLanguage.All.Single(each => each.Name == "Python");
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "app.py");
        File.WriteAllText(path, code);

        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == "logic-python-except-or");
        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines[found.Fix!.StartLine - 1] = found.Fix.NewLines.Single();
        var changed = string.Join("\n", lines) + "\n";

        var wrong = await SnippetRunner.RunAsync(python, code);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(python, changed);
        Assert.True(wrong.Shows(Behaviour.Stopping("TypeError", 3)), wrong.Said("Python"));
        Assert.True(right.Shows(Behaviour.Printing("0")), right.Said("Python"));
    }
}
