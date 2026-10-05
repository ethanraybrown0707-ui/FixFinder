using FixFinder.Core.Execution.Versions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Python a program runs with, and the words that say which and why: its project's own environment when it has one,
/// as its IDE runs it; otherwise the Python a terminal runs when that is new enough for what the project declares and the
/// code needs, and otherwise the oldest Python here that is.
/// </summary>
/// <param name="Interpreter">The python.exe - or pythonw.exe, for a program with a window - that runs it.</param>
/// <param name="Explained">How a run explanation names it, with why that one: "Python 3.12.4 (installed for this user), as ...".</param>
/// <param name="CodeNeeds">What the code needs, as a sentence of its own, when nothing else said it.</param>
/// <param name="Toolchain">The Python, with its version, when its version is known.</param>
public sealed record PythonSetup(string Interpreter, string Explained, string? CodeNeeds, VersionedToolchain? Toolchain)
{
    private const string Language = "Python";

    public static PythonSetup? For(string pythonFile, PythonEnvironment.Found? environment, bool windowed = false)
    {
        // A copy made to try a fix in runs with the Python the program itself runs with: what the program's own code needs
        // decides, so a fix is tried on the version it will run on, never on a later one it would ask for itself.
        var codeNeeds = PythonFeaturesUsed.For(ProgramCopy.OriginalOf(pythonFile));

        // A Python chosen in Settings holds over the rest, as a standard chosen there does for C, C++ and Java.
        if (LanguageStandards.Current.PythonRelease is { } chosenInSettings) return ChosenInSettings(chosenInSettings, codeNeeds, environment, windowed);

        // The project's own environment is what its IDE runs it with, and what has its packages installed: it is used as it is.
        if (environment is not null)
        {
            var version = Pythons.At(environment.Interpreter, "the project's own");
            return new PythonSetup(environment.Interpreter, environment.Described, ToolchainChoice.Said(Language, version, codeNeeds), version);
        }

        // A copy of the program made to try a change in has the change; its project's files are read where it came from.
        var declared = DeclaredPython.Of(ProgramCopy.OriginalOf(pythonFile));
        var atLeast = new[] { declared?.AtLeast, codeNeeds }.OfType<ToolchainChoice.AtLeast>().ToList();
        var atMost = new[] { declared?.AtMost }.OfType<ToolchainChoice.AtMost>().ToList();

        var usual = Pythons.Usual;
        var usualFits = usual is not null && atLeast.All(need => usual.Version >= need.Version) && atMost.All(limit => usual.Version <= limit.Version);

        // Every Python on the computer is looked for only when the usual one will not do: finding them takes longer.
        var installed = usualFits ? [usual!] : Pythons.Installed;

        if (ToolchainChoice.Choose(Language, installed, usual, atLeast, atMost, codeNeeds) is not { } chosen) return null;

        return new PythonSetup(Windowed(chosen.Toolchain.Program, windowed), chosen.Explained, chosen.CodeNeeds, chosen.Toolchain);
    }

    /// <summary>
    /// The Python of the release chosen in Settings: the project's own environment when it is of that release, as it has
    /// the project's packages; else the one a terminal runs when it is; else the newest of that release here - or null
    /// when no Python of it is on this computer, as the program is then not run with another.
    /// </summary>
    internal static PythonSetup? ChosenInSettings(LanguageVersion chosen, ToolchainChoice.AtLeast? codeNeeds, PythonEnvironment.Found? environment, bool windowed)
    {
        var own = environment is null ? null : Pythons.At(environment.Interpreter, "the project's own");
        if (environment is not null && own is not null && IsOf(own, chosen))
            return new PythonSetup(environment.Interpreter, environment.Described, ToolchainChoice.Said(Language, own, codeNeeds), own);

        var usual = Pythons.Usual;
        var python = usual is not null && IsOf(usual, chosen) ? usual : Pythons.Installed.FirstOrDefault(each => IsOf(each, chosen));
        if (python is null) return null;

        // Another Python than the project's own environment's does not have what is installed only there, which is said.
        var notTheProjects = environment is null ? ""
            : $" - not {environment.Described}{(own is null ? "" : $", which is Python {own.VersionText}")}, so what is installed only there is not found";

        return new PythonSetup(
            Windowed(python.Program, windowed), $"Python {python.VersionText} ({python.FoundIn}), as Python {chosen} is chosen in Settings{notTheProjects}",
            ToolchainChoice.Said(Language, python, codeNeeds), python);
    }

    private static bool IsOf(VersionedToolchain python, LanguageVersion release) => python.Version.Major == release.Major && python.Version.Minor == release.Minor;

    /// <summary>pythonw beside python, for a program with a window, so no console opens: pythonw.exe, or the Store's pythonw3.13.exe.</summary>
    private static string Windowed(string interpreter, bool windowed)
    {
        if (!windowed) return interpreter;

        var name = Path.GetFileName(interpreter);
        var pythonw = name.StartsWith("python", StringComparison.OrdinalIgnoreCase) ? Path.Combine(Path.GetDirectoryName(interpreter)!, "pythonw" + name["python".Length..]) : interpreter;

        return File.Exists(pythonw) ? pythonw : interpreter;
    }
}
