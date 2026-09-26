namespace FixFinder.Core.Checking.Guides;

/// <summary>Guides for logic mistakes, keyed by the pattern that finds them.</summary>
internal static class LogicGuides
{
    /// <summary>
    /// A guide with its explanation written three ways: for someone new to programming, as it is usually taught, and for
    /// someone who works on this every day. The three say the same thing about the same program; only the words differ.
    /// </summary>
    internal static GuideEntry AtEveryLevel(
        string[] ids, string title, string beginner, string explanation, string technical, string why, string fix, string example) => new()
    {
        RuleIds = ids,
        Guide = new MistakeGuide(explanation, why, fix, example)
        {
            Title = title,
            ForBeginners = beginner,
            ForTechnical = technical,
        },
    };

    private static IReadOnlyList<GuideEntry> Shared { get; } =
    [
        AtEveryLevel(["analysis-repeated-search"], "Searching from the start on every pass",
            "Looking for something in a list, or in a piece of text, means going through it from the start until it "
            + "turns up. Doing that inside a loop means doing it again on every pass, so when the loop and the list both "
            + "get ten times longer, the work gets a hundred times bigger rather than ten.",
            "The loop searches the same collection from the start on every pass, so the work grows with the number of "
            + "passes multiplied by the length of the collection.",
            "The search is linear and runs once per iteration, so the loop costs O(n*m) for n iterations over m items; "
            + "a hashed lookup built once before it makes that O(n + m) on average.",
            "It is correct either way, and stays quick while both are small. It is the pattern that gets slow first as "
            + "real data arrives, and the reason is rarely obvious afterwards.",
            "If the collection does not change inside the loop and its items can go in a set, put them in a set once "
            + "before the loop and search that instead - a set answers in about the same time however many items it "
            + "holds. Searching text for a piece of text cannot be sped up this way.",
            """
            wanted = set(names)
            for person in people:
                if person in wanted:
                    ...
            """),

        AtEveryLevel(["wrong-output"], "Wrong output",
            "The program did not crash - it ran all the way through - but what it printed is different from what you said it " +
            "should print. So the mistake is in what the program works out, not in how it is written.",
            "The program ran to the end, but what it printed is not what you said it should print.",
            "FixFinder compares standard output line by line, as exact text once trailing spaces and trailing blank lines are " +
            "dropped, and reports the first line that differs. It then ranks the lines by their Ochiai score - how strongly " +
            "running a line goes with the wrong runs rather than the right ones - and tries small changes at the highest-ranked " +
            "lines, keeping one only if every run then prints what was expected.",
            "The program looks as if it works, and gives the wrong answer.",
            "Change the line FixFinder points to; the lines the wrong runs passed through most are the likeliest place.",
            ""),

        AtEveryLevel(["logic-python-is-literal"], "Comparing values with is",
            "`is` asks whether two names point at the very same thing in memory; == asks whether two values are equal. Python " +
            "sometimes keeps one copy of a small number or a short piece of text and shares it, so `is` can work while testing " +
            "and then fail with other values.",
            "`is` checks whether two things are the very same object, not whether they are equal.",
            "`is` compares identity, while == calls __eq__ and compares values. CPython caches small integers (-5 to 256) and " +
            "interns some strings, so identity with a literal holds only by accident of the implementation; Python 3.8 and " +
            "later warn about `is` with a literal.",
            "Small numbers and short strings happen to be shared, so it works in testing and fails with other values.",
            "Compare values with == and !=.",
            """
            if answer == "yes":
                print("ok")
            """),

        AtEveryLevel(["logic-python-assert-tuple"], "An assert that can never fail",
            "assert checks that something is true. Putting the condition and its message together in brackets makes a single " +
            "tuple - a pair of values - and a tuple with anything in it always counts as true, so this assert can never fail.",
            "The brackets turn the assert into a check of a tuple, which always counts as true.",
            "The statement is assert expression, message. With both in brackets the expression is a two-item tuple, which is " +
            "always truthy, so the assertion never fires; CPython warns that the assertion is always true.",
            "The assert can never fail, so it never catches the mistake it was written to catch.",
            "Remove the brackets: assert condition, message.",
            """
            assert total >= 0, "total should never be negative"
            """),

        AtEveryLevel(["logic-python-mutable-default"], "A list shared by every call",
            "A default value like items=[] is made once, when Python first reads the def line - not each time the function is " +
            "called. So every call that leaves the argument out shares the same list, and whatever one call adds is still " +
            "there in the next.",
            "A list or dictionary used as a default value is made once, and every call that leaves the argument out shares it.",
            "Default argument values are evaluated once, when the def statement runs, and stored on the function object in " +
            "__defaults__, so a mutable default is shared by every call that uses it. The idiom is a None default, with a new " +
            "object made inside the function.",
            "Each call sees what earlier calls added, so results leak from one call into the next.",
            "Use None as the default and make a new list inside the function.",
            """
            def add_item(item, items=None):
                if items is None:
                    items = []
                items.append(item)
                return items
            """),

        AtEveryLevel(["logic-python-result-discarded", "logic-result-discarded"], "A result that is thrown away",
            "Some methods, like upper() or strip() on text, do not change the original - they give back a new, changed copy. " +
            "The line calls one of them but does not keep the copy, so nothing actually changes.",
            "The method returns a new value and leaves the original unchanged, and nothing keeps the new value.",
            "The method returns a new value and leaves its receiver as it was - strings cannot be changed in Python, Java or " +
            "C#, and calls such as sorted() or LINQ's OrderBy return new sequences - and the result is discarded, so the " +
            "statement has no effect.",
            "The line does nothing, so the change you wanted never happens.",
            "Store the result: name = name.upper().",
            """
            name = name.strip().title()
            """),

        AtEveryLevel(["logic-python-return-in-loop", "logic-return-in-loop"], "A loop that stops after the first item",
            "return ends the whole function straight away. Here every way through the loop's first pass reaches a return, so " +
            "the loop never gets to its second item - anything further along is never looked at.",
            "Both branches inside the loop return, so the loop stops on its first pass.",
            "Every path through the loop body ends in a return, so control never goes round the loop again and it runs at most " +
            "once. The 'not found' answer belongs after the loop, where it is reached only once every item has been checked.",
            "Only the first item is ever looked at, so a match further along is never found.",
            "Return the 'not found' answer after the loop, once every item has been checked.",
            """
            for item in items:
                if item == target:
                    return True
            return False
            """),

        AtEveryLevel(["logic-python-reset-in-loop", "logic-reset-in-loop"], "A total reset inside its own loop",
            "To add things up, you set the total to 0 once and then add each item to it. Here the total is set back to its " +
            "start at the beginning of every pass, which throws away everything added so far - so at the end it holds only " +
            "the last item's part.",
            "The total is set back to its starting value at the start of every pass of the loop.",
            "The accumulator is re-initialised inside the loop body, so each pass's update is wiped out by the next pass's " +
            "reset and only the last pass's contribution survives; the initialisation belongs before the loop.",
            "After the loop it only holds the last item's contribution, not the sum of all of them.",
            "Set the starting value once, before the loop.",
            """
            total = 0
            for price in prices:
                total += price
            """),

        AtEveryLevel(["logic-python-comparison-statement"], "A comparison where an assignment was meant",
            "One = stores a value; two == compare values. This line compares with == but does nothing with the answer, so " +
            "nothing changes - it was probably meant to store a value with =.",
            "A line compares with == but does nothing with the answer, where an assignment with = was meant.",
            "An expression statement that is a comparison computes a bool and throws it away, so it has no effect unless " +
            "__eq__ itself does something; an assignment was almost certainly meant.",
            "The variable is never changed.",
            "Use = to store a value.",
            """
            total = 0
            """),

        AtEveryLevel(["logic-python-loop-never-advances", "logic-loop-never-advances"], "A loop that never ends",
            "A while loop keeps going until its condition becomes false. Nothing inside this loop changes anything the " +
            "condition looks at, so once the condition is true it stays true, and the loop never stops.",
            "Nothing inside the loop changes what its condition checks, so once the loop starts it never stops.",
            "No variable the condition reads is assigned in the loop body, so the condition has the same value on every pass: " +
            "if it holds when the loop is entered, the loop never terminates.",
            "The program hangs forever and never prints its result.",
            "Move the counter on inside the loop.",
            """
            i = 0
            while i < len(items):
                print(items[i])
                i += 1
            """),

        AtEveryLevel(["logic-integer-division"], "Whole-number division loses the fraction",
            "When both numbers in a division are whole numbers, Java, C#, C and C++ give a whole-number answer and throw the " +
            "fraction away - 7 / 2 is 3. Storing that in a decimal variable afterwards is too late: the fraction is already gone.",
            "Two whole numbers are divided, which throws the fraction away before the result is stored in a decimal variable.",
            "When both operands have an integer type, / is integer division, truncating toward zero, and the result is only " +
            "converted to double afterwards, on assignment. Casting one operand first makes it a floating-point division.",
            "Averages and percentages come out rounded down - 7 / 2 gives 3, not 3.5.",
            "Make one side a decimal before dividing.",
            """
            double average = (double) sum / count;
            """),

        AtEveryLevel(["logic-empty-loop-body"], "A loop with an empty body",
            "A semicolon straight after for (...) or while (...) is a complete, empty statement, so it becomes the loop's whole " +
            "body. The loop runs doing nothing, and the block underneath runs once, afterwards.",
            "There is a semicolon straight after the loop's brackets, so the loop has an empty body.",
            "The null statement is the loop's body, so the braces after it are an ordinary block run once the loop has " +
            "finished; a while loop whose body cannot change its condition may never finish at all.",
            "The loop runs without doing anything, and the block below runs once afterwards.",
            "Remove the semicolon after the loop's brackets.",
            """
            for (int i = 0; i < n; i++) {
                total += values[i];
            }
            """),

        AtEveryLevel(["logic-empty-if-body"], "An if that controls nothing",
            "A semicolon straight after if (...) is a complete, empty statement, so it becomes everything the if controls. The " +
            "block underneath is not part of the if, and runs every time.",
            "There is a semicolon straight after the if's condition, which makes its body empty.",
            "if (condition); has the null statement as its then-branch, so the block after it is an independent statement run " +
            "unconditionally; the condition is still evaluated, for whatever side effects it has.",
            "The block below runs every time, whether the condition is true or not.",
            "Remove the semicolon after the condition.",
            """
            if (score > 50) {
                System.out.println("pass");
            }
            """),

        AtEveryLevel(["logic-java-string-equals"], "Strings compared with ==",
            "In Java a String variable holds a reference - where the text is stored - and == compares those references, not " +
            "the letters. Text typed in or put together while the program runs is stored in a new place, so == says 'different' " +
            "even when the words match.",
            "Strings are compared with ==, which checks whether they are the same object, not whether they have the same letters.",
            "== on references tests identity. String literals are interned, so comparing one literal with another can appear " +
            "to work, but strings made while the program runs - Scanner input, concatenation, substring - are distinct " +
            "objects. String.equals compares the characters.",
            "Text read from input or built at run time is a new object, so the comparison fails even when the words match.",
            "Compare strings with .equals().",
            """
            if (answer.equals("yes")) {
                System.out.println("ok");
            }
            """),

        AtEveryLevel(["logic-assignment-in-condition"], "An assignment inside a condition",
            "One = stores a value; two == compare values. The condition here uses one =, so it overwrites the variable instead " +
            "of checking it, and the if goes the same way almost every time.",
            "The condition uses = (assignment) where == (comparison) was meant.",
            "In C and C++ an assignment is an expression whose value is the value assigned, so if (x = 0) stores 0 and tests " +
            "it as false; compilers warn about it (-Wparentheses) unless the assignment is wrapped in a second pair of " +
            "parentheses.",
            "The condition assigns instead of checking, so the if takes the same branch every time and the variable is overwritten.",
            "Use == to compare.",
            """
            if (count == 0) {
                printf("empty\n");
            }
            """),

        AtEveryLevel(["logic-bitwise-precedence"], "A bit test worked out in the wrong order",
            "Operators are worked out in a set order, as multiplication comes before addition in maths. In these languages == " +
            "comes before & and |, so flags & MASK == MASK does the comparison first and then the & on its answer.",
            "& and | are applied after ==, so the comparison is worked out first and the bit test is done on its result.",
            "In C, C++, Java, C# and JavaScript the equality operators bind more tightly than the bitwise &, ^ and | - an order " +
            "inherited from C - so flags & MASK == MASK parses as flags & (MASK == MASK). The bitwise operation needs brackets.",
            "The test checks something other than what it reads as.",
            "Put brackets around the bit test.",
            """
            if ((flags & MASK) == MASK) {
            }
            """),

        AtEveryLevel(["logic-switch-fallthrough"], "A case that runs into the next",
            "In a switch the program jumps to the matching case and keeps going down through the cases below until it meets a " +
            "break. This case has no break, so the next case's code runs as well.",
            "This case has no break, so the program carries on into the next case.",
            "Case labels are only entry points: control runs on into the next label's statements unless break, return, throw " +
            "or goto intervenes. C# rejects falling out of a non-empty section, and Java's arrow-style labels never fall through.",
            "Choosing one option also runs the next.",
            "End the case with break.",
            """
            case 1:
                System.out.println("one");
                break;
            """),

        AtEveryLevel(["logic-uninitialised-total"], "A total that starts from garbage",
            "In C and C++ a variable declared inside a function starts with whatever junk was in that memory, not with 0. " +
            "Adding to it adds to the junk, so the total is wrong - and different from run to run.",
            "The total starts with no value, so it adds to whatever happened to be in memory.",
            "An automatic variable without an initializer has an indeterminate value, and reading it is undefined behaviour - " +
            "in C, for a variable whose address is never taken. Static and global variables, by contrast, start at zero.",
            "The result is different from run to run.",
            "Start the total at 0.",
            """
            int total = 0;
            """),

        AtEveryLevel(["logic-string-literal-modified"], "Changing a string literal",
            "Text written in quotes in C, like \"hello\", is stored where the program is not meant to change it. A pointer to it " +
            "may read the letters but must not change them; an array made from it, char name[] = \"hello\", is your own copy, " +
            "which you can change.",
            "The code changes the letters of a string literal, which C stores in read-only memory.",
            "Modifying a string literal is undefined behaviour: literals have static storage, are usually placed in read-only " +
            "memory - so the write faults - and identical literals may share storage. char s[] = \"...\" copies the literal " +
            "into an array that can be modified.",
            "The program crashes on some systems and silently changes shared text on others.",
            "Copy the text into an array first.",
            """
            char name[] = "hello";
            name[0] = 'H';
            """),

        AtEveryLevel(["logic-c-string-equals"], "C strings compared with ==",
            "In C a string variable holds the address of where its letters are stored, and == compares addresses, not letters. " +
            "Two copies of the same word are usually at different addresses, so == says they are different.",
            "C strings are compared with ==, which compares their addresses, not their letters.",
            "An array decays to a pointer to its first element, so == compares addresses, and whether equal literals share " +
            "storage is unspecified. strcmp compares the characters and returns 0 when they are equal.",
            "The comparison fails even when the words match.",
            "Use strcmp(a, b) == 0.",
            """
            if (strcmp(answer, "yes") == 0) {
            }
            """),

        AtEveryLevel(["logic-cpp-catch-by-value"], "An exception caught by value",
            "Exceptions often come in families: a specific error built on a general one. Catching the general kind by value " +
            "makes a copy of just the general part, so the specific details are cut off.",
            "The exception is caught by value, which copies it as the base class and drops what made it specific.",
            "catch (std::exception e) copy-initialises a std::exception from the thrown object, slicing off the derived part, " +
            "so virtual calls such as what() go to the base class. Catching by const reference binds to the original object.",
            "The handler loses the real error's type and message.",
            "Catch by const reference.",
            """
            catch (const std::exception& error) {
                std::cerr << error.what() << '\n';
            }
            """),

        AtEveryLevel(["logic-cpp-non-virtual-destructor"], "A base class without a virtual destructor",
            "When an object is deleted, its destructor cleans up what it owns. If the base class's destructor is not virtual, " +
            "deleting a derived object through a pointer to the base runs only the base's clean-up, and the derived part's " +
            "never happens.",
            "A class meant to be a base class has no virtual destructor.",
            "Deleting a derived object through a pointer to a base whose destructor is not virtual is undefined behaviour; in " +
            "practice only the base destructor runs, leaking what the derived members own. A base meant for polymorphic " +
            "deletion needs a public virtual destructor.",
            "Deleting a derived object through a base pointer skips the derived destructor, leaking what it owns.",
            "Give the base class a virtual destructor.",
            """
            class Shape {
            public:
                virtual ~Shape() = default;
            };
            """),

        AtEveryLevel(["logic-js-var-in-closure"], "Every callback sees the last loop value",
            "A var variable in a for loop is one single variable shared by every pass. Functions made inside the loop remember " +
            "the variable, not its value at the time - so when they run later, they all see its final value.",
            "A var loop variable is shared by every pass, so functions created in the loop all see its final value.",
            "var is function-scoped, so the loop has a single binding that every closure captures; let gives a for loop a " +
            "fresh binding for each iteration, so each closure keeps its own value.",
            "Every callback uses the last value instead of its own.",
            "Declare the loop variable with let.",
            """
            for (let i = 0; i < buttons.length; i++) {
              buttons[i].onclick = () => console.log(i);
            }
            """),

        AtEveryLevel(["logic-js-numeric-sort"], "Numbers sorted as text",
            "sort() with nothing in its brackets sorts everything as text, character by character - and as text \"10\" comes " +
            "before \"9\", because \"1\" comes before \"9\". Giving sort a way to compare numbers fixes it.",
            "sort() with no comparison sorts numbers as text, so 10 comes before 9.",
            "Array.prototype.sort without a comparator converts the elements to strings and orders them by UTF-16 code units, " +
            "so 10 sorts before 9; the comparator (a, b) => a - b orders them numerically.",
            "Numbers come out in the wrong order.",
            "Give sort a comparison: (a, b) => a - b.",
            """
            scores.sort((a, b) => a - b);
            """),

        AtEveryLevel(["logic-js-map-parseint"], "parseInt given the index as its base",
            "map calls the function you give it with each item and also the item's position in the list. parseInt takes a " +
            "second value as the number base - base 1, base 2 and so on - so most items come out as NaN, 'not a number'.",
            "map passes each item's index as parseInt's second argument, the number base.",
            "Array.prototype.map calls its callback with (element, index, array), and parseInt(string, radix) takes the index " +
            "as the radix: 0 means the default, 1 is invalid and gives NaN, and 2 accepts only binary digits. Number, or an " +
            "arrow function passing radix 10, avoids it.",
            "Most values come out as NaN.",
            "Use Number, or wrap parseInt: map(s => parseInt(s, 10)).",
            """
            const numbers = texts.map(Number);
            """),
    ];

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        .. PythonPatternGuides.All,
        .. BracePatternGuides.All,
        .. ManagedPatternGuides.All,
        .. AnalysisGuides.All,
        .. Shared,
    ];

    /// <summary>The guides written in this file, apart from those the other pattern files add.</summary>
    internal static IReadOnlyList<GuideEntry> SharedGuides => Shared;
}
