using System.Text.RegularExpressions;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// What F# says of a script as it checks it, and the exceptions that stop one - each explained for someone new to
/// programming.
/// </summary>
/// <remarks>
/// Every message matched here is one F# Interactive 15.2 (F# 10) was seen to give, in its own words. Every example is a whole
/// script, and FSharpLiveTests checks each one with dotnet fsi --typecheck-only and runs it, and fails on anything F# says.
/// </remarks>
internal static class FSharpGuides
{
    private const string NothingRuns = "F# checks the whole script before it runs any of it, so while this error is there no part of the script runs.";

    private const string Stops = "The script stops at this line, and nothing after it runs.";

    private const string Runs = "F# only warns, and runs the script - but this is very likely a mistake, or code that does nothing.";

    private static GuideEntry Refused(string code, string? pattern, string explanation, string fix, string example) =>
        Entry([code], FSharpParser.CompileError, pattern, explanation, NothingRuns, fix, example);

    private static GuideEntry Warned(string code, string? pattern, string explanation, string fix, string example) =>
        Entry([code], FSharpParser.Warning, pattern, explanation, Runs, fix, example);

    private static GuideEntry Stopped(string type, string? pattern, string explanation, string fix, string example) =>
        Entry([], type, pattern, explanation, Stops, fix, example);

    private static GuideEntry Entry(string[] codes, string type, string? pattern, string explanation, string consequence, string fix, string example) => new()
    {
        Codes = codes,
        ExceptionTypes = [type],
        Message = pattern is null ? null : new Regex(pattern),
        Guide = new MistakeGuide(explanation, consequence, fix, example),
    };

