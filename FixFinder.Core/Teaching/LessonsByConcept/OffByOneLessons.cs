namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Going one step too far, in each language: what it does, shown by running a program that does it.</summary>
internal static class OffByOneLessons
{
    private static Concept Concept => Concepts.OffByOne;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, Scala, OCaml, Rust, Ruby, Php, Lua];

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "range(a, b) counts from a up to but not including b, so range(1, 5) gives 1, 2, 3 and 4 - not 5. And a loop over " +
        "range(len(marks) + 1) goes one past the last position, and stops with IndexError there.",
        [
            new WorkedExample("Counting from 1 to 5",
                Broken: """
                    for number in range(1, 5):
                        print(number)
                    """,
                BrokenDoes: Behaviour.Printing("1\n2\n3\n4"),
                Fixed: """
                    for number in range(1, 6):
                        print(number)
                    """,
                FixedDoes: Behaviour.Printing("1\n2\n3\n4\n5"),
                WhatChanged: "range stops before its second number, so counting to 5 means giving it 6."),
            new WorkedExample("Printing every mark",
                Broken: """
                    marks = [70, 80, 90]
                    for i in range(len(marks) + 1):
                        print(marks[i])
                    """,
                BrokenDoes: Behaviour.Stopping("IndexError", 3),
                Fixed: """
                    marks = [70, 80, 90]
                    for i in range(len(marks)):
                        print(marks[i])
                    """,
                FixedDoes: Behaviour.Printing("70\n80\n90"),
                WhatChanged: "range(len(marks)) gives exactly the positions there are, 0 to 2.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        marks = [70, 80, 90]
                        for i in range(1, len(marks) + 1):
                            print(marks[i])
                        """,
                        Behaviour.Stopping("IndexError", 3),
                        "Starting at 1 skips the first mark, and the loop still goes one past the end."),
                ],
            },
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "A for loop that keeps going while i < 5 stops before 5, so counting from 1 that way gives 1 to 4. <= takes the last " +
        "step too - which is right for counting to 5, and one too many for the positions of an array.",
        [
            new WorkedExample("Counting from 1 to 5",
                Broken: """
                    public class Count {
                        public static void main(String[] args) {
                            for (int number = 1; number < 5; number++) {
                                System.out.println(number);
                            }
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Printing("1\n2\n3\n4"),
                Fixed: """
                    public class Count {
                        public static void main(String[] args) {
                            for (int number = 1; number <= 5; number++) {
                                System.out.println(number);
                            }
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("1\n2\n3\n4\n5"),
                WhatChanged: "<= keeps going while number is 5 too, so 5 is printed."),
        ]);

    private static Lesson Scala => new(Concept, CodeLanguage.Scala,
        "0 to n counts from 0 up to and including n, and 0 until n stops before it. An array's positions run from 0 to one less " +
        "than its length, so a loop over 0 to marks.length goes one past the end, and stops with ArrayIndexOutOfBoundsException.",
        [
            new WorkedExample("Printing every mark",
                Broken: """
                    object Marks {
                      def main(args: Array[String]): Unit = {
                        val marks = Array(70, 80, 90)
                        for (i <- 0 to marks.length) println(marks(i))
                      }
                    }
                    """,
                BrokenDoes: Behaviour.Stopping("ArrayIndexOutOfBoundsException", 4),
                Fixed: """
                    object Marks {
                      def main(args: Array[String]): Unit = {
                        val marks = Array(70, 80, 90)
                        for (i <- 0 until marks.length) println(marks(i))
                      }
                    }
                    """,
                FixedDoes: Behaviour.Printing("70\n80\n90"),
                WhatChanged: "until stops before marks.length, so the last position used is 2."),
        ]);

    private static Lesson OCaml => new(Concept, CodeLanguage.OCaml,
        "OCaml's for counts up to and including its last number: for i = 0 to 3 goes round four times. An array's positions run " +
        "from 0 to one less than its length, so a loop to Array.length goes one past the end, and stops with " +
        "Invalid_argument(\"index out of bounds\").",
        [
            new WorkedExample("Printing every mark",
                Broken: """
                    let marks = [| 70; 80; 90 |]

                    let () =
                      for i = 0 to Array.length marks do
                        print_int marks.(i);
                        print_newline ()
                      done
                    """,
                BrokenDoes: Behaviour.Stopping("Invalid_argument", 5),
                Fixed: """
                    let marks = [| 70; 80; 90 |]

                    let () =
                      for i = 0 to Array.length marks - 1 do
                        print_int marks.(i);
                        print_newline ()
                      done
                    """,
                FixedDoes: Behaviour.Printing("70\n80\n90"),
                WhatChanged: "Array.length marks - 1 is the last position there is, 2, and for includes it."),
        ]);

    private static Lesson Rust => new(Concept, CodeLanguage.Rust,
        "0..n counts from 0 up to but not including n, and 0..=n up to and including it. A Vec's positions run from 0 to one less " +
        "than its length, so a loop over 0..=marks.len() goes one past the end, and stops with a panic, index out of bounds.",
        [
            new WorkedExample("Printing every mark",
                Broken: """
                    fn main() {
                        let marks = vec![70, 80, 90];
                        for i in 0..=marks.len() {
                            println!("{}", marks[i]);
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Stopping("index out of bounds", 4),
                Fixed: """
                    fn main() {
                        let marks = vec![70, 80, 90];
                        for i in 0..marks.len() {
                            println!("{}", marks[i]);
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("70\n80\n90"),
                WhatChanged: ".. stops before marks.len(), so the last position used is 2."),
        ]);

    private static Lesson Ruby => new(Concept, CodeLanguage.Ruby,
        "0..n counts up to and including n, and 0...n stops before it. An array's positions run from 0 to one less than its length, " +
        "so a loop over 0..marks.length asks for one position too many - and Ruby gives nil for it, which stops the program as soon " +
        "as it is used.",
        [
            new WorkedExample("Doubling every mark",
                Broken: """
                    marks = [70, 80, 90]
                    (0..marks.length).each { |i| puts marks[i] * 2 }
                    """,
                BrokenDoes: Behaviour.Stopping("NoMethodError", 2),
                Fixed: """
                    marks = [70, 80, 90]
                    (0...marks.length).each { |i| puts marks[i] * 2 }
                    """,
                FixedDoes: Behaviour.Printing("140\n160\n180"),
                WhatChanged: "... stops before marks.length, so the last position used is 2."),
        ]);

    private static Lesson Php => new(Concept, CodeLanguage.Php,
        "A for loop runs while its condition is true, so < 5 stops before 5 is reached and <= 5 takes it in. Positions in an array " +
        "are the other way round: they run from 0 to count($marks) - 1, so < count($marks) is right there, and <= reads one past the " +
        "end - which PHP only warns of, Undefined array key, carrying on with nothing in its place.",
        [
            new WorkedExample("Counting to five",
                Broken: """
                    <?php
                    for ($number = 1; $number < 5; $number++) {
                        echo $number . "\n";
                    }
                    """,
                BrokenDoes: Behaviour.Printing("1\n2\n3\n4"),
                Fixed: """
                    <?php
                    for ($number = 1; $number <= 5; $number++) {
                        echo $number . "\n";
                    }
                    """,
                FixedDoes: Behaviour.Printing("1\n2\n3\n4\n5"),
                WhatChanged: "<= 5 lets the loop run once more, for 5 itself."),
        ]);

    private static Lesson Lua => new(Concept, CodeLanguage.Lua,
        "A Lua list's first item is at 1, not 0, and its last at #marks. A loop from 0 asks for marks[0] first - which is nil, with no " +
        "error - so the program carries on with nothing in the place of an item.",
        [
            new WorkedExample("Printing every mark",
                Broken: """
                    local marks = {70, 80, 90}
                    for i = 0, #marks do
                      print(marks[i])
                    end
                    """,
                BrokenDoes: Behaviour.Printing("nil\n70\n80\n90"),
                Fixed: """
                    local marks = {70, 80, 90}
                    for i = 1, #marks do
                      print(marks[i])
                    end
                    """,
                FixedDoes: Behaviour.Printing("70\n80\n90"),
                WhatChanged: "The loop counts from 1, where the list's first item is."),
        ]);
}
