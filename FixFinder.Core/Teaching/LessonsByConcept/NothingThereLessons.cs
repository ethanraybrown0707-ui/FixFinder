namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Using a value that is not there, in each language: what it does, shown by running a program that does it.</summary>
internal static class NothingThereLessons
{
    private static Concept Concept => Concepts.NothingThere;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, JavaScript, Scala];

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "Python's value for nothing is None. A dictionary's get gives it back for a key the dictionary does not have, and a " +
        "function that reaches its end without a return gives it back too. Asking None for a method stops the program with " +
        "AttributeError, and using it as a number stops it with TypeError.",
        [
            new WorkedExample("A nickname that is not there",
                Broken: """
                    nicknames = {"Ada": "ada99"}
                    nickname = nicknames.get("Bob")
                    print(nickname.upper())
                    """,
                BrokenDoes: Behaviour.Stopping("AttributeError", 3),
                Fixed: """
                    nicknames = {"Ada": "ada99"}
                    nickname = nicknames.get("Bob")
                    if nickname is None:
                        print("Bob has no nickname")
                    else:
                        print(nickname.upper())
                    """,
                FixedDoes: Behaviour.Printing("Bob has no nickname"),
                WhatChanged: "get gives back None for a key it does not have, and the fixed program checks for None before using what it got.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        nicknames = {"Ada": "ada99"}
                        nickname = nicknames.get("Bob")
                        print(str(nickname).upper())
                        """,
                        Behaviour.Printing("NONE"),
                        "str turns None into the text None, so the crash stops - but it prints NONE, which is nobody's nickname."),
                    new WrongFix("""
                        nicknames = {"Ada": "ada99"}
                        nickname = nicknames["Bob"]
                        print(nickname.upper())
                        """,
                        Behaviour.Stopping("KeyError", 2),
                        "Looking the key up with [] fails sooner: it stops with KeyError when the key is not there."),
                ],
            },
            new WorkedExample("A function that sometimes returns nothing",
                Broken: """
                    def find_mark(name):
                        if name == "Ada":
                            return 70

                    mark = find_mark("Bob")
                    print(mark + 5)
                    """,
                BrokenDoes: Behaviour.Stopping("TypeError", 6),
                Fixed: """
                    def find_mark(name):
                        if name == "Ada":
                            return 70
                        return 0

                    mark = find_mark("Bob")
                    print(mark + 5)
                    """,
                FixedDoes: Behaviour.Printing("5"),
                WhatChanged: "For any name but Ada the function reached its end without a return, so it gave back None; the fixed function returns 0 then."),
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "Java's value for nothing is null. A map's get gives it back for a key the map does not have, and calling a method on null " +
        "stops the program with a NullPointerException. Printing null does not stop it: Java prints the word null.",
        [
            new WorkedExample("A nickname that is not there",
                Broken: """
                    import java.util.HashMap;
                    import java.util.Map;

                    public class Nicknames {
                        public static void main(String[] args) {
                            Map<String, String> nicknames = new HashMap<>();
                            nicknames.put("Ada", "ada99");
                            String nickname = nicknames.get("Bob");
                            System.out.println(nickname.toUpperCase());
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Stopping("NullPointerException", 9),
                Fixed: """
                    import java.util.HashMap;
                    import java.util.Map;

                    public class Nicknames {
                        public static void main(String[] args) {
                            Map<String, String> nicknames = new HashMap<>();
                            nicknames.put("Ada", "ada99");
                            String nickname = nicknames.getOrDefault("Bob", "none");
                            System.out.println(nickname.toUpperCase());
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("NONE"),
                WhatChanged: "getOrDefault gives back the value it is given - here \"none\" - for a key the map does not have, rather than null.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        import java.util.HashMap;
                        import java.util.Map;

                        public class Nicknames {
                            public static void main(String[] args) {
                                Map<String, String> nicknames = new HashMap<>();
                                nicknames.put("Ada", "ada99");
                                String nickname = nicknames.get("Bob");
                                System.out.println(nickname);
                            }
                        }
                        """,
                        Behaviour.Printing("null"),
                        "Printing null does not stop the program - Java prints the word null - but the nickname in capitals is never made."),
                ],
            },
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "JavaScript's value for something missing is undefined: it is what an object gives back for a property it does not have. " +
        "Asking undefined for a method stops the program with a TypeError; printing it does not - JavaScript prints the word undefined.",
        [
            new WorkedExample("A nickname that is not there",
                Broken: """
                    const nicknames = { Ada: "ada99" };
                    const nickname = nicknames["Bob"];
                    console.log(nickname.toUpperCase());
                    """,
                BrokenDoes: Behaviour.Stopping("TypeError", 3),
                Fixed: """
                    const nicknames = { Ada: "ada99" };
                    const nickname = nicknames["Bob"];
                    console.log(nickname === undefined ? "Bob has no nickname" : nickname.toUpperCase());
                    """,
                FixedDoes: Behaviour.Printing("Bob has no nickname"),
                WhatChanged: "The fixed line checks for undefined before asking it for a method.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        const nicknames = { Ada: "ada99" };
                        const nickname = nicknames["Bob"];
                        console.log(nickname);
                        """,
                        Behaviour.Printing("undefined"),
                        "Printing undefined does not stop the program, but nothing says the nickname is missing - it prints the word undefined."),
                ],
            },
        ]);

    private static Lesson Scala => new(Concept, CodeLanguage.Scala,
        "Scala's way of saying a value may be missing is an Option: Some(value), or None. A Map's get gives back an Option, and " +
        "taking the value out of a None with .get stops the program with NoSuchElementException.",
        [
            new WorkedExample("A nickname that is not there",
                Broken: """
                    object Nicknames {
                      def main(args: Array[String]): Unit = {
                        val nicknames = Map("Ada" -> "ada99")
                        val nickname = nicknames.get("Bob").get
                        println(nickname.toUpperCase)
                      }
                    }
                    """,
                BrokenDoes: Behaviour.Stopping("NoSuchElementException", 4),
                Fixed: """
                    object Nicknames {
                      def main(args: Array[String]): Unit = {
                        val nicknames = Map("Ada" -> "ada99")
                        val nickname = nicknames.getOrElse("Bob", "none")
                        println(nickname.toUpperCase)
                      }
                    }
                    """,
                FixedDoes: Behaviour.Printing("NONE"),
                WhatChanged: "getOrElse gives back the value it is given - here \"none\" - for a key the map does not have, so there is no None to take a value out of.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        object Nicknames {
                          def main(args: Array[String]): Unit = {
                            val nicknames = Map("Ada" -> "ada99")
                            val nickname = nicknames("Bob")
                            println(nickname.toUpperCase)
                          }
                        }
                        """,
                        Behaviour.Stopping("NoSuchElementException", 4),
                        "Looking the key up with () fails the same way when it is not there - NoSuchElementException, key not found."),
                ],
            },
        ]);
}
