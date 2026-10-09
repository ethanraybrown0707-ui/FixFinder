using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Python's own errors and warnings, each explained for someone new to programming.
/// </summary>
internal static class PythonGuides
{
    private static GuideEntry Syntax(string pattern, string explanation, string why, string fix, string example) => new()
    {
        ExceptionTypes = ["SyntaxError", "IndentationError", "TabError"],
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example),
    };

    private static GuideEntry Error(
        string type, string explanation, string why, string fix, string example, string? pattern = null) => new()
    {
        ExceptionTypes = [type],
        Message = pattern is null ? null : new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example),
    };

    private const string NothingRuns =
        "Python reads the whole file before it runs any of it, so while this is wrong not a single line of the program runs.";

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Syntax(@"expected ':'",
            "In Python, a line that starts a group of lines - such as if, for, while or def - has to end with a colon (:). " +
            "The colon tells Python that the indented lines underneath belong to it, and this line is missing it.",
            NothingRuns,
            "Put a colon at the end of the line that starts the block.",
            """
            if score > 50:
                print("pass")
            """),

        Syntax(@"was never closed|unexpected EOF|EOF while scanning",
            "Brackets come in pairs: every ( needs a ), every [ a ] and every { a }. One of them was opened and never closed, " +
            "so Python kept reading to the end of the file looking for its partner.",
            NothingRuns + " The line Python names is often after the real mistake.",
            "Find the opening bracket and add its closing partner where the expression ends.",
            """
            total = sum(prices)
            print(round(total, 2))
            """),

        Syntax(@"unmatched '[)\]}]'|closing parenthesis .* does not match",
            "Every closing bracket needs an opening bracket of the same kind before it. Here there is a closing bracket with " +
            "no partner, or a bracket that closes the wrong kind - a ] closing a (, for example.",
            NothingRuns,
            "Remove the extra closing bracket, or change it to the kind that was opened.",
            """
            values = [1, 2, 3]
            print(len(values))
            """),

        Syntax(@"unterminated (?:triple-quoted )?string|EOL while scanning string",
            "Text in Python goes between quotes, and the quote at the start needs a matching quote at the end. This text " +
            "starts with a quote, but the line ends before the closing one.",
            NothingRuns,
            "Close the string with the same kind of quote it was opened with.",
            """
            name = "Alice"
            print("Hello, " + name)
            """),

        Syntax(@"expected an indented block",
            "A line ending in a colon - like if, for or def - starts a block, and Python needs at least one line underneath " +
            "it that is pushed further in, to know what belongs to it. Nothing indented follows this one.",
            NothingRuns,
            "Indent the lines that belong to the block, or write pass if the block is meant to be empty for now.",
            """
            def greet(name):
                print("Hello", name)
            """),

        Syntax(@"unexpected indent",
            "Python uses the spaces at the start of a line to tell which lines belong together. This line is pushed in " +
            "further than the one above it, but the line above did not start a new block - it does not end with a colon.",
            NothingRuns,
            "Line the statement up with the lines around it.",
            """
            total = 0
            total = total + 5
            """),

        Syntax(@"unindent does not match|inconsistent use of tabs",
            "When a block ends, the next line has to line up exactly with a line above it. This line's spaces do not line up " +
            "with any of them - often because some lines use tabs and others spaces, which look the same but are not.",
            NothingRuns,
            "Use four spaces for each level everywhere, and line this statement up with the block it belongs to.",
            """
            for n in range(3):
                if n > 0:
                    print(n)
                print("done with", n)
            """),

        Syntax(@"Missing parentheses in call to 'print'",
            "print is a function, and a function needs round brackets around what you give it. Examples written for the old " +
            "Python 2 leave the brackets out, which no longer works.",
            NothingRuns,
            "Put brackets around what print should print.",
            """
            print("Hello, world")
            """),

        Syntax(@"Maybe you meant '==' or ':='|cannot assign to",
            "One = stores a value in a variable; two == compare two values. An if or a while needs a comparison, so it needs " +
            "==. The same message appears when the left side of = is something that cannot store a value, like a number or " +
            "a function call.",
            NothingRuns,
            "Use == to compare two values; keep = for storing a value in a variable.",
            """
            if answer == 42:
                print("correct")
            """),

        Syntax(@"invalid character|invalid non-printable character",
            "The line has a character that looks like ordinary punctuation but is not - usually a curly quote or a long dash " +
            "copied from a word processor or a web page. Python only understands the plain keyboard versions.",
            NothingRuns,
            "Retype the character: straight quotes ' or \", and a plain minus sign -.",
            """
            message = "It's done"
            """),

        Syntax(@"'return' outside function|'break' outside loop|'continue' (?:not properly in|outside) loop|'yield' outside function|'await' outside",
            "return only makes sense inside a function, and break and continue only inside a loop - they end the function, or " +
            "jump within the loop. This one is not inside any function or loop, often because its indentation moved it out.",
            NothingRuns,
            "Indent the statement so it sits inside the function or loop it belongs to.",
            """
            def find(items, target):
                for item in items:
                    if item == target:
                        return item
                return None
            """),

        Syntax(@"invalid decimal literal|leading zeros|invalid syntax",
            "Python could not make sense of this line at all. Usually something small is missing - a comma between items, an " +
            "operator between two values, a closing bracket on the line before - or a name starts with a digit, which names " +
            "are not allowed to do.",
            NothingRuns,
            "Look at the exact place the arrow points to, and at the end of the line before: add the missing comma, operator or " +
            "bracket, or rename anything that starts with a digit.",
            """
            first_score = 10
            scores = [first_score, 20, 30]
            """),

        Syntax(@"f-string",
            "An f-string is text with {curly braces} where Python puts in values. Something inside one of the braces is not " +
            "right - often a { without its }, or the same kind of quote used inside the braces as around the whole text.",
            NothingRuns,
            "Balance every { with a }, and use the other kind of quote for strings inside the braces.",
            """
            print(f"{name}'s total is {prices['apple']}")
            """),

        Error("SyntaxWarning",
            "`is` asks whether two names point at the very same thing in memory; == asks whether two values are equal. Two " +
            "equal numbers or strings can be separate things in memory, so `is` can say no where == would say yes.",
            "Small numbers and short strings happen to be shared, so the check works in testing and then fails with other values.",
            "Compare values with == and !=.",
            """
            if count == 5:
                print("five")
            """,
            pattern: @"""is"" with (?:a|'\w+') literal|""is not"" with"),

        Error("SyntaxWarning",
            "assert checks that something is true. Putting the condition and its message together in brackets makes one " +
            "tuple - a pair of values - and a tuple with anything in it always counts as true, so this assert can never fail.",
            "The assert can never fail, so it never catches the mistake it was written to catch.",
            "Remove the brackets: assert condition, message.",
            """
            assert total >= 0, "total should never be negative"
            """,
            pattern: @"assertion is always true"),

        Error("SyntaxWarning",
            "In ordinary text a backslash has a special job: \\n means a new line and \\t a tab. When a backslash is followed " +
            "by a letter with no special meaning, such as \\d, Python warns, because a later version will treat it as an error.",
            "Python keeps the backslash for now but warns that it will become an error; in a regular expression the meaning can change.",
            "Write the string as a raw string with an r in front, or double the backslash.",
            """
            pattern = r"\d+"
            """,
            pattern: @"invalid escape sequence"),

        Error("SyntaxWarning",
            "Items in a list or tuple need commas between them. Without the comma, Python reads (1, 2) (3, 4) as 'call (1, 2) " +
            "with (3, 4)' - as if the first item were a function - and that fails when the line runs.",
            "When the line runs it crashes with a TypeError.",
            "Add the missing comma between the items.",
            """
            points = [(1, 2), (3, 4)]
            """,
            pattern: @"is not callable; perhaps you missed a comma"),

        Error("SyntaxWarning",
            "The code in finally always runs as the try ends - even when it ended with an error. A return, break or continue " +
            "in finally ends things right there, so an error the try raised is thrown away without anyone ever seeing it.",
            "Errors disappear without a trace, and the function returns something other than what the try block decided.",
            "Move the return out of the finally block, after the whole try statement.",
            """
            try:
                result = compute()
            finally:
                close_file()
            return result
            """,
            pattern: @"in a 'finally' block"),

        Error("NameError",
            "A name is how the program refers to a value, and Python only knows a name once a line has given it one - with =, " +
            "def or import. This line uses a name Python has not been told about: it may be spelt differently where it is " +
            "set, or be set further down.",
            "The program crashes at this line, and everything after it is skipped.",
            "Check the spelling against where the name is defined, define it before this line, or add the import it needs.",
            """
            import math

            radius = 3
            area = math.pi * radius ** 2
            """),

        Error("UnboundLocalError",
            "When a function stores a value in a name anywhere inside it, Python treats that name as the function's own - even " +
            "on the lines before. This line reads it before the function has stored anything in it, so Python will not fall " +
            "back to a variable of the same name outside.",
            "The function crashes whenever it reaches this line.",
            "Give the variable a value at the start of the function, or declare it global if it is meant to be the module's variable.",
            """
            count = 0

            def add_one():
                global count
                count = count + 1
            """),

        Error("TypeError",
            "+ adds numbers and joins text, but it will not do both at once. \"5\" + 3 could mean \"53\" or 8, so Python " +
            "refuses to guess and stops.",
            "The line crashes every time it runs with a number in it.",
            "Turn the number into text with str(), or use an f-string.",
            """
            age = 20
            print("You are " + str(age))
            print(f"You are {age}")
            """,
            pattern: @"can only concatenate str|must be str, not|unsupported operand type\(s\) for \+: 'int' and 'str'|unsupported operand type\(s\) for \+: 'str' and 'int'"),

        Error("TypeError",
            "An operator like +, - or < only works on values that fit together. One side here is a kind of value the " +
            "operator cannot combine with the other - a number and a list, say, or a value and None, which means 'nothing'.",
            "The line crashes whenever those types meet.",
            "Convert one side to match the other, or find out why one side has the wrong type - often a function that returned None.",
            """
            quantity = int(input("How many? "))
            total = quantity * 2.5
            """,
            pattern: @"unsupported operand type|not supported between instances"),

        Error("TypeError",
            "Round brackets after a name mean 'run this function'. This name holds a value - a number, some text, a list - " +
            "rather than a function, so there is nothing to run. Often a variable has the same name as a function and hides it.",
            "The line crashes every time it runs.",
            "Remove the brackets if you meant the value, or rename the variable that is hiding a function of the same name.",
            """
            numbers = [3, 1, 2]
            total = sum(numbers)
            """,
            pattern: @"object is not callable"),

        Error("TypeError",
            "A function lists the values it needs in its def line. This call gives it more, fewer or differently named values " +
            "than that list, so Python cannot match them up.",
            "The call crashes, so the function never runs.",
            "Pass exactly the arguments the def line lists, or give the extra parameters default values.",
            """
            def greet(name, greeting="Hello"):
                print(greeting, name)

            greet("Sam")
            """,
            pattern: @"missing \d+ required positional argument|takes \d+ positional arguments? but \d+ (?:were|was) given|got an unexpected keyword argument"),

        Error("TypeError",
            "Square brackets pick one item out of a list, a string or a dictionary. The value here is none of those - it may " +
            "be a number, None, or a function that needed calling first - so there is nothing to pick from.",
            "The line crashes every time it runs.",
            "Index the list or string itself, and check the value is not None - or a function that should have been called first.",
            """
            names = get_names()
            first = names[0]
            """,
            pattern: @"not subscriptable"),

        Error("TypeError",
            "A for loop goes through the items of a collection - a list, a string, a range. This value has no items to go " +
            "through: it may be a single number, or None. To repeat something n times, loop over range(n).",
            "The line crashes every time it runs.",
            "Loop over a list, a string or range(n) - for a count, range(count) - and check the value is not None.",
            """
            for i in range(count):
                print(i)
            """,
            pattern: @"not iterable|cannot unpack non-iterable"),

        Error("TypeError",
            "A dictionary's keys and a set's items have to be values that can never change, so Python can find them again " +
            "quickly. Lists, dictionaries and sets can change, so they cannot be keys or set items; a tuple, which cannot " +
            "change, can.",
            "The line crashes every time it runs.",
            "Use a tuple instead of a list, or a frozenset instead of a set.",
            """
            seen = set()
            seen.add(tuple(point))
            """,
            pattern: @"unhashable type"),

        Error("TypeError",
            "Each operation expects a certain kind of value - a number, some text, a list. This line gives one of them a kind " +
            "of value it cannot use.",
            "The line crashes whenever it gets a value of that type.",
            "Convert the value to the type the operation needs - int(), float(), str(), list() - before using it.",
            """
            count = int(input("How many? "))
            print(count + 1)
            """),

        Error("AttributeError",
            "A dot after a value asks it for something it has, as names.sort does. This value has nothing by that name: the " +
            "name may be misspelt, belong to another kind of value, or the value may be None - 'nothing' - because an earlier " +
            "function did not return anything.",
            "The line crashes every time it runs.",
            "Check the spelling and the type of the value; if it is None, fix whatever should have given it a value.",
            """
            names = ["b", "a"]
            names.sort()
            print(names)
            """),

        Error("IndexError",
            "The items in a list are numbered from 0, not 1. A list of three items has items 0, 1 and 2, so asking for item 3 " +
            "goes past the end. A loop that counts one step too far does this on its last pass.",
            "The program crashes as soon as the index goes out of range - often on the last pass of a loop.",
            "Loop over the items directly, use range(len(items)), or check the index is less than len(items) first.",
            """
            for i in range(len(items)):
                print(items[i])
            """),

        Error("KeyError",
            "A dictionary finds each value by its key, like a word in a glossary. This key is not in the dictionary: it may be " +
            "spelt differently, in different capitals, of a different kind - the text \"1\" is not the number 1 - or not added yet.",
            "The line crashes whenever the key is missing.",
            "Use dictionary.get(key, default), check with `if key in dictionary`, or add the key first.",
            """
            price = prices.get("apple", 0)
            """),

        Error("ValueError",
            "The value is the right kind of thing but has the wrong content: int() can turn \"42\" into a number, but not " +
            "\"forty-two\" or \"3.5\". This usually happens when someone types something the program did not expect.",
            "The program crashes as soon as someone types or reads a value like that.",
            "Check or clean the value before converting it, or catch the ValueError and ask again.",
            """
            while True:
                try:
                    age = int(input("Age: "))
                    break
                except ValueError:
                    print("Please type a whole number.")
            """),

        Error("ZeroDivisionError",
            "Dividing by zero has no answer, so Python stops rather than make one up. The value divided by is 0 here - often " +
            "because it counts the items of a list that turned out to be empty.",
            "The program crashes whenever the divisor is zero - often when a list is empty.",
            "Check that the divisor is not zero before dividing, and decide what the answer should be when it is.",
            """
            average = total / count if count > 0 else 0
            """),

        Error("RecursionError",
            "A function that calls itself needs a stopping point - a case where it returns without calling itself again. " +
            "Without one, or when the calls never get closer to it, it keeps calling itself until Python runs out of room " +
            "and stops it.",
            "Python gives up after about a thousand calls, and the program crashes.",
            "Add a base case that returns without calling the function again, and make every call move closer to it.",
            """
            def factorial(n):
                if n <= 1:
                    return 1
                return n * factorial(n - 1)
            """),

        Error("ModuleNotFoundError",
            "import brings in code from a module. Python looked for a module with this name and found none: it may not be " +
            "installed yet, or its name may be spelt differently from the module's real name.",
            "The program stops on its first lines, before anything else runs.",
            "Check the spelling, or install the package with pip for the same Python.",
            """
            import requests
            """),

        Error("ImportError",
            "from module import name takes one thing out of a module. Python found the module, but there is nothing in it " +
            "called that - check the spelling and the capitals.",
            "The program stops on its first lines, before anything else runs.",
            "Check the spelling and capitals of the imported name, and that the module really defines it.",
            """
            from math import sqrt
            """),

        Error("FileNotFoundError",
            "The program asked for a file that is not where it looked. A name like scores.txt is looked for in the folder the " +
            "program was started from, which may not be the folder the program's own file is in.",
            "The program crashes as soon as it tries to open the file.",
            "Check the name, and build the path from the program's own folder so it works wherever the program is run from.",
            """
            from pathlib import Path

            data = Path(__file__).parent / "scores.txt"
            with open(data) as file:
                lines = file.readlines()
            """),

        Error("RuntimeError",
            "A for loop keeps track of where it is in a dictionary or set. Adding or removing items while the loop is going " +
            "muddles that up, so Python stops. Loop over a copy instead.",
            "Python stops the loop with an error, because it can no longer tell which items it has seen.",
            "Loop over a copy - list(d.keys()) - or build a new collection instead of changing the one being looped over.",
            """
            for key in list(stock.keys()):
                if stock[key] == 0:
                    del stock[key]
            """,
            pattern: @"changed size during iteration"),

        Error("EOFError",
            "input() waits for someone to type a line. The program was given no more lines to read - nothing was typed, or the " +
            "input ran out - so input() had nothing to give back.",
            "The program stops at the question it asked.",
            "Give the program its input - in FixFinder, type the answers into the input box, one per line.",
            """
            name = input("Name: ")
            """),

        Error("AssertionError",
            "assert is a check the programmer wrote down: 'this must be true here'. It was not true, so Python stopped the " +
            "program at the check, before the wrong value could cause more trouble later on.",
            "The program stops here, because something the code relies on is not true.",
            "Find out why the condition is false - the assert is doing its job - and fix the value that broke it.",
            """
            assert len(items) > 0, "there should be at least one item"
            """),

        Error("StopIteration",
            "next() takes the next item from something that hands out items one at a time. There were no items left, so " +
            "there was nothing to take. A for loop handles this for you by simply stopping.",
            "The program crashes, or inside a generator the loop ends early without saying why.",
            "Pass a default to next(iterator, None), or loop with for instead.",
            """
            first = next(iter(items), None)
            """),
    ];
}
