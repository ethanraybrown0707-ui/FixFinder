using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

internal static class PythonGuides
{
    private static GuideEntry Syntax(string pattern, string explanation, string why, string fix, string example) => new()
    {
        ExceptionTypes = ["SyntaxError", "IndentationError", "TabError"],
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example),
    };

    private static GuideEntry Error(string type, string explanation, string why, string fix, string example, string? pattern = null) => new()
    {
        ExceptionTypes = [type],
        Message = pattern is null ? null : new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example),
    };

    private const string NothingRuns =
        "Python reads the whole file before it runs any of it, so while this is wrong not a single line of the program runs.";

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        // ------------------------------------------------------------------ syntax
        Syntax(@"expected ':'",
            "Lines that start a block - if, elif, else, for, while, def, class, try, except, with - must end with a colon.",
            NothingRuns,
            "Put a colon at the end of the line that starts the block.",
            """
            if score > 50:
                print("pass")
            """),

        Syntax(@"was never closed|unexpected EOF|EOF while scanning",
            "A bracket, brace or parenthesis is opened and never closed, so Python reads on to the end of the file looking for it.",
            NothingRuns + " The line Python names is often after the real mistake.",
            "Find the opening bracket and add its closing partner where the expression ends.",
            """
            total = sum(prices)
            print(round(total, 2))
            """),

        Syntax(@"unmatched '[)\]}]'|closing parenthesis .* does not match",
            "There is a closing bracket with no opening bracket to match it, or it closes the wrong kind of bracket.",
            NothingRuns,
            "Remove the extra closing bracket, or change it to the kind that was opened.",
            """
            values = [1, 2, 3]
            print(len(values))
            """),

        Syntax(@"unterminated (?:triple-quoted )?string|EOL while scanning string",
            "A string starts with a quote and the line ends before the matching quote.",
            NothingRuns,
            "Close the string with the same kind of quote it was opened with.",
            """
            name = "Alice"
            print("Hello, " + name)
            """),

        Syntax(@"expected an indented block",
            "A line ending in a colon must be followed by at least one indented line - the block it starts.",
            NothingRuns,
            "Indent the lines that belong to the block, or write pass if the block is meant to be empty for now.",
            """
            def greet(name):
                print("Hello", name)
            """),

        Syntax(@"unexpected indent",
            "This line is indented further than the line before it, but nothing above it opened a new block.",
            NothingRuns,
            "Line the statement up with the lines around it.",
            """
            total = 0
            total = total + 5
            """),

        Syntax(@"unindent does not match|inconsistent use of tabs",
            "The indentation of this line does not line up with any block above it, often because tabs and spaces are mixed.",
            NothingRuns,
            "Use four spaces for each level everywhere, and line this statement up with the block it belongs to.",
            """
            for n in range(3):
                if n > 0:
                    print(n)
                print("done with", n)
            """),

        Syntax(@"Missing parentheses in call to 'print'",
            "In Python 3, print is a function, so what it prints must go in brackets.",
            NothingRuns,
            "Put brackets around what print should print.",
            """
            print("Hello, world")
            """),

        Syntax(@"Maybe you meant '==' or ':='|cannot assign to",
            "A single = is used where a comparison is needed, or something is assigned to that cannot hold a value.",
            NothingRuns,
            "Use == to compare two values; keep = for storing a value in a variable.",
            """
            if answer == 42:
                print("correct")
            """),

        Syntax(@"invalid character|invalid non-printable character",
            "The line contains a character Python does not allow in code, often a curly quote or a dash pasted from a document.",
            NothingRuns,
            "Retype the character: straight quotes ' or \", and a plain minus sign -.",
            """
            message = "It's done"
            """),

        Syntax(@"'return' outside function|'break' outside loop|'continue' (?:not properly in|outside) loop|'yield' outside function|'await' outside",
            "The statement is only allowed inside a function or a loop, and this one is outside every function or loop.",
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
            "Python could not read this line as any statement it knows. The usual causes are a missing operator or comma, a name " +
            "that starts with a digit, or a word from another language.",
            NothingRuns,
            "Look at the exact place the arrow points to, and at the end of the line before: add the missing comma, operator or " +
            "bracket, or rename anything that starts with a digit.",
            """
            first_score = 10
            scores = [first_score, 20, 30]
            """),

        Syntax(@"f-string",
            "Something inside the braces of an f-string is not valid, often an unbalanced brace or the same quote used inside and outside.",
            NothingRuns,
            "Balance every { with a }, and use the other kind of quote for strings inside the braces.",
            """
            print(f"{name}'s total is {prices['apple']}")
            """),

        // ------------------------------------------------------------------ warnings the compiler gives
        Error("SyntaxWarning",
            "`is` checks whether two things are the very same object, not whether they are equal.",
            "Small numbers and short strings happen to be shared, so the check works in testing and then fails with other values.",
            "Compare values with == and !=.",
            """
            if count == 5:
                print("five")
            """,
            pattern: @"""is"" with (?:a|'\w+') literal|""is not"" with"),

        Error("SyntaxWarning",
            "The brackets turn the assert into a check of a tuple, and a tuple with anything in it always counts as true.",
            "The assert can never fail, so it never catches the mistake it was written to catch.",
            "Remove the brackets: assert condition, message.",
            """
            assert total >= 0, "total should never be negative"
            """,
            pattern: @"assertion is always true"),

        Error("SyntaxWarning",
            "A backslash inside a normal string starts an escape sequence, and this one (such as \\d) is not a real escape.",
            "Python keeps the backslash for now but warns that it will become an error; in a regular expression the meaning can change.",
            "Write the string as a raw string with an r in front, or double the backslash.",
            """
            pattern = r"\d+"
            """,
            pattern: @"invalid escape sequence"),

        Error("SyntaxWarning",
            "Two things are written side by side with nothing between them, so Python tries to call the first one with the second as its argument.",
            "When the line runs it crashes with a TypeError.",
            "Add the missing comma between the items.",
            """
            points = [(1, 2), (3, 4)]
            """,
            pattern: @"is not callable; perhaps you missed a comma"),

        Error("SyntaxWarning",
            "A return, break or continue in a finally block overrides whatever the try block was doing - including an error it raised.",
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

        // ------------------------------------------------------------------ runtime
        Error("NameError",
            "The line uses a name - a variable, a function or a module - that Python does not know at this point. It was never " +
            "defined, is misspelt, is used before the line that defines it, or needs an import.",
            "The program crashes at this line, and everything after it is skipped.",
            "Check the spelling against where the name is defined, define it before this line, or add the import it needs.",
            """
            import math

            radius = 3
            area = math.pi * radius ** 2
            """),

        Error("UnboundLocalError",
            "The function assigns to this name somewhere, which makes it local to the function, but this line reads it before any " +
            "value has been assigned.",
            "The function crashes whenever it reaches this line.",
            "Give the variable a value at the start of the function, or declare it global if it is meant to be the module's variable.",
            """
            count = 0

            def add_one():
                global count
                count = count + 1
            """),

        Error("TypeError",
            "Text and a number are joined with +, and Python will not guess whether you meant arithmetic or text.",
            "The line crashes every time it runs with a number in it.",
            "Turn the number into text with str(), or use an f-string.",
            """
            age = 20
            print("You are " + str(age))
            print(f"You are {age}")
            """,
            pattern: @"can only concatenate str|must be str, not|unsupported operand type\(s\) for \+: 'int' and 'str'|unsupported operand type\(s\) for \+: 'str' and 'int'"),

        Error("TypeError",
            "The values on each side of the operator are of types it cannot combine, such as a number and a list, or a value and None.",
            "The line crashes whenever those types meet.",
            "Convert one side to match the other, or find out why one side has the wrong type - often a function that returned None.",
            """
            quantity = int(input("How many? "))
            total = quantity * 2.5
            """,
            pattern: @"unsupported operand type|not supported between instances"),

        Error("TypeError",
            "Something is called with brackets as if it were a function, but it is a value such as a number, a string or a list.",
            "The line crashes every time it runs.",
            "Remove the brackets if you meant the value, or rename the variable that is hiding a function of the same name.",
            """
            numbers = [3, 1, 2]
            total = sum(numbers)
            """,
            pattern: @"object is not callable"),

        Error("TypeError",
            "The function is called with a different number of arguments than it was written to take.",
            "The call crashes, so the function never runs.",
            "Pass exactly the arguments the def line lists, or give the extra parameters default values.",
            """
            def greet(name, greeting="Hello"):
                print(greeting, name)

            greet("Sam")
            """,
            pattern: @"missing \d+ required positional argument|takes \d+ positional arguments? but \d+ (?:were|was) given|got an unexpected keyword argument"),

        Error("TypeError",
            "Square brackets are used on a value that cannot be indexed, such as a number, None or a function.",
            "The line crashes every time it runs.",
            "Index the list or string itself, and check the value is not None - or a function that should have been called first.",
            """
            names = get_names()
            first = names[0]
            """,
            pattern: @"not subscriptable"),

        Error("TypeError",
            "The code loops over, or unpacks, a value that is not a collection - often a number or None.",
            "The line crashes every time it runs.",
            "Loop over a list, a string or range(n) - for a count, range(count) - and check the value is not None.",
            """
            for i in range(count):
                print(i)
            """,
            pattern: @"not iterable|cannot unpack non-iterable"),

        Error("TypeError",
            "A list, dictionary or set is used as a dictionary key or put in a set, and only values that never change can be.",
            "The line crashes every time it runs.",
            "Use a tuple instead of a list, or a frozenset instead of a set.",
            """
            seen = set()
            seen.add(tuple(point))
            """,
            pattern: @"unhashable type"),

        Error("TypeError",
            "A value of the wrong type is passed to an operation that needs a particular type.",
            "The line crashes whenever it gets a value of that type.",
            "Convert the value to the type the operation needs - int(), float(), str(), list() - before using it.",
            """
            count = int(input("How many? "))
            print(count + 1)
            """),

        Error("AttributeError",
            "The value does not have the attribute or method the line asks for. The name may be misspelt, belong to another type, " +
            "or the value may be None because something earlier returned nothing.",
            "The line crashes every time it runs.",
            "Check the spelling and the type of the value; if it is None, fix whatever should have given it a value.",
            """
            names = ["b", "a"]
            names.sort()
            print(names)
            """),

        Error("IndexError",
            "The index is past the end of the list or string. Indexes start at 0, so the last item of a list of n items is at n - 1.",
            "The program crashes as soon as the index goes out of range - often on the last pass of a loop.",
            "Loop over the items directly, use range(len(items)), or check the index is less than len(items) first.",
            """
            for i in range(len(items)):
                print(items[i])
            """),

        Error("KeyError",
            "The dictionary has no entry with this key. The key may be misspelt, differ in case or type, or not have been added yet.",
            "The line crashes whenever the key is missing.",
            "Use dictionary.get(key, default), check with `if key in dictionary`, or add the key first.",
            """
            price = prices.get("apple", 0)
            """),

        Error("ValueError",
            "The value has the right type but a content the operation cannot use - such as int() given text that is not a whole number.",
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
            "The code divides by a value that is zero at this point.",
            "The program crashes whenever the divisor is zero - often when a list is empty.",
            "Check that the divisor is not zero before dividing, and decide what the answer should be when it is.",
            """
            average = total / count if count > 0 else 0
            """),

        Error("RecursionError",
            "The function calls itself again and again without ever reaching a case that stops it.",
            "Python gives up after about a thousand calls, and the program crashes.",
            "Add a base case that returns without calling the function again, and make every call move closer to it.",
            """
            def factorial(n):
                if n <= 1:
                    return 1
                return n * factorial(n - 1)
            """),

        Error("ModuleNotFoundError",
            "The module imported here is not installed for the Python that ran the program, or its name is misspelt.",
            "The program stops on its first lines, before anything else runs.",
            "Check the spelling, or install the package with pip for the same Python.",
            """
            import requests
            """),

        Error("ImportError",
            "The module was found, but it has no name like the one this line imports from it.",
            "The program stops on its first lines, before anything else runs.",
            "Check the spelling and capitals of the imported name, and that the module really defines it.",
            """
            from math import sqrt
            """),

        Error("FileNotFoundError",
            "The file named here does not exist where Python looked - relative paths are looked up from the folder the program runs in.",
            "The program crashes as soon as it tries to open the file.",
            "Check the name, and build the path from the program's own folder so it works wherever the program is run from.",
            """
            from pathlib import Path

            data = Path(__file__).parent / "scores.txt"
            with open(data) as file:
                lines = file.readlines()
            """),

        Error("RuntimeError",
            "A dictionary or set is changed - items added or removed - while a for loop is still walking over it.",
            "Python stops the loop with an error, because it can no longer tell which items it has seen.",
            "Loop over a copy - list(d.keys()) - or build a new collection instead of changing the one being looped over.",
            """
            for key in list(stock.keys()):
                if stock[key] == 0:
                    del stock[key]
            """,
            pattern: @"changed size during iteration"),

        Error("EOFError",
            "input() was called, but there was nothing left to read.",
            "The program stops at the question it asked.",
            "Give the program its input - in FixFinder, type the answers into the input box, one per line.",
            """
            name = input("Name: ")
            """),

        Error("AssertionError",
            "An assert statement found its condition false.",
            "The program stops here, because something the code relies on is not true.",
            "Find out why the condition is false - the assert is doing its job - and fix the value that broke it.",
            """
            assert len(items) > 0, "there should be at least one item"
            """),

        Error("StopIteration",
            "next() was called on an iterator that had nothing left.",
            "The program crashes, or inside a generator the loop ends early without saying why.",
            "Pass a default to next(iterator, None), or loop with for instead.",
            """
            first = next(iter(items), None)
            """),
    ];
}
