using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// OCaml's logic checks, each run the way FixFinder Learn runs a lesson - compiled by ocamlc and run by ocamlrun: the mistake
/// compiles with no warning and prints the wrong answer, and the check's change prints the right one. This computer cannot run
/// OCaml, so these run on GitHub's runners, in the OCaml job; here they are passed over, and said to be.
/// </summary>
public class OCamlLogicLiveTests(ITestOutputHelper output)
{
    /// <summary>Each: the check, the mistake, what it prints, and what its change prints.</summary>
    public static TheoryData<string, string, string, string> Mistakes => new()
    {
        { "logic-ocaml-odd-test-negative", "let () =\n  let n = -3 in\n  if n mod 2 = 1 then print_endline \"odd\" else print_endline \"even\"\n", "even", "odd" },
        { "logic-ocaml-loop-never-runs", "let () =\n  let total = ref 0 in\n  for i = 3 to 1 do\n    total := !total + i\n  done;\n  print_int !total\n", "0", "6" },
        { "logic-ocaml-percent-divided-first", "let () =\n  let score = 45 in\n  let total = 60 in\n  print_int (score / total * 100)\n", "0", "75" },
        {
            "logic-ocaml-float-equality",
            "let () =\n  let price = 0.1 +. 0.2 in\n  Printf.printf \"%.17g\\n\" price;\n  if price = 0.3 then print_endline \"equal\" else print_endline \"not equal\"\n",
            "0.30000000000000004\nnot equal", "0.30000000000000004\nequal"
        },
        {
            "logic-ocaml-branch-never-reached",
            "let grade score =\n  if score >= 50 then \"C\"\n  else if score >= 70 then \"B\"\n  else \"F\"\n\nlet () = print_endline (grade 75)\n",
            "C", "B"
        },
        {
            "logic-ocaml-rows-shared",
            "let () =\n  let grid = Array.make 3 (Array.make 3 0) in\n  grid.(0).(0) <- 1;\n  Array.iter (fun row -> Array.iter print_int row; print_newline ()) grid\n",
            "100\n100\n100", "100\n000\n000"
        },
        {
            "logic-ocaml-then-takes-one-expression",
            "let () =\n  let total = ref 0 in\n  for i = 1 to 4 do\n    if i mod 2 = 0 then\n      print_endline \"even\";\n      total := !total + i\n  done;\n  print_int !total\n",
            "even\neven\n10", "even\neven\n6"
        },
        {
            "logic-ocaml-hashtbl-add-for-update",
            "let () =\n  let counts = Hashtbl.create 10 in\n  List.iter (fun word ->\n    let count = try Hashtbl.find counts word with Not_found -> 0 in\n" +
            "    Hashtbl.add counts word (count + 1)) [ \"a\"; \"b\"; \"a\" ];\n  Printf.printf \"a: %d\\n\" (Hashtbl.find counts \"a\");\n" +
            "  Printf.printf \"%d %d\\n\" (Hashtbl.length counts) (Hashtbl.fold (fun _ _ seen -> seen + 1) counts 0)\n",
            "a: 2\n3 3", "a: 2\n2 2"
        },
    };

    private static LogicFinding FoundIn(string check, string code, TempFolder temp)
    {
        var path = Path.Combine(temp.Path, "program.ml");
        File.WriteAllText(path, code);
        return Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == check);
    }

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakeCompilesWithNoWarningAndPrintsTheWrongAnswerAndTheChangePrintsTheRightOne(string check, string mistake, string mistakePrints, string changePrints)
    {
        using var temp = new TempFolder();
        var fix = FoundIn(check, mistake, temp).Fix!;
        var lines = mistake.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(fix.StartLine - 1, fix.RemoveCount);
        lines.InsertRange(fix.StartLine - 1, fix.NewLines);

        var wrong = await SnippetRunner.RunAsync(CodeLanguage.OCaml, mistake);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }
        var right = await SnippetRunner.RunAsync(CodeLanguage.OCaml, string.Join("\n", lines) + "\n");

        output.WriteLine($"ocamlc and the mistake:\n{string.Join("\n", wrong.Output)}\nthe change:\n{string.Join("\n", right.Output)}");
        Assert.DoesNotContain(wrong.Output, line => line.Contains("Warning", StringComparison.Ordinal));
        Assert.True(wrong.Shows(Behaviour.Printing(mistakePrints)), wrong.Said("OCaml"));
        Assert.True(right.Shows(Behaviour.Printing(changePrints)), right.Said("OCaml"));
    }
}
