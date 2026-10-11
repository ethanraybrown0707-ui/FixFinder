namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Values of the wrong type, in each language: what it does, shown by running a program that does it.</summary>
internal static class WrongTypeLessons
{
    private static Concept Concept => Concepts.WrongType;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, JavaScript, OCaml, Rust, Ruby, Php, Lua];

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
                BrokenDoes: Behaviour.Refused("The value count has type int but an expression was expected of type", 3),
                Fixed: """
                    let count = 3

                    let () = print_float (float_of_int count *. 2.5)
                    """,
                FixedDoes: Behaviour.Printing("7.5"),
                WhatChanged: "float_of_int turns the whole number 3 into the float 3., which *. can multiply."),
        ]);

    private static Lesson Rust => new(Concept, CodeLanguage.Rust,
        "Rust checks the type of every value before the program runs, and never turns one into another by itself: text where a " +
        "number is needed is refused with E0308, mismatched types. parse turns text into a number.",
        [
            new WorkedExample("A number kept as text",
                Broken: """
                    fn main() {
                        let count: i32 = "3";
                        println!("{}", count + 1);
                    }
                    """,
                BrokenDoes: Behaviour.Refused("E0308", 2),
                Fixed: """
                    fn main() {
                        let count: i32 = "3".parse().unwrap();
                        println!("{}", count + 1);
                    }
                    """,
                FixedDoes: Behaviour.Printing("4"),
                WhatChanged: "parse turns the text \"3\" into the number 3, which can be added to."),
        ]);

    private static Lesson Ruby => new(Concept, CodeLanguage.Ruby,
        "Ruby never turns a number into text by itself: + between text and a number stops the program with TypeError. .to_s turns " +
        "the number into text first.",
        [
            new WorkedExample("A message with a number in it",
                Broken: """
                    age = 36
                    puts "Age: " + age
                    """,
                BrokenDoes: Behaviour.Stopping("TypeError", 2),
                Fixed: """
                    age = 36
                    puts "Age: " + age.to_s
                    """,
                FixedDoes: Behaviour.Printing("Age: 36"),
                WhatChanged: "age.to_s turns the number into the text \"36\", which + can join to the rest."),
        ]);

    private static Lesson Php => new(Concept, CodeLanguage.Php,
        "In PHP, + adds numbers and . joins text. + between a number and text that does not start with one stops the program with " +
        "TypeError, Unsupported operand types. Text that does start with a number, such as \"5 apples\", is added as that number, " +
        "with a warning. . turns the number into text and joins the two.",
        [
            new WorkedExample("Printing a total",
                Broken: """
                    <?php
                    $total = 12;
                    echo "Total: " + $total;
                    """,
                BrokenDoes: Behaviour.Stopping("TypeError", 3),
                Fixed: """
                    <?php
                    $total = 12;
                    echo "Total: " . $total;
                    """,
                FixedDoes: Behaviour.Printing("Total: 12"),
                WhatChanged: ". joins the text and the number, where + tried to add them."),
        ]);

    private static Lesson Lua => new(Concept, CodeLanguage.Lua,
        "In Lua, + only adds numbers, and .. joins text. + between text that is not a number and a number stops the program with " +
        "attempt to add a 'string' with a 'number'. .. turns the number into text and joins the two.",
        [
            new WorkedExample("Printing a total",
                Broken: """
                    local total = 12
                    print("Total: " + total)
                    """,
                BrokenDoes: Behaviour.Stopping("attempt to add a 'string' with a 'number'", 2),
                Fixed: """
                    local total = 12
                    print("Total: " .. total)
                    """,
                FixedDoes: Behaviour.Printing("Total: 12"),
                WhatChanged: ".. joins the text and the number, where + tried to add them."),
        ]);
}
