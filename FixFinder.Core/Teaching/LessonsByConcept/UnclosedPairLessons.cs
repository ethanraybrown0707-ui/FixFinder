namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Brackets and quotes that are never closed, in each language: what it does, shown by running a program that does it.</summary>
internal static class UnclosedPairLessons
{
    private static Concept Concept => Concepts.UnclosedPair;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, JavaScript];

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "Python reads on from an opening bracket looking for the one that closes it - over line ends too - and refuses the " +
        "program with SyntaxError when the file ends first. It names the line of the bracket that was never closed.",
        [
            new WorkedExample("A list of marks",
                Broken: """
                    marks = [70, 80, 90
                    print(len(marks))
                    """,
                BrokenDoes: Behaviour.Refused("SyntaxError", 1),
                Fixed: """
                    marks = [70, 80, 90]
                    print(len(marks))
                    """,
                FixedDoes: Behaviour.Printing("3"),
                WhatChanged: "The ] closes the list where it ends."),
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "Every { needs its }. When the last one is missing, javac reads to the end of the file still waiting for it, and " +
        "says so on the last line - not where the brace should have been.",
        [
            new WorkedExample("A class with no end",
                Broken: """
                    public class Hello {
                        public static void main(String[] args) {
                            System.out.println("Hello");
                        }
                    """,
                BrokenDoes: Behaviour.Refused("reached end of file while parsing", 4),
                Fixed: """
                    public class Hello {
                        public static void main(String[] args) {
                            System.out.println("Hello");
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("Hello"),
                WhatChanged: "The last } closes the class, which the one before it does not: that one closes main."),
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "A piece of text in quotes has to end on the line it starts on, with the same quote it starts with. Without its " +
        "closing quote, JavaScript refuses the program with SyntaxError before any of it runs.",
        [
            new WorkedExample("Printing a name",
                Broken: """
                    const name = "Ada;
                    console.log(name);
                    """,
                BrokenDoes: Behaviour.Refused("SyntaxError", 1),
                Fixed: """
                    const name = "Ada";
                    console.log(name);
                    """,
                FixedDoes: Behaviour.Printing("Ada"),
                WhatChanged: "The closing quote ends the text after Ada, so the ; is code again."),
        ]);
}
