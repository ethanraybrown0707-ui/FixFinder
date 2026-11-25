using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// A function of the program's own called with fewer values than it takes, in JavaScript and PowerShell - which give the rest
/// undefined or $null and say nothing - found with no change, as only the writer knows what the missing value was; and left
/// alone where leaving a value out is meant: a default, a ...rest, arguments, a fallback, values given by name, and TypeScript,
/// whose compiler says so itself.
/// </summary>
public class TooFewArgumentsTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string fileName, string code, string check)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == check).ToList();
    }

    [Theory]
    [InlineData("main.js", "function add(a, b) {\n    return a + b;\n}\n\nconsole.log(add(1));\n", "logic-js-too-few-arguments", 5, "`add` takes 2 values - a, b - and is given 1 here, so `b` is undefined inside it")]
    [InlineData("main.js", "const area = (width, height) => width * height;\nconsole.log(area());\n", "logic-js-too-few-arguments", 2, "`area` takes 2 values - width, height - and is given 0 here, so `width` and `height` are undefined inside it")]
    [InlineData("main.ps1", "function Add($a, $b) {\n    return $a + $b\n}\n\nWrite-Output (Add 1)\n", "logic-powershell-too-few-arguments", 5, "Add takes 2 values - $a, $b - and is given 1 here, so $b is $null inside it")]
    [InlineData("main.ps1", "function Get-Area($width, $height) {\n    return $width * $height\n}\n\n$area = Get-Area 3\nWrite-Output $area\n", "logic-powershell-too-few-arguments", 5, "Get-Area takes 2 values - $width, $height - and is given 1 here, so $height is $null inside it")]
    public void AFunctionGivenFewerValuesThanItTakesIsFoundWithNoChange(string fileName, string code, string check, int line, string message)
    {
        var found = Assert.Single(Scan(fileName, code, check));

        Assert.Equal(line, found.Line);
        Assert.StartsWith(message, found.Message, StringComparison.Ordinal);
        Assert.Null(found.Fix);
    }

    [Theory]
    // Every value given.
    [InlineData("main.js", "function add(a, b) {\n    return a + b;\n}\n\nconsole.log(add(1, 2));\n", "logic-js-too-few-arguments")]
    // A default, and a ...rest, are values that may be left out.
    [InlineData("main.js", "function add(a, b = 0) {\n    return a + b;\n}\n\nconsole.log(add(1));\n", "logic-js-too-few-arguments")]
    [InlineData("main.js", "function sum(...marks) {\n    return marks.reduce((a, b) => a + b, 0);\n}\n\nconsole.log(sum(1));\n", "logic-js-too-few-arguments")]
    // A function that reads arguments, or gives the parameter a fallback, expects it to be left out.
    [InlineData("main.js", "function add(a, b) {\n    return arguments.length > 1 ? a + b : a;\n}\n\nconsole.log(add(1));\n", "logic-js-too-few-arguments")]
    [InlineData("main.js", "function greet(name, greeting) {\n    return (greeting ?? \"Hello\") + \", \" + name;\n}\n\nconsole.log(greet(\"Ada\"));\n", "logic-js-too-few-arguments")]
    // TypeScript's compiler says it itself: TS2554.
    [InlineData("main.ts", "function add(a: number, b: number): number {\n    return a + b;\n}\n\nconsole.log(add(1));\n", "logic-js-too-few-arguments")]
    // A method of the same name on an object is not the function.
    [InlineData("main.js", "function add(a, b) {\n    return a + b;\n}\n\nconst list = [];\nlist.add(1);\n", "logic-js-too-few-arguments")]
    [InlineData("main.ps1", "function Add($a, $b) {\n    return $a + $b\n}\n\nWrite-Output (Add 1 2)\n", "logic-powershell-too-few-arguments")]
    [InlineData("main.ps1", "function Add($a, $b = 0) {\n    return $a + $b\n}\n\nWrite-Output (Add 1)\n", "logic-powershell-too-few-arguments")]
    // Values given by name are not counted by place.
    [InlineData("main.ps1", "function Add($a, $b) {\n    return $a + $b\n}\n\nWrite-Output (Add -a 1 -b 2)\n", "logic-powershell-too-few-arguments")]
    public void LeavingAValueOutThatIsMeantIsLeftAlone(string fileName, string code, string check)
    {
        Assert.Empty(Scan(fileName, code, check));
    }
}
