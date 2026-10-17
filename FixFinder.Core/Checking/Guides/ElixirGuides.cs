using System.Text.RegularExpressions;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// What Elixir says of a program as it compiles it, and the errors that stop one - each explained for someone new to
/// programming.
/// </summary>
/// <remarks>
/// Every message matched here is one Elixir 1.20.4 was seen to give, in its own words. Every example is a whole program,
/// and ElixirLiveTests compiles each one with FixFinder's check and runs it, and fails on anything Elixir says.
/// </remarks>
internal static class ElixirGuides
{
    private const string NothingRuns = "Elixir will not compile a program with this error, so while it is there no part of the program runs.";

    private const string Stops = "The program stops at this line, and nothing after it runs.";

    private const string Runs = "Elixir only warns, and runs the program - but this is very likely a mistake, or code that does nothing.";

    private static GuideEntry Refused(string type, string? pattern, string explanation, string fix, string example) => Entry(type, pattern, explanation, NothingRuns, fix, example);

    private static GuideEntry Stopped(string type, string? pattern, string explanation, string fix, string example) => Entry(type, pattern, explanation, Stops, fix, example);

    private static GuideEntry Warned(string pattern, string explanation, string fix, string example) => Entry(ElixirParser.Warning, pattern, explanation, Runs, fix, example);

