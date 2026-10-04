using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// A call that ends the whole program - sys.exit, System.exit, Environment.Exit, process.exit, os.Exit, log.Fatal - ends
/// the path it is on, so a check that stops the program when a list is empty guards what comes after it, and is not a
/// contract a caller can break. Without the call, the same mistake is still reported.
/// </summary>
public class ProgramStopTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<string> WriteAsync(string name, string code)
    {
        var path = Path.Combine(_temp.Path, name);
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return path;
    }

    private static IReadOnlyList<AnalysisFinding> Mistakes(IEnumerable<AnalysisFinding> findings) => findings.Where(f => f.Severity != Severity.Suggestion).ToList();

    private static string Shown(IEnumerable<AnalysisFinding> findings) => string.Join("; ", findings.Select(f => $"{f.CheckId} line {f.Span.Line}: {f.Message}"));

    private async Task<IReadOnlyList<AnalysisFinding>?> PythonAsync(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;
        return Mistakes(AbstractChecks.Run(await PythonFrontend.ReadAsync([await WriteAsync("marks.py", code)], python), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> JavaAsync(string code)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;
        return Mistakes(AbstractChecks.Run(await JavaFrontend.ReadAsync([await WriteAsync("Marks.java", code)], tools.Javac, tools.Java), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> GoAsync(string code)
    {
        var file = await WriteAsync("main.go", code);
        if (GoFrontend.FindGo(file) is not { } go) return null;
        return Mistakes(AbstractChecks.Run(await GoFrontend.ReadAsync([file], go), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>> CSharpAsync(string code) =>
        Mistakes(AbstractChecks.Run(await CSharpFrontend.ReadAsync([await WriteAsync("Marks.cs", code)]), new SourceText()));

    private async Task<IReadOnlyList<AnalysisFinding>> JavaScriptAsync(string code) =>
        Mistakes(AbstractChecks.Run(await JavaScriptFrontend.ReadAsync([await WriteAsync("marks.js", code)]), new SourceText()));

    private static string PythonAverage(string stop) => $$"""
        import os
        import sys


        def average(marks):
            if not marks:
                print("Error: no marks to average")
                {{stop}}
            return sum(marks) / len(marks)
        """;

    [Theory]
    [InlineData("sys.exit(1)")]
    [InlineData("sys.exit(\"no marks\")")]
    [InlineData("exit(1)")]
    [InlineData("quit()")]
    [InlineData("os._exit(1)")]
    public async Task APythonProgramThatStopsItselfGuardsWhatComesAfter(string stop)
    {
        if (await PythonAsync(PythonAverage(stop)) is not { } found) return;

        Assert.True(found.Count == 0, $"{stop}: {Shown(found)}");
    }

    [Fact]
    public async Task WithoutTheStopTheDivisionIsStillReported()
    {
        if (await PythonAsync(PythonAverage("pass")) is not { } found) return;

        Assert.Contains(found, finding => finding.CheckId == "analysis-division-by-zero");
    }

    [Fact]
    public async Task AFunctionOfTheProgramsOwnCalledExitIsNotTakenForTheBuiltIn()
    {
        var code = PythonAverage("exit(1)") + """


            def exit(code):
                print("would stop with", code)
            """;

        if (await PythonAsync(code) is not { } found) return;

        Assert.Contains(found, finding => finding.CheckId == "analysis-division-by-zero");
    }

    [Fact]
    public async Task AGuardThatStopsTheProgramIsNotAContractACallerBreaks()
    {
        const string code = """
            import sys


            def check(marks):
                if not marks:
                    sys.exit("no marks")
                return len(marks)


            check([])
            """;

        if (await PythonAsync(code) is not { } found) return;

        Assert.DoesNotContain(found, finding => finding.CheckId == "analysis-contract-broken");
    }

    [Fact]
    public async Task AJavaProgramThatCallsSystemExitGuardsWhatComesAfter()
    {
        const string code = """
            public class Marks {
                static int average(int[] marks) {
                    if (marks.length == 0) {
                        System.out.println("Usage: java Marks <mark> ...");
                        System.exit(1);
                    }
                    int total = 0;
                    for (int mark : marks) {
                        total += mark;
                    }
                    return total / marks.length;
                }

                public static void main(String[] args) {
                    System.out.println(average(new int[] {70, 80}));
                }
            }
            """;

        if (await JavaAsync(code) is not { } found) return;

        Assert.True(found.Count == 0, Shown(found));
    }

    [Fact]
    public async Task ACSharpProgramThatCallsEnvironmentExitGuardsWhatComesAfter()
    {
        const string stops = """
            using System;

            public static class Shares
            {
                public static int Share(int total, int people)
                {
                    if (people == 0)
                    {
                        Console.WriteLine("Nobody to share with");
                        Environment.Exit(1);
                    }
                    return total / people;
                }
            }
            """;

        Assert.True((await CSharpAsync(stops)).Count == 0, Shown(await CSharpAsync(stops)));
        Assert.Contains(await CSharpAsync(stops.Replace("Environment.Exit(1);", "")), finding => finding.CheckId == "analysis-division-by-zero");
    }

    [Fact]
    public async Task AJavaScriptProgramThatCallsProcessExitGuardsWhatComesAfter()
    {
        const string stops = """
            function describe(user) {
                if (user === null) {
                    console.error("No user given");
                    process.exit(1);
                }
                return user.name;
            }
            """;

        Assert.True((await JavaScriptAsync(stops)).Count == 0, Shown(await JavaScriptAsync(stops)));
        Assert.NotEmpty(await JavaScriptAsync(stops.Replace("process.exit(1);", "")));
    }

    [Theory]
    [InlineData("os.Exit(1)")]
    [InlineData("log.Fatal(\"nobody to share with\")")]
    [InlineData("log.Fatalf(\"nobody to share %d with\", total)")]
    public async Task AGoProgramThatExitsGuardsWhatComesAfter(string stop)
    {
        static string Program(string stopping) => $$"""
            package main

            import (
            	"fmt"
            	"log"
            	"os"
            )

            func share(total int, people int) int {
            	if people == 0 {
            		fmt.Println("nobody to share with")
            		{{stopping}}
            	}
            	return total / people
            }

            func main() {
            	fmt.Println(share(10, 2), len(os.Args), log.Flags())
            }
            """;

        if (await GoAsync(Program(stop)) is not { } found || await GoAsync(Program("")) is not { } unguarded) return;

        Assert.True(found.Count == 0, $"{stop}: {Shown(found)}");
        Assert.Contains(unguarded, finding => finding.CheckId == "analysis-division-by-zero");
    }

    [Fact]
    public async Task AGoGuardThatCallsLogFatalIsNotAContractACallerBreaks()
    {
        const string code = """
            package main

            import (
            	"fmt"
            	"log"
            )

            func check(n int) int {
            	if n < 0 {
            		log.Fatal("negative")
            	}
            	return n
            }

            func main() {
            	fmt.Println(check(-1))
            }
            """;

        if (await GoAsync(code) is not { } found) return;

        Assert.DoesNotContain(found, finding => finding.CheckId == "analysis-contract-broken");
    }
}
