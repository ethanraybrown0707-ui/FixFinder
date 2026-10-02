namespace FixFinder.Core.Execution;

/// <summary>
/// The environment Pipenv made for a project. Pipenv keeps it in a folder of environments of its own, and writes into each
/// one a .project file naming the project it was made for - so it is found by what that file says, not by working its name
/// out, which depends on how the version of Pipenv that made it wrote names.
/// </summary>
public static class PipenvEnvironments
{
    /// <summary>The Python of the environment Pipenv made for the project whose Pipfile is in the folder, when there is one.</summary>
    public static PythonEnvironment.Found? DeclaredIn(string folder, PythonEnvironment.Places places, bool windowed)
    {
        if (!File.Exists(Path.Combine(folder, "Pipfile"))) return null;

        var environments = EnvironmentsFolder(places);
        if (!Directory.Exists(environments)) return null;

        IReadOnlyList<string> made;
        try
        {
            made = [.. Directory.EnumerateDirectories(environments)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var environment in made)
        {
            if (MadeFor(environment) is not { } project || !SameFolder(project, folder)) continue;

            if (PythonEnvironment.Usable(environment) && PythonEnvironment.InterpreterIn(environment, windowed) is { } interpreter)
                return new PythonEnvironment.Found(interpreter, "the Python in the environment Pipenv made for this project");
        }

        return null;
    }

    /// <summary>
    /// Where Pipenv keeps environments: WORKON_HOME, as virtualenvwrapper names it, with a leading ~ standing for the home
    /// folder; otherwise ~/.virtualenvs on Windows, and virtualenvs in XDG_DATA_HOME, or ~/.local/share, elsewhere.
    /// </summary>
    private static string EnvironmentsFolder(PythonEnvironment.Places places)
    {
        if (places.Variable("WORKON_HOME") is { Length: > 0 } named)
            return named.StartsWith('~') ? places.Home + named[1..] : named;

        return OperatingSystem.IsWindows()
            ? Path.Combine(places.Home, ".virtualenvs")
            : Path.Combine(places.Variable("XDG_DATA_HOME") is { Length: > 0 } data ? data : Path.Combine(places.Home, ".local", "share"), "virtualenvs");
    }

    /// <summary>The project an environment says it was made for, in its .project file; null when it names none.</summary>
    private static string? MadeFor(string environment)
    {
        var marker = Path.Combine(environment, ".project");

        try
        {
            return File.Exists(marker) && File.ReadAllText(marker).Trim() is { Length: > 0 } project ? project : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool SameFolder(string first, string second)
    {
        try
        {
            return string.Equals(Path.GetFullPath(first).TrimEnd('\\', '/'), Path.GetFullPath(second).TrimEnd('\\', '/'),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
