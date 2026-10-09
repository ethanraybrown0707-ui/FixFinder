namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>A name that has not been defined, in each language: what it does, shown by running a program that does it.</summary>
internal static class UndefinedNameLessons
{
    private static Concept Concept => Concepts.UndefinedName;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, JavaScript, Scala, OCaml];

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "Python looks a name up only when the line using it runs, so a misspelt name stops the program with NameError at that " +
        "line - after every line before it has already run.",
        [
            new WorkedExample("A total spelt two ways",
                Broken: """
                    total = 5
                    print(totl)
                    """,
                BrokenDoes: Behaviour.Stopping("NameError", 2),
                Fixed: """
                    total = 5
                    print(total)
                    """,
                FixedDoes: Behaviour.Printing("5"),
                WhatChanged: "The name is spelt as it was when it was made: total.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        total = 5
                        print("totl")
                        """,
                        Behaviour.Printing("totl"),
                        "Quotes make it a piece of text: it prints the letters totl, not the value of total."),
                ],
            },
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "Java checks every name before the program runs: a name it cannot find stops javac with \"cannot find symbol\", and no " +
        "part of the program runs.",
        [
            new WorkedExample("A total spelt two ways",
                Broken: """
                    public class Total {
                        public static void main(String[] args) {
                            int total = 5;
                            System.out.println(totl);
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Refused("cannot find symbol", 4),
                Fixed: """
                    public class Total {
                        public static void main(String[] args) {
                            int total = 5;
                            System.out.println(total);
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("5"),
                WhatChanged: "The name is spelt as it was declared: total."),
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "JavaScript looks a name up when the line using it runs, so a misspelt name stops the program with ReferenceError at that line.",
        [
            new WorkedExample("A total spelt two ways",
                Broken: """
                    const total = 5;
                    console.log(totl);
                    """,
                BrokenDoes: Behaviour.Stopping("ReferenceError", 2),
                Fixed: """
                    const total = 5;
                    console.log(total);
                    """,
                FixedDoes: Behaviour.Printing("5"),
                WhatChanged: "The name is spelt as it was declared: total."),
        ]);

    private static Lesson Scala => new(Concept, CodeLanguage.Scala,
        "Scala checks every name before the program runs: a name it cannot find stops the compiler with \"Not found\", and no part " +
        "of the program runs.",
        [
            new WorkedExample("A total spelt two ways",
                Broken: """
                    object Total {
                      def main(args: Array[String]): Unit = {
                        val total = 5
                        println(totl)
                      }
                    }
                    """,
                BrokenDoes: Behaviour.Refused("Not found", 4),
                Fixed: """
                    object Total {
                      def main(args: Array[String]): Unit = {
                        val total = 5
                        println(total)
                      }
                    }
                    """,
                FixedDoes: Behaviour.Printing("5"),
                WhatChanged: "The name is spelt as it was defined: total."),
        ]);

    private static Lesson OCaml => new(Concept, CodeLanguage.OCaml,
        "OCaml checks every name before the program runs: a name it cannot find stops the compiler with \"Unbound value\", and no " +
        "part of the program runs.",
        [
            new WorkedExample("A total spelt two ways",
                Broken: """
                    let total = 5

                    let () = print_int totl
                    """,
                BrokenDoes: Behaviour.Refused("Unbound value", 3),
                Fixed: """
                    let total = 5

                    let () = print_int total
                    """,
                FixedDoes: Behaviour.Printing("5"),
                WhatChanged: "The name is spelt as it was defined: total."),
        ]);
}
