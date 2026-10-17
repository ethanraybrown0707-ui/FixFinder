using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>A value compared with NaN in nine more languages: found, with the language's own test as the change, and left alone where NaN is tested for already.</summary>
public class NaNComparisonPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string fileName, string code, string patternId)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == patternId).ToList();
    }

    [Theory]
    [InlineData("main.py", "logic-python-nan-comparison", "import math\nif value == float(\"nan\"):\n    pass\n", "if math.isnan(value):")]
    [InlineData("main.py", "logic-python-nan-comparison", "import math\nif value != math.nan:\n    pass\n", "if not math.isnan(value):")]
    [InlineData("main.py", "logic-python-nan-comparison", "import numpy as np\nmissing = marks == np.nan\n", "missing = np.isnan(marks)")]
    [InlineData("main.go", "logic-go-nan-comparison", "package main\n\tif value == math.NaN() {\n", "\tif math.IsNaN(value) {")]
    [InlineData("main.rb", "logic-ruby-nan-comparison", "value = Float::NAN\nputs \"missing\" if value != Float::NAN\n", "puts \"missing\" if !value.nan?")]
    [InlineData("main.php", "logic-php-nan-comparison", "<?php\nif ($value === NAN) {\n", "if (is_nan($value)) {")]
    [InlineData("main.scala", "logic-scala-nan-comparison", "val value = Double.NaN\nif value == Double.NaN then println(\"missing\")\n", "if value.isNaN then println(\"missing\")")]
    [InlineData("main.fsx", "logic-fsharp-nan-comparison", "let value = nan\nif value = nan then printfn \"missing\"\n", "if System.Double.IsNaN value then printfn \"missing\"")]
    [InlineData("main.fsx", "logic-fsharp-nan-comparison", "let value = nan\nlet present = (value <> nan)\n", "let present = (not (System.Double.IsNaN value))")]
    [InlineData("main.ps1", "logic-powershell-nan-comparison", "$value = [double]::NaN\nif ($value -EQ [Double]::NaN) {\n", "if ([double]::IsNaN($value)) {")]
    [InlineData("main.c", "logic-c-nan-comparison", "#include <math.h>\n    if (value == NAN) {\n", "    if (isnan(value)) {")]
    [InlineData("main.cpp", "logic-cpp-nan-comparison", "#include <cmath>\n    if (value != NAN) {\n", "    if (!std::isnan(value)) {")]
    public void AComparisonWithNaNIsFoundAndBecomesTheLanguagesOwnTest(string fileName, string patternId, string code, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code, patternId));
        Assert.Equal(2, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Fact]
    public void PythonsFloatNaNInAFileWithoutImportMathIsFoundWithNoChange()
    {
        var found = Assert.Single(Scan("main.py", "value = float(\"nan\")\nprint(value == float(\"nan\"))\n", "logic-python-nan-comparison"));
        Assert.Equal(2, found.Line);
        Assert.Null(found.Fix);
        Assert.Contains("import math", found.Message);
    }

    [Theory]
    // The language's own test; a value given NaN; another number; and NaN inside text or a comment.
    [InlineData("main.py", "logic-python-nan-comparison", "import math\nvalue = float(\"nan\")\nprint(math.isnan(value), value == 0.5, value == float(\"0.5\"))\n# value == math.nan\n")]
    [InlineData("main.go", "logic-go-nan-comparison", "package main\n\nimport \"math\"\n\nfunc main() {\n\tvalue := math.NaN()\n\t_ = math.IsNaN(value)\n}\n")]
    [InlineData("main.rb", "logic-ruby-nan-comparison", "value = Float::NAN\nputs value.nan?\nputs 'value == Float::NAN'\n")]
    [InlineData("main.php", "logic-php-nan-comparison", "<?php\n$value = NAN;\nvar_dump(is_nan($value), '$value == NAN');\n")]
    // F#'s = giving a value its name is no comparison.
    [InlineData("main.fsx", "logic-fsharp-nan-comparison", "let value = nan\nlet missing = System.Double.IsNaN value\n")]
    [InlineData("main.ps1", "logic-powershell-nan-comparison", "$value = [double]::NaN\nWrite-Output ([double]::IsNaN($value))\n")]
    [InlineData("main.c", "logic-c-nan-comparison", "#include <math.h>\nint main(void) {\n    double value = NAN;\n    return isnan(value) ? 0 : 1;\n}\n")]
    public void NaNTestedForAlreadyIsLeftAlone(string fileName, string patternId, string code) =>
        Assert.Empty(Scan(fileName, code, patternId));
}

