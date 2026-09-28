using System.Diagnostics;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Execution.Libraries;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Covers running a class of JUnit tests with JUnit itself: knowing a test class, what JUnit needs to run one, reading
/// back what each test did - and, with JUnit here, a failing test reported on its own line with JUnit's own words.
/// </summary>
/// <remarks>
/// The tests that run JUnit need its jars, which the CI workflow fetches from Maven Central and names in
/// FIXFINDER_JUNIT_JARS; without them they return early, as the tests that need a missing compiler do.
/// </remarks>
public class JavaTestRunTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        return path;
    }

    private string Folder(string relative) => Path.GetFullPath(Path.Combine(_temp.Path, relative));

    /// <summary>The folder of JUnit jars the CI workflow fetched, holding the one named, or null when it is not here.</summary>
    private static string? JUnitJar(string startsWith) =>
        Environment.GetEnvironmentVariable("FIXFINDER_JUNIT_JARS") is { Length: > 0 } folder && Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, startsWith + "*.jar").FirstOrDefault()
            : null;

    private async Task<CheckReport> CheckAsync(string file)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);
        output.WriteLine($"how: {launch.Explanation}");

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Java }.CheckAsync(launch);

        output.WriteLine($"syntax: {report.SyntaxSummary} | logic: {report.LogicSummary}");
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings) output.WriteLine($"[{finding.Severity}] {finding.Location}: {finding.Title} :: {finding.Explanation}");
        return report;
    }

    private const string Calculator = """
        public class Calculator {
            public int add(int left, int right) {
                return left + right;
            }

            public int divide(int dividend, int divisor) {
                return dividend / divisor;
            }
        }
        """;

    private const string JUnit5Tests = """
        import static org.junit.jupiter.api.Assertions.assertEquals;

        import org.junit.jupiter.api.Test;

        class CalculatorTest {
            private final Calculator calculator = new Calculator();

            @Test
            void addsTwoNumbers() {
                assertEquals(4, calculator.add(2, 2));
            }

            @Test
            void addsANegativeNumber() {
                assertEquals(1, calculator.add(3, -1));
            }

            @Test
            void dividesByTwo() {
                assertEquals(2, calculator.divide(4, 0));
            }
        }
        """;

    [Theory]
    [InlineData("import org.junit.jupiter.api.Test;\nclass T { @Test void a() {} }", JavaTests.Framework.JUnit5)]
    [InlineData("import static org.junit.jupiter.api.Assertions.*;\nimport org.junit.jupiter.params.ParameterizedTest;\nclass T { @ParameterizedTest void a() {} }", JavaTests.Framework.JUnit5)]
    [InlineData("import org.junit.Test;\npublic class T { @Test public void a() {} }", JavaTests.Framework.JUnit4)]
    [InlineData("public class App { public static void main(String[] args) {} }", JavaTests.Framework.None)]
    [InlineData("import org.junit.jupiter.api.Assertions;\nclass Helper { }", JavaTests.Framework.None)]
    public void ATestClassIsKnownByItsJUnitImportAndItsTests(string code, JavaTests.Framework framework) =>
        Assert.Equal(framework, JavaTests.FrameworkOf(Write("T.java", code)));

    [Fact]
    public void WhatJUnitNeedsToRunIsFoundOrNamed()
    {
        var store = new MavenRepository(Folder("repository"));
        var launcher = Write(@"repository\org\junit\platform\junit-platform-launcher\1.11.4\junit-platform-launcher-1.11.4.jar", "");

        Assert.Null(JavaTests.RunnerFor(JavaTests.Framework.JUnit5, [@"C:\lib\junit-platform-console-standalone-1.11.4.jar"], store).CannotRun);

        var withLauncher = JavaTests.RunnerFor(JavaTests.Framework.JUnit5,
            [@"C:\m2\junit-jupiter-engine-5.11.4.jar", @"C:\m2\junit-platform-engine-1.11.4.jar", @"C:\m2\junit-jupiter-api-5.11.4.jar"], store);
        Assert.Null(withLauncher.CannotRun);
        Assert.Equal([Path.GetFullPath(launcher)], withLauncher.ExtraJars.Select(Path.GetFullPath).ToArray(), StringComparer.OrdinalIgnoreCase);

        Assert.Contains("junit-jupiter-engine", JavaTests.RunnerFor(JavaTests.Framework.JUnit5, [@"C:\m2\junit-jupiter-api-5.11.4.jar"], store).CannotRun);
        Assert.Contains("junit-platform-launcher 1.10.0",
            JavaTests.RunnerFor(JavaTests.Framework.JUnit5, [@"C:\m2\junit-jupiter-engine-5.10.0.jar", @"C:\m2\junit-platform-engine-1.10.0.jar"], store).CannotRun);

        Assert.Null(JavaTests.RunnerFor(JavaTests.Framework.JUnit4, [@"C:\lib\junit-4.13.2.jar", @"C:\lib\hamcrest-core-1.3.jar"], store).CannotRun);
        Assert.NotNull(JavaTests.RunnerFor(JavaTests.Framework.JUnit4, [], store).CannotRun);
    }

    [Fact]
    public void WhatEachTestDidIsReadBackFromTheLaunchersLines()
    {
        string[] lines =
        [
            "a line the test itself printed",
            "FIXFINDER-TEST\tSUCCESSFUL\taddsTwoNumbers()\tCalculatorTest\taddsTwoNumbers\t\t\t",
            "FIXFINDER-TEST\tFAILED\tdividesByTwo()\tCalculatorTest\tdividesByTwo\tjava.lang.ArithmeticException\t/ by zero\t" +
            "Calculator#divide#Calculator.java#7;CalculatorTest#dividesByTwo#CalculatorTest.java#20;",
            "FIXFINDER-TEST\tFAILED\taddsTwoNumbers()\tCalculatorTest\taddsTwoNumbers\torg.opentest4j.AssertionFailedError\texpected: <5> but was: <4>\\nand a tab\\there\t",
            "FIXFINDER-TESTS-FOUND\t3",
        ];

        var results = JavaTests.ResultsIn(lines);

        Assert.Equal(["SUCCESSFUL", "FAILED", "FAILED"], results.Select(result => result.Status).ToArray());
        Assert.Equal("/ by zero", results[1].Message);
        Assert.Equal(("Calculator", "divide", "Calculator.java", 7), results[1].Frames[0]);
        Assert.Equal("expected: <5> but was: <4>\nand a tab\there", results[2].Message);
        Assert.Equal(3, JavaTests.TestsFound(lines));
    }

    [Fact]
    public async Task ATestClassJUnitCannotRunHereSaysWhyRatherThanThatItCrashed()
    {
        if (Toolchains.FindJavac() is not { } javac) return;

        // A jar with JUnit 5's API alone - what a project that names junit-jupiter-api and not its engine has.
        var api = Write(@"api-src\org\junit\jupiter\api\Test.java", "package org.junit.jupiter.api;\n\n@java.lang.annotation.Retention(java.lang.annotation.RetentionPolicy.RUNTIME)\npublic @interface Test {\n}\n");
        var assertions = Write(@"api-src\org\junit\jupiter\api\Assertions.java", "package org.junit.jupiter.api;\n\npublic final class Assertions {\n    public static void assertEquals(int expected, int actual) {\n    }\n}\n");
        var jarTool = Path.Combine(Path.GetDirectoryName(javac.Program)!, "jar.exe");
        Run(javac.Program, $"-d \"{Folder("api-classes")}\" \"{api}\" \"{assertions}\"");
        Directory.CreateDirectory(Folder(@"coursework\lib"));
        Run(jarTool, $"cf \"{Folder(@"coursework\lib\junit-jupiter-api-5.11.4.jar")}\" -C \"{Folder("api-classes")}\" .");

        Write(@"coursework\Calculator.java", Calculator);
        var tests = Write(@"coursework\CalculatorTest.java", JUnit5Tests);

        var report = await CheckAsync(tests);

        Assert.DoesNotContain(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Contains(report.Notes, note => note.StartsWith("CalculatorTest.java is a class of JUnit 5 tests", StringComparison.Ordinal) &&
                                              note.Contains("junit-jupiter-engine", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailingJUnit5TestIsReportedOnItsLineInJUnitsOwnWords()
    {
        if (Toolchains.FindJavac() is null || JUnitJar("junit-platform-console-standalone") is not { } junit) return;

        Directory.CreateDirectory(Folder(@"coursework\lib"));
        File.Copy(junit, Path.Combine(Folder(@"coursework\lib"), Path.GetFileName(junit)));
        Write(@"coursework\Calculator.java", Calculator.Replace("return left + right;", "return left + right + (right < 0 ? 1 : 0);"));
        var tests = Write(@"coursework\CalculatorTest.java", JUnit5Tests);

        var report = await CheckAsync(tests);

        var wrongSum = Assert.Single(report.Findings, finding => finding.Title.StartsWith("Test addsANegativeNumber failed", StringComparison.Ordinal));
        Assert.Equal(15, wrongSum.Line);
        Assert.Contains("expected: <1> but was: <2>", wrongSum.Explanation, StringComparison.Ordinal);

        var division = Assert.Single(report.Findings, finding => finding.Title.StartsWith("Test dividesByTwo stopped with ArithmeticException", StringComparison.Ordinal));
        Assert.Contains("thrown in Calculator.divide on line 7 of Calculator.java", division.Explanation, StringComparison.Ordinal);

        Assert.Equal("It builds; 2 of 3 tests failed", report.SyntaxSummary);
    }

    [Fact]
    public async Task AFailingJUnit4TestIsReportedToo()
    {
        if (Toolchains.FindJavac() is null || JUnitJar("junit-4") is not { } junit || JUnitJar("hamcrest-core") is not { } hamcrest) return;

        Directory.CreateDirectory(Folder(@"coursework\lib"));
        File.Copy(junit, Path.Combine(Folder(@"coursework\lib"), Path.GetFileName(junit)));
        File.Copy(hamcrest, Path.Combine(Folder(@"coursework\lib"), Path.GetFileName(hamcrest)));
        Write(@"coursework\Calculator.java", Calculator);
        var tests = Write(@"coursework\CalculatorTest.java", """
            import static org.junit.Assert.assertEquals;

            import org.junit.Test;

            public class CalculatorTest {
                @Test
                public void addsTwoNumbers() {
                    assertEquals(5, new Calculator().add(2, 2));
                }

                @Test
                public void addsZero() {
                    assertEquals(2, new Calculator().add(2, 0));
                }
            }
            """);

        var report = await CheckAsync(tests);

        var failed = Assert.Single(report.Findings, finding => finding.Title.StartsWith("Test addsTwoNumbers failed", StringComparison.Ordinal));
        Assert.Equal(8, failed.Line);
        Assert.Equal("It builds; 1 of 2 tests failed", report.SyntaxSummary);
    }

    [Fact]
    public async Task TheTestsOfAMavenProjectAreRunAgainstItsMainCode()
    {
        if (Toolchains.FindJavac() is null || JUnitJar("junit-platform-console-standalone") is not { } junit) return;

        var version = Path.GetFileNameWithoutExtension(junit)["junit-platform-console-standalone-".Length..];
        var repository = Folder("repository");
        var folder = Path.Combine(repository, "org", "junit", "platform", "junit-platform-console-standalone", version);
        Directory.CreateDirectory(folder);
        File.Copy(junit, Path.Combine(folder, Path.GetFileName(junit)));

        Write(@"project\pom.xml", $"""
            <project xmlns="http://maven.apache.org/POM/4.0.0">
              <modelVersion>4.0.0</modelVersion>
              <groupId>uni</groupId>
              <artifactId>calculator</artifactId>
              <version>1.0</version>
              <dependencies>
                <dependency>
                  <groupId>org.junit.platform</groupId>
                  <artifactId>junit-platform-console-standalone</artifactId>
                  <version>{version}</version>
                  <scope>test</scope>
                </dependency>
              </dependencies>
            </project>
            """);
        Write(@"project\src\main\java\uni\Calculator.java", "package uni;\n\n" + Calculator);
        var tests = Write(@"project\src\test\java\uni\CalculatorTest.java", "package uni;\n\n" + JUnit5Tests.Replace("assertEquals(2, calculator.divide(4, 0));", "assertEquals(2, calculator.divide(4, 2));"));

        using var stores = JavaLibraries.UsingStores(new LibraryStores([new MavenRepository(repository)]));
        var report = await CheckAsync(tests);

        Assert.DoesNotContain(report.Findings, finding => finding.Severity == Severity.Error);
        Assert.Equal("It builds, and all 3 of its tests pass", report.SyntaxSummary);
    }

    private static void Run(string program, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(program, arguments) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true })!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);
    }
}
