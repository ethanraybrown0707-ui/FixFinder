using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A + given to Write-Host or Write-Output as a word of its own: found where the words have no brackets round them, with the
/// brackets added, and left alone where they are bracketed or the + is inside text.
/// </summary>
public class PowerShellLogicPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string code)
    {
        var path = Path.Combine(_temp.Path, "main.ps1");
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == "logic-powershell-plus-outside-brackets").ToList();
    }

    [Theory]
    [InlineData("$total = 150\nWrite-Host \"Total: \" + $total\n", 2, "Write-Host (\"Total: \" + $total)")]
    [InlineData("    write-output 'Name: ' + $name + ' ' + $surname\n", 1, "    write-output ('Name: ' + $name + ' ' + $surname)")]
    [InlineData("Write-Host $first + $second\n", 1, "Write-Host ($first + $second)")]
    public void APlusGivenAsAWordIsFoundAndTheSumBracketed(string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(code));

        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    [InlineData("Write-Host (\"Total: \" + $total)\n")]
    [InlineData("Write-Host \"Total: $total\"\n")]
    [InlineData("Write-Host \"1 + 1\"\n")]
    // The command's own parameters after the words: not a line the change can bracket.
    [InlineData("Write-Host \"Total: \" + $total -ForegroundColor Green\n")]
    [InlineData("# Write-Host \"Total: \" + $total\n")]
    public void ABracketedSumOrAPlusInsideTextIsLeftAlone(string code) => Assert.Empty(Scan(code));
}

/// <summary>A + given to Write-Host as a word, run with Windows PowerShell, and the check's own change run the same way.</summary>
public class PowerShellLogicLiveTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("$total = 150\nWrite-Host \"Total: \" + $total\n", "Total:  + 150", "Total: 150")]
    [InlineData("$total = 150\nWrite-Output \"Total: \" + $total\n", "Total:\n+\n150", "Total: 150")]
    public async Task TheMistakeWritesThePlusAndTheChangeTheSum(string mistake, string mistakePrints, string changePrints)
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "main.ps1");
        File.WriteAllText(path, mistake);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == "logic-powershell-plus-outside-brackets");

        var lines = mistake.TrimEnd('\n').Split('\n').ToList();
        lines[found.Fix!.StartLine - 1] = found.Fix.NewLines.Single();
        var changed = string.Join("\n", lines) + "\n";

        var powerShell = CodeLanguage.All.Single(each => each.Name == "PowerShell");
        var wrong = await SnippetRunner.RunAsync(powerShell, mistake);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(powerShell, changed);
        output.WriteLine($"the mistake: {wrong.Said("PowerShell")}\nthe change:  {right.Said("PowerShell")}");

        Assert.True(wrong.Shows(Behaviour.Printing(mistakePrints)), wrong.Said("PowerShell"));
        Assert.True(right.Shows(Behaviour.Printing(changePrints)), right.Said("PowerShell"));
    }
}
