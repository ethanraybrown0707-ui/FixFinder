using System.Text.RegularExpressions;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Execution.Libraries;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Diffing;
using FixFinder.Core.Analysis.Dynamic;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Logic;
using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;
using FixFinder.Core.Sources;

namespace FixFinder.Core.Checking;

public enum CheckLane
{
    Syntax,
    Logic,
}

/// <summary>Everything one check found, and how the program ran.</summary>
public sealed record CheckReport(
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<string> Notes,
    string SyntaxSummary,
    string LogicSummary,
    SessionOutcome? Run);

/// <summary>Checks a program's syntax and its logic at the same time, and reports every mistake found with how sure it is and how
/// to fix it.</summary>
public sealed partial class ProgramChecker(FixFinderHttpClient http, FixSourceRegistry sources)
{
    private const int MostErrorsFixed = 12;
    private const int MostWarningsFixed = 20;
    private const int MostFixesCompared = 5;

    /// <summary>How many fixes are actually run to see whether they work; each one runs the program again.</summary>
    private const int MostFixesVerified = 3;

    private readonly object _gate = new();
    private readonly List<Finding> _findings = [];
    private readonly List<string> _notes = [];
    private readonly Dictionary<string, Task> _comparing = [];
    private readonly Dictionary<string, IReadOnlyList<string>> _fixChanges = [];
    private readonly Dictionary<string, Task> _verifying = [];
    private readonly Dictionary<string, Verification> _verified = [];

    /// <summary>Set for a check of the code alone, which compiles and runs nothing.</summary>
    private bool _codeOnly;

    /// <summary>How many functions the analyses took unchanged from the last check, and how many they looked at afresh.</summary>
    private int _reusedFunctions;
    private int _analysedFunctions;
    private LaunchPlan? _launch;
    private CancellationToken _cancellation;

    public CodeLanguage Language { get; init; } = CodeLanguage.Any;

    public ExpectedBehaviour? Expected { get; init; }

    /// <summary>
    /// What earlier checks in this session found in each function, so a check after an edit analyses only what the edit
    /// could have changed. Null analyses everything every time.
    /// </summary>
    public AnalysisCache? Cache { get; init; }

    public event Action<IReadOnlyList<Finding>>? FindingsChanged;
    public event Action<CheckLane, string>? Progress;
    public event Action<CheckLane, string>? LaneFinished;
    public event Action<CapturedLine>? LineCaptured;
    public event Action<string>? Log;

    public async Task<CheckReport> CheckAsync(LaunchPlan launch, CancellationToken cancellationToken = default)
    {
        if (!launch.Ok || launch.Spec is null) return new CheckReport([], [launch.Problem ?? "That program cannot be run."], "Not checked", "Not checked", null);

        var files = launch.ChosenFile is { } chosen ? ProgramFiles.Of(chosen) : [];
        var builds = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _launch = launch;
        _cancellation = cancellationToken;
        NoteWhatANotebookIsCheckedAs();

        var syntax = Task.Run(() => SyntaxLaneAsync(launch, files, builds, cancellationToken), CancellationToken.None);
        var logic = Task.Run(() => LogicLaneAsync(launch, files, builds.Task, cancellationToken), CancellationToken.None);

        try
        {
            await Task.WhenAll(syntax, logic);
        }
        finally
        {
            builds.TrySetResult(false);
        }

        var (syntaxSummary, run) = syntax.Result;

        // Both of these run in the background while the report fills in; the report waits for them so that what it
        // finally says about a fix is what was actually established, not what had been established so far.
        Task[] finishing;
        lock (_gate) finishing = [.. _comparing.Values, .. _verifying.Values];
        await Task.WhenAll(finishing);

        lock (_gate)
        {
            var logicSummary = logic.Result;

            // What was found in the code is counted as it is reported: two findings of one mistake are one, and one in lines
            // FixFinder put in itself is none. What was said of the output stays said.
            if (logicSummary.EndsWith("in the code", StringComparison.Ordinal))
            {
                var fromCode = FoundInTheCode();
                var printedAsExpected = logicSummary.StartsWith("Printed what you expected", StringComparison.Ordinal);

                logicSummary = (printedAsExpected, fromCode) switch
                {
                    (true, 0) => "It printed what you expected",
                    (true, _) => $"Printed what you expected; {Count(fromCode, "possible mistake")} in the code",
                    (false, 0) => "No logic mistakes found in the code",
                    _ => $"{Count(fromCode, "possible mistake")} in the code",
                };
            }

            if (_reusedFunctions > 0)
                logicSummary += $" - {_reusedFunctions} of {_reusedFunctions + _analysedFunctions} functions unchanged since the last check";

            return new CheckReport(Sorted(_findings), [.. _notes], syntaxSummary, logicSummary, AsPrinted(run));
        }
    }

    /// <summary>How many of the findings reported came from reading the code - its patterns and its analyses.</summary>
    private int FoundInTheCode() =>
        _findings.Count(f => f.RuleId.StartsWith("logic-", StringComparison.Ordinal) || f.RuleId.StartsWith("analysis-", StringComparison.Ordinal));

    /// <summary>
    /// A line the program printed, as it is shown: for a notebook's code, with each place in the script its cells were run
    /// as put as a cell and a line, as Jupyter puts them in a traceback. What is read for errors is the line as printed.
    /// </summary>
    private CapturedLine AsPrinted(CapturedLine line) =>
        _launch?.ChosenFile is { } chosen && NotebookScript.Of(chosen) is { } notebook ? line with { Text = notebook.InCellTerms(line.Text) } : line;

    private SessionOutcome? AsPrinted(SessionOutcome? outcome) =>
        outcome?.Run is { } run && _launch?.ChosenFile is { } chosen && NotebookScript.Of(chosen) is not null
            ? outcome with { Run = run with { Lines = [.. run.Lines.Select(AsPrinted)] } }
            : outcome;

    private async Task<(string Summary, SessionOutcome? Run)> SyntaxLaneAsync(
        LaunchPlan launch, IReadOnlyList<string> files, TaskCompletionSource<bool> builds, CancellationToken cancellationToken)
    {
        try
        {
            var chosen = launch.ChosenFile ?? launch.Spec!.ExecutablePath;

            Progress?.Invoke(CheckLane.Syntax, launch.NeedsCompiling ? "Compiling..." : "Reading the code...");

            var report = files.Count > 0
                ? await CompilerDiagnostics.CollectAsync(launch, files, Language, LineCaptured, Log, cancellationToken)
                : CompilerReport.NotChecked();

            if (report.Problem is { } problem) Note(problem);

            var root = launch.SourceFolder ?? Path.GetDirectoryName(chosen);

            // What javac says of FixFinder's own JUnit launcher is FixFinder's to answer for, never a mistake in the code.
            var fromLauncher = report.Errors.Concat(report.Warnings).Where(FromTestLauncher).ToList();
            if (fromLauncher.Count > 0)
                Note($"FixFinder's launcher for JUnit could not be built with the JUnit here, so the tests were not run: {fromLauncher[0].Summary}.");

            // Errors that only say a library is not here are said once, as that, rather than as mistakes in the code.
            var sorted = LibraryErrors.Sort(report.Errors.Where(error => !FromTestLauncher(error)).ToList(), chosen);
            if (sorted.Note is { } missingLibrary) Note(missingLibrary);
            var codeErrors = sorted.CodeErrors;

            if (report.Errors.Count > 0)
            {
                builds.TrySetResult(false);
                if (codeErrors.Count > 0) Progress?.Invoke(CheckLane.Syntax, $"Found {Count(codeErrors.Count, "error")} - working out fixes...");

                await ForEachAsync(codeErrors.Take(MostErrorsFixed), async error =>
                {
                    var fix = await FixAsync(error, report.Errors, report.Output, root, fromBuild: true, cancellationToken);
                    Add(FindingFactory.FromError(error, FindingKind.Syntax, Severity.Error, Confidence.Certain, chosen, fix));
                }, cancellationToken);

                foreach (var error in codeErrors.Skip(MostErrorsFixed))
                    Add(FindingFactory.FromError(error, FindingKind.Syntax, Severity.Error, Confidence.Certain, chosen));
            }

            await ForEachAsync(report.Warnings.Where(warning => !FromTestLauncher(warning)).Take(MostWarningsFixed), async warning =>
            {
                var fix = await FixAsync(warning, [], report.Output, root, fromBuild: false, cancellationToken);
                Add(FindingFactory.FromWarning(warning, WarningRatings.For(warning), chosen, fix));
            }, cancellationToken);

            if (report.Errors.Count > 0 || report.Build is { Outcome: not RunOutcome.ExitedClean } || report.Failed && report.Checked)
            {
                builds.TrySetResult(false);

                return (codeErrors.Count > 0 ? $"{Count(codeErrors.Count, "error")} {(codeErrors.Count == 1 ? "stops" : "stop")} it building"
                    : sorted.FromLibraries > 0 ? "It needs a library that is not on this computer, so it was not built"
                    : "It did not build", null);
            }

            builds.TrySetResult(true);

            var outcome = await RunAsync(launch, report, cancellationToken);
            return (Summarise(outcome, report, chosen), outcome);
        }
        catch (OperationCanceledException)
        {
            builds.TrySetResult(false);
            throw;
        }
        finally
        {
            LaneFinished?.Invoke(CheckLane.Syntax, "done");
        }
    }

