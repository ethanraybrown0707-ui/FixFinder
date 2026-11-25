using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// Decimals compared exactly in Go, PowerShell and Scala: each compared within a tiny tolerance instead - in Go with
/// math.Abs only when the program imports math, as a change to one line cannot add the import - and whole numbers, which
/// compare exactly, left alone.
/// </summary>
public class FloatToleranceTests : IDisposable
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

    private const string GoWithMath = "package main\n\nimport (\n\t\"fmt\"\n\t\"math\"\n)\n\nfunc main() {\n\ttotal := 0.1\n\ttotal += 0.2\n";
    private const string GoWithoutMath = "package main\n\nimport \"fmt\"\n\nfunc main() {\n\ttotal := 0.1\n\ttotal += 0.2\n";

    [Theory]
    [InlineData("main.go", GoWithMath + "\tif total == 0.3 {\n\t\tfmt.Println(\"equal\")\n\t}\n}\n", "logic-go-float-equality", 11, "\tif math.Abs(total-0.3) < 1e-9 {")]
    [InlineData("main.go", GoWithMath + "\tif total != 0.3 {\n\t\tfmt.Println(\"different\")\n\t}\n}\n", "logic-go-float-equality", 11, "\tif math.Abs(total-0.3) >= 1e-9 {")]
    [InlineData("main.go", GoWithoutMath + "\tif total == 0.3 {\n\t\tfmt.Println(\"equal\")\n\t}\n}\n", "logic-go-float-equality", 8, "\tif total-0.3 < 1e-9 && 0.3-total < 1e-9 {")]
    [InlineData("main.ps1", "$total = 0.1 + 0.2\nif ($total -ne 0.3) {\n    Write-Output \"different\"\n}\n", "logic-powershell-float-equality", 2, "if ([math]::Abs($total - 0.3) -ge 1e-9) {")]
    [InlineData("Main.scala", "@main def run(): Unit =\n  val total = 0.1 + 0.2\n  if total != 0.3 then println(\"different\")\n", "logic-scala-float-equality", 3, "  if math.abs(total - 0.3) >= 1e-9 then println(\"different\")")]
    public void ADecimalComparedExactlyIsComparedWithinATolerance(string fileName, string code, string check, int line, string compared)
    {
        var found = Assert.Single(Scan(fileName, code, check));

        Assert.Equal(line, found.Line);
        Assert.Equal([compared], found.Fix?.NewLines);
    }

    [Theory]
    [InlineData("main.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tcount := 3\n\tif count == 3 {\n\t\tfmt.Println(\"three\")\n\t}\n}\n", "logic-go-float-equality")]
    [InlineData("main.ps1", "$count = 3\nif ($count -eq 3) {\n    Write-Output \"three\"\n}\n", "logic-powershell-float-equality")]
    [InlineData("Main.scala", "@main def run(): Unit =\n  val count = 3\n  if count == 3 then println(\"three\")\n", "logic-scala-float-equality")]
    public void WholeNumbersComparedExactlyAreLeftAlone(string fileName, string code, string check)
    {
        Assert.Empty(Scan(fileName, code, check));
    }
}
