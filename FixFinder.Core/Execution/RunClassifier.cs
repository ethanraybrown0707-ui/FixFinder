namespace FixFinder.Core.Execution;

/// <summary>Decides whether a finished run counts as a crash.</summary>
public static class RunClassifier
{
    private static readonly Dictionary<int, string> KnownCrashExitCodes = new()
    {
        [unchecked((int)0xC0000005)] = "access violation (0xC0000005)",
        [unchecked((int)0xC0000409)] = "stack buffer overrun (0xC0000409)",
        [unchecked((int)0xC000001D)] = "illegal instruction (0xC000001D)",
        [unchecked((int)0xC00000FD)] = "stack overflow (0xC00000FD)",
        [unchecked((int)0xC0000094)] = "integer divide by zero (0xC0000094)",
        [unchecked((int)0xC0000095)] = "integer overflow (0xC0000095)",
        [unchecked((int)0xC000008E)] = "floating-point divide by zero (0xC000008E)",
        [unchecked((int)0xC0000374)] = "heap corruption (0xC0000374)",
        [unchecked((int)0xC0000417)] = "invalid argument to a C runtime function (0xC0000417)",
        [unchecked((int)0xC0000420)] = "assertion failure (0xC0000420)",
        [unchecked((int)0xE0434352)] = "unhandled .NET exception (0xE0434352)",
        [unchecked((int)0x80000003)] = "breakpoint / assertion (0x80000003)",
        [134] = "SIGABRT (134)",
        [139] = "segmentation fault (139)",
        [101] = "Rust panic (101)",
    };

    public static (RunOutcome Outcome, string Explanation) Classify(int exitCode, bool hasParsedError)
    {
        if (exitCode == 0)
        {
            return hasParsedError
                ? (RunOutcome.ExitedNonZero, "Exited 0, but an error was found in the output - the program reported a failure and still exited successfully.")
                : (RunOutcome.ExitedClean, "Exited 0 with no error in the output - nothing to search for.");
        }

        if (hasParsedError)
            return (RunOutcome.Crashed, $"Exited {exitCode} with an error in the output.");

        if (KnownCrashExitCodes.TryGetValue(exitCode, out var meaning))
            return (RunOutcome.Crashed,
                $"Exited {exitCode} - {meaning}. Nothing in the output parsed as a stack trace.");

        return (RunOutcome.ExitedNonZero,
            $"Exited {exitCode}, but nothing in the output parsed as an error. This is usually a handled failure, not a crash.");
    }
}
