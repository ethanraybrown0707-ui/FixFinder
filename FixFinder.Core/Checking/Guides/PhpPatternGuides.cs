using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic checks of PHP programs, each explained for someone new to programming. Every example is a whole
/// program, and PhpLiveTests checks each one with php -l and runs it, and fails on anything PHP says.
/// </summary>
internal static class PhpPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-php-loop-to-count"], "A loop that runs one past the end",
            "An array's positions start at 0, so an array of 3 items has positions 0, 1 and 2. A loop that keeps going while " +
            "$i <= count($marks) takes one step more than that, and on its last step $marks[$i] asks for a key that is not there.",
            "PHP only warns, Undefined array key, and carries on with null - so the last pass works with a value that is not there.",
            "Loop while $i < count($marks), or loop over the items themselves: foreach ($marks as $mark).",
            """
            <?php
            $marks = [70, 80, 90];
            for ($i = 0; $i < count($marks); $i++) {
                echo $marks[$i] . "\n";
            }
            """),

        GuideFor(["logic-php-assignment-in-condition"], "A condition that stores a value",
            "= stores the value on its right in the variable on its left; == compares the two. In an if, = stores the value and then " +
            "asks whether that value is true - which for a number other than 0, or text other than \"\" and \"0\", it always is.",
            "The if takes the same way every time, whatever the variable held - and the variable's own value is lost.",
            "Compare with ==, or === to compare the type too.",
            """
            <?php
            $mark = 70;
            if ($mark == 40) {
                echo "forty\n";
            }
            echo $mark;
            """),

        GuideFor(["logic-php-float-equality"], "Decimal numbers compared exactly",
            "Computers store fractions like 0.1 in binary, where most of them can only be stored approximately - the way 1/3 can only be " +
            "written as 0.333... in decimal. Arithmetic on them leaves a tiny error, so 0.1 + 0.2 is 0.30000000000000004, and " +
            "0.1 + 0.2 == 0.3 is false.",
            "An exact comparison fails even when the numbers are equal for every practical purpose, so the code it guards never runs.",
            "Check that the difference is smaller than a tiny tolerance: abs($total - 0.3) < 1e-9.",
            """
            <?php
            $total = 0.1 + 0.2;
            if (abs($total - 0.3) < 1e-9) {
                echo "equal";
            }
            """),

        GuideFor(["logic-php-search-compared-with-false"], "A search compared with == false",
            "strpos gives the position where it found what it looked for, counting from 0 - so a match at the very start is 0 - and " +
            "false when it found nothing. == does not compare types, and counts 0 as equal to false, so strpos(...) == false is true " +
            "both when nothing was found and when it was found at the very start. stripos, strrpos and array_search do the same.",
            "A match at the very start is read as no match at all, with no error.",
            "Compare with === false, or !== false, which only false itself matches.",
            """
            <?php
            $word = "apple";
            if (strpos($word, "a") === false) {
                echo "no a\n";
            } else {
                echo "has an a\n";
            }
            """),

        GuideFor(["logic-php-caret-as-power"], "^ used as a power",
            "In maths side^2 means side squared, but in PHP ^ is exclusive or: it compares two whole numbers bit by bit " +
            "and keeps the bits that differ. 5 is 101 in binary and 2 is 010, so 5 ^ 2 is 111 - which is 7, not 25.",
            "The program runs and works out a number - just the wrong one - and neither php -l nor PHP says anything of it.",
            "Use PHP's power, **: $side ** 2.",
            """
            <?php
            $side = 5;
            echo $side ** 2;
            """),

        GuideFor(["logic-php-power-of-one-over"], "A root written as a power of 1 over a number",
            "In maths the square root of side can be written as side to the power 1/2. In PHP ** is worked out before /, so " +
            "$side ** 1/2 is $side ** 1 - $side itself - divided by 2. With 16 it is 8, not 4.",
            "The program works out half of the number - or a third, for 1/3 - instead of its root, and neither php -l nor PHP says " +
            "anything of it.",
            "Raise it to the fraction itself: $side ** 0.5, or $side ** (1 / 3) for a cube root, with the fraction in brackets - or " +
            "use sqrt($side).",
            """
            <?php
            $side = 16;
            echo $side ** 0.5, "\n";
            """),

        GuideFor(["logic-php-sort-result-kept"], "Keeping what sort gives back",
            "sort puts the array it is given in order - it changes that array - and gives back only true, to say it worked. So " +
            "$sorted = sort($marks) gives $sorted true, not a sorted array. rsort, usort, asort, ksort, shuffle and the other sorting " +
            "functions work the same way.",
            "$sorted holds true - echo shows it as 1 - while $marks is the array that was sorted. Neither php -l nor PHP says anything.",
            "Sort the array on a line of its own, then use the array: sort($marks); - and $sorted = $marks; after it, if a second " +
            "name is wanted.",
            """
            <?php
            $marks = [90, 70, 80];
            sort($marks);
            echo implode(" ", $marks), "\n";
            """),

        GuideFor(["logic-php-input-not-trimmed"], "A line read with its newline still on it",
            "fgets gives back the line that was typed with the newline from pressing Enter still on the end: typing yes gives \"yes\\n\". " +
            "Compared with \"yes\", it is not equal.",
            "The comparison is false whatever was typed, so the program takes the other branch every time.",
            "Trim the line as it is read: $answer = trim(fgets(STDIN));",
            """
            <?php
            $answer = trim("yes\n");
            echo $answer == "yes" ? "yes" : "no";
            """),

        GuideFor(["logic-php-foreach-copy-changed"], "A loop that changes its own copy",
            "foreach ($marks as $mark) gives each item of $marks to $mark as a copy of its own. Giving $mark a new value changes the copy, " +
            "not the item in the array - so after the loop, $marks is just as it was.",
            "The array is unchanged, with no error, so whatever reads it next works with the old values.",
            "Take each item's key as well, and change the item under it: foreach ($marks as $key => $mark) { $marks[$key] += 5; }",
            """
            <?php
            $marks = [70, 80, 90];
            foreach ($marks as $key => $mark) {
                $marks[$key] += 5;
            }
            echo implode(" ", $marks), "\n";
            """),

        GuideFor(["logic-php-variable-in-single-quotes"], "A variable inside single quotes",
            "PHP puts a variable's value into text only when the text is in double quotes: \"Hello, $name\" becomes Hello, Ada. In " +
            "single quotes, nothing in the text is worked out - $name is ordinary characters, and is printed as it is written. php -l " +
            "says nothing, and neither does PHP as it runs, as the text is still text.",
            "The output shows $name instead of the name, which looks broken to anyone using the program.",
            "Put the text in double quotes. Text that has a \" or a \\ of its own needs those written differently first, as double " +
            "quotes read them differently.",
            """
            <?php
            $name = "Ada";
            echo "Hello, $name\n";
            """),

        GuideFor(["logic-php-nan-comparison"], "Comparing with NaN",
            "NaN means 'not a number' - a value a calculation gives when it has no answer as a number. By the rules, " +
            "NaN is not equal to anything, not even itself, so $value == NAN is false even when value is NaN. php -l " +
            "says nothing, and neither does PHP as it runs.",
            "A comparison with == or === is always false, and with != or !== always true, so a value that is NaN is " +
            "never noticed.",
            "Test with is_nan($value).",
            """
            <?php
            $value = NAN;
            if (is_nan($value)) {
                echo "not a number\n";
            }
            """),

        GuideFor(["logic-php-format-code-printed"], "A %d code that echo does not fill in",
            "%d and %s are codes from C's printf: each marks where a value goes in the text. echo writes each thing it is given side " +
            "by side and fills nothing in, so echo \"Total: %d\", $total prints Total: %d150.",
            "The output shows the code as it is, with the value run on after it, and neither php -l nor PHP says anything of it.",
            "Use printf, which fills the codes in and prints the text - printf(\"Total: %d\\n\", $total) - or give echo the text " +
            "sprintf fills in: echo sprintf(\"Total: %d\", $total).",
            """
            <?php
            $total = 150;
            printf("Total: %d\n", $total);
            """),

        GuideFor(["logic-php-push-result-kept"], "An array given what array_push gives back",
            "array_push adds to the array it is given - it changes that array - and gives back how many items the array now has. So " +
            "$marks = array_push($marks, 90) adds 90 and then replaces the whole array with that count, 3. array_unshift is the same.",
            "$marks is a number from then on, its items lost, and neither php -l nor PHP says anything until it is used as an array.",
            "Call array_push on a line of its own - array_push($marks, 90); - or add with $marks[] = 90;.",
            """
            <?php
            $marks = [70, 80];
            array_push($marks, 90);
            echo implode(" ", $marks), "\n";
            """),

        GuideFor(["logic-php-search-as-condition"], "A search's result used as the condition",
            "strpos and array_search give the position where they found what they looked for, counting from 0, and false when they " +
            "found nothing. A condition counts 0 as false too, so if (strpos($word, \"a\")) treats a match at the very start - " +
            "position 0 - as no match.",
            "Text or an array that starts with what is looked for is treated as not containing it, and neither php -l nor PHP says anything.",
            "Compare with false strictly: if (strpos($word, \"a\") !== false).",
            """
            <?php
            $word = "apple";
            if (strpos($word, "a") !== false) {
                echo "has an a\n";
            }
            """),

        GuideFor(["logic-php-plus-equals-reversed"], "=+ where += was meant",
            "$total =+ $mark is read as $total = +$mark: the = gives $total a new value, and the + is only a plus sign in front of $mark. " +
            "So $total becomes $mark itself, and what it held is lost. += adds to what is already there - and =- the same way gives " +
            "$total -$mark, where -= takes away.",
            "A running total ends as the last value given - 10, not 15, for 5 and 10 - and PHP says nothing.",
            "Write += to add, or -= to take away: $total += $mark;",
            """
            <?php
            $total = 0;
            foreach ([5, 10] as $mark) {
                $total += $mark;
            }
            echo $total, PHP_EOL;
            """),

        GuideFor(["logic-php-odd-test-negative"], "An odd number looked for with % 2 == 1",
            "% gives what is left over after dividing, and it keeps the sign of the number divided: 7 % 2 is 1, but -7 % 2 is -1. So " +
            "$n % 2 == 1 is true only for odd numbers above 0 - a negative odd number leaves -1, and is taken for even.",
            "The program is right until it meets a negative odd number, which it treats as even, with no error.",
            "Ask whether anything is left over: $n % 2 != 0 is true for every odd number, negative or not.",
            """
            <?php
            $n = -3;
            if ($n % 2 != 0) {
                echo "odd", PHP_EOL;
            }
            """),

        GuideFor(["logic-php-date-minutes-as-month"], "m in a time, where the minutes were meant",
            "In the letters date and format take, m is the month and i is the minutes. So \"H:m\" writes the hour and then the month: " +
            "9:30 on 5 February gives 09:02.",
            "The time is written wrongly, with no error.",
            "Use i for the minutes: date(\"H:i\").",
            """
            <?php
            $whenever = mktime(9, 30, 0, 2, 5, 2024);
            echo date("Y-m-d H:i", $whenever), PHP_EOL;
            """),

        GuideFor(["logic-php-misspelt-name"], "A misspelt name given a value",
            "In PHP, giving a value to a variable makes it if it does not exist yet. So $totl = $total + $mark, with $total misspelt, " +
            "makes a new variable called $totl, and $total never changes.",
            "The variable that was meant keeps its old value - a total stays 0 - with nothing said.",
            "Give the value to the name that was meant: $total = $total + $mark;",
            """
            <?php
            $total = 0;
            foreach ([5, 10, 20] as $mark) {
                $total = $total + $mark;
            }
            echo $total, PHP_EOL;
            """),
    ];
}