    private static GuideEntry Entry(string type, string? pattern, string explanation, string consequence, string fix, string example) => new()
    {
        ExceptionTypes = [type],
        Message = pattern is null ? null : new Regex(pattern),
        Guide = new MistakeGuide(explanation, consequence, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Refused(ElixirParser.CompileError, @"^undefined variable """,
            "Elixir knows no variable by this name at this line. It was never given a value, it is spelt differently where it was, or " +
            "it was given its value inside a block - an if, a for, an fn - and a variable given a value inside a block is gone once the " +
            "block ends.",
            "Check the spelling against where the variable is given its value, and give it one before this line, outside any block.",
            """
            marks = [70, 80, 90]
            total = Enum.sum(marks)
            IO.puts(total)
            """),

        Refused(ElixirParser.CompileError, @"^undefined function return/\d+",
            "Elixir has no return. A function gives back the value of its last line, so return is read as a call to a function of " +
            "that name - and there is none.",
            "Take return away and leave the value as the function's last line.",
            """
            defmodule Marks do
              def total(marks) do
                Enum.sum(marks)
              end
            end

            IO.puts(Marks.total([70, 80]))
            """),

        Refused(ElixirParser.CompileError, @"^undefined function [a-z_]\w*[?!]?/\d+ \(there is no such import\)",
            "There is no function by this name that this line can call on its own. Most functions belong to a module and are called with " +
            "its name first - Enum.sum, String.upcase - and the module's name is missing, or the name is spelt differently.",
            "Put the module's name before the function - Enum.sum(marks) - or check the spelling.",
            """
            marks = [70, 80]
            IO.puts(Enum.sum(marks))
            """),

        Refused(ElixirParser.CompileError, @"^undefined function ",
            "The module has no function by this name taking this many arguments. It is spelt differently from where it is defined, it " +
            "takes a different number of arguments, or it is defined in another module and needs that module's name before it.",
            "Check the name and the number of arguments against the def, or put the module's name before it.",
            """
            defmodule Marks do
              def average(marks), do: Enum.sum(marks) / length(marks)
              def report(marks), do: IO.puts(average(marks))
            end

            Marks.report([70, 80])
            """),

        Refused("TokenMissingError", @"^missing terminator: end",
            "Every do in Elixir needs an end, and Elixir read to the end of the file with a block still open - └ unclosed delimiter marks " +
            "the line that opened it. Elixir has no else if or elsif either: written after else, if starts a block of its own that needs " +
            "an end of its own, and elsif is read as a call.",
            "Add the missing end after the block's last line, lined up with the line that starts it. To check one condition after another, " +
            "use cond, which tries each in turn.",
            """
            mark = 50

            cond do
              mark > 70 -> IO.puts("high")
              mark > 40 -> IO.puts("pass")
              true -> IO.puts("fail")
            end
            """),

        Refused("TokenMissingError", @"^missing terminator: ",
            "A bracket or a quote was opened and never closed - the message says which one it expected, └ unclosed delimiter marks where " +
            "it was opened, and └ missing closing delimiter where Elixir gave up looking for it.",
            "Close it where it belongs - often at the end of the same line.",
            """
            marks = [70, 80]
            IO.puts(Enum.sum(marks))
            """),

        Refused("SyntaxError", @"^unexpected reserved word: end",
            "Elixir found an end with no block to close. Most often the line that starts the block - a def, an if - has no do.",
            "Put do at the end of the line that starts the block.",
            """
            defmodule Marks do
              def total(marks) do
                Enum.sum(marks)
              end
            end

            IO.puts(Marks.total([70, 80]))
            """),

        Refused("SyntaxError", @"^syntax error before: ",
            "Elixir could not make sense of the code at the place the ^ marks; the message names what it found there. Most often something " +
            "is missing just before it - a comma between two values, or <> between two pieces of text - or something is written twice, " +
            "such as = =.",
            "Look just before the ^ for what is missing or doubled.",
            """
            first = "Ada"
            last = "Lovelace"
            IO.puts(first <> " " <> last)
            """),

        Refused("MismatchedDelimiterError", null,
            "A closing bracket does not match the bracket it closes: brackets close in the opposite order to the one they were opened in, " +
            "so the ] of a list must come before the ) of the call around it. Elixir marks where each one opened, and says which it expected.",
            "Close the inner bracket first.",
            """
            IO.puts(Enum.sum([70, 80]))
            """),

        Stopped("ArithmeticError", @"^bad argument in arithmetic expression: (?:"".*"" \+ .+|.+ \+ "".*"")$",
            "+ adds numbers, and one side is text. Elixir joins text with <>, and turns a number into text with to_string - or puts it " +
            "inside the text with #{...}.",
            "Join text with <>, turning a number into text first: \"Total: \" <> to_string(total).",
            """
            total = 10
            IO.puts("Total: " <> to_string(total))
            IO.puts("Total: #{total}")
            """),

        Stopped("ArithmeticError", @"^bad argument in arithmetic expression: (?:nil [-+*/]|.+ [-+*/] nil$)",
            "One side of the arithmetic is nil - nothing. Enum.at gives nil for a position past the end of a list, and Map.get gives it " +
            "for a key the map does not have.",
            "Check where the nil came from: a position one past the end, or a key spelt differently.",
            """
            marks = [70, 80]
            IO.puts(Enum.at(marks, 1) + 1)
            """),

        Stopped("ArithmeticError", @"^bad argument in arithmetic expression(?:: (?:div|rem)\(.+, 0\))?$",
            "Dividing by zero stops an Elixir program: / and div have no answer for it. For /, Elixir says no more than this; for div and " +
            "rem it shows the call, ending in 0.",
            "Check what you divide by first, and give an answer of your own when it is zero.",
            """
            marks = []
            count = length(marks)
            IO.puts(if count == 0, do: 0, else: Enum.sum(marks) / count)
            """),

        Stopped("ArithmeticError", @"^bad argument in arithmetic expression",
            "Arithmetic - + - * / - was given something that is not a number. After the colon Elixir shows the arithmetic as it was " +
            "worked out, with the values it had.",
            "Check the value that is not a number, and turn text into a number first with String.to_integer.",
            """
            price = String.to_integer("12")
            IO.puts(price + 1)
            """),

        Stopped("KeyError", null,
            "The map has no such key. map.key asks for a key the map must have; Map.get(map, :key) gives nil when it is not there. " +
            "Elixir often suggests the key it does have, after Did you mean.",
            "Use the key the map has - or Map.get, when the key may not be there.",
            """
            person = %{name: "Ada", age: 36}
            IO.puts(person.age)
            IO.inspect(Map.get(person, :height))
            """),

        Stopped("UndefinedFunctionError", @"\(module \S+ is not available\)",
            "Elixir has no module by this name. A module in another file is only there once that file is brought in - " +
            "Code.require_file(\"helpers.exs\", __DIR__) - and the module's name must be written in full, as it is defined.",
            "Bring in the file that defines the module before calling it, or check the module's name.",
            """
            defmodule Helpers do
              def average(marks), do: Enum.sum(marks) / length(marks)
            end

            IO.puts(Helpers.average([70, 80]))
            """),

        Stopped("UndefinedFunctionError", null,
            "The module has no function by this name taking this many arguments that this line can call. It is spelt differently, it takes " +
            "a different number of arguments, or it is private - defined with defp, which only its own module can call. Elixir often " +
            "suggests the one it has, after Did you mean.",
            "Use the name and the number of arguments the module has - or def, not defp, for a function called from outside its module.",
            """
            marks = [70, 80]
            IO.puts(Enum.sum(marks))
            """),

        Stopped("FunctionClauseError", null,
            "The function was called with arguments none of its clauses takes: each def of it takes only the values its pattern and its " +
            "when allow. Under the message Elixir lists the arguments it was given.",
            "Add a clause for these arguments - often a last one that takes anything - or check what was passed in.",
            """
            defmodule Grade do
              def letter(mark) when mark >= 70, do: "A"
              def letter(mark) when mark >= 40, do: "C"
              def letter(_mark), do: "F"
            end

            IO.puts(Grade.letter(20))
            """),

        Stopped("CaseClauseError", null,
            "None of the case's clauses matched the value - Elixir shows the value under the message. A case has to have a clause for " +
            "every value it can be given.",
            "Add a clause for this value - often a last one, _ ->, for anything else.",
            """
            defmodule Grade do
              def letter(mark) do
                case mark do
                  70 -> "A"
                  40 -> "C"
                  _ -> "F"
                end
              end
            end

            IO.puts(Grade.letter(20))
            """),

        Stopped("MatchError", null,
            "= in Elixir matches: the left side must have the shape of the right. {:ok, value} = ... fails when the right side is " +
            "{:error, ...} - Elixir shows the value it could not match.",
            "Match each shape the value can have with case.",
            """
            defmodule Answer do
              def show(result) do
                case result do
                  {:ok, value} -> IO.puts(value)
                  {:error, reason} -> IO.puts("failed: " <> reason)
                end
              end
            end

            Answer.show({:error, "nope"})
            """),

        Stopped("BadMapError", null,
            "A map was needed - for person.age, or a Map function - and the value was something else: most often nil, from a lookup " +
            "that found nothing, as Map.get gives nil for a key that is not there.",
            "Give Map.get a value to use when the key is not there - Map.get(people, \"Bob\", %{age: 0}) - or check for nil first.",
            """
            people = %{"Ada" => %{age: 36}}
            person = Map.get(people, "Bob", %{age: 0})
            IO.puts(person.age)
            """),

        Stopped("ArgumentError", @"^the Access module does not support accessing lists by index",
            "[ ] reads a map by its key, and a list has no keys: marks[0] does not read a list's first item in Elixir.",
            "Read a list by position with Enum.at(marks, 0), counting from 0.",
            """
            marks = [70, 80]
            IO.puts(Enum.at(marks, 0))
            """),

        Stopped("ArgumentError", @"^errors were found at the given arguments",
            "A function was given an argument it cannot work with. Under the message Elixir says which argument and what is wrong with it " +
            "- not a list, not a textual representation of an integer. A line read with IO.gets still ends with its newline, which " +
            "String.to_integer will not read.",
            "Give the function what it says it needs - and trim a line read with IO.gets first, with String.trim.",
            """
            typed = "12\n"
            IO.puts(String.to_integer(String.trim(typed)) + 1)
            """),

        Stopped("Protocol.UndefinedError", @"^protocol Enumerable not implemented for BitString",
            "Enum's functions go over lists, ranges and maps - not text, which Elixir calls a BitString here.",
            "To go over the letters of text, String.graphemes gives them as a list.",
            """
            letters = String.graphemes("abc")
            IO.inspect(Enum.map(letters, &String.upcase/1))
            """),

        Stopped("RuntimeError", null,
            "The program stopped itself, with raise, and the message is the program's own.",
            "Look at the line raise is on, and at why the program reached it.",
            """
            marks = [70, 80]
            if marks == [], do: raise("no marks"), else: IO.puts(Enum.sum(marks))
            """),

        Warned(@"^variable ""\w+"" is unused \(there is a variable with the same name in the context",
            "Elixir's variables never change. = inside fn, for or if makes a new variable of the same name, which lasts only until the " +
            "block ends - so the variable outside keeps the value it had, and the new one is never used.",
            "Give the block's result to the variable instead: Enum.reduce for a total built up item by item, for with reduce:, or " +
            "variable = if condition, do: new_value, else: variable.",
            """
            marks = [70, 80, 90]
            total = Enum.reduce(marks, 0, fn mark, total -> total + mark end)
            IO.puts(total)
            """),

        Warned(@"^variable ""\w+"" is unused \(if the variable is not meant to be used",
            "This variable is given a value that nothing uses - often because it is spelt differently where it was meant to be used, or a " +
            "step was left unfinished.",
            "Use the variable where it was meant to be used, or remove it.",
            """
            marks = [70, 80]
            IO.puts(Enum.sum(marks))
            """),

        Warned(@"^function \S+ is unused",
            "This private function - defined with defp - is never called within its module, and nothing outside the module can call it.",
            "Call it where it was meant to be used, or remove it.",
            """
            defmodule Marks do
              def average(marks), do: total(marks) / length(marks)
              defp total(marks), do: Enum.sum(marks)
            end

            IO.puts(Marks.average([70, 80]))
            """),

        Warned(@"^this clause for .+ cannot match because a previous clause at line \d+ always matches|^the following clause is redundant",
            "Elixir tries a function's clauses from the top, and an earlier clause takes every value this one would - so this one is " +
            "never used.",
            "Put the clauses for particular values first, and the clause that takes anything last.",
            """
            defmodule Grade do
              def letter(70), do: "A"
              def letter(_mark), do: "F"
            end

            IO.puts(Grade.letter(70))
            """),

        Warned(@"^using single-quoted strings to represent charlists is deprecated",
            "Text in single quotes is not text in Elixir but a charlist - a list of character codes. Text is written in double quotes.",
            "Write text in double quotes: \"Ada\".",
            """
            name = "Ada"
            IO.puts(name)
            """),
    ];
}