/// <summary>
/// The mistake and its change run with each language's own toolchain, as FixFinder Learn runs a lesson: for a value that is NaN,
/// the mistake takes the branch for a number, and the change the one for NaN. A language this computer cannot run is passed
/// over, and said to be.
/// </summary>
public class NaNComparisonLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string, string, string, int> Mistakes => new()
    {
        {
            "Python", "main.py", "logic-python-nan-comparison",
            "import math\nvalue = float(\"nan\")\nif value == float(\"nan\"):\n    print(\"not a number\")\nelse:\n    print(\"a number\")\n", 3
        },
        {
            "Go", "main.go", "logic-go-nan-comparison",
            "package main\n\nimport (\n\t\"fmt\"\n\t\"math\"\n)\n\nfunc main() {\n\tvalue := math.NaN()\n\tif value == math.NaN() {\n\t\tfmt.Println(\"not a number\")\n\t} else {\n\t\tfmt.Println(\"a number\")\n\t}\n}\n", 10
        },
        { "Ruby", "main.rb", "logic-ruby-nan-comparison", "value = Float::NAN\nif value == Float::NAN\n  puts \"not a number\"\nelse\n  puts \"a number\"\nend\n", 2 },
        {
            "PHP", "main.php", "logic-php-nan-comparison",
            "<?php\n$value = NAN;\nif ($value == NAN) {\n    echo \"not a number\";\n} else {\n    echo \"a number\";\n}\n", 3
        },
        {
            "Scala", "main.scala", "logic-scala-nan-comparison",
            "@main def run(): Unit =\n  val value = Double.NaN\n  if value == Double.NaN then println(\"not a number\") else println(\"a number\")\n", 3
        },
        { "F#", "main.fsx", "logic-fsharp-nan-comparison", "let value = nan\nif value = nan then printfn \"not a number\" else printfn \"a number\"\n", 2 },
        {
            "PowerShell", "main.ps1", "logic-powershell-nan-comparison",
            "$value = [double]::NaN\nif ($value -eq [double]::NaN) {\n    Write-Output \"not a number\"\n} else {\n    Write-Output \"a number\"\n}\n", 2
        },
        {
            "C", "main.c", "logic-c-nan-comparison",
            "#include <math.h>\n#include <stdio.h>\n\nint main(void) {\n    double value = NAN;\n    if (value == NAN) {\n        printf(\"not a number\\n\");\n    } else {\n        printf(\"a number\\n\");\n    }\n    return 0;\n}\n", 6
        },
        {
            "C++", "main.cpp", "logic-cpp-nan-comparison",
            "#include <cmath>\n#include <iostream>\n\nint main() {\n    double value = NAN;\n    if (value == NAN) {\n        std::cout << \"not a number\\n\";\n    } else {\n        std::cout << \"a number\\n\";\n    }\n}\n", 6
        },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakeTakesTheBranchForANumberAndTheChangeTheOneForNaN(string languageName, string fileName, string patternId, string code, int line)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);

        var language = CodeLanguage.All.Single(each => each.Name == languageName);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == patternId);
        Assert.Equal(line, found.Line);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines[found.Fix!.StartLine - 1] = found.Fix.NewLines.Single();
        var changed = string.Join("\n", lines) + "\n";

        var wrong = await SnippetRunner.RunAsync(language, code);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(language, changed);
        output.WriteLine($"the mistake: {wrong.Said(languageName)}\nthe change:  {right.Said(languageName)}");

        Assert.True(wrong.Shows(Behaviour.Printing("a number")), wrong.Said(languageName));
        Assert.True(right.Shows(Behaviour.Printing("not a number")), right.Said(languageName));
    }
}
