namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Changing a list while looping over it, in each language: what it does, shown by running a program that does it.</summary>
internal static class ChangingWhileLoopingLessons
{
    private static Concept Concept => Concepts.ChangingWhileLooping;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java];

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "A for loop over a list goes through its positions in turn. Removing an item moves every item after it back one " +
        "position, so the item that moves into the place just looked at is never looked at - Python carries on without a " +
        "word, with items skipped.",
        [
            new WorkedExample("Removing the fails",
                Broken: """
                    marks = [40, 35, 70, 30]
                    for mark in marks:
                        if mark < 50:
                            marks.remove(mark)
                    print(marks)
                    """,
                BrokenDoes: Behaviour.Printing("[35, 70]"),
                Fixed: """
                    marks = [40, 35, 70, 30]
                    marks = [mark for mark in marks if mark >= 50]
                    print(marks)
                    """,
                FixedDoes: Behaviour.Printing("[70]"),
                WhatChanged: "The fixed program makes a new list of the marks to keep, instead of removing from the list being looped over."),
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "A for-each loop over a list notices when the list is changed by anything but itself, and stops the program with a " +
        "ConcurrentModificationException the next time round. removeIf removes every item that matches, safely.",
        [
            new WorkedExample("Removing the fails",
                Broken: """
                    import java.util.ArrayList;
                    import java.util.List;

                    public class Marks {
                        public static void main(String[] args) {
                            List<Integer> marks = new ArrayList<>(List.of(40, 70, 30));
                            for (int mark : marks) {
                                if (mark < 50) {
                                    marks.remove(Integer.valueOf(mark));
                                }
                            }
                            System.out.println(marks);
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Stopping("ConcurrentModificationException", 7),
                Fixed: """
                    import java.util.ArrayList;
                    import java.util.List;

                    public class Marks {
                        public static void main(String[] args) {
                            List<Integer> marks = new ArrayList<>(List.of(40, 70, 30));
                            marks.removeIf(mark -> mark < 50);
                            System.out.println(marks);
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("[70]"),
                WhatChanged: "removeIf goes through the list and removes the marks under 50 itself, so no loop is left walking a list that changes."),
        ]);
}
