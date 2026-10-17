using System.Text.RegularExpressions;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// What erlc says of an Erlang program as it compiles it, and what stops one as it runs - each explained for someone new to
/// programming.
/// </summary>
/// <remarks>
/// Every message matched here is one Erlang/OTP 29 was seen to give; what stops a program is matched by Erlang's own name for
/// its reason - badarith, badmatch. Every example is a whole module, and ErlangLiveTests compiles each one with erlc -Wall and
/// runs it, and fails on anything Erlang says.
/// </remarks>
internal static class ErlangGuides
{
    private const string NothingRuns = "erlc will not compile a program with this error, so while it is there no part of the program runs.";

    private const string Stops = "The program stops at this line, and nothing after it runs.";

    private const string Runs = "erlc only warns, and compiles the program - but this is very likely a mistake, or code that does nothing.";

    private static GuideEntry Refused(string pattern, string explanation, string fix, string example) => Entry(ErlangParser.CompileError, pattern, explanation, NothingRuns, fix, example);

    private static GuideEntry Warned(string pattern, string explanation, string fix, string example) => Entry(ErlangParser.CompileWarning, pattern, explanation, Runs, fix, example);

    private static GuideEntry Stopped(string reason, string explanation, string fix, string example) => Entry(reason, null, explanation, Stops, fix, example);

