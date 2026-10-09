namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>A variable used before it is given a value, in each language: what it does, shown by running a program that does it.</summary>
internal static class ReadBeforeSetLessons
{
    private static Concept Concept => Concepts.ReadBeforeSet;

    public static IReadOnlyList<Lesson> All { get; } = [Java, CSharp, Python, JavaScript];

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "javac checks that every variable inside a method is given a value before it is read, on every way through the " +
        "method, and refuses the program when one might not be.",
        [
            new WorkedExample("Adding up marks",
                Broken: """
                    public class Total {
                        public static void main(String[] args) {
                            int total;
                            int[] marks = {70, 80};
                            for (int mark : marks) {
                                total = total + mark;
                            }
                            System.out.println(total);
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Refused("variable total might not have been initialized", 6),
                Fixed: """
                    public class Total {
                        public static void main(String[] args) {
                            int total = 0;
                            int[] marks = {70, 80};
                            for (int mark : marks) {
                                total = total + mark;
                            }
                            System.out.println(total);
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("150"),
                WhatChanged: "total starts at 0, so there is a value to add the first mark to."),
        ]);

    private static Lesson CSharp => new(Concept, CodeLanguage.CSharp,
        "The C# compiler checks that every local variable is given a value before it is read, and refuses the program with " +
        "CS0165, \"use of unassigned local variable\", when one might not be.",
        [
            new WorkedExample("Adding up marks",
                Broken: """
                    int total;
                    int[] marks = { 70, 80 };
                    foreach (int mark in marks)
                    {
                        total = total + mark;
                    }
                    Console.WriteLine(total);
                    """,
                BrokenDoes: Behaviour.Refused("CS0165", 5),
                Fixed: """
                    int total = 0;
                    int[] marks = { 70, 80 };
                    foreach (int mark in marks)
                    {
                        total = total + mark;
                    }
                    Console.WriteLine(total);
                    """,
                FixedDoes: Behaviour.Printing("150"),
                WhatChanged: "total starts at 0, so there is a value to add the first mark to."),
        ]);

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "A function that gives a name a value anywhere in it treats that name as its own variable all the way through - so " +
        "reading it before that line stops the program with UnboundLocalError, even when a variable of the same name " +
        "exists outside the function.",
        [
            new WorkedExample("Adding a mark to a total",
                Broken: """
                    total = 0

                    def add(mark):
                        total = total + mark
                        return total

                    print(add(70))
                    """,
                BrokenDoes: Behaviour.Stopping("UnboundLocalError", 4),
                Fixed: """
                    def add(total, mark):
                        return total + mark

                    total = add(0, 70)
                    print(total)
                    """,
                FixedDoes: Behaviour.Printing("70"),
                WhatChanged: "The total is given to the function, and the new total handed back, so the function only reads names it has been given."),
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "A variable made with let and no value holds undefined, and JavaScript carries on with it: undefined + 70 is NaN, " +
        "\"not a number\", and so is everything worked out from that.",
        [
            new WorkedExample("Adding up marks",
                Broken: """
                    let total;
                    const marks = [70, 80];
                    for (const mark of marks) {
                      total = total + mark;
                    }
                    console.log(total);
                    """,
                BrokenDoes: Behaviour.Printing("NaN"),
                Fixed: """
                    let total = 0;
                    const marks = [70, 80];
                    for (const mark of marks) {
                      total = total + mark;
                    }
                    console.log(total);
                    """,
                FixedDoes: Behaviour.Printing("150"),
                WhatChanged: "total starts at 0, so the first mark is added to a number."),
        ]);
}
