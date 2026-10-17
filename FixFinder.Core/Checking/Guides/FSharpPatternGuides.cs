using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic checks of F# scripts, each explained for someone new to programming. Every example is a whole script,
/// and FSharpLiveTests checks each one with dotnet fsi --typecheck-only and runs it, and fails on anything F# says.
/// </summary>
internal static class FSharpPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-fsharp-comparison-for-assignment"], "= used to give a new value",
            "In F# = compares two values; it never changes one. total = total + 70 works out whether total equals total + 70 - false - " +
            "and on a line of the script's own, F# throws that answer away without a word.",
            "total keeps its old value, with no error, so everything after it works with the old one.",
            "Give a mutable value its new one with <-: total <- total + 70.",
            """
            let mutable total = 0
            total <- total + 70
            printfn "%d" total
            """),

        GuideFor(["logic-fsharp-result-discarded"], "A new list made and thrown away",
            "F#'s lists never change. List.sort marks makes a new, sorted list and leaves marks as it was - and on a line of the " +
            "script's own, F# throws the new list away without a word.",
            "marks is as it was, with no error, so whatever reads it next works with the unsorted list.",
            "Keep the new list with let under a name of its own - F# will not give marks a second value at the top of a script - and " +
            "read that name after it.",
            """
            let marks = [80; 70; 90]
            let sortedMarks = List.sort marks
            printfn "%A" sortedMarks
            """),

        GuideFor(["logic-fsharp-whole-number-division"], "Whole numbers divided before they become decimals",
            "/ divides two whole numbers as whole numbers, and drops what is left over: 7 / 2 is 3. float (total / count) makes the " +
            "answer a decimal only after that, so float 3 is 3.0 - the half is already gone.",
            "The average, or whatever is worked out, is too small, with no error.",
            "Make each number a decimal before dividing: float total / float count is 3.5.",
            """
            let total = 7
            let count = 2
            let average = float total / float count
            printfn "%.1f" average
            """),

        GuideFor(["logic-fsharp-loop-to-length"], "A loop that runs one past the end",
            "An array's positions run from 0 to its Length - 1. for index in 0 .. marks.Length do - and for index = 0 to marks.Length do - " +
            "count to marks.Length itself, one more than the last position.",
            "On the last pass marks.[index] is one past the end, and the script stops with System.IndexOutOfRangeException.",
            "Stop at the last position - for index in 0 .. marks.Length - 1 do - or go over the items themselves: for mark in marks do.",
            """
            let marks = [|70; 80; 90|]
            let mutable total = 0
            for index in 0 .. marks.Length - 1 do
                total <- total + marks.[index]
            printfn "%d" total
            """),

        GuideFor(["logic-fsharp-float-equality"], "Decimals compared exactly",
            "Decimal numbers are stored in binary, which cannot hold most decimals exactly, so arithmetic on them leaves a tiny error: " +
            "0.1 + 0.2 is 0.30000000000000004.",
            "Compared exactly with = the two are not equal, with no error, though they are for every practical purpose.",
            "Compare the difference with a tiny tolerance: abs (price - 0.3) < 1e-9.",
            """
            let price = 0.1 + 0.2
            if abs (price - 0.3) < 1e-9 then printfn "equal" else printfn "not equal"
            """),

        GuideFor(["logic-fsharp-input-compared-as-text"], "A number typed in, compared as text",
            "stdin.ReadLine() gives the line typed as text, and a number in quotes - \"10\" - is text too. <, >, <= and >= put two pieces " +
            "of text in order letter by letter, as a dictionary does, so \"7\" > \"10\" is true: \"7\" comes after \"1\".",
            "The comparison gives the wrong answer for many numbers, with no error.",
            "Turn the line into a number with int, and compare it with the number itself: int answer > 10.",
            """
            let answer = "7"
            if int answer > 10 then printfn "big" else printfn "small"
            """),

        GuideFor(["logic-fsharp-text-without-dollar"], "Braces in F# text with no $",
            "F# puts a value into text only when the text is interpolated - when a $ comes before its opening quote, as in " +
            "$\"Hello, {name}\". Without the $, the braces are ordinary characters: printfn \"Hello, {name}\" prints Hello, {name}, " +
            "braces and all. dotnet fsi says nothing, as the text is still text.",
            "The output shows {name} instead of the name, which looks broken to anyone using the program.",
            "Put a $ before the opening quote. Text written for printfn's own placeholders - printfn \"Hello, %s\" name - has its " +
            "values after it, and needs no $.",
            """
            let name = "Ada"
            printfn $"Hello, {name}"
            """),

        GuideFor(["logic-fsharp-nan-comparison"], "Comparing with NaN",
            "NaN means 'not a number' - a value a calculation gives when it has no answer as a number. By the rules, " +
            "NaN is not equal to anything, not even itself, so value = nan is false even when value is NaN. dotnet " +
            "fsi says nothing.",
            "A comparison with = is always false, and with <> always true, so a value that is NaN is never noticed.",
            "Test with System.Double.IsNaN value.",
            """
            let value = nan
            if System.Double.IsNaN value then printfn "not a number"
            """),
    ];
}