    private static GuideEntry Entry(string type, string? pattern, string explanation, string consequence, string fix, string example) => new()
    {
        ExceptionTypes = [type],
        Message = pattern is null ? null : new Regex(pattern),
        Guide = new MistakeGuide(explanation, consequence, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Refused(@"^variable '\w+' is unbound",
            "No variable by this name is given a value before this line. It is spelt differently from where it is given one, it is given " +
            "its value later, or it starts with a small letter where it should start with a capital. erlc often suggests the one it knows, " +
            "after did you mean.",
            "Use the name as it is given its value, and give it a value before this line.",
            """
            -module(marks).
            -export([main/0]).

            main() ->
                Marks = [70, 80],
                Total = lists:sum(Marks),
                io:format("~p~n", [Total]).
            """),

        Refused(@"^function \S+ undefined",
            "The module has no function by this name taking this many arguments. It is spelt differently, it takes a different number of " +
            "arguments, it is in another module and needs that module's name before it - lists:sum, not sum - or -export names a function " +
            "the module never defines. erlc often suggests the one it has, after did you mean.",
            "Use the name and the number of arguments the function is defined with, or put its module's name before it.",
            """
            -module(marks).
            -export([main/0]).

            average(Marks) -> lists:sum(Marks) / length(Marks).

            main() ->
                io:format("~p~n", [average([70, 80])]).
            """),

        Refused(@"^syntax error before: ",
            "erlc could not make sense of the code at the place the ^ marks, and the message names what it found there. Most often the " +
            "line above has no separator at its end: a comma between two expressions, a semicolon between two clauses, or a full stop at " +
            "the end of a function. Two comparisons joined with and or or stop here too - and and or bind more tightly than < and >, so " +
            "they need andalso and orelse.",
            "Look at the end of the line above for a missing , ; or . - and join comparisons with andalso or orelse.",
            """
            -module(grades).
            -export([main/0]).

            grade(Mark) when Mark > 40 andalso Mark < 70 -> "pass";
            grade(_Mark) -> "other".

            main() ->
                Mark = 50,
                io:format("~s~n", [grade(Mark)]).
            """),

        Refused(@"^head mismatch",
            "The clauses of a function must all have its name and the same number of arguments, each ending with a semicolon but the last, " +
            "which ends with a full stop. A clause here has another name or number - often because the clause before ends with a semicolon " +
            "where a full stop was meant, so erlc read on into the next function as one more clause.",
            "End the function's last clause with a full stop, or give the clause the function's name.",
            """
            -module(grades).
            -export([main/0]).

            letter(70) -> "A";
            letter(_Mark) -> "C".

            main() ->
                io:format("~s~n", [letter(70)]).
            """),

        Refused(@"^variable '\w+' unsafe in",
            "The variable is given its value in only some branches of a case or an if, and read after it - so whether it has a value " +
            "depends on which branch ran.",
            "Give the case's own result to the variable: Grade = case ... end.",
            """
            -module(grades).
            -export([main/0]).

            grade(Mark) ->
                Grade = case Mark > 40 of
                    true -> "pass";
                    false -> "fail"
                end,
                Grade.

            main() ->
                io:format("~s~n", [grade(50)]).
            """),

        Refused(@"^unterminated string",
            "A string was started with \" and never closed, so erlc read the rest of the file as part of it.",
            "Close the string with \" where it ends.",
            """
            -module(hello).
            -export([main/0]).

            main() ->
                io:format("hello~n").
            """),

        Refused(@"^illegal guard expression",
            "A guard - the condition after when, or each condition of an if - may only use comparisons, arithmetic and a few of Erlang's own " +
            "functions, such as length and abs. A function of your own, or of another module such as string:trim, cannot be called there.",
            "Work the value out before the guard, and compare that - or use case, which can call any function.",
            """
            -module(names).
            -export([main/0]).

            describe(Name) ->
                Trimmed = string:trim(Name),
                if
                    length(Trimmed) > 2 -> "long";
                    true -> "short"
                end.

            main() ->
                io:format("~s~n", [describe(" Ada ")]).
            """),

        Warned(@"^variable '\w+' is unused",
            "This variable is given a value that nothing reads - often because it is spelt differently where it was meant to be read, or a " +
            "step was left unfinished.",
            "Read it where it was meant to be read, or start its name with _ when it is meant to be unused.",
            """
            -module(marks).
            -export([main/0]).

            main() ->
                Marks = [70, 80],
                io:format("~p~n", [lists:sum(Marks)]).
            """),

        Warned(@"^function \S+ is unused",
            "This function is neither exported nor called within its module, so nothing can call it.",
            "Call it where it was meant to be called, or export it with -export.",
            """
            -module(marks).
            -export([main/0]).

            total(Marks) -> lists:sum(Marks).

            main() ->
                io:format("~p~n", [total([70, 80])]).
            """),

        Warned(@"^this clause cannot match because a previous clause at line \d+ always matches",
            "Erlang tries a function's clauses from the top, and an earlier clause takes every value this one would - so this one is never used.",
            "Put the clauses for particular values first, and the clause that takes anything last.",
            """
            -module(grades).
            -export([main/0]).

            letter(70) -> "A";
            letter(_Mark) -> "F".

            main() ->
                io:format("~s~n", [letter(70)]).
            """),

        Warned(@"^no clause will ever match",
            "The two sides of the = can never match. Most often a name starts with a small letter, which makes it an atom - a fixed value, " +
            "like a word - rather than a variable: total = 10 asks whether the atom total is 10, which it never is.",
            "Start a variable's name with a capital letter: Total = 10.",
            """
            -module(marks).
            -export([main/0]).

            main() ->
                Total = 10,
                io:format("~p~n", [Total]).
            """),

        Warned(@"^the call to \S+ will fail with a",
            "erlc worked out, from the values written into the code, that this call will stop the program - its warning names how.",
            "Give the function what it needs: list_to_integer, for one, reads text that is a whole number and nothing else.",
            """
            -module(numbers).
            -export([main/0]).

            main() ->
                io:format("~p~n", [list_to_integer("12") + 1]).
            """),

        Warned(@"^evaluation of operator .+ will fail with a 'badarith' exception",
            "erlc worked out that this arithmetic will stop the program: one side is not a number - text written in quotes, for one.",
            "Turn text into a number before the arithmetic, with list_to_integer.",
            """
            -module(numbers).
            -export([main/0]).

            main() ->
                Typed = "5",
                io:format("~p~n", [list_to_integer(Typed) + 1]).
            """),

        Warned(@"^this clause cannot match because its guard evaluates to 'false'",
            "erlc worked out, from the values written into the code, that this guard is never true. With decimals it is often an exact " +
            "comparison that arithmetic makes false: 0.1 + 0.2 is 0.30000000000000004, not 0.3.",
            "Compare decimals within a tiny tolerance: abs(Total - 0.3) < 1.0e-9.",
            """
            -module(totals).
            -export([main/0]).

            close(Total) -> abs(Total - 0.3) < 1.0e-9.

            main() ->
                io:format("~p~n", [close(0.1 + 0.2)]).
            """),

        Stopped("badarith",
            "Arithmetic - + - * / div rem - was given something it cannot work with: something that is not a number, or a division by zero, " +
            "which stops an Erlang program. When an operator was given something that is not a number, Erlang shows what it was called as.",
            "Check what you divide by first, and give an answer of your own when it is zero - and turn text into a number before arithmetic.",
            """
            -module(marks).
            -export([main/0]).

            average(Marks) ->
                case length(Marks) of
                    0 -> 0;
                    Count -> lists:sum(Marks) / Count
                end.

            main() ->
                io:format("~p~n", [average([])]).
            """),

        Stopped("badmatch",
            "= in Erlang matches: the right side must have the shape of the left, and a variable that already has a value must be given the " +
            "same value again - a variable is given its value once. So Total = Total + Mark inside lists:foreach stops here, and so does " +
            "total = 10, as total is an atom. Erlang shows the value it could not match.",
            "Give a new value a new name - or build a total with lists:foldl.",
            """
            -module(marks).
            -export([main/0]).

            main() ->
                Total = lists:foldl(fun(Mark, Sum) -> Sum + Mark end, 0, [70, 80]),
                io:format("~p~n", [Total]).
            """),

        Stopped("function_clause",
            "The function was called with arguments none of its clauses takes - each clause takes only the values its pattern and its " +
            "when allow. lists:nth counts from 1, so lists:nth(0, ...) stops here too.",
            "Add a clause for these arguments - often a last one that takes anything - or check what was passed in.",
            """
            -module(grades).
            -export([main/0]).

            letter(Mark) when Mark >= 70 -> "A";
            letter(Mark) when Mark >= 40 -> "C";
            letter(_Mark) -> "F".

            main() ->
                io:format("~s~n", [letter(20)]).
            """),

        Stopped("case_clause",
            "None of the case's clauses matched the value - Erlang shows the value. A case has to have a clause for every value it can be given.",
            "Add a clause for this value - often a last one, _ ->, for anything else.",
            """
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
            """),

        Stopped("if_clause",
            "None of the if's conditions was true. An if has to have one that is.",
            "Add a last condition, true ->, for anything else.",
            """
            -module(grades).
            -export([main/0]).

            grade(Mark) ->
                if
                    Mark >= 70 -> "A";
                    Mark >= 40 -> "C";
                    true -> "F"
                end.

            main() ->
                io:format("~s~n", [grade(20)]).
            """),

        Stopped("undef",
            "There is no such function: the module's name or the function's is spelt differently, it takes another number of arguments, or " +
            "it is not exported - only the functions -export names can be called from outside their module.",
            "Use the module's and the function's names as they are, and export a function called from another module.",
            """
            -module(marks).
            -export([main/0]).

            main() ->
                io:format("~p~n", [lists:sum([70, 80])]).
            """),

        Stopped("badarg",
            "One of Erlang's own functions was given an argument it cannot work with. Erlang shows what it was called as and which argument " +
            "was wrong. A line read with io:get_line still ends with its newline, which list_to_integer will not read; ++ joins lists, so a " +
            "number cannot be joined to text with it; and ~s prints text only.",
            "Trim a line before reading it as a number, and print any value with ~p.",
            """
            -module(numbers).
            -export([main/0]).

            main() ->
                Typed = "12\n",
                Number = list_to_integer(string:trim(Typed)),
                io:format("Total: ~p~n", [Number + 1]).
            """),

        Stopped("badkey",
            "The map has no such key: maps:get(Key, Map) needs a key the map has. maps:get(Key, Map, Default) gives Default when the key " +
            "is not there.",
            "Use the key the map has - or maps:get with a default, when the key may not be there.",
            """
            -module(people).
            -export([main/0]).

            main() ->
                Person = #{name => "Ada", age => 36},
                io:format("~p~n", [maps:get(age, Person)]),
                io:format("~p~n", [maps:get(height, Person, unknown)]).
            """),

        Stopped("badmap",
            "A map was needed - for maps:get or #{...} - and the value was something else; Erlang shows what it was.",
            "Check where the value came from, and pass a map.",
            """
            -module(people).
            -export([main/0]).

            age(Person) -> maps:get(age, Person).

            main() ->
                io:format("~p~n", [age(#{age => 36})]).
            """),
    ];
}
