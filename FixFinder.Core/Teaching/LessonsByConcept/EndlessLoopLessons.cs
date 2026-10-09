namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>A loop that never ends, in each language: what it does, shown by running a program that does it.</summary>
internal static class EndlessLoopLessons
{
    private static Concept Concept => Concepts.EndlessLoop;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java];

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "A while loop runs until its condition is false, and nothing makes the condition change but the loop's own lines. " +
        "A loop that never moves on to the next position, or that steps past the value it is waiting for, runs until it is " +
        "stopped.",
        [
            new WorkedExample("Adding up marks",
                Broken: """
                    marks = [70, 80, 90]
                    total = 0
                    i = 0
                    while i < len(marks):
                        total = total + marks[i]
                    print(total)
                    """,
                BrokenDoes: Behaviour.Running(),
                Fixed: """
                    marks = [70, 80, 90]
                    total = 0
                    i = 0
                    while i < len(marks):
                        total = total + marks[i]
                        i = i + 1
                    print(total)
                    """,
                FixedDoes: Behaviour.Printing("240"),
                WhatChanged: "i = i + 1 moves the loop on to the next mark, so i reaches len(marks) and the loop ends."),
            new WorkedExample("Counting down in threes",
                Broken: """
                    number = 10
                    steps = 0
                    while number != 0:
                        number = number - 3
                        steps = steps + 1
                    print(steps)
                    """,
                BrokenDoes: Behaviour.Running(),
                Fixed: """
                    number = 10
                    steps = 0
                    while number > 0:
                        number = number - 3
                        steps = steps + 1
                    print(steps)
                    """,
                FixedDoes: Behaviour.Printing("4"),
                WhatChanged: "number goes 10, 7, 4, 1, -2 and is never exactly 0, so the loop now stops once it is 0 or below."),
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "A while loop runs until its condition is false, and nothing makes the condition change but the loop's own lines. " +
        "A loop whose position is never moved on runs until it is stopped.",
        [
            new WorkedExample("Adding up marks",
                Broken: """
                    public class Total {
                        public static void main(String[] args) {
                            int[] marks = {70, 80, 90};
                            int total = 0;
                            int i = 0;
                            while (i < marks.length) {
                                total = total + marks[i];
                            }
                            System.out.println(total);
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Running(),
                Fixed: """
                    public class Total {
                        public static void main(String[] args) {
                            int[] marks = {70, 80, 90};
                            int total = 0;
                            int i = 0;
                            while (i < marks.length) {
                                total = total + marks[i];
                                i++;
                            }
                            System.out.println(total);
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("240"),
                WhatChanged: "i++ moves the loop on to the next mark, so i reaches marks.length and the loop ends."),
        ]);
}
