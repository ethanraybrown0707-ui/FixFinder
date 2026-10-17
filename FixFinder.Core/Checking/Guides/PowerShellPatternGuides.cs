using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic checks of PowerShell scripts, each explained for someone new to programming. Every example is a
/// whole script, and PowerShellLiveTests checks each one with PowerShell's parser and runs it, and fails on any error it prints.
/// </summary>
internal static class PowerShellPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-powershell-greater-than-writes-a-file"], "A > that writes a file",
            "> in PowerShell does not compare: it sends what is on its left to a file named by what is on its right. In an if, " +
            "$mark > 40 writes the mark to a file called 40, and the condition is then nothing.",
            "The if is false every time, whatever the mark, and a stray file appears in the folder the script runs in.",
            "Compare with -gt - and -lt, -ge, -le, -eq and -ne for the others.",
            """
            $mark = 50
            if ($mark -gt 40) {
                Write-Output "pass"
            }
            """),

        GuideFor(["logic-powershell-assignment-in-condition"], "A condition that stores a value",
            "= gives the variable on its left a value; -eq compares. In an if, = stores the value, and the condition is then that value - " +
            "true for any number but 0.",
            "The if takes the same way every time, whatever the variable held - and the variable's own value is lost.",
            "Compare with -eq.",
            """
            $mark = 70
            if ($mark -eq 40) {
                Write-Output "forty"
            }
            Write-Output $mark
            """),

        GuideFor(["logic-powershell-loop-to-count"], "A loop that runs one past the end",
            "An array's positions start at 0, so an array of 3 items has positions 0, 1 and 2. A loop that goes on while $i -le " +
            "$marks.Count takes one step more than that.",
            "PowerShell gives $null for a position past the end, with no error, so the last pass works with nothing.",
            "Loop while $i -lt $marks.Count, or go through the items themselves: foreach ($mark in $marks).",
            """
            $marks = 70, 80, 90
            for ($i = 0; $i -lt $marks.Count; $i++) {
                Write-Output ("Mark: " + $marks[$i])
            }
            """),

        GuideFor(["logic-powershell-function-called-with-brackets"], "A function called with brackets and commas",
            "PowerShell calls a function by its name and then its values, apart: Area 3 4. Written Area(3, 4), as other languages write " +
            "it, the brackets and comma make one value, the list 3, 4, which goes to the first parameter alone.",
            "The function works with a list where it wanted a number, and with nothing for its other parameters.",
            "Call it with its values after its name, separated by spaces: Area 3 4.",
            """
            function Area($width, $height) {
                return $width * $height
            }
            Write-Output (Area 3 4)
            """),

        GuideFor(["logic-powershell-read-host-compared-as-text"], "Typed text compared with a number",
            "Read-Host gives back what was typed as text, even when it is a number. PowerShell compares as its left side is, so text is " +
            "compared with -gt as text, character by character: \"7\" -gt 17 compares \"7\" with \"17\", and as 7 comes after 1, it is true.",
            "The comparison gives the wrong answer for many numbers, with no error.",
            "Read the text as a whole number: $age = [int](Read-Host \"Age\").",
            """
            $age = [int]"7"
            if ($age -gt 17) {
                Write-Output "adult"
            } else {
                Write-Output "child"
            }
            """),

        GuideFor(["logic-powershell-function-output-returned"], "Text a function writes comes back with its result",
            "A PowerShell function gives back everything it outputs, not only what return names. Write-Output outputs its text, so the " +
            "text comes back as part of the function's result, beside the value.",
            "The value kept from the function holds the text too, so arithmetic on it gives a list rather than a number.",
            "Write text meant for the screen with Write-Host, which outputs nothing.",
            """
            function Get-Total($marks) {
                Write-Host "Adding up the marks"
                $sum = 0
                foreach ($mark in $marks) { $sum += $mark }
                return $sum
            }

            $total = Get-Total @(70, 80)
            Write-Output ($total + 1)
            """),

        GuideFor(["logic-powershell-property-in-text"], "A property inside double quotes",
            "In double quotes PowerShell puts in a variable's value - but only the variable's: in \"$marks.Count\" it puts in $marks, " +
            "and writes .Count after it as it is. The same goes for $marks[0].",
            "The text holds the whole of the variable followed by the words, with no error - 70 80 90.Count.",
            "Put the whole expression inside $( ): \"$($marks.Count)\" puts in the value of everything between its brackets.",
            """
            $marks = @(70, 80, 90)
            Write-Output "Marks counted: $($marks.Count)"
            """),

        GuideFor(["logic-powershell-variable-in-single-quotes"], "A variable inside single quotes",
            "PowerShell puts a variable's value into text only in double quotes. Text in single quotes is written exactly as it is, " +
            "$name and all.",
            "The text holds the variable's name rather than its value, with no error.",
            "Put the text in double quotes: \"Hello, $name\".",
            """
            $name = "Ada"
            Write-Output "Hello, $name"
            """),

        GuideFor(["logic-powershell-nan-comparison"], "Comparing with NaN",
            "NaN means 'not a number' - a value a calculation gives when it has no answer as a number. By the rules, " +
            "NaN is not equal to anything, not even itself, so $value -eq [double]::NaN is false even when value is " +
            "NaN. PowerShell says nothing.",
            "A comparison with -eq is always false, and with -ne always true, so a value that is NaN is never " +
            "noticed.",
            "Test with [double]::IsNaN($value).",
            """
            $value = [double]::NaN
            if ([double]::IsNaN($value)) {
                Write-Output "not a number"
            }
            """),
    ];
}
