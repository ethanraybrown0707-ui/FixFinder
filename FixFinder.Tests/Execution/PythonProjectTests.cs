using System.Diagnostics;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Covers checking a Python program as the project it belongs to is set up: run with the project's own virtual
/// environment, so what is installed only there is found as its IDE finds it.
/// </summary>
public class PythonProjectTests(ITestOutputHelper output) : IDisposable
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

    private string Folder(string relative) => Path.GetFullPath(Path.Combine(_temp.Path, relative));

    private async Task<CheckReport> CheckAsync(string file, string? expected = null, TimeSpan? timeLimit = null)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);
        if (timeLimit is { } limit) launch = launch with { Spec = launch.Spec!.WithTimeout(limit) };
        output.WriteLine($"how: {launch.Explanation}");

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry())
        {
            Language = CodeLanguage.Python,
            Expected = expected is null ? null : ExpectedBehaviour.From([new ExpectedRun(null, expected)], ""),
        };

        var report = await checker.CheckAsync(launch);
        output.WriteLine($"syntax: {report.SyntaxSummary} | logic: {report.LogicSummary}");
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings) output.WriteLine($"[{finding.Severity}/{finding.Confidence}] {finding.Location} {finding.RuleId}: {finding.Title}");
        return report;
    }

    /// <summary>
    /// A virtual environment made the way PyCharm and VS Code make one, with nothing downloaded: python -m venv without
    /// pip. False when there is no Python here to make it with.
    /// </summary>
    private bool MadeEnvironment(string folder)
    {
        if (!LocalFixLiveTests.Available("python") || TargetFactory.FindOnPath("python") is not { } python) return false;

        using var making = Process.Start(new ProcessStartInfo(python, $"-m venv --without-pip \"{folder}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        making.StandardOutput.ReadToEnd();
        making.StandardError.ReadToEnd();
        making.WaitForExit(120_000);

        return File.Exists(Path.Combine(folder, "Scripts", "python.exe")) || File.Exists(Path.Combine(folder, "bin", "python"));
    }

    /// <summary>A module installed into an environment only, as pip would put it there.</summary>
    private static void InstallInto(string environment, string module, string source)
    {
        var sitePackages = OperatingSystem.IsWindows()
            ? Path.Combine(environment, "Lib", "site-packages")
            : Directory.EnumerateDirectories(Path.Combine(environment, "lib")).Select(version => Path.Combine(version, "site-packages")).First();

        Directory.CreateDirectory(Path.Combine(sitePackages, module));
        File.WriteAllText(Path.Combine(sitePackages, module, "__init__.py"), source.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task AProgramRunsWithItsProjectsOwnEnvironmentAndWhatIsInstalledThere()
    {
        var environment = Folder(@"coursework\.venv");
        if (!MadeEnvironment(environment)) return;

        InstallInto(environment, "gradetools", "def classify(mark):\n    return \"pass\" if mark >= 40 else \"fail\"\n");
        var program = Write(@"coursework\grades.py", "from gradetools import classify\n\nfor mark in (72, 55, 38):\n    print(mark, classify(mark))\n");

        Assert.Contains("the Python in .venv, the project's own environment", TargetFactory.FromFile(program).Explanation, StringComparison.Ordinal);

        var report = await CheckAsync(program, "72 pass\n55 pass\n38 fail");

        Assert.Empty(report.Findings);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    [Fact]
    public async Task AChangeIsTriedOnACopyOfTheProgramWithTheProjectsOwnEnvironmentToo()
    {
        var environment = Folder(@"tried\.venv");
        if (!MadeEnvironment(environment)) return;

        InstallInto(environment, "gradetools", "def classify(mark):\n    return \"pass\" if mark >= 40 else \"fail\"\n");
        var program = Write(@"tried\grades.py", """
            from gradetools import classify


            def average(values):
                return sum(values) / len(values)


            for mark in (72, 55, 38):
                print(mark, classify(mark))
            print(average([]))
            """);

        var report = await CheckAsync(program);

        // The copy a fix is tried on is outside the project, where its environment is not, and it runs with it all the same:
        // with the Python on PATH, gradetools would not be found and the copy would stop before it reached the fixed line.
        var byZero = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.StartsWith("It crashed: ZeroDivisionError", byZero.Title, StringComparison.Ordinal);
        Assert.True(byZero.Verified.IsVerified, string.Join(" / ", byZero.Verified.Steps.Select(step => step.Detail)));
    }

    [Fact]
    public void AnEnvironmentBeyondTheProjectsOwnFolderIsNotTaken()
    {
        // An environment of some other project further up: the folder with .git is where this project begins.
        Write(@"outer\.venv\pyvenv.cfg", "include-system-site-packages = false\n");
        Write(@"outer\.venv\Scripts\python.exe", "");
        Directory.CreateDirectory(Folder(@"outer\project\.git"));
        var program = Write(@"outer\project\app\main.py", "print('hello')\n");

        Assert.Null(PythonEnvironment.For(program));
    }

    [Fact]
    public void AnEnvironmentWhosePythonIsNoLongerInstalledIsPassedOver()
    {
        Write(@"stale\.venv\pyvenv.cfg", $"home = {Folder("uninstalled-python")}\ninclude-system-site-packages = false\n");
        Write(@"stale\.venv\Scripts\python.exe", "");
        var program = Write(@"stale\main.py", "print('hello')\n");

        Assert.Null(PythonEnvironment.For(program));
    }

    [Fact]
    public async Task AWindowProgramStillRunningWhenItsTimeRunsOutIsANoteNotAMistake()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        // Waits as tkinter's mainloop does, without opening a window while the tests run.
        var program = Write(@"window\converter.py", """
            import threading
            import tkinter as tk


            def build(root):
                tk.Label(root, text="Temperature converter").pack()


            threading.Event().wait()
            """);

        var report = await CheckAsync(program, timeLimit: TimeSpan.FromSeconds(5));

        Assert.DoesNotContain(report.Findings, finding => finding.RuleId == "timed-out");
        Assert.Contains(report.Notes, note => note.StartsWith("converter.py is a program with a window - it uses tkinter", StringComparison.Ordinal));
        Assert.Equal("No syntax errors, and it was still running when its time ran out, as a program with a window does", report.SyntaxSummary);
    }

    [Fact]
    public async Task AServerStillWaitingForConnectionsIsANoteAndAnEndlessLoopBesideItIsStillWarnedAbout()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        // On this computer's loopback address and a port the system picks, so nothing outside can connect or be asked to allow it.
        var server = Write(@"web\server.py", """
            from http.server import BaseHTTPRequestHandler, HTTPServer


            class Hello(BaseHTTPRequestHandler):
                def do_GET(self):
                    self.send_response(200)
                    self.end_headers()
                    self.wfile.write(b"hello")


            HTTPServer(("127.0.0.1", 0), Hello).serve_forever()
            """);
        var endless = Write(@"web\counter.py", "count = 0\ntotal = 0\nwhile count < 10:\n    total += count\nprint(total)\n");

        var served = await CheckAsync(server, timeLimit: TimeSpan.FromSeconds(5));

        Assert.DoesNotContain(served.Findings, finding => finding.RuleId == "timed-out");
        Assert.Contains(served.Notes, note => note.StartsWith("server.py is a server - it waits for connections with Python's http.server", StringComparison.Ordinal));

        var counted = await CheckAsync(endless, timeLimit: TimeSpan.FromSeconds(5));

        Assert.Contains(counted.Findings, finding => finding.RuleId == "timed-out");
        Assert.DoesNotContain(counted.Notes, note => note.Contains("keeps running until", StringComparison.Ordinal));
    }

    private const string Bank = """
        class InsufficientFunds(Exception):
            pass


        class BankAccount:
            def __init__(self, owner, balance=0):
                self.owner = owner
                self.balance = balance

            def withdraw(self, amount):
                if amount > self.balance + 1:
                    raise InsufficientFunds(f"{self.owner} has only {self.balance}")
                self.balance -= amount

            def share_of(self, people):
                return self.balance / people
        """;

    private const string BankTests = """
        import unittest

        from bank import BankAccount, InsufficientFunds


        class BankAccountTests(unittest.TestCase):
            def setUp(self):
                self.account = BankAccount("Ada", 100)

            def test_withdraw_takes_away(self):
                self.account.withdraw(30)
                self.assertEqual(self.account.balance, 70)

            def test_withdraw_more_than_balance(self):
                with self.assertRaises(InsufficientFunds):
                    self.account.withdraw(101)

            def test_share_between_nobody(self):
                self.assertEqual(self.account.share_of(0), 0)

            def test_shares(self):
                for people, share in [(1, 100), (2, 50), (4, 20)]:
                    with self.subTest(people=people):
                        self.assertEqual(self.account.share_of(people), share)
        """;

    [Fact]
    public async Task EachUnittestTestIsRunAndEachThatFailsIsReportedOnItsOwnLine()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        // No unittest.main(): the file's tests are run all the same.
        Write(@"tests\bank.py", Bank);
        var tests = Write(@"tests\test_bank.py", BankTests);
        int LineOf(string text) => Array.FindIndex(BankTests.ReplaceLineEndings("\n").Split('\n'), line => line.Contains(text, StringComparison.Ordinal)) + 1;

        Assert.StartsWith("Running its tests with unittest, test by test, using", TargetFactory.FromFile(tests).Explanation, StringComparison.Ordinal);

        var report = await CheckAsync(tests);
        var failed = report.Findings.Where(finding => finding.RuleId == "test-failed").ToList();

        var notRaised = Assert.Single(failed, finding => finding.Title == "Test test_withdraw_more_than_balance failed: InsufficientFunds not raised");
        Assert.Equal(LineOf("with self.assertRaises(InsufficientFunds)"), notRaised.Line);
        Assert.Equal(Severity.Error, notRaised.Severity);

        var byZero = Assert.Single(failed, finding => finding.Title.StartsWith("Test test_share_between_nobody stopped with ZeroDivisionError", StringComparison.Ordinal));
        Assert.Equal(LineOf("self.assertEqual(self.account.share_of(0), 0)"), byZero.Line);
        Assert.Contains("raised in bank.share_of on line 16 of bank.py", byZero.Explanation, StringComparison.Ordinal);

        // A share of 100 among 4 is 25, not 20: the one subtest that fails is said, with what it was run with.
        var share = Assert.Single(failed, finding => finding.Title.StartsWith("Test test_shares (people=4) failed: 25.0 != 20", StringComparison.Ordinal));
        Assert.Equal(LineOf("self.assertEqual(self.account.share_of(people), share)"), share.Line);

        // Four tests: one passes, and test_shares is one test however many of its subtests fail.
        Assert.Equal(3, failed.Count);
        Assert.Equal("No syntax errors; 3 of 4 tests failed", report.SyntaxSummary);
    }

    [Fact]
    public async Task AUnittestFileWhoseTestsAllPassSaysSo()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        Write(@"passing\bank.py", Bank);
        var tests = Write(@"passing\test_bank.py", """
            import unittest

            from bank import BankAccount


            class BankAccountTests(unittest.TestCase):
                def test_withdraw_takes_away(self):
                    account = BankAccount("Ada", 100)
                    account.withdraw(30)
                    self.assertEqual(account.balance, 70)

                def test_share(self):
                    self.assertEqual(BankAccount("Ada", 100).share_of(4), 25)


            if __name__ == "__main__":
                unittest.main()
            """);

        var report = await CheckAsync(tests);

        Assert.Empty(report.Findings);
        Assert.Equal("No syntax errors, and all 2 of its tests pass", report.SyntaxSummary);
    }

    [Fact]
    public async Task TestsWrittenForPytestAreSaidNotToHaveBeenRun()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var tests = Write(@"pytest-style\test_totals.py", "def total(values):\n    return sum(values)\n\n\ndef test_total():\n    assert total([1, 2, 3]) == 6\n");

        var report = await CheckAsync(tests);

        Assert.Contains(report.Notes, note => note.StartsWith("test_totals.py's tests are written for pytest, which FixFinder does not run", StringComparison.Ordinal));
        Assert.Equal("No syntax errors; its tests are written for pytest, which FixFinder does not run", report.SyntaxSummary);
    }

    [Fact]
    public void TheInterpreterAProjectsVsCodeSettingsNameIsTaken()
    {
        var interpreter = Write(@"vscode\tools\python\python.exe", "");
        Write(@"vscode\.vscode\settings.json", """
            {
                // chosen for this project
                "python.defaultInterpreterPath": "${workspaceFolder}/tools/python/python.exe",
            }
            """);
        var program = Write(@"vscode\src\main.py", "print('hello')\n");

        var found = PythonEnvironment.For(program);

        Assert.NotNull(found);
        Assert.Equal(Path.GetFullPath(interpreter), found!.Interpreter, StringComparer.OrdinalIgnoreCase);
    }
}
