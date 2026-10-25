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

        GuideFor(["logic-powershell-array-shared"], "A second name for an array is not a copy",
            "$backup = $marks does not copy the array: it gives the same array a second name. A change to an item " +
            "through either name - $marks[0] = 100 - is a change to the one array, so $backup shows it too. " +
            "PowerShell says nothing.",
            "The second name does not keep the values the array had: after $marks[0] = 100, $backup[0] prints 100.",
            "Make a copy: $backup = $marks.Clone(). The copy is a new array with the same items, and a change to one " +
            "no longer shows in the other.",
            """
            $marks = @(70, 80, 90)
            $backup = $marks.Clone()
            $marks[0] = 100
            Write-Output $backup[0]
            """),

        GuideFor(["logic-powershell-format-code-printed"], "A %d code that Write-Host does not fill in",
            "%d and %s are codes from C's printf: each marks where a value goes in the text. Write-Host writes each thing it is " +
            "given with a space between, and Write-Output each on a line of its own - neither fills %d in. PowerShell's own places " +
            "for values are {0}, {1} and on, and -f fills them in.",
            "The output shows the code as it is, with the value after it or below it, and PowerShell says nothing of it.",
            "Write {0} where the value goes, and fill it in with -f: Write-Host (\"Total: {0}\" -f $total).",
            """
            $total = 150
            Write-Host ("Total: {0}" -f $total)
            """),

        GuideFor(["logic-powershell-foreach-copy-changed"], "A loop that changes its own copy",
            "foreach gives each item of the array to the loop's variable as a copy of its own. Giving the variable a new value - " +
            "$mark += 5 - changes that copy, not the item in the array, so after the loop the array is just as it was.",
            "The array is unchanged, with no error, so whatever reads it next works with the old values.",
            "Loop over the positions and change the item at each: for ($index = 0; $index -lt $marks.Count; $index++), then " +
            "$marks[$index] += 5.",
            """
            $marks = @(70, 80, 90)
            for ($index = 0; $index -lt $marks.Count; $index++) {
                $marks[$index] += 5
            }
            Write-Output ($marks -join " ")
            """),

        GuideFor(["logic-powershell-numbers-joined-as-text"], "Numbers joined to text instead of added",
            "+ works from left to right. \"Total: \" + $first comes first, and gives text - so the + after it joins $second to that " +
            "text, rather than adding it to $first. With 70 and 80 the line prints Total: 7080.",
            "The numbers are written side by side, not added, and PowerShell says nothing of it.",
            "Put brackets round the numbers, so they are added first: \"Total: \" + ($first + $second).",
            """
            $first = 70
            $second = 80
            Write-Output ("Total: " + ($first + $second))
            """),

        GuideFor(["logic-powershell-plus-outside-brackets"], "A + given to Write-Host as a word",
            "Write-Host and Write-Output take each word after them as a thing to write - and a + standing on its own is a word too. " +
            "So Write-Host \"Total: \" + $total writes three things, with a space between: Total:  + 150. Write-Output writes each " +
            "on a line of its own.",
            "The output shows the + and the parts side by side rather than joined, and PowerShell says nothing of it.",
            "Put the sum in brackets, so it is worked out first and written as one value: Write-Host (\"Total: \" + $total).",
            """
            $total = 150
            Write-Host ("Total: " + $total)
            """),

        GuideFor(["logic-powershell-arguments-with-commas"], "A function's values separated by commas",
            "In PowerShell a comma makes a list. So Get-Area 3, 4 gives the function one value - the list 3, 4 - for its first " +
            "parameter, and nothing for its second. A function's values are separated by spaces, as a command's are.",
            "The function works with the wrong values - Get-Area 3, 4 gave back nothing at all - and PowerShell says nothing of it.",
            "Separate the values with spaces: Get-Area 3 4.",
            """
            function Get-Area($width, $height) {
                return $width * $height
            }
            Write-Output (Get-Area 3 4)
            """),

        GuideFor(["logic-powershell-plus-equals-reversed"], "=+ where += was meant",
            "$total =+ $mark is read as $total = +$mark: the = gives $total a new value, and the + is only a plus sign in front of $mark. " +
            "So $total becomes $mark itself, and what it held is lost. += adds to what is already there - and =- the same way gives " +
            "$total -$mark, where -= takes away.",
            "A running total ends as the last value given - 10, not 15, for 5 and 10 - and PowerShell says nothing.",
            "Write += to add, or -= to take away: $total += $mark",
            """
            $total = 0
            foreach ($mark in 5, 10) {
                $total += $mark
            }
            Write-Output $total
            """),

        GuideFor(["logic-powershell-odd-test-negative"], "An odd number looked for with % 2 -eq 1",
            "% gives what is left over after dividing, and it keeps the sign of the number divided: 7 % 2 is 1, but -7 % 2 is -1. So " +
            "$n % 2 -eq 1 is true only for odd numbers above 0 - a negative odd number leaves -1, and is taken for even.",
            "The script is right until it meets a negative odd number, which it treats as even, with no error.",
            "Ask whether anything is left over: $n % 2 -ne 0 is true for every odd number, negative or not.",
            """
            $n = -3
            if ($n % 2 -ne 0) {
                Write-Output "odd"
            }
            """),

        GuideFor(["logic-powershell-date-format-letters"], "A date format with the wrong letters",
            "In a date's format, the case of each letter matters: MM is the month, but mm is the minutes - so 9:30 on 5 February 2024 " +
            "with \"yyyy-mm-dd\" gives 2024-30-05. YYYY and DD are not letters a date's format knows, so they are printed as they are.",
            "The date is written wrongly, with no error.",
            "Use yyyy for the year, MM for the month and dd for the day: Get-Date -Format \"yyyy-MM-dd\".",
            """
            $whenever = Get-Date -Year 2024 -Month 2 -Day 5 -Hour 9 -Minute 30
            Write-Output (Get-Date $whenever -Format "yyyy-MM-dd")
            """),

        GuideFor(["logic-powershell-misspelt-name"], "A misspelt name given a value",
            "In PowerShell, giving a value to a variable makes it if it does not exist yet. So $totl = $total + $mark, with $total " +
            "misspelt, makes a new variable called $totl, and $total never changes. (Names are the same whatever their case: $Total " +
            "and $total are one variable.)",
            "The variable that was meant keeps its old value - a total stays 0 - with nothing said.",
            "Give the value to the name that was meant: $total = $total + $mark",
            """
            $total = 0
            foreach ($mark in 5, 10, 20) {
                $total = $total + $mark
            }
            Write-Output $total
            """),
    ];
}
