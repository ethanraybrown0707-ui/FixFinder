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

        GuideFor(["logic-scala-array-printed"], "An Array printed as it is",
            "A List prints its items - List(70, 80, 90) - but an Array has no text of its own for them. Printed as it is - on its " +
            "own, joined to text with + or put into s\"...\" - it shows its kind and a code, such as [I@4563e9ab. Neither Scala " +
            "3.8.4 nor Scala 2.13.18 says anything of it.",
            "The output shows [I@ and a code instead of the items, which looks broken to anyone using the program.",
            "Print the items with mkString: marks.mkString(\", \") writes 70, 80, 90.",
            """
            @main def run(): Unit =
              val marks = Array(70, 80, 90)
              println(marks.mkString(", "))
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

        GuideFor(["logic-scala-text-without-s"], "$ in Scala text with no s",
            "Scala puts a value into text only through an interpolator - a letter before the opening quote, most often s, as in " +
            "s\"Hello, $name\". Without it, $name is ordinary characters, and is printed as it is written; so is ${...}. The compiler " +
            "does not read inside the text: the most it says is that the name is never used - Scala 3's warning E198, unused local " +
            "definition, or Scala 2.13's local val name is never used - and only when the text was the one place meant to use it.",
            "The output shows $name instead of the name, which looks broken to anyone using the program.",
            "Put an s before the opening quote.",
            """
            @main def run(): Unit =
              val name = "Ada"
              println(s"Hello, $name")
            """),

        GuideFor(["logic-scala-nan-comparison"], "Comparing with NaN",
            "NaN means 'not a number' - a value a calculation gives when it has no answer as a number. By the rules, " +
            "NaN is not equal to anything, not even itself, so value == Double.NaN is false even when value is NaN. " +
            "Neither Scala 3.8.4 nor Scala 2.13.18 says anything of it.",
            "A comparison with == is always false, and with != always true, so a value that is NaN is never noticed.",
            "Test with value.isNaN.",
            """
            @main def run(): Unit =
              val value = Double.NaN
              if value.isNaN then println("not a number")
            """),

        GuideFor(["logic-scala-constant-overflow"], "Whole numbers too big for an Int",
            "365 * 24 * 60 * 60 * 1000 is worked out in Ints, because every number in it is one - and an Int holds nothing above " +
            "2,147,483,647. The product, 31,536,000,000, wraps round to 1,471,228,928 before it is stored anywhere, even in a Long. " +
            "Scala says nothing.",
            "The number is wrong, and nothing says so: the milliseconds in a year come out as 1471228928.",
            "Make the first number a Long with an L - 365L * 24 * 60 * 60 * 1000 - so the arithmetic is done in Longs.",
            """
            @main def run(): Unit =
              val millisPerYear: Long = 365L * 24 * 60 * 60 * 1000
              println(millisPerYear)
            """),

        GuideFor(["logic-scala-regex-character-as-text"], "A character split on that means something else",
            "split, replaceAll and replaceFirst read their first text as a regular expression, as Java's do, where . is any character, " +
            "| is \"or\" and $ is the end of the text. So \"3.14\".split(\".\") gives no parts at all, and " +
            "\"3.14\".replaceAll(\".\", \",\") gives ,,,,. Scala says nothing.",
            "The text is split or replaced in the wrong places, with no error - a list of numbers read from a line comes back empty.",
            "Escape the character - split(\"\\\\.\") - so it means itself, or use replace, which takes its text as it is: " +
            "\"3.14\".replace(\".\", \",\") is 3,14.",
            """
            @main def run(): Unit =
              val parts = "3.14".split("\\.")
              println(parts.length)
            """),

        GuideFor(["logic-scala-numbers-joined-as-text"], "Numbers joined to text instead of added",
            "+ works from left to right. \"Total: \" + first comes first, and gives text - so the + after it joins second to that " +
            "text, rather than adding it to first. With 70 and 80 the line prints Total: 7080.",
            "The numbers are written side by side, not added, and the compiler says nothing of it.",
            "Put brackets round the numbers, so they are added first: \"Total: \" + (first + second) - or put the sum into the text: " +
            "s\"Total: ${first + second}\".",
            """
            @main def run(): Unit =
              val first = 70
              val second = 80
              println("Total: " + (first + second))
            """),

        GuideFor(["logic-scala-percent-divided-first"], "A percentage that divides first",
            "In Scala, / of two whole numbers gives a whole number - the part below 1 is thrown away. In score / total * 100 the " +
            "division comes first, so for 45 out of 60 it is 0, and 0 * 100 is 0.",
            "Every score below the total comes out as 0%, and the compiler says nothing of it.",
            "Multiply first - score * 100 / total - so only the last division drops a fraction: 45 * 100 / 60 is 75.",
            """
            @main def run(): Unit =
              val score = 45
              val total = 60
              println(score * 100 / total)
            """),

        GuideFor(["logic-scala-compare-to-one"], "compareTo compared with 1 or -1",
            "compareTo gives back a negative number when the first comes before the second, zero when they are equal, and a " +
            "positive number when it comes after - but not only -1 and 1. \"apple\".compareTo(\"cherry\") is -2, so == -1 is false " +
            "although apple does come first.",
            "The comparison is false for most pairs that do come first or after, and the compiler says nothing of it.",
            "Ask for the sign alone: < 0 for before, > 0 for after.",
            """
            @main def run(): Unit =
              println("apple".compareTo("cherry") < 0)
            """),
    ];
}
