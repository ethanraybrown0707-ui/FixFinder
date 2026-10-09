namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Whole-number division, in each language: what it does, shown by running a program that does it.</summary>
internal static class WholeNumberDivisionLessons
{
    private static Concept Concept => Concepts.WholeNumberDivision;

    public static IReadOnlyList<Lesson> All { get; } = [Java, C, Python, Scala, OCaml];

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "Dividing one int by another gives an int: the fraction is dropped, so 7 / 2 is 3. Storing the answer in a double " +
        "afterwards cannot bring the fraction back. Making one of the numbers a double first makes it a decimal division.",
        [
            new WorkedExample("An average of two marks",
                Broken: """
                    public class Average {
                        public static void main(String[] args) {
                            int total = 7;
                            int count = 2;
                            double average = total / count;
                            System.out.println(average);
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Printing("3.0"),
                Fixed: """
                    public class Average {
                        public static void main(String[] args) {
                            int total = 7;
                            int count = 2;
                            double average = (double) total / count;
                            System.out.println(average);
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("3.5"),
                WhatChanged: "(double) total makes the first number a decimal, so the division keeps its fraction.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        public class Average {
                            public static void main(String[] args) {
                                int total = 7;
                                int count = 2;
                                double average = (double) (total / count);
                                System.out.println(average);
                            }
                        }
                        """,
                        Behaviour.Printing("3.0"),
                        "The brackets divide first, as ints, and only then make the answer a double - after the fraction is gone."),
                ],
            },
        ]);

    private static Lesson C => new(Concept, CodeLanguage.C,
        "Dividing one int by another gives an int: the fraction is dropped, so 7 / 2 is 3, even when the answer is stored in a " +
        "double. Making one of the numbers a double first makes it a decimal division.",
        [
            new WorkedExample("An average of two marks",
                Broken: """
                    #include <stdio.h>

                    int main(void) {
                        int total = 7;
                        int count = 2;
                        double average = total / count;
                        printf("%.1f\n", average);
                        return 0;
                    }
                    """,
                BrokenDoes: Behaviour.Printing("3.0"),
                Fixed: """
                    #include <stdio.h>

                    int main(void) {
                        int total = 7;
                        int count = 2;
                        double average = (double) total / count;
                        printf("%.1f\n", average);
                        return 0;
                    }
                    """,
                FixedDoes: Behaviour.Printing("3.5"),
                WhatChanged: "(double) total makes the first number a decimal, so the division keeps its fraction."),
        ]);

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "In Python, / always gives a decimal - 7 / 2 is 3.5 - and // is the division that drops the fraction, giving 3. So an " +
        "average comes out whole only when // was used where / was meant.",
        [
            new WorkedExample("An average of two marks",
                Broken: """
                    total = 7
                    count = 2
                    print(total // count)
                    """,
                BrokenDoes: Behaviour.Printing("3"),
                Fixed: """
                    total = 7
                    count = 2
                    print(total / count)
                    """,
                FixedDoes: Behaviour.Printing("3.5"),
                WhatChanged: "/ keeps the fraction; // dropped it."),
        ]);

    private static Lesson Scala => new(Concept, CodeLanguage.Scala,
        "Dividing one Int by another gives an Int: the fraction is dropped, so 7 / 2 is 3, and storing it in a Double afterwards " +
        "gives 3.0. Making one of the numbers a Double first, with .toDouble, makes it a decimal division.",
        [
            new WorkedExample("An average of two marks",
                Broken: """
                    object Average {
                      def main(args: Array[String]): Unit = {
                        val total = 7
                        val count = 2
                        val average: Double = total / count
                        println(average)
                      }
                    }
                    """,
                BrokenDoes: Behaviour.Printing("3.0"),
                Fixed: """
                    object Average {
                      def main(args: Array[String]): Unit = {
                        val total = 7
                        val count = 2
                        val average: Double = total.toDouble / count
                        println(average)
                      }
                    }
                    """,
                FixedDoes: Behaviour.Printing("3.5"),
                WhatChanged: "total.toDouble makes the first number a decimal, so the division keeps its fraction."),
        ]);

    private static Lesson OCaml => new(Concept, CodeLanguage.OCaml,
        "/ divides ints and gives an int, dropping the fraction - 7 / 2 is 3 - and turning that into a float afterwards gives 3. " +
        "Floats have their own operator, /., which keeps the fraction once both numbers are floats.",
        [
            new WorkedExample("An average of two marks",
                Broken: """
                    let total = 7

                    let count = 2

                    let () = print_float (float_of_int (total / count))
                    """,
                BrokenDoes: Behaviour.Printing("3."),
                Fixed: """
                    let total = 7

                    let count = 2

                    let () = print_float (float_of_int total /. float_of_int count)
                    """,
                FixedDoes: Behaviour.Printing("3.5"),
                WhatChanged: "Both numbers become floats before dividing, and /. divides floats, so the fraction is kept."),
        ]);
}
