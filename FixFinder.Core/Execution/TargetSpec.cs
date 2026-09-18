using System.Text;

namespace FixFinder.Core.Execution;

/// <summary>
/// Everything needed to launch the program under investigation, exactly as the user confirmed
/// it in step 1 of the window.
/// </summary>
/// <remarks>
/// Immutable on purpose: the same spec is used to run the target before a patch and again after
/// it, and verify-by-rerun only means anything if the second invocation is identical to the
/// first. See <c>FixVerifier</c>.
/// </remarks>
public sealed class TargetSpec
{
    /// <summary>The program to run - an executable, or a managed .dll when <see cref="LaunchViaDotnet"/> is set.</summary>
    public required string ExecutablePath { get; init; }

    public string Arguments { get; init; } = "";

    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Launch as <c>dotnet "&lt;path&gt;"</c> rather than executing the file directly.
    /// </summary>
    /// <remarks>
    /// Required for a managed .dll, which is not directly executable. Also the workaround for
    /// this machine's Application Control policy, which blocks a freshly-built, unsigned
    /// binary under the user profile with 0x800711C7 while trusting dotnet.exe.
    /// </remarks>
    public bool LaunchViaDotnet { get; init; }

    /// <summary>Variables added to (not replacing) the inherited environment.</summary>
    public IReadOnlyDictionary<string, string> ExtraEnvironment { get; init; }
        = new Dictionary<string, string>();

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How to decode the target's stdout/stderr bytes.
    /// </summary>
    /// <remarks>
    /// UTF-8 is right far more often than not, but plenty of Windows console programs still
    /// emit the OEM codepage (cp437/cp850), which decodes into U+FFFD replacement characters.
    /// <see cref="TargetRunner"/> counts those and warns rather than guessing - auto-detecting
    /// an encoding from a few hundred bytes of error text is exactly the kind of silent wrong
    /// answer this tool is supposed to avoid.
    /// </remarks>
    public Encoding OutputEncoding { get; init; } = Encoding.UTF8;

    /// <summary>What to type into the program when it asks, one answer per line. Null to type nothing.</summary>
    /// <remarks>
    /// Without it the program reads end-of-input the moment it asks, which for a Python script is
    /// <c>EOFError: EOF when reading a line</c> at its first <c>input()</c> - long before it reaches
    /// whatever else is wrong. It belongs to the spec rather than to one run, so every re-run of the
    /// program is typed the same answers.
    /// </remarks>
    public string? StandardInput { get; init; }

    /// <summary>This spec, with <paramref name="input"/> typed into the program.</summary>
    public TargetSpec WithInput(string? input) => Copy(Arguments, string.IsNullOrEmpty(input) ? null : input);

    /// <summary>
    /// This spec, with <paramref name="programArguments"/> passed to the program itself - what it reads
    /// as <c>sys.argv</c>, <c>args</c> or <c>argv</c>.
    /// </summary>
    /// <remarks>
    /// Added after everything FixFinder put on the command line, and exactly as typed, the way they
    /// would be typed after the program's name in a terminal. The one place order is not enough is
    /// <c>dotnet run</c>, which reads arguments as its own until it sees <c>--</c>: without it,
    /// <c>dotnet run app.cs --verbose</c> is dotnet being asked to be verbose.
    /// </remarks>
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

    /// <summary>This spec, with a different time limit - shorter, for runs that might loop forever.</summary>
    public TargetSpec WithTimeout(TimeSpan timeout) => Copy(Arguments, StandardInput, timeout);

    /// <summary>This spec, with more variables set in the program's environment.</summary>
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

    /// <summary>The command line as a user would type it, for logs and confirmation prompts.</summary>
    public string DisplayCommandLine
    {
        get
        {
            var head = LaunchViaDotnet ? $"dotnet \"{ExecutablePath}\"" : $"\"{ExecutablePath}\"";
            return Arguments.Length > 0 ? $"{head} {Arguments}" : head;
        }
    }
}
