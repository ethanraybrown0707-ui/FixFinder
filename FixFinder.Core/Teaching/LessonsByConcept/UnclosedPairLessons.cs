namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Brackets and quotes that are never closed, in each language: what it does, shown by running a program that does it.</summary>
internal static class UnclosedPairLessons
{
    private static Concept Concept => Concepts.UnclosedPair;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, JavaScript, Php, Lua, Perl];

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

    private static Lesson Php => new(Concept, CodeLanguage.Php,
        "Every { needs a } to close it. When one is missing, PHP reads to the end of the file looking for it, then refuses the whole " +
        "file before any of it runs: Unclosed '{' names the line the { that was left open is on, and the line at the end is the last " +
        "line of the file, where PHP gave up looking.",
        [
            new WorkedExample("Printing the higher marks",
                Broken: """
                    <?php
                    $marks = [70, 80, 90];
                    foreach ($marks as $mark) {
                        if ($mark > 75) {
                            echo $mark . "\n";
                    }
                    """,
                BrokenDoes: Behaviour.Refused("ParseError", 6),
                Fixed: """
                    <?php
                    $marks = [70, 80, 90];
                    foreach ($marks as $mark) {
                        if ($mark > 75) {
                            echo $mark . "\n";
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("80\n90"),
                WhatChanged: "The if gets a } of its own, so the last } closes the foreach - the { PHP said was left open, on line 3."),
        ]);

    private static Lesson Lua => new(Concept, CodeLanguage.Lua,
        "Every function, if, for and while block is closed with end. When one is missing, Lua reads on looking for it and refuses the " +
        "whole file before any of it runs, saying which block it is - 'end' expected (to close 'function' at line 1) - and the line " +
        "where it gave up.",
        [
            new WorkedExample("An average",
                Broken: """
                    local function average(marks)
                      local total = 0
                      for _, mark in ipairs(marks) do
                        total = total + mark
                      end
                      return total / #marks

                    print(average({70, 80}))
                    """,
                BrokenDoes: Behaviour.Refused("'end' expected", 8),
                Fixed: """
                    local function average(marks)
                      local total = 0
                      for _, mark in ipairs(marks) do
                        total = total + mark
                      end
                      return total / #marks
                    end

                    print(average({70, 80}))
                    """,
                FixedDoes: Behaviour.Printing("75.0"),
                WhatChanged: "The end after the return closes the function, the block Lua said was opened on line 1."),
        ]);

    private static Lesson Perl => new(Concept, CodeLanguage.Perl,
        "Every { needs a } to close it. When one is missing, Perl reads to the end of the file looking for it and refuses the whole " +
        "file before any of it runs, with Missing right curly or square bracket - naming the last line of the file, where it gave up, " +
        "not the block that is missing it.",
        [
            new WorkedExample("Printing the higher marks",
                Broken: """
                    use strict;
                    use warnings;

                    my @marks = (70, 80);
                    foreach my $mark (@marks) {
                        if ($mark > 75) {
                            print "$mark\n";
                    }
                    """,
                BrokenDoes: Behaviour.Refused("Missing right curly or square bracket", 8),
                Fixed: """
                    use strict;
                    use warnings;

                    my @marks = (70, 80);
                    foreach my $mark (@marks) {
                        if ($mark > 75) {
                            print "$mark\n";
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("80"),
                WhatChanged: "The if gets a } of its own, so the last } closes the foreach."),
        ]);
}
