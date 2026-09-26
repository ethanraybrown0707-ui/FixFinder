namespace FixFinder.Core.Checking.Guides;

/// <summary>Guides for logic mistakes, keyed by the pattern that finds them.</summary>
internal static class LogicGuides
{
    internal static GuideEntry Pattern(string[] ids, string title, string explanation, string why, string fix, string example) => new()
    {
        RuleIds = ids,
        Guide = new MistakeGuide(explanation, why, fix, example) { Title = title },
    };

    /// <summary>
    /// The same guide with the explanation also written for someone new to programming and for someone who works on
    /// this every day. The three say the same thing about the same program; only the words differ.
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

        Pattern(["wrong-output"], "Wrong output",
            "The program ran to the end, but what it printed is not what you said it should print.",
            "The program looks as if it works, and gives the wrong answer.",
            "Change the line FixFinder points to; the lines the wrong runs passed through most are the likeliest place.",
            ""),

        Pattern(["logic-python-is-literal"], "Comparing values with is",
            "`is` checks whether two things are the very same object, not whether they are equal.",
            "Small numbers and short strings happen to be shared, so it works in testing and fails with other values.",
            "Compare values with == and !=.",
            """
            if answer == "yes":
                print("ok")
            """),

        Pattern(["logic-python-assert-tuple"], "An assert that can never fail",
            "The brackets turn the assert into a check of a tuple, which always counts as true.",
            "The assert can never fail, so it never catches the mistake it was written to catch.",
            "Remove the brackets: assert condition, message.",
            """
            assert total >= 0, "total should never be negative"
            """),

        Pattern(["logic-python-mutable-default"], "A list shared by every call",
            "A list or dictionary used as a default value is made once, and every call that leaves the argument out shares it.",
            "Each call sees what earlier calls added, so results leak from one call into the next.",
            "Use None as the default and make a new list inside the function.",
            """
            def add_item(item, items=None):
                if items is None:
                    items = []
                items.append(item)
                return items
            """),

        Pattern(["logic-python-result-discarded", "logic-result-discarded"], "A result that is thrown away",
            "The method returns a new value and leaves the original unchanged, and nothing keeps the new value.",
            "The line does nothing, so the change you wanted never happens.",
            "Store the result: name = name.upper().",
            """
            name = name.strip().title()
            """),

        Pattern(["logic-python-return-in-loop", "logic-return-in-loop"], "A loop that stops after the first item",
            "Both branches inside the loop return, so the loop stops on its first pass.",
            "Only the first item is ever looked at, so a match further along is never found.",
            "Return the 'not found' answer after the loop, once every item has been checked.",
            """
            for item in items:
                if item == target:
                    return True
            return False
            """),

        Pattern(["logic-python-reset-in-loop", "logic-reset-in-loop"], "A total reset inside its own loop",
            "The total is set back to its starting value at the start of every pass of the loop.",
            "After the loop it only holds the last item's contribution, not the sum of all of them.",
            "Set the starting value once, before the loop.",
            """
            total = 0
            for price in prices:
                total += price
            """),

        Pattern(["logic-python-comparison-statement"], "A comparison where an assignment was meant",
            "A line compares with == but does nothing with the answer, where an assignment with = was meant.",
            "The variable is never changed.",
            "Use = to store a value.",
            """
            total = 0
            """),

        Pattern(["logic-python-loop-never-advances", "logic-loop-never-advances"], "A loop that never ends",
            "Nothing inside the loop changes what its condition checks, so once the loop starts it never stops.",
            "The program hangs forever and never prints its result.",
            "Move the counter on inside the loop.",
            """
            i = 0
            while i < len(items):
                print(items[i])
                i += 1
            """),

        Pattern(["logic-integer-division"], "Whole-number division loses the fraction",
            "Two whole numbers are divided, which throws the fraction away before the result is stored in a decimal variable.",
            "Averages and percentages come out rounded down - 7 / 2 gives 3, not 3.5.",
            "Make one side a decimal before dividing.",
            """
            double average = (double) sum / count;
            """),

        Pattern(["logic-empty-loop-body"], "A loop with an empty body",
            "There is a semicolon straight after the loop's brackets, so the loop has an empty body.",
            "The loop runs without doing anything, and the block below runs once afterwards.",
            "Remove the semicolon after the loop's brackets.",
            """
            for (int i = 0; i < n; i++) {
                total += values[i];
            }
            """),

