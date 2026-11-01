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
    public enum Framework { None, Unittest, Pytest, Doctest }

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
    /// is named test_*.py or *_test.py and has test functions of its own; doctest when the examples in its docstrings are
    /// all it runs; None otherwise.
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

        if (namedAsTests && TopLevelTestFunction().IsMatch(text)) return Framework.Pytest;

        return OnlyRunsItsDoctests(text) ? Framework.Doctest : Framework.None;
    }

    /// <summary>An example in a docstring, as doctest finds one: >>> and a space, or >>> ending the line.</summary>
    [GeneratedRegex(@"^\s*>>>(?: |$)")]
    private static partial Regex DoctestExample();

    [GeneratedRegex(@"^if\s+__name__\s*==\s*(?:'__main__'|""__main__"")\s*:")]
    private static partial Regex MainBlockHead();

    /// <summary>What a module whose doctests are all it runs has at its top: a definition, an import, a decorator, or a name given a value.</summary>
    [GeneratedRegex(@"^(?:(?:async\s+)?def|class|import|from)\b|^@|^[A-Za-z_][\w.]*(?:\s*,\s*[A-Za-z_][\w.]*)*\s*(?::[^=]+)?=(?!=)")]
    private static partial Regex Definition();

    /// <summary>A line of a __main__ block that runs the doctests and nothing else: import doctest, doctest.testmod(), testmod(verbose=True), pass.</summary>
    [GeneratedRegex(@"^\s*(?:import\s+doctest\b.*|from\s+doctest\s+import\b.*|(?:doctest\.)?testmod\s*\(.*|pass)$")]
    private static partial Regex RunsTheDoctests();

    /// <summary>
    /// Whether a file's doctests - the examples in its docstrings - are all it runs: it has examples, and at its top only
    /// defines functions, classes and names, maybe with a __main__ block that runs doctest.testmod() and nothing else. Such a
    /// file run as a program does nothing but run its doctests, or nothing at all, so its examples are its tests. A program
    /// that also does something of its own - reads input, prints, calls main() - is run as the program it is.
    /// </summary>
    private static bool OnlyRunsItsDoctests(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var masked = LocalFixes.CodeText.MaskAll(lines, LocalFixes.Syntax.Python);

        // An example is in a docstring, so it is text: the line masks to nothing.
        if (!Enumerable.Range(0, lines.Length).Any(index => DoctestExample().IsMatch(lines[index]) && masked[index].Trim().Length == 0)) return false;

        var openBrackets = 0;
        var inMainBlock = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = masked[index];
            var startsStatement = openBrackets == 0;
            openBrackets = Math.Max(0, openBrackets + line.Count(c => c is '(' or '[' or '{') - line.Count(c => c is ')' or ']' or '}'));
            if (!startsStatement || line.Trim().Length == 0) continue;

            if (char.IsWhiteSpace(line[0]))
            {
                if (inMainBlock && !RunsTheDoctests().IsMatch(line.TrimEnd())) return false;
                continue;
            }

            // "__main__" is text, which masking leaves out, so the block's head is read as it is written.
            inMainBlock = MainBlockHead().IsMatch(lines[index].TrimEnd());
            if (inMainBlock) continue;

            if (!Definition().IsMatch(line) || line.Contains("input(", StringComparison.Ordinal)) return false;
        }

        return true;
    }

    /// <summary>The name the unittest launcher is written under, so a report can tell a failure of FixFinder's own launcher from the tests'.</summary>
    public const string LauncherPrefix = "fixfinder_unittest_launcher";

    /// <summary>The name the pytest launcher is written under, for the same reason.</summary>
    public const string PytestLauncherPrefix = "fixfinder_pytest_launcher";

    /// <summary>The name the doctest launcher is written under, for the same reason.</summary>
    public const string DoctestLauncherPrefix = "fixfinder_doctest_launcher";

    /// <summary>Writes the doctest launcher where every Python can read it, and says where.</summary>
    public static string WriteDoctestLauncher() => Written(DoctestLauncherPrefix, DoctestLauncherSource);

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
    /// The pytest launcher: python launcher.py file [project-folder]. It runs pytest itself on the file - with the project's
    /// own pytest settings and conftest.py, as pytest finds them, from the project's folder when that is given - and prints
    /// one line for each test in the form TestReport reads,
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

        # The project's folder, when FixFinder found one pytest would not from the file alone: the one with the conftest.py above it.
        project_folder = sys.argv[2] if len(sys.argv) > 2 else None

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
                options = [test_file, "-q", "--tb=no", "-rN", "--color=no", "-o", "cache_dir=" + cache]
                if project_folder:
                    options.append("--rootdir=" + project_folder)
                ended = pytest.main(options, plugins=[reporter])
            finally:
                shutil.rmtree(cache, ignore_errors=True)
            reporter.emit([ENDED, str(int(ended))])
            sys.exit(1 if reporter.collection_failed else 0)


        main()
        """;

    /// <summary>
    /// The doctest launcher: python launcher.py file. It imports the file by its own name from its own folder, as python -m
    /// doctest file.py does - so its __main__ block does not run - and runs the examples in each of its docstrings with
    /// doctest itself, with the options the file's own doctest.testmod(optionflags=...) gives, printing one line for each
    /// docstring in the form TestReport reads: the first example that did not give what it shows, on its own line of the file.
    /// </summary>
    private const string DoctestLauncherSource = """
        # Written by FixFinder: runs the examples in one file's docstrings with doctest and prints what each docstring's did, one line each.

        import ast
        import doctest
        import importlib.util
        import os
        import sys
        import tokenize

        RESULT = "FIXFINDER-TEST"
        FOUND = "FIXFINDER-TESTS-FOUND"


        def clean(text):
            return str(text).replace("\\", "\\\\").replace("\t", "\\t").replace("\r", "\\r").replace("\n", "\\n")


        def part(text):
            # The parts of a place are joined with # and ;, which a folder's name can hold, so those - and % - are written %23, %3B and %25.
            return str(text).replace("%", "%25").replace("#", "%23").replace(";", "%3B")


        def option_flags(source):
            # The options the file's own doctest.testmod(optionflags=...) gives - ELLIPSIS, NORMALIZE_WHITESPACE - so its
            # examples are judged here as they are when it runs them itself.
            flags = 0
            names = dict(doctest.OPTIONFLAGS_BY_NAME, doctest=doctest)
            for node in ast.walk(ast.parse(source)):
                called = getattr(node, "func", None)
                if isinstance(node, ast.Call) and getattr(called, "attr", getattr(called, "id", None)) == "testmod":
                    for keyword in node.keywords:
                        if keyword.arg == "optionflags":
                            try:
                                flags |= eval(compile(ast.Expression(keyword.value), "<optionflags>", "eval"), {"__builtins__": {}}, names)
                            except Exception:
                                pass
            return flags


        class Reporter(doctest.DocTestRunner):
            # Keeps the first example of a docstring that did not give what it shows, rather than printing doctest's report.
            def __init__(self, optionflags):
                super().__init__(verbose=False, optionflags=optionflags)
                self.first_failure = None

            def report_start(self, out, test, example):
                pass

            def report_success(self, out, test, example, got):
                pass

            def report_failure(self, out, test, example, got):
                if self.first_failure is None:
                    self.first_failure = (example, got, None)

            def report_unexpected_exception(self, out, test, example, exc_info):
                if self.first_failure is None:
                    self.first_failure = (example, None, exc_info)


        def line_of(test, example):
            # doctest's own numbering: the docstring's first line in the file, counted from 0, and the example's in the docstring.
            return (test.lineno or 0) + example.lineno + 1


        def frames_of(module_name, name, test_file, test, example, exc_info):
            # Where it went wrong, innermost first: the example's own line, and for an exception the lines it was raised
            # through, with doctest's own left out.
            example_place = "%s#%s#%s#%d" % (part(module_name), part(name), part(test_file), line_of(test, example))
            if exc_info is None:
                return example_place
            frames = []
            trace = exc_info[2]
            while trace is not None:
                frame = trace.tb_frame
                code = frame.f_code
                if code.co_filename.startswith("<doctest "):
                    frames.append(example_place)
                elif frame.f_globals.get("__name__") != "doctest":
                    frames.append("%s#%s#%s#%d" % (part(frame.f_globals.get("__name__", "")), part(code.co_name), part(code.co_filename), trace.tb_lineno))
                trace = trace.tb_next
            frames.reverse()
            return ";".join(frames)


        def main():
            test_file = os.path.abspath(sys.argv[1])
            module_name = os.path.splitext(os.path.basename(test_file))[0]

            # The file's own folder is where Python looks first for what it imports, as python -m doctest has it.
            sys.path[0] = os.path.dirname(test_file)
            sys.argv = [test_file]

            spec = importlib.util.spec_from_file_location(module_name, test_file)
            module = importlib.util.module_from_spec(spec)
            sys.modules[module_name] = module
            try:
                spec.loader.exec_module(module)
            except SystemExit as ended:
                raise RuntimeError("%s ended the program with sys.exit(%r) while it was being imported, so its doctests could not be run"
                                   % (os.path.basename(test_file), ended.code)) from None

            with tokenize.open(test_file) as reading:
                flags = option_flags(reading.read())

            tests = sorted((test for test in doctest.DocTestFinder().find(module, module_name) if test.examples), key=lambda test: test.lineno or 0)
            print("%s\t%d" % (FOUND, len(tests)), flush=True)

            runner = Reporter(flags)
            for test in tests:
                runner.first_failure = None
                runner.run(test, out=lambda text: None)
                # Named as the code is: grade, or Stack.push - the module's own docstring by the module's name.
                name = test.name[len(module_name) + 1:] if test.name.startswith(module_name + ".") else test.name
                method = name.split(".")[-1]
                if runner.first_failure is None:
                    fields = [RESULT, "SUCCESSFUL", name, module_name, method, "", "", ""]
                else:
                    example, got, exc_info = runner.first_failure
                    if exc_info is None:
                        message = "%s: expected %s, got %s" % (example.source.strip(), example.want.strip() or "nothing", got.strip() or "nothing")
                        exception = ""
                    else:
                        message = str(exc_info[1])
                        exception = exc_info[0].__name__
                    fields = [RESULT, "FAILED", name, module_name, method, exception, message,
                              frames_of(module_name, name, test_file, test, example, exc_info)]
                print("\t".join(clean(field) if index > 0 else field for index, field in enumerate(fields)), flush=True)


        main()
        """;
}
