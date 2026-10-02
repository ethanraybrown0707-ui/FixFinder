using System.Text.Json;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Python a project is set up to run with, when it has one of its own - so a program that imports what only that
/// environment has installed runs as it does in its IDE, rather than failing for a package the Python on PATH does not have.
/// </summary>
/// <remarks>
/// <para>
/// The search goes up from the program's folder, stopping at the first folder that marks a project - a .git, a
/// pyproject.toml, an .idea - so an environment belonging to some other project further up is not taken. In each folder,
/// in this order:
/// </para>
/// <list type="bullet">
/// <item>the interpreter the project's VS Code settings name, or its PyCharm settings - what the person chose;</item>
/// <item>a virtual environment in the folder - the .venv PyCharm and VS Code make, or any of the usual names;</item>
/// <item>an environment a tool keeps outside the project, found from the file the project declares it in: conda's
/// environment.yml, Poetry's pyproject.toml, Pipenv's Pipfile.</item>
/// </list>
/// </remarks>
public static class PythonEnvironment
{
    /// <summary>What an environment is called when it lives in the project: .venv by PyCharm and VS Code, venv by older tools.</summary>
    private static readonly string[] UsualNames = [".venv", "venv", "env", ".env", "virtualenv"];

    /// <summary>What marks a folder as a project's own, beyond which an environment belongs to something else.</summary>
    private static readonly string[] ProjectMarkers =
        [".git", ".idea", ".vscode", "pyproject.toml", "setup.py", "setup.cfg", "requirements.txt", "Pipfile", "environment.yml", "environment.yaml"];

    /// <param name="Interpreter">The environment's own python.exe.</param>
    /// <param name="Described">How a run explanation names it: "the Python in .venv, the project's own environment".</param>
    public sealed record Found(string Interpreter, string Described);

    /// <summary>
    /// Where the tools that keep environments outside a project keep them, and their settings: the home folder, the two
    /// application data folders, and the environment's variables.
    /// </summary>
    public sealed record Places(string Home, string AppData, string LocalAppData, Func<string, string?> Variable)
    {
        public static Places OfThisComputer { get; } = new(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable);
    }

    private static readonly AsyncLocal<Places?> PlacesLookedIn = new();

    /// <summary>The places looked in: this computer's, unless a test has said to look in others.</summary>
    internal static Places Current => PlacesLookedIn.Value ?? Places.OfThisComputer;

    /// <summary>Looks in other places than this computer's until disposed - for a test, which cannot install a real conda.</summary>
    public static IDisposable LookingIn(Places places)
    {
        var before = PlacesLookedIn.Value;
        PlacesLookedIn.Value = places;
        return new Restored(() => PlacesLookedIn.Value = before);
    }

    private sealed class Restored(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    /// <summary>The project's own Python for this file, or null when it has none and the Python on PATH is the one to use.</summary>
    /// <param name="windowed">A .pyw file, which runs with pythonw so no console opens.</param>
    public static Found? For(string pythonFile, bool windowed = false)
    {
        var places = Current;
        var home = places.Home.TrimEnd(Path.DirectorySeparatorChar);

        for (var folder = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(pythonFile))!); folder?.Parent is not null; folder = folder.Parent)
        {
            if (string.Equals(folder.FullName.TrimEnd(Path.DirectorySeparatorChar), home, StringComparison.OrdinalIgnoreCase)) break;

            if (NamedByVisualStudioCode(folder.FullName) is { } named) return named;
            if (PyCharmInterpreters.NamedFor(folder.FullName, places) is { } chosen) return chosen;

            foreach (var name in UsualNames)
            {
                var environment = Path.Combine(folder.FullName, name);
                if (Usable(environment) && InterpreterIn(environment, windowed) is { } interpreter)
                    return new Found(interpreter, $"the Python in {name}, the project's own environment");
            }

            if (CondaEnvironments.DeclaredIn(folder.FullName, places) is { } conda) return conda;
            if (PoetryEnvironments.DeclaredIn(folder.FullName, places, windowed) is { } poetry) return poetry;
            if (PipenvEnvironments.DeclaredIn(folder.FullName, places, windowed) is { } pipenv) return pipenv;

            if (ProjectMarkers.Any(marker => Path.Exists(Path.Combine(folder.FullName, marker)))) break;
        }

        return null;
    }

    /// <summary>
    /// The private copies of the application data folders the Microsoft Store's Python keeps - its LocalCache, holding a
    /// Local and a Roaming - where a tool it runs writes what it would put in LocalAppData or AppData, seen only by it.
    /// </summary>
    internal static IReadOnlyList<string> StorePythonCaches(Places places)
    {
        var packages = Path.Combine(places.LocalAppData, "Packages");

        try
        {
            return Directory.Exists(packages)
                ? [.. Directory.EnumerateDirectories(packages, "PythonSoftwareFoundation.Python.*")
                    .Select(package => Path.Combine(package, "LocalCache"))
                    .Where(Directory.Exists)]
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Whether a folder is a virtual environment that can still start: it has a pyvenv.cfg, and the Python it was made
    /// from - its home - is still installed, since an environment's python.exe only hands over to that one.
    /// </summary>
    internal static bool Usable(string environment)
    {
        var settings = Path.Combine(environment, "pyvenv.cfg");
        if (!File.Exists(settings)) return false;

        try
        {
            var home = File.ReadLines(settings)
                .Select(line => line.Split('=', 2))
                .Where(pair => pair.Length == 2 && pair[0].Trim().Equals("home", StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair[1].Trim())
                .FirstOrDefault();

            return home is null || Directory.Exists(home);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A virtual environment's own python: Scripts\python.exe on Windows, bin/python elsewhere.</summary>
    internal static string? InterpreterIn(string environment, bool windowed)
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? [Path.Combine(environment, "Scripts", windowed ? "pythonw.exe" : "python.exe"), Path.Combine(environment, "Scripts", "python.exe")]
            : [Path.Combine(environment, "bin", "python3"), Path.Combine(environment, "bin", "python")];

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// The interpreter a project's .vscode\settings.json names in python.defaultInterpreterPath, or python.pythonPath as
    /// older settings did - with ${workspaceFolder} standing for the project - when that file is there.
    /// </summary>
    private static Found? NamedByVisualStudioCode(string project)
    {
        var settings = Path.Combine(project, ".vscode", "settings.json");
        if (!File.Exists(settings)) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settings),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            foreach (var setting in new[] { "python.defaultInterpreterPath", "python.pythonPath" })
            {
                if (!document.RootElement.TryGetProperty(setting, out var value) || value.ValueKind != JsonValueKind.String) continue;

                var written = value.GetString()!.Replace("${workspaceFolder}", project, StringComparison.Ordinal);
                var interpreter = Path.GetFullPath(Path.IsPathRooted(written) ? written : Path.Combine(project, written));

                if (File.Exists(interpreter)) return new Found(interpreter, "the Python this project's VS Code settings name");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
        }

        return null;
    }
}
