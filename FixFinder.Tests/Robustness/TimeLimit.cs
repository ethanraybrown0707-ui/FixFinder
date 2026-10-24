namespace FixFinder.Tests;

/// <summary>Waiting for work a test has handed to another thread - for a minute at most.</summary>
/// <remarks>
/// The minute is a guard against work that would never end, not a timing: CI's busy runner times nothing reliably. The work
/// is awaited rather than waited on, so no test thread sits blocked while it runs.
/// </remarks>
internal static class TimeLimit
{
    public static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    /// <summary>What the work gives back - or a failure saying what was still going after a minute.</summary>
    /// <param name="stillGoing">What was still going, as the failure puts it: "logic-x was still reading empty".</param>
    public static async Task<T> WithinAMinute<T>(Task<T> work, string stillGoing)
    {
        try
        {
            return await work.WaitAsync(OneMinute);
        }
        catch (TimeoutException) when (!work.IsCompleted)
        {
            throw new TimeoutException($"{stillGoing} after a minute");
        }
    }
}
