namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Turning text into a number, in each language: what it does, shown by running a program that does it.</summary>
internal static class TextToNumberLessons
{
    private static Concept Concept => Concepts.TextToNumber;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, CSharp, JavaScript, OCaml, Rust, Ruby, Php, Lua];

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "int turns text such as \"12\" into a number, and stops the program with ValueError when the text is not a whole number. " +
        "isdigit says first whether every character of the text is a digit.",
        [
            new WorkedExample("An age typed as a word",
                Broken: """
                    typed = "twelve"
                    age = int(typed)
                    print(age + 1)
                    """,
                BrokenDoes: Behaviour.Stopping("ValueError", 2),
                Fixed: """
                    typed = "twelve"
                    if typed.isdigit():
                        print(int(typed) + 1)
                    else:
                        print("not a whole number")
                    """,
                FixedDoes: Behaviour.Printing("not a whole number"),
                WhatChanged: "The fixed program turns the text into a number only when every character of it is a digit."),
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "Integer.parseInt turns text into a number, and stops the program with a NumberFormatException when the text is not a " +
        "whole number. Catching that exception lets the program say so and carry on.",
        [
            new WorkedExample("An age typed as a word",
                Broken: """
                    public class Age {
                        public static void main(String[] args) {
                            String typed = "twelve";
                            int age = Integer.parseInt(typed);
                            System.out.println(age + 1);
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Stopping("NumberFormatException", 4),
                Fixed: """
                    public class Age {
                        public static void main(String[] args) {
                            String typed = "twelve";
                            try {
                                int age = Integer.parseInt(typed);
                                System.out.println(age + 1);
                            } catch (NumberFormatException e) {
                                System.out.println("not a whole number");
                            }
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("not a whole number"),
                WhatChanged: "The catch handles text that is not a number, so the program says so instead of stopping."),
        ]);

    private static Lesson CSharp => new(Concept, CodeLanguage.CSharp,
        "int.Parse turns text into a number, and stops the program with a FormatException when the text is not a whole number. " +
        "int.TryParse says whether it could, and gives the number when it could, without stopping.",
        [
            new WorkedExample("An age typed as a word",
                Broken: """
                    string typed = "twelve";
                    int age = int.Parse(typed);
                    Console.WriteLine(age + 1);
                    """,
                BrokenDoes: Behaviour.Stopping("FormatException", 2),
                Fixed: """
                    string typed = "twelve";
                    if (int.TryParse(typed, out int age))
                    {
                        Console.WriteLine(age + 1);
                    }
                    else
                    {
                        Console.WriteLine("not a whole number");
                    }
                    """,
                FixedDoes: Behaviour.Printing("not a whole number"),
                WhatChanged: "TryParse gives back false for text that is not a number, rather than stopping the program."),
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "Number turns text into a number, and does not stop the program when the text is not one: it gives NaN, \"not a number\", " +
        "and everything worked out from NaN is NaN too. Number.isNaN says whether that happened.",
        [
            new WorkedExample("An age typed as a word",
                Broken: """
                    const age = Number("twelve");
                    console.log(age + 1);
                    """,
                BrokenDoes: Behaviour.Printing("NaN"),
                Fixed: """
                    const age = Number("twelve");
                    console.log(Number.isNaN(age) ? "not a number" : age + 1);
                    """,
                FixedDoes: Behaviour.Printing("not a number"),
                WhatChanged: "The fixed line checks for NaN before using the number."),
        ]);

    private static Lesson OCaml => new(Concept, CodeLanguage.OCaml,
        "int_of_string turns text into a number, and stops the program with Failure(\"int_of_string\") when the text is not a " +
        "whole number. int_of_string_opt gives back None instead, and Some of the number when it is one.",
        [
            new WorkedExample("An age typed as a word",
                Broken: """
                    let age = int_of_string "twelve"

                    let () = print_int (age + 1)
                    """,
                BrokenDoes: Behaviour.Stopping("Failure", 1),
                Fixed: """
                    let () =
                      match int_of_string_opt "twelve" with
                      | Some age -> print_int (age + 1)
                      | None -> print_string "not a whole number"
                    """,
                FixedDoes: Behaviour.Printing("not a whole number"),
                WhatChanged: "int_of_string_opt gives back None for text that is not a number, and the match handles that case."),
        ]);

    private static Lesson Rust => new(Concept, CodeLanguage.Rust,
        "parse turns text into a number, and gives back a Result: Ok with the number, or Err when the text is not one. unwrap on an " +
        "Err stops the program with a panic; a match says what to do instead.",
        [
            new WorkedExample("An age typed as a word",
                Broken: """
                    fn main() {
                        let typed = "twelve";
                        let age: i32 = typed.parse().unwrap();
                        println!("{}", age + 1);
                    }
                    """,
                BrokenDoes: Behaviour.Stopping("called `Result::unwrap()` on an `Err` value", 3),
                Fixed: """
                    fn main() {
                        let typed = "twelve";
                        match typed.parse::<i32>() {
                            Ok(age) => println!("{}", age + 1),
                            Err(_) => println!("not a whole number"),
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("not a whole number"),
                WhatChanged: "The match handles the Err that parse gives for text that is not a number, instead of stopping."),
        ]);

    private static Lesson Ruby => new(Concept, CodeLanguage.Ruby,
        "Integer() turns text into a number, and stops the program with ArgumentError when the text is not one. Given " +
        "exception: false, it gives nil instead, which the program can check for.",
        [
            new WorkedExample("An age typed as a word",
                Broken: """
                    age = Integer("twelve")
                    puts age + 1
                    """,
                BrokenDoes: Behaviour.Stopping("ArgumentError", 1),
                Fixed: """
                    age = Integer("twelve", exception: false)
                    puts age.nil? ? "not a whole number" : age + 1
                    """,
                FixedDoes: Behaviour.Printing("not a whole number"),
                WhatChanged: "With exception: false, Integer() gives nil for text that is not a number, and the line checks for nil."),
        ]);

    private static Lesson Php => new(Concept, CodeLanguage.Php,
        "(int) turns text into a whole number, but never says when the text is not one: (int) of \"twenty\" is 0, and the program " +
        "carries on with the 0. is_numeric says first whether the text is a number at all.",
        [
            new WorkedExample("An age typed in words",
                Broken: """
                    <?php
                    $typed = "twenty";
                    $age = (int)$typed;
                    echo $age + 1;
                    """,
                BrokenDoes: Behaviour.Printing("1"),
                Fixed: """
                    <?php
                    $typed = "twenty";
                    if (is_numeric($typed)) {
                        echo (int)$typed + 1;
                    } else {
                        echo "Please type a number";
                    }
                    """,
                FixedDoes: Behaviour.Printing("Please type a number"),
                WhatChanged: "is_numeric checks the text before it is used as a number, so text that is not one is said rather than taken as 0."),
        ]);

    private static Lesson Lua => new(Concept, CodeLanguage.Lua,
        "Text is never equal to a number in Lua - \"5\" == 5 is false - and Lua says nothing of it, so a comparison like that gives " +
        "the same answer every time. What io.read() reads is text. tonumber turns text into a number, or gives nil for text that is not one.",
        [
            new WorkedExample("Checking what was typed",
                Broken: """
                    local typed = "5"
                    if typed == 5 then
                      print("five")
                    else
                      print("not five")
                    end
                    """,
                BrokenDoes: Behaviour.Printing("not five"),
                Fixed: """
                    local typed = "5"
                    if tonumber(typed) == 5 then
                      print("five")
                    else
                      print("not five")
                    end
                    """,
                FixedDoes: Behaviour.Printing("five"),
                WhatChanged: "tonumber turns the text into the number 5 before it is compared with 5."),
        ]);
}
