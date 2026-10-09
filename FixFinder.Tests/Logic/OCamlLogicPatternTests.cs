using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// The mistakes OCaml programs make that still compile - each found where it is, with its change, and left alone where the
/// same words are correct: a loop that stops in time, == between things that are the same in memory, a division of floats.
/// </summary>
public class OCamlLogicPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string code, string pattern)
    {
        var path = Path.Combine(_temp.Path, "program.ml");
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == pattern).ToList();
    }

    [Theory]
    [InlineData("let marks = [| 70; 80; 90 |]\nlet () =\n  for i = 0 to Array.length marks do\n    print_int marks.(i)\n  done\n", "  for i = 0 to Array.length marks - 1 do")]
    [InlineData("let name = \"Ada\"\nlet () =\n  for i = 0 to String.length name do print_char name.[i] done\n", "  for i = 0 to String.length name - 1 do print_char name.[i] done")]
    [InlineData("let marks = [70; 80]\nlet () =\n  for i = 0 to List.length marks do\n    print_int (List.nth marks i)\n  done\n", "  for i = 0 to List.length marks - 1 do")]
    public void ALoopToALengthThatUsesItsNumberAsAPositionIsFound(string code, string fixedLine)
    {
        var finding = Assert.Single(Scan(code, "logic-ocaml-for-to-length"));

        Assert.Equal(fixedLine, finding.Fix?.NewLines.Single());
    }

    [Theory]
    [InlineData("let marks = [| 70; 80; 90 |]\nlet () =\n  for i = 0 to Array.length marks - 1 do\n    print_int marks.(i)\n  done\n")]
    [InlineData("let marks = [| 70; 80; 90 |]\nlet () =\n  for count = 0 to Array.length marks do\n    print_int count\n  done\n")]
    public void ALoopThatStopsInTimeOrUsesItsNumberForSomethingElseIsLeftAlone(string code) =>
        Assert.Empty(Scan(code, "logic-ocaml-for-to-length"));

    [Theory]
    [InlineData("let check name = if name == \"Ada\" then print_endline \"hello\"\n", "let check name = if name = \"Ada\" then print_endline \"hello\"")]
    [InlineData("let check marks = if marks != [70; 80] then print_endline \"changed\"\n", "let check marks = if marks <> [70; 80] then print_endline \"changed\"")]
    [InlineData("let check price = price == 2.5\n", "let check price = price = 2.5")]
    public void TextListsAndDecimalsComparedInMemoryAreFound(string code, string fixedLine)
    {
        var finding = Assert.Single(Scan(code, "logic-ocaml-physical-equality"));

        Assert.Equal(fixedLine, finding.Fix?.NewLines.Single());
    }

    [Theory]
    [InlineData("let check marks = if marks == [] then print_endline \"none\"\n")]
    [InlineData("let check count = count == 3\n")]
    [InlineData("let check name = if name = \"Ada\" then print_endline \"hello\"\n")]
    public void ComparingWhatIsTheSameInMemoryOrComparingValuesIsLeftAlone(string code) =>
        Assert.Empty(Scan(code, "logic-ocaml-physical-equality"));

    [Theory]
    [InlineData("let average total count = float_of_int (total / count)\n", "let average total count = (float_of_int total /. float_of_int count)")]
    [InlineData("let average marks = float (List.fold_left ( + ) 0 marks / List.length marks)\n",
        "let average marks = (float_of_int (List.fold_left ( + ) 0 marks) /. float_of_int (List.length marks))")]
    [InlineData("let share total = float_of_int (total / 2)\n", "let share total = (float_of_int total /. float_of_int 2)")]
    public void AWholeNumberDivisionMadeAFloatAfterwardsIsFound(string code, string? fixedLine)
    {
        var found = Scan(code, "logic-ocaml-integer-average");

        if (fixedLine is null)
        {
            Assert.Empty(found);
            return;
        }

        Assert.Equal(fixedLine, Assert.Single(found).Fix?.NewLines.Single());
    }

    [Fact]
    public void AFloatDivisionIsLeftAlone() =>
        Assert.Empty(Scan("let average total count = float_of_int total /. float_of_int count\n", "logic-ocaml-integer-average"));
}
