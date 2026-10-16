namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>A name that has not been defined, in each language: what it does, shown by running a program that does it.</summary>
internal static class UndefinedNameLessons
{
    private static Concept Concept => Concepts.UndefinedName;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, JavaScript, Scala, OCaml, Rust, Ruby, Php, Lua, Perl, Dart, Fortran, PowerShell, Elixir, Erlang];

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "Python looks a name up only when the line using it runs, so a misspelt name stops the program with NameError at that " +
        "line - after every line before it has already run.",
        [
            new WorkedExample("A total spelt two ways",
                Broken: """
                    total = 5
                    print(totl)
                    """,
                BrokenDoes: Behaviour.Stopping("NameError", 2),
                Fixed: """
                    total = 5
                    print(total)
                    """,
                FixedDoes: Behaviour.Printing("5"),
                WhatChanged: "The name is spelt as it was when it was made: total.")
            {
                WrongFixes =
                [
                    new WrongFix("""
                        total = 5
                        print("totl")
                        """,
                        Behaviour.Printing("totl"),
                        "Quotes make it a piece of text: it prints the letters totl, not the value of total."),
                ],
            },
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "Java checks every name before the program runs: a name it cannot find stops javac with \"cannot find symbol\", and no " +
        "part of the program runs.",
        [
            new WorkedExample("A total spelt two ways",
                Broken: """
                    public class Total {
                        public static void main(String[] args) {
                            int total = 5;
                            System.out.println(totl);
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Refused("cannot find symbol", 4),
                Fixed: """
                    public class Total {
                        public static void main(String[] args) {
                            int total = 5;
                            System.out.println(total);
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("5"),
                WhatChanged: "The name is spelt as it was declared: total."),
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "JavaScript looks a name up when the line using it runs, so a misspelt name stops the program with ReferenceError at that line.",
        [
            new WorkedExample("A total spelt two ways",
                Broken: """
                    const total = 5;
                    console.log(totl);
                    """,
                BrokenDoes: Behaviour.Stopping("ReferenceError", 2),
                Fixed: """
                    const total = 5;
                    console.log(total);
                    """,
                FixedDoes: Behaviour.Printing("5"),
                WhatChanged: "The name is spelt as it was declared: total."),
        ]);

    private static Lesson Scala => new(Concept, CodeLanguage.Scala,
        "Scala checks every name before the program runs: a name it cannot find stops the compiler with \"Not found\", and no part " +
        "of the program runs.",
        [
            new WorkedExample("A total spelt two ways",
                Broken: """
                    object Total {
                      def main(args: Array[String]): Unit = {
                        val total = 5
                        println(totl)
                      }
                    }
                    """,
                BrokenDoes: Behaviour.Refused("Not found", 4),
                Fixed: """
                    object Total {
                      def main(args: Array[String]): Unit = {
                        val total = 5
                        println(total)
                      }
                    }
                    """,
                FixedDoes: Behaviour.Printing("5"),
                WhatChanged: "The name is spelt as it was defined: total."),
        ]);

    private static Lesson OCaml => new(Concept, CodeLanguage.OCaml,
        "OCaml checks every name before the program runs: a name it cannot find stops the compiler with \"Unbound value\", and no " +
        "part of the program runs.",
        [
            new WorkedExample("A total spelt two ways",
                Broken: """
                    let total = 5

                    let () = print_int totl
                    """,
                BrokenDoes: Behaviour.Refused("Unbound value", 3),
                Fixed: """
                    let total = 5

                    let () = print_int total
                    """,
                FixedDoes: Behaviour.Printing("5"),
                WhatChanged: "The name is spelt as it was defined: total."),
        ]);

    private static Lesson Rust => new(Concept, CodeLanguage.Rust,
        "Every name has to be made before it is used, and spelt the same each time. rustc refuses a name it cannot find with E0425, " +
        "before any of the program runs - and often names the one it thinks was meant.",
        [
            new WorkedExample("A misspelt total",
                Broken: """
                    fn main() {
                        let total = 10;
                        println!("{}", totl);
                    }
                    """,
                BrokenDoes: Behaviour.Refused("E0425", 3),
                Fixed: """
                    fn main() {
                        let total = 10;
                        println!("{}", total);
                    }
                    """,
                FixedDoes: Behaviour.Printing("10"),
                WhatChanged: "The name is spelt as it was made: total."),
        ]);

    private static Lesson Ruby => new(Concept, CodeLanguage.Ruby,
        "Every name has to be made before it is used, and spelt the same each time. Ruby stops the program with NameError when it " +
        "reaches a name it cannot find, and names the one it thinks was meant after \"Did you mean?\".",
        [
            new WorkedExample("A misspelt total",
                Broken: """
                    total = 10
                    puts totl
                    """,
                BrokenDoes: Behaviour.Stopping("NameError", 2),
                Fixed: """
                    total = 10
                    puts total
                    """,
                FixedDoes: Behaviour.Printing("10"),
                WhatChanged: "The name is spelt as it was made: total."),
        ]);

    private static Lesson Php => new(Concept, CodeLanguage.Php,
        "Every function has to be made before it is called, and its name spelt the same each time. PHP stops the program with Error, " +
        "Call to undefined function, naming the line of the call. A misspelt variable is different: PHP only warns - Undefined " +
        "variable - and carries on with nothing in its place, so the program can go on to print a wrong answer.",
        [
            new WorkedExample("An average",
                Broken: """
                    <?php
                    function average($marks) {
                        return array_sum($marks) / count($marks);
                    }
                    echo averge([70, 80]);
                    """,
                BrokenDoes: Behaviour.Stopping("Error", 5),
                Fixed: """
                    <?php
                    function average($marks) {
                        return array_sum($marks) / count($marks);
                    }
                    echo average([70, 80]);
                    """,
                FixedDoes: Behaviour.Printing("75"),
                WhatChanged: "The call spells average the way its function line does."),
        ]);

    private static Lesson Lua => new(Concept, CodeLanguage.Lua,
        "In Lua, a name that was never given a value is not an error by itself: it is nil. A misspelt name stops the program only " +
        "when its nil is used - in arithmetic, say, which Lua cannot do with nil - and Lua names the misspelt name at the end of " +
        "its message, as (global 'totl').",
        [
            new WorkedExample("Adding one to a total",
                Broken: """
                    local total = 10
                    print(totl + 1)
                    """,
                BrokenDoes: Behaviour.Stopping("attempt to perform arithmetic on a nil value", 2),
                Fixed: """
                    local total = 10
                    print(total + 1)
                    """,
                FixedDoes: Behaviour.Printing("11"),
                WhatChanged: "The name is spelt the way the line that gives it its value spells it."),
        ]);

    private static Lesson Perl => new(Concept, CodeLanguage.Perl,
        "With use strict at the top, Perl refuses the whole file before any of it runs if a variable is used without being declared " +
        "with my - which catches a misspelt name. Without use strict, a misspelt name is a new, empty variable, and the program " +
        "carries on with nothing in it.",
        [
            new WorkedExample("Adding one to a total",
                Broken: """
                    use strict;
                    use warnings;

                    my $total = 10;
                    print $totl + 1, "\n";
                    """,
                BrokenDoes: Behaviour.Refused("Global symbol", 5),
                Fixed: """
                    use strict;
                    use warnings;

                    my $total = 10;
                    print $total + 1, "\n";
                    """,
                FixedDoes: Behaviour.Printing("11"),
                WhatChanged: "The name is spelt the way the my line declares it."),
        ]);

    private static Lesson Dart => new(Concept, CodeLanguage.Dart,
        "Every name has to be declared before it is used, and spelt the same each time. Dart refuses a program with a name it does " +
        "not know before any of it runs: Undefined name.",
        [
            new WorkedExample("Adding one to a total",
                Broken: """
                    void main() {
                      var total = 10;
                      print(totl + 1);
                    }
                    """,
                BrokenDoes: Behaviour.Refused("Undefined name 'totl'", 3),
                Fixed: """
                    void main() {
                      var total = 10;
                      print(total + 1);
                    }
                    """,
                FixedDoes: Behaviour.Printing("11"),
                WhatChanged: "The name is spelt the way its declaration spells it."),
        ]);

    private static Lesson Fortran => new(Concept, CodeLanguage.Fortran,
        "With implicit none at the top, every name has to be declared before it is used, and gfortran refuses the program before " +
        "any of it runs when one is not - often saying which declared name it thinks was meant. Without implicit none, a misspelt " +
        "name would be a new variable instead, with no value.",
        [
            new WorkedExample("Adding one to a total",
                Broken: """
                    program marks
                      implicit none
                      integer :: total
                      total = 10
                      print '(I0)', totl + 1
                    end program marks
                    """,
                BrokenDoes: Behaviour.Refused("Symbol 'totl' at (1) has no IMPLICIT type", 5),
                Fixed: """
                    program marks
                      implicit none
                      integer :: total
                      total = 10
                      print '(I0)', total + 1
                    end program marks
                    """,
                FixedDoes: Behaviour.Printing("11"),
                WhatChanged: "The name is spelt the way its declaration spells it."),
        ]);

    private static Lesson PowerShell => new(Concept, CodeLanguage.PowerShell,
        "A variable PowerShell has never seen a value for is $null, with no error - so a misspelt name is simply nothing, and the " +
        "script carries on with it. Set-StrictMode -Version Latest at the top makes PowerShell say so instead.",
        [
            new WorkedExample("Adding one to a total",
                Broken: """
                    $total = 10
                    Write-Output ($totl + 1)
                    """,
                BrokenDoes: Behaviour.Printing("1"),
                Fixed: """
                    $total = 10
                    Write-Output ($total + 1)
                    """,
                FixedDoes: Behaviour.Printing("11"),
                WhatChanged: "The name is spelt the way the line that sets it spells it."),
        ]);

    private static Lesson Elixir => new(Concept, CodeLanguage.Elixir,
        "Elixir compiles the whole file before running any of it, and a variable used without ever being given a value - often a name " +
        "spelt differently - stops it compiling, with undefined variable.",
        [
            new WorkedExample("Printing the total",
                Broken: """
                    marks = [70, 80]
                    total = Enum.sum(marks)
                    IO.puts(totl)
                    """,
                BrokenDoes: Behaviour.Refused("undefined variable", 3),
                Fixed: """
                    marks = [70, 80]
                    total = Enum.sum(marks)
                    IO.puts(total)
                    """,
                FixedDoes: Behaviour.Printing("150"),
                WhatChanged: "The name is spelt as it was when it was given its value."),
        ]);

    private static Lesson Erlang => new(Concept, CodeLanguage.Erlang,
        "erlc compiles the whole module before any of it runs, and a variable used without being given a value - often a name spelt " +
        "differently - stops it compiling. erlc compares the name with the variables it knows, and suggests one after did you mean.",
        [
            new WorkedExample("Printing the total",
                Broken: """
                    -module(marks).
                    -export([main/0]).

                    main() ->
                        Marks = [70, 80],
                        Total = lists:sum(Marks),
                        io:format("~p~n", [Totl]).
                    """,
                BrokenDoes: Behaviour.Refused("variable 'Totl' is unbound", 7),
                Fixed: """
                    -module(marks).
                    -export([main/0]).

                    main() ->
                        Marks = [70, 80],
                        Total = lists:sum(Marks),
                        io:format("~p~n", [Total]).
                    """,
                FixedDoes: Behaviour.Printing("150"),
                WhatChanged: "The name is spelt as it was when it was given its value - Total, as erlc suggested."),
        ]);
}
