using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// removeAt in a Dart loop counting up through the same list: found where it removes at the loop's own position, with the loop
/// turned to count down, and left alone where the loop counts down already or removes something else.
/// </summary>
public class DartLogicPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string code)
    {
        var path = Path.Combine(_temp.Path, "main.dart");
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == "logic-dart-remove-while-counting-up").ToList();
    }

    private static string InMain(string body) => $"void main() {{\n  var marks = [70, 40, 40, 90];\n{body}  print(marks);\n}}\n";

    [Theory]
    [InlineData("  for (var i = 0; i < marks.length; i++) {\n    if (marks[i] == 40) {\n      marks.removeAt(i);\n    }\n  }\n", "  for (var i = marks.length - 1; i >= 0; i--) {")]
    [InlineData("  for (int k = 0; k < marks.length; k++) {\n    if (marks[k] < 50) marks.removeAt(k);\n  }\n", "  for (int k = marks.length - 1; k >= 0; k--) {")]
    public void RemoveAtWhileCountingUpIsFoundAndTheLoopCountsDown(string body, string changedTo)
    {
        var found = Assert.Single(Scan(InMain(body)));

        Assert.Equal(3, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // Counting down already.
    [InlineData("  for (var i = marks.length - 1; i >= 0; i--) {\n    if (marks[i] == 40) {\n      marks.removeAt(i);\n    }\n  }\n")]
    // Removing from another list, or at another position, or nothing at all.
    [InlineData("  var kept = [1, 2, 3, 4];\n  for (var i = 0; i < marks.length; i++) {\n    if (marks[i] == 40) {\n      kept.removeAt(i);\n    }\n  }\n")]
    [InlineData("  for (var i = 0; i < marks.length; i++) {\n    if (marks[i] == 40) {\n      marks.removeAt(0);\n    }\n  }\n")]
    [InlineData("  for (var i = 0; i < marks.length; i++) {\n    print(marks[i]);\n  }\n")]
    public void ALoopThatSkipsNothingIsLeftAlone(string body) => Assert.Empty(Scan(InMain(body)));
}
