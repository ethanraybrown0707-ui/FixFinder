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
    ];
}
