using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Tests;

/// <summary>
/// A fix written into a person's file with one click: only one FixFinder is certain solves the mistake - a copy with it
/// printed what the person said the program should print; running without the error alone is not enough - and of 3 lines
/// or fewer, written as the copy was, with the file's own line endings, after the file as it was is kept; and not when the
/// file has been changed since its check began, when nothing is written or kept.
/// </summary>
public class OneClickFixTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string KeepIn => Path.Combine(_temp.Path, "kept");

    /// <summary>A copy with the change that ran without the error: likely, not certain.</summary>
    private static readonly Verification RanWithoutTheError =
        Verification.NotTested.With(VerificationStage.Ran, StageResult.Passed, "A copy of the program with the change ran, and did not stop.");

    /// <summary>A copy with the change that printed what the person said the program should print: certain.</summary>
    private static readonly Verification ShownToWork =
        RanWithoutTheError.With(VerificationStage.MatchedExpectedOutput, StageResult.Passed, "It printed 240, as you said it should.");

    private string Program(string text)
    {
        var path = Path.Combine(_temp.Path, "marks.py");
        File.WriteAllText(path, text);
        return path;
    }

    private static Finding FoundIn(string file, LocalFix fix, Confidence confidence = Confidence.Certain, Verification? verified = null) => new()
    {
        Kind = FindingKind.Syntax,
        Severity = Severity.Error,
        Confidence = confidence,
        File = file,
        Line = fix.StartLine,
        Title = "A colon is missing",
        Explanation = "",
        WhyItMatters = "",
        SuggestedFix = "",
        CorrectedExample = "",
        Fix = fix,
        Verified = verified ?? ShownToWork,
    };

    private static LocalFix ColonOn(string file, int line, string text) =>
        LocalFix.ReplaceLine("python-missing-colon", "Add the colon", "", file, line, text);

    [Fact]
    public void AFixCertainShownToWorkAndSmallIsWrittenAndTheFileAsItWasKept()
    {
        const string before = "total = 0\r\nfor mark in [70, 80]\r\n    total += mark\r\nprint(total)\r\n";
        var file = Program(before);
        var checkedAt = DateTime.UtcNow.AddMinutes(1);

        var (applied, whyNot) = OneClickFix.Apply(FoundIn(file, ColonOn(file, 2, "for mark in [70, 80]:")), checkedAt, KeepIn);

        Assert.Null(whyNot);
        Assert.NotNull(applied);
        Assert.Equal(2, applied.Line);
        Assert.Equal("total = 0\r\nfor mark in [70, 80]:\r\n    total += mark\r\nprint(total)\r\n", File.ReadAllText(file));
        Assert.StartsWith(KeepIn, applied.KeptAt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllText(applied.KeptAt));
    }

    [Fact]
    public void TwoFixesToOneFileInTheSameSecondAreEachKept()
    {
        var file = Program("for mark in [70]\n    print(mark)\nfor grade in [80]\n    print(grade)\n");
        var checkedAt = DateTime.UtcNow.AddMinutes(1);

        var (first, _) = OneClickFix.Apply(FoundIn(file, ColonOn(file, 1, "for mark in [70]:")), checkedAt, KeepIn);
        var (second, _) = OneClickFix.Apply(FoundIn(file, ColonOn(file, 3, "for grade in [80]:")), checkedAt, KeepIn);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.KeptAt, second.KeptAt);
        Assert.Equal("for mark in [70]:\n    print(mark)\nfor grade in [80]:\n    print(grade)\n", File.ReadAllText(file));
    }

    [Fact]
    public void AFileChangedSinceItsCheckBeganIsLeftAsItIs()
    {
        const string before = "for mark in [70]\n    print(mark)\n";
        var file = Program(before);
        var checkedAt = DateTime.UtcNow.AddMinutes(-5);

        var (applied, whyNot) = OneClickFix.Apply(FoundIn(file, ColonOn(file, 1, "for mark in [70]:")), checkedAt, KeepIn);

        Assert.Null(applied);
        Assert.Equal("The file has been changed since it was checked, so the lines this fix was worked out from may have moved. Check it again first.", whyNot);
        Assert.Equal(before, File.ReadAllText(file));
        Assert.False(Directory.Exists(KeepIn));
    }

    /// <summary>
    /// A file that cannot be written - here, one made read-only - is left as it is, and the note says where the copy of it
    /// kept just before is, as a write that stops part way would have nothing else to go back to.
    /// </summary>
    [Fact]
    public void AFileThatCannotBeWrittenSaysWhereItsCopyIsKept()
    {
        const string before = "for mark in [70]\n    print(mark)\n";
        var file = Program(before);
        File.SetAttributes(file, FileAttributes.ReadOnly);

        try
        {
            var (applied, whyNot) = OneClickFix.Apply(FoundIn(file, ColonOn(file, 1, "for mark in [70]:")), DateTime.UtcNow.AddMinutes(1), KeepIn);

            Assert.Null(applied);
            Assert.NotNull(whyNot);
            Assert.StartsWith("The file could not be written: ", whyNot);
            var kept = Assert.Single(Directory.GetFiles(KeepIn));
            Assert.EndsWith($" The file as it was is kept at {kept}.", whyNot);
            Assert.Equal(before, File.ReadAllText(kept));
            Assert.Equal(before, File.ReadAllText(file));
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
            foreach (var kept in Directory.Exists(KeepIn) ? Directory.GetFiles(KeepIn) : []) File.SetAttributes(kept, FileAttributes.Normal);
        }
    }

    [Fact]
    public void OnlyAFixCertainToSolveItAndOf3LinesOrFewerIsOffered()
    {
        var file = Program("for mark in [70]\n    print(mark)\n");
        var fix = ColonOn(file, 1, "for mark in [70]:");
        const string notCertain = "FixFinder is not certain its fix solves it: it is, only when a copy of the program with the fix prints what you said " +
                                  "the program should print.";

        Assert.Null(OneClickFix.WhyNot(FoundIn(file, fix)));
        Assert.Equal(notCertain, OneClickFix.WhyNot(FoundIn(file, fix, verified: RanWithoutTheError)));
        Assert.Equal(notCertain, OneClickFix.WhyNot(FoundIn(file, fix, verified: Verification.NotTested)));
        Assert.Equal(notCertain, OneClickFix.WhyNot(FoundIn(file, fix, verified: Verification.NotTested.With(VerificationStage.Ran, StageResult.Failed, "It stopped again."))));
        Assert.Equal(notCertain, OneClickFix.WhyNot(FoundIn(file, fix,
            verified: RanWithoutTheError.With(VerificationStage.MatchedExpectedOutput, StageResult.Failed, "It printed 70, not 240."))));
        Assert.Equal("Its change only stops the failure, and leaves what made it as it is.",
            OneClickFix.WhyNot(FoundIn(file, fix with { OnlyStopsTheFailure = true })));
        Assert.Equal("Its change is more than 3 lines.",
            OneClickFix.WhyNot(FoundIn(file, fix with { NewLines = ["a = 1", "b = 2", "c = 3", "d = 4"] })));
        Assert.Equal("Its change is more than 3 lines.",
            OneClickFix.WhyNot(FoundIn(file, fix with { RemoveCount = 4 })));
        Assert.Equal("Its file is not there to change.",
            OneClickFix.WhyNot(FoundIn(file, fix with { File = Path.Combine(_temp.Path, "gone.py") })));
    }
}
