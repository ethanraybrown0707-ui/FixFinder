using FixFinder.Core.Engine;

// Ctrl+C stops the check and whatever program it was running, rather than leaving that program behind.
using var stopping = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stopping.Cancel();
};

try
{
    return await CommandLine.RunAsync(args, Console.Out, Console.Error, stopping.Token);
}
catch (OperationCanceledException)
{
    await Console.Error.WriteLineAsync("fixfinder: stopped");
    return CommandLine.CouldNotCheck;
}
