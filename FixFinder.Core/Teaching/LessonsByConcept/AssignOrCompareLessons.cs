namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Assigning or comparing, in each language: what it does, shown by running a program that does it.</summary>
internal static class AssignOrCompareLessons
{
    private static Concept Concept => Concepts.AssignOrCompare;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, JavaScript, C, Ruby, Php, Lua, Perl, Dart, Fortran, PowerShell, Elixir, Erlang];

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "Python does not allow = in an if's condition at all: it refuses the program with SyntaxError before any of it runs. == " +
        "compares.",
        [
            new WorkedExample("Checking a mark",
                Broken: """
                    mark = 70
                    if mark = 70:
                        print("seventy")
                    """,
                BrokenDoes: Behaviour.Refused("SyntaxError", 2),
                Fixed: """
                    mark = 70
                    if mark == 70:
                        print("seventy")
                    """,
                FixedDoes: Behaviour.Printing("seventy"),
                WhatChanged: "== asks whether mark is 70; = would have stored 70 in it."),
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "An if in Java needs a true-or-false value. mark = 70 stores 70 and is the number 70, so javac refuses the program with " +
        "\"incompatible types\" - an int where a boolean is needed - and no part of it runs. == compares.",
        [
            new WorkedExample("Checking a mark",
                Broken: """
                    public class Mark {
                        public static void main(String[] args) {
                            int mark = 70;
                            if (mark = 70) {
                                System.out.println("seventy");
                            }
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Refused("incompatible types", 4),
                Fixed: """
                    public class Mark {
                        public static void main(String[] args) {
                            int mark = 70;
                            if (mark == 70) {
                                System.out.println("seventy");
                            }
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("seventy"),
                WhatChanged: "== asks whether mark is 70, which is true or false, as an if needs."),
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "JavaScript lets = stand in an if's condition: it stores the value, and the if then asks whether that value counts as " +
        "true. Any number but 0 does, so the if always runs - whatever the variable held before - and nothing stops the program.",
        [
            new WorkedExample("Checking a mark",
                Broken: """
                    let mark = 40;
                    if (mark = 70) {
                      console.log("seventy");
                    }
                    console.log(mark);
                    """,
                BrokenDoes: Behaviour.Printing("seventy\n70"),
                Fixed: """
                    let mark = 40;
                    if (mark === 70) {
                      console.log("seventy");
                    }
                    console.log(mark);
                    """,
                FixedDoes: Behaviour.Printing("40"),
                WhatChanged: "=== compares without changing mark, so the if is false for 40, and mark keeps its value."),
        ]);

    private static Lesson C => new(Concept, CodeLanguage.C,
        "C lets = stand in an if's condition: it stores the value, and any value but 0 counts as true - so the if always runs, and " +
        "the variable's own value is lost.",
        [
            new WorkedExample("Checking a mark",
                Broken: """
                    #include <stdio.h>

                    int main(void) {
                        int mark = 40;
                        if (mark = 70) {
                            printf("seventy\n");
                        }
                        printf("%d\n", mark);
                        return 0;
                    }
                    """,
                BrokenDoes: Behaviour.Printing("seventy\n70"),
                Fixed: """
                    #include <stdio.h>

                    int main(void) {
                        int mark = 40;
                        if (mark == 70) {
                            printf("seventy\n");
                        }
                        printf("%d\n", mark);
                        return 0;
                    }
                    """,
                FixedDoes: Behaviour.Printing("40"),
                WhatChanged: "== compares without changing mark, so the if is false for 40, and mark keeps its value."),
        ]);

    private static Lesson Ruby => new(Concept, CodeLanguage.Ruby,
        "In an if, = stores a value and == compares. Ruby warns about = in a condition, but runs the program: the condition is " +
        "then always true, and the variable is changed.",
        [
            new WorkedExample("Checking a mark",
                Broken: """
                    mark = 70
                    if mark = 40
                      puts "forty"
                    end
                    puts mark
                    """,
                BrokenDoes: Behaviour.Printing("forty\n40"),
                Fixed: """
                    mark = 70
                    if mark == 40
                      puts "forty"
                    end
                    puts mark
                    """,
                FixedDoes: Behaviour.Printing("70"),
                WhatChanged: "== compares the mark with 40 without changing it."),
        ]);

    private static Lesson Php => new(Concept, CodeLanguage.Php,
        "In an if, = stores a value and == compares. PHP gives no warning for = in a condition: it stores the value, and the if's " +
        "code runs whenever that value counts as true - as 40 does. So the variable is changed, and the if is taken.",
        [
            new WorkedExample("Checking a mark",
                Broken: """
                    <?php
                    $mark = 70;
                    if ($mark = 40) {
                        echo "forty\n";
                    }
                    echo $mark;
                    """,
                BrokenDoes: Behaviour.Printing("forty\n40"),
                Fixed: """
                    <?php
                    $mark = 70;
                    if ($mark == 40) {
                        echo "forty\n";
                    }
                    echo $mark;
                    """,
                FixedDoes: Behaviour.Printing("70"),
                WhatChanged: "== compares the mark with 40 without changing it."),
        ]);

    private static Lesson Lua => new(Concept, CodeLanguage.Lua,
        "In Lua, = stores a value and == compares. Lua will not store a value in a condition at all: = in an if refuses the whole " +
        "file before any of it runs - 'then' expected near '=' - so this mistake is caught before it can do harm.",
        [
            new WorkedExample("Checking a mark",
                Broken: """
                    local mark = 70
                    if mark = 40 then
                      print("forty")
                    end
                    print(mark)
                    """,
                BrokenDoes: Behaviour.Refused("'then' expected near '='", 2),
                Fixed: """
                    local mark = 70
                    if mark == 40 then
                      print("forty")
                    end
                    print(mark)
                    """,
                FixedDoes: Behaviour.Printing("70"),
                WhatChanged: "== compares the mark with 40 without changing it."),
        ]);

    private static Lesson Perl => new(Concept, CodeLanguage.Perl,
        "In Perl, = stores a value and == compares. = in an if stores the value, and the if is taken whenever that value counts as " +
        "true - as 40 does. With use warnings Perl warns, Found = in conditional, but runs the program all the same.",
        [
            new WorkedExample("Checking a mark",
                Broken: """
                    use strict;
                    use warnings;

                    my $mark = 70;
                    if ($mark = 40) {
                        print "forty\n";
                    }
                    print "$mark\n";
                    """,
                BrokenDoes: Behaviour.Printing("forty\n40"),
                Fixed: """
                    use strict;
                    use warnings;

                    my $mark = 70;
                    if ($mark == 40) {
                        print "forty\n";
                    }
                    print "$mark\n";
                    """,
                FixedDoes: Behaviour.Printing("70"),
                WhatChanged: "== compares the mark with 40 without changing it."),
        ]);

    private static Lesson Dart => new(Concept, CodeLanguage.Dart,
        "In Dart, = stores a value and == compares. An if needs true or false, and = gives the value it stored - a number here - so " +
        "Dart refuses the program before any of it runs.",
        [
            new WorkedExample("Checking a mark",
                Broken: """
                    void main() {
                      var mark = 70;
                      if (mark = 40) {
                        print("forty");
                      }
                      print(mark);
                    }
                    """,
                BrokenDoes: Behaviour.Refused("A value of type 'int' can't be assigned to a variable of type 'bool'", 3),
                Fixed: """
                    void main() {
                      var mark = 70;
                      if (mark == 40) {
                        print("forty");
                      }
                      print(mark);
                    }
                    """,
                FixedDoes: Behaviour.Printing("70"),
                WhatChanged: "== compares the mark with 40, giving true or false, without changing it."),
        ]);

    private static Lesson Fortran => new(Concept, CodeLanguage.Fortran,
        "In Fortran, = gives a variable a value and == compares; \"is not equal to\" is /=. gfortran refuses = in an if's condition " +
        "before any of the program runs. != is refused too, for another reason: ! starts a comment, so gfortran reads only as far as it.",
        [
            new WorkedExample("Checking a mark",
                Broken: """
                    program marks
                      implicit none
                      integer :: mark
                      mark = 70
                      if (mark = 40) then
                        print '(A)', 'forty'
                      end if
                      print '(I0)', mark
                    end program marks
                    """,
                BrokenDoes: Behaviour.Refused("Syntax error in IF-expression", 5),
                Fixed: """
                    program marks
                      implicit none
                      integer :: mark
                      mark = 70
                      if (mark == 40) then
                        print '(A)', 'forty'
                      end if
                      print '(I0)', mark
                    end program marks
                    """,
                FixedDoes: Behaviour.Printing("70"),
                WhatChanged: "== compares the mark with 40 without changing it."),
            new WorkedExample("A mark that is not forty",
                Broken: """
                    program marks
                      implicit none
                      integer :: mark
                      mark = 50
                      if (mark != 40) then
                        print '(A)', 'not forty'
                      end if
                    end program marks
                    """,
                BrokenDoes: Behaviour.Refused("Missing ')' in statement", 5),
                Fixed: """
                    program marks
                      implicit none
                      integer :: mark
                      mark = 50
                      if (mark /= 40) then
                        print '(A)', 'not forty'
                      end if
                    end program marks
                    """,
                FixedDoes: Behaviour.Printing("not forty"),
                WhatChanged: "/= is Fortran's \"is not equal to\"; with !, everything after the ! was a comment."),
        ]);

    private static Lesson PowerShell => new(Concept, CodeLanguage.PowerShell,
        "PowerShell compares with words - -eq, -gt, -lt - and its symbols mean other things: = gives a variable a value, and > sends " +
        "output to a file. In an if, neither is an error: = changes the variable and the if is taken, and > writes a file, leaving " +
        "the condition empty, so false.",
        [
            new WorkedExample("Checking a mark",
                Broken: """
                    $mark = 70
                    if ($mark = 40) {
                        Write-Output "forty"
                    }
                    Write-Output $mark
                    """,
                BrokenDoes: Behaviour.Printing("forty\n40"),
                Fixed: """
                    $mark = 70
                    if ($mark -eq 40) {
                        Write-Output "forty"
                    }
                    Write-Output $mark
                    """,
                FixedDoes: Behaviour.Printing("70"),
                WhatChanged: "-eq compares the mark with 40 without changing it."),
            new WorkedExample("A mark over forty",
                Broken: """
                    $mark = 50
                    if ($mark > 40) {
                        Write-Output "pass"
                    }
                    Write-Output "done"
                    """,
                BrokenDoes: Behaviour.Printing("done"),
                Fixed: """
                    $mark = 50
                    if ($mark -gt 40) {
                        Write-Output "pass"
                    }
                    Write-Output "done"
                    """,
                FixedDoes: Behaviour.Printing("pass\ndone"),
                WhatChanged: "-gt compares; > wrote 50 to a file named 40, and the if had nothing to test."),
        ]);

    private static Lesson Elixir => new(Concept, CodeLanguage.Elixir,
        "= in Elixir matches. In an if's condition it gives the variable the value, and the condition is then that value - true for " +
        "anything but false and nil - so the block always runs. Elixir warns only that the value the variable had first is never used.",
        [
            new WorkedExample("Is the mark forty?",
                Broken: """
                    mark = 70

                    if mark = 40 do
                      IO.puts("forty")
                    end

                    IO.puts(mark)
                    """,
                BrokenDoes: Behaviour.Printing("forty\n40"),
                Fixed: """
                    mark = 70

                    if mark == 40 do
                      IO.puts("forty")
                    end

                    IO.puts(mark)
                    """,
                FixedDoes: Behaviour.Printing("70"),
                WhatChanged: "== asks whether mark is 40 and leaves it as it was, so the if is skipped and mark is still 70."),
        ]);

    private static Lesson Erlang => new(Concept, CodeLanguage.Erlang,
        "= in Erlang matches, and an if's condition may only compare - so Mark = 40 in an if is an illegal guard expression, and erlc " +
        "refuses the module before any of it runs.",
        [
            new WorkedExample("Is the mark forty?",
                Broken: """
                    -module(grades).
                    -export([main/0]).

                    is_forty(Mark) ->
                        if
                            Mark = 40 -> true;
                            true -> false
                        end.

                    main() ->
                        io:format("~p~n", [is_forty(70)]).
                    """,
                BrokenDoes: Behaviour.Refused("illegal guard expression", 6),
                Fixed: """
                    -module(grades).
                    -export([main/0]).

                    is_forty(Mark) ->
                        if
                            Mark == 40 -> true;
                            true -> false
                        end.

                    main() ->
                        io:format("~p~n", [is_forty(70)]).
                    """,
                FixedDoes: Behaviour.Printing("false"),
                WhatChanged: "== asks whether Mark is 40, and leaves Mark as it was."),
        ]);
}
