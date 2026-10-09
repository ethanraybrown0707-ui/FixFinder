namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>A case nobody handled, in each language: what it does, shown by running a program that does it.</summary>
internal static class MissedCaseLessons
{
    private static Concept Concept => Concepts.MissedCase;

    public static IReadOnlyList<Lesson> All { get; } = [Scala, Java, Python, OCaml];

    private static Lesson Scala => new(Concept, CodeLanguage.Scala,
        "A match tries its cases in order, and when none fits the value, the program stops with a MatchError. A last case _ " +
        "fits every value the others leave.",
        [
            new WorkedExample("Describing a mark",
                Broken: """
                    object Grade {
                      def describe(mark: Int): String = mark match {
                        case 1 => "first"
                        case 2 => "second"
                      }

                      def main(args: Array[String]): Unit = println(describe(3))
                    }
                    """,
                BrokenDoes: Behaviour.Stopping("MatchError", 4),
                Fixed: """
                    object Grade {
                      def describe(mark: Int): String = mark match {
                        case 1 => "first"
                        case 2 => "second"
                        case _ => "another"
                      }

                      def main(args: Array[String]): Unit = println(describe(3))
                    }
                    """,
                FixedDoes: Behaviour.Printing("another"),
                WhatChanged: "case _ fits any value the cases above it do not, so every mark gets an answer."),
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "A switch that gives back a value - switch (...) { case 1 -> ... } - has to have an answer for every value. javac refuses " +
        "one that does not, before any of the program runs; default is the answer for every value the cases leave.",
        [
            new WorkedExample("Describing a mark",
                Broken: """
                    public class Grade {
                        static String describe(int mark) {
                            return switch (mark) {
                                case 1 -> "first";
                                case 2 -> "second";
                            };
                        }

                        public static void main(String[] args) {
                            System.out.println(describe(3));
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Refused("the switch expression does not cover all possible input values", 3),
                Fixed: """
                    public class Grade {
                        static String describe(int mark) {
                            return switch (mark) {
                                case 1 -> "first";
                                case 2 -> "second";
                                default -> "another";
                            };
                        }

                        public static void main(String[] args) {
                            System.out.println(describe(3));
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("another"),
                WhatChanged: "default answers for every value the cases leave, so the switch always has a value to give back."),
        ]);

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "A match whose cases do not fit the value does nothing at all: the program carries on past it, so nothing says a value " +
        "was missed. A last case _ fits every value the others leave.",
        [
            new WorkedExample("Describing a mark",
                Broken: """
                    mark = 3
                    match mark:
                        case 1:
                            print("first")
                        case 2:
                            print("second")
                    """,
                BrokenDoes: Behaviour.Printing(""),
                Fixed: """
                    mark = 3
                    match mark:
                        case 1:
                            print("first")
                        case 2:
                            print("second")
                        case _:
                            print("another")
                    """,
                FixedDoes: Behaviour.Printing("another"),
                WhatChanged: "case _ fits any value the cases above it do not, so a 3 gets an answer too."),
        ]);

    private static Lesson OCaml => new(Concept, CodeLanguage.OCaml,
        "OCaml warns, before the program runs, that a match leaves a value out - and when such a value comes, the program stops " +
        "with Match_failure. A last case | _ -> fits every value the others leave.",
        [
            new WorkedExample("Describing a mark",
                Broken: """
                    let describe mark =
                      match mark with
                      | 1 -> "first"
                      | 2 -> "second"

                    let () = print_string (describe 3)
                    """,
                BrokenDoes: Behaviour.Stopping("Match_failure", 2),
                Fixed: """
                    let describe mark =
                      match mark with
                      | 1 -> "first"
                      | 2 -> "second"
                      | _ -> "another"

                    let () = print_string (describe 3)
                    """,
                FixedDoes: Behaviour.Printing("another"),
                WhatChanged: "| _ -> fits any value the cases above it do not, so every mark gets an answer."),
        ]);
}
