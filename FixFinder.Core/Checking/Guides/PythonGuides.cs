using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Python's own errors and warnings, each explained three ways: for someone new to programming, as it is usually taught,
/// and in the language's own terms. The three say the same thing about the program; only how much is spelled out differs.
/// </summary>
internal static class PythonGuides
{
    private static GuideEntry Syntax(string pattern, string beginner, string explanation, string technical, string why, string fix, string example) => new()
    {
        ExceptionTypes = ["SyntaxError", "IndentationError", "TabError"],
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example) { ForBeginners = beginner, ForTechnical = technical },
    };

    private static GuideEntry Error(
        string type, string beginner, string explanation, string technical, string why, string fix, string example, string? pattern = null) => new()
    {
        ExceptionTypes = [type],
        Message = pattern is null ? null : new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example) { ForBeginners = beginner, ForTechnical = technical },
    };

    private const string NothingRuns =
        "Python reads the whole file before it runs any of it, so while this is wrong not a single line of the program runs.";

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Syntax(@"expected ':'",
            "In Python, a line that starts a group of lines - such as if, for, while or def - has to end with a colon (:). " +
            "The colon tells Python that the indented lines underneath belong to it, and this line is missing it.",
            "Lines that start a block - if, elif, else, for, while, def, class, try, except, with - must end with a colon.",
            "Every compound statement's header - if, elif, else, for, while, def, class, try, except, finally, with, match, " +
            "case - ends with a colon before its suite. The parser expected the colon at the end of this header and found " +
            "something else.",
            NothingRuns,
            "Put a colon at the end of the line that starts the block.",
            """
            if score > 50:
                print("pass")
            """),

        Syntax(@"was never closed|unexpected EOF|EOF while scanning",
            "Brackets come in pairs: every ( needs a ), every [ a ] and every { a }. One of them was opened and never closed, " +
            "so Python kept reading to the end of the file looking for its partner.",
            "A bracket, brace or parenthesis is opened and never closed, so Python reads on to the end of the file looking for it.",
            "Inside brackets, lines are joined implicitly, so an unclosed (, [ or { makes every line after it part of one " +
            "logical line until the end of the input. Python 3.10 and later report the opening bracket; older versions report " +
            "the end of the file.",
            NothingRuns + " The line Python names is often after the real mistake.",
            "Find the opening bracket and add its closing partner where the expression ends.",
            """
            total = sum(prices)
            print(round(total, 2))
            """),

        Syntax(@"unmatched '[)\]}]'|closing parenthesis .* does not match",
            "Every closing bracket needs an opening bracket of the same kind before it. Here there is a closing bracket with " +
            "no partner, or a bracket that closes the wrong kind - a ] closing a (, for example.",
            "There is a closing bracket with no opening bracket to match it, or it closes the wrong kind of bracket.",
            "The tokenizer keeps a stack of open brackets, and each closing bracket must match the kind on top of it. Here " +
            "the stack was empty, or its top was a different kind of bracket.",
            NothingRuns,
            "Remove the extra closing bracket, or change it to the kind that was opened.",
            """
            values = [1, 2, 3]
            print(len(values))
            """),

        Syntax(@"unterminated (?:triple-quoted )?string|EOL while scanning string",
            "Text in Python goes between quotes, and the quote at the start needs a matching quote at the end. This text " +
            "starts with a quote, but the line ends before the closing one.",
            "A string starts with a quote and the line ends before the matching quote.",
            "A string literal in single or double quotes cannot run past the end of its line unless the newline is escaped " +
            "with a backslash, and a triple-quoted one cannot run past the end of the file. The tokenizer reached that end " +
            "without the closing delimiter.",
            NothingRuns,
            "Close the string with the same kind of quote it was opened with.",
            """
            name = "Alice"
            print("Hello, " + name)
            """),

        Syntax(@"expected an indented block",
            "A line ending in a colon - like if, for or def - starts a block, and Python needs at least one line underneath " +
            "it that is pushed further in, to know what belongs to it. Nothing indented follows this one.",
            "A line ending in a colon must be followed by at least one indented line - the block it starts.",
            "A compound statement's suite is either statements on the same line after the colon, or a newline and an " +
            "indented block of at least one statement. The next line was not indented further, so there is no suite.",
            NothingRuns,
            "Indent the lines that belong to the block, or write pass if the block is meant to be empty for now.",
            """
            def greet(name):
                print("Hello", name)
            """),

        Syntax(@"unexpected indent",
            "Python uses the spaces at the start of a line to tell which lines belong together. This line is pushed in " +
            "further than the one above it, but the line above did not start a new block - it does not end with a colon.",
            "This line is indented further than the line before it, but nothing above it opened a new block.",
            "Indentation is part of the grammar: the tokenizer produces an INDENT where the indentation grows, and an INDENT " +
            "is only allowed where a block begins, after a colon. Here the indentation grows where no block begins.",
            NothingRuns,
            "Line the statement up with the lines around it.",
            """
            total = 0
            total = total + 5
            """),

        Syntax(@"unindent does not match|inconsistent use of tabs",
            "When a block ends, the next line has to line up exactly with a line above it. This line's spaces do not line up " +
            "with any of them - often because some lines use tabs and others spaces, which look the same but are not.",
            "The indentation of this line does not line up with any block above it, often because tabs and spaces are mixed.",
            "When indentation shrinks, it must return exactly to one of the levels on the tokenizer's indentation stack; this " +
            "line matches none of them. Python 3 raises TabError when mixed tabs and spaces make the meaning depend on how " +
            "wide a tab is.",
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
            "In Python 3, print is a function, so what it prints must go in brackets.",
            "Python 3 replaced the print statement with a built-in function (PEP 3105), so print followed by a value is not a " +
            "call. The parser recognises the old form and says so.",
            NothingRuns,
            "Put brackets around what print should print.",
            """
            print("Hello, world")
            """),

        Syntax(@"Maybe you meant '==' or ':='|cannot assign to",
            "One = stores a value in a variable; two == compare two values. An if or a while needs a comparison, so it needs " +
            "==. The same message appears when the left side of = is something that cannot store a value, like a number or " +
            "a function call.",
            "A single = is used where a comparison is needed, or something is assigned to that cannot hold a value.",
            "An assignment is a statement, not an expression, so it cannot stand in a condition - only the walrus operator := " +
            "can. And an assignment's target must be a name, an attribute, a subscription, a slice or an unpacking of those, " +
            "never a literal, a call or an operator expression.",
            NothingRuns,
            "Use == to compare two values; keep = for storing a value in a variable.",
            """
            if answer == 42:
                print("correct")
            """),

        Syntax(@"invalid character|invalid non-printable character",
            "The line has a character that looks like ordinary punctuation but is not - usually a curly quote or a long dash " +
            "copied from a word processor or a web page. Python only understands the plain keyboard versions.",
            "The line contains a character Python does not allow in code, often a curly quote or a dash pasted from a document.",
            "Python accepts non-ASCII letters in names (PEP 3131), strings and comments, but its operators and delimiters are " +
            "all ASCII. A character such as U+201C or U+2013 outside a string or comment cannot begin any token.",
            NothingRuns,
            "Retype the character: straight quotes ' or \", and a plain minus sign -.",
            """
            message = "It's done"
            """),

        Syntax(@"'return' outside function|'break' outside loop|'continue' (?:not properly in|outside) loop|'yield' outside function|'await' outside",
            "return only makes sense inside a function, and break and continue only inside a loop - they end the function, or " +
            "jump within the loop. This one is not inside any function or loop, often because its indentation moved it out.",
            "The statement is only allowed inside a function or a loop, and this one is outside every function or loop.",
            "The compiler checks this before anything runs: return and yield need an enclosing function, await an enclosing " +
            "async function, and break and continue an enclosing loop in the same function - a loop in the function around a " +
            "nested def does not count.",
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
            "Python could not read this line as any statement it knows. The usual causes are a missing operator or comma, a name " +
            "that starts with a digit, or a word from another language.",
            "No rule of the grammar matches the tokens here. 'invalid decimal literal' means a number runs straight into " +
            "letters, as in a name that starts with a digit; 'leading zeros' means a decimal number such as 07, which Python 3 " +
            "does not allow - octal is written 0o7.",
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
            "Something inside the braces of an f-string is not valid, often an unbalanced brace or the same quote used inside and outside.",
            "Each replacement field is parsed as an expression. Before Python 3.12 an expression in the braces could not use " +
            "the string's own quote character or a backslash; from 3.12 (PEP 701) it can, so there the cause is an unbalanced " +
            "brace or an expression that is not valid.",
            NothingRuns,
            "Balance every { with a }, and use the other kind of quote for strings inside the braces.",
            """
            print(f"{name}'s total is {prices['apple']}")
            """),

        Error("SyntaxWarning",
            "`is` asks whether two names point at the very same thing in memory; == asks whether two values are equal. Two " +
            "equal numbers or strings can be separate things in memory, so `is` can say no where == would say yes.",
            "`is` checks whether two things are the very same object, not whether they are equal.",
            "`is` compares identity, as id() would, while == calls __eq__. CPython happens to reuse small integers (-5 to 256) " +
            "and some strings, so `x is 5` can be true by accident; since Python 3.8 the compiler warns when `is` or `is not` " +
            "compares with a literal.",
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
            "The brackets turn the assert into a check of a tuple, and a tuple with anything in it always counts as true.",
            "The statement is assert expression, message. With both in brackets the expression is a two-item tuple, which is " +
            "always truthy, so the assert does nothing; the compiler warns that the assertion is always true.",
            "The assert can never fail, so it never catches the mistake it was written to catch.",
            "Remove the brackets: assert condition, message.",
            """
            assert total >= 0, "total should never be negative"
            """,
            pattern: @"assertion is always true"),

        Error("SyntaxWarning",
            "In ordinary text a backslash has a special job: \\n means a new line and \\t a tab. When a backslash is followed " +
            "by a letter with no special meaning, such as \\d, Python warns, because a later version will treat it as an error.",
            "A backslash inside a normal string starts an escape sequence, and this one (such as \\d) is not a real escape.",
            "An unrecognised escape in a string literal that is not raw is left in the string as written, but it has been " +
            "deprecated since Python 3.6 and gives a SyntaxWarning from 3.12. In a raw string, r\"...\", a backslash is just a " +
            "character, which is what a regular expression needs.",
            "Python keeps the backslash for now but warns that it will become an error; in a regular expression the meaning can change.",
            "Write the string as a raw string with an r in front, or double the backslash.",
            """
            pattern = r"\d+"
            """,
            pattern: @"invalid escape sequence"),

        Error("SyntaxWarning",
            "Items in a list or tuple need commas between them. Without the comma, Python reads (1, 2) (3, 4) as 'call (1, 2) " +
            "with (3, 4)' - as if the first item were a function - and that fails when the line runs.",
            "Two things are written side by side with nothing between them, so Python tries to call the first one with the second as its argument.",
            "An expression followed by a bracketed one is a call. Since Python 3.8 the compiler warns when what is called is a " +
            "literal that can never be called, such as a tuple; when the line runs, the call raises TypeError.",
            "When the line runs it crashes with a TypeError.",
            "Add the missing comma between the items.",
            """
            points = [(1, 2), (3, 4)]
            """,
            pattern: @"is not callable; perhaps you missed a comma"),

        Error("SyntaxWarning",
            "The code in finally always runs as the try ends - even when it ended with an error. A return, break or continue " +
            "in finally ends things right there, so an error the try raised is thrown away without anyone ever seeing it.",
            "A return, break or continue in a finally block overrides whatever the try block was doing - including an error it raised.",
            "Leaving a finally clause with return, break or continue discards any exception that was propagating and " +
            "overrides a return value from the try. PEP 765 makes the compiler warn about it from Python 3.14.",
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
            "The line uses a name - a variable, a function or a module - that Python does not know at this point. It was never " +
            "defined, is misspelt, is used before the line that defines it, or needs an import.",
            "When the line runs, the name is looked up in the local, enclosing, global and built-in scopes in turn, and is " +
            "bound in none of them, so the lookup raises NameError.",
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
            "The function assigns to this name somewhere, which makes it local to the function, but this line reads it before any " +
            "value has been assigned.",
            "Scope is decided when the function is compiled: any binding of a name in its body makes the name local " +
            "throughout, unless it is declared global or nonlocal. A read before the first binding runs raises " +
            "UnboundLocalError, a subclass of NameError.",
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
            "Text and a number are joined with +, and Python will not guess whether you meant arithmetic or text.",
            "str's + only accepts another str, and int's returns NotImplemented for a str, so neither side can handle the " +
            "operation. Python never converts between text and numbers on its own.",
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
            "The values on each side of the operator are of types it cannot combine, such as a number and a list, or a value and None.",
            "For a binary operator Python tries the left operand's method, such as __add__ or __lt__, and then the right " +
            "operand's reflected one. Both returned NotImplemented, or neither exists, so the operation raises TypeError.",
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
            "Something is called with brackets as if it were a function, but it is a value such as a number, a string or a list.",
            "A call needs the object's type to define __call__, and this value's type - int, str or list, say - does not. " +
            "Assigning to a built-in's name, such as sum or list, hides the built-in in that scope.",
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
            "The function is called with a different number of arguments than it was written to take.",
            "Arguments are bound to parameters by position and then by keyword, and defaults fill the rest. Binding fails when " +
            "a parameter with no default is left over, when there are more positional arguments than parameters and no " +
            "*args, or when a keyword names no parameter and there is no **kwargs. A method's self counts as its first argument.",
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
            "Square brackets are used on a value that cannot be indexed, such as a number, None or a function.",
            "value[key] calls __getitem__ on the value's type - or, for a class, __class_getitem__ - and this type, such as " +
            "int, NoneType or function, defines neither.",
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
            "The code loops over, or unpacks, a value that is not a collection - often a number or None.",
            "A for loop and an unpacking both call iter() on the value, which needs __iter__ or the older sequence protocol of " +
            "__getitem__; int and NoneType have neither.",
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
            "A list, dictionary or set is used as a dictionary key or put in a set, and only values that never change can be.",
            "Dictionary keys and set members are found by their hash, so they must be hashable. list, dict and set are mutable " +
            "and compare by content, so they set __hash__ to None; a tuple or frozenset is hashable when all its items are.",
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
            "A value of the wrong type is passed to an operation that needs a particular type.",
            "The function or operator checked its argument's type and raised TypeError. Python converts on its own only " +
            "between numbers - int to float to complex - so any other conversion has to be written out.",
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
            "The value does not have the attribute or method the line asks for. The name may be misspelt, belong to another type, " +
            "or the value may be None because something earlier returned nothing.",
            "Attribute lookup tries a data descriptor found on the type, then the instance's __dict__, then anything else " +
            "found on the type or its bases, then __getattr__ if one is defined, and none of them supplied the name. A " +
            "function with no return statement returns None, which is where a NoneType here usually comes from.",
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
            "The index is past the end of the list or string. Indexes start at 0, so the last item of a list of n items is at n - 1.",
            "A sequence accepts indexes from -len(s) to len(s) - 1, the negative ones counting from the end, and raises " +
            "IndexError for any other. A slice, unlike an index, is clipped to the bounds and never raises.",
            "The program crashes as soon as the index goes out of range - often on the last pass of a loop.",
            "Loop over the items directly, use range(len(items)), or check the index is less than len(items) first.",
            """
            for i in range(len(items)):
                print(items[i])
            """),

        Error("KeyError",
            "A dictionary finds each value by its key, like a word in a glossary. This key is not in the dictionary: it may be " +
            "spelt differently, in different capitals, of a different kind - the text \"1\" is not the number 1 - or not added yet.",
            "The dictionary has no entry with this key. The key may be misspelt, differ in case or type, or not have been added yet.",
            "d[key] looks for a key equal to this one with the same hash, and when there is none and the dictionary's type " +
            "defines no __missing__ - as defaultdict and Counter do - it raises KeyError. \"1\" and 1 are different keys, " +
            "while 1 and 1.0 are the same key.",
            "The line crashes whenever the key is missing.",
            "Use dictionary.get(key, default), check with `if key in dictionary`, or add the key first.",
            """
            price = prices.get("apple", 0)
            """),

        Error("ValueError",
            "The value is the right kind of thing but has the wrong content: int() can turn \"42\" into a number, but not " +
            "\"forty-two\" or \"3.5\". This usually happens when someone types something the program did not expect.",
            "The value has the right type but a content the operation cannot use - such as int() given text that is not a whole number.",
            "ValueError means an argument of the right type with a value the operation cannot accept - int() given text that " +
            "is not an integer in that base, float() given text that is not a number, or an unpacking with the wrong number " +
            "of items.",
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
            "The code divides by a value that is zero at this point.",
            "/, //, % and divmod() raise ZeroDivisionError for a zero divisor, integer or float alike: Python does not return " +
            "infinity or NaN for a float divided by zero the way IEEE 754 arithmetic would.",
            "The program crashes whenever the divisor is zero - often when a list is empty.",
            "Check that the divisor is not zero before dividing, and decide what the answer should be when it is.",
            """
            average = total / count if count > 0 else 0
            """),

        Error("RecursionError",
            "A function that calls itself needs a stopping point - a case where it returns without calling itself again. " +
            "Without one, or when the calls never get closer to it, it keeps calling itself until Python runs out of room " +
            "and stops it.",
            "The function calls itself again and again without ever reaching a case that stops it.",
            "CPython limits how deep Python calls can go - sys.getrecursionlimit(), 1000 unless changed - and exceeding it " +
            "raises RecursionError, a subclass of RuntimeError. Python does not optimise tail calls, so tail recursion counts too.",
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
            "The module imported here is not installed for the Python that ran the program, or its name is misspelt.",
            "The import system searched every directory on sys.path, through the finders on sys.meta_path, and found no " +
            "module or package by that name - it may be installed for a different interpreter or environment than the one " +
            "running the program. ModuleNotFoundError is a subclass of ImportError.",
            "The program stops on its first lines, before anything else runs.",
            "Check the spelling, or install the package with pip for the same Python.",
            """
            import requests
            """),

        Error("ImportError",
            "from module import name takes one thing out of a module. Python found the module, but there is nothing in it " +
            "called that - check the spelling and the capitals.",
            "The module was found, but it has no name like the one this line imports from it.",
            "from m import x imports m, then looks x up as an attribute of m - and, for a package, tries to import the " +
            "submodule m.x - and neither exists. Two modules importing each other can also leave m only partly run when x " +
            "is looked up.",
            "The program stops on its first lines, before anything else runs.",
            "Check the spelling and capitals of the imported name, and that the module really defines it.",
            """
            from math import sqrt
            """),

        Error("FileNotFoundError",
            "The program asked for a file that is not where it looked. A name like scores.txt is looked for in the folder the " +
            "program was started from, which may not be the folder the program's own file is in.",
            "The file named here does not exist where Python looked - relative paths are looked up from the folder the program runs in.",
            "open() resolves a relative path against the process's current working directory, not the script's directory. " +
            "The operating system reported that the path does not exist, which Python raises as FileNotFoundError, a " +
            "subclass of OSError.",
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
            "A dictionary or set is changed - items added or removed - while a for loop is still walking over it.",
            "A dict or set iterator checks on every step that the collection still has the size it had when the loop began, " +
            "and raises RuntimeError when it does not. Loop over a snapshot such as list(d), or build a new collection with " +
            "a comprehension.",
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
            "input() was called, but there was nothing left to read.",
            "input() reads a line from sys.stdin and raises EOFError when it reaches the end of the input before reading " +
            "anything, as when standard input is empty, closed, or a file that has been read to the end.",
            "The program stops at the question it asked.",
            "Give the program its input - in FixFinder, type the answers into the input box, one per line.",
            """
            name = input("Name: ")
            """),

        Error("AssertionError",
            "assert is a check the programmer wrote down: 'this must be true here'. It was not true, so Python stopped the " +
            "program at the check, before the wrong value could cause more trouble later on.",
            "An assert statement found its condition false.",
            "assert condition, message behaves like `if __debug__ and not condition: raise AssertionError(message)`. Running " +
            "Python with -O makes __debug__ false and removes every assert, so an assert must never be the only check on input.",
            "The program stops here, because something the code relies on is not true.",
            "Find out why the condition is false - the assert is doing its job - and fix the value that broke it.",
            """
            assert len(items) > 0, "there should be at least one item"
            """),

        Error("StopIteration",
            "next() takes the next item from something that hands out items one at a time. There were no items left, so " +
            "there was nothing to take. A for loop handles this for you by simply stopping.",
            "next() was called on an iterator that had nothing left.",
            "An exhausted iterator's __next__ raises StopIteration, which a for loop takes as the end. next() passes it on " +
            "unless given a default, and since PEP 479 (Python 3.7) a StopIteration that escapes a generator's body becomes " +
            "a RuntimeError.",
            "The program crashes, or inside a generator the loop ends early without saying why.",
            "Pass a default to next(iterator, None), or loop with for instead.",
            """
            first = next(iter(items), None)
            """),
    ];
}