    private async Task<SessionOutcome> RunAsync(LaunchPlan launch, CompilerReport report, CancellationToken cancellationToken)
    {
        Progress?.Invoke(CheckLane.Syntax, $"Running {Path.GetFileName(launch.ChosenFile ?? launch.Spec!.ExecutablePath)}...");

        var session = new FixFinderSession(http, sources) { Language = Language, SearchOnline = false, CheckLogic = false };

        void Relay(string message) => Log?.Invoke(message);
        void Forward(CapturedLine line) => LineCaptured?.Invoke(AsPrinted(line));
        void Status(string message) => Progress?.Invoke(CheckLane.Syntax, message);

        session.Log += Relay;
        session.LineCaptured += Forward;
        session.Progress += Status;

        try
        {
            var outcome = report.Build is { } build
                ? await session.RunAsync(launch.Spec!, null, cancellationToken, launch.SourceFolder, build.Lines, session.SanitizerFor(launch))
                : await session.RunAsync(launch, null, cancellationToken);

            RecordRun(outcome, launch);
            RecordTests(outcome, launch);
            return outcome;
        }
        finally
        {
            session.Log -= Relay;
            session.LineCaptured -= Forward;
            session.Progress -= Status;
        }
    }

    /// <summary>
    /// What to say of a Java program that stopped for want of a class - or of a database driver - when libraries its build
    /// names are not on this computer: it ran without them, so what it could not find may be theirs rather than missing
    /// from the code, and which cannot be told until they are here. Null for any other failure, or when nothing is missing.
    /// </summary>
    private static string? RanWithoutItsLibraries(ParsedError error, string chosen)
    {
        if (!chosen.EndsWith(".java", StringComparison.OrdinalIgnoreCase)) return null;

        var notFound = ChainOf(error).FirstOrDefault(link =>
            link.ExceptionType is "java.lang.ClassNotFoundException" or "java.lang.NoClassDefFoundError" ||
            link.ExceptionType == "java.sql.SQLException" && (link.Message ?? "").StartsWith("No suitable driver", StringComparison.Ordinal));

        if (notFound is null || JavaLibraries.For(chosen) is not { Missing.Count: > 0, DeclaredIn: { } declared } libraries) return null;

        var missing = libraries.Missing.Select(library => library.Name).Distinct(StringComparer.Ordinal).ToList();
        var one = missing.Count == 1;
        var named = missing.Count <= 6 ? string.Join(", ", missing) : $"{string.Join(", ", missing.Take(6))} and {missing.Count - 6} more";
        var theLibraries = one ? "that library" : "one of those libraries";

        var stopped = notFound.ExceptionType == "java.sql.SQLException"
            ? $"It stopped because no database driver it has could take the address it gave - {notFound.Message}. The driver may be in {theLibraries}"
            : $"It stopped because a class it needed could not be found - {notFound.Message}. That class may be in {theLibraries}";

        return $"{named} {(one ? "is" : "are")} named in {declared} but not on this computer, so the program ran without {(one ? "it" : "them")}. " +
               $"{stopped}, and whether it is cannot be told until {(one ? "it is" : "they are")} on this computer.";
    }

    private static IEnumerable<ParsedError> ChainOf(ParsedError error) => new[] { error }.Concat(error.Causes.SelectMany(ChainOf));

    [GeneratedRegex(@"\bvoid\s+main\s*\(")]
    private static partial Regex DeclaresMain();

    [GeneratedRegex(@"(?m)^\s*import\s+(?:static\s+)?(?<name>javafx\.|javax\.swing\.|java\.net\.ServerSocket\b|java\.net\.\*|com\.sun\.net\.httpserver\.|org\.springframework\.boot\.)")]
    private static partial Regex RunsUntilStoppedImport();

    /// <summary>
    /// The web server among a Java program's libraries - Tomcat, Jetty, Undertow or Netty, by the jar Spring Boot's web
    /// starters bring - or null when there is none, and a Spring Boot application is one meant to finish.
    /// </summary>
    private static string? WebServerOf(string chosen) =>
        JavaLibraries.For(chosen).ClassPath.Select(jar => Path.GetFileName(jar)).Select(jar => jar switch
        {
            _ when jar.StartsWith("tomcat-embed-core-", StringComparison.OrdinalIgnoreCase) => "Tomcat",
            _ when jar.StartsWith("jetty-server-", StringComparison.OrdinalIgnoreCase) => "Jetty",
            _ when jar.StartsWith("undertow-core-", StringComparison.OrdinalIgnoreCase) => "Undertow",
            _ when jar.StartsWith("reactor-netty-http-", StringComparison.OrdinalIgnoreCase) => "Netty",
            _ => null,
        }).FirstOrDefault(server => server is not null);

    /// <summary>
    /// A program that runs until it is stopped rather than until it is done - one with a window, or a server: what it is,
    /// until when it runs, and what of it a run that FixFinder ended cannot have checked.
    /// </summary>
    private sealed record RunsUntilStopped(string Is, string Like, string Until, string Unchecked)
    {
        public static RunsUntilStopped Window(string toolkit) =>
            new($"a program with a window - it uses {toolkit}", "a program with a window", "its window is closed", "what it does when someone uses its window");

        public static RunsUntilStopped Server(string how) =>
            new($"a server - it waits for connections with {how}", "a server", "it is stopped", "what it does when something connects");
    }

    /// <summary>What a program is when it runs until it is stopped - a window program, or a server - or null for one meant to finish.</summary>
    private static RunsUntilStopped? RunsUntilStoppedOf(string chosen) => Path.GetExtension(chosen).ToLowerInvariant() switch
    {
        ".java" => JavaRunsUntilStopped(chosen),
        ".py" or ".pyw" => PythonRunsUntilStopped(chosen),
        _ => null,
    };

    [GeneratedRegex(@"(?m)^[ \t]*(?:from[ \t]+(?<module>[\w.]+)[ \t]+import[ \t]+\(?(?<names>[\w \t,]+)|import[ \t]+(?<modules>[\w.]+(?:[ \t]+as[ \t]+\w+)?(?:[ \t]*,[ \t]*[\w.]+(?:[ \t]+as[ \t]+\w+)?)*))")]
    private static partial Regex PythonImport();

    /// <summary>
    /// Python's windowing toolkits and servers, by the module that brings each - with, where importing it is not enough,
    /// the call that makes the program wait: a chart can be drawn to a file, and a socket opened to talk to a server.
    /// </summary>
    private static readonly (string Module, string? WaitsAt, RunsUntilStopped What)[] PythonProgramsThatWait =
    [
        ("tkinter", null, RunsUntilStopped.Window("tkinter")),
        ("turtle", null, RunsUntilStopped.Window("turtle")),
        ("pygame", null, RunsUntilStopped.Window("pygame")),
        ("PyQt5", null, RunsUntilStopped.Window("Qt")),
        ("PyQt6", null, RunsUntilStopped.Window("Qt")),
        ("PySide2", null, RunsUntilStopped.Window("Qt")),
        ("PySide6", null, RunsUntilStopped.Window("Qt")),
        ("wx", null, RunsUntilStopped.Window("wxPython")),
        ("kivy", null, RunsUntilStopped.Window("Kivy")),
        ("matplotlib", ".show(", RunsUntilStopped.Window("matplotlib's plot window")),
        ("http.server", null, RunsUntilStopped.Server("Python's http.server")),
        ("socketserver", null, RunsUntilStopped.Server("socketserver")),
        ("socket", ".listen(", RunsUntilStopped.Server("a listening socket")),
        ("flask", ".run(", RunsUntilStopped.Server("Flask")),
        ("uvicorn", ".run(", RunsUntilStopped.Server("uvicorn")),
        ("aiohttp", "run_app(", RunsUntilStopped.Server("aiohttp")),
        ("asyncio", "start_server(", RunsUntilStopped.Server("asyncio's start_server")),
    ];

    /// <summary>
    /// What a Python program is when it runs until it is stopped, from what its files import - the file it starts from and
    /// the program's own modules it imports, which are all FixFinder takes for its program.
    /// </summary>
    private static RunsUntilStopped? PythonRunsUntilStopped(string chosen)
    {
        // A notebook's plots are made with no window, as Jupyter makes them, so plotting does not make it wait.
        var plotsWithoutAWindow = NotebookScript.IsCodeOfANotebook(chosen);

        foreach (var text in ProgramFiles.Of(chosen).Select(ReadOrNull).OfType<string>())
        {
            var imported = PythonImport().Matches(text).SelectMany(ModulesIn).ToList();

            foreach (var (module, waitsAt, what) in PythonProgramsThatWait)
            {
                if (plotsWithoutAWindow && module == "matplotlib") continue;

                var imports = imported.Any(name => name == module || name.StartsWith(module + ".", StringComparison.Ordinal));
                if (imports && (waitsAt is null || text.Contains(waitsAt, StringComparison.Ordinal))) return what;
            }
        }

        return null;
    }

