using System.Diagnostics;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Logic;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A copy a change is tried in is used again for the next change, and Python takes the bytecode it saved there as a file's
/// own while the file is the same size, with the same time to the second: a change the same length as the last, written in
/// the same second, ran the last one's code - on the CI runner, a test was said to catch a change it passes with. The copy
/// forgets what Python saved before each run, so each change runs as it is written.
/// </summary>
public class SavedBytecodeTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Average = "def average(marks):\n    total = 0\n    for mark in marks:\n        total += mark\n    return total / len(marks)\n";

    private const string AverageTest = """
        import unittest

        from grades import average


        class AverageTests(unittest.TestCase):
            def test_the_average_of_two_marks(self):
                self.assertEqual(average([2, 4]), 3)
        """;

    /// <summary>Whether Python, run in the folder with the arguments, ended without an error: for unittest, every test passed.</summary>
    private static bool EndsWithoutAnError(string python, string folder, params string[] arguments)
    {
        var start = new ProcessStartInfo(python) { WorkingDirectory = folder, RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var run = Process.Start(start)!;
        run.StandardOutput.ReadToEnd();
        run.StandardError.ReadToEnd();
        run.WaitForExit();
        return run.ExitCode == 0;
    }

    [Fact]
    public void ACopyForgetsTheBytecodePythonSavedSoAChangeOfTheSameLengthRunsAsWritten()
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return;

        var program = Path.Combine(_temp.Path, "grades");
        Directory.CreateDirectory(program);
        File.WriteAllText(Path.Combine(program, "grades.py"), Average);
        File.WriteAllText(Path.Combine(program, "test_grades.py"), AverageTest.ReplaceLineEndings("\n"));

        using var workspace = new LogicRepair.Workspace(program, Path.Combine(program, "test_grades.py"));
        Assert.True(workspace.Create());
        var copy = Path.Combine(workspace.Folder, "grades.py");

        // The change tried first - total += marks, which every test fails with - and the next, the same length: / made //,
        // which the test passes with, as 6 // 2 is 3. Both are written early in one second, so the time Python saved is the same.
        var failsTheTest = Average.Replace("total += mark\n", "total += marks\n", StringComparison.Ordinal);
        var passesTheTest = Average.Replace("total / len", "total // len", StringComparison.Ordinal);
        Assert.Equal(failsTheTest.Length, passesTheTest.Length);

        static long SecondWritten(string file) => File.GetLastWriteTimeUtc(file).Ticks / TimeSpan.TicksPerSecond;

        while (DateTime.Now.Millisecond > 300) Thread.Sleep(10);
        File.WriteAllText(copy, failsTheTest);
        var firstWritten = SecondWritten(copy);
        Assert.True(EndsWithoutAnError(python, workspace.Folder, "-c", "import py_compile; py_compile.compile('grades.py')"));
        File.WriteAllText(copy, passesTheTest);

        // Only both changes written in one second show it; a slower machine that took longer says nothing either way.
        if (SecondWritten(copy) != firstWritten)
        {
            output.WriteLine("The two changes were written in different seconds, so the test shows nothing either way.");
            return;
        }

        output.WriteLine("Both changes were written in the same second.");

        // Python runs the bytecode saved for the first change, though the file holds the second.
        Assert.False(EndsWithoutAnError(python, workspace.Folder, "-m", "unittest", "-q"));

        workspace.ForgetSavedBytecode();

        Assert.False(Directory.Exists(Path.Combine(workspace.Folder, "__pycache__")));
        Assert.True(File.Exists(copy));
        Assert.True(EndsWithoutAnError(python, workspace.Folder, "-m", "unittest", "-q"));
    }
}
