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
    ];
}
