using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Reporting;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// The same program in each language a copy of the program follows: describe(21, readings) passes over an if and an else if,
/// both false as temperature is 21, into the else, where a while reads one item past the end. Why the line ran is each of
/// those decisions with what its condition read - an if chain's names printed by a copy just before its if, a while's at the
/// start of its body, in the call that stopped. Each row is what FixFinder showed for the program on this computer when these
/// were written; a computer without the language passes it over.
/// </summary>
public class BraceBranchesLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The file, the program, the line it stopped on, and Why this line ran, a decision to a line as the report shows it.</summary>
    public static IEnumerable<object[]> Programs =>
    [
        new object[] { "Weather.java", "public class Weather {\n    static int describe(int temperature, int[] readings) {\n        if (temperature < 0) {\n            return readings[0];\n        } else if (temperature < 15) {\n            return readings[1];\n        } else {\n            int index = 0;\n            int total = 0;\n            while (index <= readings.length) {\n                total += readings[index];\n                index++;\n            }\n            return total;\n        }\n    }\n\n    public static void main(String[] args) {\n        System.out.println(describe(21, new int[] {3, 4, 5}));\n    }\n}\n", 11,
            "`if (temperature < 0)` on line 3 was false - `temperature` was `21`\n`else if (temperature < 15)` on line 5 was false - `temperature` was `21`\n`else` on line 7 ran, as the conditions above it were false\n`while (index <= readings.length)` on line 10 was true - `index` was `3`, `readings` was `[3, 4, 5]`" },
        new object[] { "Program.cs", "using System;\n\npublic static class Program\n{\n    static int Describe(int temperature, int[] readings)\n    {\n        if (temperature < 0)\n        {\n            return readings[0];\n        }\n        else if (temperature < 15)\n        {\n            return readings[1];\n        }\n        else\n        {\n            var index = 0;\n            var total = 0;\n            while (index <= readings.Length)\n            {\n                total += readings[index];\n                index++;\n            }\n            return total;\n        }\n    }\n\n    public static void Main()\n    {\n        Console.WriteLine(Describe(21, new[] { 3, 4, 5 }));\n    }\n}\n", 21,
            "`if (temperature < 0)` on line 7 was false - `temperature` was `21`\n`else if (temperature < 15)` on line 11 was false - `temperature` was `21`\n`else` on line 15 ran, as the conditions above it were false\n`while (index <= readings.Length)` on line 19 was true - `index` was `3`, `readings` was `[3, 4, 5]`" },
        new object[] { "weather.js", "function describe(temperature, readings) {\n  if (temperature < 0) {\n    return readings[0].value;\n  } else if (temperature < 15) {\n    return readings[1].value;\n  } else {\n    let index = 0;\n    let total = 0;\n    while (index <= readings.length) {\n      total += readings[index].value;\n      index++;\n    }\n    return total;\n  }\n}\n\nconsole.log(describe(21, [{ value: 3 }, { value: 4 }, { value: 5 }]));\n", 10,
            "`if (temperature < 0)` on line 2 was false - `temperature` was `21`\n`else if (temperature < 15)` on line 4 was false - `temperature` was `21`\n`else` on line 6 ran, as the conditions above it were false\n`while (index <= readings.length)` on line 9 was true - `index` was `3`, `readings` was `[{\"value\":3},{\"value\":4},{\"value\":5}]`" },
        new object[] { "weather.ts", "interface Reading {\n  value: number;\n}\n\nfunction describe(temperature: number, readings: Reading[]): number {\n  if (temperature < 0) {\n    return readings[0].value;\n  } else if (temperature < 15) {\n    return readings[1].value;\n  } else {\n    let index = 0;\n    let total = 0;\n    while (index <= readings.length) {\n      total += readings[index].value;\n      index++;\n    }\n    return total;\n  }\n}\n\nconsole.log(describe(21, [{ value: 3 }, { value: 4 }, { value: 5 }]));\n", 14,
            "`if (temperature < 0)` on line 6 was false - `temperature` was `21`\n`else if (temperature < 15)` on line 8 was false - `temperature` was `21`\n`else` on line 10 ran, as the conditions above it were false\n`while (index <= readings.length)` on line 13 was true - `index` was `3`, `readings` was `[{\"value\":3},{\"value\":4},{\"value\":5}]`" },
        new object[] { "main.go", "package main\n\nimport \"fmt\"\n\nfunc describe(temperature int, readings []int) int {\n\tif temperature < 0 {\n\t\treturn readings[0]\n\t} else if temperature < 15 {\n\t\treturn readings[1]\n\t} else {\n\t\tindex := 0\n\t\ttotal := 0\n\t\tfor index <= len(readings) {\n\t\t\ttotal += readings[index]\n\t\t\tindex++\n\t\t}\n\t\treturn total\n\t}\n}\n\nfunc main() {\n\tfmt.Println(describe(21, []int{3, 4, 5}))\n}\n", 14,
            "`if temperature < 0` on line 6 was false - `temperature` was `21`\n`else if temperature < 15` on line 8 was false - `temperature` was `21`\n`else` on line 10 ran, as the conditions above it were false\n`for index <= len(readings)` on line 13 was true - `index` was `3`, `readings` was `[3 4 5]`" },
        new object[] { "main.rs", "fn describe(temperature: i32, readings: &[i32]) -> i32 {\n    if temperature < 0 {\n        readings[0]\n    } else if temperature < 15 {\n        readings[1]\n    } else {\n        let mut index = 0;\n        let mut total = 0;\n        while index <= readings.len() {\n            total += readings[index];\n            index += 1;\n        }\n        total\n    }\n}\n\nfn main() {\n    println!(\"{}\", describe(21, &[3, 4, 5]));\n}\n", 10,
            "`if temperature < 0` on line 2 was false - `temperature` was `21`\n`else if temperature < 15` on line 4 was false - `temperature` was `21`\n`else` on line 6 ran, as the conditions above it were false\n`while index <= readings.len()` on line 9 was true - `index` was `3`, `readings` was `[3, 4, 5]`" },
        new object[] { "weather.php", "<?php\nfunction describe(int $temperature, array $readings): int\n{\n    if ($temperature < 0) {\n        return $readings[0];\n    } elseif ($temperature < 15) {\n        return $readings[1];\n    } else {\n        $index = 0;\n        $total = 0;\n        while ($index <= count($readings)) {\n            $total += 100 % $readings[$index];\n            $index++;\n        }\n        return $total;\n    }\n}\n\necho describe(21, [3, 4, 5]), PHP_EOL;\n", 12,
            "`if ($temperature < 0)` on line 4 was false - `$temperature` was `21`\n`elseif ($temperature < 15)` on line 6 was false - `$temperature` was `21`\n`else` on line 8 ran, as the conditions above it were false\n`while ($index <= count($readings))` on line 11 was true - `$index` was `3`, `$readings` was `[3,4,5]`" },
        new object[] { "weather.dart", "int describe(int temperature, List<int> readings) {\n  if (temperature < 0) {\n    return readings[0];\n  } else if (temperature < 15) {\n    return readings[1];\n  } else {\n    var index = 0;\n    var total = 0;\n    while (index <= readings.length) {\n      total += readings[index];\n      index++;\n    }\n    return total;\n  }\n}\n\nvoid main() {\n  print(describe(21, [3, 4, 5]));\n}\n", 10,
            "`if (temperature < 0)` on line 2 was false - `temperature` was `21`\n`else if (temperature < 15)` on line 4 was false - `temperature` was `21`\n`else` on line 6 ran, as the conditions above it were false\n`while (index <= readings.length)` on line 9 was true - `index` was `3`, `readings` was `[3, 4, 5]`" },
        new object[] { "weather.lua", "local function describe(temperature, readings)\n  if temperature < 0 then\n    return readings[1]\n  elseif temperature < 15 then\n    return readings[2]\n  else\n    local index = 1\n    local total = 0\n    while index <= #readings + 1 do\n      total = total + readings[index]\n      index = index + 1\n    end\n    return total\n  end\nend\n\nprint(describe(21, {3, 4, 5}))\n", 10,
            "`if temperature < 0 then` on line 2 was false - `temperature` was `21`\n`elseif temperature < 15 then` on line 4 was false - `temperature` was `21`\n`else` on line 6 ran, as the conditions above it were false\n`while index <= #readings + 1 do` on line 9 was true - `index` was `4`, `readings` was `{3, 4, 5}`" },
        new object[] { "weather.rb", "def describe(temperature, readings)\n  if temperature < 0\n    readings[0]\n  elsif temperature < 15\n    readings[1]\n  else\n    index = 0\n    total = 0\n    while index <= readings.length\n      total += readings[index]\n      index += 1\n    end\n    total\n  end\nend\n\nputs describe(21, [3, 4, 5])\n", 10,
            "`if temperature < 0` on line 2 was false - `temperature` was `21`\n`elsif temperature < 15` on line 4 was false - `temperature` was `21`\n`else` on line 6 ran, as the conditions above it were false\n`while index <= readings.length` on line 9 was true - `index` was `3`, `readings` was `[3, 4, 5]`" },
        new object[] { "weather.pl", "use strict;\nuse warnings;\n\nsub describe {\n    my ($temperature, @readings) = @_;\n    if ($temperature < 0) {\n        return $readings[0];\n    } elsif ($temperature < 15) {\n        return $readings[1];\n    } else {\n        my $index = 0;\n        my $total = 0;\n        while ($index <= scalar(@readings)) {\n            $total += 100 / $readings[$index];\n            $index++;\n        }\n        return $total;\n    }\n}\n\nprint describe(21, 3, 4, 5), \"\\n\";\n", 14,
            "`if ($temperature < 0)` on line 6 was false - `$temperature` was `21`\n`elsif ($temperature < 15)` on line 8 was false - `$temperature` was `21`\n`else` on line 10 ran, as the conditions above it were false\n`while ($index <= scalar(@readings))` on line 13 was true - `$index` was `3`" },
    ];

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task EachDecisionIsShownWithWhatItsConditionRead(string fileName, string code, int crashLine, string why)
    {
        var program = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(program, code);

        var launch = TargetFactory.FromFile(program, LiveAllowance.For(program, TimeSpan.FromMinutes(2)));
        if (!launch.Ok)
        {
            output.WriteLine($"Not run on this computer: {launch.Problem}");
            return;
        }

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()).CheckAsync(launch);
        var stopped = report.Findings.SingleOrDefault(finding => finding.Branches.Count > 0);
        Assert.True(stopped is not null, $"no decisions among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");

        var shown = FindingLayout.WhyThisLineRan(stopped!);
        foreach (var decision in shown) output.WriteLine(decision);
        Assert.Equal(crashLine, stopped!.Line);
        Assert.Equal(why.Split('\n'), shown);
        Assert.Equal(code, File.ReadAllText(program));
    }
}
