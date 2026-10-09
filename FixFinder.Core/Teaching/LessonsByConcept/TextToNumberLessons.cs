namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Turning text into a number, in each language: what it does, shown by running a program that does it.</summary>
internal static class TextToNumberLessons
{
    private static Concept Concept => Concepts.TextToNumber;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, CSharp, JavaScript, OCaml];

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
}
