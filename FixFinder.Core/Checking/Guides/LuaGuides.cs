using System.Text.RegularExpressions;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Lua's syntax errors, and the runtime errors that stop its programs - each explained for someone new to programming.
/// </summary>
/// <remarks>
/// Every message matched here is one Lua 5.4.6 was seen to give. Every example is a whole program, and LuaLiveTests checks
/// each one with luac -p and runs it, and fails on anything Lua says.
/// </remarks>
internal static class LuaGuides
{
    private const string NothingRuns = "Lua reads the whole file before it runs any of it, so while this is wrong no part of the program runs.";

    private const string Stops = "The program stops at this line, and nothing after it runs.";

    private static GuideEntry Syntax(string pattern, string explanation, string fix, string example) => new()
    {
        ExceptionTypes = [LuaParser.SyntaxError],
        Message = new Regex(pattern),
        Guide = new MistakeGuide(explanation, NothingRuns, fix, example),
    };

    private static GuideEntry Runtime(string pattern, string explanation, string fix, string example) => new()
    {
        ExceptionTypes = [LuaParser.RuntimeError],
        Message = new Regex(pattern),
        Guide = new MistakeGuide(explanation, Stops, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Syntax(@"near '!'$",
            "Lua writes \"is not equal to\" as ~=, and \"not\" as the word not. It has no ! at all, so it stopped reading where the ! is.",
            "Write ~= for \"is not equal to\", and not for \"not\".",
            """
            local mark = 50
            if mark ~= 40 then
              print("not forty")
            end
            """),

        Syntax(@"^syntax error near '(?:[-+*/%^]|\.\.)'$",
            "Lua has no shorthand for changing a variable by an amount - no +=, -=, *= or ++, as some languages have - so it stopped " +
            "reading at the operator.",
            "Write the change out in full: total = total + 5, or count = count + 1.",
            """
            local total = 10
            total = total + 5
            print(total)
            """),

        Syntax(@"^'then' expected",
            "In Lua, the condition of an if or an elseif ends with the word then. Without it, Lua read on looking for then, and stopped " +
            "where it was sure it was missing - often at the start of the next line.",
            "Put then at the end of the if or elseif line - or, when the line has then, look in its condition for something Lua could not read.",
            """
            local mark = 50
            if mark > 40 then
              print("pass")
            end
            """),

        Syntax(@"^'do' expected",
            "In Lua, the heading of a for or while loop ends with the word do. Without it, Lua read on looking for do, and stopped where " +
            "it was sure it was missing - often at the start of the next line.",
            "Put do at the end of the for or while line.",
            """
            local count = 0
            while count < 3 do
              count = count + 1
            end
            print(count)
            """),

        Syntax(@"^'end' expected",
            "A block - a function, an if, a for or while loop, or a do - was opened and not closed with end, and Lua reached the line it " +
            "names, or the end of the file, still waiting for it. Lua takes each end to close the latest block, so when an inner block's end " +
            "is the one missing, the block it says was left open is the outer one.",
            "Put an end where the block finishes. Indenting each block's lines shows where each one ends.",
            """
            local function average(marks)
              local total = 0
              for _, mark in ipairs(marks) do
                total = total + mark
              end
              return total / #marks
            end

            print(average({70, 80}))
            """),

        Syntax(@"^'[)\]}]' expected",
            "A bracket was opened and not closed - Lua says where it was opened - so Lua reached the place it names still waiting for it.",
            "Close the bracket where the call, the index or the table ends.",
            """
            local total = 10
            print(total)
            """),

        Syntax(@"^unfinished string",
            "A piece of text was started with a quote that is never closed on its line, so Lua took the rest of the line - and found no end to it.",
            "Close the text with the same kind of quote it opened with. For text over several lines, use [[ and ]].",
            """
            local name = "Ada"
            print(name)
            """),

        Syntax(@"^<eof> expected near 'end'",
            "There is an end with nothing open for it to close, so Lua expected the file to end there instead - often an end too many after an if or a loop.",
            "Match each end to the block it closes, and remove the one left over.",
            """
            local mark = 50
            if mark > 40 then
              print("pass")
            end
            """),

        Syntax(@"^unexpected symbol near",
            "Lua found a symbol where nothing of the kind can go - often a bracket closed twice, or one that was never opened.",
            "Look at the line Lua names for a bracket too many, or a symbol Lua does not have.",
            """
            local total = 10
            print(total)
            """),

        Syntax(@"^attempt to assign to const variable",
            "This local was marked <const>, so it can never be given another value - and Lua will not even read a line that tries.",
            "Leave the constant as it is, or make it an ordinary local, without <const>, if its value has to change.",
            """
            local passMark = 40
            passMark = 50
            print(passMark)
            """),

        Syntax(@"^syntax error near",
            "Lua could not make sense of the code at the word or symbol it names - something is missing just before it, such as = in an " +
            "assignment, or there is a word Lua does not have, such as continue.",
            "Read the line up to the place Lua names, and check each part is something Lua has: Lua has goto, but no continue.",
            """
            local total
            total = 10
            print(total)
            """),

        Runtime(@"^attempt to perform arithmetic on a nil value",
            "In Lua a name that was never given a value is nil - nothing - and nil cannot be used in arithmetic. Often the name is spelt " +
            "differently from where it is set, or it is a parameter the call gave no value for: Lua gives a missing one nil.",
            "Check the spelling against where the value is set, and that every call gives each parameter a value.",
            """
            local function average(total, count)
              return total / count
            end
            print(average(10, 2))
            """),

        Runtime(@"^attempt to call a nil value",
            "Lua was asked to call a function by a name that holds nothing - nil. The name may be spelt differently from where the " +
            "function is written, or the function is written further down the file than the call: Lua runs a file from the top, so " +
            "below the call it does not exist yet.",
            "Check the name against the function's own, and write the function above the line that calls it.",
            """
            local function average(marks)
              return (marks[1] + marks[2]) / 2
            end
            print(average({70, 80}))
            """),

        Runtime(@"^attempt to index a nil value",
            "Something was looked up inside a value that is nil - a table that is not there, or a key a table does not have, so the " +
            "next step has nothing to look in.",
            "Check the table and each key on the way are there before going on: if students.ada then ... end.",
            """
            local students = {ada = {name = "Ada"}}
            if students.bob then
              print(students.bob.name)
            else
              print("No such student")
            end
            """),

        Runtime(@"^attempt to concatenate a nil value",
            ".. joins text, and one side is nil - a variable never given a value, or a key a table does not have.",
            "Give the value a default before joining: (name or \"nobody\").",
            """
            local name
            print("Hello, " .. (name or "nobody"))
            """),

        Runtime(@"^attempt to get length of a nil value",
            "# gives the length of a table or a piece of text, and this value is nil - a list never made, or a key a table does not have.",
            "Make the list before using it: local marks = {}.",
            """
            local marks = {}
            print(#marks)
            """),

        Runtime(@"^attempt to compare (?:\w+ with nil|nil with \w+)",
            "< and > compare two numbers, or two pieces of text, and one side is nil - a variable never given a value.",
            "Give the variable its value before comparing it, or check for nil first: if mark and mark > 40 then.",
            """
            local mark = 50
            if mark and mark > 40 then
              print("pass")
            end
            """),

        Runtime(@"^attempt to compare (?:number with string|string with number)",
            "< and > compare numbers with numbers and text with text, and Lua will not compare a number with text - even text such as \"5\".",
            "Turn the text into a number first: tonumber(typed).",
            """
            local typed = "5"
            if tonumber(typed) > 3 then
              print("big")
            end
            """),

        Runtime(@"^attempt to add a '(?:string|number)' with a '(?:string|number)'|^attempt to perform arithmetic on a string value",
            "+ only adds numbers in Lua, and one side is text that is not a number. Text is joined with .., which turns a number into text as it joins.",
            "Join text with .., or turn text that holds a number into one with tonumber.",
            """
            local total = 12
            print("Total: " .. total)
            """),

        Runtime(@"^attempt to (?:perform arithmetic on|concatenate) a table value",
            "A table - a list, or a set of keys and values - was used as a number or as text, which a table is not.",
            "Use the items in the table: table.concat(marks, \", \") joins a list's items as text.",
            """
            local marks = {70, 80}
            print("Marks: " .. table.concat(marks, ", "))
            """),

        Runtime(@"^attempt to (?:divide by zero|perform 'n%%?0')",
            "A whole number was divided by zero with // or %, which has no answer, so Lua stopped. Dividing with / does not stop: it gives " +
            "inf - or, for zero by zero, nan, which Lua on Windows prints as -nan(ind) - and the program carries on with that.",
            "Check the divisor is not zero first, and decide what the answer should be when it is.",
            """
            local sweets = 7
            local people = 0
            if people == 0 then
              print("nobody to share with")
            else
              print(sweets // people)
            end
            """),

        Runtime(@"^stack overflow",
            "Each call of a function waits for the calls it makes, and a function that calls itself with no case that stops it waits for " +
            "ever - until Lua runs out of room to keep track and stops it.",
            "Give the function a case that returns without calling itself, and make each call come nearer to it.",
            """
            local function countDown(number)
              if number == 0 then
                return 0
              end
              return 1 + countDown(number - 1)
            end
            print(countDown(3))
            """),

        new GuideEntry
        {
            RuleIds = ["lua-elseif"],
            Guide = new MistakeGuide(
                "Lua writes \"else if\" as one word, elseif. else if is an else with a new if inside it - and that if needs an end of its " +
                "own, so Lua reached the end of the file still looking for it.",
                NothingRuns,
                "Write elseif, which starts the next branch of the same if.",
                """
                local mark = 70
                if mark > 80 then
                  print("high")
                elseif mark > 50 then
                  print("pass")
                end
                """),
        },

        new GuideEntry
        {
            RuleIds = ["lua-colon-for-method"],
            Guide = new MistakeGuide(
                "A string's own functions - upper, lower, sub, len - are called with a colon: name:upper(). The colon gives the function the " +
                "string itself to work on. With a dot, name.upper() calls it with nothing at all.",
                Stops,
                "Call it with a colon: name:upper() - or give the string as the first argument: string.upper(name).",
                """
                local name = "ada"
                print(name:upper())
                """),
        },

        Runtime(@"^bad argument #\d+ to",
            "One of Lua's own functions was given a value it cannot use - the message says which argument, by its number, what it " +
            "expected, and what it got.",
            "Give the function the kind of value it asks for - a whole number for %d in string.format, text for string.upper.",
            """
            print(string.format("%d", 2))
            """),
    ];
}
