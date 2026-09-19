using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>Guides for what following every value through a program proves or shows is possible.</summary>
internal static class AnalysisGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Pattern(["analysis-division-by-zero"], "Dividing by something that can be zero",
            "Following the values through the code shows the number being divided by is, or can be, 0 at this line - for example a count that stays 0 when a loop never runs.",
            "Dividing by zero stops the program with an error, often only for the inputs nobody tried, like an empty list.",
            "Check the divisor first and decide what the answer should be when it is 0.",
            """
            if count == 0:
                return 0
            return total / count
            """),

        Pattern(["analysis-null-used"], "Using something that can be None",
            "On at least one way through the code, the value is None when this line uses it - for example a variable set to None and only sometimes given a real value, or a search that found nothing.",
            "Reading an attribute of None, calling a method on it or taking an item from it stops the program with an error.",
            "Check for None before using it, or make sure every way through the code gives it a real value.",
            """
            found = re.match(pattern, text)
            if found is None:
                return ""
            return found.group(0)
            """),

        Pattern(["analysis-type-mismatch"], "Combining values of the wrong types",
            "The values at this line are certainly of types that cannot be combined this way - text and a number, or a number used where something with a length or items is needed.",
            "Python stops with a TypeError as soon as the line runs.",
            "Convert one side first: str(number) to join it to text, or int(text) to calculate with it.",
            """
            print("Age: " + str(age))
            """),

        Pattern(["analysis-index-out-of-range"], "Asking for a position that does not exist",
            "The list or text has a known length here, and the position asked for is past its end.",
            "Positions run from 0 to length - 1, so this stops the program with an IndexError.",
            "Use a position inside the list, such as -1 for the last item.",
            """
            points = [3, 5, 8]
            last = points[-1]
            """),

        Pattern(["analysis-empty-collection"], "Taking an item from something empty",
            "The list is certainly empty when this line runs, so there is nothing to take.",
            "pop() on an empty list stops the program with an IndexError.",
            "Check that it has items first.",
            """
            if stack:
                top = stack.pop()
            """),

        Pattern(["analysis-not-a-number"], "Converting text that is not a number",
            "The text given to int() or float() here can never be read as a number.",
            "The conversion stops the program with a ValueError.",
            "Convert text that holds digits, and check input with isdigit() or try/except before converting it.",
            """
            value = int("42")
            """),

        Pattern(["analysis-never-true"], "A condition that can never be true",
            "Following the values shows this condition is false every time it is reached - often two tests that contradict each other, or a check after the value has already been ruled out.",
            "The code it guards never runs, so whatever it was meant to handle is not handled.",
            "Check the comparison and which variable it tests; one of the two tests is usually the wrong way round.",
            """
            if mark > 100 or mark < 0:
                print("Out of range")
            """),

        Pattern(["analysis-always-true"], "A condition that is always true",
            "Following the values shows this condition is true every time it is reached, because an earlier test already settled it.",
            "The check does nothing. It is harmless, but it usually means the logic is not quite what was intended.",
            "Use else instead of a test that can only be true, or remove the check.",
            """
            if mark >= 50:
                result = "pass"
            else:
                result = "fail"
            """),

        Pattern(["analysis-loop-never-runs"], "A loop that never runs",
            "The loop's condition is already false the first time it is checked, so the body is skipped entirely.",
            "Whatever the loop was meant to do - count, total, search - never happens.",
            "Check the starting value and the comparison: often it should be < rather than >, or the variable should start somewhere else.",
            """
            n = 10
            while n > 0:
                n -= 1
            """),

        Pattern(["analysis-assert-always-fails"], "An assert that always fails",
            "The condition in this assert is false every time the line is reached.",
            "The program stops with an AssertionError here every time.",
            "Check the condition; if it describes what should be true, the code before it is setting the value wrongly.",
            """
            n = 10
            assert n > 5
            """),
    ];
}
