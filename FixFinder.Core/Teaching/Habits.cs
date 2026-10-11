namespace FixFinder.Core.Teaching;

/// <summary>
/// For each idea behind a mistake, one habit that stops it happening again - what to do while writing the code, in the
/// same plain words as the idea itself.
/// </summary>
public static class Habits
{
    private static readonly Dictionary<string, string> ByConceptCode = new(StringComparer.Ordinal)
    {
        ["DIVZERO"] = "Before dividing, ask whether the number you divide by can ever be zero - a count of things that might be empty, " +
                      "a number someone types in - and decide what the answer should be when it is.",
        ["NOTHING"] = "When a value might be missing - a search that finds nothing, a variable not yet given an object - check for that " +
                      "before using it.",
        ["INDEX"] = "Remember that most languages count positions from 0, so a collection of n items has positions 0 to n - 1. Looping " +
                    "over the items themselves, rather than their positions, cannot go past the end.",
        ["KEYS"] = "Before looking a key up, check it is there - or use the way of looking up that gives a value of your choosing when it " +
                   "is not.",
        ["NAMES"] = "Copy a name rather than typing it again, keep its capital letters the same everywhere, and make it before the line " +
                    "that uses it.",
        ["UNSET"] = "Give every variable its starting value on the line that makes it.",
        ["TYPES"] = "Know the type of each value you put together, and turn a value into the type you need yourself - text into a " +
                    "number, a number into text - before you combine them.",
        ["BLOCKS"] = "Let your editor indent for you, keep to one way of indenting, and put braces around the body of every if and " +
                     "loop, even one of a single line.",
        ["PAIRS"] = "Type the closing bracket or quote as soon as you type the opening one, then write what goes between them.",
        ["ENDINGS"] = "End each statement with its semicolon as you finish typing it - and when an error names a line, look at the end " +
                      "of the line above it too.",
        ["COMPARE"] = "In a condition, read = as \"becomes\" and == as \"is equal to\", and check which of the two you meant.",
        ["EQUALITY"] = "Compare text and other objects by what they hold - equals in Java, == in Python and C# for text - rather than by " +
                       "whether they are the same object.",
        ["OFFBYONE"] = "When a loop uses positions, check its first and last pass by hand: does it start at the first item, and stop at " +
                       "the last one?",
        ["ENDLESS"] = "For every loop, find the line that moves it towards its end, and check that its condition really can become false.",
        ["INTDIV"] = "When you want an answer with a fraction, make one of the numbers a decimal before dividing.",
        ["CONVERT"] = "Treat anything typed in, or read from a file, as text until you have checked that it really is a number.",
        ["ARGS"] = "Look at a function's definition before you call it, and give it each value it asks for, in the order it asks.",
        ["RETURNS"] = "Decide what each function gives back, make every way through it give that back, and keep what it gives you.",
        ["RECURSION"] = "Write the case where a function that calls itself stops first, and check that every call moves closer to it.",
        ["CASES"] = "Give every switch or match a last case - default, or _ - for the values you did not list.",
        ["FALLTHROUGH"] = "End every case of a switch with break, unless you mean it to run on into the next case.",
        ["CONSTANT"] = "Make a name that can change - var, let, no final - only when you mean to change it.",
        ["CHANGELOOP"] = "Do not add to or remove from a list while a loop walks through it: build a new list as you go, or loop over a copy.",
        ["UNUSED"] = "Use each variable you make - and when one goes unused, check you did not store a result under one name and read " +
                     "another.",
        ["GENERAL"] = "Read an error message starting from where it names your own file and line, and change one thing at a time, " +
                      "checking again after each.",
    };

    /// <summary>The habit for an idea. Every idea FixFinder teaches has one.</summary>
    public static string For(Concept concept) =>
        ByConceptCode.TryGetValue(concept.Code, out var habit) ? habit : ByConceptCode["GENERAL"];

    /// <summary>Whether an idea has a habit of its own, rather than the general one.</summary>
    public static bool HasOwn(Concept concept) => ByConceptCode.ContainsKey(concept.Code);
}
