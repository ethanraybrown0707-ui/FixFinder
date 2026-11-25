using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Execution.Libraries;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A file of tests that fail, checked as a student would: the change to the code the tests call that makes every one of
/// them pass is found by running the tests with each change tried - in Python with unittest, and in Java with JUnit 5
/// where the CI workflow has fetched its jar - and the program's own files are never written to while changes are tried.
/// </summary>
public class TestRepairLiveTests(ITestOutputHelper output) : IDisposable
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

    private async Task<CheckReport> CheckAsync(string file, CodeLanguage language, TimeSpan? timeLimit = null)
    {
        var launch = TargetFactory.FromFile(file, timeLimit);
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry()) { Language = language };
        checker.Log += message => output.WriteLine($"log: {message}");
        var report = await checker.CheckAsync(launch);

        output.WriteLine($"syntax: {report.SyntaxSummary}");
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings) output.WriteLine($"[{finding.RuleId}] {finding.Title} :: {finding.Fix?.Title} :: {finding.Verified.Summary}");
        return report;
    }

    private const string Grades = """
        def average(marks):
            if not marks:
                return 0
            return sum(marks) / len(marks)


        def grade(mark):
            if mark > 70:
                return "First"
            if mark >= 60:
                return "2:1"
            if mark >= 50:
                return "2:2"
            return "Fail"
        """;

    private const string GradeTests = """
        import unittest

        from grades import average, grade


        class GradeTests(unittest.TestCase):
            def test_average(self):
                self.assertEqual(average([60, 80]), 70)

            def test_empty_average(self):
                self.assertEqual(average([]), 0)

            def test_first_at_seventy(self):
                self.assertEqual(grade(70), "First")

            def test_upper_second(self):
                self.assertEqual(grade(65), "2:1")

            def test_fail(self):
                self.assertEqual(grade(30), "Fail")


        if __name__ == "__main__":
            unittest.main()
        """;

    [Fact]
    public async Task TheChangeThatMakesTheFailingTestPassIsFoundInTheCodeItCalls()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var program = Write(@"grades\grades.py", Grades);
        var tests = Write(@"grades\test_grades.py", GradeTests);

        var report = await CheckAsync(tests, CodeLanguage.Python);

        if (report.SyntaxSummary == "It could not be started") return;
        var failed = Assert.Single(report.Findings, finding => finding.RuleId == "test-failed");
        Assert.NotNull(failed.Fix);
        Assert.Equal(program, failed.Fix!.File, ignoreCase: true);
        Assert.Equal(8, failed.Fix.StartLine);
        Assert.Equal(["    if mark >= 70:"], failed.Fix.NewLines);
        Assert.True(failed.Verified.IsVerified, failed.Verified.Summary);
        Assert.StartsWith("With this change every one of the 5 tests in test_grades.py passes - 1 of which failed before.", failed.FixCheckedBy, StringComparison.Ordinal);

        // Changes are tried in copies: the program's own file is as it was.
        Assert.Equal(Grades.ReplaceLineEndings("\n"), File.ReadAllText(program));
    }

    private const string Stack = """
        class Stack:
            def __init__(self):
                self.items = []

            def push(self, item):
                self.items.append(item)

            def pop(self):
                return self.items.pop(0)

            def peek(self):
                return self.items[len(self.items)]

            def size(self):
                return len(self.items)
        """;

    private const string StackTests = """
        import unittest

        from stack import Stack


        class StackTests(unittest.TestCase):
            def test_push_then_size(self):
                s = Stack()
                s.push(1)
                s.push(2)
                self.assertEqual(s.size(), 2)

            def test_pop_gives_last_pushed(self):
                s = Stack()
                s.push(1)
                s.push(2)
                self.assertEqual(s.pop(), 2)

            def test_peek_gives_last_pushed(self):
                s = Stack()
                s.push(1)
                s.push(2)
                self.assertEqual(s.peek(), 2)
        """;

    /// <summary>
    /// Two mistakes, each failing a test of its own: no one change makes every test pass, so the fix is built a change at a
    /// time - and adding 1 to every item pushed, which makes the pop test pass for the wrong reason, is given up when it
    /// leads nowhere.
    /// </summary>
    [Fact]
    public async Task CodeWithTwoMistakesIsFixedAChangeAtATime()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var program = Write(@"stack\stack.py", Stack);
        var tests = Write(@"stack\test_stack.py", StackTests);

        var report = await CheckAsync(tests, CodeLanguage.Python);

        if (report.SyntaxSummary == "It could not be started") return;
        var fixedFirst = Assert.Single(report.Findings, finding => finding is { RuleId: "test-failed", Fix: not null });
        Assert.Equal(9, fixedFirst.Fix!.StartLine);
        Assert.Equal(["        return self.items.pop()", "", "    def peek(self):", "        return self.items[len(self.items) - 1]"], fixedFirst.Fix.NewLines);
        Assert.True(fixedFirst.Verified.IsVerified, fixedFirst.Verified.Summary);
        Assert.Contains("so it was built a change at a time: each of its 2 changes", fixedFirst.FixCheckedBy, StringComparison.Ordinal);

        var alsoFixed = Assert.Single(report.Findings, finding => finding is { RuleId: "test-failed", Fix: null });
        Assert.EndsWith("makes this test pass too.", alsoFixed.SuggestedFix, StringComparison.Ordinal);
        Assert.Equal(Stack.ReplaceLineEndings("\n"), File.ReadAllText(program));
    }

    /// <summary>
    /// QuixBugs' bitcount (MIT licence): n ^= n - 1 never reaches 0, so the first test never finishes and none is said to
    /// fail - the run is stopped for time. The tests that never finished count as not passing, and the change that lets
    /// every test finish and pass goes on the run that was stopped.
    /// </summary>
    [Fact]
    public async Task TestsThatNeverFinishedAreMadeToFinishAndPass()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var program = Write(@"bits\bitcount.py", "def bitcount(n):\n    count = 0\n    while n:\n        n ^= n - 1\n        count += 1\n    return count\n");
        var tests = Write(@"bits\test_bitcount.py", """
            import unittest

            from bitcount import bitcount


            class BitcountTests(unittest.TestCase):
                def test_seven_ones(self):
                    self.assertEqual(bitcount(127), 7)

                def test_one_one(self):
                    self.assertEqual(bitcount(128), 1)

                def test_nothing(self):
                    self.assertEqual(bitcount(0), 0)
            """);

        var report = await CheckAsync(tests, CodeLanguage.Python, TimeSpan.FromSeconds(10));

        if (report.SyntaxSummary == "It could not be started") return;
        var stopped = Assert.Single(report.Findings, finding => finding is { RuleId: "timed-out", Fix: not null });
        Assert.Equal(program, stopped.Fix!.File, ignoreCase: true);
        Assert.Equal(["        n &= n - 1"], stopped.Fix.NewLines);
        Assert.True(stopped.Verified.IsVerified, stopped.Verified.Summary);
        Assert.Contains("never finished before, the tests stopped for time", stopped.FixCheckedBy, StringComparison.Ordinal);
    }

    /// <summary>A mistake no small change can put right is said to have been looked for, not given a change that does not work.</summary>
    [Fact]
    public async Task WhenNoSmallChangeMakesTheTestsPassThatIsSaid()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        Write(@"basket\basket.py", "def total(prices):\n    return prices[0]\n");
        var tests = Write(@"basket\test_basket.py", """
            import unittest

            from basket import total


            class BasketTests(unittest.TestCase):
                def test_one_price(self):
                    self.assertEqual(total([4]), 4)

                def test_three_prices(self):
                    self.assertEqual(total([4, 5, 6]), 15)
            """);

        var report = await CheckAsync(tests, CodeLanguage.Python);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.DoesNotContain(report.Findings, finding => finding is { RuleId: "test-failed", Fix: not null });
        Assert.Contains(report.Notes, note => note.Contains("to basket.py, the code the failing tests call", StringComparison.Ordinal) &&
                                              note.Contains("none made every test pass", StringComparison.Ordinal));
    }

    /// <summary>The folder of JUnit jars the CI workflow fetched, holding the one named, or null when it is not here.</summary>
    private static string? JUnitJar(string startsWith) =>
        Environment.GetEnvironmentVariable("FIXFINDER_JUNIT_JARS") is { Length: > 0 } folder && Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, startsWith + "*.jar").FirstOrDefault()
            : null;

    [Fact]
    public async Task InJavaTheChangeThatMakesTheFailingJUnitTestsPassIsFound()
    {
        if (Toolchains.FindJavac() is null || JUnitJar("junit-platform-console-standalone") is not { } junit) return;

        Directory.CreateDirectory(Path.Combine(_temp.Path, @"coursework\lib"));
        File.Copy(junit, Path.Combine(_temp.Path, @"coursework\lib", Path.GetFileName(junit)));
        var program = Write(@"coursework\Calculator.java", """
            public class Calculator {
                public int add(int left, int right) {
                    return left - right;
                }

                public int multiply(int left, int right) {
                    return left * right;
                }
            }
            """);
        var tests = Write(@"coursework\CalculatorTest.java", """
            import static org.junit.jupiter.api.Assertions.assertEquals;

            import org.junit.jupiter.api.Test;

            class CalculatorTest {
                private final Calculator calculator = new Calculator();

                @Test
                void addsTwoNumbers() {
                    assertEquals(4, calculator.add(2, 2));
                }

                @Test
                void addsANegativeNumber() {
                    assertEquals(2, calculator.add(3, -1));
                }

                @Test
                void multipliesTwoNumbers() {
                    assertEquals(6, calculator.multiply(2, 3));
                }
            }
            """);

        var report = await CheckAsync(tests, CodeLanguage.Java);

        var fixedFirst = Assert.Single(report.Findings, finding => finding is { RuleId: "test-failed", Fix: not null });
        Assert.Equal(program, fixedFirst.Fix!.File, ignoreCase: true);
        Assert.Equal(["        return left + right;"], fixedFirst.Fix.NewLines);
        Assert.True(fixedFirst.Verified.IsVerified, fixedFirst.Verified.Summary);
        Assert.StartsWith("With this change every one of the 3 tests in CalculatorTest.java passes - 2 of which failed before.", fixedFirst.FixCheckedBy, StringComparison.Ordinal);
    }

    /// <summary>Maven's layout: the tests in src\test\java, the code they call in src\main\java, and JUnit named in pom.xml.</summary>
    [Fact]
    public async Task InAMavenProjectTheChangeIsFoundInItsMainCode()
    {
        if (Toolchains.FindJavac() is null || JUnitJar("junit-platform-console-standalone") is not { } junit) return;

        var version = Path.GetFileNameWithoutExtension(junit)["junit-platform-console-standalone-".Length..];
        var repository = Path.Combine(_temp.Path, "repository");
        var jarFolder = Path.Combine(repository, "org", "junit", "platform", "junit-platform-console-standalone", version);
        Directory.CreateDirectory(jarFolder);
        File.Copy(junit, Path.Combine(jarFolder, Path.GetFileName(junit)));

        Write(@"calculator\pom.xml", $"""
            <project xmlns="http://maven.apache.org/POM/4.0.0">
              <modelVersion>4.0.0</modelVersion>
              <groupId>uni</groupId>
              <artifactId>calculator</artifactId>
              <version>1.0</version>
              <dependencies>
                <dependency>
                  <groupId>org.junit.platform</groupId>
                  <artifactId>junit-platform-console-standalone</artifactId>
                  <version>{version}</version>
                  <scope>test</scope>
                </dependency>
              </dependencies>
            </project>
            """);
        var program = Write(@"calculator\src\main\java\uni\Calculator.java", """
            package uni;

            public class Calculator {
                public int add(int left, int right) {
                    return left - right;
                }

                public int multiply(int left, int right) {
                    return left * right;
                }
            }
            """);
        var tests = Write(@"calculator\src\test\java\uni\CalculatorTest.java", """
            package uni;

            import static org.junit.jupiter.api.Assertions.assertEquals;

            import org.junit.jupiter.api.Test;

            class CalculatorTest {
                private final Calculator calculator = new Calculator();

                @Test
                void addsTwoNumbers() {
                    assertEquals(4, calculator.add(2, 2));
                }

                @Test
                void addsANegativeNumber() {
                    assertEquals(2, calculator.add(3, -1));
                }

                @Test
                void multipliesTwoNumbers() {
                    assertEquals(6, calculator.multiply(2, 3));
                }
            }
            """);

        using var stores = JavaLibraries.UsingStores(new LibraryStores([new MavenRepository(repository)]));
        var report = await CheckAsync(tests, CodeLanguage.Java);

        var fixedFirst = Assert.Single(report.Findings, finding => finding is { RuleId: "test-failed", Fix: not null });
        Assert.Equal(program, fixedFirst.Fix!.File, ignoreCase: true);
        Assert.Equal(["        return left + right;"], fixedFirst.Fix.NewLines);
        Assert.True(fixedFirst.Verified.IsVerified, fixedFirst.Verified.Summary);
    }
}
