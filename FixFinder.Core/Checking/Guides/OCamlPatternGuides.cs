using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic checks of OCaml programs, each explained for someone new to programming. Every example is a whole
/// program, and OCamlLiveTests compiles each one with ocamlc, and fails on any warning.
/// </summary>
internal static class OCamlPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-ocaml-for-to-length"], "A loop that runs one past the end",
            "OCaml's for counts up to and including its last number: for i = 0 to 3 goes round four times, for 0, 1, 2 and 3. The " +
            "positions in an array, a string or a list run from 0 to one less than its length, so a loop to the length itself takes " +
            "one step too many, and that step asks for a position that is not there.",
            "The last pass stops the program with Invalid_argument(\"index out of bounds\") - or Failure(\"nth\") for a list.",
            "Stop one before the length: for i = 0 to Array.length a - 1 do.",
            """
            let marks = [| 70; 80; 90 |]

            let () =
              for i = 0 to Array.length marks - 1 do
                print_int marks.(i)
              done
            """),

        GuideFor(["logic-ocaml-physical-equality"], "Values compared in memory",
            "OCaml has two ways of comparing. = asks whether two values are equal - hold the same letters, the same items, the same " +
            "number - and <> whether they are not. == asks whether they are the very same value in memory, and != whether they are " +
            "not. Text, lists and decimals are made anew each time they are worked out, so two equal ones are often not the same " +
            "one, and == says false for them.",
            "The comparison can say false for two equal values, so the code it guards does not run when it should.",
            "Compare values with = and <>; keep == and != for asking whether two names hold the very same thing.",
            """
            let name = String.make 3 'a'

            let () = print_endline (if name = "aaa" then "same" else "different")
            """),

        GuideFor(["logic-ocaml-integer-average"], "A decimal that lost its fraction",
            "Dividing one int by another with / gives an int: the part after the point is dropped, so 7 / 2 is 3. Turning that into " +
            "a float afterwards cannot bring the fraction back - it is already gone - so float_of_int (7 / 2) is 3., not 3.5. " +
            "Turning each number into a float first, and dividing with /., keeps it.",
            "Averages and percentages come out rounded down, with no error to say so.",
            "Make each number a float before dividing, and divide with /.: float_of_int total /. float_of_int count.",
            """
            let total = 7

            let count = 2

            let () = print_float (float_of_int total /. float_of_int count)
            """),

        GuideFor(["logic-ocaml-odd-test-negative"], "An odd number looked for with mod 2 = 1",
            "mod gives what is left over after dividing, and it keeps the sign of the number divided: 7 mod 2 is 1, but -7 mod 2 is -1. " +
            "So n mod 2 = 1 is true only for odd numbers above 0 - a negative odd number leaves -1, and is taken for even.",
            "The program is right until it meets a negative odd number, which it treats as even, and ocamlc says nothing.",
            "Ask whether anything is left over: n mod 2 <> 0 is true for every odd number, negative or not.",
            """
            let n = -3

            let () = if n mod 2 <> 0 then print_endline "odd" else print_endline "even"
            """),

        GuideFor(["logic-ocaml-loop-never-runs"], "A count down written as for i = 10 to 1",
            "OCaml's for with to counts up: for i = 10 to 1 starts at 10, which is already past 1, so the loop runs no times at all.",
            "Nothing in the loop ever runs, so it cannot be tested, and ocamlc says nothing.",
            "Count down with downto: for i = 10 downto 1 do.",
            """
            let () =
              for count = 3 downto 1 do
                print_int count
              done
            """),

        GuideFor(["logic-ocaml-percent-divided-first"], "A percentage that divides first",
            "OCaml's / divides whole numbers and gives a whole number - the part below 1 is thrown away. In score / total * 100 the " +
            "division comes first, so for 45 out of 60 it is 0, and 0 * 100 is 0.",
            "Every score below the total comes out as 0%, and ocamlc says nothing of it.",
            "Multiply first - score * 100 / total - so only the last division drops a fraction: 45 * 100 / 60 is 75.",
            """
            let score = 45

            let total = 60

            let () = print_int (score * 100 / total)
            """),

        GuideFor(["logic-ocaml-float-equality"], "Decimals compared exactly",
            "Decimal numbers are stored in binary, which cannot hold most decimals exactly, so arithmetic on them leaves a tiny error: " +
            "0.1 +. 0.2 is 0.30000000000000004.",
            "Compared exactly with = the two are not equal, with no error, though they are for every practical purpose.",
            "Compare the difference with a tiny tolerance: abs_float (price -. 0.3) < 1e-9.",
            """
            let price = 0.1 +. 0.2

            let () = if abs_float (price -. 0.3) < 1e-9 then print_endline "equal" else print_endline "not equal"
            """),

        GuideFor(["logic-ocaml-branch-never-reached"], "An else if that can never run",
            "An if - else if chain takes the first branch whose test passes. If an earlier test lets through every value a later one " +
            "does - score >= 50 before score >= 70 - the later branch never gets a value.",
            "That branch never runs, so no input can test it - a score of 75 gets C, not B - and ocamlc says nothing.",
            "Put the strictest test first: score >= 70, then score >= 50.",
            """
            let grade score =
              if score >= 70 then "B"
              else if score >= 50 then "C"
              else "F"

            let () = print_endline (grade 75)
            """),

        GuideFor(["logic-ocaml-rows-shared"], "A grid made with Array.make of an array",
            "Array.make 3 row puts the one value it is given in all three places - and an array is one array wherever it is put. So " +
            "every row of the grid is the same row, and setting one cell sets it in every row.",
            "grid.(0).(0) <- 1 changes the first cell of every row, and ocamlc says nothing.",
            "Make each row with Array.init, which runs its function once for each: Array.init 3 (fun _ -> Array.make 3 0).",
            """
            let grid = Array.init 3 (fun _ -> Array.make 3 0)

            let () =
              grid.(0).(0) <- 1;
              Array.iter (fun row -> Array.iter print_int row; print_newline ()) grid
            """),

        GuideFor(["logic-ocaml-then-takes-one-expression"], "Two lines under then, the first ending with ;",
            "then takes one expression, and a ; ends it. In if even then print_endline \"even\"; total := !total + i, only the " +
            "print_endline belongs to the if - the line after the ; runs every time, however far it is indented.",
            "The lines after the first run whether or not the condition holds, and ocamlc says nothing.",
            "Put the lines the condition is for in begin ... end, which makes them one expression.",
            """
            let () =
              let total = ref 0 in
              for i = 1 to 4 do
                if i mod 2 = 0 then begin
                  print_endline "even";
                  total := !total + i
                end
              done;
              print_int !total
            """),

        GuideFor(["logic-ocaml-hashtbl-add-for-update"], "Hashtbl.add where a count was to change",
            "Hashtbl.add does not change the binding a key has: it puts a new one on top, and keeps the old one under it. " +
            "Hashtbl.find sees the newest, but Hashtbl.length counts every binding, and Hashtbl.iter and fold go over every one.",
            "A word counted twice is in the table twice, so the number of words, and the counts listed, are wrong - with no error.",
            "Use Hashtbl.replace, which changes the binding there is, or adds one when there is none.",
            """
            let () =
              let counts = Hashtbl.create 10 in
              List.iter
                (fun word ->
                  let count = Option.value (Hashtbl.find_opt counts word) ~default:0 in
                  Hashtbl.replace counts word (count + 1))
                [ "a"; "b"; "a" ];
              print_int (Hashtbl.length counts)
            """),
    ];
}
