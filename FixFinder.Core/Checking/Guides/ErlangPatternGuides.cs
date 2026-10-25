using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic checks of Erlang programs, each explained for someone new to programming. Every example is a whole
/// module, and ErlangLiveTests compiles each one with erlc -Wall and runs it, and fails on anything Erlang says.
/// </summary>
internal static class ErlangPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-erlang-text-compared-with-number"], "Text compared with a number",
            "io:get_line gives back what was typed as text - a list of characters - even when it is a number. Erlang can compare any two " +
            "values, and it puts every number before every list, so text compared with a number gives the same answer whatever was typed: " +
            "\"7\\n\" > 17 is true.",
            "The comparison gives the same answer for every input, with no error.",
            "Read the line as a number as it is read: list_to_integer(string:trim(io:get_line(\"Age? \"))).",
            """
            -module(ages).
            -export([main/0]).

            describe(Age) when Age > 17 -> "adult";
            describe(_Age) -> "child".

            main() ->
                Age = list_to_integer(string:trim("7\n")),
                io:format("~s~n", [describe(Age)]).
            """),

        GuideFor(["logic-erlang-input-not-trimmed"], "A line read with its newline still on it",
            "io:get_line gives back the line that was typed with the newline from pressing Enter still on the end: typing y gives \"y\\n\". " +
            "Compared with \"y\", it is not equal.",
            "The comparison is false whatever was typed, so the program takes the other branch every time.",
            "Trim the line as it is read: string:trim(io:get_line(\"Continue? \")).",
            """
            -module(answers).
            -export([main/0]).

            describe("y") -> "yes";
            describe(_Answer) -> "no".

            main() ->
                Answer = string:trim("y\n"),
                io:format("~s~n", [describe(Answer)]).
            """),

        GuideFor(["logic-erlang-float-equality"], "Decimal numbers compared exactly",
            "Decimals are stored in binary, which holds most of them only nearly: 0.1 + 0.2 is held as 0.30000000000000004, not 0.3.",
            "== asks for an exact match, so a total worked out by arithmetic can fail to equal the decimal it should.",
            "Compare within a tiny tolerance: abs(Total - 0.3) < 1.0e-9 - Erlang writes the tolerance with a point before the e.",
            """
            -module(totals).
            -export([main/0]).

            close(Total) -> abs(Total - 0.3) < 1.0e-9.

            main() ->
                io:format("~p~n", [close(0.1 + 0.2)]).
            """),

        GuideFor(["logic-erlang-list-printed-as-text"], "A list of numbers printed as text",
            "Text in Erlang is a list of numbers, one for each character, so ~p cannot tell a list of numbers from text - and prints any list " +
            "whose numbers are all printable characters as text: [80, 70, 90] prints as \"PFZ\".",
            "The numbers are printed as letters, so the output is wrong with no error.",
            "Print a list of numbers with ~w, which prints it as it is: [80,70,90].",
            """
            -module(marks).
            -export([main/0]).

            main() ->
                Marks = [80, 70, 90],
                io:format("~w~n", [Marks]).
            """),

        GuideFor(["logic-erlang-number-joined-to-text"], "A number joined to text with ++",
            "++ joins two lists, and text is a list - but a number is not, so \"Total: \" ++ Total is not text, and io:format given it stops " +
            "the program with bad argument.",
            "The program stops at the line that prints.",
            "Let io:format put the number in the text: io:format(\"Total: ~p~n\", [Total]).",
            """
            -module(marks).
            -export([main/0]).

            main() ->
                Total = lists:sum([70, 80]),
                io:format("Total: ~p~n", [Total]).
            """),

        GuideFor(["logic-erlang-result-discarded"], "A changed copy that is thrown away",
            "Erlang's values never change. lists:sort, lists:reverse, string:uppercase and their like give back a new, changed copy and leave " +
            "the value they were given as it was - so a line that calls one and keeps nothing changes nothing.",
            "The change never happens, so the lines after it go on using the value as it was.",
            "Give the copy a name of its own - a name is given its value once - and use that name after: Sorted = lists:sort(Marks).",
            """
            -module(marks).
            -export([main/0]).

            main() ->
                Marks = [80, 70, 90],
                Sorted = lists:sort(Marks),
                io:format("~w~n", [Sorted]).
            """),

        GuideFor(["logic-erlang-odd-test-negative"], "An odd number looked for with rem 2 == 1",
            "rem gives what is left over after dividing, and it keeps the sign of the number divided: 7 rem 2 is 1, but -7 rem 2 is -1. " +
            "So N rem 2 == 1 is true only for odd numbers above 0 - a negative odd number leaves -1, and is taken for even.",
            "The program is right until it meets a negative odd number, which it treats as even, and erlc says nothing.",
            "Ask whether anything is left over: N rem 2 =/= 0 is true for every odd number, negative or not.",
            """
            -module(parity).
            -export([main/0]).

            describe(N) when N rem 2 =/= 0 -> "odd";
            describe(_N) -> "even".

            main() ->
                io:format("~s~n", [describe(-3)]).
            """),

        GuideFor(["logic-erlang-nth-from-zero"], "lists:nth from position 0",
            "lists:nth counts positions from 1: lists:nth(1, List) is the first item. There is no position 0, so lists:nth(0, ...) - or " +
            "positions taken from lists:seq(0, ...) - stops the program with function_clause.",
            "The program stops at the first lookup, with the error in lists itself.",
            "Count the positions from 1 to the length: lists:seq(1, length(Marks)).",
            """
            -module(positions).
            -export([main/0]).

            main() ->
                Marks = [70, 80, 90],
                [io:format("~p~n", [lists:nth(Position, Marks)]) || Position <- lists:seq(1, length(Marks))].
            """),
    ];
}
