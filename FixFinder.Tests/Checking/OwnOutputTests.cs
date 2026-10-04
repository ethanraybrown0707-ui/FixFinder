using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Covers what a program says about itself. Its own messages - an interpreter reporting a mistake in its script, a menu
/// refusing a choice - are its output, not a crash; only a runtime's report of a failure, or a failing exit code, is.
/// </summary>
public class OwnOutputTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
        return path;
    }

    private async Task<CheckReport> CheckAsync(string file, CodeLanguage language)
    {
        using var http = new FixFinderHttpClient();
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);

        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = language }.CheckAsync(launch);

        output.WriteLine($"run: {report.Run?.Result} - {report.Run?.Headline}");
        foreach (var finding in report.Findings) output.WriteLine($"[{finding.Severity}/{finding.Kind}] {finding.Title}");

        return report;
    }

    [Fact]
    public async Task AProgramThatPrintsItsOwnErrorMessageAndSucceedsDidNotCrash()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var file = Write("calculator.py", """
            script = "print 1 / 0"
            print("error at 6: division by zero in the script")
            print("done")
            """);

        var report = await CheckAsync(file, CodeLanguage.Python);

        Assert.Equal(SessionResult.RanFine, report.Run?.Result);
        Assert.DoesNotContain(report.Findings, finding => finding.Kind == FindingKind.Runtime);
    }

    [Fact]
    public async Task AJavaInterpreterReportingAMistakeInItsScriptDidNotCrash()
    {
        if (Toolchains.FindJavac() is null) return;

        var file = Write("Interpreter.java", """
            public class Interpreter {
                public static void main(String[] args) {
                    String script = "let = 4;";
                    if (script.startsWith("let =")) {
                        System.out.println("error at 4: expected a name after 'let' but found '='");
                    }
                }
            }
            """);

        var report = await CheckAsync(file, CodeLanguage.Java);

        Assert.DoesNotContain(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.DoesNotContain(report.Findings, finding => finding.Severity == Severity.Error);
    }

    [Fact]
    public async Task AnExceptionPrintedOnTheWayToTheEndIsAWarningRatherThanACrash()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var file = Write("marks.py", """
            import traceback

            try:
                mark = int("twelve")
            except ValueError:
                traceback.print_exc()
            print("carried on")
            """);

        var report = await CheckAsync(file, CodeLanguage.Python);

        var printed = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal(Severity.Warning, printed.Severity);
        Assert.StartsWith("It ran to the end, but printed an exception:", printed.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProgramThatEndsItselfWithAFailingCodeIsAPossibleWarningQuotingWhatItPrinted()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var file = Write("average.py", """
            import sys

            marks = [int(argument) for argument in sys.argv[1:]]
            if not marks:
                print("Error: give the marks to average after the program's name")
                sys.exit(1)
            print(sum(marks) / len(marks))
            """);

        var report = await CheckAsync(file, CodeLanguage.Python);

        var ended = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal(Severity.Warning, ended.Severity);
        Assert.Equal(Confidence.Possible, ended.Confidence);
        Assert.StartsWith("It stopped with exit code 1", ended.Title, StringComparison.Ordinal);
        Assert.Contains("Error: give the marks to average", ended.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain(report.Findings, finding => finding.Severity == Severity.Error);
    }

    [Fact]
    public async Task AJavaProgramThatPrintsHowToRunItAndExitsIsNotSaidToHaveCrashed()
    {
        if (Toolchains.FindJavac() is null) return;

        var file = Write("Greeting.java", """
            public class Greeting {
                public static void main(String[] args) {
                    if (args.length == 0) {
                        System.out.println("Usage: java Greeting <name>");
                        System.exit(1);
                    }
                    System.out.println("Hello, " + args[0]);
                }
            }
            """);

        var report = await CheckAsync(file, CodeLanguage.Java);

        var ended = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal(Severity.Warning, ended.Severity);
        Assert.Contains("Usage: java Greeting <name>", ended.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain("crash", ended.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WindowsRefusingToStartTheProgramIsANoteRatherThanAFindingAboutTheCode()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var file = Write("launcher.py", """
            import sys

            print("fork/exec C:/build/main.exe: An Application Control policy has blocked this file.")
            sys.exit(1)
            """);

        var report = await CheckAsync(file, CodeLanguage.Python);

        Assert.DoesNotContain(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Contains(report.Notes, note => note.StartsWith("Windows would not start the program.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AJavaClassWithNoMainHasNothingToRunRatherThanACrash()
    {
        if (Toolchains.FindJavac() is null) return;

        var file = Write("Calculator.java", """
            public class Calculator {
                // Tests call this; the program that uses it has the main method.
                public int divide(int dividend, int divisor) {
                    if (divisor == 0) {
                        throw new IllegalArgumentException("divisor must not be zero");
                    }
                    return dividend / divisor;
                }
            }
            """);

        var report = await CheckAsync(file, CodeLanguage.Java);

        Assert.DoesNotContain(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Contains(report.Notes, note => note.StartsWith("Calculator.java has no main method", StringComparison.Ordinal));
        Assert.EndsWith("it has no main method to run", report.SyntaxSummary, StringComparison.Ordinal);
    }

    /// <summary>A main given one String rather than an array of them is no main to any Java, so java cannot start the class.</summary>
    [Fact]
    public async Task AJavaMainOfTheWrongShapeIsStillAMistakeJavaCouldNotStart()
    {
        if (Toolchains.FindJavac() is null) return;

        var file = Write("Greeter.java", """
            public class Greeter {
                public static void main(String args) {
                    System.out.println("Hello");
                }
            }
            """);

        var report = await CheckAsync(file, CodeLanguage.Java);

        var launch = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.StartsWith("Java could not start it: Main method", launch.Title, StringComparison.Ordinal);
    }

    /// <summary>
    /// A main that is not static is a mistake java will not start before Java 25 - "Main method is not static" - and from Java
    /// 25 it is a main like any other: java makes an object of the class and calls it (JEP 512).
    /// </summary>
    [Fact]
    public async Task AMainThatIsNotStaticStartsOnlyFromJava25()
    {
        if (Toolchains.FindJavac() is null || Jdks.InUse is not { } jdk) return;

        var file = Write("Welcome.java", """
            public class Welcome {
                public void main(String[] args) {
                    System.out.println("Hello");
                }
            }
            """);

        var report = await CheckAsync(file, CodeLanguage.Java);

        if (jdk.Version >= 25)
        {
            Assert.DoesNotContain(report.Findings, finding => finding.Kind == FindingKind.Runtime);
            Assert.Equal("It builds, and it runs to the end", report.SyntaxSummary);
        }
        else
        {
            var launch = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
            Assert.StartsWith("Java could not start it: Main method is not static", launch.Title, StringComparison.Ordinal);
        }
    }
}
