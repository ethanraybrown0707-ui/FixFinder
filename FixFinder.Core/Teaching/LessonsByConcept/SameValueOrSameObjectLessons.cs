namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Equal values, or the same object, in each language: what it does, shown by running a program that does it.</summary>
internal static class SameValueOrSameObjectLessons
{
    private static Concept Concept => Concepts.SameValueOrSameObject;

    public static IReadOnlyList<Lesson> All { get; } = [Java, Python, JavaScript, Scala];

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "For objects - Strings among them - == asks whether two names are the very same object, and equals asks whether they hold " +
        "the same value. Text read in or built while the program runs is a new String, so == can say false for two equal texts.",
        [
            new WorkedExample("A password typed in",
                Broken: """
                    public class Password {
                        public static void main(String[] args) {
                            String typed = new StringBuilder("secret").toString();
                            if (typed == "secret") {
                                System.out.println("welcome");
                            } else {
                                System.out.println("wrong password");
                            }
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Printing("wrong password"),
                Fixed: """
                    public class Password {
                        public static void main(String[] args) {
                            String typed = new StringBuilder("secret").toString();
                            if (typed.equals("secret")) {
                                System.out.println("welcome");
                            } else {
                                System.out.println("wrong password");
                            }
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("welcome"),
                WhatChanged: "equals compares the letters; == compared whether typed is the very same String as \"secret\", which a String built while the program runs is not."),
        ]);

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "== asks whether two values are equal, and is asks whether they are the very same object. Two lists made apart are equal " +
        "when they hold the same items, but they are never the same list.",
        [
            new WorkedExample("Two lists of the same marks",
                Broken: """
                    first = [70, 80]
                    second = [70, 80]
                    print(first is second)
                    """,
                BrokenDoes: Behaviour.Printing("False"),
                Fixed: """
                    first = [70, 80]
                    second = [70, 80]
                    print(first == second)
                    """,
                FixedDoes: Behaviour.Printing("True"),
                WhatChanged: "== compares the items the lists hold; is asked whether they are one and the same list, which two lists made apart never are."),
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "== turns its two sides into the same type before comparing - so the text \"0\" is == to the number 0 - while === compares " +
        "them as they are. For arrays and objects, both ask whether they are the very same one, never whether they hold the same items.",
        [
            new WorkedExample("A number typed in",
                Broken: """
                    const typed = "0";
                    console.log(typed == 0);
                    """,
                BrokenDoes: Behaviour.Printing("true"),
                Fixed: """
                    const typed = "0";
                    console.log(typed === 0);
                    """,
                FixedDoes: Behaviour.Printing("false"),
                WhatChanged: "=== does not change either side's type, so text is never equal to a number."),
            new WorkedExample("Two arrays of the same marks",
                Broken: """
                    const first = [70, 80];
                    const second = [70, 80];
                    console.log(first === second);
                    """,
                BrokenDoes: Behaviour.Printing("false"),
                Fixed: """
                    const first = [70, 80];
                    const second = [70, 80];
                    console.log(first.length === second.length && first.every((mark, i) => mark === second[i]));
                    """,
                FixedDoes: Behaviour.Printing("true"),
                WhatChanged: "=== asked whether they are the same array; the fixed line compares the items one by one."),
        ]);

    private static Lesson Scala => new(Concept, CodeLanguage.Scala,
        "== on two Lists compares the items they hold. == on two Arrays does not: it asks whether they are the very same array, so " +
        "two arrays made apart are never ==, whatever they hold. sameElements compares an array's items.",
        [
            new WorkedExample("Two arrays of the same marks",
                Broken: """
                    object Marks {
                      def main(args: Array[String]): Unit = {
                        val first = Array(70, 80)
                        val second = Array(70, 80)
                        println(first == second)
                      }
                    }
                    """,
                BrokenDoes: Behaviour.Printing("false"),
                Fixed: """
                    object Marks {
                      def main(args: Array[String]): Unit = {
                        val first = Array(70, 80)
                        val second = Array(70, 80)
                        println(first.sameElements(second))
                      }
                    }
                    """,
                FixedDoes: Behaviour.Printing("true"),
                WhatChanged: "sameElements compares the items; == asked whether they are one and the same array."),
        ]);
}
