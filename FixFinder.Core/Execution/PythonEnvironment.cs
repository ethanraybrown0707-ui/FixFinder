using System.Text.Json;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Python a project is set up to run with, when it has one of its own: the interpreter VS Code's settings for the
/// project name, or a virtual environment in the program's folder or one above it - the .venv PyCharm and VS Code make,
/// or any of the usual names holding a pyvenv.cfg. A program that imports what only that environment has installed then
/// runs as it does in its IDE, rather than failing for a package the Python on PATH does not have.
/// </summary>
/// <remarks>
/// The search goes up from the program's folder, stopping at the first folder that marks a project - a .git, a
/// pyproject.toml, an .idea - so an environment belonging to some other project further up is not taken. An environment
/// kept outside the project, as conda and Poetry keep theirs, is not found.
/// </remarks>
public static class PythonEnvironment
{
    /// <summary>What an environment is called when it lives in the project: .venv by PyCharm and VS Code, venv by older tools.</summary>
    private static readonly string[] UsualNames = [".venv", "venv", "env", ".env", "virtualenv"];

    /// <summary>What marks a folder as a project's own, beyond which an environment belongs to something else.</summary>
    private static readonly string[] ProjectMarkers =
        [".git", ".idea", ".vscode", "pyproject.toml", "setup.py", "setup.cfg", "requirements.txt", "Pipfile"];

    /// <param name="Interpreter">The environment's own python.exe.</param>
    /// <param name="Described">How a run explanation names it: "the Python in .venv, the project's own environment".</param>
    public sealed record Found(string Interpreter, string Described);

    /// <summary>The project's own Python for this file, or null when it has none and the Python on PATH is the one to use.</summary>
    /// <param name="windowed">A .pyw file, which runs with pythonw so no console opens.</param>
    public static Found? For(string pythonFile, bool windowed = false)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar);

        for (var folder = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(pythonFile))!); folder?.Parent is not null; folder = folder.Parent)
        {
            if (string.Equals(folder.FullName.TrimEnd(Path.DirectorySeparatorChar), home, StringComparison.OrdinalIgnoreCase)) break;

            if (NamedByVisualStudioCode(folder.FullName) is { } named) return named;

            foreach (var name in UsualNames)
            {
                var environment = Path.Combine(folder.FullName, name);
                if (Usable(environment) && InterpreterIn(environment, windowed) is { } interpreter)
                    return new Found(interpreter, $"the Python in {name}, the project's own environment");
            }

            if (ProjectMarkers.Any(marker => Path.Exists(Path.Combine(folder.FullName, marker)))) break;
        }

        return null;
    }

    /// <summary>
    /// Whether a folder is a virtual environment that can still start: it has a pyvenv.cfg, and the Python it was made
    /// from - its home - is still installed, since an environment's python.exe only hands over to that one.
    /// </summary>
    private static bool Usable(string environment)
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

    private static string? InterpreterIn(string environment, bool windowed)
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
