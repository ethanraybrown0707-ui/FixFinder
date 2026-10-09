using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic checks of Scala programs, each explained for someone new to programming. Every example compiles
/// and runs with Scala 3.8.4 and Scala 2.13.18.
/// </summary>
internal static class ScalaPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-scala-range-to-length"], "A loop that runs one past the end",
            "0 to xs.length counts from 0 up to and including xs.length. But the positions in an Array or a List run from 0 to one " +
            "less than its length - a list of 3 items has positions 0, 1 and 2 - so the loop's last pass asks for a position that " +
            "is not there. 0 until xs.length stops one before the length, and xs.indices gives exactly the positions there are.",
            "The last pass stops the program with an index out of bounds - after the earlier passes have already run.",
            "Count with until instead of to, or loop over xs.indices.",
            """
            for (i <- 0 until marks.length) println(marks(i))
            """),

        GuideFor(["logic-scala-array-equals"], "Arrays compared with ==",
            "== on two Lists compares the items they hold. == on two Arrays does not: it asks whether they are one and the same " +
            "array in memory. Two arrays made apart - even with exactly the same items - are two different arrays, so == says false. " +
            "sameElements compares the items, in order.",
            "The comparison is false whenever the arrays were made apart, so the code it guards never runs when it should.",
            "Compare the items with sameElements: first.sameElements(second).",
            """
            if (first.sameElements(second)) println("same marks")
            """),

        GuideFor(["logic-scala-integer-average"], "A decimal that lost its fraction",
            "Dividing one whole number by another in Scala gives a whole number: the part after the point is dropped, so 3 / 2 is 1. " +
            "Storing the answer in a Double afterwards cannot bring the fraction back - it is already gone - so an average of 1 and 2 " +
            "comes out as 1.0 rather than 1.5. Making one of the numbers a Double first makes the division a decimal one.",
            "Averages and percentages come out rounded down to a whole number, with no error to say so.",
            "Turn the first number into a Double before dividing: scores.sum.toDouble / scores.length.",
            """
            val average: Double = scores.sum.toDouble / scores.length
            """),

        GuideFor(["logic-scala-result-discarded"], "A changed copy that is thrown away",
            "Scala's Lists and pieces of text never change once made. sorted, reverse, trim and their like do not change the value " +
            "they are called on - they give back a new, changed copy. A line that calls one and keeps nothing changes nothing: the " +
            "value is just as it was on the next line.",
            "The change never happens, so the lines after it go on using the value as it was.",
            "Keep the copy: give it a name of its own with val, or - for a var - give the var the copy.",
            """
            val sortedMarks = marks.sorted
            println(sortedMarks)
            """),

        GuideFor(["logic-scala-option-get"], "A value taken out of an Option without checking",
            "An Option holds a value - Some(value) - or nothing - None. A Map's get gives back None for a key that is not there, and " +
            "find gives back None when nothing matches. .get takes the value out without checking which it is, so when it is a None, " +
            "the program stops with NoSuchElementException: None.get.",
            "The program crashes the first time the key is missing, or nothing matches.",
            "Use getOrElse with a value for when there is none, or match on Some and None.",
            """
            val age = ages.getOrElse("Bob", 0)
            """),
    ];
}
