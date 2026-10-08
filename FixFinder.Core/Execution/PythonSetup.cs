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

    /// <summary>pythonw beside python, for a program with a window, so no console opens: pythonw.exe, or the Store's pythonw3.13.exe.</summary>
    private static string Windowed(string interpreter, bool windowed)
    {
        if (!windowed) return interpreter;

        var name = Path.GetFileName(interpreter);
        var pythonw = name.StartsWith("python", StringComparison.OrdinalIgnoreCase) ? Path.Combine(Path.GetDirectoryName(interpreter)!, "pythonw" + name["python".Length..]) : interpreter;

        return File.Exists(pythonw) ? pythonw : interpreter;
    }
}
