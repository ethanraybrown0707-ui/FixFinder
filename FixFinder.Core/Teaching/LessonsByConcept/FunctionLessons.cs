namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>
/// Giving a function what it needs, what a function gives back, and a function that calls itself for ever - each in the
/// languages that do something different about it, shown by running a program that does it.
/// </summary>
internal static class FunctionLessons
{
    public static IReadOnlyList<Lesson> All { get; } =
    [
        ArgumentsInPython, ArgumentsInJava, ArgumentsInJavaScript,
        ReturnsInPython, ReturnsInJava, ReturnsInJavaScript,
        RecursionInPython, RecursionInJava,
    ];

    private static Lesson ArgumentsInPython => new(Concepts.Arguments, CodeLanguage.Python,
        "A call has to give a function one value for each of its parameters. A call that gives fewer stops the program with " +
        "TypeError, which names the missing parameter.",
        [
            new WorkedExample("The area of a rectangle",
                Broken: """
                    def area(width, height):
                        return width * height

                    print(area(3))
                    """,
                BrokenDoes: Behaviour.Stopping("TypeError", 4),
                Fixed: """
                    def area(width, height):
                        return width * height

                    print(area(3, 4))
                    """,
                FixedDoes: Behaviour.Printing("12"),
                WhatChanged: "The call gives both the width and the height."),
        ]);

    private static Lesson ArgumentsInJava => new(Concepts.Arguments, CodeLanguage.Java,
        "javac checks every call against the method it calls before the program runs: a call with the wrong number of arguments " +
        "is refused, and no part of the program runs.",
        [
            new WorkedExample("The area of a rectangle",
                Broken: """
                    public class Area {
                        static int area(int width, int height) {
                            return width * height;
                        }

                        public static void main(String[] args) {
                            System.out.println(area(3));
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Refused("method area in class Area cannot be applied to given types", 7),
                Fixed: """
                    public class Area {
                        static int area(int width, int height) {
                            return width * height;
                        }

                        public static void main(String[] args) {
                            System.out.println(area(3, 4));
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("12"),
                WhatChanged: "The call gives both the width and the height."),
        ]);

    private static Lesson ArgumentsInJavaScript => new(Concepts.Arguments, CodeLanguage.JavaScript,
        "JavaScript lets a call give a function fewer values than it has parameters: the missing ones are undefined, and the " +
        "program carries on with them - so arithmetic with one gives NaN, \"not a number\".",
        [
            new WorkedExample("The area of a rectangle",
                Broken: """
                    function area(width, height) {
                      return width * height;
                    }
                    console.log(area(3));
                    """,
                BrokenDoes: Behaviour.Printing("NaN"),
                Fixed: """
                    function area(width, height) {
                      return width * height;
                    }
                    console.log(area(3, 4));
                    """,
                FixedDoes: Behaviour.Printing("12"),
                WhatChanged: "The call gives both the width and the height."),
        ]);

    private static Lesson ReturnsInPython => new(Concepts.ReturnValues, CodeLanguage.Python,
        "A function that prints its answer does not give it back: the line that called it gets None, the value a function gives " +
        "back when it reaches its end without a return.",
        [
            new WorkedExample("A total printed instead of returned",
                Broken: """
                    def add(a, b):
                        print(a + b)

                    total = add(2, 3)
                    print("Total:", total)
                    """,
                BrokenDoes: Behaviour.Printing("5\nTotal: None"),
                Fixed: """
                    def add(a, b):
                        return a + b

                    total = add(2, 3)
                    print("Total:", total)
                    """,
                FixedDoes: Behaviour.Printing("Total: 5"),
                WhatChanged: "return hands the sum back to the line that called add, which keeps it in total."),
        ]);

    private static Lesson ReturnsInJava => new(Concepts.ReturnValues, CodeLanguage.Java,
        "A method that says it gives back a value has to give one back on every way through it. javac refuses one that can " +
        "reach its end without a return, before any of the program runs.",
        [
            new WorkedExample("A grade with no answer for a fail",
                Broken: """
                    public class Grade {
                        static String grade(int mark) {
                            if (mark >= 40) {
                                return "pass";
                            }
                        }

                        public static void main(String[] args) {
                            System.out.println(grade(70));
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Refused("missing return statement", 6),
                Fixed: """
                    public class Grade {
                        static String grade(int mark) {
                            if (mark >= 40) {
                                return "pass";
                            }
                            return "fail";
                        }

                        public static void main(String[] args) {
                            System.out.println(grade(70));
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("pass"),
                WhatChanged: "The method now gives back \"fail\" when the mark is under 40, so every way through it returns something."),
        ]);

    private static Lesson ReturnsInJavaScript => new(Concepts.ReturnValues, CodeLanguage.JavaScript,
        "A function that reaches its end without a return gives back undefined, and the program carries on with it.",
        [
            new WorkedExample("A sum worked out and not returned",
                Broken: """
                    function add(a, b) {
                      const sum = a + b;
                    }
                    console.log(add(2, 3));
                    """,
                BrokenDoes: Behaviour.Printing("undefined"),
                Fixed: """
                    function add(a, b) {
                      const sum = a + b;
                      return sum;
                    }
                    console.log(add(2, 3));
                    """,
                FixedDoes: Behaviour.Printing("5"),
                WhatChanged: "return hands the sum back to the line that called add."),
        ]);

    private static Lesson RecursionInPython => new(Concepts.EndlessRecursion, CodeLanguage.Python,
        "Python allows only so many calls inside one another - about a thousand - so a function that never stops calling itself " +
        "stops the program with RecursionError.",
        [
            new WorkedExample("Counting down with no bottom",
                Broken: """
                    def count_down(n):
                        return count_down(n - 1)

                    print(count_down(3))
                    """,
                BrokenDoes: Behaviour.Stopping("RecursionError", 2),
                Fixed: """
                    def count_down(n):
                        if n == 0:
                            return "lift off"
                        return count_down(n - 1)

                    print(count_down(3))
                    """,
                FixedDoes: Behaviour.Printing("lift off"),
                WhatChanged: "When n reaches 0 the function returns without calling itself, so the calls stop."),
        ]);

    private static Lesson RecursionInJava => new(Concepts.EndlessRecursion, CodeLanguage.Java,
        "Each call waiting for the one inside it takes room on the stack, so a method that never stops calling itself runs out of " +
        "room and stops the program with a StackOverflowError.",
        [
            new WorkedExample("Counting down with no bottom",
                Broken: """
                    public class CountDown {
                        static int countDown(int n) {
                            return countDown(n - 1);
                        }

                        public static void main(String[] args) {
                            System.out.println(countDown(3));
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Stopping("StackOverflowError", 3),
                Fixed: """
                    public class CountDown {
                        static int countDown(int n) {
                            if (n == 0) {
                                return 0;
                            }
                            return countDown(n - 1);
                        }

                        public static void main(String[] args) {
                            System.out.println(countDown(3));
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("0"),
                WhatChanged: "When n reaches 0 the method returns without calling itself, so the calls stop."),
        ]);
}
