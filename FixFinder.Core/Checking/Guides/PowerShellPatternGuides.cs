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
    ];
}
