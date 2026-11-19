using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Dynamic;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// What the analyses predicted of a Go function, tried for real: the function is called with the witness's values from a
/// small file built beside a copy of its own, and only a run that panics as predicted on the predicted line makes the
/// finding certain. A method, and a file that does not build on its own, are not run, and their findings stay as they
/// were. The words each certain finding carries are those the runs here gave.
/// </summary>
public class GoConfirmationTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Tried(string fileName, string code)
    {
        var path = Path.Combine(_temp.Path, fileName);
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        if (GoFrontend.FindGo(path) is not { } go) return null;

        var program = await GoFrontend.ReadAsync([path], go);
        var findings = AbstractChecks.Run(program, new SourceText());
        return await GoConfirmation.ConfirmAsync(findings, program, go, CancellationToken.None);
    }

    [Fact]
    public async Task ADivisionByASlicesLengthIsRunWithAnEmptySliceAndBecomesCertain()
    {
        const string code = "package main\n\nimport (\n\t\"fmt\"\n\t\"strings\"\n)\n" +
                            "\nfunc average(marks []int) int {\n\ttotal := 0\n\tfor _, mark := range marks {\n\t\ttotal += mark\n\t}\n" +
                            "\treturn total / len(marks)\n}\n\nfunc first(marks []int) int {\n\treturn marks[0]\n}\n" +
                            "\nfunc upper(name string) string {\n\tvar chosen *string\n\tif len(name) > 3 {\n\t\tchosen = &name\n\t}\n" +
                            "\treturn strings.ToUpper(*chosen)\n}\n\nfunc main() {\n\tfmt.Println(average([]int{40, 60}), first([]int{4}), upper(\"annabel\"))\n}\n";
        if (await Tried("marks.go", code) is not { } findings) return;

        var division = Assert.Single(findings, finding => finding.CheckId == "analysis-division-by-zero");
        Assert.Equal(Confidence.Certain, division.Confidence);
        Assert.Equal("Running `average([]int{})` stopped with a panic - runtime error: integer divide by zero - on line 13.", division.Confirmation);
    }

    [Fact]
    public async Task ParametersTheWitnessLeavesFreeAreGivenGosZeroValues()
    {
        const string code = "package main\n\nimport \"fmt\"\n\nfunc share(prize, winners int, label string) int {\n\tif winners == 0 {\n" +
                            "\t\tfmt.Println(\"no winners for\", label)\n\t}\n\treturn prize / winners\n}\n\nfunc letter(word string) byte {\n" +
                            "\tif len(word) == 0 {\n\t\tfmt.Println(\"empty\")\n\t}\n\treturn word[0]\n}\n\n" +
                            "func pick(names []string, at int) string {\n\tif at >= len(names) {\n\t\tfmt.Println(\"past the end\")\n\t}\n\treturn names[at]\n}\n" +
                            "\nfunc main() {\n\tfmt.Println(share(100, 4, \"cup\"), letter(\"go\"), pick([]string{\"a\"}, 0))\n}\n";
        if (await Tried("shares.go", code) is not { } findings) return;

        var division = Assert.Single(findings, finding => finding.CheckId == "analysis-division-by-zero");
        Assert.Equal("`winners` is 0", division.Witness);
        Assert.Equal(Confidence.Certain, division.Confidence);
        Assert.Equal("Running `share(0, 0, \"\")` stopped with a panic - runtime error: integer divide by zero - on line 9.", division.Confirmation);

        var letter = Assert.Single(findings, finding => finding.CheckId == "analysis-index-out-of-range" && finding.Span.Line == 16);
        Assert.Equal(Confidence.Certain, letter.Confidence);
        Assert.Equal("Running `letter(\"\")` stopped with a panic - runtime error: index out of range [0] with length 0 - on line 16.", letter.Confirmation);

        var pick = Assert.Single(findings, finding => finding.CheckId == "analysis-index-out-of-range" && finding.Span.Line == 23);
        Assert.Equal(Confidence.Certain, pick.Confidence);
        Assert.Equal("Running `pick([]string{}, 0)` stopped with a panic - runtime error: index out of range [0] with length 0 - on line 23.", pick.Confirmation);
    }

    [Fact]
    public async Task ANilPointerIsNamedAsGoNamesItAndRunWithNil()
    {
        const string code = "package main\n\nimport \"fmt\"\n\ntype Person struct {\n\tName string\n" +
                            "}\n\nfunc greet(person *Person) string {\n\tif person == nil {\n\t\tfmt.Println(\"nobody there\")\n\t}\n" +
                            "\treturn \"hello \" + person.Name\n}\n\nfunc main() {\n\tfmt.Println(greet(&Person{Name: \"sam\"}))\n}\n";
        if (await Tried("people.go", code) is not { } findings) return;

        var nothing = Assert.Single(findings, finding => finding.CheckId == "analysis-null-used");
        Assert.Equal("`person` is nil", nothing.Witness);
        Assert.Equal(Confidence.Certain, nothing.Confidence);
        Assert.Equal("Running `greet(nil)` stopped with a panic - runtime error: invalid memory address or nil pointer dereference - on line 13.",
            nothing.Confirmation);
    }

    [Fact]
    public async Task AMethodIsNotRunAndItsFindingStaysAsItWas()
    {
        const string code = "package main\n\nimport \"fmt\"\n\ntype Counter struct {\n\tseen int\n" +
                            "}\n\nfunc (c Counter) average(marks []int) int {\n\ttotal := 0\n\tfor _, mark := range marks {\n\t\ttotal += mark\n" +
                            "\t}\n\treturn total / len(marks)\n}\n\nfunc main() {\n\tfmt.Println(Counter{}.average([]int{4}))\n" +
                            "}\n";
        if (await Tried("counter.go", code) is not { } findings) return;

        var division = Assert.Single(findings, finding => finding.CheckId == "analysis-division-by-zero");
        Assert.NotEqual(Confidence.Certain, division.Confidence);
        Assert.Null(division.Confirmation);
    }

    [Fact]
    public async Task AFileThatDoesNotBuildLeavesItsFindingAsItWas()
    {
        const string code = "package main\n\nimport \"fmt\"\n\nfunc average(marks []int) int {\n\ttotal := 0\n" +
                            "\tfor _, mark := range marks {\n\t\ttotal += mark\n\t}\n\treturn total / len(marks)\n}\n\n" +
                            "func main() {\n\tfmt.Println(average([]int{40, 60}), missing)\n}\n";
        if (await Tried("broken.go", code) is not { } findings) return;

        var division = Assert.Single(findings, finding => finding.CheckId == "analysis-division-by-zero");
        Assert.NotEqual(Confidence.Certain, division.Confidence);
        Assert.Null(division.Confirmation);
    }
}
