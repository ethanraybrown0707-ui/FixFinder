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
    ];
}