    private static GuideEntry ByRule(string ruleId, string explanation, string consequence, string fix, string example) => new()
    {
        RuleIds = [ruleId],
        Guide = new MistakeGuide(explanation, consequence, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        ByRule("fsharp-missing-rec",
            "A function that calls itself has to be defined with let rec - rec for recursive. With plain let, the function's name is " +
            "only defined once its whole body has been read, so inside its own body the name means nothing yet.",
            NothingRuns,
            "Write let rec in place of let on the function's first line.",
            """
            let rec factorial n =
                if n <= 1 then 1 else n * factorial (n - 1)

            printfn "%d" (factorial 5)
            """),

        ByRule("fsharp-assign-with-arrow",
            "In F# = compares two values; it never changes one. count = count + 1 works out whether count equals count + 1 - false - " +
            "and F# throws that answer away, so count keeps its old value. A mutable value is given a new one with <-.",
            Runs,
            "Write <- in place of = to give the value its new one: count <- count + 1.",
            """
            let countUp () =
                let mutable count = 0
                count <- count + 1
                count

            printfn "%d" (countUp ())
            """),

        Refused("FS0039", @"is not defined\.",
            "F# knows nothing by this name at this point in the script. It is spelt differently where it is defined, it is defined " +
            "further down - F# reads a script from the top, and a name can only be used below the line that defines it - or it " +
            "belongs to a module whose name is missing, as length belongs to List: List.length.",
            "Check the spelling against where the name is defined - F# often suggests the name it thinks was meant - and define it " +
            "above the line that uses it.",
            """
            let total = 150
            let count = 2
            printfn "%d" (total / count)
            """),

        Refused("FS0027", null,
            "A value given with let keeps the value it was given: F# values do not change unless they are declared mutable. <- gives " +
            "a value a new one, so the value it is used on has to be mutable.",
            "Declare the value with let mutable where it is first given its value.",
            """
            let mutable count = 0
            count <- count + 1
            printfn "%d" count
            """),

        Refused("FS0043", @"does not support the operator '(?:!=|==)'",
            "F# writes \"not equal\" as <> and \"equal\" as a single =. != and == are not F# operators, so F# looked for an operator " +
            "by that name for the values being compared and found none.",
            "Write <> for not equal, and = for equal.",
            """
            let mark = 70
            if mark <> 100 then printfn "not full"
            if mark = 70 then printfn "seventy"
            """),

        Refused("FS0748", null,
            "An F# function gives back the value of its last expression, with no word in front of it. return belongs to computation " +
            "expressions, such as async { }, and nowhere else.",
            "Take return away, and leave the value as the function's last line.",
            """
            let double x =
                x * 2

            printfn "%d" (double 4)
            """),

        Refused("FS3123", null,
            "An F# for loop is written for ... in ... do - or for ... = ... to ... do - and do is what starts the loop's body. Without it, " +
            "F# cannot tell where the loop's heading ends.",
            "End the for line with do.",
            """
            let marks = [70; 80]
            for mark in marks do
                printfn "%d" mark
            """),

        Refused("FS0589", null,
            "An F# if is written if ... then, and then is what ends the condition. Without it, F# reads on into the next line as more of " +
            "the condition, and finds no then.",
            "End the condition with then.",
            """
            let mark = 70
            if mark > 50 then
                printfn "pass"
            """),

        Refused("FS0001", @"Consider using 'not expr' here\.",
            "F# writes \"not\" as the word not. ! in front of a value reads what a ref cell holds - a box with a value in it - so F# " +
            "expected a ref cell there, and found a true-or-false value.",
            "Write not in place of !: if not finished then.",
            """
            let finished = false
            if not finished then printfn "keep going"
            """),

        Refused("FS0001", @"arising from the use of a printf-style format string",
            "Each % in a printf format stands for one of the values after it, in order, and its letter says what kind of value it " +
            "takes: %d a whole number, %f a decimal number, %s text, %b true or false, %c a character, and %A any value at all. A " +
            "value of another kind does not fit.",
            "Make each % the one for the kind of value it stands for - or write the value with %A.",
            """
            let name = "Ada"
            let average = 72.5
            printfn "%s has an average of %f" name average
            """),

        Refused("FS0001", @"^This expression was expected to have type 'string' but here has type '(?:int|int64|float|decimal|bool|char)'$",
            "F# expected text here and found a number, or another kind of value. In a printf format, %s takes text only - and F# " +
            "turns nothing into text by itself.",
            "Use the % for the kind of value - %d for a whole number, %f for a decimal number - or turn the value into text with string.",
            """
            let mark = 70
            printfn "Mark: %d" mark
            printfn "%s" ("Mark: " + string mark)
            """),

        Refused("FS0001", @"^This 'if' expression is missing an 'else' branch\.",
            "In F# an if gives back a value, like a calculation does - so when its value is used, it needs an else saying what the " +
            "value is when the condition is false. Without one, F# would have nothing to give back then.",
            "Add an else that gives back a value of the same kind as the then.",
            """
            let mark = 70
            let grade = if mark >= 50 then "pass" else "fail"
            printfn "%s" grade
            """),

        Refused("FS0001", @"^The type '(?:int|int64|float|decimal|bool|char)' does not match the type 'string'$",
            "+ joins two pieces of text, or adds two numbers - and in F# both sides have to be the same type. F# turns nothing into " +
            "text by itself, so text + a number does not fit together.",
            "Turn the number into text first with string: \"Mark: \" + string mark.",
            """
            let mark = 70
            printfn "%s" ("Mark: " + string mark)
            """),

        Refused("FS0001", @"^The type '(?:float|int)' does not match the type '(?:int|float)'$",
            "F# never mixes whole numbers and decimal numbers on its own: 7 / 2.5 is a whole number divided by a decimal, and F# will " +
            "not work it out. Both sides of +, -, * and / have to be the same type.",
            "Turn the whole number into a decimal with float - float total / 2.5 - or the decimal into a whole number with int.",
            """
            let total = 10
            let average = float total / 2.5
            printfn "%.1f" average
            """),

        Refused("FS0001", @"but is a tuple of type",
            "An F# function defined as let area width height takes its arguments one after another, separated by spaces: area 3 4. " +
            "Brackets and commas join values into a tuple - one value made of several - so area(3, 4) gives area a single argument, " +
            "the pair (3, 4), where it needs a number.",
            "Give the arguments with spaces between them, and no commas: area 3 4. An argument that is more than a name or a number " +
            "goes in brackets of its own: area (width + 1) 4.",
            """
            let area (width: int) (height: int) = width * height
            let result = area 3 4
            printfn "%d" result
            """),

        Refused("FS0001", @"Operator '\+' cannot be applied to a tuple type\.",
            "A comma joins values into a tuple - one value made of several. In a list, [70, 80, 90] is one item, the three numbers " +
            "together, so adding up the list adds up tuples, which F# cannot do. The items of an F# list are separated by ;.",
            "Separate the items with ;: [70; 80; 90].",
            """
            let marks = [70; 80; 90]
            printfn "%d" (List.sum marks)
            """),

        Refused("FS0001", null,
            "Two kinds of value meet where only one fits: F# works out the type of every value, and here a value is not the type " +
            "its place needs - text where a number is needed, a list where a single value is, or a function given too few arguments.",
            "Read the two types F# names, and change the value - or what it is given to - so the two match.",
            """
            let width = 3
            let height = 4
            let area = width * height
            printfn "%d" area
            """),

        Refused("FS0583", null,
            "Every bracket in F# is closed by its partner - ( by ), [ by ], [| by |] - and this one is closed nowhere, so F# cannot " +
            "tell where what is inside it ends.",
            "Close the bracket where what is inside it ends - often at the end of the same line.",
            """
            let marks = [70; 80; 90]
            printfn "%d" (List.sum marks)
            """),

        Refused("FS0010", null,
            "F# could not read the code at this point. F# reads a script by its indentation - the lines inside a function, a loop or " +
            "an if are indented further than the line that starts them, and lined up with each other - so a line out of line, a " +
            "bracket or a quote left open, or a word where it cannot go, stops it here or just after.",
            "Line up the lines of each block, and close every bracket and quote that is opened.",
            """
            let total marks =
                let mutable sum = 0
                for mark in marks do
                    sum <- sum + mark
                sum

            printfn "%d" (total [70; 80])
            """),

        Warned("FS0025", null,
            "A match has no case for some values it can be given - F# names one of them. Given that value, none of the cases fits, and " +
            "the script stops with MatchFailureException.",
            "Add a case for the values left over - | _ -> matches anything not matched above it.",
            """
            let describe mark =
                match mark with
                | 100 -> "full"
                | 0 -> "none"
                | _ -> "some"

            printfn "%s" (describe 50)
            """),

        Warned("FS0020", null,
            "A value worked out on a line of its own is thrown away. F#'s lists never change, so List.sort marks or List.map ... marks " +
            "makes a new list - and on a line of its own, the new list is lost and marks is as it was.",
            "Keep the value with let - let sortedMarks = List.sort marks - and use that, or end the function with it.",
            """
            let raise marks =
                List.map (fun mark -> mark + 5) marks

            printfn "%A" (raise [70; 80])
            """),

        Warned("FS3886", null,
            "In F# the items of a list are separated by ;, and a comma joins values into a tuple. [70, 80, 90] is a list of one item - " +
            "the three numbers together - not a list of three numbers.",
            "Separate the items with ;: [70; 80; 90].",
            """
            let marks = [70; 80; 90]
            printfn "%A" marks
            """),

        Stopped("System.DivideByZeroException", null,
            "A whole number was divided by zero, which has no answer. A decimal divided by zero gives infinity, and stops nothing - " +
            "but / and % on whole numbers stop the script.",
            "Check the number you divide by before dividing, and give an answer for when it is zero.",
            """
            let average total count = if count = 0 then 0 else total / count
            let marks : int list = []
            printfn "%d" (average (List.sum marks) (List.length marks))
            """),

        Stopped("System.IndexOutOfRangeException", null,
            "The script read a position an array - or a piece of text - does not have. An array's positions run from 0 to its Length - 1, " +
            "so a loop to marks.Length reads one past the end on its last pass.",
            "Stop the loop at the last position - for index in 0 .. marks.Length - 1 do - or go over the items themselves: for mark in marks do.",
            """
            let marks = [|70; 80; 90|]
            let mutable total = 0
            for index in 0 .. marks.Length - 1 do
                total <- total + marks.[index]
            printfn "%d" total
            """),

        Stopped("System.ArgumentException", @"^The index was outside the range of elements in the list\.",
            "List.item - or list.[i] - was asked for a position the list does not have. A list's positions run from 0 to its length - 1.",
            "Ask only for positions below List.length - or go over the items themselves with for mark in marks do.",
            """
            let marks = [70; 80; 90]
            printfn "%d" (List.item 2 marks)
            """),

        Stopped("System.ArgumentException", @"^The input list was empty\.",
            "List.head - the first item - or another function that needs at least one item was given an empty list, which has none.",
            "Check that the list has items first - if List.isEmpty marks then ... - or use List.tryHead, which gives None for an empty list.",
            """
            let marks : int list = []
            match List.tryHead marks with
            | Some first -> printfn "%d" first
            | None -> printfn "no marks"
            """),

        Stopped("System.Collections.Generic.KeyNotFoundException", null,
            "The script looked up a key the map - or dictionary - does not have: looking one up with .[key] stops the script when the " +
            "key is not there.",
            "Use Map.tryFind, which gives None when the key is not there - or check first with Map.containsKey.",
            """
            let ages = Map.ofList [("Ada", 36)]
            match Map.tryFind "Bob" ages with
            | Some age -> printfn "%d" age
            | None -> printfn "no age for Bob"
            """),

        Stopped("System.FormatException", null,
            "int was given text that is not a whole number - \"seven\", \"7.5\", or nothing at all - and could not turn it into one.",
            "Check the text first: System.Int32.TryParse gives true and the number when the text is one, and false when it is not.",
            """
            let text = "seven"
            match System.Int32.TryParse text with
            | true, number -> printfn "%d" number
            | _ -> printfn "%s is not a whole number" text
            """),

        Stopped("System.ArgumentNullException", null,
            "A value that was not there - null - was given where one is needed. stdin.ReadLine() gives null when there is nothing more " +
            "to read, and int null stops the script.",
            "Check for null before using the line: match line with | null -> ... | typed -> ....",
            """
            let line = stdin.ReadLine()
            match line with
            | null -> printfn "nothing was typed"
            | typed -> printfn "%s" typed
            """),

        Stopped("Microsoft.FSharp.Core.MatchFailureException", null,
            "None of a match's cases fitted the value it was given. F# warns of this as it checks the script (FS0025), naming a value " +
            "with no case.",
            "Add a case for the values left over - | _ -> matches anything not matched above it.",
            """
            let describe mark =
                match mark with
                | 100 -> "full"
                | 0 -> "none"
                | _ -> "some"

            printfn "%s" (describe 50)
            """),

        Stopped("System.NullReferenceException", null,
            "The script used something that is null - not there at all - as if it were: reading its Length, or calling one of its methods.",
            "Check for null before using it, or give it a value where it is made.",
            """
            let name : string = "Ada"
            printfn "%d" name.Length
            """),

        Stopped(FSharpParser.StackOverflow, null,
            "A function called itself over and over without stopping, until there was no room left for more calls. A function that " +
            "calls itself needs a case that does not - a base case - which each call comes closer to.",
            "Give the function a case that does not call itself, and make each call come closer to it.",
            """
            let rec countDown n =
                if n = 0 then 0 else countDown (n - 1)

            printfn "%d" (countDown 10)
            """),

        Stopped("System.Exception", null,
            "The script stopped itself: failwith - or raise - stops a script with the message it is given. The message is the script's own.",
            "Read the message: it says what the script found wrong. Then change what the script was given, or what it does about it.",
            """
            let check mark = if mark < 0 then failwith "mark below zero" else mark
            printfn "%d" (check 70)
            """),
    ];
}
