using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Scala programs checked whole, as the window checks them: built and run by Scala CLI, offline, their mistakes found at
/// their lines and their fixes compiled - and run - in copies. A computer without Scala CLI, or with no Scala 3 in its
/// cache, passes over them and says so.
/// </summary>
public class ScalaLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private bool Available()
    {
        if (ScalaToolchains.Cli is not null && ScalaToolchains.CachedVersions.Any(version => version.IsScala3)) return true;

        output.WriteLine("Not run on this computer: Scala CLI, or a Scala 3 in its cache, is not here.");
        return false;
    }

    private string Write(string relative, string code)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
        return path;
    }

    private async Task<CheckReport> CheckAsync(string file)
    {
        var launch = TargetFactory.FromFile(file, LiveAllowance.For(file, TimeSpan.FromMinutes(2)));
        Assert.True(launch.Ok, launch.Problem);
        output.WriteLine(launch.Explanation);

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Scala }.CheckAsync(launch);

        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
            output.WriteLine($"{finding.Line}: {finding.RuleId}: {finding.Title} | {finding.FixCheckedBy} | {finding.Verified.Summary}");

        return report;
    }

    [Fact]
    public async Task AWholeNumberDividedByZeroIsFoundAtItsLineAndItsFixIsRunInACopy()
    {
        if (!Available()) return;

        var program = Write(Path.Combine("marks", "Marks.scala"), """
            object Marks {
              def average(scores: List[Int]): Int = scores.sum / scores.length

              def main(args: Array[String]): Unit = {
                val scores = List[Int]()
                println("Average: " + average(scores))
              }
            }
            """);

        var report = await CheckAsync(program);

        var crash = Assert.Single(report.Findings, finding => finding.RuleId == "scala-division-guard");
        Assert.Equal(2, crash.Line);
        Assert.Equal(Confidence.Certain, crash.Confidence);
        Assert.Contains("Dividing a whole number - an Int or a Long - by zero has no answer", crash.Explanation);
        Assert.True(crash.Verified.IsVerified, crash.Verified.Summary);
    }

    [Fact]
    public async Task AMisspeltNameIsChangedToScalasOwnSuggestionOnceACopyCompilesWithIt()
    {
        if (!Available()) return;

        var program = Write(Path.Combine("hello", "Hello.scala"), """
            object Hello {
              def main(args: Array[String]): Unit = {
                val name = "Ada"
                prinln("Hello, " + name)
              }
            }
            """);

        var report = await CheckAsync(program);

        var misspelt = Assert.Single(report.Findings, finding => finding.Line == 4);
        Assert.Equal("scala-did-you-mean", misspelt.RuleId);
        Assert.Equal(FindingKind.Syntax, misspelt.Kind);
        Assert.Contains("compiled it with Scala CLI", misspelt.FixCheckedBy);
        Assert.Contains("println(\"Hello, \" + name)", misspelt.CorrectedExample);
        Assert.DoesNotContain("prinln", misspelt.CorrectedExample);
    }

    [Fact]
    public async Task CodeThatOnlyScala2ReadsIsBuiltAndRunAsScala2WhenOneIsHere()
    {
        if (!Available()) return;
        if (!ScalaToolchains.CachedVersions.Any(version => !version.IsScala3))
        {
            output.WriteLine("Not run on this computer: no Scala 2 is in Scala CLI's cache.");
            return;
        }

        var program = Write(Path.Combine("old", "Old.scala"), """
            object Old {
              def main(args: Array[String]) {
                println("procedure syntax")
              }
            }
            """);

        var report = await CheckAsync(program);

        Assert.Contains(report.Notes, note => note.Contains("so it is Scala 2 code, and was built and run as Scala 2.", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Findings, finding => finding.Severity == Severity.Error);
        Assert.Contains(report.Run!.Run!.Lines, line => line.Text == "procedure syntax");
    }

    [Fact]
    public async Task AMatchThatMissesACaseIsWarnedOfAndItsMatchErrorFound()
    {
        if (!Available()) return;

        var program = Write(Path.Combine("grades", "Grade.scala"), """
            object Grade {
              sealed trait Mark
              case object Pass extends Mark
              case object Fail extends Mark
              case object Merit extends Mark

              def describe(mark: Mark): String = mark match {
                case Pass => "passed"
                case Fail => "failed"
              }

              def main(args: Array[String]): Unit = println(describe(Merit))
            }
            """);

        var report = await CheckAsync(program);

        var warning = Assert.Single(report.Findings, finding => finding.Line == 7);
        Assert.Equal((Severity.Warning, Confidence.Likely), (warning.Severity, warning.Confidence));
        Assert.Contains("the program stops with a scala.MatchError", warning.Explanation);

        var crash = Assert.Single(report.Findings, finding => finding.Line == 9);
        Assert.StartsWith("It crashed: MatchError: Merit", crash.Title);
    }

    [Fact]
    public async Task NoneGetIsTheCrashItIsAndOneFindingWithTheCheckThatExplainsIt()
    {
        if (!Available()) return;

        // Java's NoSuchElementException is said as a note when a Scanner has nothing left to read; a Scala program's is its own mistake.
        var program = Write(Path.Combine("ages", "NoneGet.scala"), """
            object NoneGet {
              def main(args: Array[String]): Unit = {
                val ages = Map("Ada" -> 36)
                println(ages.get("Bob").get)
              }
            }
            """);

        var report = await CheckAsync(program);

        var crash = Assert.Single(report.Findings, finding => finding.Line == 4);
        Assert.StartsWith("It crashed: NoSuchElementException: None.get", crash.Title);
        Assert.Equal(Confidence.Certain, crash.Confidence);
        Assert.DoesNotContain(report.Notes, note => note.Contains("None.get", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFileWithNoMainIsSaidToHaveNothingToRunRatherThanReportedAsAMistake()
    {
        if (!Available()) return;

        var program = Write(Path.Combine("library", "Student.scala"), "case class Student(name: String, mark: Int)\n");

        var report = await CheckAsync(program);

        Assert.Contains(report.Notes, note => note.StartsWith("Student.scala has no main, so there is nothing in it to run", StringComparison.Ordinal));
        Assert.Empty(report.Findings);
    }

    [Fact]
    public async Task ALibraryThatIsNotInTheCacheIsSaidOnceAndIsNotAMistakeInTheCode()
    {
        if (!Available()) return;

        // A library nobody publishes, so it is in no cache anywhere.
        var program = Write(Path.Combine("library", "UsesLibrary.scala"), """
            //> using dep org.example.fixfinder::not-a-library:0.0.1
            object UsesLibrary {
              def main(args: Array[String]): Unit = println(1)
            }
            """);

        var report = await CheckAsync(program);

        Assert.Contains(report.Notes, note => note.Contains("which is not in Scala CLI's cache on this computer. FixFinder never downloads anything", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Findings, finding => finding.Kind == FindingKind.Syntax);
    }
}
