using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Python's logic patterns, each explained for someone new to programming.
/// </summary>
internal static class PythonPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-python-or-constant"], "A condition that is always true",
            "In English 'if the answer is yes or y' makes sense, but Python reads answer == \"yes\" or \"y\" as two separate " +
            "questions: 'is the answer yes?', or '\"y\"?'. The second part is just a piece of text, and any text that is not " +
            "empty counts as true - so the whole condition is always true.",
            "The if always runs its block, whatever was typed or calculated, so the other branches never run.",
            "Compare x with each value, or check membership: x in (a, b).",
            """
            if answer in ("yes", "y"):
                print("ok")
            """),

        GuideFor(["logic-python-none-returned-assigned"], "A list method's result assigned",
            "Methods like sort() and append() change the list itself and give back nothing - None. Storing what they give " +
            "back stores None, so the variable loses the list.",
            "The variable ends up holding None, and the next line that uses it fails or prints None.",
            "Call the method on its own line, or use sorted() to get a new sorted list.",
            """
            names.sort()
            ordered = sorted(names)
            """),

        GuideFor(["logic-python-missing-f-prefix"], "Braces in a string with no f",
            "Curly braces are only filled in with values in an f-string - a string with an f in front of its opening quote. " +
            "Without the f, Python prints the braces and the name inside them exactly as they were typed.",
            "The output shows {name} instead of the value, which looks broken to anyone using the program.",
            "Put an f in front of the opening quote.",
            """
            print(f"Hello {name}, you scored {score}")
            """),

        GuideFor(["logic-python-method-not-called"], "A method used without calling it",
            "The round brackets after a method's name are what make it run. Without them, name.upper is the method itself - " +
            "like naming a recipe instead of cooking it - so you get the method, not its answer.",
            "In a condition the method always counts as true; stored or printed, it shows <built-in method ...> instead of a value.",
            "Add the brackets that call it.",
            """
            if answer.isdigit():
                number = int(answer)
            """),

        GuideFor(["logic-python-modified-while-looping"], "A list changed while looping over it",
            "A for loop goes through a list by counting positions: item 0, then 1, then 2. Removing an item moves everything " +
            "after it back one place, so the loop skips the item that moved into the gap - and adding items makes the list " +
            "longer while it is being walked through.",
            "Some items are never checked, or the loop never finishes.",
            "Loop over a copy with list(items), or build a new list of the items to keep.",
            """
            positives = [n for n in numbers if n >= 0]
            """),

        GuideFor(["logic-python-input-used-as-number"], "Typed input used as a number",
            "Whatever someone types, input() gives it back as text - \"18\", not 18. Text and numbers do not mix: comparing " +
            "them fails, and * with text repeats it instead of multiplying. Turn the text into a number first with int() or " +
            "float().",
            "Comparing it with a number or doing arithmetic with it crashes with a TypeError - and * repeats the text instead of multiplying.",
            "Convert it as it is read: int(input(...)) or float(input(...)).",
            """
            age = int(input("Age: "))
            if age >= 18:
                print("adult")
            """),

        GuideFor(["logic-python-print-instead-of-return"], "A function that prints instead of returning",
            "print shows a value on the screen; return hands a value back to the code that called the function. This function " +
            "only prints, so the code that calls it gets nothing back - Python gives it None.",
            "Any code that uses the function's result gets None, and crashes or prints None.",
            "Return the value, and print it where the function is called.",
            """
            def area(width, height):
                return width * height

            print(area(2, 3))
            """),

        GuideFor(["logic-python-return-print"], "Returning what print gives back",
            "print shows a value and then gives back nothing - None. So return print(x) shows x on the screen, but hands None " +
            "back to whoever called the function.",
            "The caller gets None instead of the value.",
            "Print and return as two statements.",
            """
            print(total)
            return total
            """),

        GuideFor(["logic-python-statement-has-no-effect"], "A calculation that is thrown away",
            "count + 1 works out a new number, but a line on its own does not save it anywhere, so count stays as it was. To " +
            "change count, store the result: count += 1.",
            "The variable never changes, so a counter stays at zero or a total never grows.",
            "Use an augmented assignment: count += 1.",
            """
            count += 1
            """),

        GuideFor(["logic-python-shadowed-builtin"], "A variable named after a built-in function",
            "Python has functions ready to use, like sum, list and max. Naming your own variable sum makes that name mean your " +
            "value instead, so the next time you try to use the sum function, Python finds your number and cannot run it.",
            "Any later call to that function fails with 'object is not callable'.",
            "Give the variable a name of its own, such as total, items or largest.",
            """
            total = 0
            for n in numbers:
                total += n
            print(sum(numbers))
            """),

        GuideFor(["logic-python-unreachable-code"], "Code that can never run",
            "return, break, continue and raise all leave the current block straight away. A line written after one of them in " +
            "the same block can never be reached, so it never runs.",
            "Whatever it was meant to do never happens, and nothing warns you.",
            "Move the line above the return, or remove it.",
            """
            def total(items):
                print("adding up", len(items), "items")
                return sum(items)
            """),

        GuideFor(["logic-python-duplicate-condition"], "A branch that can never be reached",
            "An if/elif chain checks its conditions from the top, runs the first one that is true, and skips the rest. This " +
            "elif checks exactly what a branch above it checks, so whenever it would be true, the earlier branch has already run.",
            "When the condition is true the earlier branch runs, so the later one never does.",
            "Change the later condition to the case it was meant to catch.",
            """
            if score >= 70:
                grade = "A"
            elif score >= 50:
                grade = "B"
            """),

        GuideFor(["logic-python-endless-while-true"], "A while True loop with no way out",
            "while True: means 'repeat for ever' unless something inside the loop stops it - a break, a return, an error being " +
            "raised, or exiting the program. This loop has none of those, so it never stops.",
            "The program never gets past the loop, and anything after it never runs.",
            "Add a condition that breaks out of the loop.",
            """
            while True:
                choice = input("Choice (q to quit): ")
                if choice == "q":
                    break
            """),

        GuideFor(["logic-python-range-skips-last"], "A loop that stops one item early",
            "range(n) counts 0, 1, 2 and so on up to n - 1: it already stops one short of n. So range(len(items)) covers every " +
            "position, and range(len(items) - 1) stops one too early and misses the last item.",
            "The last item is never counted, printed or checked.",
            "Use range(len(items)), or loop over the items directly.",
            """
            for i in range(len(items)):
                print(items[i])
            """),

        GuideFor(["logic-python-bare-except"], "An except that catches everything",
            "except: on its own catches every kind of error - even mistakes in your own code, and even pressing Ctrl+C to stop " +
            "the program. That hides problems you need to see.",
            "Real bugs are hidden, and the program can be impossible to stop.",
            "Name the error you expect, such as except ValueError:, or at least use except Exception:.",
            """
            try:
                age = int(text)
            except ValueError:
                print("Please type a whole number.")
            """),

        GuideFor(["logic-python-silent-except"], "An error caught and ignored",
            "When an error is caught and all that is done with it is pass, the error just disappears. The program carries on " +
            "as if nothing happened, usually with a wrong or missing value, and nobody knows why.",
            "When something goes wrong the program carries on with wrong values, and nothing says why.",
            "Handle the error: tell the user, use a sensible default, or let it be raised.",
            """
            try:
                age = int(text)
            except ValueError as error:
                print("Not a number:", error)
                age = 0
            """),

        GuideFor(["logic-python-shared-class-list"], "A list shared by every object",
            "Variables written straight into a class body belong to the class itself, not to each object made from it. A list " +
            "written there is one list shared by every object, so adding to it through one object adds for all of them.",
            "Adding to one object's list adds to all of them.",
            "Create the list in __init__ with self.name = [].",
            """
            class Basket:
                def __init__(self):
                    self.items = []
            """),

        GuideFor(["logic-python-none-comparison"], "None compared with ==",
            "None means 'no value', and there is only ever one None in a program. `is None` asks 'is this that exact None?', " +
            "which cannot be fooled; == asks the object to compare itself, which a class can change.",
            "It works in most programs, but `is None` is clearer and cannot be fooled.",
            "Use is None and is not None.",
            """
            if value is None:
                value = 0
            """),

        GuideFor(["logic-python-bool-comparison"], "Comparing with True or False",
            "A condition like done is already true or false, so writing done == True asks the same question twice. It can " +
            "also go wrong: some values count as true without being exactly True, like a list with something in it.",
            "It is longer to read, and == True fails for values that count as true without being exactly True.",
            "Use the value itself, or not value.",
            """
            if done:
                print("finished")
            """),

        GuideFor(["logic-python-type-comparison"], "Checking a type with type() ==",
            "type(x) == int asks 'is x exactly an int?', which fails for values built on int - True is one of them. " +
            "isinstance(x, int) asks 'is x an int, or anything built on one?', which is usually what is meant.",
            "The check can reject values that should pass.",
            "Use isinstance(x, int).",
            """
            if isinstance(value, int):
                total += value
            """),

        GuideFor(["logic-python-range-len-loop"], "Looping over positions to read items",
            "for i in range(len(items)) counts positions and then looks each item up by its position. When you only need the " +
            "items, for item in items gives you each one directly, with no counting to get wrong.",
            "The index adds a place to make an off-by-one mistake and makes the loop harder to read.",
            "Loop over the items: for item in items.",
            """
            for name in names:
                print(name)
            """),

        GuideFor(["logic-python-file-not-closed"], "A file opened and never closed",
            "Opening a file is like borrowing it: you have to hand it back by closing it. Until then, what you wrote may still " +
            "be waiting in memory rather than saved. A with block closes the file for you, even if something goes wrong.",
            "Written data can be lost, and other programs cannot use the file meanwhile.",
            "Open it with a with block, which closes it for you.",
            """
            with open("scores.txt", "w") as file:
                file.write("10\n")
            """),

        GuideFor(["logic-python-returns-nothing-sometimes"], "A function that sometimes returns nothing",
            "A function gives back whatever its return statement says. On one way through this function there is no return, " +
            "and then Python gives back None - so the caller sometimes gets a value and sometimes nothing.",
            "Code that uses the result works for some inputs and crashes, or prints None, for others.",
            "Make every path end in a return - often a final else, or a return after the loop.",
            """
            def grade(score):
                if score >= 50:
                    return "pass"
                return "fail"
            """),

        GuideFor(["logic-python-floor-division-average"], "An average worked out with //",
            "Python has two kinds of division: / gives the exact answer, 3.5, and // rounds down to a whole number, 3. An " +
            "average needs the exact answer, so // makes it too low.",
            "Averages come out too low - the average of 3 and 4 becomes 3.",
            "Use / for an exact division.",
            """
            average = sum(scores) / len(scores)
            """),

        GuideFor(["logic-python-attribute-not-set"], "A method that changes a local instead of the object",
            "Inside a method, a plain name like count is a new variable just for that call of the method. To change the " +
            "object's own value you have to write self.count - otherwise the object never changes.",
            "The object's value never changes, so the next method call starts from the old value.",
            "Assign to self.name.",
            """
            def add(self):
                self.count = self.count + 1
            """),
    ];
}
