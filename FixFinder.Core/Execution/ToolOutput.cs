namespace FixFinder.Core.Execution;

/// <summary>What a tool FixFinder asks something of - Python, Node, javap, go doc, Visual Studio's setup script - printed.</summary>
internal static class ToolOutput
{
    /// <summary>
    /// How long, once a tool has exited, what it printed is waited for. Nothing more is written by then: finishing the read
    /// only needs a thread, and a machine short of threads - a test runner doing a great deal at once - can take many seconds
    /// to give it one. A wait of a few seconds made a tool that had answered look as though it had said nothing, and the
    /// fix that needed its answer was missed.
    /// </summary>
    public static readonly TimeSpan AfterExit = TimeSpan.FromMinutes(1);
}
