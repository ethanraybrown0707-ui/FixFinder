using System.Text.Json;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Python of the Jupyter kernel a notebook was saved with. The notebook names its kernel; Jupyter finds a kernel by the
/// kernel.json in a kernels folder, and the first thing that file runs is the kernel's Python.
/// </summary>
/// <remarks>
/// The kernel every Python install brings is named python3 whichever Python it is, so that name says nothing about which
/// one a notebook needs, and is passed over for the project's own environment.
/// </remarks>
public static class JupyterKernels
{
    private static readonly HashSet<string> NamesOfAnyPython = new(StringComparer.OrdinalIgnoreCase) { "python3", "python2", "python" };

    /// <summary>The Python of the kernel of that name, when it is a kernel of its own and is on this computer.</summary>
    public static PythonEnvironment.Found? Named(string? kernelName, PythonEnvironment.Places places)
    {
        if (string.IsNullOrWhiteSpace(kernelName) || NamesOfAnyPython.Contains(kernelName)) return null;

        foreach (var kernels in KernelsFolders(places))
        {
            var specification = Path.Combine(kernels, kernelName, "kernel.json");
            if (!File.Exists(specification)) continue;

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(specification));
                var root = document.RootElement;

                if (!root.TryGetProperty("argv", out var command) || command.ValueKind != JsonValueKind.Array || command.GetArrayLength() == 0) continue;

                if (command[0].GetString() is { Length: > 0 } interpreter && Path.IsPathRooted(interpreter) && File.Exists(interpreter) &&
                    Path.GetFileName(interpreter).StartsWith("python", StringComparison.OrdinalIgnoreCase))
                {
                    var shown = root.TryGetProperty("display_name", out var display) && display.GetString() is { Length: > 0 } title ? title : kernelName;
                    return new PythonEnvironment.Found(interpreter, $"the Python of the notebook's Jupyter kernel, {shown}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
            }
        }

        return null;
    }

    /// <summary>
    /// Where Jupyter keeps kernels, as jupyter_core looks for them: the folders JUPYTER_PATH names, then this user's Jupyter
    /// folder - JUPYTER_DATA_DIR in place of it when set - and the Microsoft Store Python's copy of it; and the folder for
    /// everyone in ProgramData only when JUPYTER_USE_PROGRAMDATA says to trust it, since anyone can write there.
    /// </summary>
    private static IEnumerable<string> KernelsFolders(PythonEnvironment.Places places)
    {
        foreach (var folder in (places.Variable("JUPYTER_PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return Path.Combine(folder, "kernels");

        if (places.Variable("JUPYTER_DATA_DIR") is { Length: > 0 } data)
        {
            yield return Path.Combine(data, "kernels");
        }
        else
        {
            yield return Path.Combine(UsersJupyterFolder(places), "kernels");

            foreach (var storeCache in PythonEnvironment.StorePythonCaches(places))
                yield return Path.Combine(storeCache, "Roaming", "jupyter", "kernels");
        }

        if (OperatingSystem.IsWindows() && IsSet(places.Variable("JUPYTER_USE_PROGRAMDATA")) && places.Variable("ProgramData") is { Length: > 0 } everyones)
            yield return Path.Combine(everyones, "jupyter", "kernels");
    }

    private static string UsersJupyterFolder(PythonEnvironment.Places places) =>
        OperatingSystem.IsWindows() ? Path.Combine(places.AppData, "jupyter")
        : OperatingSystem.IsMacOS() ? Path.Combine(places.Home, "Library", "Jupyter")
        : Path.Combine(places.Variable("XDG_DATA_HOME") is { Length: > 0 } data ? data : Path.Combine(places.Home, ".local", "share"), "jupyter");

    /// <summary>Whether a variable is set as jupyter_core reads one: to anything but no, n, false, off, 0 or 0.0.</summary>
    private static bool IsSet(string? value) =>
        value is not null && !new[] { "no", "n", "false", "off", "0", "0.0" }.Contains(value.ToLowerInvariant());
}
