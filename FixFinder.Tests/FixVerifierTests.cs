using FixFinder.Core.Execution;
using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Parsing;
using FixFinder.Core.Patching;
using FixFinder.Core.Verification;

namespace FixFinder.Tests;

/// <summary>
/// Covers the verify-and-rollback loop end to end, against real processes.
/// </summary>
/// <remarks>
/// Driven with Python scripts written into a temp folder rather than with a stubbed runner,
/// because the thing being tested is precisely whether re-running a real program and comparing
/// two real fingerprints reaches the right conclusion. A fake runner would only test that the
/// switch statement matches the enum.
/// </remarks>
public class FixVerifierTests : IDisposable
{
    private readonly TempFolder _temp = new();

    /// <summary>
    /// Python, if this machine has it - the quickest real program to drive.
    /// </summary>
    /// <remarks>
    /// Found the same way the tool finds it, rather than by a hard-coded path. Every test that
    /// uses it returns early when it is absent, so the suite reports on the code rather than on
    /// whichever interpreters happen to be installed.
    /// </remarks>
    private static readonly string? Python = FindPython();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string? FindPython() =>
        TargetFactory.FindOnPath("python") ??
        TargetFactory.FindOnPath("py") ??
        TargetFactory.FindOnPath("python3");

    private string WriteScript(string name, string body)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, body.ReplaceLineEndings("\n"));
        return path;
    }

    private TargetSpec SpecFor(string script, string? buildCommand = null) => new()
    {
        ExecutablePath = Python!,
        Arguments = $"\"{script}\"",
        WorkingDirectory = _temp.Path,
        Timeout = TimeSpan.FromSeconds(30),
        BuildCommand = buildCommand,
        BuildWorkingDirectory = _temp.Path,
    };

    /// <summary>Runs a script once and fingerprints whatever it threw.</summary>
    private static async Task<ErrorFingerprint> FingerprintOfRun(TargetSpec spec)
    {
        var result = await new TargetRunner().RunAsync(spec, CancellationToken.None);

        Assert.NotNull(result.Error);
        return FingerprintBuilder.Build(result.Error!);
    }

    private const string CrashingScript = """
        data = {}
        print("starting")
        print(data["user_id"])
        """;

    private const string FixedScript = """
        data = {}
        print("starting")
        print(data.get("user_id"))
        """;

    private const string DifferentCrashScript = """
        data = {}
        print("starting")
        print(1 / 0)
        """;

    // ================================================================== verdicts

    [Fact]
    public async Task APatchThatRemovesTheErrorIsReportedAsFixed()
    {
        if (Python is null) return;

        var script = WriteScript("target.py", CrashingScript);
        var spec = SpecFor(script);
        var before = await FingerprintOfRun(spec);

        // Stand in for the patch having been applied.
        File.WriteAllText(script, FixedScript.ReplaceLineEndings("\n"));

        var result = await new FixVerifier().VerifyAsync(
            spec, before, new BackupStore(Path.Combine(_temp.Path, "backups")), backupFolder: null);

        Assert.Equal(FixVerdict.Fixed, result.Verdict);
        Assert.False(result.RolledBack);
    }

    /// <summary>
    /// The same error must be recognised as the same despite the patch moving line numbers.
    /// </summary>
    /// <remarks>
    /// The reason the comparison is on fingerprints rather than on output text. The edit here
    /// adds a line, so the traceback reports a different line number for the identical bug - and
    /// a naive comparison would call that a different error and keep a patch that fixed nothing.
    /// </remarks>
    [Fact]
    public async Task TheSameErrorAtADifferentLineNumberIsStillTheSameError()
    {
        if (Python is null) return;

        var script = WriteScript("target.py", CrashingScript);
        var spec = SpecFor(script);
        var before = await FingerprintOfRun(spec);

        File.WriteAllText(script,
            ("# a comment the patch added\n# and another\n" + CrashingScript).ReplaceLineEndings("\n"));

        var result = await new FixVerifier().VerifyAsync(
            spec, before, new BackupStore(Path.Combine(_temp.Path, "backups")), backupFolder: null);

        Assert.Equal(FixVerdict.SameErrorPersists, result.Verdict);
        Assert.Equal(before.Hash, result.AfterHash);
    }

    /// <summary>
    /// A different error is kept, not undone.
    /// </summary>
    /// <remarks>
    /// Fixing the first of two bugs looks exactly like this, and rolling back real progress
    /// unasked would be the worse mistake of the two.
    /// </remarks>
    [Fact]
    public async Task ADifferentErrorIsKeptRatherThanRolledBack()
    {
        if (Python is null) return;

        var script = WriteScript("target.py", CrashingScript);
        var spec = SpecFor(script);
        var before = await FingerprintOfRun(spec);

        File.WriteAllText(script, DifferentCrashScript.ReplaceLineEndings("\n"));

        var result = await new FixVerifier().VerifyAsync(
            spec, before, new BackupStore(Path.Combine(_temp.Path, "backups")), backupFolder: null);

        Assert.Equal(FixVerdict.DifferentError, result.Verdict);
        Assert.False(result.RolledBack);
        Assert.NotEqual(before.Hash, result.AfterHash);
        Assert.Contains("often progress", result.Explanation, StringComparison.Ordinal);
    }

    // ================================================================== rollback

    /// <summary>The whole loop: apply, re-run, find the same error, undo it.</summary>
    [Fact]
    public async Task WhenTheSameErrorReturnsThePatchIsRolledBackByteForByte()
    {
        if (Python is null) return;

        var script = WriteScript("target.py", CrashingScript);
        var spec = SpecFor(script);
        var before = await FingerprintOfRun(spec);
        var original = File.ReadAllBytes(script);

        var backups = new BackupStore(Path.Combine(_temp.Path, "backups"));
        var folder = backups.Create(_temp.Path, [script], "gh#1", "a patch that does not help");

        // A change that alters the source without addressing the bug.
        File.WriteAllText(script, ("# patched\n" + CrashingScript).ReplaceLineEndings("\n"));

        var result = await new FixVerifier().VerifyAsync(spec, before, backups, folder);

        Assert.Equal(FixVerdict.SameErrorPersists, result.Verdict);
        Assert.True(result.RolledBack);
        Assert.Equal(original, File.ReadAllBytes(script));
    }

    [Fact]
    public async Task RollbackCanBeSwitchedOffAndThenTheChangeStays()
    {
        if (Python is null) return;

        var script = WriteScript("target.py", CrashingScript);
        var spec = SpecFor(script);
        var before = await FingerprintOfRun(spec);

        var backups = new BackupStore(Path.Combine(_temp.Path, "backups"));
        var folder = backups.Create(_temp.Path, [script]);

        var patched = ("# patched\n" + CrashingScript).ReplaceLineEndings("\n");
        File.WriteAllText(script, patched);

        var result = await new FixVerifier().VerifyAsync(spec, before, backups, folder, autoRollback: false);

        Assert.Equal(FixVerdict.SameErrorPersists, result.Verdict);
        Assert.False(result.RolledBack);
        Assert.Equal(patched, File.ReadAllText(script));
    }

    // ================================================================== the build step

    /// <summary>A patch that does not compile is not a fix, and is undone immediately.</summary>
    [Fact]
    public async Task AFailingBuildRollsBackWithoutEvenRunningTheProgram()
    {
        if (Python is null) return;

        var script = WriteScript("target.py", CrashingScript);
        var spec = SpecFor(script, buildCommand: $"\"{Python}\" -c \"import sys; sys.exit(3)\"");
        var before = await FingerprintOfRun(SpecFor(script));
        var original = File.ReadAllBytes(script);

        var backups = new BackupStore(Path.Combine(_temp.Path, "backups"));
        var folder = backups.Create(_temp.Path, [script]);

        File.WriteAllText(script, "this is not valid python at all\n");

        var result = await new FixVerifier().VerifyAsync(spec, before, backups, folder);

        Assert.Equal(FixVerdict.BuildFailed, result.Verdict);
        Assert.True(result.RolledBack);
        Assert.Null(result.Rerun);
        Assert.Equal(original, File.ReadAllBytes(script));
    }

    [Fact]
    public async Task ASucceedingBuildLetsVerificationContinue()
    {
        if (Python is null) return;

        var script = WriteScript("target.py", CrashingScript);
        var spec = SpecFor(script, buildCommand: $"\"{Python}\" -c \"print('built')\"");
        var before = await FingerprintOfRun(SpecFor(script));

        File.WriteAllText(script, FixedScript.ReplaceLineEndings("\n"));

        var result = await new FixVerifier().VerifyAsync(
            spec, before, new BackupStore(Path.Combine(_temp.Path, "backups")), backupFolder: null);

        Assert.Equal(FixVerdict.Fixed, result.Verdict);
        Assert.NotNull(result.Build);
        Assert.Equal(RunOutcome.ExitedClean, result.Build!.Outcome);
    }

    /// <summary>
    /// For a compiled language with no build command, re-running would run the old binary.
    /// </summary>
    /// <remarks>
    /// Reporting that as a persisting error would roll back a patch that may have been perfectly
    /// good, so it refuses to reach a verdict instead.
    /// </remarks>
    [Fact]
    public async Task ACompiledLanguageWithNoBuildCommandIsInconclusiveRatherThanWrong()
    {
        var before = FingerprintBuilder.Build(new ParsedError
        {
            LanguageId = "csharp",
            Confidence = 95,
            RawText = "boom",
            FirstLineSequence = 1,
            ExceptionType = "System.NullReferenceException",
            Message = "Object reference not set to an instance of an object.",
            Frames = [],
        });

        var spec = new TargetSpec
        {
            ExecutablePath = "dotnet",
            Arguments = "nothing.dll",
            WorkingDirectory = _temp.Path,
            BuildCommand = null,
        };

        var result = await new FixVerifier().VerifyAsync(
            spec, before, new BackupStore(Path.Combine(_temp.Path, "backups")), backupFolder: null);

        Assert.Equal(FixVerdict.Inconclusive, result.Verdict);
        Assert.False(result.RolledBack);
        Assert.Contains("build command", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("csharp", true)]
    [InlineData("java", true)]
    [InlineData("rust", true)]
    [InlineData("python", false)]
    [InlineData("node", false)]
    [InlineData("ruby", false)]
    public void OnlyCompiledLanguagesDemandABuildStep(string language, bool expected) =>
        Assert.Equal(expected, FixVerifier.NeedsBuild(language));

    // ================================================================== inconclusive cases

    [Fact]
    public async Task WithNoOriginalErrorThereIsNothingToVerifyAgainst()
    {
        var result = await new FixVerifier().VerifyAsync(
            new TargetSpec { ExecutablePath = "whatever", WorkingDirectory = _temp.Path },
            before: null,
            new BackupStore(Path.Combine(_temp.Path, "backups")),
            backupFolder: null);

        Assert.Equal(FixVerdict.Inconclusive, result.Verdict);
    }

    [Fact]
    public async Task AProgramThatCannotBeLaunchedProvesNothingAndIsNotRolledBack()
    {
        if (Python is null) return;

        var script = WriteScript("target.py", CrashingScript);
        var before = await FingerprintOfRun(SpecFor(script));

        var broken = new TargetSpec
        {
            ExecutablePath = Path.Combine(_temp.Path, "no-such-program.exe"),
            WorkingDirectory = _temp.Path,
            Timeout = TimeSpan.FromSeconds(10),
        };

        var backups = new BackupStore(Path.Combine(_temp.Path, "backups"));
        var folder = backups.Create(_temp.Path, [script]);

        var result = await new FixVerifier().VerifyAsync(broken, before, backups, folder);

        Assert.Equal(FixVerdict.Inconclusive, result.Verdict);
        Assert.False(result.RolledBack);
    }

    // ================================================================== command splitting

    [Theory]
    [InlineData("dotnet build App.csproj", "dotnet", "build App.csproj")]
    [InlineData("make", "make", "")]
    [InlineData("  npm run build  ", "npm", "run build")]
    [InlineData("\"C:\\Program Files\\dotnet\\dotnet.exe\" build", "C:\\Program Files\\dotnet\\dotnet.exe", "build")]
    public void ABuildCommandSplitsIntoProgramAndArguments(string command, string program, string arguments)
    {
        var (actualProgram, actualArguments) = FixVerifier.SplitCommand(command);

        Assert.Equal(program, actualProgram);
        Assert.Equal(arguments, actualArguments);
    }
}
