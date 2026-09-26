using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Python's logic patterns, each explained three ways: for someone new to programming, as it is usually taught, and in
/// the language's own terms.
/// </summary>
internal static class PythonPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        AtEveryLevel(["logic-python-or-constant"], "A condition that is always true",
            "In English 'if the answer is yes or y' makes sense, but Python reads answer == \"yes\" or \"y\" as two separate " +
            "questions: 'is the answer yes?', or '\"y\"?'. The second part is just a piece of text, and any text that is not " +
            "empty counts as true - so the whole condition is always true.",
            "`x == a or b` compares x only with a; `b` on its own is a value, and any non-empty value counts as true.",
            "== binds more tightly than or, so x == a or b parses as (x == a) or b; or returns its first truthy operand, and a " +
            "non-empty literal is always truthy, so the condition can never fail. Membership, x in (a, b), tests each value.",
            "The if always runs its block, whatever was typed or calculated, so the other branches never run.",
            "Compare x with each value, or check membership: x in (a, b).",
            """
            if answer in ("yes", "y"):
                print("ok")
            """),

        AtEveryLevel(["logic-python-none-returned-assigned"], "A list method's result assigned",
            "Methods like sort() and append() change the list itself and give back nothing - None. Storing what they give " +
            "back stores None, so the variable loses the list.",
            "sort, append, remove and the other methods that change a list where it is return None, not the list.",
            "Methods that mutate in place - list.sort, append, extend, insert, remove, reverse, dict.update - return None by " +
            "convention, so assigning the call's result binds None; sorted() and slicing return new objects instead.",
            "The variable ends up holding None, and the next line that uses it fails or prints None.",
            "Call the method on its own line, or use sorted() to get a new sorted list.",
            """
            names.sort()
            ordered = sorted(names)
            """),

        AtEveryLevel(["logic-python-missing-f-prefix"], "Braces in a string with no f",
            "Curly braces are only filled in with values in an f-string - a string with an f in front of its opening quote. " +
            "Without the f, Python prints the braces and the name inside them exactly as they were typed.",
            "Only an f-string fills in {name} with the value of name; without the f the braces are printed as they are.",
            "Replacement fields are evaluated only in formatted string literals, f\"...\", or by str.format; in an ordinary " +
            "literal {name} is just text, so the value is never put in.",
            "The output shows {name} instead of the value, which looks broken to anyone using the program.",
            "Put an f in front of the opening quote.",
            """
            print(f"Hello {name}, you scored {score}")
            """),

        AtEveryLevel(["logic-python-method-not-called"], "A method used without calling it",
            "The round brackets after a method's name are what make it run. Without them, name.upper is the method itself - " +
            "like naming a recipe instead of cooking it - so you get the method, not its answer.",
            "Without brackets, name.upper is the method itself rather than its result.",
            "Attribute access without a call gives a bound method object: it is always truthy, is never equal to a string or " +
            "a number, and prints as <built-in method ...>, so the call's result is never worked out.",
            "In a condition the method always counts as true; stored or printed, it shows <built-in method ...> instead of a value.",
            "Add the brackets that call it.",
            """
            if answer.isdigit():
                number = int(answer)
            """),

        AtEveryLevel(["logic-python-modified-while-looping"], "A list changed while looping over it",
            "A for loop goes through a list by counting positions: item 0, then 1, then 2. Removing an item moves everything " +
            "after it back one place, so the loop skips the item that moved into the gap - and adding items makes the list " +
            "longer while it is being walked through.",
            "The for loop walks through the list by position, so removing an item makes it skip the next one, and adding items makes it keep going.",
            "A list iterator keeps an index and reads the item at it on each step, checking only against the current length. " +
            "Removing an item shifts the later ones left, so the next is skipped, and appending extends the loop; unlike a " +
            "dict or a set, a list raises no error.",
            "Some items are never checked, or the loop never finishes.",
            "Loop over a copy with list(items), or build a new list of the items to keep.",
            """
            positives = [n for n in numbers if n >= 0]
            """),

        AtEveryLevel(["logic-python-input-used-as-number"], "Typed input used as a number",
            "Whatever someone types, input() gives it back as text - \"18\", not 18. Text and numbers do not mix: comparing " +
            "them fails, and * with text repeats it instead of multiplying. Turn the text into a number first with int() or " +
            "float().",
            "input() always returns text, even when a number is typed.",
            "input() always returns a str. Ordering comparisons between a str and an int raise TypeError, == between them is " +
            "simply False, and str * int repeats the string, so the value must be converted with int() or float().",
            "Comparing it with a number or doing arithmetic with it crashes with a TypeError - and * repeats the text instead of multiplying.",
            "Convert it as it is read: int(input(...)) or float(input(...)).",
            """
            age = int(input("Age: "))
            if age >= 18:
                print("adult")
            """),

        AtEveryLevel(["logic-python-print-instead-of-return"], "A function that prints instead of returning",
            "print shows a value on the screen; return hands a value back to the code that called the function. This function " +
            "only prints, so the code that calls it gets nothing back - Python gives it None.",
            "A function with no return statement returns None, however much it prints.",
            "A function whose body ends without a return statement returns None; print writes to sys.stdout as a side effect " +
            "and does not make its argument the function's result.",
            "Any code that uses the function's result gets None, and crashes or prints None.",
            "Return the value, and print it where the function is called.",
            """
            def area(width, height):
                return width * height

            print(area(2, 3))
            """),

        AtEveryLevel(["logic-python-return-print"], "Returning what print gives back",
            "print shows a value and then gives back nothing - None. So return print(x) shows x on the screen, but hands None " +
            "back to whoever called the function.",
            "print shows its value and returns None, so return print(x) returns None.",
            "print() returns None, so return print(x) evaluates the print for its side effect and returns None to the caller.",
            "The caller gets None instead of the value.",
            "Print and return as two statements.",
            """
            print(total)
            return total
            """),

        AtEveryLevel(["logic-python-statement-has-no-effect"], "A calculation that is thrown away",
            "count + 1 works out a new number, but a line on its own does not save it anywhere, so count stays as it was. To " +
            "change count, store the result: count += 1.",
            "An expression like count + 1 on a line of its own works out a value that nothing keeps.",
            "An expression statement evaluates the expression and discards the value. An int cannot be changed, so count + 1 " +
            "cannot alter count - only an assignment or an augmented assignment rebinds the name.",
            "The variable never changes, so a counter stays at zero or a total never grows.",
            "Use an augmented assignment: count += 1.",
            """
            count += 1
            """),

        AtEveryLevel(["logic-python-shadowed-builtin"], "A variable named after a built-in function",
            "Python has functions ready to use, like sum, list and max. Naming your own variable sum makes that name mean your " +
            "value instead, so the next time you try to use the sum function, Python finds your number and cannot run it.",
            "Naming a variable sum, list, max, input or similar replaces Python's own function of that name.",
            "The built-ins live in the outermost scope, so assigning to the same name in a module or a function shadows the " +
            "built-in there, and a later call finds the value that shadows it, which cannot be called.",
            "Any later call to that function fails with 'object is not callable'.",
            "Give the variable a name of its own, such as total, items or largest.",
            """
            total = 0
            for n in numbers:
                total += n
            print(sum(numbers))
            """),

        AtEveryLevel(["logic-python-unreachable-code"], "Code that can never run",
            "return, break, continue and raise all leave the current block straight away. A line written after one of them in " +
            "the same block can never be reached, so it never runs.",
            "This line comes after a return, break, continue or raise in the same block.",
            "Statements after a return, break, continue or raise in the same block are unreachable: they can never execute, " +
            "and Python gives no warning about them.",
            "Whatever it was meant to do never happens, and nothing warns you.",
            "Move the line above the return, or remove it.",
            """
            def total(items):
                print("adding up", len(items), "items")
                return sum(items)
            """),

        AtEveryLevel(["logic-python-duplicate-condition"], "A branch that can never be reached",
            "An if/elif chain checks its conditions from the top, runs the first one that is true, and skips the rest. This " +
            "elif checks exactly what a branch above it checks, so whenever it would be true, the earlier branch has already run.",
            "An elif checks the same condition as an earlier branch of the same if.",
            "if/elif evaluates its conditions in order and runs only the first true branch, so an elif whose condition is " +
            "identical to an earlier one's can only be reached when that condition is false: its body is dead code.",
            "When the condition is true the earlier branch runs, so the later one never does.",
            "Change the later condition to the case it was meant to catch.",
            """
            if score >= 70:
                grade = "A"
            elif score >= 50:
                grade = "B"
            """),

        AtEveryLevel(["logic-python-endless-while-true"], "A while True loop with no way out",
            "while True: means 'repeat for ever' unless something inside the loop stops it - a break, a return, an error being " +
            "raised, or exiting the program. This loop has none of those, so it never stops.",
            "Nothing in the loop - no break, return, raise or exit - can end it.",
            "The condition is the constant True and the body holds no break, return, raise or call to exit, so the loop can " +
            "only be left by an exception raised inside a function it calls, or by a signal from outside.",
            "The program never gets past the loop, and anything after it never runs.",
            "Add a condition that breaks out of the loop.",
            """
            while True:
                choice = input("Choice (q to quit): ")
                if choice == "q":
                    break
            """),

        AtEveryLevel(["logic-python-range-skips-last"], "A loop that stops one item early",
            "range(n) counts 0, 1, 2 and so on up to n - 1: it already stops one short of n. So range(len(items)) covers every " +
            "position, and range(len(items) - 1) stops one too early and misses the last item.",
            "range(n) already stops before n, so range(len(items) - 1) never reaches the last item.",
            "range(stop) yields 0 to stop - 1, so range(len(s)) already covers every index of s; subtracting one leaves the " +
            "last index out - an off-by-one error.",
            "The last item is never counted, printed or checked.",
            "Use range(len(items)), or loop over the items directly.",
            """
            for i in range(len(items)):
                print(items[i])
            """),

        AtEveryLevel(["logic-python-bare-except"], "An except that catches everything",
            "except: on its own catches every kind of error - even mistakes in your own code, and even pressing Ctrl+C to stop " +
            "the program. That hides problems you need to see.",
            "except: with no error named catches every error, including mistakes in your own code and Ctrl+C.",
            "A bare except catches BaseException: KeyboardInterrupt, SystemExit and GeneratorExit as well as every " +
            "programming error such as NameError. except Exception leaves out the first three, and naming the expected " +
            "exception leaves out the rest.",
            "Real bugs are hidden, and the program can be impossible to stop.",
            "Name the error you expect, such as except ValueError:, or at least use except Exception:.",
            """
            try:
                age = int(text)
            except ValueError:
                print("Please type a whole number.")
            """),

        AtEveryLevel(["logic-python-silent-except"], "An error caught and ignored",
            "When an error is caught and all that is done with it is pass, the error just disappears. The program carries on " +
            "as if nothing happened, usually with a wrong or missing value, and nobody knows why.",
            "An except block that only says pass throws the error away.",
            "An except clause whose body is only pass swallows the exception - its type, message and traceback are discarded - " +
            "and execution carries on after the try statement with whatever state the failed code left behind.",
            "When something goes wrong the program carries on with wrong values, and nothing says why.",
            "Handle the error: tell the user, use a sensible default, or let it be raised.",
            """
            try:
                age = int(text)
            except ValueError as error:
                print("Not a number:", error)
                age = 0
            """),

        AtEveryLevel(["logic-python-shared-class-list"], "A list shared by every object",
            "Variables written straight into a class body belong to the class itself, not to each object made from it. A list " +
            "written there is one list shared by every object, so adding to it through one object adds for all of them.",
            "A list written in the class body belongs to the class, so every object shares that one list.",
            "An assignment in the class body makes a class attribute, evaluated once when the class statement runs. Looking an " +
            "attribute up on an instance falls back to the class, so every instance changes the same list unless __init__ " +
            "binds a new one to self.",
            "Adding to one object's list adds to all of them.",
            "Create the list in __init__ with self.name = [].",
            """
            class Basket:
                def __init__(self):
                    self.items = []
            """),

        AtEveryLevel(["logic-python-none-comparison"], "None compared with ==",
            "None means 'no value', and there is only ever one None in a program. `is None` asks 'is this that exact None?', " +
            "which cannot be fooled; == asks the object to compare itself, which a class can change.",
            "There is only one None, and `is None` asks for exactly that; == can be changed by a class.",
            "None is a singleton, so identity is the exact test; == calls the left operand's __eq__, which a class may " +
            "override to answer True for None. PEP 8 asks for is and is not when comparing with singletons.",
            "It works in most programs, but `is None` is clearer and cannot be fooled.",
            "Use is None and is not None.",
            """
            if value is None:
                value = 0
            """),

        AtEveryLevel(["logic-python-bool-comparison"], "Comparing with True or False",
            "A condition like done is already true or false, so writing done == True asks the same question twice. It can " +
            "also go wrong: some values count as true without being exactly True, like a list with something in it.",
            "A condition is already true or false, so == True adds nothing.",
            "if x: tests truthiness through __bool__ or __len__, while x == True compares with True, which equals only True " +
            "and 1; a non-empty list is truthy but not equal to True. PEP 8 advises against comparing booleans with True or " +
            "False using ==.",
            "It is longer to read, and == True fails for values that count as true without being exactly True.",
            "Use the value itself, or not value.",
            """
            if done:
                print("finished")
            """),

        AtEveryLevel(["logic-python-type-comparison"], "Checking a type with type() ==",
            "type(x) == int asks 'is x exactly an int?', which fails for values built on int - True is one of them. " +
            "isinstance(x, int) asks 'is x an int, or anything built on one?', which is usually what is meant.",
            "type(x) == int is false for anything built on int, such as True.",
            "type(x) returns the exact class, so comparing it ignores inheritance - bool is a subclass of int - while " +
            "isinstance(x, cls) accepts subclasses, and virtual subclasses registered with an ABC.",
            "The check can reject values that should pass.",
            "Use isinstance(x, int).",
            """
            if isinstance(value, int):
                total += value
            """),

        AtEveryLevel(["logic-python-range-len-loop"], "Looping over positions to read items",
            "for i in range(len(items)) counts positions and then looks each item up by its position. When you only need the " +
            "items, for item in items gives you each one directly, with no counting to get wrong.",
            "for i in range(len(items)) followed only by items[i] can loop over the items directly.",
            "Iterating the sequence directly uses its iterator and needs no index; where both the index and the item are " +
            "needed, enumerate(items) yields (index, item) pairs.",
            "The index adds a place to make an off-by-one mistake and makes the loop harder to read.",
            "Loop over the items: for item in items.",
            """
            for name in names:
                print(name)
            """),

        AtEveryLevel(["logic-python-file-not-closed"], "A file opened and never closed",
            "Opening a file is like borrowing it: you have to hand it back by closing it. Until then, what you wrote may still " +
            "be waiting in memory rather than saved. A with block closes the file for you, even if something goes wrong.",
            "The file stays open until the program ends, and what was written may not be saved.",
            "File objects buffer writes, which are flushed when the file is closed. Leaving the closing to garbage collection " +
            "depends on the implementation - CPython closes promptly through reference counting, PyPy may not - while a with " +
            "statement closes the file on leaving the block, even when an exception is raised.",
            "Written data can be lost, and other programs cannot use the file meanwhile.",
            "Open it with a with block, which closes it for you.",
            """
            with open("scores.txt", "w") as file:
                file.write("10\n")
            """),

        AtEveryLevel(["logic-python-returns-nothing-sometimes"], "A function that sometimes returns nothing",
            "A function gives back whatever its return statement says. On one way through this function there is no return, " +
            "and then Python gives back None - so the caller sometimes gets a value and sometimes nothing.",
            "The function returns a value on some paths, but on another it reaches the end and Python returns None.",
            "Some paths end in return with a value, while at least one falls off the end of the function, which returns None - " +
            "so the result may be None. PEP 8 asks for consistency: a return with a value on every path, or an explicit " +
            "return None.",
            "Code that uses the result works for some inputs and crashes, or prints None, for others.",
            "Make every path end in a return - often a final else, or a return after the loop.",
            """
            def grade(score):
                if score >= 50:
                    return "pass"
                return "fail"
            """),

        AtEveryLevel(["logic-python-floor-division-average"], "An average worked out with //",
            "Python has two kinds of division: / gives the exact answer, 3.5, and // rounds down to a whole number, 3. An " +
            "average needs the exact answer, so // makes it too low.",
            "// divides and rounds down, so the fraction of an average is lost.",
            "// is floor division - it rounds toward negative infinity, so -7 // 2 is -4 - while / is true division, which " +
            "returns a float even for two ints in Python 3.",
            "Averages come out too low - the average of 3 and 4 becomes 3.",
            "Use / for an exact division.",
            """
            average = sum(scores) / len(scores)
            """),

        AtEveryLevel(["logic-python-attribute-not-set"], "A method that changes a local instead of the object",
            "Inside a method, a plain name like count is a new variable just for that call of the method. To change the " +
            "object's own value you have to write self.count - otherwise the object never changes.",
            "A bare name inside a method is a local variable, even when the object has an attribute of the same name.",
            "Assigning to a bare name inside a method binds a local variable; an attribute is only reached through the " +
            "instance, so self.count has to be assigned explicitly - Python has no implicit this.",
            "The object's value never changes, so the next method call starts from the old value.",
            "Assign to self.name.",
            """
            def add(self):
                self.count = self.count + 1
            """),
    ];
}
