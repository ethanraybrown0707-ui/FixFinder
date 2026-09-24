using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// What a folder holds that is worth checking. Most of this is about what is left out: a folder of real work is
/// mostly dependencies, build output and caches, and checking those means checking somebody else's code.
/// </summary>
public class ProjectScanTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Make(string relative, string text = "print('hello')\n")
    {
        var path = Path.Combine(_temp.Path, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>Stands in for the real one, so these tests are about the scan and not about how programs are gathered.</summary>
    private static ScanPlan Scan(string folder, Func<string, IReadOnlyList<string>>? together = null) =>
        ProjectScan.Of(folder, together ?? (file => [file]));

    [Fact]
    public void EverySourceFileInTheFolderIsAProgramToCheck()
    {
        Make("one.py");
        Make("two.py");
        Make("three.js");

        var plan = Scan(_temp.Path);

        Assert.Equal(3, plan.Programs.Count);
        Assert.Equal(3, plan.FilesFound);
        Assert.False(plan.StoppedEarly);
    }

    [Fact]
    public void FilesUnderFoldersThatHoldBuildOutputAreNotChecked()
    {
        Make("mine.py");
        Make("bin/theirs.py");
        Make("obj/theirs.py");
        Make("node_modules/left-pad/index.js");
        Make("__pycache__/mine.py");
        Make(".git/hooks/thing.py");
        Make("venv/lib/site-packages/requests/api.py");

        var plan = Scan(_temp.Path);

        Assert.Equal("mine.py", Assert.Single(plan.Programs).Name);
    }

    [Fact]
    public void AHiddenFolderIsSomebodyElsesBusiness()
    {
        Make("mine.py");
        Make(".tooling/theirs.py");

        Assert.Equal("mine.py", Assert.Single(Scan(_temp.Path).Programs).Name);
    }

    [Fact]
    public void FilesOfOneProgramAreCheckedOnceAsThatProgram()
    {
        var main = Make("main.py");
        var helper = Make("helper.py");

        // main.py imports helper.py, so they are one program and checking helper on its own would repeat the work.
        var plan = Scan(_temp.Path, file => file == main ? [main, helper] : [file]);

        var program = Assert.Single(plan.Programs);
        Assert.Equal("main.py", program.Name);
        Assert.True(program.IsSeveralFiles);
        Assert.Equal(2, program.Files.Count);
    }

    [Fact]
    public void AHeaderIsPartOfAProgramRatherThanAProgram()
    {
        Make("shop.c", "int main(void) { return 0; }\n");
        Make("shop.h", "int total(void);\n");

        var plan = Scan(_temp.Path);

        Assert.Equal("shop.c", Assert.Single(plan.Programs).Name);
        Assert.Equal(1, plan.FilesFound);
    }

    [Fact]
    public void SomethingFarTooBigToBeHandWrittenIsSkipped()
    {
        Make("small.js");
        Make("huge.js", new string('x', (int)ProjectScan.LargestFile + 1));

        Assert.Equal("small.js", Assert.Single(Scan(_temp.Path).Programs).Name);
    }

    [Fact]
    public void AFolderWithNothingToCheckInItIsNotAFailure()
    {
        Make("notes.txt", "nothing to see");
        Make("data.csv", "a,b,c");

        var plan = Scan(_temp.Path);

        Assert.Empty(plan.Programs);
        Assert.False(plan.StoppedEarly);
    }

    [Fact]
    public void AnEnormousFolderIsCutShortAndSaysSo()
    {
        for (var i = 0; i < ProjectScan.MostPrograms + 20; i++) Make($"file{i:000}.py");

        var plan = Scan(_temp.Path);

        Assert.Equal(ProjectScan.MostPrograms, plan.Programs.Count);
        Assert.True(plan.StoppedEarly);
        Assert.Contains(ProjectScan.MostPrograms.ToString(), plan.Summary);
    }

    [Fact]
    public void ProgramsDeeperInTheFolderAreFoundToo()
    {
        Make("src/app/main.py");
        Make("tests/test_main.py");

        Assert.Equal(2, Scan(_temp.Path).Programs.Count);
    }

    /// <summary>A program reaching outside the folder is still this folder's program; what it reaches is not.</summary>
    [Fact]
    public void AProgramThatReachesOutsideTheFolderIsStillOneProgram()
    {
        var main = Make("main.py");
        var outside = Path.Combine(Path.GetTempPath(), "somewhere-else.py");

        var plan = Scan(_temp.Path, file => file == main ? [main, outside] : [file]);

        var program = Assert.Single(plan.Programs);
        Assert.Contains(outside, program.Files);
    }
}
