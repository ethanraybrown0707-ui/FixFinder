using System.Text.Json;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Execution.Libraries;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A file of tests that all pass, checked as a student would: the tests are measured by small changes of the kinds a
/// mistake makes, made to the code they call one at a time in a copy, with the tests run against each - in Python with
/// unittest, in JavaScript with node:test, and in Java with JUnit 5 where the CI workflow has fetched its jar. Each change
/// every test still passed with is a suggestion on its line, and the program's own files are never written to.
/// </summary>
public class TestStrengthLiveTests(ITestOutputHelper output) : IDisposable
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

    private async Task<CheckReport> CheckAsync(string file, CodeLanguage language)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry()) { Language = language };
        checker.Log += message => output.WriteLine($"log: {message}");
        var report = await checker.CheckAsync(launch);

        output.WriteLine($"syntax: {report.SyntaxSummary}");
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings) output.WriteLine($"[{finding.RuleId}] {finding.Line}: {finding.Title} :: {finding.Found}");
        return report;
    }

    /// <summary>The note that says what measuring the tests showed, or null when they were not measured.</summary>
    private static string? MeasuredNote(CheckReport report) =>
        report.Notes.FirstOrDefault(note => note.StartsWith("The tests were measured with ", StringComparison.Ordinal));

    /// <summary>The lines each change the tests missed was made to, in order.</summary>
    private static List<int?> MissedLines(CheckReport report) =>
        report.Findings.Where(finding => finding.RuleId == "tests-miss-change").Select(finding => finding.Line).ToList();

    private const string PythonGrades = """
        def grade(score):
            if score >= 70:
                return "A"
            if score >= 50:
                return "B"
            return "Fail"


        def average(marks):
            total = 0
            for mark in marks:
                total += mark
            return total / len(marks)
        """;

    /// <summary>Tests that pass, but none of them on a boundary, and none with an average that is not a whole number.</summary>
    private const string PythonWeakTests = """
        import unittest

        from grades import average, grade


        class GradeTests(unittest.TestCase):
            def test_a_high_score_is_an_a(self):
                self.assertEqual(grade(85), "A")

            def test_a_low_score_fails(self):
                self.assertEqual(grade(30), "Fail")

            def test_the_average_of_two_marks(self):
                self.assertEqual(average([2, 4]), 3)


        if __name__ == "__main__":
            unittest.main()
        """;

    [Fact]
    public async Task InPythonTheChangesUnittestMissesAreSaidOnTheirLines()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var program = Write(@"grades\grades.py", PythonGrades);
        var tests = Write(@"grades\test_grades.py", PythonWeakTests);

        var report = await CheckAsync(tests, CodeLanguage.Python);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("The tests were measured with 6 small changes to grades.py, each made on its own in a copy, of the kinds a mistake makes: " +
                     "they caught 3 of the 6 that ran.", MeasuredNote(report));
        Assert.Equal(new int?[] { 2, 4, 13 }, MissedLines(report));

        var boundary = report.Findings.First(finding => finding.RuleId == "tests-miss-change");
        Assert.Equal(Severity.Suggestion, boundary.Severity);
        Assert.Equal(program, boundary.File, ignoreCase: true);
        Assert.Equal("The tests do not notice `>=` changed to `>` on line 2", boundary.Title);
        Assert.Equal("Every one of the 3 tests still passed with line 2 changed from `if score >= 70:` to `if score > 70:` - a comparison off by one, " +
                     "or the wrong way round - in a copy of the program, so that mistake there would go unnoticed.", boundary.Found);

        var division = report.Findings.Last(finding => finding.RuleId == "tests-miss-change");
        Assert.Equal("The tests do not notice `/` changed to `//` on line 13", division.Title);
        Assert.Equal(PythonGrades.ReplaceLineEndings("\n"), File.ReadAllText(program));
    }

    /// <summary>The same tests written for pytest, run with a Python that has it, which the project's VS Code settings name.</summary>
    [Fact]
    public async Task InPythonTheChangesPytestMissesAreSaidOnTheirLines()
    {
        if (!LocalFixLiveTests.Available("python") || NotebookTests.PythonWith("pytest") is not { } python) return;

        Write(@"grades\.vscode\settings.json", JsonSerializer.Serialize(new Dictionary<string, string> { ["python.defaultInterpreterPath"] = python }));
        Write(@"grades\grades.py", PythonGrades);
        var tests = Write(@"grades\test_grades.py", """
            from grades import average, grade


            def test_a_high_score_is_an_a():
                assert grade(85) == "A"


            def test_a_low_score_fails():
                assert grade(30) == "Fail"


            def test_the_average_of_two_marks():
                assert average([2, 4]) == 3
            """);

        var report = await CheckAsync(tests, CodeLanguage.Python);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors, and all 3 of its tests pass", report.SyntaxSummary);
        Assert.Equal("The tests were measured with 6 small changes to grades.py, each made on its own in a copy, of the kinds a mistake makes: " +
                     "they caught 3 of the 6 that ran.", MeasuredNote(report));
        Assert.Equal(new int?[] { 2, 4, 13 }, MissedLines(report));
    }

    [Fact]
    public async Task InPythonTestsThatCatchEveryChangeHaveNoSuggestions()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        Write(@"grades\grades.py", PythonGrades);
        var tests = Write(@"grades\test_grades.py", """
            import unittest

            from grades import average, grade


            class GradeTests(unittest.TestCase):
                def test_seventy_is_an_a(self):
                    self.assertEqual(grade(70), "A")

                def test_sixty_nine_is_a_b(self):
                    self.assertEqual(grade(69), "B")

                def test_fifty_is_a_b(self):
                    self.assertEqual(grade(50), "B")

                def test_forty_nine_fails(self):
                    self.assertEqual(grade(49), "Fail")

                def test_an_average_with_a_fraction(self):
                    self.assertEqual(average([1, 2]), 1.5)


            if __name__ == "__main__":
                unittest.main()
            """);

        var report = await CheckAsync(tests, CodeLanguage.Python);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("The tests were measured with 6 small changes to grades.py, each made on its own in a copy, of the kinds a mistake makes: " +
                     "they caught 6 of the 6 that ran.", MeasuredNote(report));
        Assert.Empty(MissedLines(report));
    }

    /// <summary>When a test fails, what to change comes first: the tests are not measured until every one passes.</summary>
    [Fact]
    public async Task InPythonFailingTestsAreNotMeasured()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        Write(@"grades\grades.py", PythonGrades);
        var tests = Write(@"grades\test_grades.py", """
            import unittest

            from grades import grade


            class GradeTests(unittest.TestCase):
                def test_seventy_is_a_b(self):
                    self.assertEqual(grade(70), "B")


            if __name__ == "__main__":
                unittest.main()
            """);

        var report = await CheckAsync(tests, CodeLanguage.Python);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Contains(report.Findings, finding => finding.RuleId == "test-failed");
        Assert.Null(MeasuredNote(report));
        Assert.Empty(MissedLines(report));
    }

    /// <summary>Doctests are written in the code they test, so a change could reach the tests themselves: they are not measured.</summary>
    [Fact]
    public async Task InPythonDoctestsAreNotMeasured()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var squares = Write(@"squares\squares.py", """"
            import doctest


            def square(n):
                """
                >>> square(3)
                9
                """
                return n * n


            if __name__ == "__main__":
                doctest.testmod()
            """");

        var report = await CheckAsync(squares, CodeLanguage.Python);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors, and its doctest passes", report.SyntaxSummary);
        Assert.Null(MeasuredNote(report));
    }

    [Fact]
    public async Task InJavaScriptTheChangesNodeTestMissesAreSaidOnTheirLines()
    {
        if (!LocalFixLiveTests.Available("node")) return;

        const string grades = """
            function grade(score) {
                if (score >= 70) {
                    return "A";
                }
                if (score >= 50) {
                    return "B";
                }
                return "Fail";
            }

            function average(marks) {
                let total = 0;
                for (const mark of marks) {
                    total += mark;
                }
                return total / marks.length;
            }

            module.exports = { grade, average };
            """;
        var program = Write(@"grades\grades.js", grades);
        var tests = Write(@"grades\grades.test.js", """
            const test = require("node:test");
            const assert = require("node:assert");
            const { grade, average } = require("./grades");

            test("a high score is an A", () => {
                assert.strictEqual(grade(85), "A");
            });

            test("a low score fails", () => {
                assert.strictEqual(grade(30), "Fail");
            });

            test("the average of two marks", () => {
                assert.strictEqual(average([2, 4]), 3);
            });
            """);

        var report = await CheckAsync(tests, CodeLanguage.JavaScript);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("The tests were measured with 7 small changes to grades.js, each made on its own in a copy, of the kinds a mistake makes: " +
                     "they caught 5 of the 7 that ran.", MeasuredNote(report));
        Assert.Equal(new int?[] { 2, 5 }, MissedLines(report));
        Assert.Equal("Every one of the 3 tests still passed with line 5 changed from `if (score >= 50) {` to `if (score > 50) {` - a comparison off by one, " +
                     "or the wrong way round - in a copy of the program, so that mistake there would go unnoticed.",
                     report.Findings.Last(finding => finding.RuleId == "tests-miss-change").Found);
        Assert.Equal(grades.ReplaceLineEndings("\n"), File.ReadAllText(program));
    }

    /// <summary>The folder of JUnit jars the CI workflow fetched, holding the one named, or null when it is not here.</summary>
    private static string? JUnitJar(string startsWith) =>
        Environment.GetEnvironmentVariable("FIXFINDER_JUNIT_JARS") is { Length: > 0 } folder && Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, startsWith + "*.jar").FirstOrDefault()
            : null;

    [Fact]
    public async Task InJavaTheChangesJUnitMissesAreSaidOnTheirLines()
    {
        if (Toolchains.FindJavac() is null || JUnitJar("junit-platform-console-standalone") is not { } junit) return;

        Directory.CreateDirectory(Path.Combine(_temp.Path, @"coursework\lib"));
        File.Copy(junit, Path.Combine(_temp.Path, @"coursework\lib", Path.GetFileName(junit)));
        const string grades = """
            public class Grades {
                static String grade(int score) {
                    if (score >= 70) {
                        return "A";
                    }
                    if (score >= 50) {
                        return "B";
                    }
                    return "Fail";
                }
            }
            """;
        var program = Write(@"coursework\Grades.java", grades);
        var tests = Write(@"coursework\GradesTest.java", """
            import static org.junit.jupiter.api.Assertions.assertEquals;

            import org.junit.jupiter.api.Test;

            public class GradesTest {
                @Test
                void aHighScoreIsAnA() {
                    assertEquals("A", Grades.grade(85));
                }

                @Test
                void aLowScoreFails() {
                    assertEquals("Fail", Grades.grade(30));
                }
            }
            """);

        var report = await CheckAsync(tests, CodeLanguage.Java);

        Assert.StartsWith("The tests were measured with ", MeasuredNote(report));
        Assert.Contains(" small changes to Grades.java, each made on its own in a copy, of the kinds a mistake makes: they caught ", MeasuredNote(report));
        Assert.Equal(new int?[] { 3, 6 }, MissedLines(report));
        Assert.Equal("The tests do not notice `>=` changed to `>` on line 3", report.Findings.First(finding => finding.RuleId == "tests-miss-change").Title);
        Assert.Equal(program, report.Findings.First(finding => finding.RuleId == "tests-miss-change").File, ignoreCase: true);
        Assert.Equal(grades.ReplaceLineEndings("\n"), File.ReadAllText(program));
    }
}