    /// <summary>The modules one import statement names: import a, b as c names a and b; from a import b names a and a.b.</summary>
    private static IEnumerable<string> ModulesIn(Match import)
    {
        if (import.Groups["module"].Success)
        {
            var from = import.Groups["module"].Value;
            return [from, .. import.Groups["names"].Value.Split(',').Select(name => name.Trim().Split(' ')[0]).Where(name => name.Length > 0).Select(name => $"{from}.{name}")];
        }

        return import.Groups["modules"].Value.Split(',').Select(part => part.Trim().Split(' ', '\t')[0]);
    }

    /// <summary>
    /// What a Java program is when it runs until it is stopped - a window program written with JavaFX or Swing, or a server
    /// on a ServerSocket, Java's HttpServer or Spring Boot with a web server - from what the file it starts from imports,
    /// or a file whose class that file names; or null for a program meant to finish. Only those files count: a folder of
    /// exercises can hold a window program beside one that never ends for want of a loop that stops.
    /// </summary>
    private static RunsUntilStopped? JavaRunsUntilStopped(string chosen)
    {
        if (ReadOrNull(chosen) is not { } starting) return null;

        var webServer = new Lazy<string?>(() => WebServerOf(chosen));

        var named = ProgramFiles.Of(chosen)
            .Where(file => !string.Equals(file, Path.GetFullPath(chosen), StringComparison.OrdinalIgnoreCase))
            .Where(file => Regex.IsMatch(starting, $@"\b{Regex.Escape(Path.GetFileNameWithoutExtension(file))}\b"));

        foreach (var text in new[] { starting }.Concat(named.Select(ReadOrNull).OfType<string>()))
        {
            foreach (Match import in RunsUntilStoppedImport().Matches(text))
            {
                switch (import.Groups["name"].Value)
                {
                    case "javafx.":
                        return RunsUntilStopped.Window("JavaFX");
                    case "javax.swing.":
                        return RunsUntilStopped.Window("Swing");
                    case "com.sun.net.httpserver.":
                        return RunsUntilStopped.Server("Java's HttpServer");
                    case "java.net.ServerSocket":
                    case "java.net.*" when text.Contains("new ServerSocket(", StringComparison.Ordinal):
                        return RunsUntilStopped.Server("a ServerSocket");
                    case "org.springframework.boot." when webServer.Value is { } server:
                        return RunsUntilStopped.Server($"Spring Boot and {server}");
                }
            }
        }

        return null;
    }

