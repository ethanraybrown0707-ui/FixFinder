using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// How to make a finding happen, said only from what FixFinder ran or worked out: the call a run confirmed it with, what the
/// program's own run was given, the values that make it fail, or the language refusing the code - and nothing at all for a
/// finding nothing ran.
/// </summary>
public class ReproductionTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Finding Finding(
        FindingKind kind = FindingKind.Runtime, Severity severity = Severity.Error, ParsedError? error = null, RunGiven? ranWith = null,
        string? witness = null, string? confirmation = null, string file = @"C:\work\marks.py") => new()
    {
        Kind = kind,
        Severity = severity,
        Confidence = Confidence.Certain,
        File = file,
        Line = 5,
        Title = "Title",
        Explanation = "what is wrong",
        WhyItMatters = "why",
        SuggestedFix = "fix",
        CorrectedExample = "",
        Error = error,
        RanWith = ranWith,
        Witness = witness,
        Confirmation = confirmation,
    };

    private static ParsedError Said() => new() { LanguageId = "python", Confidence = 90, FirstLineSequence = 0, RawText = "", Message = "something", Frames = [] };

    [Fact]
    public void ARunThatConfirmedItGivesTheCallItRan()
    {
        Assert.Equal("Call `take(flag=False)` - FixFinder did, and that call stopped here.",
            Reproduction.For(Finding(witness: "`flag` is False", confirmation: "Running `take(flag=False)` stopped with IndexError: pop from empty list on line 5. Lines it ran: 1-5.")));
        Assert.Equal("Run the program, typing `5` then `0` - FixFinder did, and that run stopped here.",
            Reproduction.For(Finding(confirmation: "Running the program, typing `5` then `0` stopped with ZeroDivisionError: integer division or modulo by zero on line 3. Lines it ran: 1-3.")));
    }

    /// <summary>The program's own run, with what it was given: its arguments, and each line typed in - the first six, then how many more.</summary>
    [Fact]
    public void TheProgramsOwnRunIsSaidWithWhatItWasGiven()
    {
        Assert.Equal("Run marks.py with no arguments and nothing typed in, as FixFinder did - that run stopped here.",
            Reproduction.For(Finding(error: Said(), ranWith: new RunGiven("marks.py", "", null))));
        Assert.Equal("Run marks.py with the arguments `scores.txt 3`, typing `5` then an empty line then `0`, as FixFinder did - that run stopped here.",
            Reproduction.For(Finding(error: Said(), ranWith: new RunGiven("marks.py", "scores.txt 3", "5\n\n0\n"))));
        Assert.Equal("Run marks.py typing `1` then `2` then `3` then `4` then `5` then `6`, then 2 more lines, as FixFinder did - that run printed this here, and carried on.",
            Reproduction.For(Finding(severity: Severity.Warning, error: Said(), ranWith: new RunGiven("marks.py", "", "1\n2\n3\n4\n5\n6\n7\n8\n"))));
    }

    [Fact]
    public void ValuesWorkedOutFromTheCodeAndALanguageRefusingItAreSaidAsThat()
    {
        Assert.Equal("Worked out from the code, it fails here when `count` is 0: run it with values like those to see it happen.",
            Reproduction.For(Finding(witness: "`count` is 0")));
        Assert.Equal("Check or run it again: Python refuses this before the program starts, every time, until it is changed.",
            Reproduction.For(Finding(FindingKind.Syntax, error: Said())));
    }

    /// <summary>Nothing ran it and nothing worked out an input: there is nothing true to say about how to make it happen.</summary>
    [Fact]
    public void AFindingNothingRanSaysNothingAboutMakingItHappen()
    {
        Assert.Null(Reproduction.For(Finding(FindingKind.Logic)));
        Assert.Null(Reproduction.For(Finding(FindingKind.Syntax, Severity.Warning, error: Said())));
        Assert.Null(Reproduction.For(Finding(error: Said())));
    }

    /// <summary>A real run given two numbers, the second 0: Python stopped with ZeroDivisionError, and the finding says how it was run.</summary>
    [Fact]
    public async Task ARealRunGivenInputIsSaidWithWhatWasTypedIn()
    {
        if (TargetFactory.FindOnPath("python") is null)
        {
            output.WriteLine("Not run on this computer: no Python.");
            return;
        }

        var file = Path.Combine(_temp.Path, "share.py");
        File.WriteAllText(file, "total = int(input())\npeople = int(input())\nprint(total // people)\n");
        var launch = TargetFactory.FromFile(file, TimeSpan.FromMinutes(2));
        Assert.True(launch.Ok, launch.Problem);
        launch = launch with { Spec = launch.Spec!.WithArguments("").WithInput("12\n0\n") };

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Python }.CheckAsync(launch);
        foreach (var each in report.Findings) output.WriteLine($"{each.Line}: {each.RuleId}: {FurtherExplanation.For(each).Reproduce}");

        var stopped = Assert.Single(report.Findings, finding => finding.Line == 3);
        Assert.Equal("ZeroDivisionError", stopped.Error?.ShortExceptionType);
        Assert.Contains(FurtherExplanation.For(stopped).Reproduce,
            ["Run share.py typing `12` then `0`, as FixFinder did - that run stopped here.", "Run the program, typing `12` then `0` - FixFinder did, and that run stopped here."]);
    }
}
