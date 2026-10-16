namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Changing something that cannot change, in each language: what it does, shown by running a program that does it.</summary>
internal static class ChangingAConstantLessons
{
    private static Concept Concept => Concepts.ChangingAConstant;

    public static IReadOnlyList<Lesson> All { get; } = [Java, JavaScript, Scala, Rust, Ruby, Php, Lua, Perl, Dart, Fortran, Elixir, Erlang];

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "A variable declared final can be given a value once. javac refuses a line that gives it another, before any of the " +
        "program runs.",
        [
            new WorkedExample("Adding a bonus to a score",
                Broken: """
                    public class Score {
                        public static void main(String[] args) {
                            final int score = 10;
                            score = score + 5;
                            System.out.println(score);
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Refused("cannot assign a value to final variable", 4),
                Fixed: """
                    public class Score {
                        public static void main(String[] args) {
                            int score = 10;
                            score = score + 5;
                            System.out.println(score);
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("15"),
                WhatChanged: "Without final, score is a variable that may be given new values."),
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "A name made with const keeps the value it is first given. JavaScript only finds out when the line that changes it " +
        "runs, and stops the program there with TypeError; let makes a name whose value can change.",
        [
            new WorkedExample("Adding a bonus to a score",
                Broken: """
                    const score = 10;
                    score = score + 5;
                    console.log(score);
                    """,
                BrokenDoes: Behaviour.Stopping("TypeError", 2),
                Fixed: """
                    let score = 10;
                    score = score + 5;
                    console.log(score);
                    """,
                FixedDoes: Behaviour.Printing("15"),
                WhatChanged: "let makes score a name whose value can be changed."),
        ]);

    private static Lesson Scala => new(Concept, CodeLanguage.Scala,
        "A name made with val keeps the value it is first given, and the compiler refuses a line that gives it another, " +
        "before any of the program runs. var makes a name whose value can change.",
        [
            new WorkedExample("Adding a bonus to a score",
                Broken: """
                    object Score {
                      def main(args: Array[String]): Unit = {
                        val score = 10
                        score = score + 5
                        println(score)
                      }
                    }
                    """,
                BrokenDoes: Behaviour.Refused("Reassignment to val", 4),
                Fixed: """
                    object Score {
                      def main(args: Array[String]): Unit = {
                        var score = 10
                        score = score + 5
                        println(score)
                      }
                    }
                    """,
                FixedDoes: Behaviour.Printing("15"),
                WhatChanged: "var makes score a name whose value can be changed."),
        ]);

    private static Lesson Rust => new(Concept, CodeLanguage.Rust,
        "A variable made with let keeps the value it is first given - Rust's variables cannot change unless they are made with " +
        "let mut. rustc refuses a line that changes one, with E0384, before any of the program runs.",
        [
            new WorkedExample("Adding a bonus to a score",
                Broken: """
                    fn main() {
                        let score = 10;
                        score = score + 5;
                        println!("{}", score);
                    }
                    """,
                BrokenDoes: Behaviour.Refused("E0384", 3),
                Fixed: """
                    fn main() {
                        let mut score = 10;
                        score = score + 5;
                        println!("{}", score);
                    }
                    """,
                FixedDoes: Behaviour.Printing("15"),
                WhatChanged: "let mut makes score a variable whose value can be changed."),
        ]);

    private static Lesson Ruby => new(Concept, CodeLanguage.Ruby,
        "A value made with .freeze can never be changed: a line that changes it stops the program with FrozenError. A changed copy " +
        "can always be made.",
        [
            new WorkedExample("Adding to a frozen name",
                Broken: """
                    NAME = "ada".freeze
                    NAME << "!"
                    puts NAME
                    """,
                BrokenDoes: Behaviour.Stopping("FrozenError", 2),
                Fixed: """
                    NAME = "ada".freeze
                    greeting = NAME + "!"
                    puts greeting
                    """,
                FixedDoes: Behaviour.Printing("ada!"),
                WhatChanged: "+ makes a new, changed copy, and leaves the frozen value as it is."),
        ]);

    private static Lesson Php => new(Concept, CodeLanguage.Php,
        "A constant made with const cannot be changed. PHP will not even read a line that sets one with =: it refuses the whole file " +
        "before any of it runs. A value that has to change is a variable, written with a $.",
        [
            new WorkedExample("Raising the pass mark",
                Broken: """
                    <?php
                    const PASS_MARK = 40;
                    PASS_MARK = 50;
                    echo PASS_MARK;
                    """,
                BrokenDoes: Behaviour.Refused("ParseError", 3),
                Fixed: """
                    <?php
                    $passMark = 40;
                    $passMark = 50;
                    echo $passMark;
                    """,
                FixedDoes: Behaviour.Printing("50"),
                WhatChanged: "$passMark is a variable, so it can be given a new value."),
        ]);

    private static Lesson Lua => new(Concept, CodeLanguage.Lua,
        "A local marked <const> cannot be given another value. Lua will not even read a line that gives it one: it refuses the whole " +
        "file before any of it runs, with attempt to assign to const variable. A value that has to change is a local without <const>.",
        [
            new WorkedExample("Raising the pass mark",
                Broken: """
                    local PASS_MARK <const> = 40
                    PASS_MARK = 50
                    print(PASS_MARK)
                    """,
                BrokenDoes: Behaviour.Refused("attempt to assign to const variable", 2),
                Fixed: """
                    local passMark = 40
                    passMark = 50
                    print(passMark)
                    """,
                FixedDoes: Behaviour.Printing("50"),
                WhatChanged: "passMark is an ordinary local, so it can be given a new value."),
        ]);

    private static Lesson Perl => new(Concept, CodeLanguage.Perl,
        "A constant made with use constant cannot be changed. Perl will not even read a line that gives it a new value: it refuses the " +
        "whole file before any of it runs, with Can't modify constant item. A value that has to change is a variable, declared with my.",
        [
            new WorkedExample("Raising the pass mark",
                Broken: """
                    use strict;
                    use warnings;

                    use constant PASS_MARK => 40;
                    PASS_MARK = 50;
                    print PASS_MARK, "\n";
                    """,
                BrokenDoes: Behaviour.Refused("Can't modify constant item", 5),
                Fixed: """
                    use strict;
                    use warnings;

                    my $pass_mark = 40;
                    $pass_mark = 50;
                    print "$pass_mark\n";
                    """,
                FixedDoes: Behaviour.Printing("50"),
                WhatChanged: "$pass_mark is a variable, so it can be given a new value."),
        ]);

    private static Lesson Dart => new(Concept, CodeLanguage.Dart,
        "A variable declared final can be set only once, and Dart refuses a program that sets it again before any of it runs. A value " +
        "that has to change is declared with var.",
        [
            new WorkedExample("Raising the pass mark",
                Broken: """
                    void main() {
                      final passMark = 40;
                      passMark = 50;
                      print(passMark);
                    }
                    """,
                BrokenDoes: Behaviour.Refused("Can't assign to the final variable 'passMark'", 3),
                Fixed: """
                    void main() {
                      var passMark = 40;
                      passMark = 50;
                      print(passMark);
                    }
                    """,
                FixedDoes: Behaviour.Printing("50"),
                WhatChanged: "var lets passMark be given a new value."),
        ]);

    private static Lesson Fortran => new(Concept, CodeLanguage.Fortran,
        "A parameter is a constant, set where it is declared and never changed, and gfortran refuses a program that gives it another " +
        "value. An argument declared intent(in) is a promise to only read it, kept the same way.",
        [
            new WorkedExample("Raising the pass mark",
                Broken: """
                    program marks
                      implicit none
                      integer, parameter :: pass_mark = 40
                      pass_mark = 50
                      print '(I0)', pass_mark
                    end program marks
                    """,
                BrokenDoes: Behaviour.Refused("Named constant 'pass_mark' in variable definition context", 4),
                Fixed: """
                    program marks
                      implicit none
                      integer :: pass_mark = 40
                      pass_mark = 50
                      print '(I0)', pass_mark
                    end program marks
                    """,
                FixedDoes: Behaviour.Printing("50"),
                WhatChanged: "Without parameter, pass_mark is a variable, which can be given a new value."),
        ]);

    private static Lesson Elixir => new(Concept, CodeLanguage.Elixir,
        "Elixir's variables never change. = inside an fn makes a new variable, which lasts only until the fn ends - so a total added " +
        "to inside Enum.each is still 0 afterwards. Elixir only warns that the new total is unused.",
        [
            new WorkedExample("Adding up the marks",
                Broken: """
                    marks = [70, 80, 90]
                    total = 0
                    Enum.each(marks, fn mark -> total = total + mark end)
                    IO.puts(total)
                    """,
                BrokenDoes: Behaviour.Printing("0"),
                Fixed: """
                    marks = [70, 80, 90]
                    total = 0
                    total = Enum.reduce(marks, total, fn mark, total -> total + mark end)
                    IO.puts(total)
                    """,
                FixedDoes: Behaviour.Printing("240"),
                WhatChanged: "Enum.reduce passes the total on from one mark to the next and gives back the last one, which is given to total."),
        ]);

    private static Lesson Erlang => new(Concept, CodeLanguage.Erlang,
        "An Erlang variable is given its value once. = after that matches rather than changes it, so Total = Total + Mark asks whether 0 " +
        "is 70 - and stops the program with badmatch.",
        [
            new WorkedExample("Adding up the marks",
                Broken: """
                    -module(marks).
                    -export([main/0]).

                    main() ->
                        Total = 0,
                        lists:foreach(fun(Mark) -> Total = Total + Mark end, [70, 80]),
                        io:format("~p~n", [Total]).
                    """,
                BrokenDoes: Behaviour.Stopping("badmatch", 6),
                Fixed: """
                    -module(marks).
                    -export([main/0]).

                    main() ->
                        Total = lists:foldl(fun(Mark, Sum) -> Sum + Mark end, 0, [70, 80]),
                        io:format("~p~n", [Total]).
                    """,
                FixedDoes: Behaviour.Printing("150"),
                WhatChanged: "lists:foldl passes the sum on from one mark to the next, and gives back the last one, which is given to Total once."),
        ]);
}
