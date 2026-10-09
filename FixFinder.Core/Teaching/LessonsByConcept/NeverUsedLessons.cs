namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Something made and never used, in each language: what it does, shown by running a program that does it.</summary>
internal static class NeverUsedLessons
{
    private static Concept Concept => Concepts.NeverUsed;

    public static IReadOnlyList<Lesson> All { get; } = [Go, Python];

    private static Lesson Go => new(Concept, CodeLanguage.Go,
        "Go refuses to build a program with a variable that is made and never used - which often catches a result worked out " +
        "under one name and then printed from another.",
        [
            new WorkedExample("A total printed from the wrong name",
                Broken: """
                    package main

                    import "fmt"

                    func main() {
                    	total := 0
                    	result := 70 + 80
                    	fmt.Println(total)
                    }
                    """,
                BrokenDoes: Behaviour.Refused("declared and not used", 7),
                Fixed: """
                    package main

                    import "fmt"

                    func main() {
                    	total := 70 + 80
                    	fmt.Println(total)
                    }
                    """,
                FixedDoes: Behaviour.Printing("150"),
                WhatChanged: "The sum is kept in total, the name that is printed."),
        ]);

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "Python says nothing about a variable that is never used: the program runs, and a result worked out under one name " +
        "and printed from another prints the wrong value.",
        [
            new WorkedExample("A total printed from the wrong name",
                Broken: """
                    total = 0
                    result = 70 + 80
                    print(total)
                    """,
                BrokenDoes: Behaviour.Printing("0"),
                Fixed: """
                    total = 70 + 80
                    print(total)
                    """,
                FixedDoes: Behaviour.Printing("150"),
                WhatChanged: "The sum is kept in total, the name that is printed."),
        ]);
}
