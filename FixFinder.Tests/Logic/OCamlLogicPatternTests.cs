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

    [Theory]
    // Each: the check, the program, the line found on, and that line as the change writes it.
    [InlineData("logic-ocaml-odd-test-negative", "let () =\n  let n = -3 in\n  if n mod 2 = 1 then print_endline \"odd\" else print_endline \"even\"\n", 3,
        "  if n mod 2 <> 0 then print_endline \"odd\" else print_endline \"even\"")]
    [InlineData("logic-ocaml-odd-test-negative", "let is_even n = n mod 2 <> 1\n", 1, "let is_even n = n mod 2 = 0")]
    [InlineData("logic-ocaml-loop-never-runs", "let () =\n  for i = 3 to 1 do\n    print_int i\n  done\n", 2, "  for i = 3 downto 1 do")]
    [InlineData("logic-ocaml-percent-divided-first", "let percent score total = score / total * 100\n", 1, "let percent score total = score * 100 / total")]
    [InlineData("logic-ocaml-float-equality", "let price = 0.1 +. 0.2\n\nlet () = if price = 0.3 then print_endline \"equal\"\n", 3,
        "let () = if abs_float (price -. 0.3) < 1e-9 then print_endline \"equal\"")]
    [InlineData("logic-ocaml-float-equality", "let () =\n  let price = 0.1 +. 0.2 in\n  if price <> 0.3 then print_endline \"different\"\n", 3,
        "  if abs_float (price -. 0.3) >= 1e-9 then print_endline \"different\"")]
    [InlineData("logic-ocaml-rows-shared", "let grid = Array.make 3 (Array.make 3 0)\n", 1, "let grid = Array.init 3 (fun _ -> Array.make 3 0)")]
    [InlineData("logic-ocaml-rows-shared", "let grid = Array.make 2 [| 0; 0 |]\n", 1, "let grid = Array.init 2 (fun _ -> [| 0; 0 |])")]
    [InlineData("logic-ocaml-rows-shared", "let row = Array.make 3 0\nlet grid = Array.make 3 row\n", 2, "let grid = Array.init 3 (fun _ -> Array.copy row)")]
    [InlineData("logic-ocaml-hashtbl-add-for-update",
        "let count_words words =\n  let counts = Hashtbl.create 10 in\n  List.iter (fun word ->\n    let count = try Hashtbl.find counts word with Not_found -> 0 in\n    Hashtbl.add counts word (count + 1)) words;\n  counts\n",
        5, "    Hashtbl.replace counts word (count + 1)) words;")]
    public void AnOCamlMistakeIsFoundWithItsChange(string check, string code, int line, string changedLine)
    {
        var finding = Assert.Single(Scan(code, check));

        Assert.Equal(line, finding.Line);
        Assert.Equal(changedLine, finding.Fix?.NewLines.Single());
    }

    [Theory]
    [InlineData("logic-ocaml-odd-test-negative", "let () =\n  for i = 0 to 9 do\n    if i mod 2 = 1 then print_int i\n  done\n")]
    [InlineData("logic-ocaml-odd-test-negative", "let is_even n = n mod 2 = 0\n")]
    [InlineData("logic-ocaml-loop-never-runs", "let () =\n  for i = 1 to 3 do\n    print_int i\n  done\n")]
    [InlineData("logic-ocaml-loop-never-runs", "let () =\n  for i = 3 downto 1 do\n    print_int i\n  done\n")]
    [InlineData("logic-ocaml-percent-divided-first", "let percent score total = score * 100 / total\n")]
    [InlineData("logic-ocaml-float-equality", "let price = 0.5\n\nlet () = if price = 0.5 then print_endline \"half\"\n")]
    [InlineData("logic-ocaml-rows-shared", "let marks = Array.make 3 0\n")]
    [InlineData("logic-ocaml-rows-shared", "let grid = Array.make_matrix 3 3 0\n")]
    [InlineData("logic-ocaml-rows-shared", "let fill size value = Array.make size value\n")]
    [InlineData("logic-ocaml-hashtbl-add-for-update", "let index words =\n  let positions = Hashtbl.create 10 in\n  List.iteri (fun position word -> Hashtbl.add positions word position) words;\n  positions\n")]
    [InlineData("logic-ocaml-hashtbl-add-for-update",
        "let scope = Hashtbl.create 10\n\nlet shadow name value body =\n  let outer = Hashtbl.find_opt scope name in\n  Hashtbl.add scope name value;\n  body ();\n  Hashtbl.remove scope name;\n  outer\n")]
    public void TheSameWordsUsedRightlyAreLeftAlone(string check, string code) =>
        Assert.Empty(Scan(code, check));

    private const string GradeLadder = "let grade score =\n  if score >= 50 then \"C\"\n  else if score >= 70 then \"B\"\n  else \"F\"\n\nlet () = print_endline (grade 75)\n";

    [Fact]
    public void AnElseIfAnEarlierBranchTakesEveryValueFromIsFoundAndPutFirst()
    {
        var finding = Assert.Single(Scan(GradeLadder, "logic-ocaml-branch-never-reached"));

        Assert.Equal(3, finding.Line);
        Assert.Equal((2, 2), (finding.Fix!.StartLine, finding.Fix.RemoveCount));
        Assert.Equal(["  if score >= 70 then \"B\"", "  else if score >= 50 then \"C\""], finding.Fix.NewLines);
    }

    [Fact]
    public void BranchesInOrderAreLeftAlone() =>
        Assert.Empty(Scan("let grade score =\n  if score >= 70 then \"B\"\n  else if score >= 50 then \"C\"\n  else \"F\"\n", "logic-ocaml-branch-never-reached"));

    private const string EvenTotal = "let () =\n  let total = ref 0 in\n  for i = 1 to 4 do\n    if i mod 2 = 0 then\n      print_endline \"even\";\n      total := !total + i\n  done;\n  print_int !total\n";

    [Fact]
    public void TwoLinesUnderThenTheFirstEndingWithASemicolonAreFoundAndPutInBeginAndEnd()
    {
        var finding = Assert.Single(Scan(EvenTotal, "logic-ocaml-then-takes-one-expression"));

        Assert.Equal(6, finding.Line);
        Assert.Equal((4, 3), (finding.Fix!.StartLine, finding.Fix.RemoveCount));
        Assert.Equal(["    if i mod 2 = 0 then begin", "      print_endline \"even\";", "      total := !total + i", "    end"], finding.Fix.NewLines);
    }

    [Fact]
    public void ALastLineEndingWithASemicolonKeepsItAfterEnd()
    {
        var code = "let () =\n  let count = ref 0 in\n  if !count = 0 then\n    print_string \"none \";\n    incr count;\n  print_int !count\n";

        var finding = Assert.Single(Scan(code, "logic-ocaml-then-takes-one-expression"));

        Assert.Equal(["  if !count = 0 then begin", "    print_string \"none \";", "    incr count", "  end;"], finding.Fix!.NewLines);
    }

    [Fact]
    public void LinesThatCloseABracketFromOutsideTheIfAreFoundWithNoChange()
    {
        var code = "let () =\n  let passed = ref 0 in\n  List.iter (fun mark ->\n    if mark >= 50 then\n      print_endline \"pass\";\n      incr passed) [ 30; 70 ];\n  print_int !passed\n";

        var finding = Assert.Single(Scan(code, "logic-ocaml-then-takes-one-expression"));

        Assert.Equal(6, finding.Line);
        Assert.Null(finding.Fix);
    }

    [Theory]
    [InlineData("let () =\n  let total = ref 0 in\n  for i = 1 to 4 do\n    if i mod 2 = 0 then begin\n      print_endline \"even\";\n      total := !total + i\n    end\n  done\n")]
    [InlineData("let () =\n  let total = ref 0 in\n  for i = 1 to 4 do\n    if i mod 2 = 0 then\n      print_endline \"even\";\n    total := !total + i\n  done\n")]
    [InlineData("let () =\n  if true then\n    let message = \"yes\" in\n    print_endline message;\n    print_newline ()\n")]
    [InlineData("let () =\n  if true then\n    print_endline \"yes\"\n  else\n    print_endline \"no\"\n")]
    public void ThenLinesThatAreOneExpressionOrMeantToRunAfterAreLeftAlone(string code) =>
        Assert.Empty(Scan(code, "logic-ocaml-then-takes-one-expression"));
}
