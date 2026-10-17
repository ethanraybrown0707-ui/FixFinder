using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>An array of numbers sent to cout: found, with a loop that writes its items, and left alone for a char array or one item.</summary>
public class CppOutputPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string code)
    {
        var path = Path.Combine(_temp.Path, "main.cpp");
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == "logic-cpp-array-printed").ToList();
    }

    private static string InsideMain(string body) => "#include <iostream>\nusing namespace std;\nint main() {\n" + body + "}\n";

    [Fact]
    public void AnArrayAloneIsWrittenItemByItem()
    {
        var found = Assert.Single(Scan(InsideMain("    int marks[] = {70, 80, 90};\n    std::cout << marks << \"\\n\";\n")));
        Assert.Equal(5, found.Line);
        Assert.Equal(["    for (int mark : marks) std::cout << mark << \" \";", "    std::cout << \"\\n\";"], found.Fix!.NewLines);
    }

    [Fact]
    public void TextBeforeAndAfterTheArrayIsKeptAroundTheLoop()
    {
        var found = Assert.Single(Scan(InsideMain("    int marks[] = {70, 80, 90};\n    std::cout << \"Marks: \" << marks << std::endl;\n")));
        Assert.Equal(
            ["    std::cout << \"Marks: \";", "    for (int mark : marks) std::cout << mark << \" \";", "    std::cout << std::endl;"],
            found.Fix!.NewLines);
    }

    [Fact]
    public void ADoubleArrayWithCoutFromUsingNamespaceGetsItsOwnType()
    {
        var found = Assert.Single(Scan(InsideMain("    double prices[3] = {1.5, 2.5, 3.5};\n    cout << prices;\n")));
        Assert.Equal(["    for (double price : prices) cout << price << \" \";"], found.Fix!.NewLines);
    }

    [Fact]
    public void TheLoopsVariableIsNotANameTheCodeUsesAlready()
    {
        var found = Assert.Single(Scan(InsideMain("    int marks[] = {70, 80};\n    int mark = 0;\n    cout << marks;\n")));
        Assert.Equal(["    for (int item : marks) cout << item << \" \";"], found.Fix!.NewLines);
    }

    [Theory]
    // A char array is text, and cout writes it as text.
    [InlineData("    char name[] = \"Ada\";\n    std::cout << name << \"\\n\";\n")]
    // One item of the array, and its size.
    [InlineData("    int marks[] = {70, 80, 90};\n    std::cout << marks[0] << \" \" << sizeof(marks) << \"\\n\";\n")]
    public void TextOrOneItemSentToCoutIsLeftAlone(string body) => Assert.Empty(Scan(InsideMain(body)));
}

/// <summary>
/// The mistake and its change built and run with g++, as FixFinder Learn runs a lesson: the mistake writes an address, and the
/// change the items. Passed over, and said to be, where this computer cannot build C++.
/// </summary>
public class CppOutputLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task TheMistakeWritesAnAddressAndTheChangeTheItems()
    {
        const string code = "#include <iostream>\n\nint main() {\n    int marks[] = {70, 80, 90};\n    std::cout << \"Marks: \" << marks << \"\\n\";\n}\n";
        var path = Path.Combine(_temp.Path, "main.cpp");
        File.WriteAllText(path, code);

        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == "logic-cpp-array-printed");
        Assert.Equal(5, found.Line);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(found.Fix!.StartLine - 1, found.Fix.RemoveCount);
        lines.InsertRange(found.Fix.StartLine - 1, found.Fix.NewLines);
        var changed = string.Join("\n", lines) + "\n";

        var cpp = CodeLanguage.All.Single(each => each.Name == "C++");
        var wrong = await SnippetRunner.RunAsync(cpp, code);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(cpp, changed);
        output.WriteLine($"the mistake: {wrong.Said("C++")}\nthe change:  {right.Said("C++")}");

        // The address is not the same from run to run - 0xe84efff7b4 in one, 0xa459bffe64 in the next - so only its start is checked.
        Assert.True(wrong.Error is null && wrong.Printed is { } printed && printed.StartsWith("Marks: 0x", StringComparison.Ordinal), wrong.Said("C++"));
        // The loop writes a space after each item, the last one too.
        Assert.True(right.Error is null && right.Printed?.TrimEnd() == "Marks: 70 80 90", right.Said("C++"));
    }
}
