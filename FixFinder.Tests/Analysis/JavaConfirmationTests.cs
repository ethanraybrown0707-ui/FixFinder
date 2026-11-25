using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Dynamic;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// What the analyses predicted of a Java method, tried for real: a static method - a private one too - is called with the
/// witness's values from a class compiled beside a copy of its file, and only a run that stops with the predicted exception
/// on the predicted line makes the finding certain. A method of an instance is not run, and its finding stays as it was.
/// The words each certain finding carries are those the runs here gave.
/// </summary>
public class JavaConfirmationTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Tried(string className, string code)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var path = Path.Combine(_temp.Path, className + ".java");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        var program = await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java);
        var findings = AbstractChecks.Run(program, new SourceText());
        return await JavaConfirmation.ConfirmAsync(findings, program, tools.Javac, tools.Java, CancellationToken.None);
    }

    [Fact]
    public async Task ADivisionByAnArraysLengthIsRunWithAnEmptyArrayAndBecomesCertain()
    {
        const string code = "public class Average {\n    static int average(int[] marks) {\n        int total = 0;\n        for (int mark : marks) {\n            total += mark;\n        }\n" +
                            "        return total / marks.length;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(average(new int[] {40, 60}));\n    }\n}\n";
        if (await Tried("Average", code) is not { } findings) return;

        var division = Assert.Single(findings, finding => finding.CheckId == "analysis-division-by-zero");
        Assert.Equal(Confidence.Certain, division.Confidence);
        Assert.Equal("Running `average(new int[0])` stopped with ArithmeticException: / by zero on line 7.", division.Confirmation);
    }

    [Fact]
    public async Task APrivateMethodsNullIsRunWithEmptyTextAndBecomesCertain()
    {
        const string code = "public class Upper {\n    private static String upper(String name) {\n        String chosen = null;\n        if (name.length() > 3) {\n            chosen = name;\n        }\n" +
                            "        return chosen.toUpperCase();\n    }\n\n    public static void main(String[] args) {\n        System.out.println(upper(\"annabel\"));\n    }\n}\n";
        if (await Tried("Upper", code) is not { } findings) return;

        var nothing = Assert.Single(findings, finding => finding.CheckId == "analysis-null-used");
        Assert.Equal(Confidence.Certain, nothing.Confidence);
        Assert.Equal("Running `upper(\"\")` stopped with NullPointerException: Cannot invoke \"String.toUpperCase()\" because \"chosen\" is null on line 7.",
            nothing.Confirmation);
    }

    [Fact]
    public async Task AMethodOfAnInstanceIsNotRunAndItsFindingStaysAsItWas()
    {
        const string code = "public class Counter {\n    private int[] counts = new int[3];\n\n    int average(int[] marks) {\n        int total = 0;\n        for (int mark : marks) {\n" +
                            "            total += mark;\n        }\n        return total / marks.length;\n    }\n\n    public static void main(String[] args) {\n" +
                            "        System.out.println(new Counter().average(new int[] {4}));\n    }\n}\n";
        if (await Tried("Counter", code) is not { } findings) return;

        var division = Assert.Single(findings, finding => finding.CheckId == "analysis-division-by-zero");
        Assert.NotEqual(Confidence.Certain, division.Confidence);
        Assert.Null(division.Confirmation);
    }
}
