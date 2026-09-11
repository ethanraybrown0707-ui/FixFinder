namespace FixFinder.Core.Execution;

/// <summary>
/// Decides whether a finished run counts as a crash.
/// </summary>
/// <remarks>
/// The governing rule, and the reason this is its own class: <b>a parsed stack trace is the
/// primary signal and the exit code is only corroboration.</b> Exit codes lie in both
/// directions. Test runners and plenty of frameworks print a full traceback and then exit 0;
/// command-line tools exit non-zero for ordinary usage mistakes that are not crashes at all.
/// Trusting the code over the trace would mislabel both.
/// </remarks>
public static class RunClassifier
{
    /// <summary>
    /// Exit codes that mean a fatal fault on their own, used only when nothing in the output
    /// parsed as an error.
    /// </summary>
    /// <remarks>
    /// The Windows entries are NTSTATUS values as the OS reports them through a signed int.
    /// The 128+signal entries show up when the target runs under a POSIX-ish shell.
    /// <para>
    /// Deliberately absent: <b>2</b>. A Go panic does exit with 2, but so does almost every
    /// command-line program with a usage error, and this table is consulted precisely when
    /// there is no trace to confirm what happened. Including it would relabel a mistyped
    /// argument as a crash and send FixFinder searching for a fix that does not exist. A real
    /// Go panic prints "panic:" to stderr, which the Go parser recognises in M2, so nothing is
    /// lost by leaving it out.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<int, string> KnownCrashExitCodes = new()
    {
        [unchecked((int)0xC0000005)] = "access violation (0xC0000005)",
        [unchecked((int)0xC0000409)] = "stack buffer overrun (0xC0000409)",
        [unchecked((int)0xC000001D)] = "illegal instruction (0xC000001D)",
        [unchecked((int)0xC00000FD)] = "stack overflow (0xC00000FD)",
        [unchecked((int)0xE0434352)] = "unhandled .NET exception (0xE0434352)",
        [unchecked((int)0x80000003)] = "breakpoint / assertion (0x80000003)",
        [134] = "SIGABRT (134)",
        [139] = "segmentation fault (139)",
        [101] = "Rust panic (101)",
    };

    /// <summary>
    /// Classifies a process that started and finished on its own.
    /// </summary>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="hasParsedError">
    /// Whether a stack-trace parser recognised an error in the captured output. Always false
    /// until M2 wires the parsers in; the reasoning below is written for both cases so that
    /// turning it on does not change how anything else behaves.
    /// </param>
    public static (RunOutcome Outcome, string Explanation) Classify(int exitCode, bool hasParsedError)
    {
        if (exitCode == 0)
        {
            return hasParsedError
                // Common enough to call out by name rather than quietly reclassify: the program
                // reported a failure in its output and then told the OS everything was fine.
                ? (RunOutcome.ExitedNonZero, "Exited 0, but an error was found in the output - the program reported a failure and still exited successfully.")
                : (RunOutcome.ExitedClean, "Exited 0 with no error in the output - nothing to search for.");
        }

        if (hasParsedError)
            return (RunOutcome.Crashed, $"Exited {exitCode} with an error in the output.");

        if (KnownCrashExitCodes.TryGetValue(exitCode, out var meaning))
            // Careful with the wording: this branch means nothing in the output *parsed* as an
            // error, which is not the same as nothing having been printed. Saying "no stack
            // trace was printed" would be flatly wrong whenever a parser is missing or fails.
            return (RunOutcome.Crashed,
                $"Exited {exitCode} - {meaning}. Nothing in the output parsed as a stack trace.");

        return (RunOutcome.ExitedNonZero,
            $"Exited {exitCode}, but nothing in the output parsed as an error. This is usually a handled failure, not a crash.");
    }
}
