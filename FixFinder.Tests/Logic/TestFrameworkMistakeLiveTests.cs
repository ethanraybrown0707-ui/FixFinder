using System.ComponentModel;
using System.Diagnostics;
using FixFinder.Core;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Tests that never run, or can never fail, each run with its own test runner - unittest, minitest and ExUnit the way FixFinder
/// runs a program, go test as go runs it - testing an add that subtracts: the mistake's tests pass although add is wrong, and
/// with the check's change they catch it. A runner this computer does not have, or a program Windows refused to start, is passed
/// over, and said to be.
/// </summary>
public class TestFrameworkMistakeLiveTests(ITestOutputHelper output)
{
    /// <summary>Each: the language, the check, the tests, what the runner says when they all pass, and what it says when one fails.</summary>
    public static TheoryData<string, string, string, string, string> Mistakes => new()
    {
        {
            "Python", "logic-python-test-never-run",
            "import unittest\n\n\ndef add(a, b):\n    return a - b\n\n\nclass AddTests(unittest.TestCase):\n    def check_add(self):\n        self.assertEqual(add(5, 2), 7)\n\n    def test_positive(self):\n        self.assertGreater(add(5, 2), 0)\n",
            "\tSUCCESSFUL\t", "\tFAILED\t"
        },
        {
            "Python", "logic-python-test-class-not-testcase",
            "import unittest\n\n\ndef add(a, b):\n    return a - b\n\n\nclass AddTests(unittest.TestCase):\n    def test_positive(self):\n        self.assertGreater(add(5, 2), 0)\n\n\nclass MoreTests:\n    def test_add(self):\n        self.assertEqual(add(5, 2), 7)\n",
            "\tSUCCESSFUL\t", "\tFAILED\t"
        },
        {
            "Python", "logic-python-assert-true-with-expected",
            "import unittest\n\n\ndef add(a, b):\n    return a - b\n\n\nclass AddTests(unittest.TestCase):\n    def test_add(self):\n        self.assertTrue(add(5, 2), 7)\n",
            "\tSUCCESSFUL\t", "\tFAILED\t"
        },
        {
            "Ruby", "logic-ruby-test-never-run",
            "require \"minitest/autorun\"\n\ndef add(a, b)\n  a - b\nend\n\nclass AddTest < Minitest::Test\n  def check_add\n    assert_equal 7, add(5, 2)\n  end\n\n  def test_positive\n    assert_operator add(5, 2), :>, 0\n  end\nend\n",
            "0 failures", "1 failures"
        },
        {
            "Ruby", "logic-ruby-assert-with-expected",
            "require \"minitest/autorun\"\n\ndef add(a, b)\n  a - b\nend\n\nclass AddTest < Minitest::Test\n  def test_add\n    assert add(5, 2), 7\n  end\nend\n",
            "0 failures", "1 failures"
        },
        {
            "Elixir", "logic-elixir-test-never-run",
            "ExUnit.start()\n\ndefmodule AddTest do\n  use ExUnit.Case\n\n  def add(a, b), do: a - b\n\n  def test_add do\n    assert add(5, 2) == 7\n  end\n\n  test \"positive\" do\n    assert add(5, 2) > 0\n  end\nend\n",
            "Result: 1 passed", "Failed: 1 test"
        },
        {
            "Perl", "logic-perl-ok-with-expected",
            "use strict;\nuse warnings;\nuse Test::More tests => 1;\n\nsub add { my ($first, $second) = @_; return $first - $second; }\n\nok(add(5, 2), 7);\n",
            "ok 1", "not ok 1"
        },
    };

    private static CodeLanguage LanguageNamed(string name) => CodeLanguage.All.Single(each => each.Name == name);

    private static bool RefusedByWindows(Observation observed) =>
        ApplicationControl.Refused(string.Join("\n", observed.Output) + "\n" + observed.WhyNotRun);

