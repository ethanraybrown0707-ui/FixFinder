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
        ArgumentsInRust, ReturnsInRust,
        ArgumentsInRuby, ReturnsInRuby, RecursionInRuby,
        ArgumentsInPhp, ReturnsInPhp,
        ArgumentsInLua, RecursionInLua,
        ArgumentsInPerl,
        ArgumentsInDart, ReturnsInDart, RecursionInDart,
        ArgumentsInFortran,
        ArgumentsInPowerShell,
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

    private static Lesson ArgumentsInRust => new(Concepts.Arguments, CodeLanguage.Rust,
        "rustc checks every call against the function it calls before the program runs: a call with the wrong number of " +
        "arguments is refused with E0061, which says which one is missing.",
        [
            new WorkedExample("The area of a rectangle",
                Broken: """
                    fn area(width: i32, height: i32) -> i32 {
                        width * height
                    }

                    fn main() {
                        println!("{}", area(3));
                    }
                    """,
                BrokenDoes: Behaviour.Refused("E0061", 6),
                Fixed: """
                    fn area(width: i32, height: i32) -> i32 {
                        width * height
                    }

                    fn main() {
                        println!("{}", area(3, 4));
                    }
                    """,
                FixedDoes: Behaviour.Printing("12"),
                WhatChanged: "The call gives both the width and the height."),
        ]);

    private static Lesson ReturnsInRust => new(Concepts.ReturnValues, CodeLanguage.Rust,
        "A Rust function gives back the value of its last line - when that line has no semicolon. A semicolon makes it a statement, " +
        "which gives back nothing, so a function that promises a number is refused with E0308.",
        [
            new WorkedExample("A sum with a semicolon",
                Broken: """
                    fn add(a: i32, b: i32) -> i32 {
                        a + b;
                    }

                    fn main() {
                        println!("{}", add(2, 3));
                    }
                    """,
                BrokenDoes: Behaviour.Refused("E0308", 1),
                Fixed: """
                    fn add(a: i32, b: i32) -> i32 {
                        a + b
                    }

                    fn main() {
                        println!("{}", add(2, 3));
                    }
                    """,
                FixedDoes: Behaviour.Printing("5"),
                WhatChanged: "Without the semicolon, a + b is the function's last value, so it is what add gives back."),
        ]);

    private static Lesson ArgumentsInRuby => new(Concepts.Arguments, CodeLanguage.Ruby,
        "A method has to be given one value for each parameter its def line lists. Ruby stops the program with ArgumentError when it is " +
        "given too few or too many - saying how many it was given and how many it expects. The line Ruby names first is the method's " +
        "def; the call that gave the wrong number is the line after from.",
        [
            new WorkedExample("The area of a rectangle",
                Broken: """
                    def area(width, height)
                      width * height
                    end

                    puts area(3)
                    """,
                BrokenDoes: Behaviour.Stopping("ArgumentError", 1),
                Fixed: """
                    def area(width, height)
                      width * height
                    end

                    puts area(3, 4)
                    """,
                FixedDoes: Behaviour.Printing("12"),
                WhatChanged: "The call gives both the width and the height."),
        ]);

    private static Lesson ReturnsInRuby => new(Concepts.ReturnValues, CodeLanguage.Ruby,
        "upcase, sort, strip and their like give back a new, changed copy, and leave the value they are called on as it was. Called " +
        "on a line of its own, such a method changes nothing - the copy is thrown away.",
        [
            new WorkedExample("A name made capitals",
                Broken: """
                    name = "ada"
                    name.upcase
                    puts name
                    """,
                BrokenDoes: Behaviour.Printing("ada"),
                Fixed: """
                    name = "ada"
                    name = name.upcase
                    puts name
                    """,
                FixedDoes: Behaviour.Printing("ADA"),
                WhatChanged: "The copy upcase gives back is kept in name."),
        ]);

    private static Lesson RecursionInRuby => new(Concepts.EndlessRecursion, CodeLanguage.Ruby,
        "Each call still in progress takes room on the stack, so a method that never stops calling itself runs out of room and stops " +
        "the program with SystemStackError, \"stack level too deep\".",
        [
            new WorkedExample("Counting down with no bottom",
                Broken: """
                    def count_down(n)
                      count_down(n - 1)
                    end

                    puts count_down(3)
                    """,
                BrokenDoes: Behaviour.Stopping("SystemStackError", 2),
                Fixed: """
                    def count_down(n)
                      return 0 if n.zero?

                      count_down(n - 1)
                    end

                    puts count_down(3)
                    """,
                FixedDoes: Behaviour.Printing("0"),
                WhatChanged: "When n reaches 0 the method returns without calling itself, so the calls stop."),
        ]);

    private static Lesson ArgumentsInPhp => new(Concepts.Arguments, CodeLanguage.Php,
        "A function has to be given a value for each parameter its function line lists, unless the parameter has a default. Given " +
        "too few, PHP stops the program with ArgumentCountError, saying how many were passed and how many it expects: the line it " +
        "names at the end is the function's own, and the call's line is in the message, after passed in. Given too many, PHP says " +
        "nothing and passes over the extra ones.",
        [
            new WorkedExample("The area of a rectangle",
                Broken: """
                    <?php
                    function area($width, $height) {
                        return $width * $height;
                    }
                    echo area(3);
                    """,
                BrokenDoes: Behaviour.Stopping("ArgumentCountError", 2),
                Fixed: """
                    <?php
                    function area($width, $height) {
                        return $width * $height;
                    }
                    echo area(3, 4);
                    """,
                FixedDoes: Behaviour.Printing("12"),
                WhatChanged: "The call gives both the width and the height."),
        ]);

    private static Lesson ReturnsInPhp => new(Concepts.ReturnValues, CodeLanguage.Php,
        "A function gives back only what return gives it. Without return it gives back null, and PHP carries on with it - null + 1 " +
        "is 1 - so the program prints a wrong answer rather than stopping.",
        [
            new WorkedExample("Doubling a number",
                Broken: """
                    <?php
                    function double($number) {
                        $number * 2;
                    }
                    echo double(4) + 1;
                    """,
                BrokenDoes: Behaviour.Printing("1"),
                Fixed: """
                    <?php
                    function double($number) {
                        return $number * 2;
                    }
                    echo double(4) + 1;
                    """,
                FixedDoes: Behaviour.Printing("9"),
                WhatChanged: "return gives the doubled number back to the line that called double."),
        ]);

    private static Lesson ArgumentsInLua => new(Concepts.Arguments, CodeLanguage.Lua,
        "Lua does not check how many values a call gives a function: a parameter given none is nil, and values given beyond the " +
        "parameters are passed over. The program stops only when the nil is used - inside the function, at the line that uses it.",
        [
            new WorkedExample("The area of a rectangle",
                Broken: """
                    local function area(width, height)
                      return width * height
                    end
                    print(area(3))
                    """,
                BrokenDoes: Behaviour.Stopping("attempt to perform arithmetic on a nil value", 2),
                Fixed: """
                    local function area(width, height)
                      return width * height
                    end
                    print(area(3, 4))
                    """,
                FixedDoes: Behaviour.Printing("12"),
                WhatChanged: "The call gives both the width and the height."),
        ]);

    private static Lesson RecursionInLua => new(Concepts.EndlessRecursion, CodeLanguage.Lua,
        "Each call still in progress takes room, so a function that never stops calling itself runs out of room and stops the program " +
        "with stack overflow.",
        [
            new WorkedExample("Counting down",
                Broken: """
                    local function countDown(number)
                      return 1 + countDown(number - 1)
                    end
                    print(countDown(3))
                    """,
                BrokenDoes: Behaviour.Stopping("stack overflow", 2),
                Fixed: """
                    local function countDown(number)
                      if number == 0 then
                        return 0
                      end
                      return 1 + countDown(number - 1)
                    end
                    print(countDown(3))
                    """,
                FixedDoes: Behaviour.Printing("3"),
                WhatChanged: "When the number reaches 0 the function returns without calling itself, so the calls stop."),
        ]);

    private static Lesson ArgumentsInPerl => new(Concepts.Arguments, CodeLanguage.Perl,
        "Perl does not check how many values a call gives a sub: the sub gets whatever is in @_, and a parameter given none is undef - " +
        "which Perl uses as 0 in arithmetic, warning of it only with use warnings.",
        [
            new WorkedExample("The area of a rectangle",
                Broken: """
                    use strict;
                    use warnings;

                    sub area {
                        my ($width, $height) = @_;
                        return $width * $height;
                    }
                    print area(3), "\n";
                    """,
                BrokenDoes: Behaviour.Printing("0"),
                Fixed: """
                    use strict;
                    use warnings;

                    sub area {
                        my ($width, $height) = @_;
                        return $width * $height;
                    }
                    print area(3, 4), "\n";
                    """,
                FixedDoes: Behaviour.Printing("12"),
                WhatChanged: "The call gives both the width and the height."),
        ]);

    private static Lesson ArgumentsInDart => new(Concepts.Arguments, CodeLanguage.Dart,
        "A call has to give a function a value for each of its parameters, and Dart refuses a call that gives too few, or too many, " +
        "before the program runs - saying how many it needs and how many it was given.",
        [
            new WorkedExample("The area of a rectangle",
                Broken: """
                    int area(int width, int height) => width * height;

                    void main() {
                      print(area(3));
                    }
                    """,
                BrokenDoes: Behaviour.Refused("Too few positional arguments", 4),
                Fixed: """
                    int area(int width, int height) => width * height;

                    void main() {
                      print(area(3, 4));
                    }
                    """,
                FixedDoes: Behaviour.Printing("12"),
                WhatChanged: "The call gives both the width and the height."),
        ]);

    private static Lesson ReturnsInDart => new(Concepts.ReturnValues, CodeLanguage.Dart,
        "A function that says it gives back an int has to return one on every way through it. Without return, it would give back " +
        "null, which an int cannot be - so Dart refuses the program before it runs.",
        [
            new WorkedExample("Doubling a number",
                Broken: """
                    int doubled(int n) {
                      n * 2;
                    }

                    void main() {
                      print(doubled(4) + 1);
                    }
                    """,
                BrokenDoes: Behaviour.Refused("A non-null value must be returned", 1),
                Fixed: """
                    int doubled(int n) {
                      return n * 2;
                    }

                    void main() {
                      print(doubled(4) + 1);
                    }
                    """,
                FixedDoes: Behaviour.Printing("9"),
                WhatChanged: "return gives the doubled number back to the line that called doubled."),
        ]);

    private static Lesson RecursionInDart => new(Concepts.EndlessRecursion, CodeLanguage.Dart,
        "Each call still in progress takes room, so a function that never stops calling itself runs out of room and stops the program " +
        "with Stack Overflow.",
        [
            new WorkedExample("Counting down",
                Broken: """
                    int countDown(int n) => 1 + countDown(n - 1);

                    void main() {
                      print(countDown(3));
                    }
                    """,
                BrokenDoes: Behaviour.Stopping("Stack Overflow", 1),
                Fixed: """
                    int countDown(int n) => n == 0 ? 0 : 1 + countDown(n - 1);

                    void main() {
                      print(countDown(3));
                    }
                    """,
                FixedDoes: Behaviour.Printing("3"),
                WhatChanged: "When n reaches 0 the function gives 0 without calling itself, so the calls stop."),
        ]);

    private static Lesson ArgumentsInFortran => new(Concepts.Arguments, CodeLanguage.Fortran,
        "A call has to give a function a value for each of its arguments, and gfortran refuses a call that gives too few before the " +
        "program runs, naming the argument with no value.",
        [
            new WorkedExample("The area of a rectangle",
                Broken: """
                    program marks
                      implicit none
                      print '(I0)', area(3)
                    contains
                      integer function area(width, height)
                        integer, intent(in) :: width, height
                        area = width * height
                      end function area
                    end program marks
                    """,
                BrokenDoes: Behaviour.Refused("Missing actual argument for argument 'height'", 3),
                Fixed: """
                    program marks
                      implicit none
                      print '(I0)', area(3, 4)
                    contains
                      integer function area(width, height)
                        integer, intent(in) :: width, height
                        area = width * height
                      end function area
                    end program marks
                    """,
                FixedDoes: Behaviour.Printing("12"),
                WhatChanged: "The call gives both the width and the height."),
        ]);

    private static Lesson ArgumentsInPowerShell => new(Concepts.Arguments, CodeLanguage.PowerShell,
        "PowerShell calls a function by its name, then its values, apart: Area 3 4. Written Area(3, 4), as other languages write it, " +
        "the brackets and comma make one value - the list 3, 4 - which goes to the first parameter, and the second gets nothing.",
        [
            new WorkedExample("The area of a rectangle",
                Broken: """
                    function Area($width, $height) {
                        return $width * $height
                    }
                    Write-Output (Area(3, 4))
                    """,
                BrokenDoes: Behaviour.Printing(""),
                Fixed: """
                    function Area($width, $height) {
                        return $width * $height
                    }
                    Write-Output (Area 3 4)
                    """,
                FixedDoes: Behaviour.Printing("12"),
                WhatChanged: "Area 3 4 gives the width and the height a value each."),
        ]);
}
