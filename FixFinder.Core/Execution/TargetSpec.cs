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
    /// Command run through the shell after a patch is applied and before the target is re-run.
    /// Null for interpreted languages, effectively mandatory for compiled ones - the binary
    /// that was launched is stale the moment the source changes.
    /// </summary>
    public string? BuildCommand { get; init; }

    public string? BuildWorkingDirectory { get; init; }

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
