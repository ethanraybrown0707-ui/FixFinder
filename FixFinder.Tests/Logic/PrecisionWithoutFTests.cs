using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A precision with no letter after it in a Python f-string: found for a decimal, with f added, and left alone for text, which
/// it cuts short on purpose, and for a precision that already says f.
/// </summary>
public class PrecisionWithoutFPatternTests : IDisposable
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
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == "logic-python-precision-without-f").ToList();
    }

    [Theory]
    [InlineData("average = 150.5\nprint(f\"Average: {average:.2}\")\n", 2, "print(f\"Average: {average:.2f}\")")]
    // A division, which in Python always gives a decimal, and a decimal number with a width before its precision.
    [InlineData("total = 7\ncount = 2\nprint(f\"{total / count:.1}\")\n", 3, "print(f\"{total / count:.1f}\")")]
    [InlineData("print(F'{3.14159:>8.3}')\n", 1, "print(F'{3.14159:>8.3f}')")]
    public void APrecisionWithNoLetterForADecimalIsFoundAndAsksForDecimalPlaces(string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(code));

        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    [InlineData("average = 150.5\nprint(f\"{average:.2f}\")\n")]
    // Text cut short to three characters, as .3 does to text - on purpose.
    [InlineData("name = \"Ada Lovelace\"\nprint(f\"{name:.3}\")\n")]
    // A width alone, and a name never given a decimal.
    [InlineData("count = 3\nprint(f\"{count:5}\")\n")]
    [InlineData("print(f\"{marks[0]:.2}\")\n")]
    // Not an f-string, and inside a comment.
    [InlineData("print(\"{average:.2}\")\n")]
    [InlineData("# print(f\"{average:.2}\")\n")]
    public void TextOrAPrecisionThatSaysWhatItMeansIsLeftAlone(string code) => Assert.Empty(Scan(code));
}

/// <summary>The mistake and its change run with Python, as FixFinder Learn runs a lesson.</summary>
public class PrecisionWithoutFLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task TheMistakePrintsScientificNotationAndTheChangeTwoDecimalPlaces()
    {
        const string code = "average = 150.5\nprint(f\"Average: {average:.2}\")\n";
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "main.py");
        File.WriteAllText(path, code);

        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == "logic-python-precision-without-f");
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
        Assert.True(wrong.Shows(Behaviour.Printing("Average: 1.5e+02")), wrong.Said("Python"));
        Assert.True(right.Shows(Behaviour.Printing("Average: 150.50")), right.Said("Python"));
    }
}
