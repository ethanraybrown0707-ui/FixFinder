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
    ];
}
