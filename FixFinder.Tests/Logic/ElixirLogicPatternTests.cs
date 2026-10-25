using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// Text compared with a number in Elixir: found where the name only ever holds text, turned into a number first - after a trim
/// for a line read with its newline still on - and left alone where the name holds a number.
/// </summary>
public class ElixirLogicPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string code)
    {
        var path = Path.Combine(_temp.Path, "main.exs");
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == "logic-elixir-text-compared-with-number").ToList();
    }

    [Theory]
    [InlineData("typed = \"7\"\nif typed == 7 do\n  IO.puts(\"seven\")\nend\n", 2, "if String.to_integer(typed) == 7 do")]
    [InlineData("typed = IO.gets(\"Number? \") |> String.trim()\nIO.puts(typed > 10)\n", 2, "IO.puts(String.to_integer(typed) > 10)")]
    // A line read with its newline still on is trimmed before it is turned into a number.
    [InlineData("typed = IO.gets(\"Number? \")\nIO.puts(typed != 0)\n", 2, "IO.puts(String.to_integer(String.trim(typed)) != 0)")]
    public void TextComparedWithANumberIsFoundAndTurnedIntoANumberFirst(string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(code));

        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // A number, and a name given a number as well as text.
    [InlineData("number = String.to_integer(\"7\")\nIO.puts(number == 7)\n")]
    [InlineData("typed = \"7\"\ntyped = 7\nIO.puts(typed == 7)\n")]
    // Text compared with text, and a comparison inside text.
    [InlineData("typed = \"7\"\nIO.puts(typed == \"7\")\n")]
    [InlineData("typed = \"7\"\nIO.puts(\"typed == 7\")\n")]
    public void ANumberOrTextComparedWithTextIsLeftAlone(string code) => Assert.Empty(Scan(code));
}
