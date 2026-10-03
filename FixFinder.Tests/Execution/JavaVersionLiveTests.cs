using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Programs written in the Java of 21 to 25, checked as the window checks them - built, run, and their fixes compiled - with
/// the JDK each needs, found on this computer. Each is skipped where no JDK of that Java is here, and runs on CI's JDK 25
/// and 27 as well as on whatever this computer has.
/// </summary>
public class JavaVersionLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
        return path;
    }

    private async Task<(CheckReport Report, string Explanation)> CheckAsync(string file, string? input = null, string? expected = null)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);
        output.WriteLine($"how: {launch.Explanation}");

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry())
        {
            Language = CodeLanguage.Java,
            Expected = expected is null ? null : ExpectedBehaviour.From([new ExpectedRun(input, expected)], ""),
        };

        var report = await checker.CheckAsync(launch);
        foreach (var finding in report.Findings) output.WriteLine($"[{finding.Severity}] {finding.Location}: {finding.Title} :: {finding.Explanation}");
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        output.WriteLine($"{report.SyntaxSummary} / {report.LogicSummary}");

        return (report, launch.Explanation);
    }

    /// <summary>
    /// A compact source file - Java 25's, with no class around its methods - reading a name with IO.readln and printing with
    /// IO.println, built and run by a JDK of Java 25 or later even when the default JDK is older.
    /// </summary>
    [Fact]
    public async Task ACompactSourceFileIsBuiltAndRunWithAJdkOfJava25()
    {
        if (Jdks.AtLeast(25) is null) return;

        var hello = Write(@"compact\Hello.java", """
            String greeting(String name) {
                return "Hello, " + name;
            }

            void main() {
                String name = IO.readln("Name: ");
                IO.println(greeting(name));
            }
            """);

        var (report, explanation) = await CheckAsync(hello, input: "Ada", expected: "Name: Hello, Ada");

        Assert.Empty(report.Findings);
        Assert.Equal("It printed what you expected", report.LogicSummary);
        if (Jdks.Default is { Version: < 25 })
            Assert.Contains("as Hello.java is a compact source file - methods with no class around them - which Java 25 made part of the language", explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APatternMatchingProgramRunsAsWritten()
    {
        if (Jdks.AtLeast(21) is null) return;

        var shapes = Write(@"patterns\Shapes.java", """
            public class Shapes {
                sealed interface Shape permits Circle, Square {}
                record Circle(double radius) implements Shape {}
                record Square(double side) implements Shape {}

                static String describe(Shape shape) {
                    return switch (shape) {
                        case Circle c when c.radius() > 10 -> "a big circle";
                        case Circle c -> "a circle";
                        case Square(double side) -> "a square of side " + side;
                    };
                }

                public static void main(String[] args) {
                    System.out.println(describe(new Circle(20)));
                    System.out.println(describe(new Circle(1)));
                    System.out.println(describe(new Square(2)));
                }
            }
            """);

        var (report, _) = await CheckAsync(shapes, expected: "a big circle\na circle\na square of side 2.0");

        Assert.Empty(report.Findings);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    /// <summary>
    /// A fix to a compact source file is compiled with the JDK the program is built with: with the default JDK of an older
    /// Java, the copy would not compile at all, and no fix could be checked.
    /// </summary>
    [Fact]
    public async Task AFixToACompactSourceFileIsCheckedWithItsOwnJdk()
    {
        if (Jdks.AtLeast(25) is null) return;

        var marks = Write(@"compactFix\Marks.java", """
            void main() {
                int total = 70 + 45
                IO.println(total);
            }
            """);

        var (report, _) = await CheckAsync(marks);

        var missingSemicolon = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Contains("int total = 70 + 45;", missingSemicolon.Change!.Lines.Select(line => line.Text.Trim()));
        Assert.Equal("FixFinder made this change to a copy of Marks.java, outside your project, and compiled it with javac: the error is gone.",
            missingSemicolon.FixCheckedBy);
    }

    [Fact]
    public async Task UnnamedVariablesBuildWithAJdkThatHasThem()
    {
        if (Jdks.AtLeast(22) is null) return;

        var count = Write(@"unnamed\Count.java", """
            import java.util.List;

            public class Count {
                public static void main(String[] args) {
                    int total = 0;
                    for (String _ : List.of("x", "y", "z")) total++;
                    try { Integer.parseInt("q"); } catch (NumberFormatException _) { total += 10; }
                    System.out.println(total);
                }
            }
            """);

        var (report, _) = await CheckAsync(count, expected: "13");

        Assert.Empty(report.Findings);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }
}
