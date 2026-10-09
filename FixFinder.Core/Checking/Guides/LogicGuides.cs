namespace FixFinder.Core.Checking.Guides;

/// <summary>Guides for logic mistakes, keyed by the pattern that finds them.</summary>
internal static class LogicGuides
{
    /// <summary>The guide for the checks with these ids: its title, and its explanation written for someone new to programming.</summary>
    internal static GuideEntry GuideFor(
        string[] ids, string title, string explanation, string why, string fix, string example) => new()
    {
        RuleIds = ids,
        Guide = new MistakeGuide(explanation, why, fix, example) { Title = title },
    };

    private static IReadOnlyList<GuideEntry> Shared { get; } =
    [
        GuideFor(["analysis-repeated-search"], "Searching from the start on every pass",
            "Looking for something in a list, or in a piece of text, means going through it from the start until it "
            + "turns up. Doing that inside a loop means doing it again on every pass, so when the loop and the list both "
            + "get ten times longer, the work gets a hundred times bigger rather than ten.",
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

        GuideFor(["wrong-output"], "Wrong output",
            "The program did not crash - it ran all the way through - but what it printed is different from what you said it " +
            "should print. So the mistake is in what the program works out, not in how it is written.",
            "The program looks as if it works, and gives the wrong answer.",
            "Change the line FixFinder points to; the lines the wrong runs passed through most are the likeliest place.",
            ""),

        GuideFor(["stopped-before-expected-output"], "Stopped before printing what was expected",
            "You said what the program should print, but it never got that far: it stopped with an error, or was still going " +
            "when the time ran out, before it had printed all of it. Whatever stopped it has to be put right first - only a " +
            "program that runs to the end can print everything it should.",
            "Until it runs to the end, the program cannot print everything it should, however right the rest of it is.",
            "Put right what stops it first - the error shown for the run - and then check what it prints again.",
            ""),

        GuideFor(["logic-python-is-literal"], "Comparing values with is",
            "`is` asks whether two names point at the very same thing in memory; == asks whether two values are equal. Python " +
            "sometimes keeps one copy of a small number or a short piece of text and shares it, so `is` can work while testing " +
            "and then fail with other values.",
            "Small numbers and short strings happen to be shared, so it works in testing and fails with other values.",
            "Compare values with == and !=.",
            """
            if answer == "yes":
                print("ok")
            """),

        GuideFor(["logic-python-assert-tuple"], "An assert that can never fail",
            "assert checks that something is true. Putting the condition and its message together in brackets makes a single " +
            "tuple - a pair of values - and a tuple with anything in it always counts as true, so this assert can never fail.",
            "The assert can never fail, so it never catches the mistake it was written to catch.",
            "Remove the brackets: assert condition, message.",
            """
            assert total >= 0, "total should never be negative"
            """),

        GuideFor(["logic-python-mutable-default"], "A list shared by every call",
            "A default value like items=[] is made once, when Python first reads the def line - not each time the function is " +
            "called. So every call that leaves the argument out shares the same list, and whatever one call adds is still " +
            "there in the next.",
            "Each call sees what earlier calls added, so results leak from one call into the next.",
            "Use None as the default and make a new list inside the function.",
            """
            def add_item(item, items=None):
                if items is None:
                    items = []
                items.append(item)
                return items
            """),

        GuideFor(["logic-python-result-discarded", "logic-result-discarded"], "A result that is thrown away",
            "Some methods, like upper() or strip() on text, do not change the original - they give back a new, changed copy. " +
            "The line calls one of them but does not keep the copy, so nothing actually changes.",
            "The line does nothing, so the change you wanted never happens.",
            "Store the result: name = name.upper().",
            """
            name = name.strip().title()
            """),

        GuideFor(["logic-python-return-in-loop", "logic-return-in-loop"], "A loop that stops after the first item",
            "return ends the whole function straight away. Here every way through the loop's first pass reaches a return, so " +
            "the loop never gets to its second item - anything further along is never looked at.",
            "Only the first item is ever looked at, so a match further along is never found.",
            "Return the 'not found' answer after the loop, once every item has been checked.",
            """
            for item in items:
                if item == target:
                    return True
            return False
            """),

        GuideFor(["logic-python-reset-in-loop", "logic-reset-in-loop"], "A total reset inside its own loop",
            "To add things up, you set the total to 0 once and then add each item to it. Here the total is set back to its " +
            "start at the beginning of every pass, which throws away everything added so far - so at the end it holds only " +
            "the last item's part.",
            "After the loop it only holds the last item's contribution, not the sum of all of them.",
            "Set the starting value once, before the loop.",
            """
            total = 0
            for price in prices:
                total += price
            """),

        GuideFor(["logic-python-comparison-statement"], "A comparison where an assignment was meant",
            "One = stores a value; two == compare values. This line compares with == but does nothing with the answer, so " +
            "nothing changes - it was probably meant to store a value with =.",
            "The variable is never changed.",
            "Use = to store a value.",
            """
            total = 0
            """),

        GuideFor(["logic-python-loop-never-advances", "logic-loop-never-advances"], "A loop that never ends",
            "A while loop keeps going until its condition becomes false. Nothing inside this loop changes anything the " +
            "condition looks at, so once the condition is true it stays true, and the loop never stops.",
            "The program hangs forever and never prints its result.",
            "Move the counter on inside the loop.",
            """
            i = 0
            while i < len(items):
                print(items[i])
                i += 1
            """),

        GuideFor(["logic-integer-division"], "Whole-number division loses the fraction",
            "When both numbers in a division are whole numbers, Java, C#, C and C++ give a whole-number answer and throw the " +
            "fraction away - 7 / 2 is 3. Storing that in a decimal variable afterwards is too late: the fraction is already gone.",
            "Averages and percentages come out rounded down - 7 / 2 gives 3, not 3.5.",
            "Make one side a decimal before dividing.",
            """
            double average = (double) sum / count;
            """),

        GuideFor(["logic-empty-loop-body"], "A loop with an empty body",
            "A semicolon straight after for (...) or while (...) is a complete, empty statement, so it becomes the loop's whole " +
            "body. The loop runs doing nothing, and the block underneath runs once, afterwards.",
            "The loop runs without doing anything, and the block below runs once afterwards.",
            "Remove the semicolon after the loop's brackets.",
            """
            for (int i = 0; i < n; i++) {
                total += values[i];
            }
            """),

        GuideFor(["logic-empty-if-body"], "An if that controls nothing",
            "A semicolon straight after if (...) is a complete, empty statement, so it becomes everything the if controls. The " +
            "block underneath is not part of the if, and runs every time.",
            "The block below runs every time, whether the condition is true or not.",
            "Remove the semicolon after the condition.",
            """
            if (score > 50) {
                System.out.println("pass");
            }
            """),

        GuideFor(["logic-java-string-equals"], "Strings compared with ==",
            "In Java a String variable holds a reference - where the text is stored - and == compares those references, not " +
            "the letters. Text typed in or put together while the program runs is stored in a new place, so == says 'different' " +
            "even when the words match.",
            "Text read from input or built at run time is a new object, so the comparison fails even when the words match.",
            "Compare strings with .equals().",
            """
            if (answer.equals("yes")) {
                System.out.println("ok");
            }
            """),

        GuideFor(["logic-assignment-in-condition"], "An assignment inside a condition",
            "One = stores a value; two == compare values. The condition here uses one =, so it overwrites the variable instead " +
            "of checking it, and the if goes the same way almost every time.",
            "The condition assigns instead of checking, so the if takes the same branch every time and the variable is overwritten.",
            "Use == to compare.",
            """
            if (count == 0) {
                printf("empty\n");
            }
            """),

        GuideFor(["logic-bitwise-precedence"], "A bit test worked out in the wrong order",
            "Operators are worked out in a set order, as multiplication comes before addition in maths. In these languages == " +
            "comes before & and |, so flags & MASK == MASK does the comparison first and then the & on its answer.",
            "The test checks something other than what it reads as.",
            "Put brackets around the bit test.",
            """
            if ((flags & MASK) == MASK) {
            }
            """),

        GuideFor(["logic-switch-fallthrough"], "A case that runs into the next",
            "In a switch the program jumps to the matching case and keeps going down through the cases below until it meets a " +
            "break. This case has no break, so the next case's code runs as well.",
            "Choosing one option also runs the next.",
            "End the case with break.",
            """
            case 1:
                System.out.println("one");
                break;
            """),

        GuideFor(["logic-uninitialised-total"], "A total that starts from garbage",
            "In C and C++ a variable declared inside a function starts with whatever junk was in that memory, not with 0. " +
            "Adding to it adds to the junk, so the total is wrong - and different from run to run.",
            "The result is different from run to run.",
            "Start the total at 0.",
            """
            int total = 0;
            """),

        GuideFor(["logic-string-literal-modified"], "Changing a string literal",
            "Text written in quotes in C, like \"hello\", is stored where the program is not meant to change it. A pointer to it " +
            "may read the letters but must not change them; an array made from it, char name[] = \"hello\", is your own copy, " +
            "which you can change.",
            "The program crashes on some systems and silently changes shared text on others.",
            "Copy the text into an array first.",
            """
            char name[] = "hello";
            name[0] = 'H';
            """),

        GuideFor(["logic-c-string-equals"], "C strings compared with ==",
            "In C a string variable holds the address of where its letters are stored, and == compares addresses, not letters. " +
            "Two copies of the same word are usually at different addresses, so == says they are different.",
            "The comparison fails even when the words match.",
            "Use strcmp(a, b) == 0.",
            """
            if (strcmp(answer, "yes") == 0) {
            }
            """),

        GuideFor(["logic-cpp-catch-by-value"], "An exception caught by value",
            "Exceptions often come in families: a specific error built on a general one. Catching the general kind by value " +
            "makes a copy of just the general part, so the specific details are cut off.",
            "The handler loses the real error's type and message.",
            "Catch by const reference.",
            """
            catch (const std::exception& error) {
                std::cerr << error.what() << '\n';
            }
            """),

        GuideFor(["logic-cpp-non-virtual-destructor"], "A base class without a virtual destructor",
            "When an object is deleted, its destructor cleans up what it owns. If the base class's destructor is not virtual, " +
            "deleting a derived object through a pointer to the base runs only the base's clean-up, and the derived part's " +
            "never happens.",
            "Deleting a derived object through a base pointer skips the derived destructor, leaking what it owns.",
            "Give the base class a virtual destructor.",
            """
            class Shape {
            public:
                virtual ~Shape() = default;
            };
            """),

        GuideFor(["logic-js-var-in-closure"], "Every callback sees the last loop value",
            "A var variable in a for loop is one single variable shared by every pass. Functions made inside the loop remember " +
            "the variable, not its value at the time - so when they run later, they all see its final value.",
            "Every callback uses the last value instead of its own.",
            "Declare the loop variable with let.",
            """
            for (let i = 0; i < buttons.length; i++) {
              buttons[i].onclick = () => console.log(i);
            }
            """),

        GuideFor(["logic-js-numeric-sort"], "Numbers sorted as text",
            "sort() with nothing in its brackets sorts everything as text, character by character - and as text \"10\" comes " +
            "before \"9\", because \"1\" comes before \"9\". Giving sort a way to compare numbers fixes it.",
            "Numbers come out in the wrong order.",
            "Give sort a comparison: (a, b) => a - b.",
            """
            scores.sort((a, b) => a - b);
            """),

        GuideFor(["logic-js-map-parseint"], "parseInt given the index as its base",
            "map calls the function you give it with each item and also the item's position in the list. parseInt takes a " +
            "second value as the number base - base 1, base 2 and so on - so most items come out as NaN, 'not a number'.",
            "Most values come out as NaN.",
            "Use Number, or wrap parseInt: map(s => parseInt(s, 10)).",
            """
            const numbers = texts.map(Number);
            """),

        GuideFor(["logic-js-float-equality"], "Decimal numbers compared exactly",
            "Every number in JavaScript is stored in binary, where most fractions - 0.1, 0.2 - can only be stored approximately, " +
            "the way 1/3 can only be written as 0.333... in decimal. Arithmetic on them leaves a tiny error, so 0.1 + 0.2 is " +
            "0.30000000000000004, and 0.1 + 0.2 === 0.3 is false.",
            "An exact comparison fails even when the numbers are equal for every practical purpose, so the code it guards never runs.",
            "Check that the difference is smaller than a tiny tolerance: Math.abs(total - 0.3) < 1e-9.",
            """
            const total = 0.1 + 0.2;
            if (Math.abs(total - 0.3) < 1e-9) {
              console.log("equal");
            }
            """),
    ];

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        .. PythonPatternGuides.All,
        .. BracePatternGuides.All,
        .. ManagedPatternGuides.All,
        .. ScalaPatternGuides.All,
        .. OCamlPatternGuides.All,
        .. GoPatternGuides.All,
        .. AnalysisGuides.All,
        .. Shared,
    ];

    /// <summary>The guides written in this file, apart from those the other pattern files add.</summary>
    internal static IReadOnlyList<GuideEntry> SharedGuides => Shared;
}
