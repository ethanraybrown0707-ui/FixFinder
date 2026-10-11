using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// PHP's parse errors, the fatal errors that stop its programs, and the warnings it prints while it carries on - each
/// explained for someone new to programming.
/// </summary>
/// <remarks>
/// Every message matched here is one PHP 8.4 was seen to give. Every example is a whole program, and PhpLiveTests checks
/// each one with php -l and runs it, and fails on anything PHP says.
/// </remarks>
internal static class PhpGuides
{
    private const string NothingRuns = "PHP reads the whole file before it runs any of it, so while this is wrong no part of the program runs.";

    private const string Stops = "The program stops at this line, and nothing after it runs.";

    private const string CarriesOn =
        "PHP only warns and carries on, with null in place of the value - so the program goes on with a wrong value, and what it " +
        "prints or works out from here can be wrong without any error.";

    private static GuideEntry Thrown(string type, string pattern, string explanation, string fix, string example) => new()
    {
        ExceptionTypes = [type],
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, type is "Warning" or "Notice" or "Deprecated" ? CarriesOn : type == "ParseError" ? NothingRuns : Stops, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Thrown("ParseError", @"^syntax error, unexpected (?:token|variable|identifier)",
            "PHP could not make sense of the code where it names. Most often the line before it is missing its semicolon: each " +
            "statement in PHP ends with ;, and without one PHP reads on into the next line as part of the same statement - and only " +
            "notices there. A bracket left open, or a missing $ before a variable's name, does the same.",
            "Look at the end of the line before the one PHP names for a missing ;, and at the line itself for a bracket or quote left open.",
            """
            <?php
            $total = 10;
            echo $total;
            """),

        Thrown("ParseError", @"^Unclosed '[({\[]'|unexpected end of file",
            "A bracket was opened and never closed - PHP names where it was opened - so PHP reached the end of the file still waiting " +
            "for it.",
            "Close the bracket where its block or call ends. Indenting the code inside shows where that is.",
            """
            <?php
            function total($marks) {
                return array_sum($marks);
            }

            echo total([1, 2]);
            """),

        Thrown("DivisionByZeroError", @".",
            "Dividing by zero has no answer, so PHP stops the program. The number divided by is zero at this point - often because it " +
            "counts the items of an array that turned out to be empty.",
            "Check the number is not zero before dividing by it, and decide what the answer should be when it is.",
            """
            <?php
            $marks = [];
            $average = count($marks) == 0 ? 0 : array_sum($marks) / count($marks);
            echo $average;
            """),

        Thrown("Error", @"^Call to a member function \w+\(\) on null",
            "null is PHP's value for \"nothing here\" - what a variable holds before it is given an object, and what a function gives back " +
            "when it finds nothing. A method can only be called on an object, so calling one on null stops the program.",
            "Find where the value came from and why it is null there, and check for null before calling the method.",
            """
            <?php
            $user = null;
            echo $user === null ? "no user" : $user->name();
            """),

        Thrown("Error", @"^Call to undefined (?:function|method)",
            "PHP cannot find a function or method by this name - it was never written, is spelt differently, or is in a file this one " +
            "does not include.",
            "Check the name against where the function is written - PHP's own functions are listed on php.net - or include the file it is in.",
            """
            <?php
            echo strtoupper("ada");
            """),

        Thrown("ArgumentCountError", @".",
            "A function has to be given one value for each parameter it is written with, and this call gives it too few - the message " +
            "says how many it was given, on which line, and how many it expects.",
            "Look at the function's definition and give the call a value for each parameter it lists.",
            """
            <?php
            function area($width, $height) {
                return $width * $height;
            }
            echo area(3, 4);
            """),

        Thrown("TypeError", @"^Unsupported operand types",
            "Arithmetic needs numbers on both sides, and one side is text that is not a number - or an array, or null - which PHP 8 " +
            "will not use as one.",
            "Turn the text into a number yourself, and check that it is one: is_numeric($typed) says whether it is.",
            """
            <?php
            $typed = "5";
            echo is_numeric($typed) ? $typed + 2 : "not a number";
            """),

        Thrown("TypeError", @"must be of type",
            "This function says what type each value it is given must be, and this call gives one of another type - the message says " +
            "which argument, the type it needs and the type it was given.",
            "Give the function a value of the type it asks for: turn text into a number with (int), or a number into text with (string).",
            """
            <?php
            declare(strict_types=1);
            function twice(int $n): int {
                return $n * 2;
            }
            echo twice(5);
            """),

        Thrown("Warning", @"^Undefined variable \$",
            "This variable is used before anything gave it a value - often because its name is spelt differently from where it is set. " +
            "PHP warns, and uses null in its place.",
            "Check the spelling against where the variable is given its value, and give it one before this line.",
            """
            <?php
            $total = 10;
            echo $total + 1;
            """),

        Thrown("Warning", @"^Undefined array key",
            "This asks an array for a key it does not have - a name that was never put in, or a position past the end, as positions " +
            "start at 0. PHP warns, and uses null in its place.",
            "Check the key is there first - isset($ages[\"Bob\"]) or array_key_exists - or give a value to use instead: $ages[\"Bob\"] ?? 0.",
            """
            <?php
            $ages = ["Ada" => 36];
            echo ($ages["Bob"] ?? 0) + 1;
            """),

        Thrown("Warning", @"^A non-numeric value encountered",
            "Arithmetic needs numbers, and this text only starts with one - \"5 apples\". PHP uses the number at the start and warns " +
            "that the rest was not one.",
            "Turn the text into a number yourself, and check that it is one first: is_numeric says whether it is.",
            """
            <?php
            $typed = "5";
            echo is_numeric($typed) ? $typed + 2 : "not a number";
            """),

        Thrown("Warning", @"^foreach\(\) argument must be of type array",
            "foreach goes through the items of an array, and the value given to it is not an array - often null, from a variable or a " +
            "function that gave nothing back.",
            "Check what the value is before the loop, or give it an empty array to start from: $marks = [];.",
            """
            <?php
            $marks = [70, 80];
            foreach ($marks as $mark) {
                echo $mark . "\n";
            }
            """),
    ];
}
