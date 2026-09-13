using System.ComponentModel;
using System.Diagnostics;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Execution;

/// <summary>
/// Launches the target program and captures everything it writes to stdout and stderr.
/// </summary>
/// <remarks>
/// This is the piece with the most ways to go quietly wrong, so the ordering below is
/// deliberate rather than incidental - see the comments in <see cref="RunAsync"/>. The two
/// classic failures it is built to avoid are the redirected-pipe deadlock (reading one stream
/// to completion while the other fills its buffer) and the invisible hang of a program blocked
/// on <c>Console.ReadLine()</c>.
/// </remarks>
public sealed class TargetRunner
{
    /// <summary>Raised on a thread-pool thread for every line, as it arrives.</summary>
    /// <remarks>The GUI must marshal this onto the UI thread; the run log does not need to.</remarks>
    public event Action<CapturedLine>? LineCaptured;

    /// <summary>Progress and diagnostics about the run itself, not output from the target.</summary>
    public event Action<string>? Log;

    /// <summary>
    /// How long to wait after the process exits for its output pipes to close.
    /// </summary>
    /// <remarks>
    /// Needed because a grandchild that inherited the pipe handles keeps them open after the
    /// child is gone, and the end-of-stream sentinel then never arrives. Without this guard the
    /// run would hang forever on a program that spawns a background helper - which is a very
    /// ordinary thing for a program to do.
    /// </remarks>
    private static readonly TimeSpan StreamCloseGrace = TimeSpan.FromSeconds(5);

    /// <summary>Fraction of decoded characters that may be U+FFFD before we suspect the codepage.</summary>
    private const double ReplacementCharWarningRatio = 0.005;

    private readonly ParserRegistry _parsers;

    public TargetRunner(ParserRegistry? parsers = null) => _parsers = parsers ?? new ParserRegistry();

    /// <summary>
    /// Folders holding the user's own code, used to tell their frames from a library's.
    /// </summary>
    /// <remarks>
    /// Optional, and the runner works without it - but until it is set, every frame outside a
    /// vendor directory reads as "origin unknown", and FixFinder cannot tell the user that the
    /// crash is in their own code and that searching is unlikely to help.
    /// </remarks>
    public IReadOnlyList<string> SourceRoots { get; set; } = [];

    public async Task<TargetRunResult> RunAsync(TargetSpec spec, CancellationToken cancellationToken)
    {
        var lines = new List<CapturedLine>();
        var linesLock = new object();
        var stopwatch = Stopwatch.StartNew();
        var sequence = 0;

        // Completed when each stream reports end-of-stream, which Process signals by raising the
        // handler one final time with a null Data.
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

        // Subscribed BEFORE Start(). Handlers attached afterwards can miss output from a
        // program that writes and exits immediately - which is exactly what a crashing
        // program does.
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

        // Closing stdin immediately turns "waits forever for input nobody will type" into a
        // clean end-of-input the program can handle. Without it, anything that calls
        // Console.ReadLine() burns the whole timeout on every single run and looks like a hang.
        try
        {
            // Typed as a person would type it: each answer ended by Enter, and then nothing more.
            if (spec.StandardInput is { Length: > 0 } typed)
            {
                var text = typed.ReplaceLineEndings("\n");
                await process.StandardInput.WriteAsync(text.EndsWith('\n') ? text : text + "\n");
                await process.StandardInput.FlushAsync();
            }

            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The process already exited and took the pipe with it. Nothing to write or close.
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
            // Which of the two linked sources fired decides what this run means: a user
            // pressing Stop is not the same event as a program that would not finish.
            stoppedByUser = cancellationToken.IsCancellationRequested;
            timedOut = !stoppedByUser;

            Log?.Invoke(stoppedByUser
                ? "Stopped by you - killing the process tree."
                : $"Timed out after {spec.Timeout.TotalSeconds:0.#}s - killing the process tree.");

            KillTree(process);
        }
        finally
        {
            // Belt and braces for the paths that fall out of the try without a kill - an
            // exception from WaitForExitAsync that is not cancellation, for instance.
            KillTree(process);
        }

        // Only now wait on the sentinels. A process can exit while its pipes still hold
        // buffered output, and reading result.Lines before this point can miss the last few
        // lines of a stack trace - the most important lines in the entire run.
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

        // Parsed before classifying, because the parsed trace - not the exit code - is what
        // decides whether this run counts as a crash. See RunClassifier.
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
            // UseShellExecute must be false for redirection to be possible at all, and it also
            // means no console window flashes up for a console target.
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

        // Arguments are passed as one raw string rather than through ArgumentList, because the
        // user typed them as a command line in step 1 - re-quoting each whitespace-separated
        // piece would change what they meant. Only the program path, which FixFinder supplies
        // itself, gets quoted.
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

    /// <summary>Kills the process and everything it started, tolerating the race with a natural exit.</summary>
    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill. Normal, and not worth reporting.
        }
        catch (NotSupportedException)
        {
            // Killing a tree is unsupported for this process kind; the child itself is gone.
        }
        catch (Win32Exception)
        {
            // Access denied killing a descendant, e.g. one that elevated. The target is dead.
        }
    }

    private static string DescribeLaunchFailure(Exception ex, TargetSpec spec)
    {
        // 0x800711C7 arrives here as Win32Exception 4551. Naming it is worth the special case:
        // the message Windows supplies is generic, and this is the single most likely reason a
        // freshly-built binary refuses to start on this machine.
        if (ex is Win32Exception { NativeErrorCode: 4551 })
        {
            return $"An Application Control policy blocked '{spec.ExecutablePath}' (0x800711C7). " +
                   "If this is a managed .dll or a freshly-built unsigned .exe, tick " +
                   "\"Launch through the dotnet host\" in step 1, or sign it first.";
        }

        return $"Could not start '{spec.ExecutablePath}': {ex.Message}";
    }

    /// <summary>
    /// Warns when the decoded output is peppered with U+FFFD, which means the bytes were not in
    /// <see cref="TargetSpec.OutputEncoding"/>.
    /// </summary>
    /// <remarks>
    /// Detection only - it deliberately does not re-decode with a guessed codepage. Silently
    /// swapping the encoding would change the error text the search queries are built from,
    /// and a wrong guess is worse than a visible warning.
    /// </remarks>
    private static string? DetectEncodingProblem(IReadOnlyList<CapturedLine> lines, TargetSpec spec)
    {
        var total = 0;
        var replacements = 0;

        foreach (var line in lines)
        {
            total += line.Text.Length;
            // Compared as a numeric code point rather than a pasted literal, so the check cannot
            // be broken by this file itself being re-saved in the wrong encoding.
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

        // A weak read from the generic fallback is not enough to overrule a zero exit code.
        // Anything scoring below this is reported, but does not on its own turn "finished" into
        // "crashed" - which is the call that sends FixFinder off searching.
        var confident = parsed is not null && parsed.Confidence >= 30;

        var (outcome, explanation) = RunClassifier.Classify(exitCode!.Value, confident);
        return (outcome, $"{explanation} ({duration.TotalSeconds:0.#}s)");
    }
}
