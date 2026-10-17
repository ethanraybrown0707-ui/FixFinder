using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Ruby's syntax errors and warnings, and the exceptions that stop Ruby programs, each explained for someone new to
/// programming.
/// </summary>
/// <remarks>
/// Every message matched here is one Ruby 3.3 was seen to give - with the quotes Ruby 3.4 uses instead allowed for. Every
/// example is a whole program, and RubyLiveTests checks each one with ruby -wc, and fails on any warning.
/// </remarks>
internal static class RubyGuides
{
    private const string NothingRuns = "Ruby reads the whole file before it runs any of it, so while this is wrong no part of the program runs.";

    private const string Stops = "The program stops at this line, and nothing after it runs.";

    private static GuideEntry Raised(string type, string explanation, string fix, string example, string? pattern = null, string? why = null) => new()
    {
        ExceptionTypes = [type],
        Message = pattern is null ? null : new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why ?? Stops, fix, example),
    };

    private static GuideEntry Syntax(string pattern, string explanation, string fix, string example) => new()
    {
        ExceptionTypes = ["SyntaxError"],
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, NothingRuns, fix, example),
    };

    private static GuideEntry Warned(string pattern, string explanation, string why, string fix, string example) => new()
    {
        ExceptionTypes = ["compile warning"],
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Raised("NoMethodError",
            "nil is Ruby's value for \"nothing here\" - what a hash gives for a key it does not have, what a method gives back when it " +
            "has nothing to give, and what a variable holds before it is given a value. nil has hardly any methods, so calling one on " +
            "it stops the program.",
            "Find where the value came from and why it is nil there: check for nil first - unless value.nil? - give a default with " +
            "||, as in ages[\"Bob\"] || 0, or use fetch with a default.",
            """
            ages = { "Ada" => 36 }
            age = ages.fetch("Bob", 0)
            puts age + 1
            """,
            pattern: @"for nil\b"),

        Raised("NoMethodError",
            "A value can only use the methods its kind of value has, and this one has no method by this name - often a spelling close " +
            "to the real one, which Ruby names after \"Did you mean?\".",
            "Use the method's name as Ruby has it - Ruby's Did you mean? names the one it thinks was meant.",
            """
            name = "ada"
            puts name.upcase
            """),

        Raised("NameError",
            "Every name in Ruby - a variable, a method - has to be made before it is used, and spelt the same each time; capital " +
            "letters count. Ruby cannot find this one from here, and often names the one it thinks was meant after \"Did you mean?\".",
            "Check the spelling against where the name is made, and make it before this line.",
            """
            total = 10
            puts total
            """,
            pattern: @"^undefined local variable or method"),

        Raised("NameError",
            "A name starting with a capital letter is a constant or a class, and Ruby cannot find one called this - it was never " +
            "defined, is spelt differently, or is in a file this one does not require.",
            "Define the class, check its spelling and capitals, or add the require_relative line for the file it is in.",
            """
            class Mark
            end

            puts Mark.new.class
            """,
            pattern: @"^uninitialized constant"),

        Raised("TypeError",
            "+ joins two pieces of text, or adds two numbers - and Ruby will not join text and a number, or add a number and text, " +
            "by itself.",
            "Turn the number into text first, with .to_s - or put it into the text with #{}: \"Age: #{age}\".",
            """
            age = 36
            puts "Age: " + age.to_s
            """,
            pattern: @"no implicit conversion of \w+ into String"),

        Raised("TypeError",
            "Arithmetic needs numbers on both sides, and the value on the right is not a number - it is nil, or text. Ruby will not " +
            "turn it into one by itself.",
            "Make sure the value is a number before the arithmetic: turn text into one with .to_i or Integer(), and give nil a value first.",
            """
            typed = "5"
            puts 10 + typed.to_i
            """,
            pattern: @"can't be coerced into|no implicit conversion of \w+ into (?:Integer|Float)"),

        Raised("ZeroDivisionError",
            "Dividing a whole number by zero has no answer, so Ruby stops the program. The number divided by is zero at this point - " +
            "often because it counts the items of a list that turned out to be empty. A decimal divided by zero does not stop it: 7.0 / 0 " +
            "is Infinity.",
            "Check the number is not zero before dividing by it, and decide what the answer should be when it is.",
            """
            marks = []
            average = marks.empty? ? 0 : marks.sum / marks.length
            puts average
            """),

        Raised("ArgumentError",
            "A method has to be given one value for each parameter it is written with, and this call gives it too few or too many - " +
            "the message says how many it was given and how many it expects.",
            "Look at the method's def line and give the call a value for each parameter it lists.",
            """
            def area(width, height)
              width * height
            end

            puts area(3, 4)
            """,
            pattern: @"^wrong number of arguments"),

        Raised("ArgumentError",
            "Integer() and Float() turn text into a number, and stop the program when the text is not one. (.to_i does not stop it: " +
            "it gives 0 for text that is not a number, which can hide the mistake.)",
            "Check the text is a number first - Integer(typed, exception: false) gives nil instead of stopping - and say what to do when it is not.",
            """
            typed = "twelve"
            age = Integer(typed, exception: false)
            puts age.nil? ? "not a whole number" : age + 1
            """,
            pattern: @"^invalid value for (?:Integer|Float)\(\)"),

        Raised("KeyError",
            "fetch looks a key up in a hash and stops the program when the key is not there. (Square brackets give nil instead.)",
            "Give fetch a value to use when the key is missing - ages.fetch(\"Bob\", 0) - or check with key? first.",
            """
            ages = { "Ada" => 36 }
            puts ages.fetch("Bob", 0)
            """),

        Raised("IndexError",
            "fetch asks an array for a position, and stops the program when there is none there. Positions start at 0, so an array " +
            "of 2 items has positions 0 and 1 - the message gives the positions there are.",
            "Ask only for a position below the length, or give fetch a value to use when there is none: marks.fetch(5, 0).",
            """
            marks = [70, 80]
            puts marks.fetch(1)
            """),

        Raised("FrozenError",
            "This value is frozen - made so it can never change - and the line tries to change it. A string made with .freeze is, and " +
            "so is a constant's value when the program says so.",
            "Make a changed copy instead - NAME + \"!\" - or use a copy that is not frozen: NAME.dup.",
            """
            NAME = "ada".freeze
            greeting = NAME + "!"
            puts greeting
            """),

        Raised("SystemStackError",
            "Each call still in progress takes room on the stack, and there were too many at once: a method that calls itself with " +
            "no case where it stops does that.",
            "Give the method a case where it returns without calling itself, and make every call move towards it.",
            """
            def count_down(n)
              return 0 if n.zero?

              count_down(n - 1)
            end

            puts count_down(3)
            """),

        Raised("ENOENT",
            "The program asked for a file by a name that is not there - from the folder it runs in, relative names are looked for there.",
            "Check the file's name and where it is, or give its full path.",
            """
            if File.exist?("scores.txt")
              puts File.read("scores.txt")
            else
              puts "no scores.txt here"
            end
            """),

        new GuideEntry
        {
            RuleIds = ["ruby-else-if"],
            Guide = new MistakeGuide(
                "Ruby writes \"else if\" as one word, elsif. else if is an else with a new if inside it - and that if needs an end of its " +
                "own, so Ruby reached the end of the file still looking for it.",
                NothingRuns,
                "Write elsif, which starts the next branch of the same if.",
                """
                mark = 70
                if mark > 80
                  puts "high"
                elsif mark > 50
                  puts "pass"
                end
                """),
        },

        new GuideEntry
        {
            RuleIds = ["ruby-missing-do"],
            Guide = new MistakeGuide(
                "A block given to a method - each, times, map - starts with do, and the |mark| that names each item comes after it. " +
                "Without the do, Ruby read the |mark| as more of the call, and stopped on the line after it.",
                NothingRuns,
                "Put do before the |mark|: [70, 80].each do |mark|.",
                """
                [70, 80].each do |mark|
                  puts mark
                end
                """),
        },

        new GuideEntry
        {
            RuleIds = ["ruby-python-colon"],
            Guide = new MistakeGuide(
                "Ruby's def, if and while lines end with nothing after them - no colon, as Python has - and the block they start is " +
                "closed by end.",
                NothingRuns,
                "Take the colon off the end of the line.",
                """
                def total(marks)
                  marks.sum
                end

                puts total([70, 80])
                """),
        },

        new GuideEntry
        {
            RuleIds = ["ruby-total-starts-at-zero"],
            Guide = new MistakeGuide(
                "A variable first given a value inside a loop's block is a new variable on each pass, and on the right of its first = it " +
                "is nil - Ruby's value for nothing - which cannot be added to. It is gone again once the block ends.",
                Stops,
                "Give the variable its starting value before the loop: total = 0. Then every pass adds to the one variable, and it is " +
                "still there after the loop.",
                """
                marks = [70, 80]
                total = 0
                marks.each do |mark|
                  total = total + mark
                end
                puts total
                """),
        },

        Syntax(@"unexpected end-of-input|expected an `end`|expecting `end'|expecting 'end'",
            "Every def, if, while, do and class in Ruby is closed by its own end. One is missing, so Ruby reached the end of the file " +
            "still waiting for it - and only noticed there, so the line it names is the last one, not where the end belongs.",
            "Find the def, if or do that has no end, and put its end where its body finishes. Indenting the body shows where.",
            """
            def total(marks)
              marks.sum
            end

            puts total([1, 2])
            """),

        Syntax(@"unexpected [`']?end'?|unexpected 'end'|unexpected keyword_end",
            "There is an end that nothing opened: an extra one, or one whose def, if or do is missing or spelt wrong.",
            "Remove the extra end, or put back the line it closes.",
            """
            if 2 > 1
              puts "yes"
            end
            """),

        Syntax(@"unterminated string|unterminated quoted string",
            "A piece of text in quotes was never closed, so Ruby read the rest of the file as part of it.",
            "Put the closing quote at the end of the text - the same kind of quote it starts with.",
            """
            name = "Ada"
            puts name
            """),

        Syntax(@".",
            "Ruby could not make sense of the code here. The message says what it found and what it expected; the slip is often just " +
            "before the place it names - a bracket or quote left open, a missing comma, or a word spelt wrong.",
            "Read the line it names and the line above it, and look for what is missing or extra.",
            """
            marks = [70, 80, 90]
            puts marks.sum
            """),

        Warned(@"^assigned but unused variable",
            "This variable is given a value that nothing reads. That does no harm on its own, but it is often a sign of a slip nearby: " +
            "a result stored under one name and then read from another.",
            "The program runs, but the value worked out here is never used.",
            "Use the variable, or remove it. If it is there on purpose, starting its name with _ tells Ruby so.",
            """
            total = 2 + 3
            puts total
            """),

        Warned(@"found [`']= literal' in conditional",
            "A condition needs a comparison - == - and this one has =, which stores the value instead. The condition is then always " +
            "true, and the variable is changed.",
            "The if always runs, whatever the variable held, and the variable's value is lost.",
            "Use == to compare: if mark == 40.",
            """
            mark = 70
            puts "pass" if mark >= 40
            """),

        Warned(@"^possibly useless use of",
            "This line works a value out and does nothing with it - a comparison on a line of its own, say - so it changes nothing.",
            "Whatever the line was meant to do never happens.",
            "Keep the value - store it, print it, or use it in an if - or remove the line.",
            """
            mark = 70
            passed = mark >= 40
            puts passed
            """),

        Warned(@"^mismatched indentations",
            "An end is indented differently from the line it closes, which makes it hard to see which block it belongs to - and is " +
            "often a sign that it closes a different one than was meant.",
            "The program runs, but the code may not be grouped the way it looks.",
            "Indent each end to line up with the def, if or do it closes.",
            """
            def total(marks)
              marks.sum
            end

            puts total([1, 2])
            """),
    ];
}
