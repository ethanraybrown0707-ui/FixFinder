namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Values of the wrong type, in each language: what it does, shown by running a program that does it.</summary>
internal static class WrongTypeLessons
{
    private static Concept Concept => Concepts.WrongType;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, JavaScript, OCaml];

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "Python will not join a piece of text and a number with +: it stops with TypeError when the line runs. str turns the " +
        "number into text first - and int turns text into a number, when that is what was meant.",
        [
            new WorkedExample("A message with a number in it",
                Broken: """
                    age = 36
                    print("Ada is " + age)
                    """,
                BrokenDoes: Behaviour.Stopping("TypeError", 2),
                Fixed: """
                    age = 36
                    print("Ada is " + str(age))
                    """,
                FixedDoes: Behaviour.Printing("Ada is 36"),
                WhatChanged: "str(age) turns the number into the text \"36\", which + can join to the rest.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        age = 36
                        print("Ada is " + "age")
                        """,
                        Behaviour.Printing("Ada is age"),
                        "Quotes make it the word age, not the value of age."),
                ],
            },
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "Java checks the type of every value before the program runs: text where a number is needed stops javac with " +
        "\"incompatible types\", and no part of the program runs. Integer.parseInt turns text into a number.",
        [
            new WorkedExample("A number kept as text",
                Broken: """
                    public class Ages {
                        public static void main(String[] args) {
                            String typed = "36";
                            int age = typed;
                            System.out.println(age + 1);
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Refused("incompatible types", 4),
                Fixed: """
                    public class Ages {
                        public static void main(String[] args) {
                            String typed = "36";
                            int age = Integer.parseInt(typed);
                            System.out.println(age + 1);
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("37"),
                WhatChanged: "Integer.parseInt reads the text \"36\" as the number 36, which an int can hold."),
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "JavaScript never stops to say two values are of different types: it turns one into the other's type by rules of its own. " +
        "+ with a piece of text on either side joins them as text, so \"2\" + 1 is \"21\" - and the program carries on with the wrong answer.",
        [
            new WorkedExample("Adding a number typed in",
                Broken: """
                    const typed = "2";
                    console.log(typed + 1);
                    """,
                BrokenDoes: Behaviour.Printing("21"),
                Fixed: """
                    const typed = "2";
                    console.log(Number(typed) + 1);
                    """,
                FixedDoes: Behaviour.Printing("3"),
                WhatChanged: "Number(typed) turns the text into a number first, so + adds instead of joining."),
        ]);

    private static Lesson OCaml => new(Concept, CodeLanguage.OCaml,
        "OCaml checks the type of every value before the program runs, and never turns one type into another by itself - not even " +
        "an int into a float. A whole number where a float is needed stops the compiler, and float_of_int does the turning.",
        [
            new WorkedExample("A whole number where a float is needed",
                Broken: """
                    let count = 3

                    let () = print_float (count *. 2.5)
                    """,
                BrokenDoes: Behaviour.Refused("This expression has type", 3),
                Fixed: """
                    let count = 3

                    let () = print_float (float_of_int count *. 2.5)
                    """,
                FixedDoes: Behaviour.Printing("7.5"),
                WhatChanged: "float_of_int turns the whole number 3 into the float 3., which *. can multiply."),
        ]);
}
