namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Using a value that is not there, in each language: what it does, shown by running a program that does it.</summary>
internal static class NothingThereLessons
{
    private static Concept Concept => Concepts.NothingThere;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, JavaScript, Scala, Rust, Ruby, Php, Lua];

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

    private static Lesson Rust => new(Concept, CodeLanguage.Rust,
        "Rust has no null. A value that may not be there is an Option: Some(value), or None. unwrap takes the value out, and stops " +
        "the program with a panic when there is none - so the Option's None has to be dealt with.",
        [
            new WorkedExample("The first mark of none",
                Broken: """
                    fn main() {
                        let marks: Vec<i32> = Vec::new();
                        let first = marks.first().unwrap();
                        println!("{}", first);
                    }
                    """,
                BrokenDoes: Behaviour.Stopping("called `Option::unwrap()` on a `None` value", 3),
                Fixed: """
                    fn main() {
                        let marks: Vec<i32> = Vec::new();
                        match marks.first() {
                            Some(first) => println!("{}", first),
                            None => println!("no marks"),
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("no marks"),
                WhatChanged: "The match says what to do in each case, None included, instead of taking the value out without looking."),
        ]);

    private static Lesson Ruby => new(Concept, CodeLanguage.Ruby,
        "nil is Ruby's value for \"nothing here\" - what a hash gives for a key it does not have. nil has hardly any methods, so " +
        "calling one on it - even + - stops the program with NoMethodError, \"undefined method for nil\".",
        [
            new WorkedExample("An age that is not there",
                Broken: """
                    ages = { "Ada" => 36 }
                    age = ages["Bob"]
                    puts age + 1
                    """,
                BrokenDoes: Behaviour.Stopping("NoMethodError", 3),
                Fixed: """
                    ages = { "Ada" => 36 }
                    age = ages.fetch("Bob", 0)
                    puts age + 1
                    """,
                FixedDoes: Behaviour.Printing("1"),
                WhatChanged: "fetch with a second value gives that value, 0, for a key that is not there, instead of nil."),
        ]);

    private static Lesson Php => new(Concept, CodeLanguage.Php,
        "null is PHP's value for \"nothing here\" - what ?? null gives for a key an array does not have, and what a function " +
        "without return gives back. Calling a method on null stops the program with Error, Call to a member function ... on null. " +
        "Reading a property of null only warns, and gives null.",
        [
            new WorkedExample("Greeting a student who is not there",
                Broken: """
                    <?php
                    class Student {
                        public function __construct(public string $name) {}

                        public function greeting() {
                            return "Hello, " . $this->name;
                        }
                    }

                    $students = ["ada" => new Student("Ada")];
                    $found = $students["bob"] ?? null;
                    echo $found->greeting();
                    """,
                BrokenDoes: Behaviour.Stopping("Error", 12),
                Fixed: """
                    <?php
                    class Student {
                        public function __construct(public string $name) {}

                        public function greeting() {
                            return "Hello, " . $this->name;
                        }
                    }

                    $students = ["ada" => new Student("Ada")];
                    $found = $students["bob"] ?? null;
                    echo $found === null ? "No such student" : $found->greeting();
                    """,
                FixedDoes: Behaviour.Printing("No such student"),
                WhatChanged: "The fixed line checks for null before calling greeting on what was found."),
        ]);

    private static Lesson Lua => new(Concept, CodeLanguage.Lua,
        "nil is Lua's value for \"nothing here\" - what a table gives for a key it does not have, and what a name holds before it is " +
        "given a value. Looking inside nil stops the program with attempt to index a nil value, naming the key that was not there.",
        [
            new WorkedExample("A student who is not there",
                Broken: """
                    local students = {ada = {name = "Ada"}}
                    print(students.bob.name)
                    """,
                BrokenDoes: Behaviour.Stopping("attempt to index a nil value", 2),
                Fixed: """
                    local students = {ada = {name = "Ada"}}
                    if students.bob then
                      print(students.bob.name)
                    else
                      print("No such student")
                    end
                    """,
                FixedDoes: Behaviour.Printing("No such student"),
                WhatChanged: "The fixed code checks students.bob is there before looking inside it."),
        ]);
}