        Pattern(["logic-empty-if-body"], "An if that controls nothing",
            "There is a semicolon straight after the if's condition, which makes its body empty.",
            "The block below runs every time, whether the condition is true or not.",
            "Remove the semicolon after the condition.",
            """
            if (score > 50) {
                System.out.println("pass");
            }
            """),

        Pattern(["logic-java-string-equals"], "Strings compared with ==",
            "Strings are compared with ==, which checks whether they are the same object, not whether they have the same letters.",
            "Text read from input or built at run time is a new object, so the comparison fails even when the words match.",
            "Compare strings with .equals().",
            """
            if (answer.equals("yes")) {
                System.out.println("ok");
            }
            """),

        Pattern(["logic-assignment-in-condition"], "An assignment inside a condition",
            "The condition uses = (assignment) where == (comparison) was meant.",
            "The condition assigns instead of checking, so the if takes the same branch every time and the variable is overwritten.",
            "Use == to compare.",
            """
            if (count == 0) {
                printf("empty\n");
            }
            """),

        Pattern(["logic-bitwise-precedence"], "A bit test worked out in the wrong order",
            "& and | are applied after ==, so the comparison is worked out first and the bit test is done on its result.",
            "The test checks something other than what it reads as.",
            "Put brackets around the bit test.",
            """
            if ((flags & MASK) == MASK) {
            }
            """),

        Pattern(["logic-switch-fallthrough"], "A case that runs into the next",
            "This case has no break, so the program carries on into the next case.",
            "Choosing one option also runs the next.",
            "End the case with break.",
            """
            case 1:
                System.out.println("one");
                break;
            """),

        Pattern(["logic-uninitialised-total"], "A total that starts from garbage",
            "The total starts with no value, so it adds to whatever happened to be in memory.",
            "The result is different from run to run.",
            "Start the total at 0.",
            """
            int total = 0;
            """),

        Pattern(["logic-string-literal-modified"], "Changing a string literal",
            "The code changes the letters of a string literal, which C stores in read-only memory.",
            "The program crashes on some systems and silently changes shared text on others.",
            "Copy the text into an array first.",
            """
            char name[] = "hello";
            name[0] = 'H';
            """),

        Pattern(["logic-c-string-equals"], "C strings compared with ==",
            "C strings are compared with ==, which compares their addresses, not their letters.",
            "The comparison fails even when the words match.",
            "Use strcmp(a, b) == 0.",
            """
            if (strcmp(answer, "yes") == 0) {
            }
            """),

        Pattern(["logic-cpp-catch-by-value"], "An exception caught by value",
            "The exception is caught by value, which copies it as the base class and drops what made it specific.",
            "The handler loses the real error's type and message.",
            "Catch by const reference.",
            """
            catch (const std::exception& error) {
                std::cerr << error.what() << '\n';
            }
            """),

        Pattern(["logic-cpp-non-virtual-destructor"], "A base class without a virtual destructor",
            "A class meant to be a base class has no virtual destructor.",
            "Deleting a derived object through a base pointer skips the derived destructor, leaking what it owns.",
            "Give the base class a virtual destructor.",
            """
            class Shape {
            public:
                virtual ~Shape() = default;
            };
            """),

        Pattern(["logic-js-var-in-closure"], "Every callback sees the last loop value",
            "A var loop variable is shared by every pass, so functions created in the loop all see its final value.",
            "Every callback uses the last value instead of its own.",
            "Declare the loop variable with let.",
            """
            for (let i = 0; i < buttons.length; i++) {
              buttons[i].onclick = () => console.log(i);
            }
            """),

        Pattern(["logic-js-numeric-sort"], "Numbers sorted as text",
            "sort() with no comparison sorts numbers as text, so 10 comes before 9.",
            "Numbers come out in the wrong order.",
            "Give sort a comparison: (a, b) => a - b.",
            """
            scores.sort((a, b) => a - b);
            """),

        Pattern(["logic-js-map-parseint"], "parseInt given the index as its base",
            "map passes each item's index as parseInt's second argument, the number base.",
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
}
