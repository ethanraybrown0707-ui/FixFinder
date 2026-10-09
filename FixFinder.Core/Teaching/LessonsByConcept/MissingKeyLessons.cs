namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Looking up a key that is not there, in each language: what it does, shown by running a program that does it.</summary>
internal static class MissingKeyLessons
{
    private static Concept Concept => Concepts.MissingKey;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, CSharp, Go];

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "Looking a key up in a dictionary with [] stops the program with KeyError when the key is not there. get asks without " +
        "stopping: it gives back the default it is given, or None when it is given none.",
        [
            new WorkedExample("An age nobody stored",
                Broken: """
                    ages = {"Ada": 36}
                    print(ages["Bob"])
                    """,
                BrokenDoes: Behaviour.Stopping("KeyError", 2),
                Fixed: """
                    ages = {"Ada": 36}
                    print(ages.get("Bob", 0))
                    """,
                FixedDoes: Behaviour.Printing("0"),
                WhatChanged: "get gives back the default - here 0 - for a key the dictionary does not have.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        ages = {"Ada": 36}
                        print(ages.get("Bob"))
                        """,
                        Behaviour.Printing("None"),
                        "get with no default gives back None for a missing key: the crash stops, but None is not an age."),
                ],
            },
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "A Java map's get gives back null for a key it does not have - and putting null into an int, which cannot hold it, stops " +
        "the program with a NullPointerException on that line. getOrDefault gives back a value of your choosing instead.",
        [
            new WorkedExample("An age nobody stored",
                Broken: """
                    import java.util.HashMap;
                    import java.util.Map;

                    public class Ages {
                        public static void main(String[] args) {
                            Map<String, Integer> ages = new HashMap<>();
                            ages.put("Ada", 36);
                            int age = ages.get("Bob");
                            System.out.println(age);
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Stopping("NullPointerException", 8),
                Fixed: """
                    import java.util.HashMap;
                    import java.util.Map;

                    public class Ages {
                        public static void main(String[] args) {
                            Map<String, Integer> ages = new HashMap<>();
                            ages.put("Ada", 36);
                            int age = ages.getOrDefault("Bob", 0);
                            System.out.println(age);
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("0"),
                WhatChanged: "getOrDefault gives back 0 for a key the map does not have, so there is never a null to put into the int."),
        ]);

    private static Lesson CSharp => new(Concept, CodeLanguage.CSharp,
        "Looking a key up in a Dictionary with [] stops a C# program with KeyNotFoundException when the key is not there. " +
        "GetValueOrDefault asks without stopping, and gives back the value it is given for a key that is not there.",
        [
            new WorkedExample("An age nobody stored",
                Broken: """
                    var ages = new Dictionary<string, int> { ["Ada"] = 36 };
                    Console.WriteLine(ages["Bob"]);
                    """,
                BrokenDoes: Behaviour.Stopping("KeyNotFoundException", 2),
                Fixed: """
                    var ages = new Dictionary<string, int> { ["Ada"] = 36 };
                    Console.WriteLine(ages.GetValueOrDefault("Bob", 0));
                    """,
                FixedDoes: Behaviour.Printing("0"),
                WhatChanged: "GetValueOrDefault gives back 0 for a key the dictionary does not have, rather than stopping."),
        ]);

    private static Lesson Go => new(Concept, CodeLanguage.Go,
        "Looking up a key a Go map does not have does not stop the program: it gives back the zero value of the map's type - 0 " +
        "for an int - so a missing key looks just like an age of 0. Asked for two values, the second says whether the key was there.",
        [
            new WorkedExample("An age nobody stored",
                Broken: """
                    package main

                    import "fmt"

                    func main() {
                    	ages := map[string]int{"Ada": 36}
                    	fmt.Println(ages["Bob"])
                    }
                    """,
                BrokenDoes: Behaviour.Printing("0"),
                Fixed: """
                    package main

                    import "fmt"

                    func main() {
                    	ages := map[string]int{"Ada": 36}
                    	if age, found := ages["Bob"]; found {
                    		fmt.Println(age)
                    	} else {
                    		fmt.Println("no age for Bob")
                    	}
                    }
                    """,
                FixedDoes: Behaviour.Printing("no age for Bob"),
                WhatChanged: "The second value, found, is false when the key is not there, so the fixed program can tell a missing age from an age of 0."),
        ]);
}
