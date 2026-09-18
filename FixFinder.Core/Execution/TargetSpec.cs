using System.Text;

namespace FixFinder.Core.Execution;

/// <summary>Everything needed to launch the program under investigation, exactly as the user confirmed it in step 1 of the
/// window.</summary>
public sealed class TargetSpec
{
    public required string ExecutablePath { get; init; }

    public string Arguments { get; init; } = "";

    public required string WorkingDirectory { get; init; }

    public bool LaunchViaDotnet { get; init; }

    public IReadOnlyDictionary<string, string> ExtraEnvironment { get; init; }
        = new Dictionary<string, string>();

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    public Encoding OutputEncoding { get; init; } = Encoding.UTF8;

    public string? StandardInput { get; init; }

    public TargetSpec WithInput(string? input) => Copy(Arguments, string.IsNullOrEmpty(input) ? null : input);

    public TargetSpec WithArguments(string? programArguments)
    {
        var extra = programArguments?.Trim() ?? "";
        if (extra.Length == 0) return this;

        var separator = !LaunchViaDotnet &&
                        string.Equals(Path.GetFileNameWithoutExtension(ExecutablePath), "dotnet", StringComparison.OrdinalIgnoreCase) &&
                        Arguments.StartsWith("run", StringComparison.Ordinal)
            ? " -- "
            : " ";

        return Copy(Arguments.Length > 0 ? Arguments + separator + extra : extra, StandardInput);
    }

    public TargetSpec WithTimeout(TimeSpan timeout) => Copy(Arguments, StandardInput, timeout);

    public TargetSpec WithEnvironment(IReadOnlyDictionary<string, string> extra) =>
        Copy(Arguments, StandardInput, environment: ExtraEnvironment.Concat(extra).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value));

    private TargetSpec Copy(string arguments, string? standardInput, TimeSpan? timeout = null, IReadOnlyDictionary<string, string>? environment = null) => new()
    {
        ExecutablePath = ExecutablePath,
        Arguments = arguments,
        WorkingDirectory = WorkingDirectory,
        LaunchViaDotnet = LaunchViaDotnet,
        ExtraEnvironment = environment ?? ExtraEnvironment,
        Timeout = timeout ?? Timeout,
        OutputEncoding = OutputEncoding,
        StandardInput = standardInput,
    };

    public string DisplayCommandLine
    {
        get
        {
            var head = LaunchViaDotnet ? $"dotnet \"{ExecutablePath}\"" : $"\"{ExecutablePath}\"";
            return Arguments.Length > 0 ? $"{head} {Arguments}" : head;
        }
    }
}