    private static string Changed(string code, LocalFix fix)
    {
        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(fix.StartLine - 1, fix.RemoveCount);
        lines.InsertRange(fix.StartLine - 1, fix.NewLines);
        return string.Join("\n", lines) + "\n";
    }

    private static LogicFinding FoundIn(string fileName, string check, string code, TempFolder temp)
    {
        var path = Path.Combine(temp.Path, fileName);
        File.WriteAllText(path, code);
        return Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == check);
    }

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakesTestsPassAWrongAddAndTheChangesCatchIt(string language, string check, string tests, string allPass, string oneFails)
    {
        using var temp = new TempFolder();
        var extension = LanguageNamed(language).Extensions[0];
        var fix = FoundIn("add_test" + extension, check, tests, temp).Fix!;

        var wrong = await SnippetRunner.RunAsync(LanguageNamed(language), tests);
        var right = wrong.Ran && !RefusedByWindows(wrong) ? await SnippetRunner.RunAsync(LanguageNamed(language), Changed(tests, fix)) : null;
        if (right is null || RefusedByWindows(right))
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun ?? "Windows refused to start it"}");
            return;
        }

        var mistakeSaid = string.Join("\n", wrong.Output);
        var changeSaid = string.Join("\n", right.Output);
        output.WriteLine($"the mistake's tests said:\n{mistakeSaid}\nthe change's tests said:\n{changeSaid}");
        Assert.Contains(allPass, mistakeSaid);
        Assert.DoesNotContain(oneFails, mistakeSaid);
        Assert.Contains(oneFails, changeSaid);
    }

    [Theory]
    // The test still passes, add wrong as it is: these have no change, and are shown passing.
    [InlineData("logic-python-assertion-swallowed", "import unittest\n\n\ndef add(a, b):\n    return a - b\n\n\nclass AddTests(unittest.TestCase):\n    def test_add(self):\n        try:\n            self.assertEqual(add(5, 2), 7)\n        except AssertionError:\n            pass\n")]
    [InlineData("logic-python-test-checks-nothing", "import unittest\n\n\ndef add(a, b):\n    return a - b\n\n\nclass AddTests(unittest.TestCase):\n    def test_add(self):\n        result = add(5, 2)\n        print(result)\n")]
    public async Task ATestThatCanNeverFailPassesAWrongAdd(string check, string tests)
    {
        using var temp = new TempFolder();
        FoundIn("test_add.py", check, tests, temp);

        var ran = await SnippetRunner.RunAsync(LanguageNamed("Python"), tests);
        if (!ran.Ran || RefusedByWindows(ran))
        {
            output.WriteLine($"Not run on this computer: {ran.WhyNotRun ?? "Windows refused to start it"}");
            return;
        }

        var said = string.Join("\n", ran.Output);
        output.WriteLine(said);
        // FixFinder runs unittest through its own launcher, which reports each test on a line of its own.
        Assert.Contains("\tSUCCESSFUL\t", said);
        Assert.DoesNotContain("\tFAILED\t", said);
    }

    [Fact]
    public async Task ASetUpMisspeltIsNeverCalledAndTheChangeIsCalled()
    {
        const string tests = "import unittest\n\n\nclass ListTests(unittest.TestCase):\n    def setup(self):\n        self.items = [1, 2]\n\n    def test_length(self):\n        self.assertEqual(len(self.items), 2)\n";
        using var temp = new TempFolder();
        var fix = FoundIn("test_list.py", "logic-python-setup-misspelt", tests, temp).Fix!;

        var wrong = await SnippetRunner.RunAsync(LanguageNamed("Python"), tests);
        if (!wrong.Ran || RefusedByWindows(wrong))
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun ?? "Windows refused to start it"}");
            return;
        }
        var right = await SnippetRunner.RunAsync(LanguageNamed("Python"), Changed(tests, fix));

        output.WriteLine($"{string.Join("\n", wrong.Output)}\n---\n{string.Join("\n", right.Output)}");
        Assert.Contains("AttributeError", string.Join("\n", wrong.Output));
        Assert.Contains("\tSUCCESSFUL\t", string.Join("\n", right.Output));
    }

    /// <summary>Runs go test in a folder of a module, adder.go and its tests: what go said, or null when this computer has no go.</summary>
    private static async Task<string?> GoTestAsync(string folder, string tests)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "go.mod"), "module adder\n\ngo 1.21\n");
        File.WriteAllText(Path.Combine(folder, "adder.go"), "package adder\n\nfunc Add(a, b int) int { return a - b }\n");
        File.WriteAllText(Path.Combine(folder, "adder_test.go"), tests);
        try
        {
            using var go = Process.Start(new ProcessStartInfo("go", "test -v ./...")
                { WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
            var said = await go.StandardOutput.ReadToEndAsync() + await go.StandardError.ReadToEndAsync();
            await go.WaitForExitAsync();
            return said;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Compiles a module with erlc and runs its EUnit tests: what Erlang said, or null when this computer has no Erlang.</summary>
    private static async Task<string?> EunitAsync(string folder, string module, string code)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, module + ".erl"), code);
        try
        {
            using var compiling = Process.Start(new ProcessStartInfo("erlc", $"+warn_all {module}.erl")
                { WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
            var said = await compiling.StandardOutput.ReadToEndAsync() + await compiling.StandardError.ReadToEndAsync();
            await compiling.WaitForExitAsync();

            using var running = Process.Start(new ProcessStartInfo("erl", $"-noshell -eval \"eunit:test({module}, [verbose]), init:stop().\"")
                { WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
            said += await running.StandardOutput.ReadToEndAsync() + await running.StandardError.ReadToEndAsync();
            await running.WaitForExitAsync();
            return said;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    [Fact]
    public async Task AnEunitTestNamedTestFirstIsNeverRunAndTheChangeRunsIt()
    {
        const string code = "-module(adder).\n-include_lib(\"eunit/include/eunit.hrl\").\n-export([add/2]).\n\nadd(A, B) -> A - B.\n\ntest_add() ->\n    ?assertEqual(7, add(5, 2)).\n\nadd_positive_test() ->\n    ?assert(add(5, 2) > 0).\n";
        using var temp = new TempFolder();
        var fix = FoundIn("adder.erl", "logic-erlang-test-never-run", code, temp).Fix!;

        if (await EunitAsync(Path.Combine(temp.Path, "mistake"), "adder", code) is not { } wrong)
        {
            output.WriteLine("Not run on this computer: no Erlang");
            return;
        }
        var right = await EunitAsync(Path.Combine(temp.Path, "change"), "adder", Changed(code, fix)) ?? "";
        output.WriteLine($"{wrong}\n---\n{right}");

        Assert.Contains("function test_add/0 is unused", wrong);
        Assert.Contains("Test passed.", wrong);
        Assert.Contains("add_test", right);
        Assert.Contains("Failed: 1.", right);
    }

    /// <summary>Runs a program and gives back what it wrote, or null when it could not be started.</summary>
    private static async Task<string?> RunToolAsync(string program, string arguments, string folder)
    {
        try
        {
            using var running = Process.Start(new ProcessStartInfo(program, arguments)
                { WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
            var said = await running.StandardOutput.ReadToEndAsync() + await running.StandardError.ReadToEndAsync();
            await running.WaitForExitAsync();
            return said;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Runs pytest on a test file in a folder of its own: with the Python the CI workflow made for it, or else this computer's -
    /// what pytest said, or null when there is no pytest here.
    /// </summary>
    private static async Task<string?> PytestAsync(string folder, string tests)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "test_add.py"), tests);
        var python = Environment.GetEnvironmentVariable("FIXFINDER_TEST_PACKAGES_PYTHON") is { Length: > 0 } made ? made : "python";
        var said = await RunToolAsync(python, "-m pytest -v -p no:cacheprovider test_add.py", folder);
        return said is null || said.Contains("No module named pytest", StringComparison.Ordinal) ? null : said;
    }

    [Theory]
    [InlineData("logic-python-pytest-test-never-run", "def add(a, b):\n    return a - b\n\n\ndef check_add():\n    assert add(5, 2) == 7\n\n\ndef test_positive():\n    assert add(5, 2) > 0\n")]
    [InlineData("logic-python-pytest-class-not-collected", "def add(a, b):\n    return a - b\n\n\ndef test_positive():\n    assert add(5, 2) > 0\n\n\nclass AddTests:\n    def test_add(self):\n        assert add(5, 2) == 7\n")]
    [InlineData("logic-python-assert-with-expected", "def add(a, b):\n    return a - b\n\n\ndef test_add():\n    assert add(5, 2), 7\n")]
    public async Task APytestTestThatNeverRunsOrCanNeverFailPassesAWrongAddAndTheChangeCatchesIt(string check, string tests)
    {
        using var temp = new TempFolder();
        var fix = FoundIn("test_add.py", check, tests, temp).Fix!;

        if (await PytestAsync(Path.Combine(temp.Path, "mistake"), tests) is not { } wrong)
        {
            output.WriteLine("Not run on this computer: no pytest");
            return;
        }
        var right = await PytestAsync(Path.Combine(temp.Path, "change"), Changed(tests, fix)) ?? "";
        output.WriteLine($"{wrong}\n---\n{right}");

        Assert.Matches(@"\b\d+ passed\b", wrong);
        Assert.DoesNotMatch(@"\bfailed\b", wrong);
        Assert.Matches(@"\b1 failed\b", right);
    }

    /// <summary>
    /// Compiles a JUnit 5 class and runs it with JUnit's console launcher, from the jars the CI workflow fetched - what JUnit
    /// said, or null when JUnit, javac or java is not here.
    /// </summary>
    private static async Task<string?> JUnitAsync(string folder, string tests)
    {
        if (Environment.GetEnvironmentVariable("FIXFINDER_JUNIT_JARS") is not { Length: > 0 } jars || !Directory.Exists(jars) ||
            Directory.EnumerateFiles(jars, "junit-platform-console-standalone*.jar").FirstOrDefault() is not { } standalone) return null;

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "AdderTest.java"), tests);
        if (await RunToolAsync("javac", $"-cp \"{standalone}\" AdderTest.java", folder) is not { } compiled) return null;
        var ran = await RunToolAsync("java", $"-jar \"{standalone}\" execute --class-path . --select-class AdderTest --disable-ansi-colors", folder);
        return ran is null ? null : compiled + ran;
    }

    private const string JupiterHead = "import static org.junit.jupiter.api.Assertions.assertEquals;\nimport static org.junit.jupiter.api.Assertions.assertTrue;\n\n" +
                                       "import java.util.ArrayList;\nimport java.util.List;\nimport org.junit.jupiter.api.Test;\n\nclass AdderTest {\n" +
                                       "    static int add(int a, int b) {\n        return a - b;\n    }\n\n    @Test\n    void positive() {\n        assertTrue(add(5, 2) > 0);\n    }\n\n";

    [Theory]
    // Each: the check, the tests, how many JUnit found and how many failed - with the mistake, and then with the change.
    [InlineData("logic-java-test-missing-annotation", JupiterHead + "    void addsTwoNumbers() {\n        assertEquals(7, add(5, 2));\n    }\n}\n", 1, 0, 2, 1)]
    [InlineData("logic-java-test-not-run-shape", JupiterHead + "    @Test\n    private void addsTwoNumbers() {\n        assertEquals(7, add(5, 2));\n    }\n}\n", 1, 0, 2, 1)]
    [InlineData("logic-java-test-not-run-shape", JupiterHead + "    @Test\n    static void addsTwoNumbers() {\n        assertEquals(7, add(5, 2));\n    }\n}\n", 1, 0, 2, 1)]
    [InlineData("logic-java-setup-not-annotated", JupiterHead + "    private List<Integer> numbers;\n\n    void setUp() {\n        numbers = new ArrayList<>();\n    }\n\n    @Test\n    void usesSetUp() {\n        numbers.add(1);\n        assertEquals(1, numbers.size());\n    }\n}\n", 2, 1, 2, 0)]
    public async Task AJUnitTestThatNeverRunsIsRunWithTheChange(string check, string tests, int mistakeFound, int mistakeFailed, int changeFound, int changeFailed)
    {
        using var temp = new TempFolder();
        var fix = FoundIn("AdderTest.java", check, tests, temp).Fix!;

        if (await JUnitAsync(Path.Combine(temp.Path, "mistake"), tests) is not { } wrong)
        {
            output.WriteLine("Not run on this computer: no JUnit, javac or java");
            return;
        }
        var right = await JUnitAsync(Path.Combine(temp.Path, "change"), Changed(tests, fix)) ?? "";
        output.WriteLine($"{wrong}\n---\n{right}");

        Assert.Matches($@"\b{mistakeFound} tests found\b", wrong);
        Assert.Matches($@"\b{mistakeFailed} tests failed\b", wrong);
        Assert.Matches($@"\b{changeFound} tests found\b", right);
        Assert.Matches($@"\b{changeFailed} tests failed\b", right);
    }

    [Fact]
    public async Task AJUnitAssertionCaughtInTheTestPassesAWrongAdd()
    {
        const string tests = JupiterHead + "    @Test\n    void addsTwoNumbers() {\n        try {\n            assertEquals(7, add(5, 2));\n        } catch (AssertionError error) {\n            System.out.println(\"failed: \" + error.getMessage());\n        }\n    }\n}\n";
        using var temp = new TempFolder();
        FoundIn("AdderTest.java", "logic-java-assertion-swallowed", tests, temp);

        if (await JUnitAsync(Path.Combine(temp.Path, "mistake"), tests) is not { } said)
        {
            output.WriteLine("Not run on this computer: no JUnit, javac or java");
            return;
        }
        output.WriteLine(said);
        Assert.Contains("failed: expected: <7> but was: <3>", said);
        Assert.Matches(@"\b2 tests successful\b", said);
        Assert.Matches(@"\b0 tests failed\b", said);
    }

    [Fact]
    public async Task AGoTestNotNamedTestIsNeverRunAndTheChangeRunsIt()
    {
        const string tests = "package adder\n\nimport \"testing\"\n\nfunc testAdd(t *testing.T) {\n\tif Add(5, 2) != 7 {\n\t\tt.Errorf(\"Add(5, 2) = %d\", Add(5, 2))\n\t}\n}\n\nfunc TestPositive(t *testing.T) {\n\tif Add(5, 2) <= 0 {\n\t\tt.Error(\"not positive\")\n\t}\n}\n";
        using var temp = new TempFolder();
        var fix = FoundIn("adder_test.go", "logic-go-test-never-run", tests, temp).Fix!;

        if (await GoTestAsync(Path.Combine(temp.Path, "mistake"), tests) is not { } wrong || ApplicationControl.Refused(wrong))
        {
            output.WriteLine("Not run on this computer: no go, or Windows refused to start what it built");
            return;
        }
        var right = await GoTestAsync(Path.Combine(temp.Path, "change"), Changed(tests, fix)) ?? "";
        output.WriteLine($"{wrong}\n---\n{right}");
        if (ApplicationControl.Refused(right)) return;

        Assert.DoesNotContain("testAdd", wrong);
        Assert.Contains("--- PASS: TestPositive", wrong);
        Assert.Contains("--- FAIL: TestAdd", right);
    }
}
