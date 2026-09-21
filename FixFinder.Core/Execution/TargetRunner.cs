using System.ComponentModel;
using System.Diagnostics;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Execution;

/// <summary>Launches the target program and captures everything it writes to stdout and stderr.</summary>
public sealed class TargetRunner
{
    public event Action<CapturedLine>? LineCaptured;

    public event Action<string>? Log;

    // A grandchild process that inherited the pipes can hold them open long after the program itself exits.
    private static readonly TimeSpan StreamCloseGrace = TimeSpan.FromSeconds(5);

    private const double ReplacementCharWarningRatio = 0.005;

    private readonly ParserRegistry _parsers;

    public TargetRunner(ParserRegistry? parsers = null) => _parsers = parsers ?? new ParserRegistry();

    public IReadOnlyList<string> SourceRoots { get; set; } = [];

    public async Task<TargetRunResult> RunAsync(TargetSpec spec, CancellationToken cancellationToken)
    {
        var lines = new List<CapturedLine>();
        var linesLock = new object();
        var stopwatch = Stopwatch.StartNew();
        var sequence = 0;

        var stdOutClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdErrClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Receive(DataReceivedEventArgs e, StreamKind kind, TaskCompletionSource<bool> closed)
        {
            if (e.Data is null)
            {
                closed.TrySetResult(true);
                return;
            }

            var line = new CapturedLine(Interlocked.Increment(ref sequence), kind, e.Data, stopwatch.Elapsed);
            lock (linesLock) lines.Add(line);
            LineCaptured?.Invoke(line);
        }

        using var process = new Process { StartInfo = BuildStartInfo(spec), EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) => Receive(e, StreamKind.StdOut, stdOutClosed);
        process.ErrorDataReceived += (_, e) => Receive(e, StreamKind.StdErr, stdErrClosed);

        Log?.Invoke($"Launching: {spec.DisplayCommandLine}");
        Log?.Invoke($"Working directory: {spec.WorkingDirectory}");

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            stopwatch.Stop();
            var reason = DescribeLaunchFailure(ex, spec);
            Log?.Invoke($"Launch failed: {reason}");

            return new TargetRunResult
            {
                Outcome = RunOutcome.LaunchFailed,
                Lines = [],
                Duration = stopwatch.Elapsed,
                LaunchError = reason,
                Explanation = reason,
            };
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            if (spec.StandardInput is { Length: > 0 } typed)
            {
                var text = typed.ReplaceLineEndings("\n");
                await process.StandardInput.WriteAsync(text.EndsWith('\n') ? text : text + "\n");
                await process.StandardInput.FlushAsync();
            }

            // Closed so a program that reads input gets end-of-file instead of waiting until the timeout.
            process.StandardInput.Close();
        }
        catch (IOException)
        {
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(spec.Timeout);

        var stoppedByUser = false;
        var timedOut = false;
        int? exitCode = null;

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            exitCode = process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            stoppedByUser = cancellationToken.IsCancellationRequested;
            timedOut = !stoppedByUser;

            Log?.Invoke(stoppedByUser
                ? "Stopped by you - killing the process tree."
                : $"Timed out after {spec.Timeout.TotalSeconds:0.#}s - killing the process tree.");

            KillTree(process);
        }
        finally
        {
            KillTree(process);
        }

        var streamsClosedCleanly = true;
        try
        {
            await Task.WhenAll(stdOutClosed.Task, stdErrClosed.Task)
                      .WaitAsync(StreamCloseGrace)
                      .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            streamsClosedCleanly = false;
            Log?.Invoke(
                "The output streams did not close within 5s of the process exiting. A grandchild " +
                "process probably inherited the pipes and is still holding them; captured output " +
                "may be incomplete.");
        }

        stopwatch.Stop();

        CapturedLine[] captured;
        lock (linesLock) captured = [.. lines.OrderBy(l => l.Sequence)];

        var encodingWarning = DetectEncodingProblem(captured, spec);
        if (encodingWarning is not null) Log?.Invoke(encodingWarning);

        var parsed = _parsers.Parse(captured, SourceRoots);
        if (parsed is not null)
            Log?.Invoke($"Detected: {parsed.LanguageId} · {parsed.Summary} · confidence {parsed.Confidence}");

        var (outcome, explanation) = Summarise(exitCode, stoppedByUser, timedOut, spec, stopwatch.Elapsed, parsed);
        Log?.Invoke(explanation);

        return new TargetRunResult
        {
            Outcome = outcome,
            ExitCode = exitCode,
            Lines = captured,
            Duration = stopwatch.Elapsed,
            OutputStreamsClosedCleanly = streamsClosedCleanly,
            EncodingWarning = encodingWarning,
            Error = parsed,
            Explanation = explanation,
        };
    }

    private static ProcessStartInfo BuildStartInfo(TargetSpec spec)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false),
            StandardOutputEncoding = spec.OutputEncoding,
            StandardErrorEncoding = spec.OutputEncoding,
            WorkingDirectory = spec.WorkingDirectory,
        };

        if (spec.LaunchViaDotnet)
        {
            startInfo.FileName = "dotnet";
            startInfo.Arguments = spec.Arguments.Length > 0
                ? $"\"{spec.ExecutablePath}\" {spec.Arguments}"
                : $"\"{spec.ExecutablePath}\"";
        }
        else
        {
            startInfo.FileName = spec.ExecutablePath;
            startInfo.Arguments = spec.Arguments;
        }

        foreach (var (name, value) in spec.ExtraEnvironment)
            startInfo.Environment[name] = value;

        return startInfo;
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private static string DescribeLaunchFailure(Exception ex, TargetSpec spec)
    {
        if (ex is Win32Exception { NativeErrorCode: 4551 })
        {
            return $"An Application Control policy blocked '{spec.ExecutablePath}' (0x800711C7). " +
                   "If this is a managed .dll or a freshly-built unsigned .exe, tick " +
                   "\"Launch through the dotnet host\" in step 1, or sign it first.";
        }

        return $"Could not start '{spec.ExecutablePath}': {ex.Message}";
    }

    private static string? DetectEncodingProblem(IReadOnlyList<CapturedLine> lines, TargetSpec spec)
    {
        var total = 0;
        var replacements = 0;

        foreach (var line in lines)
        {
            total += line.Text.Length;
            foreach (var c in line.Text)
                if (c == (char)0xFFFD) replacements++;
        }

        if (total == 0 || replacements == 0) return null;
        if ((double)replacements / total < ReplacementCharWarningRatio) return null;

        return $"{replacements} unreadable characters in the output: it does not look like " +
               $"{spec.OutputEncoding.WebName}. Many Windows console programs write the OEM codepage " +
               "instead. The error text may be garbled, and search results with it.";
    }

    private static (RunOutcome, string) Summarise(
        int? exitCode, bool stoppedByUser, bool timedOut, TargetSpec spec, TimeSpan duration, ParsedError? parsed)
    {
        if (stoppedByUser)
            return (RunOutcome.Cancelled, $"Stopped by you after {duration.TotalSeconds:0.#}s.");

        if (timedOut)
            return (RunOutcome.TimedOut,
                $"Still running after {spec.Timeout.TotalSeconds:0.#}s and was killed. If this program is " +
                "meant to keep running, raise the timeout - a crash it prints before then is still captured.");

        var confident = parsed is not null && parsed.Confidence >= 30;

        var (outcome, explanation) = RunClassifier.Classify(exitCode!.Value, confident);
        return (outcome, $"{explanation} ({duration.TotalSeconds:0.#}s)");
    }
}
