namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Where a statement ends, in each language: what goes wrong, shown by running a program that does it.</summary>
internal static class StatementEndLessons
{
    private static Concept Concept => Concepts.StatementEnd;

    public static IReadOnlyList<Lesson> All { get; } = [Java, CSharp, JavaScript];

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "Every statement ends with a semicolon. Without one, javac refuses the program with \"';' expected\" before any of it " +
        "runs, naming the line the semicolon is missing from.",
        [
            new WorkedExample("Printing a name",
                Broken: """
                    public class Hello {
                        public static void main(String[] args) {
                            String name = "Ada"
                            System.out.println(name);
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Refused("';' expected", 3),
                Fixed: """
                    public class Hello {
                        public static void main(String[] args) {
                            String name = "Ada";
                            System.out.println(name);
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("Ada"),
                WhatChanged: "The ; ends the line that makes name."),
        ]);

    private static Lesson CSharp => new(Concept, CodeLanguage.CSharp,
        "Every statement ends with a semicolon, and without one the compiler refuses the program before any of it runs - " +
        "usually with CS1002, \"; expected\". After a line that makes a variable it can say something else: such a line " +
        "could go on to make another variable, after a comma, so the compiler says CS1003, \"',' expected\".",
        [
            new WorkedExample("Printing two names",
                Broken: """
                    Console.WriteLine("Ada")
                    Console.WriteLine("Grace");
                    """,
                BrokenDoes: Behaviour.Refused("CS1002", 1),
                Fixed: """
                    Console.WriteLine("Ada");
                    Console.WriteLine("Grace");
                    """,
                FixedDoes: Behaviour.Printing("Ada\nGrace"),
                WhatChanged: "The ; ends the first statement, so the second is read as a statement of its own."),
            new WorkedExample("Making a name",
                Broken: """
                    string name = "Ada"
                    Console.WriteLine(name);
                    """,
                BrokenDoes: Behaviour.Refused("CS1003", 1),
                Fixed: """
                    string name = "Ada";
                    Console.WriteLine(name);
                    """,
                FixedDoes: Behaviour.Printing("Ada"),
                WhatChanged: "The ; ends the line that makes name. The compiler asked for a comma only because, with no ; there, it read on expecting another variable."),
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "JavaScript puts in a missing semicolon itself where a line could end - and a return on its own line can end there. " +
        "The value on the line after it is then never returned: the function gives back undefined, without a word.",
        [
            new WorkedExample("A total on the next line",
                Broken: """
                    function total() {
                      return
                        70 + 80;
                    }
                    console.log(total());
                    """,
                BrokenDoes: Behaviour.Printing("undefined"),
                Fixed: """
                    function total() {
                      return 70 + 80;
                    }
                    console.log(total());
                    """,
                FixedDoes: Behaviour.Printing("150"),
                WhatChanged: "The value is on the same line as return, so it is what the function gives back."),
        ]);
}
