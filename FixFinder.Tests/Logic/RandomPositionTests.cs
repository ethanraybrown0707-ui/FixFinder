using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// randint(0, len(marks)) used as a position in marks: found, at once or through a name, with the end made the last position,
/// and left alone where the position cannot pass the end.
/// </summary>
public class RandomPositionPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string code)
    {
        var path = Path.Combine(_temp.Path, "main.py");
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == "logic-python-randint-past-end").ToList();
    }

    [Theory]
    [InlineData("import random\nmarks = [70, 80, 90]\nprint(marks[random.randint(0, len(marks))])\n", 3, "print(marks[random.randint(0, len(marks) - 1)])")]
    [InlineData("from random import randint\nnames = [\"Ada\", \"Grace\"]\nchosen = randint(0, len(names))\nprint(names[chosen])\n", 3, "chosen = randint(0, len(names) - 1)")]
    public void ARandomPositionThatCanPassTheEndIsFoundAndStopsAtTheLast(string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(code));
        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // The last position already; an item chosen without a position; a dice roll; and a number never used as a position.
    [InlineData("import random\nmarks = [70, 80]\nprint(marks[random.randint(0, len(marks) - 1)], random.choice(marks), random.randint(1, 6))\ncount = random.randint(0, len(marks))\nprint(count)\n")]
    public void ARandomPositionThatCannotPassTheEndIsLeftAlone(string code) => Assert.Empty(Scan(code));
}

/// <summary>
/// The mistake and its change run with Python, as FixFinder Learn runs a lesson, drawing a thousand positions: the mistake stops
/// with IndexError when it draws len(marks) - which a thousand draws all but always do - and the change never fails. Passed
/// over, and said to be, where this computer cannot run Python.
/// </summary>
public class RandomPositionLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task TheMistakeStopsWithIndexErrorAndTheChangeNeverFails()
    {
        const string code = "import random\nmarks = [70, 80, 90]\nfor _ in range(1000):\n    pick = marks[random.randint(0, len(marks))]\nprint(\"never failed\")\n";
        var path = Path.Combine(_temp.Path, "main.py");
        File.WriteAllText(path, code);

        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == "logic-python-randint-past-end");
        Assert.Equal(4, found.Line);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines[found.Fix!.StartLine - 1] = found.Fix.NewLines.Single();
        var changed = string.Join("\n", lines) + "\n";

        var python = CodeLanguage.All.Single(each => each.Name == "Python");
        var wrong = await SnippetRunner.RunAsync(python, code);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(python, changed);
        output.WriteLine($"the mistake: {wrong.Said("Python")}\nthe change:  {right.Said("Python")}");

        // Each draw is len(marks) one time in four, so a thousand draws miss it only (3/4)^1000 of the time - about once in 10^125.
        Assert.True(wrong.Shows(Behaviour.Stopping("IndexError", 4)), wrong.Said("Python"));
        Assert.True(right.Shows(Behaviour.Printing("never failed")), right.Said("Python"));
    }
}
