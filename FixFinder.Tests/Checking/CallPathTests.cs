using FixFinder.Core.Checking;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// How a crash got to the line it stopped on, read from its own stack trace. Each trace here is the one a run of the
/// program gave on this computer - the same frames, in the same order, with the names the language gave them - so what is
/// checked is how FixFinder reads a real trace, not one made up to suit it.
/// </summary>
public class CallPathTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string fileName, string code)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return path;
    }

    private static ErrorFrame Frame(int order, string? symbol, string? file, int? line) =>
        new() { Order = order, Symbol = symbol, File = file, Line = line, RawLine = "" };

    private static ParsedError Stopped(string language, params ErrorFrame[] frames) => Stopped(language, "", frames);

    private static ParsedError Stopped(string language, string rawText, params ErrorFrame[] frames) => new()
    {
        LanguageId = language,
        Confidence = 90,
        RawText = rawText,
        FirstLineSequence = 0,
        Frames = frames,
    };

    private static string[] Steps(IReadOnlyList<PathStep> path) =>
        path.Select(step => $"{step.Depth} {step.Function} {step.Line} {step.Happened}").ToArray();

    [Fact]
    public void PhpsFunctionForALineIsTheOneTheNextFrameOutCalled()
    {
        var program = Write("scores.php", """
            <?php
            function average(array $values): int
            {
                $total = array_sum($values);
                return intdiv($total, count($values));
            }

            function loadAverage(string $name): int
            {
                $scores = [];
                echo "Loading $name", PHP_EOL;
                return average($scores);
            }

            function main(): void
            {
                $result = loadAverage("week one");
                echo "Average: $result", PHP_EOL;
            }

            main();
            """);

        // PHP names the function called at each line - "#1 scores.php(12): average(Array)" - and where it was thrown first.
        var error = Stopped("php",
            Frame(0, null, program, 5), Frame(1, "intdiv(0, 0)", program, 5), Frame(2, "average(Array)", program, 12),
            Frame(3, "loadAverage('week one')", program, 17), Frame(4, "main()", program, 21));

        Assert.Equal(
        [
            "0 <module> 21 calls `main()`",
            "1 main 17 calls `loadAverage(\"week one\")`",
            "2 loadAverage 12 calls `average($scores)`",
            "3 average 5 stops on `return intdiv($total, count($values));`",
        ], Steps(CallPath.From(error, [program])));
    }

    [Fact]
    public void ACSharpMethodIsNamedWithoutItsClassOrParameters()
    {
        var program = Write("Program.cs", """
            using System;
            using System.Collections.Generic;

            public static class Program
            {
                static int Average(List<int> values)
                {
                    var total = 0;
                    foreach (var value in values) total += value;
                    return total / values.Count;
                }

                static int LoadAverage(string name)
                {
                    var scores = new List<int>();
                    Console.WriteLine("Loading " + name);
                    return Average(scores);
                }

                public static void Main()
                {
                    var result = LoadAverage("week one");
                    Console.WriteLine("Average: " + result);
                }
            }
            """);

        // .NET's trace names each method with its class and its parameters' types.
        var error = Stopped("csharp",
            Frame(0, "Program.Average(List`1 values)", program, 10), Frame(1, "Program.LoadAverage(String name)", program, 17),
            Frame(2, "Program.Main()", program, 22));

        Assert.Equal(
        [
            "0 Main 22 calls `LoadAverage(\"week one\")`",
            "1 LoadAverage 17 calls `Average(scores)`",
            "2 Average 10 stops on `return total / values.Count;`",
        ], Steps(CallPath.From(error, [program])));
    }

    [Fact]
    public void ACallListedAtTheLineThatMadeItIsThatLine()
    {
        var program = Write("scores.rb", """
            def average(values)
              total = values.sum
              total / values.length
            end

            def load_average(name)
              scores = []
              puts "Loading #{name}"
              average(scores)
            end

            def main
              result = load_average("week one")
              puts "Average: #{result}"
            end

            main
            """);

        // Ruby lists Integer#/ at the line of average that divides, then average at that same line.
        var error = Stopped("ruby",
            Frame(0, "/", program, 3), Frame(1, "average", program, 3), Frame(2, "load_average", program, 9),
            Frame(3, "main", program, 13), Frame(4, "<main>", program, 17));

        Assert.Equal(
        [
            "0 <module> 17 calls `main`",
            "1 main 13 calls `load_average(\"week one\")`",
            "2 load_average 9 calls `average(scores)`",
            "3 average 3 stops on `total / values.length`",
        ], Steps(CallPath.From(error, [program])));
    }

    [Fact]
    public void ACallTheTraceLeavesOutIsNotFilledIn()
    {
        var program = Write("scores.lua", """
            local function average(values)
              local total = 0
              for _, value in ipairs(values) do total = total + value end
              return total // #values
            end

            local function loadAverage(name)
              local scores = {}
              print("Loading " .. name)
              return average(scores)
            end

            local function main()
              local result = loadAverage("week one")
              print("Average: " .. result)
            end

            main()
            """);

        // loadAverage ended by calling average, so Lua dropped it from the trace; average it names by the line it starts on.
        var error = Stopped("lua",
            Frame(0, null, program, 4), Frame(1, "function <scores.lua:1>", program, 4), Frame(2, "local 'main'", program, 14),
            Frame(3, "main chunk", program, 18), Frame(4, "[C] ?", null, null));

        Assert.Equal(
        [
            "0 <module> 18 calls `main()`",
            "1 main 14 runs `local result = loadAverage(\"week one\")`, which leads to `average()`",
            "2 average 4 stops on `return total // #values`",
        ], Steps(CallPath.From(error, [program])));
    }

    [Fact]
    public void ARecursionIsOneStepThatSaysHowManyCallsThereWere()
    {
        var program = Write("countdown.py", """
            def countdown(steps, left):
                if left == 0:
                    return steps[left]
                return countdown(steps, left - 1)


            def main():
                steps = []
                print(countdown(steps, 4))


            main()
            """);

        // Python 3.13's traceback: the same call listed three times, then how many more times it was repeated.
        var traceback = string.Join("\n",
            "Traceback (most recent call last):",
            $"  File \"{program}\", line 12, in <module>",
            "    main()",
            "    ~~~~^^",
            $"  File \"{program}\", line 9, in main",
            "    print(countdown(steps, 4))",
            "          ~~~~~~~~~^^^^^^^^^^",
            $"  File \"{program}\", line 4, in countdown",
            "    return countdown(steps, left - 1)",
            $"  File \"{program}\", line 4, in countdown",
            "    return countdown(steps, left - 1)",
            $"  File \"{program}\", line 4, in countdown",
            "    return countdown(steps, left - 1)",
            "  [Previous line repeated 1 more time]",
            $"  File \"{program}\", line 3, in countdown",
            "    return steps[left]",
            "           ~~~~~^^^^^^",
            "IndexError: list index out of range");

        var error = Stopped("python", traceback,
            Frame(0, "countdown", program, 3), Frame(1, "countdown", program, 4), Frame(2, "countdown", program, 4),
            Frame(3, "countdown", program, 4), Frame(4, "main", program, 9), Frame(5, "<module>", program, 12));

        Assert.Equal(
        [
            "0 <module> 12 calls `main()`",
            "1 main 9 calls `countdown(steps, 4)`",
            "2 countdown 4 calls `countdown(steps, left - 1)`, and so on - 4 of these calls, each inside the one before: the trace lists 3 and says the line was repeated 1 more time",
            "3 countdown 3 stops on `return steps[left]`",
        ], Steps(CallPath.From(error, [program])));
    }

    [Fact]
    public void ALineInAFileNotTheProgramsIsNotShown()
    {
        var program = Write("scores.py", "from helpers import average\n\n\nprint(average([]))\n");
        var elsewhere = Path.Combine(_temp.Path, "library", "helpers.py");

        // A file the program does not have: not the program's line, so one line is left, and one line alone is only the
        // line it stopped on.
        var error = Stopped("python", Frame(0, "average", elsewhere, 2), Frame(1, "<module>", program, 4));

        Assert.Empty(CallPath.From(error, [program]));
    }

    [Fact]
    public void OneLineAloneIsNoPath()
    {
        var program = Write("scores.pl", "my @values = ();\nmy $average = 0 / scalar(@values);\nprint $average;\n");

        // Perl's die names only the line it stopped on.
        Assert.Empty(CallPath.From(Stopped("perl", Frame(0, null, program, 2)), [program]));
    }

    [Theory]
    [InlineData("Scores.average", "average")]
    [InlineData("Program.Average(List`1 values)", "Average")]
    [InlineData("main.average", "average")]
    [InlineData("main::average", "average")]
    [InlineData("Scores.average/1", "average")]
    [InlineData("scores:average/1", "average")]
    [InlineData("FSI_0001.average(FSharpList`1 values)", "average")]
    [InlineData("average(Array)", "average")]
    [InlineData("local 'main'", "main")]
    [InlineData("<StartupCode$FSI_0001>.$FSI_0001.main@()", "<module>")]
    [InlineData("Object.<anonymous>", "<module>")]
    [InlineData("(file)", "<module>")]
    [InlineData("<main>", "<module>")]
    [InlineData("main chunk", "<module>")]
    [InlineData("thread 'main'", "")]
    public void EachLanguagesNameForAFunctionIsTheNameTheProgramGaveIt(string symbol, string function)
    {
        // Each name is one a trace of the scores program gave, in Java, C#, Go, Rust, Elixir, Erlang, F#, PHP, Lua, Node,
        // Ruby - and Rust's thread 'main', the panic's own line, which names no function.
        Assert.Equal(function, CallPath.NameOf(symbol, []));
    }
}
