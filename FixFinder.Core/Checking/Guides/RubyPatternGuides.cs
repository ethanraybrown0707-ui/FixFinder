using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic checks of Ruby programs, each explained for someone new to programming. Every example is a whole
/// program, and RubyLiveTests checks each one with ruby -wc, and fails on any warning.
/// </summary>
internal static class RubyPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-ruby-integer-average"], "A decimal that lost its fraction",
            "Dividing one whole number by another in Ruby gives a whole number: the part after the point is dropped, so 3 / 2 is 1. " +
            ".to_f after the division comes too late - it turns the 1 into 1.0, as the fraction is already gone. Making the first " +
            "number a decimal first makes the division a decimal one.",
            "Averages and percentages come out rounded down to a whole number, with no error to say so.",
            "Make the first number a decimal before dividing: total.to_f / count.",
            """
            total = 3
            count = 2
            puts total.to_f / count
            """),

        GuideFor(["logic-ruby-float-equality"], "Decimal numbers compared exactly",
            "Computers store fractions like 0.1 in binary, where most of them can only be stored approximately - the way 1/3 can only be " +
            "written as 0.333... in decimal. Arithmetic on them leaves a tiny error, so 0.1 + 0.2 is 0.30000000000000004, and " +
            "0.1 + 0.2 == 0.3 is false.",
            "An exact comparison fails even when the numbers are equal for every practical purpose, so the code it guards never runs.",
            "Check that the difference is smaller than a tiny tolerance: (total - 0.3).abs < 1e-9.",
            """
            total = 0.1 + 0.2
            puts "equal" if (total - 0.3).abs < 1e-9
            """),

        GuideFor(["logic-ruby-result-discarded"], "A changed copy that is thrown away",
            "upcase, strip, sort, reverse and their like do not change the value they are called on - they give back a new, changed " +
            "copy. A line that calls one and keeps nothing changes nothing: the value is just as it was on the next line. Each has a " +
            "form ending in ! - upcase!, sort! - that changes the value itself.",
            "The change never happens, so the lines after it go on using the value as it was.",
            "Keep the copy - name = name.upcase - or use the ! form, which changes the value itself.",
            """
            name = "ada"
            name = name.upcase
            puts name
            """),

        GuideFor(["logic-ruby-range-to-length"], "A range that runs one past the end",
            "A range with two dots takes in its end, so 0..marks.length goes from 0 up to marks.length itself. An array's positions " +
            "start at 0, so its last position is one less than its length - and the range's last number is one past it. A range with " +
            "three dots, 0...marks.length, stops before its end.",
            "On the last pass marks[i] is nil - Ruby gives no error for a position past the end - so the loop works with a value " +
            "that is not there.",
            "Use three dots - 0...marks.length - or go over the items themselves with marks.each.",
            """
            marks = [70, 80, 90]
            (0...marks.length).each do |i|
              puts "Mark: #{marks[i]}"
            end
            """),

        GuideFor(["logic-ruby-caret-as-power"], "^ used as a power",
            "In maths side^2 means side squared, but in Ruby ^ is exclusive or: it compares two whole numbers bit by bit " +
            "and keeps the bits that differ. 5 is 101 in binary and 2 is 010, so 5 ^ 2 is 111 - which is 7, not 25.",
            "The program runs and works out a number - just the wrong one - and ruby -w says nothing of it.",
            "Use Ruby's power, **: side ** 2.",
            """
            side = 5
            puts side ** 2
            """),

        GuideFor(["logic-ruby-input-not-chomped"], "A line read with its newline still on it",
            "gets gives back the line that was typed with the newline from pressing Enter still on the end: typing yes gives \"yes\\n\". " +
            "Compared with \"yes\", it is not equal.",
            "The comparison is false whatever was typed, so the program takes the other branch every time.",
            "Take the newline off as the line is read: answer = gets.chomp.",
            """
            answer = "yes\n".chomp
            puts answer == "yes"
            """),

        GuideFor(["logic-ruby-each-for-map"], "each where map was meant",
            "each runs its block for every item, and then gives back the list it went over, as it was - not what the block worked out. " +
            "map gives back a new list of what the block worked out for each item.",
            "The name is given the same list again, with no error, so it holds the old values.",
            "Use map when the block works out a new value for each item: doubled = marks.map { |mark| mark * 2 }.",
            """
            marks = [70, 80]
            doubled = marks.map { |mark| mark * 2 }
            p doubled
            """),

        GuideFor(["logic-ruby-interpolation-in-single-quotes"], "#{...} inside single quotes",
            "Ruby puts a value into text only when the text is in double quotes: \"Hello, #{name}\" becomes Hello, Ada. In single " +
            "quotes, nothing in the text is worked out - #{name} is ordinary characters, and is printed as it is written. Ruby does " +
            "not read inside the text: with its warnings on - ruby -w, as FixFinder runs it - the most it says is that name is " +
            "assigned but unused, and only when the text was the one place meant to use it.",
            "The output shows #{name} instead of the name, which looks broken to anyone using the program.",
            "Put the text in double quotes. Text that has a \" or a \\ of its own needs those written differently first, as double " +
            "quotes read them differently.",
            """
            name = "Ada"
            puts "Hello, #{name}"
            """),

        GuideFor(["logic-ruby-elif"], "elif, which Ruby does not have",
            "Ruby writes \"else if\" as elsif. elif is Python's word, and Ruby reads a line starting with it as a call to a method called " +
            "elif - inside the if's own branch, not as a branch of its own.",
            "When the if's condition is false, neither branch runs and nothing is said; when it is true, the program stops at the elif.",
            "Write elsif.",
            """
            mark = 70
            if mark > 80
              puts "high"
            elsif mark > 50
              puts "pass"
            end
            """),

        GuideFor(["logic-ruby-nan-comparison"], "Comparing with NaN",
            "NaN means 'not a number' - a value a calculation gives when it has no answer as a number. By the rules, " +
            "NaN is not equal to anything, not even itself, so value == Float::NAN is false even when value is NaN. " +
            "ruby -w says nothing.",
            "A comparison with == is always false, and with != always true, so a value that is NaN is never noticed.",
            "Test with value.nan?.",
            """
            value = Float::NAN
            puts "not a number" if value.nan?
            """),

        GuideFor(["logic-ruby-array-shared"], "A second name for an array is not a copy",
            "backup = marks does not copy the array: it gives the same array a second name. A change made through " +
            "either name - marks << 100 - is a change to the one array, so backup shows it too. ruby -w says nothing.",
            "The second name does not keep the values the array had: after marks << 100, p backup prints [70, 80, 90, " +
            "100].",
            "Make a copy: backup = marks.dup. The copy is a new array with the same items, and a change to one no " +
            "longer shows in the other.",
            """
            marks = [70, 80, 90]
            backup = marks.dup
            marks << 100
            p backup
            """),
    ];
}
