using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic checks of OCaml programs, each explained for someone new to programming. Every example is a whole
/// program, and OCamlGuideExampleTests compiles each one with ocamlc.
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
    ];
}
