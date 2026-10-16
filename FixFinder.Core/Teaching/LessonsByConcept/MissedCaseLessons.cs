namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>A case nobody handled, in each language: what it does, shown by running a program that does it.</summary>
internal static class MissedCaseLessons
{
    private static Concept Concept => Concepts.MissedCase;

    public static IReadOnlyList<Lesson> All { get; } = [Scala, Java, Python, OCaml, Rust, Elixir, Erlang, FSharp];

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

    private static Lesson Rust => new(Concept, CodeLanguage.Rust,
        "A match has to have an arm for every value that can arrive, and rustc checks it does before the program runs: one that " +
        "leaves values out is refused with E0004, which names them. A last arm _ takes every value the others leave.",
        [
            new WorkedExample("Describing a mark",
                Broken: """
                    fn describe(mark: u8) -> &'static str {
                        match mark {
                            1 => "first",
                            2 => "second",
                        }
                    }

                    fn main() {
                        println!("{}", describe(3));
                    }
                    """,
                BrokenDoes: Behaviour.Refused("E0004", 2),
                Fixed: """
                    fn describe(mark: u8) -> &'static str {
                        match mark {
                            1 => "first",
                            2 => "second",
                            _ => "another",
                        }
                    }

                    fn main() {
                        println!("{}", describe(3));
                    }
                    """,
                FixedDoes: Behaviour.Printing("another"),
                WhatChanged: "_ => takes any value the arms above it do not, so every mark gets an answer."),
        ]);

    private static Lesson Elixir => new(Concept, CodeLanguage.Elixir,
        "When none of a case's clauses matches the value, Elixir stops the program with CaseClauseError and shows the value.",
        [
            new WorkedExample("A grade for every mark",
                Broken: """
                    mark = 20

                    grade =
                      case mark do
                        70 -> "A"
                        40 -> "C"
                      end

                    IO.puts(grade)
                    """,
                BrokenDoes: Behaviour.Stopping("CaseClauseError", 4),
                Fixed: """
                    mark = 20

                    grade =
                      case mark do
                        70 -> "A"
                        40 -> "C"
                        _ -> "F"
                      end

                    IO.puts(grade)
                    """,
                FixedDoes: Behaviour.Printing("F"),
                WhatChanged: "The last clause, _ ->, matches anything the clauses above it did not."),
        ]);

    private static Lesson Erlang => new(Concept, CodeLanguage.Erlang,
        "When none of a case's clauses matches the value, Erlang stops the program with case_clause and shows the value.",
        [
            new WorkedExample("A grade for every mark",
                Broken: """
                    -module(grades).
                    -export([main/0]).

                    grade(Mark) ->
                        case Mark of
                            70 -> "A";
                            40 -> "C"
                        end.

                    main() ->
                        io:format("~s~n", [grade(20)]).
                    """,
                BrokenDoes: Behaviour.Stopping("case_clause", 5),
                Fixed: """
                    -module(grades).
                    -export([main/0]).

                    grade(Mark) ->
                        case Mark of
                            70 -> "A";
                            40 -> "C";
                            _ -> "F"
                        end.

                    main() ->
                        io:format("~s~n", [grade(20)]).
                    """,
                FixedDoes: Behaviour.Printing("F"),
                WhatChanged: "The last clause, _ ->, matches anything the clauses above it did not."),
        ]);

    private static Lesson FSharp => new(Concept, CodeLanguage.FSharp,
        "F# warns when a match has no case for some values it can be given (FS0025), and runs the script anyway. Given one of " +
        "those values, no case fits, and the script stops with MatchFailureException.",
        [
            new WorkedExample("Describing a mark",
                Broken: """
                    let describe mark =
                        match mark with
                        | 100 -> "full"
                        | 0 -> "none"

                    printfn "%s" (describe 50)
                    """,
                BrokenDoes: Behaviour.Stopping("MatchFailureException", 4),
                Fixed: """
                    let describe mark =
                        match mark with
                        | 100 -> "full"
                        | 0 -> "none"
                        | _ -> "some"

                    printfn "%s" (describe 50)
                    """,
                FixedDoes: Behaviour.Printing("some"),
                WhatChanged: "| _ -> matches every value the cases above it do not, so the match has a case for 50."),
        ]);
}
