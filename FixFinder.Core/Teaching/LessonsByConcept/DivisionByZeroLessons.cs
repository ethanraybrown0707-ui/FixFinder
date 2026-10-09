namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Dividing by zero, in each language: what it does, shown by running a program that does it.</summary>
internal static class DivisionByZeroLessons
{
    private static Concept Concept => Concepts.DivisionByZero;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, CSharp, C, Cpp, JavaScript, Go, Scala, OCaml];

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "Python refuses to divide by zero whether the numbers are whole numbers or decimals: it stops with ZeroDivisionError " +
        "on the line that divides.",
        [
            new WorkedExample("An average of an empty list",
                Broken: """
                    scores = []
                    average = sum(scores) / len(scores)
                    print(average)
                    """,
                BrokenDoes: Behaviour.Stopping("ZeroDivisionError", 2),
                Fixed: """
                    scores = []
                    average = sum(scores) / len(scores) if scores else 0
                    print(average)
                    """,
                FixedDoes: Behaviour.Printing("0"),
                WhatChanged: "The fixed line divides only when the list has something in it - an empty list counts as false - and gives 0 otherwise.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        scores = []
                        average = float(sum(scores)) / len(scores)
                        print(average)
                        """,
                        Behaviour.Stopping("ZeroDivisionError", 2),
                        "Making the top a decimal does not help: Python will not divide a decimal by zero either."),
                    new WrongFix("""
                        scores = []
                        average = sum(scores) // len(scores)
                        print(average)
                        """,
                        Behaviour.Stopping("ZeroDivisionError", 2),
                        "// is still a division - it only drops the fraction - so it fails in the same way."),
                ],
            },
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "Dividing whole numbers - int or long - by zero stops a Java program with ArithmeticException. Dividing decimals - " +
        "double - does not stop it: dividing by zero gives Infinity, or NaN, \"not a number\", when 0 is divided by 0, and the " +
        "program carries on with a value that is not a real answer.",
        [
            new WorkedExample("An average of no scores",
                Broken: """
                    public class Average {
                        public static void main(String[] args) {
                            int[] scores = {};
                            int total = 0;
                            int average = total / scores.length;
                            System.out.println(average);
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Stopping("ArithmeticException", 5),
                Fixed: """
                    public class Average {
                        public static void main(String[] args) {
                            int[] scores = {};
                            int total = 0;
                            int average = scores.length == 0 ? 0 : total / scores.length;
                            System.out.println(average);
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("0"),
                WhatChanged: "The fixed line divides only when there are scores, and gives 0 when there are none.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        public class Average {
                            public static void main(String[] args) {
                                int[] scores = {};
                                int total = 0;
                                double average = (double) total / scores.length;
                                System.out.println(average);
                            }
                        }
                        """,
                        Behaviour.Printing("NaN"),
                        "Dividing as decimals stops the crash but not the mistake: 0 divided by 0 as a double is NaN, which is no average."),
                ],
            },
            new WorkedExample("Sharing a bill between nobody",
                Broken: """
                    public class Bill {
                        public static void main(String[] args) {
                            double bill = 30.0;
                            int people = 0;
                            System.out.println(bill / people);
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Printing("Infinity"),
                Fixed: """
                    public class Bill {
                        public static void main(String[] args) {
                            double bill = 30.0;
                            int people = 0;
                            if (people > 0) {
                                System.out.println(bill / people);
                            } else {
                                System.out.println("Nobody to share it");
                            }
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("Nobody to share it"),
                WhatChanged: "The bill is a double, so dividing it by zero gives Infinity rather than an error - the fixed program checks there is somebody to share it first."),
        ]);

    private static Lesson CSharp => new(Concept, CodeLanguage.CSharp,
        "Dividing whole numbers - int or long - by zero stops a C# program with DivideByZeroException. Dividing decimals - " +
        "double - does not stop it: 0 divided by 0 gives NaN, \"not a number\", and the program carries on with it.",
        [
            new WorkedExample("An average of no scores",
                Broken: """
                    int[] scores = new int[0];
                    int total = 0;
                    int average = total / scores.Length;
                    Console.WriteLine(average);
                    """,
                BrokenDoes: Behaviour.Stopping("DivideByZeroException", 3),
                Fixed: """
                    int[] scores = new int[0];
                    int total = 0;
                    int average = scores.Length == 0 ? 0 : total / scores.Length;
                    Console.WriteLine(average);
                    """,
                FixedDoes: Behaviour.Printing("0"),
                WhatChanged: "The fixed line divides only when there are scores, and gives 0 when there are none.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        int[] scores = new int[0];
                        int total = 0;
                        double average = (double)total / scores.Length;
                        Console.WriteLine(average);
                        """,
                        Behaviour.Printing("NaN"),
                        "Dividing as decimals stops the crash but not the mistake: 0 divided by 0 as a double is NaN, which is no average."),
                ],
            },
        ]);

    private static Lesson C => new(Concept, CodeLanguage.C,
        "In C, dividing a whole number by zero is undefined behaviour: the C standard does not say what happens, so the " +
        "compiler and the computer decide. On Windows the program usually crashes, as this example does when FixFinder runs " +
        "it - but nothing promises that, so the division has to be prevented rather than caught.",
        [
            new WorkedExample("An average of no scores",
                Broken: """
                    #include <stdio.h>

                    int main(void) {
                        int total = 0;
                        int count = 0;
                        int average = total / count;
                        printf("%d\n", average);
                        return 0;
                    }
                    """,
                BrokenDoes: Behaviour.Crashing("integer divide by zero"),
                Fixed: """
                    #include <stdio.h>

                    int main(void) {
                        int total = 0;
                        int count = 0;
                        int average = count == 0 ? 0 : total / count;
                        printf("%d\n", average);
                        return 0;
                    }
                    """,
                FixedDoes: Behaviour.Printing("0"),
                WhatChanged: "The fixed line divides only when the count is not zero, and gives 0 when it is."),
        ]);

    private static Lesson Cpp => new(Concept, CodeLanguage.Cpp,
        "In C++, dividing a whole number by zero is undefined behaviour: the standard does not say what happens, so the " +
        "compiler and the computer decide. On Windows the program usually crashes, as this example does when FixFinder runs " +
        "it - but nothing promises that, and no exception is thrown that a try could catch, so the division has to be prevented.",
        [
            new WorkedExample("An average of an empty vector",
                Broken: """
                    #include <iostream>
                    #include <vector>

                    int main() {
                        std::vector<int> scores;
                        int total = 0;
                        int average = total / static_cast<int>(scores.size());
                        std::cout << average << "\n";
                        return 0;
                    }
                    """,
                BrokenDoes: Behaviour.Crashing("integer divide by zero"),
                Fixed: """
                    #include <iostream>
                    #include <vector>

                    int main() {
                        std::vector<int> scores;
                        int total = 0;
                        int average = scores.empty() ? 0 : total / static_cast<int>(scores.size());
                        std::cout << average << "\n";
                        return 0;
                    }
                    """,
                FixedDoes: Behaviour.Printing("0"),
                WhatChanged: "The fixed line divides only when the vector has something in it, and gives 0 when it is empty."),
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "JavaScript never stops a program for dividing by zero. Dividing a number by zero gives Infinity, and 0 divided by 0 " +
        "gives NaN, \"not a number\" - and the program carries on with them, so the mistake shows up later, as a strange answer.",
        [
            new WorkedExample("An average of an empty list",
                Broken: """
                    const scores = [];
                    const total = scores.reduce((sum, score) => sum + score, 0);
                    const average = total / scores.length;
                    console.log(average);
                    """,
                BrokenDoes: Behaviour.Printing("NaN"),
                Fixed: """
                    const scores = [];
                    const total = scores.reduce((sum, score) => sum + score, 0);
                    const average = scores.length === 0 ? 0 : total / scores.length;
                    console.log(average);
                    """,
                FixedDoes: Behaviour.Printing("0"),
                WhatChanged: "The fixed line divides only when the list has something in it, and gives 0 when it is empty."),
            new WorkedExample("Sharing a bill between nobody",
                Broken: """
                    const bill = 30;
                    const people = 0;
                    console.log(bill / people);
                    """,
                BrokenDoes: Behaviour.Printing("Infinity"),
                Fixed: """
                    const bill = 30;
                    const people = 0;
                    console.log(people > 0 ? bill / people : "Nobody to share it");
                    """,
                FixedDoes: Behaviour.Printing("Nobody to share it"),
                WhatChanged: "The fixed program checks there is somebody to share the bill before dividing it."),
        ]);

    private static Lesson Go => new(Concept, CodeLanguage.Go,
        "Dividing a whole number by zero stops a Go program with a panic, runtime error: integer divide by zero - and when the " +
        "zero is written into the code, so Go can see it before the program runs, it refuses to build the program at all.",
        [
            new WorkedExample("An average of no scores",
                Broken: """
                    package main

                    import "fmt"

                    func main() {
                    	scores := []int{}
                    	total := 0
                    	average := total / len(scores)
                    	fmt.Println(average)
                    }
                    """,
                BrokenDoes: Behaviour.Stopping("runtime error: integer divide by zero", 8),
                Fixed: """
                    package main

                    import "fmt"

                    func main() {
                    	scores := []int{}
                    	total := 0
                    	average := 0
                    	if len(scores) > 0 {
                    		average = total / len(scores)
                    	}
                    	fmt.Println(average)
                    }
                    """,
                FixedDoes: Behaviour.Printing("0"),
                WhatChanged: "The fixed program starts the average at 0 and divides only when there are scores."),
            new WorkedExample("A zero written into the code",
                Broken: """
                    package main

                    import "fmt"

                    func main() {
                    	total := 30
                    	fmt.Println(total / 0)
                    }
                    """,
                BrokenDoes: Behaviour.Refused("invalid operation: division by zero", 7),
                Fixed: """
                    package main

                    import "fmt"

                    func main() {
                    	total := 30
                    	people := 3
                    	fmt.Println(total / people)
                    }
                    """,
                FixedDoes: Behaviour.Printing("10"),
                WhatChanged: "Go saw the zero before the program ran; the fixed program divides by the number of people instead."),
        ]);

    private static Lesson Scala => new(Concept, CodeLanguage.Scala,
        "Dividing whole numbers - Int or Long - by zero stops a Scala program with an ArithmeticException, as it would stop a " +
        "Java one: Scala runs on the same JVM. Dividing decimals - Double - does not stop it: dividing by zero gives Infinity, or " +
        "NaN, \"not a number\", when 0.0 is divided by 0, and the program carries on with a value that is not a real answer.",
        [
            new WorkedExample("An average of no scores",
                Broken: """
                    object Average {
                      def main(args: Array[String]): Unit = {
                        val scores = List[Int]()
                        val average = scores.sum / scores.length
                        println(average)
                      }
                    }
                    """,
                BrokenDoes: Behaviour.Stopping("ArithmeticException", 4),
                Fixed: """
                    object Average {
                      def main(args: Array[String]): Unit = {
                        val scores = List[Int]()
                        val average = if (scores.isEmpty) 0 else scores.sum / scores.length
                        println(average)
                      }
                    }
                    """,
                FixedDoes: Behaviour.Printing("0"),
                WhatChanged: "The fixed line divides only when the list has something in it, and gives 0 otherwise - in Scala an if gives a value back, so it can stand on the right of =.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        object Average {
                          def main(args: Array[String]): Unit = {
                            val scores = List[Int]()
                            val average = scores.sum / scores.size
                            println(average)
                          }
                        }
                        """,
                        Behaviour.Stopping("ArithmeticException", 4),
                        "size is another name for length: the list is still empty, so it is still 0."),
                    new WrongFix("""
                        object Average {
                          def main(args: Array[String]): Unit = {
                            val scores = List[Int]()
                            val average = scores.sum.toDouble / scores.length
                            println(average)
                          }
                        }
                        """,
                        Behaviour.Printing("NaN"),
                        "Dividing as decimals stops the crash, but 0.0 divided by 0 is NaN - not a number - which is no average either."),
                ],
            },
            new WorkedExample("Sharing out decimals",
                Broken: """
                    object Share {
                      def main(args: Array[String]): Unit = {
                        val total = 10.0
                        val people = 0
                        println(total / people)
                      }
                    }
                    """,
                BrokenDoes: Behaviour.Printing("Infinity"),
                Fixed: """
                    object Share {
                      def main(args: Array[String]): Unit = {
                        val total = 10.0
                        val people = 0
                        println(if (people == 0) 0.0 else total / people)
                      }
                    }
                    """,
                FixedDoes: Behaviour.Printing("0.0"),
                WhatChanged: "A Double divided by zero gives Infinity rather than stopping the program, so nothing says it went wrong - the fixed line checks for zero itself."),
        ]);

    private static Lesson OCaml => new(Concept, CodeLanguage.OCaml,
        "Dividing whole numbers with / by zero stops an OCaml program with the exception Division_by_zero - and so does mod, which " +
        "divides too. Floats have an operator of their own, /., and dividing a float by zero does not stop the program: it gives " +
        "infinity, or not-a-number when 0. is divided by 0., and the program carries on with a value that is not a real answer. " +
        "Compiled to bytecode, as FixFinder compiles it, OCaml does not say where the division was: for one inside a function it " +
        "names the line that called the function, and for one outside any function it names no line at all.",
        [
            new WorkedExample("An average of no scores",
                Broken: """
                    let scores = []

                    let average = List.fold_left ( + ) 0 scores / List.length scores

                    let () = print_int average
                    """,
                BrokenDoes: Behaviour.StoppingOnNoLine("Division_by_zero"),
                Fixed: """
                    let scores = []

                    let average = if scores = [] then 0 else List.fold_left ( + ) 0 scores / List.length scores

                    let () = print_int average
                    """,
                FixedDoes: Behaviour.Printing("0"),
                WhatChanged: "The fixed line divides only when the list has something in it, and gives 0 otherwise - in OCaml an if gives a value back, so it can stand on the right of =.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        let scores = []

                        let average = List.fold_left ( + ) 0 scores / (List.length scores)

                        let () = print_int average
                        """,
                        Behaviour.StoppingOnNoLine("Division_by_zero"),
                        "Brackets change nothing: the length of an empty list is still 0."),
                    new WrongFix("""
                        let scores = []

                        let average = List.fold_left ( + ) 0 scores mod List.length scores

                        let () = print_int average
                        """,
                        Behaviour.StoppingOnNoLine("Division_by_zero"),
                        "mod is a division too - it gives what is left over - so it fails in the same way."),
                ],
            },
            new WorkedExample("Sharing out floats",
                Broken: """
                    let total = 10.

                    let people = 0.

                    let () = print_float (total /. people)
                    """,
                BrokenDoes: Behaviour.Printing("inf"),
                Fixed: """
                    let total = 10.

                    let people = 0.

                    let () = print_float (if people = 0. then 0. else total /. people)
                    """,
                FixedDoes: Behaviour.Printing("0."),
                WhatChanged: "A float divided by zero gives infinity rather than stopping the program, so nothing says it went wrong - the fixed line checks for zero itself."),
        ]);
}
