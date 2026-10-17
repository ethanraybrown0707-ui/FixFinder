namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Whole-number division, in each language: what it does, shown by running a program that does it.</summary>
internal static class WholeNumberDivisionLessons
{
    private static Concept Concept => Concepts.WholeNumberDivision;

    public static IReadOnlyList<Lesson> All { get; } = [Java, C, Python, Scala, OCaml, Rust, Ruby, Dart, Fortran, FSharp];

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

    private static Lesson Rust => new(Concept, CodeLanguage.Rust,
        "Dividing one whole number by another in Rust gives a whole number: 7 / 2 is 3. Making each number an f64 first, with as, " +
        "makes the division keep its fraction.",
        [
            new WorkedExample("An average of two marks",
                Broken: """
                    fn main() {
                        let total = 7;
                        let count = 2;
                        let average = total / count;
                        println!("{}", average);
                    }
                    """,
                BrokenDoes: Behaviour.Printing("3"),
                Fixed: """
                    fn main() {
                        let total = 7;
                        let count = 2;
                        let average = total as f64 / count as f64;
                        println!("{}", average);
                    }
                    """,
                FixedDoes: Behaviour.Printing("3.5"),
                WhatChanged: "as f64 makes each number a decimal before dividing, so the half is kept."),
        ]);

    private static Lesson Ruby => new(Concept, CodeLanguage.Ruby,
        "Dividing one whole number by another in Ruby gives a whole number: 7 / 2 is 3. A decimal on either side - 7.0, or .to_f - " +
        "makes it keep the fraction.",
        [
            new WorkedExample("Half of seven",
                Broken: """
                    puts 7 / 2
                    """,
                BrokenDoes: Behaviour.Printing("3"),
                Fixed: """
                    puts 7.0 / 2
                    """,
                FixedDoes: Behaviour.Printing("3.5"),
                WhatChanged: "7.0 is a decimal, so the division keeps the half."),
        ]);

    private static Lesson Dart => new(Concept, CodeLanguage.Dart,
        "In Dart, / always gives a decimal - 7 / 2 is 3.5 - and Dart refuses to store a decimal in an int. ~/ is its whole-number " +
        "division: 7 ~/ 2 is 3, the fraction dropped.",
        [
            new WorkedExample("An average kept as a whole number",
                Broken: """
                    void main() {
                      int total = 7;
                      int count = 2;
                      int average = total / count;
                      print(average);
                    }
                    """,
                BrokenDoes: Behaviour.Refused("A value of type 'double' can't be assigned to a variable of type 'int'", 4),
                Fixed: """
                    void main() {
                      int total = 7;
                      int count = 2;
                      int average = total ~/ count;
                      print(average);
                    }
                    """,
                FixedDoes: Behaviour.Printing("3"),
                WhatChanged: "~/ divides and keeps the whole number; a double variable would keep the 3.5."),
        ]);

    private static Lesson Fortran => new(Concept, CodeLanguage.Fortran,
        "When both sides of / are whole numbers, Fortran works the answer out as a whole number, dropping the fraction - and only " +
        "then keeps it in the real on the left. 7 / 2 gives 3, kept as 3.0. real() turns one side into a real first.",
        [
            new WorkedExample("An average",
                Broken: """
                    program marks
                      implicit none
                      integer :: total, count
                      real :: average
                      total = 7
                      count = 2
                      average = total / count
                      print '(F0.1)', average
                    end program marks
                    """,
                BrokenDoes: Behaviour.Printing("3.0"),
                Fixed: """
                    program marks
                      implicit none
                      integer :: total, count
                      real :: average
                      total = 7
                      count = 2
                      average = real(total) / count
                      print '(F0.1)', average
                    end program marks
                    """,
                FixedDoes: Behaviour.Printing("3.5"),
                WhatChanged: "real(total) makes the division one of reals, so the .5 is kept."),
        ]);

    private static Lesson FSharp => new(Concept, CodeLanguage.FSharp,
        "/ divides two whole numbers as whole numbers, dropping what is left over: 7 / 2 is 3. float (total / count) makes " +
        "the answer a decimal only after the half is gone.",
        [
            new WorkedExample("An average of two whole numbers",
                Broken: """
                    let total = 7
                    let count = 2
                    let average = float (total / count)
                    printfn "%.1f" average
                    """,
                BrokenDoes: Behaviour.Printing("3.0"),
                Fixed: """
                    let total = 7
                    let count = 2
                    let average = float total / float count
                    printfn "%.1f" average
                    """,
                FixedDoes: Behaviour.Printing("3.5"),
                WhatChanged: "Each number is made a decimal before dividing, so nothing is dropped."),
        ]);
}