    private static string? ReadOrNull(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// What to say instead of a finding when java would not start a JavaFX application because it had none of JavaFX's
    /// modules to start it with, and none are among the program's libraries: that is what this computer has, not a mistake
    /// in the code.
    /// </summary>
    private static string? NoJavaFxToStartIt(ParsedError error, string chosen)
    {
        if (error.ExceptionType != JavaStackTraceParser.LauncherError ||
            !(error.Message ?? "").StartsWith("JavaFX runtime components are missing", StringComparison.Ordinal) ||
            JavaLibraries.For(chosen).JavaFxModules.Count > 0)
        {
            return null;
        }

        return $"Java would not start {Path.GetFileName(chosen)}: its class extends javafx.application.Application, which java starts only with " +
               "JavaFX's modules, and none are among the program's libraries - FixFinder looks in its pom.xml or build.gradle, its IDE's " +
               "library settings and its lib folder. Naming JavaFX in one of those lets it run. The code was still read for mistakes.";
    }

    /// <summary>
    /// What to say instead of a finding when the file chosen has nothing to run: a Java class that declares no main method
    /// at all - one the program's other classes use, or a class of tests - which is all the launcher's "Main method not
    /// found" means for it. A main of the wrong shape is a mistake, and is still reported with its fix.
    /// </summary>
    private static string? NothingToRun(ParsedError error, string chosen)
    {
        if (error.ExceptionType != JavaStackTraceParser.LauncherError ||
            !(error.Message ?? "").StartsWith("Main method not found", StringComparison.Ordinal) ||
            SourceFile.Read(chosen) is not { } source)
        {
            return null;
        }

        string? open = null;
        foreach (var line in source.Lines)
        {
            if (DeclaresMain().IsMatch(CodeText.Mask(line, Syntax.CLike, ref open))) return null;
        }

        if (JavaTests.FrameworkOf(chosen) is var framework and not JavaTests.Framework.None)
        {
            var runner = JavaTests.RunnerFor(framework, JavaLibraries.For(chosen).ClassPath, JavaLibraries.CurrentStores);
            return $"{Path.GetFileName(chosen)} is a class of JUnit {(framework == JavaTests.Framework.JUnit4 ? "4" : "5")} tests, which FixFinder runs with " +
                   $"JUnit itself - and could not run here: {runner.CannotRun ?? "JUnit did not start"}. The code in this file was still read for mistakes.";
        }

        return $"{Path.GetFileName(chosen)} has no main method, so there is nothing in it to run: Java starts a program at " +
               "public static void main(String[] args). Choose the file of the program that has one. The code in this file was " +
               "still read for mistakes.";
    }

    /// <summary>The framework a file's tests are run with: for Python, pytest or unittest, whichever the file is written for; JUnit for Java.</summary>
    private static string TestFrameworkOf(string chosen) =>
        !IsPython(chosen) ? "JUnit" : PythonTests.FrameworkOf(chosen) == PythonTests.Framework.Pytest ? "pytest" : "unittest";

    private static bool IsPython(string file) => Path.GetExtension(file).ToLowerInvariant() is ".py" or ".pyw";

    /// <summary>Whether a file's tests are written for pytest, and the Python they were run with has no pytest installed.</summary>
    private static bool PytestWasMissing(IEnumerable<string> lines) => lines.Contains(PythonTests.PytestMissingMarker, StringComparer.Ordinal);

    /// <summary>
    /// What JUnit, unittest or pytest said of each test, when the run was a file of tests run through FixFinder's launcher:
    /// a failed test is an error, found for certain by running it, placed on the test's own line the failure went through.
    /// </summary>
    private void RecordTests(SessionOutcome outcome, LaunchPlan launch)
    {
        if (outcome.Run is not { } run || launch.ChosenFile is not { } chosen) return;

        var framework = TestFrameworkOf(chosen);
        var name = Path.GetFileName(chosen);
        var lines = run.Lines.Select(line => line.Text).ToList();
        var results = TestReport.ResultsIn(lines);

        if (PytestWasMissing(lines))
        {
            Note($"{name}'s tests are written for pytest, which is not part of Python and is not installed for the Python it was run with, " +
                 "so they were not run; the file was run as a program instead, and its code was still read for mistakes. Installing pytest " +
                 "for that Python - python -m pip install pytest - lets them be run. FixFinder never installs anything itself.");
            return;
        }

        foreach (var failed in results.Where(result => result.Status is "FAILED" or "SETUP-FAILED" or "TEARDOWN-FAILED"))
            Add(TestFinding(failed, chosen, framework));

        if (results.FirstOrDefault(result => result.Status == "FILE-SKIPPED") is { } fileSkipped)
        {
            Note($"pytest skipped the whole of {name}{(fileSkipped.Message is { } why ? $" - {why}" : "")} - so none of its tests were run.");
        }
        else if (results.Count == 0 && TestReport.TestsFound(lines) == 0)
        {
            Note(framework switch
            {
                "pytest" => $"pytest found no tests to run in {name}. By default it runs the functions whose names begin with test, and " +
                            "the methods whose names begin with test of the classes whose names begin with Test and that have no __init__.",
                "unittest" => $"unittest found no tests to run in {name}. It runs the methods of a unittest.TestCase class whose names begin with test.",
                _ => $"JUnit found no tests to run in {name}. JUnit 5 runs methods marked @Test that are not private and return nothing; " +
                     "JUnit 4 needs them public.",
            });
        }

        var skipped = results.Count(result => result.Status is "SKIPPED" or "ABORTED");
        if (skipped > 0)
        {
            Note($"{Count(skipped, "test")} {(skipped == 1 ? "was" : "were")} skipped{(IsPython(chosen) ? "" : ", or stopped by an assumption that did not hold")}, " +
                 $"so {framework} did not say whether {(skipped == 1 ? "it passes" : "they pass")}.");
        }

        switch (framework == "pytest" ? PythonTests.PytestExitCode(lines) : null)
        {
            case PythonTests.PytestUsageError:
                Note($"pytest would not run {name}'s tests, so whether they pass is not known." +
                     (PytestsReason(run) is { } reason ? $" It said: {reason}" : ""));
                break;

            case PythonTests.PytestInternalError:
                Note($"pytest itself failed while it was running {name}'s tests, so whether they pass is not known. Its INTERNALERROR lines say where.");
                break;

            case PytestInterrupted when outcome.Error is null:
                Note($"pytest stopped before it had run all of {name}'s tests" +
                     (PytestsStop(run) is { } stop ? $" - it said \"{stop}\" -" : "") + " so those it had not reached were not run.");
                break;
        }
    }

    /// <summary>pytest's code for having been stopped before it finished: by an error importing the tests, pytest.exit() or Ctrl+C.</summary>
    private const int PytestInterrupted = 2;

    /// <summary>What pytest says stopped it, between the rows of ! it prints either side: _pytest.outcomes.Exit: the marks file is missing.</summary>
    private static string? PytestsStop(TargetRunResult run) =>
        run.Lines.Select(line => Regex.Match(line.Text.Trim(), @"^!{3,} (?<said>.+?) !{3,}$")).FirstOrDefault(said => said.Success)?.Groups["said"].Value;

    /// <summary>Whether pytest was stopped before it had run all the tests, by something other than an error importing them.</summary>
    private static bool PytestStoppedEarly(SessionOutcome outcome, IReadOnlyList<string> lines, string framework) =>
        framework == "pytest" && PythonTests.PytestExitCode(lines) == PytestInterrupted && outcome.Error is null;

    /// <summary>
    /// Why pytest would not run, in its own words: what its usage error names, or the first line it wrote with the error it
    /// gives for it - a conftest.py it could not import, say - and the settings file it read, when it names one.
    /// </summary>
    private static string? PytestsReason(TargetRunResult run)
    {
        var said = run.Lines.Where(line => line.IsError).Select(line => line.Text.Trim()).Where(text => text.Length > 0).ToList();
        if (said.Count == 0) return null;

        const string usageError = ": error: ";
        var reason = said.FirstOrDefault(text => text.Contains(usageError, StringComparison.Ordinal)) is { } usage
            ? usage[(usage.IndexOf(usageError, StringComparison.Ordinal) + usageError.Length)..]
            : said.FirstOrDefault(text => text.StartsWith("E ", StringComparison.Ordinal)) is { } raised
                ? $"{said[0]} {raised[1..].Trim()}"
                : said[0];

        var settings = said.FirstOrDefault(text => text.StartsWith("inifile: ", StringComparison.Ordinal))?["inifile: ".Length..];

        return $"\"{reason}\"{(settings is null ? "" : $" - and it takes options from {settings} as well as from how it is run")}.";
    }

    /// <summary>A failed test as a finding: which test, what its framework said, and the lines the failure came through.</summary>
    private static Finding TestFinding(TestReport.TestResult test, string chosen, string framework)
    {
        var python = IsPython(chosen);

        // Python's frames name the file in full, and unittest's own are left out by its launcher; Java's name the class and file.
        bool InTheTestFile((string Class, string Method, string? File, int Line) frame) => python
            ? frame.File is { } file && string.Equals(Path.GetFullPath(file), Path.GetFullPath(chosen), StringComparison.OrdinalIgnoreCase)
            : test.ClassName.Length > 0 && (frame.Class == test.ClassName || frame.Class.StartsWith(test.ClassName + "$", StringComparison.Ordinal)) &&
              string.Equals(frame.File, Path.GetFileName(chosen), StringComparison.OrdinalIgnoreCase);

        var own = test.Frames.FirstOrDefault(frame => InTheTestFile(frame) && frame.Line > 0);

        var thrownAt = test.Frames.FirstOrDefault(frame => frame.Line > 0 && (python || (!frame.Class.StartsWith("org.junit", StringComparison.Ordinal) &&
            !frame.Class.StartsWith("org.opentest4j", StringComparison.Ordinal) && !frame.Class.StartsWith("java.", StringComparison.Ordinal) &&
            !frame.Class.StartsWith("jdk.", StringComparison.Ordinal) && !frame.Class.StartsWith("sun.", StringComparison.Ordinal))));

        var exception = test.Exception ?? "";
        var assertion = python
            ? exception == "AssertionError"
            : exception is "java.lang.AssertionError" or "junit.framework.AssertionFailedError" or "org.junit.ComparisonFailure" ||
              exception.StartsWith("org.opentest4j.", StringComparison.Ordinal) || exception.EndsWith(".AssertionFailedError", StringComparison.Ordinal);
        // pytest fails a test itself with Failed - DID NOT RAISE, when pytest.raises saw nothing raised, or pytest.fail().
        var failedByPytest = framework == "pytest" && exception == "Failed";

        // Said in one line, its runs of spaces made one; a title takes only its first line, as a long message is best read whole.
        var message = test.Message is { Length: > 0 } said && said != "null" ? Regex.Replace(said.Trim(), @"\s+", " ") : null;
        var firstLine = test.Message is { Length: > 0 } && message is not null ? test.Message.Trim().Split('\n')[0].Trim() : null;
        var shown = firstLine is null ? "" : $": {(firstLine.Length <= 120 ? firstLine : firstLine[..117] + "...")}";

        // A Python subtest is named with what it was run with: test_shares (people=4); a pytest test with its parameters: test_shares[4-20].
        var name = python && test.Name.Length > 0 ? test.Name : test.Method.Length > 0 ? test.Method : test.Name;
        var line = own is { Line: > 0 } ? $" on line {own.Line}" : "";
        var thrown = python ? "raised" : "thrown";
        var stoppedWith = $"{(exception.Length > 0 ? exception : "a failure")}{(message is null ? "" : $" ({message})")}" +
                          (thrownAt is { } at && at != own ? $", {thrown} in {at.Class}.{at.Method} on line {at.Line} of {Path.GetFileName(at.File)}" : line);
        var stoppedWithName = exception.Length > 0 ? exception.Split('.')[^1] : null;

        // Setting up for a test is not the test, and nor is cleaning up after it: when setting up fails, the test does not run
        // at all; when cleaning up fails, it has run. unittest names what failed - setUpClass (test_bank.BankTests) - and pytest
        // the test it was for.
        var (title, explanation) = (test.Status, framework) switch
        {
            ("SETUP-FAILED", "pytest") => (
                $"Setting up {name} stopped with {stoppedWithName ?? "a failure"}{shown}",
                $"pytest could not set up the test {name}: setting it up - a fixture it asks for, or a setup function - stopped with " +
                $"{stoppedWith}, so the test did not run."),
            ("SETUP-FAILED", _) => (
                $"{name} {(stoppedWithName is null ? "failed" : $"stopped with {stoppedWithName}")}{shown}",
                $"{framework} could not set up for the tests: {test.Name} stopped with {stoppedWith}, so the tests it sets up for did not run."),
            ("TEARDOWN-FAILED", "pytest") => (
                $"Cleaning up after {name} stopped with {stoppedWithName ?? "a failure"}{shown}",
                $"pytest ran the test {name}, and then cleaning up after it - the rest of a fixture after its yield, or a teardown " +
                $"function - stopped with {stoppedWith}."),
            ("TEARDOWN-FAILED", _) => (
                $"{name} {(stoppedWithName is null ? "failed" : $"stopped with {stoppedWithName}")}{shown}",
                $"{framework} ran the tests, and then cleaning up after them stopped: {test.Name} stopped with {stoppedWith}."),
            _ when assertion => (
                $"Test {name} failed{shown}",
                $"{framework} ran the test {test.Name}, and the assertion{line} did not hold{(message is null ? "." : $": {message}.")}"),
            _ when failedByPytest => (
                $"Test {name} failed{shown}",
                $"pytest ran the test {test.Name}, and failed it{line}{(message is null ? "." : $": {message}.")}"),
            _ => (
                stoppedWithName is null ? $"Test {name} failed{shown}" : $"Test {name} stopped with {stoppedWithName}{shown}",
                $"{framework} ran the test {test.Name}, and it stopped with {stoppedWith}."),
        };

        var cleaningUp = test.Status == "TEARDOWN-FAILED";

        return new Finding
        {
            Kind = FindingKind.Runtime,
            Severity = Severity.Error,
            Confidence = Confidence.Certain,
            File = chosen,
            Line = own is { Line: > 0 } ? own.Line : null,
            Title = title,
            Explanation = explanation,
            WhyItMatters = cleaningUp
                ? "What a test leaves behind when cleaning up after it fails - a file still open, a setting still changed - is still there " +
                  "for the tests that come after it."
                : "A test that fails is a check the code did not pass: either the code does not do what the test expects of it, " +
                  "or the test expects the wrong thing.",
            SuggestedFix = cleaningUp
                ? $"Go to the line the exception was {thrown} on, in the code that cleans up after the tests, and put right what failed there."
                : assertion || failedByPytest || exception.Length == 0
                    ? "Compare what the test expects with what the code it calls gives back, and follow that code to where the two part."
                    : $"Go to the line the exception was {thrown} on - in the code the test calls, if it came from there - and put right what failed there.",
            CorrectedExample = "",
            RuleId = "test-failed",
        };
    }

    /// <summary>
    /// Whether an error came from FixFinder's own test launcher - JUnit's for Java, unittest's or pytest's for Python - rather
    /// than from the program: the launcher is where it was raised, or the nearest place to that of the program's own.
    /// </summary>
    /// <remarks>
    /// Both are looked at because Python shows an error raised while another was being handled with that other one's
    /// places too, and the nearest of the program's own can then be the test file, where the first one began.
    /// </remarks>
    private static bool FromTestLauncher(ParsedError error) =>
        new[] { error.CulpritFrame?.File, error.Frames.FirstOrDefault()?.File }.OfType<string>().Any(file =>
            Path.GetFileName(file).Equals(JavaTests.LauncherClass + ".java", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(file).StartsWith(PythonTests.LauncherPrefix, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(file).StartsWith(PythonTests.PytestLauncherPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether an expected-output run is the very run the syntax check made: the same input typed in.</summary>
    private static bool SameRunAsTheCheck(ExpectedRun run, LaunchPlan launch) =>
        string.Equals(run.Input ?? "", launch.Spec?.StandardInput ?? "", StringComparison.Ordinal);

    /// <summary>
    /// What happened to the run, in words that are true of it. A run that finished and reported success, having printed
    /// an exception on the way - one caught and printed with printStackTrace, or one that ended a thread other than
    /// the main one - did not crash; nor did a program that printed an error of its own and ended with a failing code.
    /// </summary>
    private static string RunTitle(ParsedError error, TargetRunResult? run)
    {
        var what = FindingFactory.TitleOf(error);

        if (error.ExceptionType == JavaStackTraceParser.LauncherError) return $"Java could not start it: {error.Message}";
        if (run is { ExitCode: 0 }) return $"It ran to the end, but printed an exception: {what}";

        var crashCode = run?.ExitCode is { } code && RunClassifier.IsKnownCrash(code);
        return error.LanguageId == "generic" && !crashCode ? $"It stopped with an error: {what}" : $"It crashed: {what}";
    }

    private void RecordRun(SessionOutcome outcome, LaunchPlan launch)
    {
        var chosen = launch.ChosenFile ?? launch.Spec!.ExecutablePath;

        foreach (var warning in outcome.Warnings) Note(warning);

        if (outcome.Result == SessionResult.CouldNotRun)
        {
            Note($"{outcome.Headline} {outcome.Detail}");
            return;
        }

        if (outcome.Error is { } error)
        {
            if (error.ExceptionType is "EOFError" or "java.util.NoSuchElementException" && outcome.Result == SessionResult.NothingFound && outcome.Best is null)
            {
                Note($"{outcome.Headline} {outcome.Detail}");
                return;
            }

            if (NothingToRun(error, chosen) is { } nothing)
            {
                Note(nothing);
                return;
            }

            if (FromTestLauncher(error))
            {
                // Said in full: what stopped it is the part that explains it, and where in FixFinder's launcher is no help to anyone.
                var said = error.Message is { Length: > 0 } message ? $"{error.ShortExceptionType ?? error.ExceptionType}: {message}" : error.Summary;
                Note($"FixFinder's launcher for {TestFrameworkOf(chosen)} could not finish, so whether the tests pass is not known: {said}.");
                return;
            }

            if (NoJavaFxToStartIt(error, chosen) is { } noJavaFx)
            {
                Note(noJavaFx);
                return;
            }

            var kind = LocalFixEngine.IsCompileError(error) || LocalFixEngine.IsSyntaxPhase(error) ? FindingKind.Syntax : FindingKind.Runtime;

            if (kind == FindingKind.Runtime && RanWithoutItsLibraries(error, chosen) is { } withoutLibraries)
            {
                Add(FindingFactory.FromError(error, kind, Severity.Warning, Confidence.Possible, chosen) with
                {
                    Title = RunTitle(error, outcome.Run),
                    Explanation = withoutLibraries,
                    SuggestedFix = "Open the project in its IDE, or build it once with its build tool, so that what it names is downloaded, then check " +
                                   "it again - FixFinder never downloads anything itself.",
                    CorrectedExample = "",
                });
                return;
            }

            // Code written for Google Colab needs Colab's own module, which nothing installs anywhere else.
            if (kind == FindingKind.Runtime && MissingModule.IsColabOnly(error))
            {
                Add(FindingFactory.FromError(error, kind, Severity.Warning, Confidence.Certain, chosen) with
                {
                    Title = RunTitle(error, outcome.Run),
                    Explanation = "google.colab is Google Colab's own module: it is there on Colab's machines and installed nowhere else, so " +
                                  "this code was written to run on Colab. Here it stops at this import, and nothing after it runs.",
                    SuggestedFix = "Run it on Google Colab. To run it here, take out what it uses google.colab for - drive.mount, " +
                                   "files.upload - and open the files it reads by their paths on this computer instead.",
                    CorrectedExample = "",
                    RuleId = "python-colab-only",
                });
                return;
            }

            // A connection the program made was refused: what it connects to was not there to answer, which no change to the code alters.
            if (kind == FindingKind.Runtime && RefusedConnection.In(error) is { } refusal)
            {
                Add(FindingFactory.FromError(error, kind, Severity.Warning, refusal.SaidRefused ? Confidence.Certain : Confidence.Likely, chosen) with
                {
                    Title = RefusedConnection.TitleOf(refusal, error),
                    Explanation = RefusedConnection.ExplanationOf(refusal),
                    WhyItMatters = "A program that needs a database or a server cannot do its work while that is not there to answer, and no " +
                                   "change to its code alters that.",
                    SuggestedFix = RefusedConnection.FixOf(refusal),
                    CorrectedExample = "",
                    RuleId = "connection-refused",
                });
                return;
            }

            var local = outcome.Best is { } best && (best.LocalFix is not null || best.Id.EndsWith(":did-you-mean", StringComparison.Ordinal)) ? best : null;

            var finishedNormally = kind == FindingKind.Runtime && outcome.Run is { ExitCode: 0 };

            Add(FindingFactory.FromError(error, kind, finishedNormally ? Severity.Warning : Severity.Error, Confidence.Certain, chosen, local) with
            {
                Title = kind != FindingKind.Runtime ? FindingFactory.TitleOf(error) : RunTitle(error, outcome.Run),
            });

            return;
        }

        switch (outcome.Result)
        {
            case SessionResult.FailedSilently when outcome.Run?.Outcome != RunOutcome.Crashed:
                Add(new Finding
                {
                    Kind = FindingKind.Runtime,
                    Severity = Severity.Warning,
                    Confidence = Confidence.Possible,
                    File = chosen,
                    Title = outcome.Headline,
                    Explanation = outcome.Detail,
                    WhyItMatters = "A program that ends with a failing code is saying it did not finish its job. That is right when something " +
                                   "it needs is missing, and a mistake when nothing is.",
                    SuggestedFix = "If it needs arguments, input or a file, give them under the program and check it again. If it should have " +
                                   "finished, find the line that ends it - a System.exit, sys.exit or return from main - and the test that leads there.",
                    CorrectedExample = "",
                    RuleId = "ended-with-failure-code",
                });
                break;

            case SessionResult.FailedSilently:
                Add(new Finding
                {
                    Kind = FindingKind.Runtime,
                    Severity = Severity.Error,
                    Confidence = Confidence.Certain,
                    File = chosen,
                    Title = outcome.Headline,
                    Explanation = outcome.Detail,
                    WhyItMatters = "The program stops partway through, and nothing it was meant to do after that point happens.",
                    SuggestedFix = "Run it with a debugger, or add prints to find the last line that runs. For C and C++, look for a pointer " +
                                   "or index that goes out of bounds.",
                    CorrectedExample = "",
                    RuleId = "failed-silently",
                });
                break;

            case SessionResult.RanFine when outcome.Run?.Outcome == RunOutcome.TimedOut && RunsUntilStoppedOf(chosen) is { } runsOn:
                Note($"{Path.GetFileName(chosen)} is {runsOn.Is} - and such a program keeps running until {runsOn.Until}, so it was still " +
                     $"running when its time ran out, which is not by itself a sign of a mistake. What it did before then was checked; " +
                     $"{runsOn.Unchecked} was not.");
                break;

            case SessionResult.RanFine when outcome.Run?.Outcome == RunOutcome.TimedOut:
                Add(new Finding
                {
                    Kind = FindingKind.Runtime,
                    Severity = Severity.Warning,
                    Confidence = Confidence.Possible,
                    File = chosen,
                    Title = "It was still running when the time ran out",
                    Explanation = outcome.Spec is { } spec
                        ? $"It was still running after {spec.Timeout.TotalSeconds:0.#} seconds, when FixFinder stopped it, and had printed no error."
                        : "It was still running when FixFinder stopped it, and had printed no error.",
                    WhyItMatters = "A program that is meant to finish but never does has a loop that cannot end, or is waiting for input nobody gave it.",
                    SuggestedFix = "If it asks questions, type the answers into the input box. Otherwise look for a loop whose condition never changes.",
                    CorrectedExample = "",
                    RuleId = "timed-out",
                });
                break;
        }
    }

    private static string Summarise(SessionOutcome outcome, CompilerReport report, string chosen)
    {
        var warnings = report.Warnings.Count > 0 ? $", with {Count(report.Warnings.Count, "warning")}" : "";
        var reads = Path.GetExtension(chosen).ToLowerInvariant() is ".py" or ".pyw" or ".js" or ".mjs" or ".cjs" ? "No syntax errors" : "It builds";

        var lines = outcome.Run?.Lines.Select(line => line.Text).ToList() ?? [];
        var tests = TestReport.ResultsIn(lines);
        var framework = TestFrameworkOf(chosen);

        if (PytestWasMissing(lines)) return $"{reads}{warnings}; its tests are written for pytest, which is not installed for the Python it ran with";

        switch (framework == "pytest" ? PythonTests.PytestExitCode(lines) : null)
        {
            case PythonTests.PytestUsageError:
                return $"{reads}{warnings}, but pytest would not run its tests";
            case PythonTests.PytestInternalError:
                return $"{reads}{warnings}, but pytest itself failed while running its tests";
        }

        if (tests.Count > 0 || TestReport.TestsFound(lines) is 0)
        {
            // A unittest test is one test however many of its subtests fail, as unittest counts it; each of pytest's - each set
            // of parameters it is given - and each of JUnit's is its own.
            string TestOf(TestReport.TestResult test) => framework == "unittest" ? $"{test.ClassName}.{test.Method}" : $"{test.ClassName}.{test.Method}.{test.Name}";

            var failed = tests.Where(test => test.Status == "FAILED").Select(TestOf).Distinct(StringComparer.Ordinal).Count();
            var ran = tests.Where(test => test.Status is "SUCCESSFUL" or "FAILED").Select(TestOf).Distinct(StringComparer.Ordinal).Count();

            // Tests whose setting up failed never ran, so they are neither passes nor failures; cleaning up after tests that
            // ran is said apart from them.
            var notSetUp = tests.Any(test => test.Status == "SETUP-FAILED");
            var othersNotRun = notSetUp ? ", and setting up for others failed, so those did not run" : "";
            var notCleanedUp = tests.Any(test => test.Status == "TEARDOWN-FAILED") ? $"; cleaning up after {(ran == 1 ? "it" : "them")} failed" : "";

            if (PytestStoppedEarly(outcome, lines, framework))
            {
                var found = TestReport.TestsFound(lines) is { } count && count >= ran ? count : ran;
                return $"{reads}{warnings}; pytest stopped after {ran} of its {Count(found, "test")}{(failed > 0 ? $", {failed} of which failed" : "")}";
            }

            return failed > 0 ? $"{reads}{warnings}; {failed} of {Count(ran, "test")} failed{othersNotRun}{notCleanedUp}"
                : ran > 0 && notSetUp ? $"{reads}{warnings}; {(ran == 1 ? "the test that ran passes" : $"the {ran} tests that ran pass")}{othersNotRun}{notCleanedUp}"
                : ran > 0 ? $"{reads}{warnings}, and {(ran == 1 ? "its test passes" : $"all {ran} of its tests pass")}{notCleanedUp}"
                : notSetUp ? $"{reads}{warnings}, but setting up for its tests failed, so {framework} ran none of them"
                : $"{reads}{warnings}, but {framework} ran none of its tests";
        }

        return outcome.Result switch
        {
            SessionResult.CouldNotRun => "It could not be started",
            _ when outcome.Error is { } error && NothingToRun(error, chosen) is not null => $"{reads}{warnings}; it has no main method to run",
            _ when outcome.Error is { } error && NoJavaFxToStartIt(error, chosen) is not null => $"{reads}{warnings}; Java would not start it without JavaFX",
            _ when outcome.Error is { } error && RefusedConnection.In(error) is { } refusal =>
                $"{reads}{warnings}, but it could not connect{(refusal.Address is { } address ? $" to {address}" : "")} when run",
            _ when outcome.Error is not null => $"{reads}{warnings}, but it stops with an error when run",
            SessionResult.FailedSilently when outcome.Run?.Outcome != RunOutcome.Crashed => $"{reads}{warnings}, but it stops with a failing exit code",
            SessionResult.FailedSilently => $"{reads}{warnings}, but it crashes when run",
            _ when outcome.Run?.Outcome == RunOutcome.TimedOut && RunsUntilStoppedOf(chosen) is { } runsOn =>
                $"{reads}{warnings}, and it was still running when its time ran out, as {runsOn.Like} does",
            _ when outcome.Run?.Outcome == RunOutcome.TimedOut => $"{reads}{warnings}, but it never finished",
            _ => $"{reads}{warnings}, and it runs to the end",
        };
    }

    private async Task<FixCandidate?> FixAsync(
        ParsedError error, IReadOnlyList<ParsedError> all, IReadOnlyList<CapturedLine> output, string? root, bool fromBuild, CancellationToken cancellationToken)
    {
        var context = new LocalFixContext
        {
            Error = error,
            Others = all.Where(e => !ReferenceEquals(e, error)).ToList(),
            Output = output,
            SourceRoot = root,
            FromBuild = fromBuild,
            Language = Language,
        };

        try
        {
            return (await LocalFixEngine.FindAsync(context, message => Log?.Invoke(message), cancellationToken))?.Candidate;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log?.Invoke($"Working out a fix for {error.Summary} failed: {ex.Message}");
            return null;
        }
    }

    private async Task<string> LogicLaneAsync(LaunchPlan launch, IReadOnlyList<string> files, Task<bool> builds, CancellationToken cancellationToken)
    {
        try
        {
            Progress?.Invoke(CheckLane.Logic, "Reading the code for logic mistakes...");

            var found = await CodeFindingsAsync(launch, files, cancellationToken);

            if (Expected is not { IsEmpty: false } expected || launch.ChosenFile is not { } chosen)
                return found == 0 ? "No logic mistakes found in the code" : $"{Count(found, "possible mistake")} in the code";

            Progress?.Invoke(CheckLane.Logic, "Waiting for it to build before checking what it prints...");

            if (!await builds)
            {
                Note("What it prints was not checked, because it has to build first. Fix the errors that stop it building, then check it again.");
                return found == 0 ? "Output not checked - it does not build yet" : $"{Count(found, "possible mistake")}; output not checked";
            }

            var repair = new LogicRepair();
            repair.Log += message => Log?.Invoke(message);
            repair.Progress += message => Progress?.Invoke(CheckLane.Logic, message);

            var result = await repair.RunAsync(chosen, expected, launch.Spec!.Timeout, cancellationToken);

            if (result is null) return found == 0 ? "It printed what you expected" : $"Printed what you expected; {Count(found, "possible mistake")} in the code";

            foreach (var note in result.Notes) Note(note);

            var stopped = result.Mismatch.Ending != RunEnding.Finished;

            if (stopped && result.Fix is null && SameRunAsTheCheck(expected.Runs[result.FailingRun - 1], launch))
            {
                // The run that stopped is the one the syntax check made too, which reports what stopped it; a finding that
                // only says it did not get as far as the expected output would be the same failure twice.
                Note($"What it prints could not be compared with what you expected: {result.Mismatch.Describe()}.");
                return "It stopped before printing what you expected";
            }

            Add(FindingFactory.FromWrongOutput(result, expected.Runs.Count, chosen, SourceFile.Read(chosen)));

            return (stopped, result.Fix is not null) switch
            {
                (true, true) => "It stopped before printing what you expected - FixFinder found the change that fixes it",
                (true, false) => "It stopped before printing what you expected",
                (false, true) => "Wrong output - FixFinder found the change that fixes it",
                _ => "Wrong output",
            };
        }
        finally
        {
            LaneFinished?.Invoke(CheckLane.Logic, "done");
        }
    }

    /// <summary>
    /// Reads the code for mistakes - its patterns, and what following every value through it shows - without compiling
    /// or running the program: a check that can follow every save without costing the program's time or its side effects.
    /// </summary>
    /// <remarks>
    /// What it skips is said rather than left out quietly: the report says the program was not compiled or run, and code
    /// that does not read as its language at all is noted, since otherwise the report would find no mistakes in it.
    /// </remarks>
    public async Task<CheckReport> CheckCodeAsync(LaunchPlan launch, CancellationToken cancellationToken = default)
    {
        // A notebook is written out again, so what is read is the notebook as it was saved, not as it was when picked.
        if (launch.PickedFile is { } picked && launch.ChosenFile is { } script && NotebookScript.Of(script) is not null)
            launch = TargetFactory.FromFile(picked, launch.Spec?.Timeout);

        if (!launch.Ok || launch.Spec is null) return new CheckReport([], [launch.Problem ?? "That program cannot be read."], "Not checked", "Not checked", null);

        var files = launch.ChosenFile is { } chosen ? ProgramFiles.Of(chosen) : [];
        _launch = launch;
        _cancellation = cancellationToken;
        _codeOnly = true;
        NoteWhatANotebookIsCheckedAs();

        try
        {
            await CodeFindingsAsync(launch, files, cancellationToken);
        }
        finally
        {
            LaneFinished?.Invoke(CheckLane.Logic, "done");
        }

        Task[] finishing;
        lock (_gate) finishing = [.. _comparing.Values, .. _verifying.Values];
        await Task.WhenAll(finishing);

        lock (_gate)
        {
            var fromCode = FoundInTheCode();
            var logicSummary = fromCode == 0 ? "No logic mistakes found in the code" : $"{Count(fromCode, "possible mistake")} in the code";
            if (_reusedFunctions > 0)
                logicSummary += $" - {_reusedFunctions} of {_reusedFunctions + _analysedFunctions} functions unchanged since the last check";

            return new CheckReport(Sorted(_findings), [.. _notes], "Not compiled or run - read as it was saved", logicSummary, null);
        }
    }

    /// <summary>The logic patterns and the analyses, together: everything a check finds from the code alone.</summary>
    private async Task<int> CodeFindingsAsync(LaunchPlan launch, IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        var found = 0;
        var sourcesRead = files.Select(SourceFile.Read).OfType<SourceFile>().ToList();
        var patterns = sourcesRead.SelectMany(source => LogicPatterns.Scan(source, Log).Select(finding => (Source: source, Finding: finding))).ToList();

        var analysed = AnalyseAsync(launch, files, cancellationToken);

        await ForEachAsync(patterns, async item =>
        {
            var (checkedBy, compiles, verified) = await CheckPatternFixAsync(item.Source, item.Finding.Fix, cancellationToken);
            Add(FindingFactory.FromPattern(item.Finding, item.Source, checkedBy, compiles) with { Verified = verified });
            Interlocked.Increment(ref found);
        }, cancellationToken);

        return found + await analysed;
    }

    /// <summary>Follows every value through the program - abstract interpretation - for the languages it can read so far.</summary>
    private async Task<int> AnalyseAsync(LaunchPlan launch, IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        if (files.Count == 0) return 0;

        try
        {
            var reading = ReadProgramAsync(launch, files, cancellationToken);
            if (reading is null) return 0;

            Progress?.Invoke(CheckLane.Logic, "Following every value through the code...");
            var program = await reading;
            foreach (var problem in program.Problems) Log?.Invoke($"Following the values skipped {problem}");

            // A full check compiles or runs the program and reports such a problem with its fix; reading the code alone
            // would otherwise just find nothing in the part it could not read.
            if (_codeOnly && program.Problems.Count > 0)
            {
                Note($"Part of the code could not be read, so it was not checked: {program.Problems[0]}. " +
                     "Check it with its language to compile or run it, which reports the mistake with a fix.");
            }

            var findings = AbstractChecks.Run(program, new SourceText(), Cache);
            if (Cache is { LastRun: { Reused: > 0 } run })
            {
                Log?.Invoke($"{Count(run.Reused, "function")} had not changed since the last check, so what was found in them then was used again; " +
                            $"{Count(run.Analysed, "function")} analysed afresh.");
                _reusedFunctions = run.Reused;
                _analysedFunctions = run.Analysed;
            }

            // Confirming runs the program's own functions, which a check of the code alone never does.
            if (!_codeOnly && program.Language == SourceLanguage.Python && PythonInterpreter(launch) is { } python && findings.Any(f => f.WitnessValues is { Count: > 0 }))
            {
                Progress?.Invoke(CheckLane.Logic, "Running the code with the inputs that should break it...");
                findings = await Confirmation.ConfirmAsync(findings, python, cancellationToken);
            }

            foreach (var finding in findings)
            {
                // A change an analysis worked out is checked the way a pattern's is: a copy of the file with it has to compile.
                if (finding.Fix is { } fix && SourceFile.Read(fix.File) is { } source)
                {
                    var (checkedBy, compiles, verified) = await CheckPatternFixAsync(source, fix, cancellationToken);
                    Add(FindingFactory.FromAnalysis(finding, source, checkedBy, compiles) with { Verified = verified });
                }
                else
                {
                    Add(FindingFactory.FromAnalysis(finding));
                }
            }

            return findings.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log?.Invoke($"Following the values through the code stopped early: {ex.Message}");
            return 0;
        }
    }

    /// <summary>The Python the program runs with, or any Python on this computer.</summary>
    private static string? PythonInterpreter(LaunchPlan launch) =>
        launch.Spec is { } spec && Path.GetFileNameWithoutExtension(spec.ExecutablePath).StartsWith("py", StringComparison.OrdinalIgnoreCase)
            ? spec.ExecutablePath
            : PythonFrontend.FindInterpreter();

    /// <summary>Reads the program with its own language's parser, or returns null when that language cannot be read yet.</summary>
    private static Task<IrProgram>? ReadProgramAsync(LaunchPlan launch, IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        bool AllEndIn(params string[] extensions) => files.All(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

        if (AllEndIn(".py", ".pyw"))
            return PythonInterpreter(launch) is { } interpreter ? PythonFrontend.ReadAsync(files, interpreter, cancellationToken) : null;

        if (AllEndIn(".java"))
            return JavaFrontend.FindTools() is { } tools ? JavaFrontend.ReadAsync(files, tools.Javac, tools.Java, cancellationToken) : null;

        if (AllEndIn(".cs")) return CSharpFrontend.ReadAsync(files, cancellationToken);

        if (AllEndIn(".go"))
            return GoFrontend.FindGo() is { } go ? GoFrontend.ReadAsync(files, go, cancellationToken) : null;

        if (AllEndIn(".js", ".mjs", ".cjs")) return JavaScriptFrontend.ReadAsync(files, cancellationToken);

        if (AllEndIn(".c", ".h", ".cpp", ".cc", ".cxx", ".hpp", ".hh")) return CFrontend.ReadAsync(files, cancellationToken);

        return null;
    }

    /// <summary>
    /// Compiles a copy of the file with the change in it, and says what that showed.
    /// </summary>
    /// <remarks>
    /// The copy is the point: the person's own file is never written to in order to find out whether a fix is any
    /// good. What comes back is deliberately modest - compiling shows the change is valid code, not that it works -
    /// so the stage recorded is Compiled and nothing further is claimed from it.
    /// </remarks>
    private async Task<(string? CheckedBy, bool Compiles, Verification Verified)> CheckPatternFixAsync(
        SourceFile source, LocalFix? fix, CancellationToken cancellationToken)
    {
        if (fix is null || fix.ApplyTo(source) is not { } changed) return (null, false, Verification.NotTested);

        if (!CompileCheck.CanCheck(source.Path))
        {
            return (null, true, Verification.NotTested.With(
                VerificationStage.Compiled, StageResult.Skipped, "This language is not compiled before it runs."));
        }

        var after = await CompileCheck.RunAsync(source, changed, null, cancellationToken);

        if (!after.Ran)
        {
            return ("Not checked - there was nothing to compile it with.", true, Verification.NotTested.With(
                VerificationStage.Compiled, StageResult.Skipped, "There was nothing on this machine to compile it with."));
        }

        var name = Path.GetFileName(source.Path);

        if (after.Clean)
        {
            return ($"A copy of {name} with this change compiles.", true, Verification.NotTested.With(
                VerificationStage.Compiled, StageResult.Passed, $"A copy of {name} with this change compiles."));
        }

        var before = await CompileCheck.RunAsync(source, source.Lines, null, cancellationToken);
        var editedTo = fix.StartLine + Math.Max(fix.NewLines.Count, 1) - 1;
        var inEdit = after.Errors.Any(e => LocalFixContext.OwnFrame(e)?.Line is { } line && line >= fix.StartLine - 1 && line <= editedTo + 1);

        if (before.Ran && after.Errors.Count <= before.Errors.Count && !inEdit)
        {
            // The file did not compile before the change either, and the change did not make that worse or add an
            // error of its own - which is as much as compiling can show about a file that was already broken.
            return ($"A copy of {name} with this change adds no new errors.", true, Verification.NotTested.With(
                VerificationStage.Compiled, StageResult.Inconclusive,
                $"A copy of {name} with this change adds no new errors, but the file did not compile before it either."));
        }

        return (null, false, Verification.NotTested.With(
            VerificationStage.Compiled, StageResult.Failed, $"A copy of {name} with this change does not compile."));
    }

    /// <summary>
    /// Checks on an expression whose value is not used - which, as the last statement of a notebook cell, Jupyter shows
    /// under the cell rather than throwing away.
    /// </summary>
    private static readonly HashSet<string> ValueNotUsed = new(StringComparer.Ordinal)
    {
        "logic-python-statement-has-no-effect", "logic-python-result-discarded", "logic-python-comparison-statement",
    };

    private void Add(Finding finding)
    {
        // Found in the script a notebook's code was checked as: said of the notebook's cell, or not at all when it is in
        // FixFinder's own lines, or an expression Jupyter would show as a cell's last statement.
        if (NotebookScript.Of(finding.File) is { } notebook)
        {
            if (finding.Line is not { } scriptLine || notebook.PlaceOf(scriptLine) is not { } place) return;
            if (ValueNotUsed.Contains(finding.RuleId) && notebook.InACellsLastStatement(scriptLine)) return;

            finding = finding with { InNotebook = place };
        }

        IReadOnlyList<Finding> snapshot;

        lock (_gate)
        {
            var twin = _findings.FirstOrDefault(f => string.Equals(f.File, finding.File, StringComparison.OrdinalIgnoreCase) &&
                                                     (SameFamily(f, finding) || SameFix(f, finding)));

            if (twin is not null)
            {
                var better = Rank(finding) > Rank(twin) ? finding : twin;
                var surest = (Confidence)Math.Min((int)finding.Confidence, (int)twin.Confidence);
                _findings.Remove(twin);
                finding = better with { Confidence = surest };
            }

            if (finding.Fix is { } fix && _fixChanges.TryGetValue(FixKey(fix), out var changes)) finding = finding with { FixChanges = changes };
            if (finding.Fix is { } tried && _verified.TryGetValue(FixKey(tried), out var already)) finding = finding with { Verified = already };

            _findings.Add(finding);
            snapshot = Sorted(_findings);
        }

        FindingsChanged?.Invoke(snapshot);
        CompareFix(finding);
        VerifyFix(finding);
    }

    /// <summary>
    /// Starts running a copy of the program with the fix in it, to find out whether the failure stops happening.
    /// </summary>
    /// <remarks>
    /// Only worth the run when there is something for it to settle: a failure that might stop, or output the person
    /// said the program should print. Bounded the same way the fix comparison is, because each one of these runs the
    /// program again and a report full of fixes would otherwise run it a dozen times.
    /// </remarks>
    private void VerifyFix(Finding finding)
    {
        if (finding.Fix is not { } fix || finding.Verified.ResultOf(VerificationStage.Compiled) == StageResult.Failed) return;
        if (finding.Error is null && Expected is not { IsEmpty: false }) return;
        if (SourceFile.Read(fix.File) is not { } source) return;

        var key = FixKey(fix);

        lock (_gate)
        {
            if (_verifying.ContainsKey(key) || _verifying.Count >= MostFixesVerified) return;
            _verifying[key] = Task.Run(() => VerifyFixAsync(finding, source, fix, key), CancellationToken.None);
        }
    }

    private async Task VerifyFixAsync(Finding finding, SourceFile source, LocalFix fix, string key)
    {
        Verification verified;

        try
        {
            verified = await FixRun.CheckAsync(finding.Verified, source, fix, finding.Error, Expected, _cancellation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log?.Invoke($"Trying a fix out stopped early: {ex.Message}");
            return;
        }

        if (!verified.WasTested) return;

        IReadOnlyList<Finding> snapshot;

        lock (_gate)
        {
            _verified[key] = verified;
            for (var i = 0; i < _findings.Count; i++)
            {
                if (_findings[i].Fix is { } other && FixKey(other) == key) _findings[i] = _findings[i] with { Verified = verified };
            }

            snapshot = Sorted(_findings);
        }

        FindingsChanged?.Invoke(snapshot);
    }

    /// <summary>Starts working out what a finding's fix changes in what the program does - semantic diffing of the fix.</summary>
    private void CompareFix(Finding finding)
    {
        if (finding.Fix is not { } fix || !FixDiffs.Supports(fix.File) || _launch is not { } launch) return;

        var key = FixKey(fix);
        lock (_gate)
        {
            if (_comparing.ContainsKey(key) || _comparing.Count >= MostFixesCompared) return;
            _comparing[key] = Task.Run(() => CompareFixAsync(fix, key, launch), CancellationToken.None);
        }
    }

    private async Task CompareFixAsync(LocalFix fix, string key, LaunchPlan launch)
    {
        IReadOnlyList<string>? changes;

        try
        {
            var python = Path.GetExtension(fix.File).Equals(".py", StringComparison.OrdinalIgnoreCase) ? PythonInterpreter(launch) : null;
            changes = await FixDiffs.DescribeAsync(fix, python, _cancellation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log?.Invoke($"Comparing a fix with the original stopped early: {ex.Message}");
            return;
        }

        if (changes is not { Count: > 0 }) return;

        IReadOnlyList<Finding> snapshot;

        lock (_gate)
        {
            _fixChanges[key] = changes;
            for (var i = 0; i < _findings.Count; i++)
            {
                if (_findings[i].Fix is { } other && FixKey(other) == key) _findings[i] = _findings[i] with { FixChanges = changes };
            }

            snapshot = Sorted(_findings);
        }

        FindingsChanged?.Invoke(snapshot);
    }

    private static string FixKey(LocalFix fix) =>
        $"{Path.GetFullPath(fix.File).ToUpperInvariant()}|{fix.StartLine}|{fix.RemoveCount}|{string.Join("\n", fix.NewLines)}";

    private static bool SameFamily(Finding a, Finding b) => a.Family is { } family && family == b.Family && a.Line == b.Line;

    private static bool SameFix(Finding a, Finding b) =>
        a.Fix is { } x && b.Fix is { } y && x.StartLine == y.StartLine && x.RemoveCount == y.RemoveCount && x.NewLines.SequenceEqual(y.NewLines);

    private static int Rank(Finding finding) =>
        (finding.Fix is not null ? 1000 : 0) + (2 - (int)finding.Severity) * 100 + (finding.ExampleIsFromYourCode ? 10 : 0) + (2 - (int)finding.Confidence);

    /// <summary>
    /// Says, for a notebook, how its places are counted - the cell from the notebook's top and the line within it, which is
    /// not the number Jupyter shows beside a cell that has run - and which of its IPython commands were not carried out.
    /// </summary>
    private void NoteWhatANotebookIsCheckedAs()
    {
        if (_launch?.ChosenFile is not { } chosen || NotebookScript.Of(chosen) is not { } notebook) return;

        var name = Path.GetFileName(notebook.Notebook);

        Note($"{name}'s cells are counted from its top, Markdown cells included, and each line within its cell. " +
             "The number Jupyter shows beside a cell that has run - [3], or In [3] - is a different count: the order the cells were run in.");

        if (notebook.CommandsNotCarriedOut is not { Count: > 0 } notCarriedOut) return;

        const int mostNamed = 3;
        var named = notCarriedOut.Take(mostNamed).Select(command => $"\"{command.Command}\" (cell {command.Cell}, line {command.Line})");
        var more = notCarriedOut.Count > mostNamed ? $", and {notCarriedOut.Count - mostNamed} more" : "";

        Note($"{name} has IPython commands FixFinder does not carry out: {string.Join(", ", named)}{more}. Whatever they would have " +
             "done - fetched or written a file, defined a name - is missing when the cells after them run, and a failure that follows " +
             "from that is not a mistake in the notebook's code.");
    }

    private void Note(string note)
    {
        // A note about a notebook's code names the notebook, not the script FixFinder checked it as.
        if (_launch?.ChosenFile is { } chosen && NotebookScript.Of(chosen) is { } notebook)
            note = note.Replace(Path.GetFileName(chosen), Path.GetFileName(notebook.Notebook), StringComparison.Ordinal);

        lock (_gate)
        {
            if (!_notes.Contains(note)) _notes.Add(note);
        }
    }

    /// <summary>
    /// The report's order: worst first, then by where it is - and then the findings that follow from another are put
    /// behind the one they follow from, so a reader meets the cause before the four reports of its consequences. What was
    /// found in a notebook's code is worded as the notebook is read, in cells, on its way out.
    /// </summary>
    private List<Finding> Sorted(IEnumerable<Finding> findings)
    {
        var notebook = _launch?.ChosenFile is { } chosen ? NotebookScript.Of(chosen) : null;
        var worded = notebook is null ? findings : findings.Select(finding => NotebookWording.AsInTheNotebook(finding, notebook));

        return [.. RootCauses.Link([.. worded
            .OrderBy(f => f.Severity)
            .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Line ?? 0)])];
    }

    private static async Task ForEachAsync<T>(IEnumerable<T> items, Func<T, Task> body, CancellationToken cancellationToken)
    {
        using var slots = new SemaphoreSlim(2);

        var tasks = items.Select(async item =>
        {
            await slots.WaitAsync(cancellationToken);

            try
            {
                await body(item);
            }
            finally
            {
                slots.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);
    }

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
