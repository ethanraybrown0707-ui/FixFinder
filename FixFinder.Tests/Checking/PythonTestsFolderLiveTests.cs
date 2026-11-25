using System.Text.Json;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Tests kept in a folder of their own, checked as a student would: they run with the program's modules found where they
/// are - the folder above, src, the folders pytest's settings name - pytest uses the conftest.py above them, the change
/// that makes them pass is found in the program they import, and a module of the program's own that Python cannot find
/// is said to be one, never given a package of its name to install.
/// </summary>
public class PythonTestsFolderLiveTests(ITestOutputHelper output) : IDisposable
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

    private async Task<CheckReport> CheckAsync(string file)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);
        output.WriteLine($"how: {launch.Explanation}");

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Python };
        checker.Log += message => output.WriteLine($"log: {message}");
        var report = await checker.CheckAsync(launch);

        output.WriteLine($"syntax: {report.SyntaxSummary}");
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings) output.WriteLine($"[{finding.RuleId}] {finding.Title} :: {finding.Fix?.Title} :: {finding.Verified.Summary} :: {finding.SuggestedFix}");
        return report;
    }

    /// <summary>A project whose VS Code settings name a Python with pytest installed, so its tests run with pytest itself; null when there is none here.</summary>
    private string? WithPytest(string project)
    {
        if (NotebookTests.PythonWith("pytest") is not { } python) return null;

        Write(Path.Combine(project, ".vscode", "settings.json"), JsonSerializer.Serialize(new Dictionary<string, string> { ["python.defaultInterpreterPath"] = python }));
        return python;
    }

    private const string Grades = """
        def grade(mark):
            if mark > 70:
                return "First"
            if mark >= 60:
                return "2:1"
            return "Fail"
        """;

    private const string GradeUnittests = """
        import unittest

        from grades import grade


        class GradeTests(unittest.TestCase):
            def test_first_at_seventy(self):
                self.assertEqual(grade(70), "First")

            def test_upper_second(self):
                self.assertEqual(grade(65), "2:1")

            def test_fail(self):
                self.assertEqual(grade(30), "Fail")
        """;

    private const string GradePytests = """
        from grades import grade


        def test_first_at_seventy():
            assert grade(70) == "First"


        def test_upper_second():
            assert grade(65) == "2:1"


        def test_fail():
            assert grade(30) == "Fail"
        """;

    /// <summary>The fix for grades.py, found by the tests and put on the test that failed, with grades.py itself as it was.</summary>
    private static void FixedInGrades(CheckReport report, string program)
    {
        if (report.SyntaxSummary == "It could not be started") return;

        var failed = Assert.Single(report.Findings, finding => finding.RuleId == "test-failed");
        Assert.NotNull(failed.Fix);
        Assert.Equal(program, failed.Fix!.File, ignoreCase: true);
        Assert.Equal(2, failed.Fix.StartLine);
        Assert.Equal(["    if mark >= 70:"], failed.Fix.NewLines);
        Assert.True(failed.Verified.IsVerified, failed.Verified.Summary);
        Assert.Equal(Grades.ReplaceLineEndings("\n"), File.ReadAllText(program));
    }

    [Fact]
    public async Task TestsInAFolderOfTheirOwnRunWithTheProgramAboveThemAndAreFixedThere()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var program = Write(@"marks\grades.py", Grades);
        var tests = Write(@"marks\tests\test_grades.py", GradeUnittests);

        FixedInGrades(await CheckAsync(tests), program);
    }

    [Fact]
    public async Task TestsBesideASrcFolderRunWithTheProgramInItAndAreFixedThere()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var program = Write(@"marks\src\grades.py", Grades);
        var tests = Write(@"marks\tests\test_grades.py", GradeUnittests);

        FixedInGrades(await CheckAsync(tests), program);
    }

    [Fact]
    public async Task PytestIsGivenTheFoldersItsSettingsNameAndTheTestsAreFixedThere()
    {
        if (!LocalFixLiveTests.Available("python") || WithPytest("marks") is null) return;

        var program = Write(@"marks\app\grades.py", Grades);
        Write(@"marks\pytest.ini", "[pytest]\npythonpath = app\n");
        var tests = Write(@"marks\tests\test_grades.py", GradePytests);

        FixedInGrades(await CheckAsync(tests), program);
    }

    /// <summary>A fixture in the conftest.py above the tests is there for them, as it is when pytest is started in the project's folder.</summary>
    [Fact]
    public async Task PytestUsesTheConftestAboveTheTestsAndTheyAreFixed()
    {
        if (!LocalFixLiveTests.Available("python") || WithPytest("marks") is null) return;

        var program = Write(@"marks\grades.py", Grades);
        Write(@"marks\conftest.py", "import pytest\n\n\n@pytest.fixture\ndef seventy():\n    return 70\n");
        var tests = Write(@"marks\tests\test_grades.py", """
            from grades import grade


            def test_first_at_seventy(seventy):
                assert grade(seventy) == "First"


            def test_fail():
                assert grade(30) == "Fail"
            """);

        FixedInGrades(await CheckAsync(tests), program);
    }

    /// <summary>A random.py written for last week's exercise in the folder above is never put ahead of the random Python has itself.</summary>
    [Fact]
    public async Task AModulePythonHasItselfIsNotTakenFromTheFolderAboveTheTests()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        Write(@"week3\random.py", "print('the wrong random')\n");
        var tests = Write(@"week3\tests\test_dice.py", """
            import random
            import unittest


            class DiceTests(unittest.TestCase):
                def test_one_sided_die(self):
                    self.assertEqual(random.randint(1, 1), 1)
            """);

        var report = await CheckAsync(tests);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors, and its test passes", report.SyntaxSummary);
    }

    [Fact]
    public async Task TheProgramsOwnModuleOutOfPythonsReachIsSaidToBeOneNotAPackageToInstall()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        Write(@"marks\app\grades.py", Grades);
        var tests = Write(@"marks\tests\test_grades.py", GradeUnittests);

        var report = await CheckAsync(tests);

        if (report.SyntaxSummary == "It could not be started") return;
        var outOfReach = Assert.Single(report.Findings);
        Assert.Equal("python-own-module-out-of-reach", outOfReach.RuleId);
        Assert.StartsWith("grades is the program's own module - grades.py in ", outOfReach.Explanation, StringComparison.Ordinal);
        Assert.Equal("Move grades.py to where Python looks for it: beside test_grades.py, or into a folder above it - which FixFinder looks in, " +
                     "and so does python -m pytest started there.", outOfReach.SuggestedFix);
        Assert.DoesNotContain("pip", outOfReach.SuggestedFix, StringComparison.Ordinal);
    }

    /// <summary>python scripts\run.py looks only in scripts, so a program importing from the folder above fails there - and is told how it runs.</summary>
    [Fact]
    public async Task AProgramImportingFromTheFolderAboveIsToldToRunAsAModule()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        Write(@"tools\helpers.py", "def double(x):\n    return x + x\n");
        var script = Write(@"tools\scripts\run.py", "from helpers import double\n\nprint(double(4))\n");

        var report = await CheckAsync(script);

        if (report.SyntaxSummary == "It could not be started") return;
        var outOfReach = Assert.Single(report.Findings);
        Assert.Equal("python-own-module-out-of-reach", outOfReach.RuleId);
        Assert.Equal($"Move helpers.py to where Python looks for it: beside run.py. Or run run.py as a module, started in {Path.Combine(_temp.Path, "tools")}, " +
                     "where Python then looks too: python -m scripts.run", outOfReach.SuggestedFix);
    }
}
