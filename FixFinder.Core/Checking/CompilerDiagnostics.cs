using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.Checking;

/// <summary>Everything a compiler or parser said about a program, without running it.</summary>
public sealed record CompilerReport(
    bool Checked,
    IReadOnlyList<ParsedError> Errors,
    IReadOnlyList<ParsedError> Warnings,
    IReadOnlyList<CapturedLine> Output,
    TargetRunResult? Build = null,
    string? Problem = null)
{
    public bool Failed => Errors.Count > 0 || Build is { Outcome: not RunOutcome.ExitedClean };

    public static CompilerReport NotChecked(string? problem = null) => new(false, [], [], [], Problem: problem);
}

/// <summary>Asks each language's own compiler or parser for every error and warning in a program.</summary>
public static partial class CompilerDiagnostics
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    private const string PythonChecker = """
        import sys, warnings, traceback
        for path in sys.argv[1:]:
            print("@@FILE@@ " + path, file=sys.stderr, flush=True)
            try:
                with open(path, "rb") as handle:
                    source = handle.read()
            except OSError as problem:
                continue
            with warnings.catch_warnings(record=True) as caught:
                warnings.simplefilter("always")
                try:
                    compile(source, path, "exec", dont_inherit=True)
                except (SyntaxError, ValueError) as error:
                    sys.stderr.write("".join(traceback.format_exception_only(type(error), error)))
            for warning in caught:
                print("@@WARNING@@ %s|%s|%s" % (warning.lineno, warning.category.__name__, warning.message), file=sys.stderr)
            sys.stderr.flush()
        """;

    [GeneratedRegex(@"^@@WARNING@@ (?<line>\d+)\|(?<type>\w+)\|(?<message>.*)$")]
    private static partial Regex PythonWarning();

    public static async Task<CompilerReport> CollectAsync(
        LaunchPlan launch,
        IReadOnlyList<string> files,
        CodeLanguage language,
        Action<CapturedLine>? lineCaptured = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (launch.ChosenFile is not { } chosen) return CompilerReport.NotChecked();

        var extension = Path.GetExtension(chosen).ToLowerInvariant();

        if (launch.Compile is { } compile) return await BuildAsync(compile, launch, language, lineCaptured, log, cancellationToken);

        return extension switch
        {
            ".py" or ".pyw" => await PythonAsync(launch, files, log, cancellationToken),
            ".js" or ".mjs" or ".cjs" => await JavaScriptAsync(files, log, cancellationToken),
            ".cs" => await CSharpAsync(chosen, log, cancellationToken),
            ".go" => await GoAsync(chosen, log, cancellationToken),
            _ => CompilerReport.NotChecked(),
        };
    }

    private static async Task<CompilerReport> BuildAsync(
        TargetSpec compile, LaunchPlan launch, CodeLanguage language, Action<CapturedLine>? lineCaptured, Action<string>? log, CancellationToken cancellationToken)
    {
        var registry = language.Parsers();
        var runner = new TargetRunner(registry);

        void Relay(string message) => log?.Invoke(message);
        void Forward(CapturedLine line) => lineCaptured?.Invoke(line);

        runner.Log += Relay;
        runner.LineCaptured += Forward;

        TargetRunResult build;

        try
        {
            build = await runner.RunAsync(compile, cancellationToken);
        }
        finally
        {
            runner.Log -= Relay;
            runner.LineCaptured -= Forward;
        }

        if (build.Outcome == RunOutcome.LaunchFailed)
            return new CompilerReport(false, [], [], build.Lines, build, build.LaunchError ?? "The compiler could not be started.");

        var folder = compile.WorkingDirectory;
        var errors = Resolved(Errors(registry, build.Lines), folder);
        var warnings = Resolved(WarningsIn(build.Lines), folder);

        return new CompilerReport(true, errors, warnings, build.Lines, build);
    }

    private static async Task<CompilerReport> PythonAsync(LaunchPlan launch, IReadOnlyList<string> files, Action<string>? log, CancellationToken cancellationToken)
    {
        var interpreter = launch.Spec is { } spec && Path.GetFileNameWithoutExtension(spec.ExecutablePath).StartsWith("py", StringComparison.OrdinalIgnoreCase)
            ? spec.ExecutablePath
            : TargetFactory.FindOnPath("python") ?? TargetFactory.FindOnPath("py");

        if (interpreter is null) return CompilerReport.NotChecked("Python is not installed, so the code could not be checked.");

        var script = Path.Combine(Path.GetTempPath(), "FixFinder-check", $"syntax-{Guid.NewGuid():N}.py");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(script)!);
            await File.WriteAllTextAsync(script, PythonChecker, new UTF8Encoding(false), cancellationToken);

            var run = await RunAsync(new TargetSpec
            {
                ExecutablePath = interpreter,
                Arguments = $"-X utf8 \"{script}\" " + string.Join(" ", files.Select(f => $"\"{f}\"")),
                WorkingDirectory = Path.GetDirectoryName(files[0])!,
                Timeout = Timeout,
            }, log, cancellationToken);

            if (run.Outcome is RunOutcome.LaunchFailed) return CompilerReport.NotChecked(run.LaunchError);

            var errors = new List<ParsedError>();
            var warnings = new List<ParsedError>();
            var parser = new PythonTracebackParser();

            foreach (var (file, chunk) in ByFile(run.Lines))
            {
                var problem = chunk.Where(l => !l.Text.StartsWith("@@WARNING@@", StringComparison.Ordinal)).ToList();

                if (problem.Count > 0 && parser.Parse(problem) is { } error) errors.Add(error);

                foreach (var line in chunk)
                {
                    if (PythonWarning().Match(line.Text) is not { Success: true } warning) continue;

                    var frame = new ErrorFrame { Order = 0, File = file, Line = int.Parse(warning.Groups["line"].Value), RawLine = line.Text, Origin = FrameOrigin.FirstParty };

                    warnings.Add(new ParsedError
                    {
                        LanguageId = "python",
                        Confidence = 90,
                        RawText = line.Text,
                        FirstLineSequence = line.Sequence,
                        ExceptionType = warning.Groups["type"].Value,
                        Message = warning.Groups["message"].Value,
                        Frames = [frame],
                        CulpritFrame = frame,
                    });
                }
            }

            return new CompilerReport(true, errors, warnings, run.Lines);
        }
        finally
        {
            try { File.Delete(script); } catch (IOException) { }
        }
    }

    private static IEnumerable<(string File, List<CapturedLine> Lines)> ByFile(IReadOnlyList<CapturedLine> lines)
    {
        string? file = null;
        var chunk = new List<CapturedLine>();

        foreach (var line in lines)
        {
            if (line.Text.StartsWith("@@FILE@@ ", StringComparison.Ordinal))
            {
                if (file is not null) yield return (file, chunk);

                file = line.Text["@@FILE@@ ".Length..].Trim();
                chunk = [];
                continue;
            }

            chunk.Add(line);
        }

        if (file is not null) yield return (file, chunk);
    }

    private static async Task<CompilerReport> JavaScriptAsync(IReadOnlyList<string> files, Action<string>? log, CancellationToken cancellationToken)
    {
        if (TargetFactory.FindOnPath("node") is not { } node) return CompilerReport.NotChecked("Node.js is not installed, so the code could not be checked.");

        var errors = new List<ParsedError>();
        var output = new List<CapturedLine>();
        var registry = new ParserRegistry(new ParserRegistry().Parsers.Where(p => p.LanguageId == "node"));

        foreach (var file in files)
        {
            var run = await RunAsync(new TargetSpec
            {
                ExecutablePath = node,
                Arguments = $"--check \"{file}\"",
                WorkingDirectory = Path.GetDirectoryName(file)!,
                Timeout = Timeout,
            }, log, cancellationToken);

            output.AddRange(run.Lines);

            if (run.Outcome != RunOutcome.ExitedClean && registry.Parse(run.Lines) is { } error) errors.Add(error);
        }

        return new CompilerReport(true, errors, [], output);
    }

    private static async Task<CompilerReport> CSharpAsync(string chosen, Action<string>? log, CancellationToken cancellationToken)
    {
        if (TargetFactory.FindOnPath("dotnet") is not { } dotnet) return CompilerReport.NotChecked("The .NET SDK is not installed, so the code could not be checked.");

        var project = ProgramLayout.CSharpProject(chosen);
        var target = project ?? chosen;

        var run = await RunAsync(new TargetSpec
        {
            ExecutablePath = dotnet,
            Arguments = $"build \"{target}\" -nologo -v q -clp:NoSummary",
            WorkingDirectory = Path.GetDirectoryName(target)!,
            Timeout = Timeout,
        }, log, cancellationToken);

        if (run.Outcome is RunOutcome.LaunchFailed) return CompilerReport.NotChecked(run.LaunchError);

        var errors = Distinct(new MsvcParser().ParseAll(run.Lines));
        var warnings = Distinct(MsvcParser.ParseWarnings(run.Lines));

        if (errors.Count == 0 && run.ExitCode != 0)
            return new CompilerReport(false, [], warnings, run.Lines, Problem: "dotnet build failed without reporting a compiler error, so the code is checked by running it instead.");

        return new CompilerReport(true, errors, warnings, run.Lines);
    }

    private static async Task<CompilerReport> GoAsync(string chosen, Action<string>? log, CancellationToken cancellationToken)
    {
        if (TargetFactory.FindOnPath("go") is not { } go) return CompilerReport.NotChecked("Go is not installed, so the code could not be checked.");

        var program = ProgramLayout.GoPackageOf(chosen);
        var folder = program.Module ?? Path.GetDirectoryName(chosen)!;
        var targets = program.Module is not null ? "." : string.Join(" ", program.Files.Select(f => $"\"{f}\""));
        var binary = Path.Combine(Path.GetTempPath(), "FixFinder-check", $"go-{Guid.NewGuid():N}.exe");

        try
        {
            var build = await RunAsync(new TargetSpec
            {
                ExecutablePath = go,
                Arguments = $"build -o \"{binary}\" {targets}",
                WorkingDirectory = folder,
                Timeout = Timeout,
            }, log, cancellationToken);

            if (build.Outcome is RunOutcome.LaunchFailed) return CompilerReport.NotChecked(build.LaunchError);

            var errors = Resolved(new GoCompileParser().ParseAll(build.Lines), folder);
            if (errors.Count > 0 || build.ExitCode != 0) return new CompilerReport(true, errors, [], build.Lines);

            var vet = await RunAsync(new TargetSpec
            {
                ExecutablePath = go,
                Arguments = $"vet {targets}",
                WorkingDirectory = folder,
                Timeout = Timeout,
            }, log, cancellationToken);

            var warnings = Resolved(new GoCompileParser().ParseAll(vet.Lines), folder)
                .Select(AsWarning)
                .ToList();

            return new CompilerReport(true, [], warnings, [.. build.Lines, .. vet.Lines]);
        }
        finally
        {
            try { File.Delete(binary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static ParsedError AsWarning(ParsedError error) => new()
    {
        LanguageId = error.LanguageId,
        Confidence = error.Confidence,
        RawText = error.RawText,
        FirstLineSequence = error.FirstLineSequence,
        ExceptionType = "compile warning",
        ErrorCode = error.ErrorCode,
        Message = error.Message,
        Frames = error.Frames,
        CulpritFrame = error.CulpritFrame,
    };

    private static IReadOnlyList<ParsedError> Errors(ParserRegistry registry, IReadOnlyList<CapturedLine> lines)
    {
        if (registry.Parse(lines) is not { } first || first.LanguageId == "generic") return [];

        return [first, .. registry.Others(first, lines)];
    }

    private static IReadOnlyList<ParsedError> WarningsIn(IReadOnlyList<CapturedLine> lines) =>
        Distinct([.. GccClangParser.ParseWarnings(lines), .. MsvcParser.ParseWarnings(lines), .. JavaStackTraceParser.ParseWarnings(lines)]);

    private static List<ParsedError> Distinct(IEnumerable<ParsedError> errors) =>
        errors
            .GroupBy(e => $"{e.ErrorCode}|{e.Message}|{e.Frames.FirstOrDefault()?.File}|{e.Frames.FirstOrDefault()?.Line}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

    private static List<ParsedError> Resolved(IEnumerable<ParsedError> errors, string folder) =>
        errors.Select(error => WithFullPaths(error, folder)).ToList();

    private static ParsedError WithFullPaths(ParsedError error, string folder)
    {
        if (error.Frames.All(f => f.File is null || Path.IsPathRooted(f.File))) return error;

        ErrorFrame Full(ErrorFrame frame) => frame.File is { Length: > 0 } file && !Path.IsPathRooted(file)
            ? new ErrorFrame
            {
                Order = frame.Order, Symbol = frame.Symbol, File = Path.GetFullPath(Path.Combine(folder, file)), Line = frame.Line,
                Column = frame.Column, Module = frame.Module, Origin = frame.Origin, RawLine = frame.RawLine,
            }
            : frame;

        var frames = error.Frames.Select(Full).ToList();
        var culpritIndex = error.CulpritFrame is null ? -1 : error.Frames.ToList().IndexOf(error.CulpritFrame);
        var culprit = culpritIndex >= 0 ? frames[culpritIndex] : error.CulpritFrame;

        return new ParsedError
        {
            LanguageId = error.LanguageId,
            Confidence = error.Confidence,
            RawText = error.RawText,
            FirstLineSequence = error.FirstLineSequence,
            ExceptionType = error.ExceptionType,
            ErrorCode = error.ErrorCode,
            Message = error.Message,
            Frames = frames,
            CulpritFrame = culprit,
            Causes = error.Causes,
        };
    }

    private static async Task<TargetRunResult> RunAsync(TargetSpec spec, Action<string>? log, CancellationToken cancellationToken)
    {
        var runner = new TargetRunner();

        void Relay(string message) => log?.Invoke(message);

        runner.Log += Relay;

        try
        {
            return await runner.RunAsync(spec, cancellationToken);
        }
        finally
        {
            runner.Log -= Relay;
        }
    }
}
