using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic checks of Elixir programs, each explained for someone new to programming. Every example is a whole
/// program, and ElixirLiveTests compiles each one with FixFinder's check and runs it, and fails on anything Elixir says.
/// </summary>
internal static class ElixirPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-elixir-range-to-length"], "A range that runs one past the end",
            "Elixir's ranges take in their end, so 0..length(marks) goes from 0 up to length(marks) itself. A list's positions start at " +
            "0, so its last position is one less than its length - and the range's last number is one past it.",
            "On the last pass Enum.at(marks, i) is past the end and gives nil - Elixir gives no error for it - so the loop works with a " +
            "value that is not there.",
            "Stop at the last position with 0..(length(marks) - 1)//1 - the //1 makes it count up, so an empty list has no positions - " +
            "or go over the items themselves with for mark <- marks.",
            """
            marks = [70, 80, 90]

            for i <- 0..(length(marks) - 1)//1 do
              IO.puts("Mark: #{Enum.at(marks, i)}")
            end
            """),

        GuideFor(["logic-elixir-input-not-trimmed"], "A line read with its newline still on it",
            "IO.gets gives back the line that was typed with the newline from pressing Enter still on the end: typing y gives \"y\\n\". " +
            "Compared with \"y\", it is not equal.",
            "The comparison is false whatever was typed, so the program takes the other branch every time.",
            "Trim the line as it is read: String.trim(IO.gets(\"Continue? \")).",
            """
            answer = String.trim("y\n")

            if answer == "y" do
              IO.puts("yes")
            else
              IO.puts("no")
            end
            """),

        GuideFor(["logic-elixir-result-discarded"], "A changed copy that is thrown away",
            "Elixir's values never change. Map.put, String.upcase, Enum.sort and their like give back a new, changed copy and leave the " +
            "value they were given as it was - so a line that calls one and keeps nothing changes nothing.",
            "The change never happens, so the lines after it go on using the value as it was.",
            "Give the copy to the name: ages = Map.put(ages, \"Bob\", 20).",
            """
            ages = %{"Ada" => 36}
            ages = Map.put(ages, "Bob", 20)
            IO.inspect(ages)
            """),

        GuideFor(["logic-elixir-float-equality"], "Decimal numbers compared exactly",
            "Decimals are stored in binary, which holds most of them only nearly: 0.1 + 0.2 is held as 0.30000000000000004, not 0.3.",
            "== asks for an exact match, so a total worked out by arithmetic can fail to equal the decimal it should.",
            "Compare within a tiny tolerance: abs(total - 0.3) < 1.0e-9 - Elixir writes the tolerance with a point before the e.",
            """
            total = 0.1 + 0.2

            if abs(total - 0.3) < 1.0e-9 do
              IO.puts("equal")
            end
            """),

        GuideFor(["logic-elixir-assignment-in-condition"], "= in a condition",
            "= in Elixir matches: if mark = 40 gives mark the value 40, and the condition is then 40 - which counts as true, as " +
            "everything does but false and nil.",
            "The block runs whatever mark was, and from then on mark is 40.",
            "Compare with ==: if mark == 40 do.",
            """
            mark = 70

            if mark == 40 do
              IO.puts("forty")
            end

            IO.puts(mark)
            """),

        GuideFor(["logic-elixir-new-variable-in-block"], "A variable changed inside a block",
            "Elixir's variables never change. total = total + mark inside an fn, a for or an if makes a new total, which lasts only " +
            "until the block ends - so after the block, total is what it was before.",
            "A total built up in a loop stays at its starting value, and a value set inside an if is lost - with no error.",
            "Give the block's result to the variable: total = Enum.reduce(marks, total, fn mark, total -> total + mark end).",
            """
            marks = [70, 80, 90]
            total = Enum.reduce(marks, 0, fn mark, total -> total + mark end)
            IO.puts(total)
            """),

        GuideFor(["logic-elixir-each-for-map"], "Enum.each where Enum.map was meant",
            "Enum.each runs its function for every item, for what the function does - printing, say - and then gives back :ok. It " +
            "keeps nothing the function works out. Enum.map gives back a new list of what the function worked out for each item.",
            "The name is given :ok instead of a list, with no warning, so printing it shows :ok, and Enum.sum or any other Enum " +
            "function given it stops the program with Protocol.UndefinedError.",
            "Use Enum.map when the function works out a new value for each item: doubled = Enum.map(marks, fn mark -> mark * 2 end).",
            """
            marks = [70, 80]
            doubled = Enum.map(marks, fn mark -> mark * 2 end)
            IO.inspect(doubled)
            """),

        GuideFor(["logic-elixir-text-compared-with-number"], "Text compared with a number",
            "A line IO.gets reads is text, and so is anything in quotes - \"7\" is text, not the number 7. Elixir never counts text " +
            "equal to a number, so \"7\" == 7 is false. And when it puts values in order, every piece of text comes after every " +
            "number, so \"7\" > 10 is true.",
            "The comparison gives the same answer whatever was typed, with no warning, so the program takes the same branch every time.",
            "Turn the text into a number first: String.to_integer(String.trim(typed)) == 7.",
            """
            typed = "7"
            number = String.to_integer(typed)
            IO.puts(number == 7)
            """),

        GuideFor(["logic-elixir-odd-test-negative"], "An odd number looked for with rem(n, 2) == 1",
            "rem gives what is left over after dividing, and it keeps the sign of the number divided: rem(7, 2) is 1, but rem(-7, 2) is " +
            "-1. So rem(n, 2) == 1 is true only for odd numbers above 0 - a negative odd number leaves -1, and is taken for even.",
            "The program is right until it meets a negative odd number, which it treats as even, and Elixir says nothing.",
            "Ask whether anything is left over: rem(n, 2) != 0 is true for every odd number, negative or not.",
            """
            n = -3

            if rem(n, 2) != 0 do
              IO.puts("odd")
            end
            """),

        GuideFor(["logic-elixir-date-format-minutes"], "%M in a date, where the month was meant",
            "In Calendar.strftime's format, %m is the month and %M is the minutes - the case matters. So \"%Y-%M-%d\" writes the minutes " +
            "where the month should be: 9:30 on 5 February 2024 gives 2024-30-05.",
            "The date is written wrongly, with no error.",
            "Use %m for the month: Calendar.strftime(date, \"%Y-%m-%d\"). %M belongs beside an hour, as in %H:%M.",
            """
            whenever = ~N[2024-02-05 09:30:00]
            IO.puts(Calendar.strftime(whenever, "%Y-%m-%d %H:%M"))
            """),

        GuideFor(["logic-elixir-zero-is-true"], "A number used as a condition",
            "In Elixir only nil and false are false - every number is true, 0 too. So if rem(n, 2) is true whatever is left over, and " +
            "if length(items) is true even for an empty list.",
            "The if always runs and its else never does, with no error: 4 is called odd, and an empty list has items.",
            "Compare the number with 0: if rem(n, 2) != 0, or if length(items) > 0.",
            """
            n = 4

            if rem(n, 2) != 0 do
              IO.puts("odd")
            else
              IO.puts("even")
            end
            """),

        GuideFor(["logic-elixir-numbers-printed-as-text"], "IO.puts of a list of numbers",
            "IO.puts takes a list of whole numbers as text: each number is the character with that code. So IO.puts([72, 105]) writes " +
            "\"Hi\", and [7, 8, 9] writes a bell, a backspace and a tab - not the numbers.",
            "The list is printed as strange characters, or nothing visible, with no error.",
            "Print the list as numbers with IO.inspect(marks, charlists: :as_lists) - IO.inspect alone shows a list of printable codes as " +
            "text too, ~c\"Hi\" - or join it into text: IO.puts(Enum.join(marks, \", \")).",
            """
            marks = [7, 8, 9]
            IO.inspect(marks, charlists: :as_lists)
            """),

        GuideFor(["logic-elixir-split-on-nothing"], "String.split with \"\"",
            "String.split(text, \"\") splits at every point between characters - including before the first and after the last - so it " +
            "gives an empty piece at each end: String.split(\"abc\", \"\") is [\"\", \"a\", \"b\", \"c\", \"\"].",
            "A count or a loop over the characters has two extra, empty ones, with no error.",
            "Take the characters with String.graphemes(\"abc\"), which gives [\"a\", \"b\", \"c\"].",
            """
            letters = String.graphemes("abc")
            IO.inspect(length(letters))
            """),

        GuideFor(["logic-elixir-atom-for-text-key"], "A map made with text keys, read with an atom",
            "%{\"Ada\" => 36} has the text \"Ada\" as its key. The atom :Ada is a different value, so ages[:Ada] finds no such key, and " +
            "gives nil.",
            "The value is never found - nil comes back - with no error.",
            "Read it with the key it was made with: ages[\"Ada\"] - or make the map with atom keys, %{Ada: 36}, and read ages[:Ada].",
            """
            ages = %{"Ada" => 36}
            IO.inspect(ages["Ada"])
            """),
    ];
}
