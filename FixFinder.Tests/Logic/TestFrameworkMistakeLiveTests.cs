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
