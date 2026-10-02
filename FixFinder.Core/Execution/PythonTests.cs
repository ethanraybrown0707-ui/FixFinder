using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// Running a Python file of tests with its own framework, test by test. A small launcher of FixFinder's runs the file's
/// tests - handing its TestCase classes to unittest, or the file to pytest - and prints what each test did as one line,
/// read back by <see cref="TestReport"/>, so each test that fails is said on its own line. Whether a test passed is its
/// framework's to say, not FixFinder's.
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
    /// Which framework a file's tests are written for: pytest when it imports pytest - pytest runs a TestCase class too, and
    /// such a file uses what only pytest gives; unittest when it imports unittest and has a TestCase class; pytest when it
    /// is named test_*.py or *_test.py and has test functions of its own; None otherwise.
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

        if (PytestImport().IsMatch(text)) return Framework.Pytest;
        if (UnittestImport().IsMatch(text) && TestCaseClass().IsMatch(text)) return Framework.Unittest;

        var name = Path.GetFileNameWithoutExtension(pythonFile);
        var namedAsTests = name.StartsWith("test_", StringComparison.Ordinal) || name.EndsWith("_test", StringComparison.Ordinal);

        return namedAsTests && TopLevelTestFunction().IsMatch(text) ? Framework.Pytest : Framework.None;
    }

    /// <summary>The name the unittest launcher is written under, so a report can tell a failure of FixFinder's own launcher from the tests'.</summary>
    public const string LauncherPrefix = "fixfinder_unittest_launcher";

    /// <summary>The name the pytest launcher is written under, for the same reason.</summary>
    public const string PytestLauncherPrefix = "fixfinder_pytest_launcher";

    /// <summary>What the pytest launcher prints when pytest is not installed for the Python it was run with.</summary>
    public const string PytestMissingMarker = "FIXFINDER-PYTEST-MISSING";

    /// <summary>What begins the pytest launcher's last line, which gives the code pytest itself ended with.</summary>
    public const string PytestEndedMarker = "FIXFINDER-PYTEST-ENDED";

    /// <summary>pytest's code for having failed within itself, while running the tests.</summary>
    public const int PytestInternalError = 3;

    /// <summary>pytest's code for having been given what it cannot run with: an option it does not know, a conftest.py that fails.</summary>
    public const int PytestUsageError = 4;

    /// <summary>Writes the unittest launcher where every Python can read it, and says where.</summary>
    public static string WriteLauncher() => Written(LauncherPrefix, LauncherSource);

    /// <summary>Writes the pytest launcher where every Python can read it, and says where.</summary>
    public static string WritePytestLauncher() => Written(PytestLauncherPrefix, PytestLauncherSource);

    /// <summary>The code pytest itself ended with, when the pytest launcher got as far as saying.</summary>
    public static int? PytestExitCode(IEnumerable<string> lines) =>
        lines.LastOrDefault(line => line.StartsWith(PytestEndedMarker + "\t", StringComparison.Ordinal)) is { } ended &&
        int.TryParse(ended[(PytestEndedMarker.Length + 1)..].Trim(), out var code)
            ? code
            : null;

    /// <summary>
    /// Writes a launcher where every Python can read it - the temp folder, which the Microsoft Store's Python sees and
    /// its LocalAppData it does not - named for its own text, so every run uses the same copy, and says where.
    /// </summary>
    private static string Written(string prefix, string source)
    {
        var name = $"{prefix}_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..12].ToLowerInvariant()}.py";
        var launcher = Path.Combine(Path.GetTempPath(), "FixFinder-tests", name);

        if (!File.Exists(launcher))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(launcher)!);
            File.WriteAllText(launcher, source, new UTF8Encoding(false));
        }

        return launcher;
    }

    /// <summary>
    /// The launcher: python launcher.py module root file. It puts root where Python looks for modules, as running the file
    /// would, imports the module - with unittest.main() made to do nothing, so a file that calls it at its top without the
    /// __name__ test does not run its tests and end the program there - and runs each test, printing one line for it in
    /// the form TestReport reads. When setting up for a class's or a module's tests fails, that is said as what it is.
    /// </summary>
    private const string LauncherSource = """
        # Written by FixFinder: runs one file's unittest tests and prints what each test did, one line each.

        import importlib
        import os
        import sys
        import unittest

        RESULT = "FIXFINDER-TEST"
        FOUND = "FIXFINDER-TESTS-FOUND"


        def clean(text):
            return str(text).replace("\\", "\\\\").replace("\t", "\\t").replace("\r", "\\r").replace("\n", "\\n")


        def part(text):
            # The parts of a place are joined with # and ;, which a folder's name can hold, so those - and % - are written %23, %3B and %25.
            return str(text).replace("%", "%25").replace("#", "%23").replace(";", "%3B")


        def frames_of(error):
            frames = []
            trace = error[2] if error else None
            while trace is not None:
                if "__unittest" not in trace.tb_frame.f_globals:
                    code = trace.tb_frame.f_code
                    module = trace.tb_frame.f_globals.get("__name__", "")
                    frames.append("%s#%s#%s#%d" % (part(module), part(code.co_name), part(code.co_filename), trace.tb_lineno))
                trace = trace.tb_next
            frames.reverse()
            return ";".join(frames)


        def report(status, test, error=None, reason=None):
            case = getattr(test, "test_case", test)
            if hasattr(case, "_testMethodName"):
                method = case._testMethodName
                owner = type(case).__module__ + "." + type(case).__qualname__
            else:
                # What unittest gives when setting up for a class's or a module's tests failed, or cleaning up after them: no
                # test, only what was being done - setUpClass (test_bank.BankTests), tearDownModule (test_bank).
                if status == "FAILED":
                    status = "TEARDOWN-FAILED" if case.id().startswith(("tearDownClass", "tearDownModule")) else "SETUP-FAILED"
                method = ""
                owner = case.id()
            name = method + " " + test._subDescription() if case is not test and hasattr(test, "_subDescription") else (method or owner)
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

            # A file that calls unittest.main() at its top, not under if __name__ == "__main__":, would run its tests as it
            # was imported and then end the program; the launcher runs them itself, so that call does nothing here.
            unittest.main = lambda *arguments, **options: None

            try:
                module = importlib.import_module(module_name)
            except SystemExit as ended:
                raise RuntimeError("%s ended the program with sys.exit(%r) while it was being imported, so its tests could not be run"
                                   % (os.path.basename(program), ended.code)) from None

            suite = unittest.defaultTestLoader.loadTestsFromModule(module)
            print("%s\t%d" % (FOUND, suite.countTestCases()), flush=True)
            suite.run(Reporter())


        main()
        """;

    /// <summary>
    /// The pytest launcher: python launcher.py file. It runs pytest itself on the file - with the project's own pytest
    /// settings and conftest.py, as pytest finds them - and prints one line for each test in the form TestReport reads,
    /// with where a failure was raised. When pytest is not installed it says so, and runs the file as a program instead, as
    /// running it would. A file pytest cannot import is said as Python says an error that stops a program. Nothing is
    /// written into the project: pytest's cache goes in a folder of its own, removed afterwards.
    /// </summary>
    private const string PytestLauncherSource = """
        # Written by FixFinder: runs one file's tests with pytest itself and prints what each test did, one line each.

        import os
        import runpy
        import shutil
        import sys
        import tempfile
        import traceback

        RESULT = "FIXFINDER-TEST"
        FOUND = "FIXFINDER-TESTS-FOUND"
        MISSING = "FIXFINDER-PYTEST-MISSING"
        ENDED = "FIXFINDER-PYTEST-ENDED"

        test_file = os.path.abspath(sys.argv[1])
        test_module = os.path.splitext(os.path.basename(test_file))[0]

        # The file's own folder is where Python looks first for what it imports, as running the file would - not this launcher's.
        sys.path[0] = os.path.dirname(test_file)
        sys.argv = [test_file]

        try:
            import pytest
        except ImportError:
            pytest = None

        if pytest is None:
            # pytest is not part of Python, so it is said to be missing. The file is then run as a program, as running it would
            # be, so what stops it there - its own import of pytest, say - is still found, as an error of its own.
            print(MISSING, flush=True)
            runpy.run_path(test_file, run_name="__main__")
            sys.exit(0)


        def clean(text):
            return str(text).replace("\\", "\\\\").replace("\t", "\\t").replace("\r", "\\r").replace("\n", "\\n")


        def part(text):
            # The parts of a place are joined with # and ;, which a folder's name can hold, so those - and % - are written %23, %3B and %25.
            return str(text).replace("%", "%25").replace("#", "%23").replace(";", "%3B")


        def frames_of(excinfo):
            # Where a failure was raised, innermost first: without pytest's own frames, or those pytest hides from its reports.
            frames = []
            trace = excinfo.tb if excinfo is not None else None
            while trace is not None:
                frame = trace.tb_frame
                module = frame.f_globals.get("__name__", "")
                hidden = frame.f_locals.get("__tracebackhide__", frame.f_globals.get("__tracebackhide__", False))
                if not hidden and module.split(".")[0] not in ("pytest", "_pytest", "pluggy"):
                    code = frame.f_code
                    frames.append("%s#%s#%s#%d" % (part(module), part(code.co_name), part(code.co_filename), trace.tb_lineno))
                trace = trace.tb_next
            frames.reverse()
            return ";".join(frames)


        def reason_of(result):
            # Why pytest skipped something: the last of the place and reason it keeps, without its "Skipped: ".
            longrepr = result.longrepr
            reason = longrepr[2] if isinstance(longrepr, tuple) and len(longrepr) == 3 else str(longrepr or "")
            return reason[len("Skipped: "):] if reason.startswith("Skipped: ") else reason


        class Reporter:
            def __init__(self):
                self.config = None
                self.collection_failed = False

            def emit(self, fields):
                line = "\t".join([fields[0]] + [clean(field) for field in fields[1:]])
                # pytest writes a dot for each test without ending its line, so a line of FixFinder's is begun on a line of
                # its own, through pytest's own writer, which knows how far along its line is.
                try:
                    writer = self.config.get_terminal_writer()
                except Exception:
                    writer = None
                if writer is None:
                    print(line, flush=True)
                    return
                if writer.width_of_current_line:
                    writer.line()
                writer.line(line)
                writer.flush()

            def report(self, status, item, excinfo=None, reason=""):
                module = getattr(item, "module", None)
                owner_class = getattr(item, "cls", None)
                owner = (module.__name__ if module is not None else test_module) + ("." + owner_class.__qualname__ if owner_class is not None else "")
                function = getattr(item, "originalname", None) or item.name
                exception = excinfo.type.__name__ if excinfo is not None else ""
                message = str(excinfo.value) if excinfo is not None else reason
                self.emit([RESULT, status, item.name, owner, function, exception, message, frames_of(excinfo)])

            def pytest_configure(self, config):
                self.config = config

            def pytest_collectreport(self, report):
                # A file pytest was told to skip as a whole - by pytest.importorskip at its top, say - has none of its tests run.
                if report.skipped:
                    self.emit([RESULT, "FILE-SKIPPED", "", test_module, "", "", reason_of(report), ""])

            def pytest_exception_interact(self, node, call, report):
                if report.when != "collect" or call.excinfo is None:
                    return
                # The file could not be imported, so none of its tests can run: said as Python says an error that stops a
                # program. pytest puts a failed import, or a syntax error, inside an error of its own; the first is the one said.
                self.collection_failed = True
                error = call.excinfo.value
                cause = error.__cause__ if isinstance(error, pytest.Collector.CollectError) and error.__cause__ is not None else error
                traceback.print_exception(type(cause), cause, cause.__traceback__)
                sys.stderr.flush()

            def pytest_collection_finish(self, session):
                if not self.collection_failed:
                    self.emit([FOUND, str(len(session.items))])

            @pytest.hookimpl(hookwrapper=True)
            def pytest_runtest_makereport(self, item, call):
                outcome = yield
                result = outcome.get_result()
                marked_to_fail = hasattr(result, "wasxfail")
                if result.when == "setup":
                    if result.failed:
                        self.report("SETUP-FAILED", item, call.excinfo)
                    elif result.skipped:
                        self.report("SKIPPED", item, reason=reason_of(result))
                elif result.when == "call":
                    if result.passed or (result.skipped and marked_to_fail):
                        # Passed - or failed as an xfail mark says it will, which pytest does not count as failing.
                        self.report("SUCCESSFUL", item)
                    elif result.skipped:
                        self.report("SKIPPED", item, reason=reason_of(result))
                    elif result.failed:
                        # A test marked xfail(strict=True) that passed fails with no error of its own; pytest's words say why.
                        self.report("FAILED", item, call.excinfo, reason="" if call.excinfo is not None else str(result.longrepr))
                elif result.when == "teardown" and result.failed:
                    self.report("TEARDOWN-FAILED", item, call.excinfo)


        def main():
            reporter = Reporter()
            # Nothing is written into the project: pytest's cache goes in a folder of its own, and no compiled files beside the code.
            sys.dont_write_bytecode = True
            cache = tempfile.mkdtemp(prefix="fixfinder-pytest-")
            try:
                ended = pytest.main([test_file, "-q", "--tb=no", "-rN", "--color=no", "-o", "cache_dir=" + cache], plugins=[reporter])
            finally:
                shutil.rmtree(cache, ignore_errors=True)
            reporter.emit([ENDED, str(int(ended))])
            sys.exit(1 if reporter.collection_failed else 0)


        main()
        """;
}
