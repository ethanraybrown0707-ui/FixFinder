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

        GuideFor(["logic-ruby-power-of-one-over"], "A root written as a power of 1 over a number",
            "In maths the square root of side can be written as side to the power 1/2. In Ruby ** is worked out before /, so " +
            "side ** 1/2 is side ** 1 - side itself - divided by 2. With 16 it is 8, not 4. Putting 1 / 3 in brackets is not enough " +
            "either: two whole numbers divide to a whole number in Ruby, so 1 / 3 is 0, and 27 ** (1 / 3) is 1.",
            "The program works out half of the number - or a third, for 1/3 - instead of its root, and ruby -w says nothing of it.",
            "Raise it to a decimal fraction: side ** 0.5, or side ** (1.0 / 3) for a cube root.",
            """
            side = 16
            puts side ** 0.5
            """),

        GuideFor(["logic-ruby-bang-result-kept"], "Keeping what a ! method gives back",
            "Methods ending in ! - strip!, uniq!, gsub! and their like - change the value they are called on, and give back nil " +
            "when there was nothing to change: \"Ada\".strip! is nil, as \"Ada\" has no spaces to take off. So fixed = name.strip! " +
            "makes fixed nil whenever name was already as strip! would leave it.",
            "The name holds nil for some values and not others - often for input that was typed neatly - so puts prints an empty " +
            "line, and a method called on it stops the program with NoMethodError. ruby -w says nothing of it.",
            "Use the method without the !, which gives back a changed copy every time: fixed = name.strip.",
            """
            name = "Ada"
            fixed = name.strip
            puts fixed
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

        GuideFor(["logic-ruby-rows-shared"], "A grid whose rows are one row",
            "Array.new(3, Array.new(3, 0)) makes one row and fills the grid with that one row three times. Every row " +
            "is the same array, so grid[0][0] = 1 changes the first item of every row. ruby -w says nothing.",
            "A change to one row shows in all of them: after grid[0][0] = 1, p grid prints [[1, 0, 0], [1, 0, 0], [1, " +
            "0, 0]].",
            "Make each row its own array: Array.new(3) { Array.new(3, 0) } runs the block for each row, making a new " +
            "one each time.",
            """
            grid = Array.new(3) { Array.new(3, 0) }
            grid[0][0] = 1
            p grid
            """),

        GuideFor(["logic-ruby-format-code-printed"], "A %d code that puts does not fill in",
            "%d and %s are codes from C's printf: each marks where a value goes in the text. puts and print write what they are " +
            "given and fill nothing in - puts writes each thing on a line of its own - so puts \"Total: %d\", total prints Total: %d, " +
            "and then 150 on the line below it.",
            "The output shows the code as it is, with the value on a line of its own, and ruby -w says nothing of it.",
            "Fill the text in with format - puts format(\"Total: %d\", total) - or put the value into the text: puts \"Total: #{total}\".",
            """
            total = 150
            puts format("Total: %d", total)
            """),

        GuideFor(["logic-ruby-percent-divided-first"], "A percentage that divides first",
            "In Ruby, / of two whole numbers gives a whole number - the part below 1 is thrown away. In score / total * 100 the " +
            "division comes first, so for 45 out of 60 it is 0, and 0 * 100 is 0.",
            "Every score below the total comes out as 0%, and ruby -w says nothing of it.",
            "Multiply first - score * 100 / total - so only the last division drops a fraction: 45 * 100 / 60 is 75.",
            """
            score = 45
            total = 60
            puts score * 100 / total
            """),

        GuideFor(["logic-ruby-each-with-index-order"], "each_with_index's names the wrong way round",
            "each_with_index gives its block two values: each item first, and its position second. A block written |index, mark| " +
            "gives index the item and mark the position - the names say the opposite of what they hold.",
            "The program uses positions as items and items as positions - 70: 0 instead of 0: 70 - and ruby -w says nothing of it.",
            "Name the item first: each_with_index do |mark, index|.",
            """
            marks = [70, 80]
            marks.each_with_index do |mark, index|
              puts "#{index}: #{mark}"
            end
            """),

        GuideFor(["logic-ruby-plus-equals-reversed"], "=+ where += was meant",
            "total =+ mark is read as total = +mark: the = gives total a new value, and the + is only a plus sign in front of mark. So " +
            "total becomes mark itself, and what it held is lost. += adds to what is already there - and =- the same way gives total " +
            "-mark, where -= takes away.",
            "A running total ends as the last value given - 10, not 15, for 5 and 10 - and ruby -w says nothing.",
            "Write += to add, or -= to take away: total += mark.",
            """
            total = 0
            [5, 10].each do |mark|
              total += mark
            end
            puts total
            """),

        GuideFor(["logic-ruby-date-format-minutes"], "%M in a date, where the month was meant",
            "In strftime's format, %m is the month and %M is the minutes - the case matters. So \"%Y-%M-%d\" writes the minutes where " +
            "the month should be: 9:30 on 5 February 2024 gives 2024-30-05.",
            "The date is written wrongly, with no error - and it changes from one minute to the next.",
            "Use %m for the month: strftime(\"%Y-%m-%d\"). %M belongs beside an hour, as in %H:%M.",
            """
            whenever = Time.new(2024, 2, 5, 9, 30)
            puts whenever.strftime("%Y-%m-%d %H:%M")
            """),

        GuideFor(["logic-ruby-zero-is-true"], "A number used as a condition",
            "In Ruby only nil and false are false - every number is true, 0 too. So if n % 2 is true whatever is left over, and " +
            "if items.length is true even when there are no items.",
            "The if always runs and its else never does, with no error: 4 is called odd, and an empty list has items.",
            "Compare the number with 0: if n % 2 != 0, or if items.length > 0. Ruby's own n.odd? and items.any? say the same.",
            """
            n = 4
            if n % 2 != 0
              puts "odd"
            else
              puts "even"
            end
            """),

        GuideFor(["logic-ruby-hash-default-shared"], "Hash.new([]) - one default for every key",
            "Hash.new([]) makes one array, and gives that same array back for every key that is missing - without storing it. So " +
            "groups[:red] << \"Ada\" adds to the one default array, and the hash itself stays empty; every other key then shows \"Ada\" too.",
            "The hash looks empty - {} - while every missing key shares the values added, with no error.",
            "Give each key its own, stored as it is made: Hash.new { |hash, key| hash[key] = [] }.",
            """
            groups = Hash.new { |hash, key| hash[key] = [] }
            groups[:red] << "Ada"
            groups[:blue] << "Bob"
            p groups
            """),

        GuideFor(["logic-ruby-branch-never-reached"], "An elsif that can never run",
            "An if - elsif chain takes the first branch whose test passes. If an earlier test lets through every value a later one does - " +
            "score >= 50 before score >= 70 - the later branch never gets a value.",
            "That branch never runs, so no input can test it - a score of 75 got C, not B - and ruby -w says nothing.",
            "Put the strictest test first: score >= 70, then score >= 50.",
            """
            score = 75
            if score >= 70
              puts "B"
            elsif score >= 50
              puts "C"
            else
              puts "F"
            end
            """),

        GuideFor(["logic-ruby-loop-never-runs"], "A count down written as (10..1)",
            "A range counts up: (10..1) starts at 10, and 10 is already past 1 - so it holds nothing, and each over it runs no times at all.",
            "Nothing in the loop ever runs, so it cannot be tested, and ruby -w says nothing.",
            "Count down with downto: 10.downto(1).each.",
            """
            3.downto(1).each do |count|
              puts count
            end
            """),

        GuideFor(["logic-ruby-handler-never-reached"], "A rescue that can never run",
            "rescue clauses are tried in order, and the first that matches the error takes it. rescue StandardError matches " +
            "ArgumentError too - ArgumentError is a kind of StandardError - so a rescue ArgumentError after it is never reached.",
            "The specific handler never runs, so it cannot be tested - Integer(\"x\") printed the general message - and ruby -w says nothing.",
            "Put the specific rescue first: rescue ArgumentError, then rescue StandardError.",
            """
            begin
              Integer("x")
            rescue ArgumentError
              puts "not a number"
            rescue StandardError
              puts "something went wrong"
            end
            """),

        GuideFor(["logic-ruby-test-never-run"], "A test method minitest never runs",
            "minitest runs the methods of a Minitest::Test whose names start with test_ - and no others. A method named check_add or " +
            "add_test is never run, however many assertions it holds.",
            "Whatever it checks is never checked, so the tests pass while the code is wrong - 1 runs, where there were 2 tests - with nothing " +
            "from ruby -w.",
            "Start its name with test_: test_add.",
            """
            require "minitest/autorun"

            def add(first, second)
              first + second
            end

            class AddTest < Minitest::Test
              def test_add
                assert_equal 7, add(5, 2)
              end
            end
            """),

        GuideFor(["logic-ruby-assert-with-expected"], "assert given the value expected",
            "assert checks one value: that it is not nil or false. Its second argument is only the message shown when it fails - so " +
            "assert add(5, 2), 7 never compares with 7, and passes for any answer at all but nil and false.",
            "The test passes when the code gives the wrong answer - it passed with add giving 3 - and ruby -w says nothing.",
            "Compare the two with assert_equal, the value expected first: assert_equal 7, add(5, 2).",
            """
            require "minitest/autorun"

            def add(first, second)
              first + second
            end

            class AddTest < Minitest::Test
              def test_add
                assert_equal 7, add(5, 2)
              end
            end
            """),
    ];
}
