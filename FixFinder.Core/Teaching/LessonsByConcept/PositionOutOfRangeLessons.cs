namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>A position that does not exist, in each language: what it does, shown by running a program that does it.</summary>
internal static class PositionOutOfRangeLessons
{
    private static Concept Concept => Concepts.PositionOutOfRange;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, JavaScript];

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "A list's positions run from 0 to one less than its length, and asking for a position past that stops the program with " +
        "IndexError. A position below 0 counts from the end: -1 is the last item, whatever the list's length.",
        [
            new WorkedExample("The last of three marks",
                Broken: """
                    marks = [70, 80, 90]
                    print(marks[3])
                    """,
                BrokenDoes: Behaviour.Stopping("IndexError", 2),
                Fixed: """
                    marks = [70, 80, 90]
                    print(marks[-1])
                    """,
                FixedDoes: Behaviour.Printing("90"),
                WhatChanged: "Three items are at positions 0, 1 and 2, so there is no position 3. -1 counts from the end, so it is the last item.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        marks = [70, 80, 90]
                        print(marks[len(marks)])
                        """,
                        Behaviour.Stopping("IndexError", 2),
                        "len(marks) is 3 - the number of items, which is one past the last position."),
                    new WrongFix("""
                        marks = [70, 80, 90]
                        print(marks[1])
                        """,
                        Behaviour.Printing("80"),
                        "Position 1 is the second item, not the last: positions start at 0."),
                ],
            },
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "An array's positions run from 0 to its length - 1, and asking for any other stops the program with an " +
        "ArrayIndexOutOfBoundsException, which names the position asked for and the length. A loop that keeps going while " +
        "i <= length asks for one too many.",
        [
            new WorkedExample("Printing every mark",
                Broken: """
                    public class Marks {
                        public static void main(String[] args) {
                            int[] marks = {70, 80, 90};
                            for (int i = 0; i <= marks.length; i++) {
                                System.out.println(marks[i]);
                            }
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Stopping("ArrayIndexOutOfBoundsException", 5),
                Fixed: """
                    public class Marks {
                        public static void main(String[] args) {
                            int[] marks = {70, 80, 90};
                            for (int i = 0; i < marks.length; i++) {
                                System.out.println(marks[i]);
                            }
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("70\n80\n90"),
                WhatChanged: "The loop keeps going while i < marks.length, so its last pass is at position 2, the last there is.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        public class Marks {
                            public static void main(String[] args) {
                                int[] marks = {70, 80, 90};
                                for (int i = 0; i <= marks.length - 2; i++) {
                                    System.out.println(marks[i]);
                                }
                            }
                        }
                        """,
                        Behaviour.Printing("70\n80"),
                        "Stopping at length - 2 stops the crash by missing the last mark instead."),
                ],
            },
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "Asking an array for a position it does not have does not stop a JavaScript program: it gives undefined, and the program " +
        "carries on with it - so the mistake only shows up later, or as the word undefined printed.",
        [
            new WorkedExample("The last of three marks",
                Broken: """
                    const marks = [70, 80, 90];
                    console.log(marks[3]);
                    """,
                BrokenDoes: Behaviour.Printing("undefined"),
                Fixed: """
                    const marks = [70, 80, 90];
                    console.log(marks[marks.length - 1]);
                    """,
                FixedDoes: Behaviour.Printing("90"),
                WhatChanged: "Three items are at positions 0, 1 and 2; the last is always at length - 1."),
        ]);
}
