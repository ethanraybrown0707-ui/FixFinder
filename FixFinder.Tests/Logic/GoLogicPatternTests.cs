using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// The logic checks of Go programs: each shape found where it is a mistake, with its change, and left alone where it is
/// not. That each mistake does what its check says, with go build and go vet saying nothing, is GoLogicLiveTests'.
/// </summary>
public class GoLogicPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string code)
    {
        var path = Path.Combine(_temp.Path, "main.go");
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!);
    }

    private static string InsideMain(string body, string imports = "import \"fmt\"\n\n") =>
        "package main\n\n" + imports + "func main() {\n" + body + "}\n";

    [Fact]
    public void ALoopToTheLengthThatUsesItsNumberAsAPositionIsFound()
    {
        var findings = Scan(InsideMain("\tmarks := []int{70, 80, 90}\n\tfor i := 0; i <= len(marks); i++ {\n\t\tfmt.Println(marks[i])\n\t}\n"));

        var loop = Assert.Single(findings, finding => finding.PatternId == "logic-go-loop-to-length");
        Assert.Equal(7, loop.Line);
        Assert.Equal("\tfor i := 0; i < len(marks); i++ {", loop.Fix?.NewLines.Single());
    }

    [Theory]
    [InlineData("\tmarks := []int{70, 80, 90}\n\tfor i := 0; i < len(marks); i++ {\n\t\tfmt.Println(marks[i])\n\t}\n")]
    [InlineData("\tmarks := []int{70, 80, 90}\n\tfor i := 0; i <= len(marks); i++ {\n\t\tfmt.Println(i)\n\t}\n")]
    public void ALoopThatStopsInTimeOrDoesNotIndexWithItsNumberIsLeftAlone(string body)
    {
        Assert.DoesNotContain(Scan(InsideMain(body)), finding => finding.PatternId == "logic-go-loop-to-length");
    }

    [Theory]
    [InlineData("\ttotal := 3\n\tcount := 2\n\taverage := float64(total / count)\n\tfmt.Println(average)\n", "\taverage := float64(total) / float64(count)")]
    [InlineData("\tmarks := []int{1, 2}\n\ttotal := 0\n\tfmt.Println(float32(total / len(marks)))\n", "\tfmt.Println(float32(total) / float32(len(marks)))")]
    public void AWholeNumberDivisionMadeAFloatAfterwardsIsFound(string body, string fixedLine)
    {
        var division = Assert.Single(Scan(InsideMain(body)), finding => finding.PatternId == "logic-go-integer-average");

        Assert.Equal(fixedLine, division.Fix?.NewLines.Single());
    }

    [Fact]
    public void AWholeNumberDivisionOfAParameterIsFoundAndOneOfFloatsOrUnknownsIsLeftAlone()
    {
        var parameters = Scan("package main\n\nfunc average(total, count int) float64 {\n\treturn float64(total / count)\n}\n\nfunc main() {}\n");
        var floats = Scan(InsideMain("\ttotal := 3.0\n\tcount := 2\n\tfmt.Println(float64(total / count))\n"));
        var unknown = Scan("package main\n\nimport \"fmt\"\n\nfunc main() {\n\ttotal := sum()\n\tcount := 2\n\tfmt.Println(float64(total / count))\n}\n\nfunc sum() int { return 3 }\n");

        Assert.Single(parameters, finding => finding.PatternId == "logic-go-integer-average");
        Assert.DoesNotContain(floats, finding => finding.PatternId == "logic-go-integer-average");
        Assert.DoesNotContain(unknown, finding => finding.PatternId == "logic-go-integer-average");
    }

    [Fact]
    public void ANewValueGivenToARangeLoopsCopyAndNeverUsedIsFoundAndPutInTheSlice()
    {
        var unnamed = Scan(InsideMain("\tmarks := []int{70, 80, 90}\n\tfor _, mark := range marks {\n\t\tmark = mark + 5\n\t}\n\tfmt.Println(marks)\n"));
        var named = Scan(InsideMain("\tmarks := []int{70, 80, 90}\n\tfor i, mark := range marks {\n\t\tfmt.Println(i)\n\t\tmark += 5\n\t}\n\tfmt.Println(marks)\n"));

        var copyChanged = Assert.Single(unnamed, finding => finding.PatternId == "logic-go-range-value-changed");
        Assert.Equal(8, copyChanged.Line);
        Assert.Equal(7, copyChanged.Fix?.StartLine);
        Assert.Equal(new[] { "\tfor i, mark := range marks {", "\t\tmarks[i] = mark + 5" }, copyChanged.Fix?.NewLines);

        // Nothing uses mark once the line is marks[i] += 5, and Go refuses a name declared and not used, so mark goes.
        var namedChanged = Assert.Single(named, finding => finding.PatternId == "logic-go-range-value-changed");
        Assert.Equal(7, namedChanged.Fix?.StartLine);
        Assert.Equal(new[] { "\tfor i := range marks {", "\t\tfmt.Println(i)", "\t\tmarks[i] += 5" }, namedChanged.Fix?.NewLines);
    }

    [Fact]
    public void AMapValueCountedUpInTheLoopsCopyIsCountedUpInTheMap()
    {
        var findings = Scan(InsideMain("\tages := map[string]int{\"Ada\": 36}\n\tfor name, age := range ages {\n\t\tfmt.Println(name)\n\t\tage++\n\t}\n\tfmt.Println(ages)\n"));

        var copyChanged = Assert.Single(findings, finding => finding.PatternId == "logic-go-range-value-changed");
        Assert.Equal(new[] { "\tfor name := range ages {", "\t\tfmt.Println(name)", "\t\tages[name]++" }, copyChanged.Fix?.NewLines);
    }

    [Theory]
    [InlineData("\tlines := []string{\" a \"}\n\tfor _, line := range lines {\n\t\tline = line + \"!\"\n\t\tfmt.Println(line)\n\t}\n")]
    [InlineData("\tcounts := []int{3}\n\tfor _, count := range counts {\n\t\tfor count > 0 {\n\t\t\tcount = count - 1\n\t\t}\n\t}\n")]
    [InlineData("\tmarks := []int{1}\n\tfor _, mark := range marks {\n\t\tdefer func() { fmt.Println(mark) }()\n\t\tmark = 2\n\t}\n")]
    public void ARangeValueUsedAfterItIsChangedOrCountedDownInAnInnerLoopOrKeptByAClosureIsLeftAlone(string body)
    {
        Assert.DoesNotContain(Scan(InsideMain(body)), finding => finding.PatternId == "logic-go-range-value-changed");
    }

    [Fact]
    public void ARangeValueChangedInAStringIsFoundWithNoChangeAsItsBytesCannotBeChanged()
    {
        var findings = Scan(InsideMain("\tname := \"ada\"\n\tfor _, letter := range name {\n\t\tletter = letter + 1\n\t}\n\tfmt.Println(name)\n"));

        var copyChanged = Assert.Single(findings, finding => finding.PatternId == "logic-go-range-value-changed");
        Assert.Null(copyChanged.Fix);
    }

    [Theory]
    [InlineData("Atoi(\"twelve\")", "is 0")]
    [InlineData("ParseFloat(\"cheap\", 64)", "is 0")]
    [InlineData("ParseBool(\"maybe\")", "is false")]
    public void AConversionWhoseErrorIsThrownAwayIsFoundWithWhatTheValueThenIs(string call, string value)
    {
        var findings = Scan(InsideMain($"\tage, _ := strconv.{call}\n\tfmt.Println(age)\n", "import (\n\t\"fmt\"\n\t\"strconv\"\n)\n\n"));

        var ignored = Assert.Single(findings, finding => finding.PatternId == "logic-go-parse-error-ignored");
        Assert.Contains($"age {value}", ignored.Message);
        Assert.Null(ignored.Fix);
    }

    [Fact]
    public void AConversionWhoseErrorIsKeptIsLeftAlone()
    {
        var findings = Scan(InsideMain("\tage, err := strconv.Atoi(\"12\")\n\tfmt.Println(age, err)\n", "import (\n\t\"fmt\"\n\t\"strconv\"\n)\n\n"));

        Assert.DoesNotContain(findings, finding => finding.PatternId == "logic-go-parse-error-ignored");
    }

    [Theory]
    [InlineData("\tname := \"ada\"\n\tstrings.ToUpper(name)\n\tfmt.Println(name)\n", "\tname = strings.ToUpper(name)")]
    [InlineData("\tname := \" ada \"\n\tstrings.Replace(name, \"a\", \"A\", 1)\n\tfmt.Println(name)\n", "\tname = strings.Replace(name, \"a\", \"A\", 1)")]
    public void AStringsCopyMadeOnALineOfItsOwnIsFoundAndKept(string body, string fixedLine)
    {
        var findings = Scan(InsideMain(body, "import (\n\t\"fmt\"\n\t\"strings\"\n)\n\n"));

        var discarded = Assert.Single(findings, finding => finding.PatternId == "logic-go-result-discarded");
        Assert.Equal(fixedLine, discarded.Fix?.NewLines.Single());
    }

    [Fact]
    public void AStringsCopyThatIsKeptIsLeftAlone()
    {
        var findings = Scan(InsideMain("\tname := \"ada\"\n\tname = strings.ToUpper(name)\n\tfmt.Println(strings.TrimSpace(name))\n", "import (\n\t\"fmt\"\n\t\"strings\"\n)\n\n"));

        Assert.DoesNotContain(findings, finding => finding.PatternId == "logic-go-result-discarded");
    }
}
