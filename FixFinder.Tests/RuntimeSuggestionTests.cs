using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Patching;
using FixFinder.Core.Sources;
using FixFinder.Core.Verification;

namespace FixFinder.Tests;

/// <summary>
/// The runtime's own correction, read out of a real crash and applied to a real file.
/// </summary>
/// <remarks>
/// Driven end to end against Python rather than against captured text, because the whole claim is
/// that the interpreter already knows the answer. A fixture would only prove that a regex matches
/// a string somebody typed into a fixture.
/// </remarks>
public class RuntimeSuggestionTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private static readonly string? Python =
        TargetFactory.FindOnPath("python") ??
        TargetFactory.FindOnPath("py") ??
        TargetFactory.FindOnPath("python3");

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string name, string body)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, body.ReplaceLineEndings("\n"));
        return path;
    }

    private async Task<ParsedError> CrashOf(string script)
    {
        var spec = new TargetSpec
        {
            ExecutablePath = Python!,
            Arguments = $"\"{script}\"",
            WorkingDirectory = _temp.Path,
            Timeout = TimeSpan.FromSeconds(30),
        };

        var run = await new TargetRunner(new ParserRegistry()).RunAsync(spec, CancellationToken.None);

        Assert.NotNull(run.Error);
        return run.Error!;
    }

    // ------------------------------------------------------------------ reading it

    [Fact]
    public async Task AMisspeltAttributeIsReadFromWhatPythonSaid()
    {
        if (Python is null) return;

        var script = Write("attr.py", """
            class Supply:
                def __init__(self):
                    self.heavy = True

            s = Supply()
            print(s.heavey)
            """);

        var correction = RuntimeSuggestion.Read(await CrashOf(script));

        Assert.NotNull(correction);
        Assert.Equal("heavey", correction!.Wrong);
        Assert.Equal("heavy", correction.Right);
        Assert.Equal(6, correction.Line);
    }

    /// <summary>
    /// The class name is quoted first in the message and must not be the one replaced.
    /// </summary>
    /// <remarks>
    /// <c>'Supply' object has no attribute 'heavey'</c> - taking the first quoted word would
    /// rename the type instead of fixing the typo, and it would apply cleanly while doing it.
    /// </remarks>
    [Fact]
    public async Task TheClassNameIsNotMistakenForTheMisspeltName()
    {
        if (Python is null) return;

        var script = Write("attr2.py", """
            class Supply:
                def __init__(self):
                    self.heavy = True

            print(Supply().heavey)
            """);

        var correction = RuntimeSuggestion.Read(await CrashOf(script));

        Assert.Equal("heavey", correction!.Wrong);
        Assert.NotEqual("Supply", correction.Wrong);
    }

    [Fact]
    public async Task AMisspeltNameIsRead()
    {
        if (Python is null) return;

        var script = Write("name.py", """
            average = 10
            print(avarage)
            """);

        var correction = RuntimeSuggestion.Read(await CrashOf(script));

        Assert.Equal("avarage", correction!.Wrong);
        Assert.Equal("average", correction.Right);
    }

    /// <summary>An error the runtime had no suggestion for offers nothing.</summary>
    [Fact]
    public async Task AnErrorWithNoSuggestionProducesNothing()
    {
        if (Python is null) return;

        var script = Write("keyerr.py", """
            data = {}
            print(data["user_id"])
            """);

        Assert.Null(RuntimeSuggestion.Read(await CrashOf(script)));
    }

    // ------------------------------------------------------------------ runtimes not installed here

    /// <summary>
    /// An error built from output the real toolchain prints, for the ones absent from this machine.
    /// </summary>
    private static ParsedError Captured(string language, string type, string message, string? raw = null) =>
        new()
        {
            LanguageId = language,
            Confidence = 90,
            RawText = raw ?? $"{type}: {message}",
            FirstLineSequence = 0,
            ExceptionType = type,
            Message = message,
            Frames = [new ErrorFrame { Order = 0, File = "main.c", Line = 4, RawLine = "" }],
        };

    /// <summary>
    /// gcc and clang put the name on opposite sides of the same word.
    /// </summary>
    /// <remarks>
    /// One pattern looked like it covered both until it was checked against what each compiler
    /// really prints - gcc writes <c>'avarage' undeclared</c> and clang
    /// <c>undeclared identifier 'avarage'</c>. The gcc-shaped pattern silently matched nothing at
    /// all on clang.
    /// </remarks>
    [Theory]
    [InlineData("'avarage' undeclared (first use in this function); did you mean 'average'?")]
    [InlineData("use of undeclared identifier 'avarage'; did you mean 'average'?")]
    public void BothCCompilersAreRead(string message)
    {
        var correction = RuntimeSuggestion.Read(Captured("gcc", "compile error", message));

        Assert.NotNull(correction);
        Assert.Equal("avarage", correction!.Wrong);
        Assert.Equal("average", correction.Right);
    }

    /// <summary>
    /// Ruby writes a question mark where Python writes a colon, and puts it on the next line.
    /// </summary>
    /// <remarks>
    /// Two separate reasons the first version of this found nothing in Ruby: "Did you mean?" does
    /// not match a pattern expecting an optional colon, and the suggestion is not in the message
    /// at all - it is a line of its own underneath it.
    /// </remarks>
    [Theory]
    [InlineData("undefined local variable or method 'avarage' for main", "avarage", "average")]
    [InlineData("undefined method 'heavey' for an instance of Supply", "heavey", "heavy")]
    public void RubyIsReadFromTheLineUnderTheMessage(string message, string wrong, string right)
    {
        var raw = $"main.rb:3:in '<main>': {message} (NameError)\nDid you mean?  {right}";

        var correction = RuntimeSuggestion.Read(Captured("ruby", "NameError", message, raw));

        Assert.NotNull(correction);
        Assert.Equal(wrong, correction!.Wrong);
        Assert.Equal(right, correction.Right);
    }

    /// <summary>
    /// A "did you mean" somewhere else in the output cannot invent a correction on its own.
    /// </summary>
    /// <remarks>
    /// Looking at the whole captured block for the suggestion is what makes Ruby work, and it is
    /// also the thing that could have gone wrong: the name being corrected still has to come from
    /// the message, so a program that prints the phrase itself changes nothing.
    /// </remarks>
    [Fact]
    public void APhraseInTheProgramsOwnOutputIsNotASuggestion()
    {
        var raw = "checking spelling... did you mean 'banana'?\nZeroDivisionError: division by zero";

        Assert.Null(RuntimeSuggestion.Read(
            Captured("python", "ZeroDivisionError", "division by zero", raw)));
    }

    // ------------------------------------------------------------------ applying it

    /// <summary>
    /// The whole point: a typo in code nobody else has ever seen, fixed and verified.
    /// </summary>
    /// <remarks>
    /// Goes through the ordinary road - a fenced diff in the candidate body, extracted, parsed,
    /// path-mapped, context-matched, applied and re-run - so the locally produced patch is
    /// checked by exactly the machinery every downloaded one is.
    /// </remarks>
    [Fact]
    public async Task ATypoInYourOwnCodeIsFixedAndTheProgramThenRuns()
    {
        if (Python is null) return;

        var script = Write("typo.py", """
            average = 10
            print("starting")
            print(avarage)
            """);

        var error = await CrashOf(script);
        var candidate = RuntimeSuggestion.For(error, _temp.Path);

        Assert.NotNull(candidate);
        Assert.Equal(FixTier.AutoAppliable, candidate!.Tier);

        var harvest = await new PatchHarvester(new Core.Http.FixFinderHttpClient())
            .HarvestAsync(candidate, Core.Http.CacheMode.CacheOnly);

        Assert.True(harvest.HasAppliablePatch, "the fenced diff in the body did not parse");

        var plan = new PatchApplier().Plan(
            harvest.Patches[0], new SourcePathMapper(_temp.Path, [script]), [script]);

        Assert.True(plan.CanApply, plan.Explanation);

        var backups = new BackupStore(Path.Combine(_temp.Path, "backups"));
        var applied = new PatchApplier().Apply(plan, backups, _temp.Path, dryRun: false, "runtime", "t", "");

        Assert.True(applied.Ok, applied.Failure);
        Assert.Contains("print(average)", File.ReadAllText(script));

        // And it really runs now.
        var after = await new TargetRunner(new ParserRegistry()).RunAsync(
            new TargetSpec
            {
                ExecutablePath = Python,
                Arguments = $"\"{script}\"",
                WorkingDirectory = _temp.Path,
                Timeout = TimeSpan.FromSeconds(30),
            },
            CancellationToken.None);

        Assert.Null(after.Error);
        Assert.Equal(RunOutcome.ExitedClean, after.Outcome);
    }

    /// <summary>
    /// Two of the same name on one line, and nothing is offered.
    /// </summary>
    /// <remarks>
    /// The same refusal the path mapper makes for two files of the same name. One of these is the
    /// one the runtime meant and there is no way to tell which, so replacing either would be a
    /// guess - and this feature's whole claim is that it never guesses.
    /// </remarks>
    [Fact]
    public async Task TwoOccurrencesOnOneLineAreRefusedRatherThanGuessedAt()
    {
        if (Python is null) return;

        var script = Write("twice.py", """
            average = 10
            print(avarage + avarage)
            """);

        var error = await CrashOf(script);

        Assert.NotNull(RuntimeSuggestion.Read(error));
        Assert.Null(RuntimeSuggestion.For(error, _temp.Path));
    }

    /// <summary>A suggestion for a file that is not there produces nothing rather than throwing.</summary>
    [Fact]
    public async Task AMissingFileProducesNothing()
    {
        if (Python is null) return;

        var script = Write("gone.py", """
            average = 10
            print(avarage)
            """);

        var error = await CrashOf(script);
        File.Delete(script);

        Assert.Null(RuntimeSuggestion.For(error, _temp.Path));
    }

    /// <summary>
    /// The correction survives verification, which is what keeps it honest.
    /// </summary>
    [Fact]
    public async Task TheFixIsVerifiedByRerunningLikeAnyOther()
    {
        if (Python is null) return;

        var script = Write("verify.py", """
            total = 5
            print(totl)
            """);

        var error = await CrashOf(script);
        var before = Core.Fingerprinting.FingerprintBuilder.Build(error);

        var candidate = RuntimeSuggestion.For(error, _temp.Path);
        Assert.NotNull(candidate);

        var harvest = await new PatchHarvester(new Core.Http.FixFinderHttpClient())
            .HarvestAsync(candidate!, Core.Http.CacheMode.CacheOnly);

        var plan = new PatchApplier().Plan(
            harvest.Patches[0], new SourcePathMapper(_temp.Path, [script]), [script]);

        var backups = new BackupStore(Path.Combine(_temp.Path, "backups"));
        var applied = new PatchApplier().Apply(plan, backups, _temp.Path, dryRun: false, "runtime", "t", "");

        Assert.True(applied.Ok, applied.Failure);

        var spec = new TargetSpec
        {
            ExecutablePath = Python,
            Arguments = $"\"{script}\"",
            WorkingDirectory = _temp.Path,
            Timeout = TimeSpan.FromSeconds(30),
        };

        var verdict = await new FixVerifier().VerifyAsync(spec, before, backups, applied.BackupFolder);

        Assert.Equal(FixVerdict.Fixed, verdict.Verdict);
    }
}
