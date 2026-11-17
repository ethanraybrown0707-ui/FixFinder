using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Reporting;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// The same program in each language whose crash lists the calls that led to it: main calls loadAverage, which calls
/// average, which divides by the length of an empty list - or, in JavaScript and TypeScript, reduces it - and stops. How
/// it got there is read from the run's own trace, each step a line of the program, outermost first. A call the trace left
/// out - Lua, Elixir and Erlang end loadAverage by calling average, so they drop it - is not filled in. A Python countdown
/// that calls itself four times from one line, then reads past an empty list, shows those calls as one step. C and C++ name the
/// calls in the AddressSanitizer build their silent crash is run again in. Each row is what FixFinder showed for the program
/// on this computer when these were written; a computer without the language passes it over.
/// </summary>
public class CallPathLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The file, the program, the line it stopped on, and How it got there, a step to a line as the report shows it.</summary>
    public static IEnumerable<object[]> Programs =>
    [
        new object[] { "scores.py", "def average(values):\n    total = sum(values)\n    return total // len(values)\n\n\ndef load_average(name):\n    scores = []\n    print(\"Loading\", name)\n    return average(scores)\n\n\ndef main():\n    result = load_average(\"week one\")\n    print(f\"Average: {result}\")\n\n\nmain()\n", 3,
            "0 The top level, line 17 - calls `main()`\n1 `main()`, line 13 - calls `load_average(\"week one\")`\n2 `load_average()`, line 9 - calls `average(scores)`\n3 `average()`, line 3 - stops on `return total // len(values)`" },
        new object[] { "scores_recursive.py", "def countdown(steps, left):\n    if left == 0:\n        return steps[left]\n    return countdown(steps, left - 1)\n\n\ndef main():\n    steps = []\n    print(countdown(steps, 4))\n\n\nmain()\n", 3,
            "0 The top level, line 12 - calls `main()`\n1 `main()`, line 9 - calls `countdown(steps, 4)`\n2 `countdown()`, line 4 - calls `countdown(steps, left - 1)`, and so on - 4 of these calls, each inside the one before: the trace lists 3 and says the line was repeated 1 more time\n3 `countdown()`, line 3 - stops on `return steps[left]`" },
        new object[] { "Scores.java", "import java.util.ArrayList;\nimport java.util.List;\n\npublic class Scores {\n    static int average(List<Integer> values) {\n        int total = 0;\n        for (int value : values) total += value;\n        return total / values.size();\n    }\n\n    static int loadAverage(String name) {\n        List<Integer> scores = new ArrayList<>();\n        System.out.println(\"Loading \" + name);\n        return average(scores);\n    }\n\n    public static void main(String[] args) {\n        int result = loadAverage(\"week one\");\n        System.out.println(\"Average: \" + result);\n    }\n}\n", 8,
            "0 `main()`, line 18 - calls `loadAverage(\"week one\")`\n1 `loadAverage()`, line 14 - calls `average(scores)`\n2 `average()`, line 8 - stops on `return total / values.size();`" },
        new object[] { "scores.js", "function average(values) {\n  const total = values.reduce((sum, value) => sum + value);\n  return total / values.length;\n}\n\nfunction loadAverage(name) {\n  const scores = [];\n  console.log(\"Loading \" + name);\n  return average(scores);\n}\n\nfunction main() {\n  const result = loadAverage(\"week one\");\n  console.log(\"Average: \" + result);\n}\n\nmain();\n", 2,
            "0 The top level, line 17 - calls `main()`\n1 `main()`, line 13 - calls `loadAverage(\"week one\")`\n2 `loadAverage()`, line 9 - calls `average(scores)`\n3 `average()`, line 2 - stops on `const total = values.reduce((sum, value) => sum + value);`" },
        new object[] { "scores.ts", "function average(values: number[]): number {\n  const total = values.reduce((sum, value) => sum + value);\n  return total / values.length;\n}\n\nfunction loadAverage(name: string): number {\n  const scores: number[] = [];\n  console.log(\"Loading \" + name);\n  return average(scores);\n}\n\nfunction main(): void {\n  const result = loadAverage(\"week one\");\n  console.log(\"Average: \" + result);\n}\n\nmain();\n", 2,
            "0 The top level, line 17 - calls `main()`\n1 `main()`, line 13 - calls `loadAverage(\"week one\")`\n2 `loadAverage()`, line 9 - calls `average(scores)`\n3 `average()`, line 2 - stops on `const total = values.reduce((sum, value) => sum + value);`" },
        new object[] { "main.go", "package main\n\nimport \"fmt\"\n\nfunc average(values []int) int {\n\ttotal := 0\n\tfor _, value := range values {\n\t\ttotal += value\n\t}\n\treturn total / len(values)\n}\n\nfunc loadAverage(name string) int {\n\tscores := []int{}\n\tfmt.Println(\"Loading\", name)\n\treturn average(scores)\n}\n\nfunc main() {\n\tresult := loadAverage(\"week one\")\n\tfmt.Println(\"Average:\", result)\n}\n", 10,
            "0 `main()`, line 20 - calls `loadAverage(\"week one\")`\n1 `loadAverage()`, line 16 - calls `average(scores)`\n2 `average()`, line 10 - stops on `return total / len(values)`" },
        new object[] { "main.rs", "fn average(values: &[i32]) -> i32 {\n    let total: i32 = values.iter().sum();\n    total / values.len() as i32\n}\n\nfn load_average(name: &str) -> i32 {\n    let scores: Vec<i32> = Vec::new();\n    println!(\"Loading {}\", name);\n    average(&scores)\n}\n\nfn main() {\n    let result = load_average(\"week one\");\n    println!(\"Average: {}\", result);\n}\n", 3,
            "0 `main()`, line 13 - calls `load_average(\"week one\")`\n1 `load_average()`, line 9 - calls `average(&scores)`\n2 `average()`, line 3 - stops on `total / values.len() as i32`" },
        new object[] { "scores.c", "#include <stdio.h>\n\nint average(const int *values, int count) {\n    int total = 0;\n    for (int index = 0; index < count; index++) total += values[index];\n    return total / count;\n}\n\nint load_average(const char *name) {\n    int scores[10] = {0};\n    int count = 0;\n    printf(\"Loading %s\\n\", name);\n    return average(scores, count);\n}\n\nint main(void) {\n    int result = load_average(\"week one\");\n    printf(\"Average: %d\\n\", result);\n    return 0;\n}\n", 6,
            "0 `main()`, line 17 - calls `load_average(\"week one\")`\n1 `load_average()`, line 13 - calls `average(scores, count)`\n2 `average()`, line 6 - stops on `return total / count;`" },
        new object[] { "scores.cpp", "#include <cstdio>\n\nint average(const int *values, int count) {\n    int total = 0;\n    for (int index = 0; index < count; index++) total += values[index];\n    return total / count;\n}\n\nint load_average(const char *name) {\n    int scores[10] = {0};\n    int count = 0;\n    printf(\"Loading %s\\n\", name);\n    return average(scores, count);\n}\n\nint main(void) {\n    int result = load_average(\"week one\");\n    printf(\"Average: %d\\n\", result);\n    return 0;\n}\n", 6,
            "0 `main()`, line 17 - calls `load_average(\"week one\")`\n1 `load_average()`, line 13 - calls `average(scores, count)`\n2 `average()`, line 6 - stops on `return total / count;`" },
        new object[] { "scores.php", "<?php\nfunction average(array $values): int\n{\n    $total = array_sum($values);\n    return intdiv($total, count($values));\n}\n\nfunction loadAverage(string $name): int\n{\n    $scores = [];\n    echo \"Loading $name\", PHP_EOL;\n    return average($scores);\n}\n\nfunction main(): void\n{\n    $result = loadAverage(\"week one\");\n    echo \"Average: $result\", PHP_EOL;\n}\n\nmain();\n", 5,
            "0 The top level, line 21 - calls `main()`\n1 `main()`, line 17 - calls `loadAverage(\"week one\")`\n2 `loadAverage()`, line 12 - calls `average($scores)`\n3 `average()`, line 5 - stops on `return intdiv($total, count($values));`" },
        new object[] { "scores.dart", "int average(List<int> values) {\n  var total = 0;\n  for (var value in values) total += value;\n  return total ~/ values.length;\n}\n\nint loadAverage(String name) {\n  var scores = <int>[];\n  print('Loading $name');\n  return average(scores);\n}\n\nvoid main() {\n  var result = loadAverage('week one');\n  print('Average: $result');\n}\n", 4,
            "0 `main()`, line 14 - calls `loadAverage('week one')`\n1 `loadAverage()`, line 10 - calls `average(scores)`\n2 `average()`, line 4 - stops on `return total ~/ values.length;`" },
        new object[] { "scores.lua", "local function average(values)\n  local total = 0\n  for _, value in ipairs(values) do total = total + value end\n  return total // #values\nend\n\nlocal function loadAverage(name)\n  local scores = {}\n  print(\"Loading \" .. name)\n  return average(scores)\nend\n\nlocal function main()\n  local result = loadAverage(\"week one\")\n  print(\"Average: \" .. result)\nend\n\nmain()\n", 4,
            "0 The top level, line 18 - calls `main()`\n1 `main()`, line 14 - runs `local result = loadAverage(\"week one\")`, which leads to `average()`\n2 `average()`, line 4 - stops on `return total // #values`" },
        new object[] { "scores.rb", "def average(values)\n  total = values.sum\n  total / values.length\nend\n\ndef load_average(name)\n  scores = []\n  puts \"Loading #{name}\"\n  average(scores)\nend\n\ndef main\n  result = load_average(\"week one\")\n  puts \"Average: #{result}\"\nend\n\nmain\n", 3,
            "0 The top level, line 17 - calls `main`\n1 `main()`, line 13 - calls `load_average(\"week one\")`\n2 `load_average()`, line 9 - calls `average(scores)`\n3 `average()`, line 3 - stops on `total / values.length`" },
        new object[] { "scores.exs", "defmodule Scores do\n  def average(values) do\n    total = Enum.sum(values)\n    div(total, length(values))\n  end\n\n  def load_average(name) do\n    scores = []\n    IO.puts(\"Loading #{name}\")\n    average(scores)\n  end\n\n  def main do\n    result = load_average(\"week one\")\n    IO.puts(\"Average: #{result}\")\n  end\nend\n\nScores.main()\n", 4,
            "0 The top level, line 19 - calls `Scores.main()`\n1 `main()`, line 14 - runs `result = load_average(\"week one\")`, which leads to `average()`\n2 `average()`, line 4 - stops on `div(total, length(values))`" },
        new object[] { "scores.erl", "-module(scores).\n-export([main/0]).\n\naverage(Values) ->\n    Total = lists:sum(Values),\n    Total div length(Values).\n\nload_average(Name) ->\n    Scores = [],\n    io:format(\"Loading ~s~n\", [Name]),\n    Result = average(Scores),\n    Result.\n\nmain() ->\n    Result = load_average(\"week one\"),\n    io:format(\"Average: ~p~n\", [Result]).\n", 6,
            "0 `main()`, line 15 - runs `Result = load_average(\"week one\"),`, which leads to `average()`\n1 `average()`, line 6 - stops on `Total div length(Values).`" },
        new object[] { "scores.fsx", "let average (values: int list) =\n    let total = List.sum values\n    total / List.length values\n\nlet loadAverage (name: string) =\n    let scores: int list = []\n    printfn \"Loading %s\" name\n    average scores\n\nlet main () =\n    let result = loadAverage \"week one\"\n    printfn \"Average: %d\" result\n\nmain ()\n", 3,
            "0 The top level, line 14 - calls `main ()`\n1 `main()`, line 11 - calls `loadAverage()` in `let result = loadAverage \"week one\"`\n2 `loadAverage()`, line 8 - calls `average scores`\n3 `average()`, line 3 - stops on `total / List.length values`" },
    ];

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task HowItGotThereIsTheCallsTheTraceListed(string fileName, string code, int crashLine, string steps)
    {
        var program = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(program, code);

        await AssertShownAsync(program, crashLine, steps);
        Assert.Equal(code, File.ReadAllText(program));
    }

    [Fact]
    public async Task APathThroughTwoFilesNamesTheFileOfEachStep()
    {
        var program = Path.Combine(_temp.Path, "main.py");
        File.WriteAllText(program, "from stats import average\n\n\ndef load_average(name):\n    scores = []\n    print(\"Loading\", name)\n    return average(scores)\n\n\nprint(\"Average:\", load_average(\"week one\"))\n");
        File.WriteAllText(Path.Combine(_temp.Path, "stats.py"), "def average(values):\n    total = sum(values)\n    return total // len(values)\n");

        await AssertShownAsync(program, 3,
            "0 The top level in main.py, line 10 - calls `load_average(\"week one\")`\n1 `load_average()` in main.py, line 7 - calls `average(scores)`\n2 `average()` in stats.py, line 3 - stops on `return total // len(values)`");
    }

    private async Task AssertShownAsync(string program, int crashLine, string steps)
    {
        var launch = TargetFactory.FromFile(program, LiveAllowance.For(program, TimeSpan.FromMinutes(2)));
        if (!launch.Ok)
        {
            output.WriteLine($"Not run on this computer: {launch.Problem}");
            return;
        }

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()).CheckAsync(launch);
        var stopped = report.Findings.SingleOrDefault(finding => FindingLayout.Path(finding).Count > 0);
        Assert.True(stopped is not null, $"no path among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");

        var shown = FindingLayout.Path(stopped!).Select(step => $"{step.Depth} {FindingLayout.PathLine(step)}").ToList();
        foreach (var step in shown) output.WriteLine(step);
        Assert.Equal(crashLine, stopped!.Line);
        Assert.Equal(steps.Split('\n'), shown);
    }
}
