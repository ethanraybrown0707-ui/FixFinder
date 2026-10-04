using FixFinder.Core.Execution;
using FixFinder.Core.Execution.Versions;

namespace FixFinder.Core.Checking;

/// <summary>
/// The note for a program whose code needs a later version of its language than the one it ran with: what part of the
/// code needs which version, that no toolchain of it is on this computer - or that the project's own environment is older
/// - and how to install one. Said once, beside the errors, so they are not read as mistakes in the code.
/// </summary>
/// <remarks>
/// The language's own errors for it are of every kind - a SyntaxError for a match statement on Python 3.9, a module that
/// is not there for tomllib - so the note does not wait for any one of them: it is given when the run or the build had
/// errors and the version that ran is older than the code needs.
/// </remarks>
public static class ToolchainVersionErrors
{
    /// <summary>The note for the program the chosen file is part of, or null when it ran with a version new enough for its code.</summary>
    public static string? NoteFor(string chosen)
    {
        switch (Path.GetExtension(chosen).ToLowerInvariant())
        {
            case ".py" or ".pyw":
            {
                var windowed = chosen.EndsWith(".pyw", StringComparison.OrdinalIgnoreCase);
                var environment = PythonEnvironment.For(ProgramCopy.OriginalOf(chosen), windowed);
                if (PythonSetup.For(chosen, environment, windowed) is not { Toolchain: { } used } || PythonFeaturesUsed.For(chosen) is not { } needs) return null;

                return Note("Python", used, needs, Pythons.Installed, environment is not null, $"for example:\n  winget install Python.Python.3.{Math.Max(needs.Version.Minor, 14)}");
            }

            case ".go":
            {
                if (GoSetup.For(chosen) is not { Toolchain: var used } || GoFeaturesUsed.For(chosen) is not { } needs) return null;

                // A Go older than the module's go line does not build it at all, which GoVersionErrors says from what go said.
                if (DeclaredGo.Of(ProgramCopy.OriginalOf(chosen))?.AtLeast.Any(declared => used.Version < declared.Version) == true) return null;

                return Note("Go", used, needs, GoToolchains.Installed, projectsOwn: false, GoVersionErrors.InstallAdvice, built: true);
            }

            default:
                return null;
        }
    }

    /// <summary>The words of the note: what needs which version, what ran, and what to do about it.</summary>
    /// <param name="install">How to install a later version, after "Installing Python 3.12 or later runs it - ".</param>
    /// <param name="built">Whether the language builds the program before it runs, so it is the build that fails.</param>
    internal static string? Note(
        string language, VersionedToolchain used, ToolchainChoice.AtLeast needs, IReadOnlyList<VersionedToolchain> installed, bool projectsOwn, string install,
        bool built = false)
    {
        if (used.Version >= needs.Version) return null;

        // It starts with the file's name, which keeps its own case.
        var because = needs.Because;
        var (cannot, makesItWork) = built ? ("cannot be built", "builds it") : ("cannot run", "runs it");

        if (projectsOwn)
        {
            return $"{because} - and the project's own environment is {language} {used.VersionText}, so that part of it {cannot} there, which is " +
                   $"not a mistake in the code. Making the environment again with {language} {needs.Version} or later {makesItWork}.";
        }

        if (installed.FirstOrDefault(toolchain => toolchain.Version >= needs.Version) is { } newer)
        {
            return $"{because} - and it ran with {language} {used.VersionText} ({used.FoundIn}), as what its project declares rules out " +
                   $"{language} {newer.VersionText} ({newer.FoundIn}), so that part of it {cannot}, which is not a mistake in the code.";
        }

        // The newest here may not be the one that ran - the usual one runs when none is new enough.
        var newest = installed.Count > 0 && installed[0].Version > used.Version ? installed[0] : null;
        var whatIsHere = newest is null
            ? $"{language} {used.VersionText} ({used.FoundIn}) is the newest {language} on this computer"
            : $"it ran with {language} {used.VersionText} ({used.FoundIn}), and the newest {language} on this computer is {newest.VersionText} ({newest.FoundIn})";

        return $"{because} - and {whatIsHere}, so that part of it {cannot}, which is not a mistake in the code. " +
               $"Installing {language} {needs.Version} or later {makesItWork} - {install}";
    }
}
