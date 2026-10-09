namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Assigning or comparing, in each language: what it does, shown by running a program that does it.</summary>
internal static class AssignOrCompareLessons
{
    private static Concept Concept => Concepts.AssignOrCompare;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, JavaScript, C];

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "Python does not allow = in an if's condition at all: it refuses the program with SyntaxError before any of it runs. == " +
        "compares.",
        [
            new WorkedExample("Checking a mark",
                Broken: """
                    mark = 70
                    if mark = 70:
                        print("seventy")
                    """,
                BrokenDoes: Behaviour.Refused("SyntaxError", 2),
                Fixed: """
                    mark = 70
                    if mark == 70:
                        print("seventy")
                    """,
                FixedDoes: Behaviour.Printing("seventy"),
                WhatChanged: "== asks whether mark is 70; = would have stored 70 in it."),
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "An if in Java needs a true-or-false value. mark = 70 stores 70 and is the number 70, so javac refuses the program with " +
        "\"incompatible types\" - an int where a boolean is needed - and no part of it runs. == compares.",
        [
            new WorkedExample("Checking a mark",
                Broken: """
                    public class Mark {
                        public static void main(String[] args) {
                            int mark = 70;
                            if (mark = 70) {
                                System.out.println("seventy");
                            }
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Refused("incompatible types", 4),
                Fixed: """
                    public class Mark {
                        public static void main(String[] args) {
                            int mark = 70;
                            if (mark == 70) {
                                System.out.println("seventy");
                            }
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("seventy"),
                WhatChanged: "== asks whether mark is 70, which is true or false, as an if needs."),
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "JavaScript lets = stand in an if's condition: it stores the value, and the if then asks whether that value counts as " +
        "true. Any number but 0 does, so the if always runs - whatever the variable held before - and nothing stops the program.",
        [
            new WorkedExample("Checking a mark",
                Broken: """
                    let mark = 40;
                    if (mark = 70) {
                      console.log("seventy");
                    }
                    console.log(mark);
                    """,
                BrokenDoes: Behaviour.Printing("seventy\n70"),
                Fixed: """
                    let mark = 40;
                    if (mark === 70) {
                      console.log("seventy");
                    }
                    console.log(mark);
                    """,
                FixedDoes: Behaviour.Printing("40"),
                WhatChanged: "=== compares without changing mark, so the if is false for 40, and mark keeps its value."),
        ]);

    private static Lesson C => new(Concept, CodeLanguage.C,
        "C lets = stand in an if's condition: it stores the value, and any value but 0 counts as true - so the if always runs, and " +
        "the variable's own value is lost.",
        [
            new WorkedExample("Checking a mark",
                Broken: """
                    #include <stdio.h>

                    int main(void) {
                        int mark = 40;
                        if (mark = 70) {
                            printf("seventy\n");
                        }
                        printf("%d\n", mark);
                        return 0;
                    }
                    """,
                BrokenDoes: Behaviour.Printing("seventy\n70"),
                Fixed: """
                    #include <stdio.h>

                    int main(void) {
                        int mark = 40;
                        if (mark == 70) {
                            printf("seventy\n");
                        }
                        printf("%d\n", mark);
                        return 0;
                    }
                    """,
                FixedDoes: Behaviour.Printing("40"),
                WhatChanged: "== compares without changing mark, so the if is false for 40, and mark keeps its value."),
        ]);
}
