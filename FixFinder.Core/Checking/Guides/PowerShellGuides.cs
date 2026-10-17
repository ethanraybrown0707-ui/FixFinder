using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// What PowerShell's parser says of a script before it runs, and the errors a script prints as it runs - each explained
/// for someone new to programming.
/// </summary>
/// <remarks>
/// Every error id matched here is one Windows PowerShell 5.1 was seen to give. Every example is a whole script, and
/// PowerShellLiveTests checks each one with PowerShell's parser and runs it, and fails on any error it prints.
/// </remarks>
internal static class PowerShellGuides
{
    private const string NothingRuns = "PowerShell reads the whole script before it runs any of it, so while this is wrong none of the script runs.";

    private const string CarriesOn =
        "PowerShell prints the error and carries on with the next line, without what this one would have done - so what the script " +
        "prints or works out from here can be wrong.";

    private static GuideEntry Refused(string errorId, string explanation, string fix, string example) => new()
    {
        Codes = [errorId],
        Guide = new MistakeGuide(explanation, NothingRuns, fix, example),
    };

    private static GuideEntry Printed(string errorId, string? pattern, string explanation, string fix, string example) => new()
    {
        Codes = [errorId],
        Message = pattern is null ? null : new Regex(pattern),
        Guide = new MistakeGuide(explanation, CarriesOn, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Refused("MissingEndCurlyBrace",
            "A { was opened and not closed, so PowerShell reached the end of the script still waiting for its }. PowerShell takes each } " +
            "to close the latest {, so when an inner block's } is the one missing, the line it names is the outer block's.",
            "Close each block with } where its lines finish. Indenting each block's lines shows which one is missing its }.",
            """
            $marks = 70, 80
            foreach ($mark in $marks) {
                if ($mark -gt 75) {
                    Write-Output $mark
                }
            }
            """),

        Refused("MissingStatementBlockAfterElse",
            "After else, PowerShell wants a block in { }. \"else if\" is written as one word in PowerShell, elseif.",
            "Write elseif.",
            """
            $mark = 50
            if ($mark -gt 70) {
                Write-Output "high"
            } elseif ($mark -gt 40) {
                Write-Output "pass"
            }
            """),

        Refused("RedirectionNotSupported",
            "< and > are not comparisons in PowerShell: they move output to and from files, and < is not yet used at all. PowerShell compares with words.",
            "Compare with -lt, -le, -gt and -ge.",
            """
            $mark = 50
            if ($mark -lt 40) {
                Write-Output "fail"
            }
            """),

        Refused("InvalidEndOfLine",
            "&& and || join commands in some shells and comparisons in other languages, and Windows PowerShell has neither. It joins conditions with words.",
            "Join conditions with -and and -or.",
            """
            $mark = 50
            if ($mark -gt 40 -and $mark -lt 70) {
                Write-Output "middle"
            }
            """),

        Refused("UnexpectedToken",
            "PowerShell found something where it cannot go - often a comparison written as another language writes it, such as != or >=, " +
            "which PowerShell writes as -ne and -ge.",
            "Compare with -eq, -ne, -lt, -le, -gt and -ge, and check the line's brackets are each closed.",
            """
            $mark = 50
            if ($mark -ne 40) {
                Write-Output "not forty"
            }
            """),

        Refused("MissingEndParenthesisInExpression",
            "A ( was opened and not closed on its line, so PowerShell read on past the line's end looking for its ).",
            "Close the bracket where what is inside it ends - often at the end of the same line.",
            """
            $total = (70 + 80)
            Write-Output $total
            """),

        new GuideEntry
        {
            RuleIds = ["powershell-missing-dollar"],
            Guide = new MistakeGuide(
                "Every PowerShell variable's name starts with $. Without it, PowerShell takes the name for a command - a cmdlet, a function " +
                "or a program - looks for one called that, and finds none.",
                CarriesOn,
                "Put the $ before the variable's name: $total = ....",
                """
                $marks = @(70, 80)
                $total = ($marks | Measure-Object -Sum).Sum
                Write-Output $total
                """),
        },

        Refused("TerminatorExpectedAtEndOfString",
            "A piece of text was started with a quote that is never closed, so PowerShell took the rest of the script as text.",
            "Close the text with the same kind of quote it opened with.",
            """
            $name = "Ada"
            Write-Output $name
            """),

        Printed("CommandNotFoundException", null,
            "PowerShell has no command, function or program by this name - it is spelt differently, or the function is defined further " +
            "down the script than this line. When the name is =, the line has ==, which PowerShell writes as -eq.",
            "Check the spelling against the command's own - Get-Command lists them - and define a function above the line that uses it.",
            """
            $total = 10
            Write-Output $total
            """),

        Printed("InvokeMethodOnNull", null,
            "A method was called on a variable that holds $null - nothing - because it was never given a value, or was given what a command " +
            "returned when it found nothing.",
            "Check the variable holds something before calling a method on it: if ($null -ne $name) { ... }.",
            """
            $name = $null
            if ($null -ne $name) {
                Write-Output $name.ToUpper()
            } else {
                Write-Output "No name"
            }
            """),

        Printed("RuntimeException", @"^Attempted to divide by zero",
            "Dividing by zero has no answer. The number divided by is zero at this point - often the count of an empty list.",
            "Check the number is not zero before dividing by it, and decide what the answer should be when it is.",
            """
            $marks = @()
            $average = $(if ($marks.Count -eq 0) { 0 } else { 100 / $marks.Count })
            Write-Output $average
            """),

        Printed("InvalidCastFromStringToInteger", null,
            "Text that is not a number was used as one - by [int], or by + with a number on its left, which turns the right into a number too.",
            "Check text is a number before using it as one: $typed -as [int] gives $null when it is not. Put a number into text with \"$total marks\".",
            """
            $typed = "twenty"
            $age = $typed -as [int]
            if ($null -eq $age) {
                Write-Output "Please type a number"
            } else {
                Write-Output ($age + 1)
            }
            """),

        Printed("PathNotFound", null,
            "The file or folder named does not exist - the name is spelt differently, or it is looked for in another folder from the one it is in. " +
            "A name that is not a full path is looked for in the folder the script runs in.",
            "Check the name and where the file is, or check with Test-Path first.",
            """
            if (Test-Path -Path "marks.txt") {
                Get-Content -Path "marks.txt"
            } else {
                Write-Output "No marks.txt here"
            }
            """),

        Printed("NotSupportedException", @"Collection was of a fixed size",
            "An array made with @() or commas has a fixed size, so Add cannot put anything in it.",
            "Add to it with +=, which makes a new array with the item on the end.",
            """
            $marks = @()
            $marks += 70
            $marks += 80
            Write-Output $marks.Count
            """),

        Printed("VariableIsUndefined", null,
            "With Set-StrictMode on, PowerShell refuses a variable that was never given a value - often one spelt differently from where it was set.",
            "Spell it as it was set, or give it a value before this line.",
            """
            Set-StrictMode -Version Latest
            $total = 10
            Write-Output ($total + 1)
            """),
    ];
}
