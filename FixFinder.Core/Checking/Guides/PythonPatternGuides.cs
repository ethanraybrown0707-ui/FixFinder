using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

internal static class PythonPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Pattern(["logic-python-or-constant"], "A condition that is always true",
            "`x == a or b` compares x only with a; `b` on its own is a value, and any non-empty value counts as true.",
            "The if always runs its block, whatever was typed or calculated, so the other branches never run.",
            "Compare x with each value, or check membership: x in (a, b).",
            """
            if answer in ("yes", "y"):
                print("ok")
            """),

        Pattern(["logic-python-none-returned-assigned"], "A list method's result assigned",
            "sort, append, remove and the other methods that change a list where it is return None, not the list.",
            "The variable ends up holding None, and the next line that uses it fails or prints None.",
            "Call the method on its own line, or use sorted() to get a new sorted list.",
            """
            names.sort()
            ordered = sorted(names)
            """),

        Pattern(["logic-python-missing-f-prefix"], "Braces in a string with no f",
            "Only an f-string fills in {name} with the value of name; without the f the braces are printed as they are.",
            "The output shows {name} instead of the value, which looks broken to anyone using the program.",
            "Put an f in front of the opening quote.",
            """
            print(f"Hello {name}, you scored {score}")
            """),

        Pattern(["logic-python-method-not-called"], "A method used without calling it",
            "Without brackets, name.upper is the method itself rather than its result.",
            "In a condition the method always counts as true; stored or printed, it shows <built-in method ...> instead of a value.",
            "Add the brackets that call it.",
            """
            if answer.isdigit():
                number = int(answer)
            """),

        Pattern(["logic-python-modified-while-looping"], "A list changed while looping over it",
            "The for loop walks through the list by position, so removing an item makes it skip the next one, and adding items makes it keep going.",
            "Some items are never checked, or the loop never finishes.",
            "Loop over a copy with list(items), or build a new list of the items to keep.",
            """
            positives = [n for n in numbers if n >= 0]
            """),

        Pattern(["logic-python-input-used-as-number"], "Typed input used as a number",
            "input() always returns text, even when a number is typed.",
            "Comparing it with a number or doing arithmetic with it crashes with a TypeError - and * repeats the text instead of multiplying.",
            "Convert it as it is read: int(input(...)) or float(input(...)).",
            """
            age = int(input("Age: "))
            if age >= 18:
                print("adult")
            """),

        Pattern(["logic-python-print-instead-of-return"], "A function that prints instead of returning",
            "A function with no return statement returns None, however much it prints.",
            "Any code that uses the function's result gets None, and crashes or prints None.",
            "Return the value, and print it where the function is called.",
            """
            def area(width, height):
                return width * height

            print(area(2, 3))
            """),

        Pattern(["logic-python-return-print"], "Returning what print gives back",
            "print shows its value and returns None, so return print(x) returns None.",
            "The caller gets None instead of the value.",
            "Print and return as two statements.",
            """
            print(total)
            return total
            """),

        Pattern(["logic-python-statement-has-no-effect"], "A calculation that is thrown away",
            "An expression like count + 1 on a line of its own works out a value that nothing keeps.",
            "The variable never changes, so a counter stays at zero or a total never grows.",
            "Use an augmented assignment: count += 1.",
            """
            count += 1
            """),

        Pattern(["logic-python-shadowed-builtin"], "A variable named after a built-in function",
            "Naming a variable sum, list, max, input or similar replaces Python's own function of that name.",
            "Any later call to that function fails with 'object is not callable'.",
            "Give the variable a name of its own, such as total, items or largest.",
            """
            total = 0
            for n in numbers:
                total += n
            print(sum(numbers))
            """),

        Pattern(["logic-python-unreachable-code"], "Code that can never run",
            "This line comes after a return, break, continue or raise in the same block.",
            "Whatever it was meant to do never happens, and nothing warns you.",
            "Move the line above the return, or remove it.",
            """
            def total(items):
                print("adding up", len(items), "items")
                return sum(items)
            """),

        Pattern(["logic-python-duplicate-condition"], "A branch that can never be reached",
            "An elif checks the same condition as an earlier branch of the same if.",
            "When the condition is true the earlier branch runs, so the later one never does.",
            "Change the later condition to the case it was meant to catch.",
            """
            if score >= 70:
                grade = "A"
            elif score >= 50:
                grade = "B"
            """),

        Pattern(["logic-python-endless-while-true"], "A while True loop with no way out",
            "Nothing in the loop - no break, return, raise or exit - can end it.",
            "The program never gets past the loop, and anything after it never runs.",
            "Add a condition that breaks out of the loop.",
            """
            while True:
                choice = input("Choice (q to quit): ")
                if choice == "q":
                    break
            """),

        Pattern(["logic-python-range-skips-last"], "A loop that stops one item early",
            "range(n) already stops before n, so range(len(items) - 1) never reaches the last item.",
            "The last item is never counted, printed or checked.",
            "Use range(len(items)), or loop over the items directly.",
            """
            for i in range(len(items)):
                print(items[i])
            """),

        Pattern(["logic-python-bare-except"], "An except that catches everything",
            "except: with no error named catches every error, including mistakes in your own code and Ctrl+C.",
            "Real bugs are hidden, and the program can be impossible to stop.",
            "Name the error you expect, such as except ValueError:, or at least use except Exception:.",
            """
            try:
                age = int(text)
            except ValueError:
                print("Please type a whole number.")
            """),

        Pattern(["logic-python-silent-except"], "An error caught and ignored",
            "An except block that only says pass throws the error away.",
            "When something goes wrong the program carries on with wrong values, and nothing says why.",
            "Handle the error: tell the user, use a sensible default, or let it be raised.",
            """
            try:
                age = int(text)
            except ValueError as error:
                print("Not a number:", error)
                age = 0
            """),

        Pattern(["logic-python-shared-class-list"], "A list shared by every object",
            "A list written in the class body belongs to the class, so every object shares that one list.",
            "Adding to one object's list adds to all of them.",
            "Create the list in __init__ with self.name = [].",
            """
            class Basket:
                def __init__(self):
                    self.items = []
            """),

        Pattern(["logic-python-none-comparison"], "None compared with ==",
            "There is only one None, and `is None` asks for exactly that; == can be changed by a class.",
            "It works in most programs, but `is None` is clearer and cannot be fooled.",
            "Use is None and is not None.",
            """
            if value is None:
                value = 0
            """),

        Pattern(["logic-python-bool-comparison"], "Comparing with True or False",
            "A condition is already true or false, so == True adds nothing.",
            "It is longer to read, and == True fails for values that count as true without being exactly True.",
            "Use the value itself, or not value.",
            """
            if done:
                print("finished")
            """),

        Pattern(["logic-python-type-comparison"], "Checking a type with type() ==",
            "type(x) == int is false for anything built on int, such as True.",
            "The check can reject values that should pass.",
            "Use isinstance(x, int).",
            """
            if isinstance(value, int):
                total += value
            """),

        Pattern(["logic-python-range-len-loop"], "Looping over positions to read items",
            "for i in range(len(items)) followed only by items[i] can loop over the items directly.",
            "The index adds a place to make an off-by-one mistake and makes the loop harder to read.",
            "Loop over the items: for item in items.",
            """
            for name in names:
                print(name)
            """),

        Pattern(["logic-python-file-not-closed"], "A file opened and never closed",
            "The file stays open until the program ends, and what was written may not be saved.",
            "Written data can be lost, and other programs cannot use the file meanwhile.",
            "Open it with a with block, which closes it for you.",
            """
            with open("scores.txt", "w") as file:
                file.write("10\n")
            """),

        Pattern(["logic-python-returns-nothing-sometimes"], "A function that sometimes returns nothing",
            "The function returns a value on some paths, but on another it reaches the end and Python returns None.",
            "Code that uses the result works for some inputs and crashes, or prints None, for others.",
            "Make every path end in a return - often a final else, or a return after the loop.",
            """
            def grade(score):
                if score >= 50:
                    return "pass"
                return "fail"
            """),

        Pattern(["logic-python-floor-division-average"], "An average worked out with //",
            "// divides and rounds down, so the fraction of an average is lost.",
            "Averages come out too low - the average of 3 and 4 becomes 3.",
            "Use / for an exact division.",
            """
            average = sum(scores) / len(scores)
            """),

        Pattern(["logic-python-attribute-not-set"], "A method that changes a local instead of the object",
            "A bare name inside a method is a local variable, even when the object has an attribute of the same name.",
            "The object's value never changes, so the next method call starts from the old value.",
            "Assign to self.name.",
            """
            def add(self):
                self.count = self.count + 1
            """),
    ];
}
