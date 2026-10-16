namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Blocks of code, in each language: what goes wrong with them, shown by running a program that does it.</summary>
internal static class BlocksLessons
{
    private static Concept Concept => Concepts.Blocks;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, PowerShell];

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "Python knows which lines are inside a loop, an if or a function by how far they are indented: the lines after the " +
        "colon have to be indented further than it. A line that should be inside and is not is refused with " +
        "IndentationError, before any of the program runs.",
        [
            new WorkedExample("Printing every mark",
                Broken: """
                    marks = [70, 80]
                    for mark in marks:
                    print(mark)
                    """,
                BrokenDoes: Behaviour.Refused("IndentationError", 3),
                Fixed: """
                    marks = [70, 80]
                    for mark in marks:
                        print(mark)
                    """,
                FixedDoes: Behaviour.Printing("70\n80"),
                WhatChanged: "print(mark) is indented under the for, so it is the loop's body and runs for each mark."),
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "An if with no braces { } controls only the one statement after it. Indenting a second line to match does not put it " +
        "inside: Java ignores indenting, so that line runs every time - and javac says nothing about it.",
        [
            new WorkedExample("A certificate for a pass",
                Broken: """
                    public class Pass {
                        public static void main(String[] args) {
                            int mark = 30;
                            if (mark >= 40)
                                System.out.println("Passed");
                                System.out.println("Certificate sent");
                            System.out.println("Mark checked");
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Printing("Certificate sent\nMark checked"),
                Fixed: """
                    public class Pass {
                        public static void main(String[] args) {
                            int mark = 30;
                            if (mark >= 40) {
                                System.out.println("Passed");
                                System.out.println("Certificate sent");
                            }
                            System.out.println("Mark checked");
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("Mark checked"),
                WhatChanged: "The braces make both lines the if's block, so neither runs for a mark under 40."),
        ]);

    private static Lesson PowerShell => new(Concept, CodeLanguage.PowerShell,
        "After else, PowerShell wants a block in { }. \"else if\" is written as one word, elseif - written as two, PowerShell finds if " +
        "where the block should be, and refuses the script before any of it runs.",
        [
            new WorkedExample("Naming a mark's band",
                Broken: """
                    $mark = 50
                    if ($mark -gt 70) {
                        Write-Output "high"
                    } else if ($mark -gt 40) {
                        Write-Output "pass"
                    }
                    """,
                BrokenDoes: Behaviour.Refused("Missing statement block after 'else' keyword", 4),
                Fixed: """
                    $mark = 50
                    if ($mark -gt 70) {
                        Write-Output "high"
                    } elseif ($mark -gt 40) {
                        Write-Output "pass"
                    }
                    """,
                FixedDoes: Behaviour.Printing("pass"),
                WhatChanged: "elseif, one word, starts the next test with its own block."),
        ]);
}
