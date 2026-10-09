using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// OCaml's compile errors and warnings, and the exceptions that stop its programs, each explained for someone new to
/// programming.
/// </summary>
/// <remarks>
/// The messages are matched as OCaml 5.2's own tests record them, with or without the double quotes earlier OCamls left
/// off names and types. Every example is a whole program, and OCamlGuideExampleTests compiles each one with ocamlc.
/// </remarks>
internal static class OCamlGuides
{
    private const string NothingRuns = "OCaml refuses to build the program while this is wrong, so no part of it runs - not even the lines before this one.";

    private const string Stops = "The program stops at this line, and nothing after it runs.";

    private static GuideEntry Compile(string pattern, string explanation, string fix, string example, string? why = null) => new()
    {
        ExceptionTypes = ["compile error"],
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why ?? NothingRuns, fix, example),
    };

    private static GuideEntry Warned(string[] names, string explanation, string why, string fix, string example) => new()
    {
        ExceptionTypes = ["compile warning"],
        Codes = names,
        Guide = new MistakeGuide(explanation, why, fix, example),
    };

    private static GuideEntry Raised(string exception, string explanation, string fix, string example, string? message = null, string? why = null) => new()
    {
        ExceptionTypes = [exception],
        Message = message is null ? null : new Regex(message, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why ?? Stops, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Compile(@"^Unbound value[\s\S]*If this is a recursive definition",
            "In OCaml a name can only be used once the let that defines it is finished, so a function cannot call itself unless it is " +
            "defined with let rec - rec makes its own name usable inside it. This line calls the function being defined, and OCaml " +
            "names the line where rec belongs.",
            "Write let rec in place of let on the line OCaml names.",
            """
            let rec factorial n =
              if n = 0 then 1 else n * factorial (n - 1)

            let () = print_int (factorial 5)
            """),

        Compile(@"^Unbound value",
            "Every name in OCaml has to be defined with let before it is used - above the line that uses it, as OCaml reads a file " +
            "from the top - or come from a module, as List.length comes from List. OCaml cannot find this name from here. Capitals " +
            "matter: total and Total are different names. OCaml often says which name it thinks was meant.",
            "Check the spelling against the let that defines the name, and make sure that let is above this line.",
            """
            let total = 3

            let () = print_int total
            """),

        Compile(@"^Unbound module",
            "A name with a capital and a dot after it - Printf.printf - comes from a module, and OCaml has no module of this name. It " +
            "is not part of OCaml's standard library, nor a file of the program beside this one: a file marks.ml is the module Marks.",
            "Check the spelling and the capital letter, or put the file the module is in beside this one.",
            """
            let () = Printf.printf "%d\n" (List.length [1; 2])
            """),

        Compile(@"^Unbound constructor",
            "A word with a capital letter on its own - Some, None, Red - is a constructor: one of the forms a type's values take, " +
            "declared with type. OCaml knows no constructor of this name here.",
            "Check the spelling against the type that declares it, or declare it: type colour = Red | Green.",
            """
            type colour = Red | Green

            let favourite = Green

            let () = print_endline (if favourite = Red then "red" else "green")
            """),

        Compile(@"has type[\s\S]*but an expression was expected of type|This pattern matches values of type",
            "OCaml checks the type of every value, and never turns one type into another by itself - not even an int into a float. " +
            "Here a value of one type is where another is needed: the message gives the type the value has and the type that was " +
            "expected. Whole numbers and decimals have operators of their own: + - * / for ints, and +. -. *. /. for floats.",
            "Give it a value of the type that is expected - float_of_int n, string_of_int n, int_of_float x - or, for decimals, use " +
            "the operators with a dot.",
            """
            let count = 3

            let average = float_of_int 10 /. float_of_int count

            let () = print_float average
            """),

        Compile(@"^Syntax error: ""?(?:\)|\]|\}|\|\]|>\})""? expected|might be unmatched",
            "Brackets come in pairs: every ( needs a ), every [ a ], every { a }. OCaml reached a point where one was still open, and " +
            "shows the one that might be unmatched - which can be a line or more above where it stopped.",
            "Close the bracket OCaml shows, where what is inside it ends.",
            """
            let total = (1 + 2) * 3

            let () = print_int total
            """),

        Compile(@"^Syntax error: ""?(?:end|done)""? expected",
            "Some of OCaml's blocks are marked with words rather than brackets: begin needs its end, and the body of a for or a " +
            "while loop goes between do and done. OCaml reached a point where one of these was still open.",
            "Add the end or the done where the block finishes.",
            """
            let () =
              for i = 1 to 3 do
                print_int i
              done
            """),

        Compile(@"^Syntax error",
            "OCaml could not read the code here: something it needs is missing, or something is where it cannot be. Look at this " +
            "line and at the end of the line before it - OCaml names what it expected when it can.",
            "Compare the line with how OCaml writes the same thing, and add what is missing or move what is out of place.",
            """
            let double x = x * 2

            let () = print_int (double 4)
            """),

        Compile(@"applied to too many arguments",
            "A function takes the number of arguments its definition gives it, and this one is given more. Often a ; is missing " +
            "at the end of the line before, so OCaml reads two lines as one call - which OCaml itself suggests.",
            "Give the function the arguments it takes - and end each line of a sequence but the last with ;.",
            """
            let add a b = a + b

            let () =
              print_int (add 1 2);
              print_newline ()
            """),

        Warned(["partial-match"],
            "A match has a case for each value it is ready for, and this one leaves some out - OCaml shows one it would not match. " +
            "When such a value comes along, the program stops with the exception Match_failure.",
            "The program stops with Match_failure the first time a value no case matches comes along.",
            "Add a case for the values OCaml shows - or a last case | _ -> for every value the others leave.",
            """
            type mark = Pass | Fail | Merit

            let describe mark =
              match mark with
              | Pass -> "passed"
              | Fail -> "failed"
              | Merit -> "merit"

            let () = print_endline (describe Merit)
            """),

        Warned(["redundant-case", "redundant-subpat"],
            "A match tries its cases from the top and takes the first that fits. A case above this one already matches every value " +
            "this one could - a _, or a plain name, matches anything - so this case is never used.",
            "What this case does never happens, whatever the value is.",
            "Put the case that matches anything last, after the cases for particular values.",
            """
            let name n =
              match n with
              | 1 -> "one"
              | _ -> "another"

            let () = print_endline (name 1)
            """),

        Warned(["non-unit-statement"],
            "A line followed by ; is done for what it does, and what it gives back is thrown away. This one gives back a value - " +
            "not unit, OCaml's nothing - so the value is lost: often the result of a function such as List.sort or " +
            "String.uppercase_ascii, which give back a changed copy rather than change what they are given.",
            "The value it works out is thrown away, so the change it was meant to make never happens.",
            "Keep the value with let, or pass it on - or, when it really is not needed, write ignore (...) to say so.",
            """
            let marks = [3; 1; 2]

            let () =
              let sorted = List.sort compare marks in
              List.iter print_int sorted
            """),

        Warned(["ignored-partial-application"],
            "A function given fewer arguments than it takes does not run: it gives back a function waiting for the rest. Here that " +
            "is thrown away, so the function never runs - usually an argument is missing.",
            "The call never happens, so what it was meant to do never does.",
            "Give the function every argument it takes.",
            """
            let greet greeting name = print_endline (greeting ^ ", " ^ name)

            let () = greet "Hello" "Ada"
            """),

        Warned(["unused-rec-flag"],
            "rec lets a function call itself, and this one never does, so rec is not needed. It does no harm, but a function that " +
            "was meant to call itself and does not is sometimes the real mistake.",
            "Nothing goes wrong, but it may mean the function does not do what it was meant to.",
            "Remove rec - or, if the function should call itself, check where it was meant to.",
            """
            let double x = x * 2

            let () = print_int (double 4)
            """),

        Warned(["unused-var", "unused-var-strict", "unused-value-declaration", "unused-open", "unused-open-bang", "unused-for-index"],
            "This is made - given a name with let, or opened - and then never used anywhere. It does the program no harm, but it is " +
            "often a sign that something else was used where this was meant, or that it was left behind after a change.",
            "Nothing goes wrong, but it is clutter - and sometimes a sign the wrong name was used somewhere else.",
            "Use it where it was meant to be used, or remove it. A name that has to be there but is not used can start with _.",
            """
            let () =
              let spare = 3 in
              print_int spare
            """),

        Warned(["deprecated"],
            "What this line uses still works in this version of OCaml, but it has been marked as on its way out, and a later version " +
            "may not have it. The warning says what to use instead.",
            "It works now, but may stop building with a later version of OCaml.",
            "Change it to what the warning suggests.",
            """
            let () = print_endline (String.uppercase_ascii "ada")
            """),

        Raised("Division_by_zero",
            "Dividing a whole number by zero has no answer, so OCaml stops the program with the exception Division_by_zero. The " +
            "number divided by is zero at this point - often because it counts items, and there were none. Dividing floats with /. " +
            "does not stop the program: it gives infinity, or not-a-number for 0. /. 0.",
            "Check the number is not zero before dividing by it, and decide what the answer should be when it is.",
            """
            let average total count = if count = 0 then 0 else total / count

            let () = print_int (average 10 0)
            """,
            why: "The program stops whenever the number divided by is zero - often when a list is empty."),

        Raised("Not_found",
            "A search found nothing: List.assoc, List.find, Hashtbl.find and String.index raise Not_found when what they look for " +
            "is not there. Nothing caught it, so the program stopped.",
            "Use the version ending in _opt - List.assoc_opt, List.find_opt, Hashtbl.find_opt - which gives back None when nothing " +
            "is found, and match on what it gives back.",
            """
            let ages = [("Ada", 36)]

            let () =
              match List.assoc_opt "Bob" ages with
              | Some age -> print_int age
              | None -> print_endline "no age for Bob"
            """),

        Raised("Invalid_argument",
            "The items of an array or a string are numbered from 0, so one of 3 items has positions 0, 1 and 2. This line asks for a " +
            "position outside that - often because for i = 0 to Array.length a goes one too far: OCaml's for counts up to and " +
            "including its last number.",
            "Loop to Array.length a - 1, or go through the items with Array.iter.",
            """
            let marks = [| 70; 80; 90 |]

            let () =
              for i = 0 to Array.length marks - 1 do
                print_int marks.(i)
              done
            """,
            message: @"^index out of bounds$"),

        Raised("Failure",
            "List.hd takes the first item of a list and List.tl the rest - and an empty list has neither, so the program stops.",
            "Match on the list instead: | [] -> for the empty list, and | first :: rest -> for one with items.",
            """
            let first_or_zero marks =
              match marks with
              | [] -> 0
              | first :: _ -> first

            let () = print_int (first_or_zero [])
            """,
            message: @"^(?:hd|tl)$"),

        Raised("Failure",
            "List.nth asks for an item by its position, counted from 0, and the list is shorter than that - a list of 3 items has no " +
            "position 3.",
            "Check the position against List.length first, or use List.nth_opt, which gives back None for a position the list does not have.",
            """
            let () =
              match List.nth_opt [70; 80; 90] 3 with
              | Some mark -> print_int mark
              | None -> print_endline "no fourth mark"
            """,
            message: @"^nth$"),

        Raised("Failure",
            "int_of_string turns text such as \"42\" into a number. It can only do that when the text is exactly a whole number - no " +
            "spaces, no letters, no decimal point - and this text is not.",
            "Use int_of_string_opt, which gives back None for text that is not a number, and match on what it gives back - trimming " +
            "the text first with String.trim.",
            """
            let () =
              match int_of_string_opt (String.trim " 42 ") with
              | Some age -> print_int age
              | None -> print_endline "not a whole number"
            """,
            message: @"^(?:int|float)_of_string$",
            why: "The program stops as soon as it reads text that is not a number."),

        Raised("Match_failure",
            "A match tried every one of its cases and none fitted the value, so the program stopped. The place in brackets is the " +
            "match's: its file, line and column.",
            "Add a case for the value that was not matched, or a last case | _ -> for every value the others leave.",
            """
            let describe n =
              match n with
              | 1 -> "one"
              | _ -> "another"

            let () = print_endline (describe 2)
            """),

        Raised("Stack_overflow",
            "A function that calls itself has to reach a point where it stops. This one kept calling itself until the memory for " +
            "calls ran out.",
            "Give the function a case that returns without calling itself, and make sure each call comes closer to it.",
            """
            let rec count_down n =
              if n <= 0 then 0 else count_down (n - 1)

            let () = print_int (count_down 3)
            """),

        Raised("Assert_failure",
            "assert checks that something is true, and stops the program when it is not. This one was false - the place in brackets " +
            "is the assert's - so something the program relies on did not hold.",
            "Find out why the condition was false here: the assert is right to stop, and the mistake is in what led to it.",
            """
            let share total people =
              assert (people > 0);
              total / people

            let () = print_int (share 10 2)
            """),

        Raised("Failure",
            "The program called failwith - itself, or inside a function it used - which stops it with the message given.",
            "Read the message and what led to it, and handle that case before failwith is reached - or catch it with try ... with Failure message ->.",
            """
            let checked mark = if mark < 0 then failwith "a mark cannot be negative" else mark

            let () = print_int (checked 70)
            """),
    ];
}
