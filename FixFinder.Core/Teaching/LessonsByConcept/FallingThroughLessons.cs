namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>A case that runs into the next, in each language: what it does, shown by running a program that does it.</summary>
internal static class FallingThroughLessons
{
    private static Concept Concept => Concepts.FallingThrough;

    public static IReadOnlyList<Lesson> All { get; } = [Java, JavaScript, CSharp, Go];

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "A case of a switch is only where the program starts: it runs that case's lines and then carries on into the next " +
        "case's, until a break stops it. javac says nothing about a case with no break.",
        [
            new WorkedExample("Naming a day",
                Broken: """
                    public class Day {
                        public static void main(String[] args) {
                            int day = 1;
                            switch (day) {
                                case 1:
                                    System.out.println("Monday");
                                case 2:
                                    System.out.println("Tuesday");
                                    break;
                                default:
                                    System.out.println("Another day");
                            }
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Printing("Monday\nTuesday"),
                Fixed: """
                    public class Day {
                        public static void main(String[] args) {
                            int day = 1;
                            switch (day) {
                                case 1:
                                    System.out.println("Monday");
                                    break;
                                case 2:
                                    System.out.println("Tuesday");
                                    break;
                                default:
                                    System.out.println("Another day");
                            }
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("Monday"),
                WhatChanged: "The break after Monday stops the switch there, so case 2's line is not run as well."),
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "A case of a switch is only where the program starts: it runs that case's lines and then carries on into the next " +
        "case's, until a break stops it - and nothing warns about a case with no break.",
        [
            new WorkedExample("Naming a day",
                Broken: """
                    const day = 1;
                    switch (day) {
                      case 1:
                        console.log("Monday");
                      case 2:
                        console.log("Tuesday");
                        break;
                      default:
                        console.log("Another day");
                    }
                    """,
                BrokenDoes: Behaviour.Printing("Monday\nTuesday"),
                Fixed: """
                    const day = 1;
                    switch (day) {
                      case 1:
                        console.log("Monday");
                        break;
                      case 2:
                        console.log("Tuesday");
                        break;
                      default:
                        console.log("Another day");
                    }
                    """,
                FixedDoes: Behaviour.Printing("Monday"),
                WhatChanged: "The break after Monday stops the switch there, so case 2's line is not run as well."),
        ]);

    private static Lesson CSharp => new(Concept, CodeLanguage.CSharp,
        "C# does not let one case run on into the next: a case with lines in it has to end with a break - or a return, or " +
        "something else that leaves it - and the compiler refuses a switch where one does not, before any of the program runs.",
        [
            new WorkedExample("Naming a day",
                Broken: """
                    int day = 1;
                    switch (day)
                    {
                        case 1:
                            Console.WriteLine("Monday");
                        case 2:
                            Console.WriteLine("Tuesday");
                            break;
                        default:
                            Console.WriteLine("Another day");
                            break;
                    }
                    """,
                BrokenDoes: Behaviour.Refused("CS0163", 4),
                Fixed: """
                    int day = 1;
                    switch (day)
                    {
                        case 1:
                            Console.WriteLine("Monday");
                            break;
                        case 2:
                            Console.WriteLine("Tuesday");
                            break;
                        default:
                            Console.WriteLine("Another day");
                            break;
                    }
                    """,
                FixedDoes: Behaviour.Printing("Monday"),
                WhatChanged: "Each case now ends with a break, as C# requires."),
        ]);

    private static Lesson Go => new(Concept, CodeLanguage.Go,
        "Go's cases never run into the next one: each stops at its end. So an empty case does nothing at all, rather than " +
        "sharing the next case's lines as it would in Java or C - values that should be handled the same way are listed " +
        "together in one case.",
        [
            new WorkedExample("The weekend",
                Broken: """
                    package main

                    import "fmt"

                    func main() {
                    	day := 6
                    	switch day {
                    	case 6:
                    	case 7:
                    		fmt.Println("weekend")
                    	default:
                    		fmt.Println("weekday")
                    	}
                    }
                    """,
                BrokenDoes: Behaviour.Printing(""),
                Fixed: """
                    package main

                    import "fmt"

                    func main() {
                    	day := 6
                    	switch day {
                    	case 6, 7:
                    		fmt.Println("weekend")
                    	default:
                    		fmt.Println("weekday")
                    	}
                    }
                    """,
                FixedDoes: Behaviour.Printing("weekend"),
                WhatChanged: "case 6, 7 handles both days with the same lines; the empty case 6 had handled 6 by doing nothing."),
        ]);
}
