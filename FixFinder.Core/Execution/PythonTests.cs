using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// Running a Python file of unittest tests with unittest itself, test by test. A small launcher of FixFinder's imports the
/// file as running it would, hands its TestCase classes to unittest, and prints what each test did as one line, read back
/// by <see cref="TestReport"/> - so a test file is checked whether or not it calls unittest.main(), and each test that
/// fails is said on its own line. Whether a test passed is unittest's to say, not FixFinder's.
/// </summary>
public static partial class PythonTests
{
    public enum Framework { None, Unittest, Pytest }

    [GeneratedRegex(@"(?m)^[ \t]*(?:import[ \t]+unittest\b|from[ \t]+unittest\b)")]
    private static partial Regex UnittestImport();

    [GeneratedRegex(@"(?m)^[ \t]*class[ \t]+\w+[ \t]*\([^)]*\bTestCase\b")]
    private static partial Regex TestCaseClass();

    [GeneratedRegex(@"(?m)^[ \t]*(?:import[ \t]+pytest\b|from[ \t]+pytest\b)")]
    private static partial Regex PytestImport();

    /// <summary>A test function at the top of a module, as pytest finds one: def test_total():.</summary>
    [GeneratedRegex(@"(?m)^def[ \t]+test\w*[ \t]*\(")]
    private static partial Regex TopLevelTestFunction();

    /// <summary>
    /// Which framework a file's tests are written for: unittest when it imports unittest and has a TestCase class;
    /// pytest when it imports pytest, or is named test_*.py or *_test.py and has test functions of its own; None otherwise.
    /// </summary>
    public static Framework FrameworkOf(string pythonFile)
    {
        string text;
        try
        {
            text = File.ReadAllText(pythonFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Framework.None;
        }

        if (UnittestImport().IsMatch(text) && TestCaseClass().IsMatch(text)) return Framework.Unittest;

        var name = Path.GetFileNameWithoutExtension(pythonFile);
        var namedAsTests = name.StartsWith("test_", StringComparison.Ordinal) || name.EndsWith("_test", StringComparison.Ordinal);

        return PytestImport().IsMatch(text) || (namedAsTests && TopLevelTestFunction().IsMatch(text)) ? Framework.Pytest : Framework.None;
    }

    /// <summary>The name the launcher is written under, so a report can tell a failure of FixFinder's own launcher from the tests'.</summary>
    public const string LauncherPrefix = "fixfinder_unittest_launcher";

    /// <summary>
    /// Writes the launcher where every Python can read it - the temp folder, which the Microsoft Store's Python sees and
    /// its LocalAppData it does not - named for its own text, so every run uses the same copy, and says where.
    /// </summary>
    public static string WriteLauncher()
    {
        var name = $"{LauncherPrefix}_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(LauncherSource)))[..12].ToLowerInvariant()}.py";
        var launcher = Path.Combine(Path.GetTempPath(), "FixFinder-tests", name);

        if (!File.Exists(launcher))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(launcher)!);
            File.WriteAllText(launcher, LauncherSource, new UTF8Encoding(false));
        }

        return launcher;
    }

    /// <summary>
    /// The launcher: python launcher.py module root file. It puts root where Python looks for modules, as running the file
    /// would, imports the module - a module that calls unittest.main() at its top without the __name__ guard ends there,
    /// and its tests are still run - and runs each test, printing one line for it in the form TestReport reads.
    /// </summary>
    private const string LauncherSource = """
        # Written by FixFinder: runs one file's unittest tests and prints what each test did, one line each.

        import importlib
        import sys
        import unittest

        RESULT = "FIXFINDER-TEST"
        FOUND = "FIXFINDER-TESTS-FOUND"


        def clean(text):
            return str(text).replace("\\", "\\\\").replace("\t", "\\t").replace("\r", "\\r").replace("\n", "\\n")


        def frames_of(error):
            frames = []
            trace = error[2] if error else None
            while trace is not None:
                if "__unittest" not in trace.tb_frame.f_globals:
                    code = trace.tb_frame.f_code
                    module = trace.tb_frame.f_globals.get("__name__", "")
                    frames.append("%s#%s#%s#%d" % (module, code.co_name, code.co_filename, trace.tb_lineno))
                trace = trace.tb_next
            frames.reverse()
            return ";".join(frames)


        def report(status, test, error=None, reason=None):
            case = getattr(test, "test_case", test)
            method = getattr(case, "_testMethodName", case.id().split(".")[-1])
            owner = type(case).__module__ + "." + type(case).__qualname__ if hasattr(case, "_testMethodName") else case.id()
            name = method + " " + test._subDescription() if case is not test and hasattr(test, "_subDescription") else method
            exception = error[0].__name__ if error else ""
            message = str(error[1]) if error else (reason or "")
            fields = [RESULT, status, name, owner, method, exception, message, frames_of(error)]
            print("\t".join(clean(field) if index > 0 else field for index, field in enumerate(fields)), flush=True)


        class Reporter(unittest.TestResult):
            def addSuccess(self, test):
                super().addSuccess(test)
                report("SUCCESSFUL", test)

            def addFailure(self, test, error):
                super().addFailure(test, error)
                report("FAILED", test, error)

            def addError(self, test, error):
                super().addError(test, error)
                report("FAILED", test, error)

            def addSkip(self, test, reason):
                super().addSkip(test, reason)
                report("SKIPPED", test, reason=reason)

            def addExpectedFailure(self, test, error):
                super().addExpectedFailure(test, error)
                report("SUCCESSFUL", test)

            def addUnexpectedSuccess(self, test):
                super().addUnexpectedSuccess(test)
                report("FAILED", test, reason="it is marked expectedFailure, and it passed")

            def addSubTest(self, test, subtest, error):
                super().addSubTest(test, subtest, error)
                if error is not None:
                    report("FAILED", subtest, error)


        def main():
            module_name, root, program = sys.argv[1], sys.argv[2], sys.argv[3]
            sys.path.insert(0, root)
            sys.argv = [program]
            try:
                module = importlib.import_module(module_name)
            except SystemExit:
                module = sys.modules[module_name]
            suite = unittest.defaultTestLoader.loadTestsFromModule(module)
            print("%s\t%d" % (FOUND, suite.countTestCases()), flush=True)
            suite.run(Reporter())


        main()
        """;
}
