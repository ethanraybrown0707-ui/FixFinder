using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The environment Poetry made for a project. Poetry keeps it in a folder of its own, not the project's, and names it after
/// the project and a hash of the project's folder - my-project-X8f3K2aQ-py3.12 - so it is found by working that name out
/// exactly as Poetry does, in the folder Poetry's settings keep environments in.
/// </summary>
public static partial class PoetryEnvironments
{
    /// <summary>What a folder name cannot safely hold, made an underscore in the name Poetry gives an environment.</summary>
    [GeneratedRegex(@"[ $`!*@""\\\r\n\t]")]
    private static partial Regex UnsafeInAName();

    /// <summary>The runs of - _ and . a package's name treats as one -, as Python packaging normalises names.</summary>
    [GeneratedRegex(@"[-_.]+")]
    private static partial Regex NameSeparators();

    /// <summary>The Python of the environment Poetry made for the project whose pyproject.toml is in the folder, when there is one.</summary>
    public static PythonEnvironment.Found? DeclaredIn(string folder, PythonEnvironment.Places places, bool windowed)
    {
        var project = Path.Combine(folder, "pyproject.toml");
        if (Lines(project) is not { } declaration) return null;

        // A project Poetry manages says so: a [tool.poetry] table, or - from Poetry 2 - a poetry.lock beside its [project] table.
        var managedByPoetry = declaration.Any(line => line.Trim() == "[tool.poetry]") || File.Exists(Path.Combine(folder, "poetry.lock"));
        if (!managedByPoetry) return null;

        if ((ValueIn(declaration, "tool.poetry", "name") ?? ValueIn(declaration, "project", "name")) is not { Length: > 0 } name) return null;

        var environmentName = EnvironmentName(name, folder);

        foreach (var environments in EnvironmentsFolders(folder, places))
        {
            if (Chosen(environments, environmentName) is { } environment && PythonEnvironment.Usable(environment) &&
                PythonEnvironment.InterpreterIn(environment, windowed) is { } interpreter)
            {
                return new PythonEnvironment.Found(interpreter, "the Python in the environment Poetry made for this project");
            }
        }

        return null;
    }

    /// <summary>
    /// The name Poetry gives a project's environment, before the Python version: the project's name as packaging writes it,
    /// lowercased, with what a folder name cannot safely hold made underscores and cut at 42 characters, then a hyphen and
    /// the first eight characters of the URL-safe base 64 of the SHA-256 of the project's folder - lowercased, with
    /// backslashes, on Windows, as Python's os.path.normcase has it.
    /// </summary>
    internal static string EnvironmentName(string projectName, string projectFolder)
    {
        var packageName = NameSeparators().Replace(projectName, "-").ToLowerInvariant();
        var safeName = UnsafeInAName().Replace(packageName, "_");
        if (safeName.Length > 42) safeName = safeName[..42];

        var folder = Path.GetFullPath(projectFolder);
        if (folder.Length > Path.GetPathRoot(folder)!.Length) folder = folder.TrimEnd(Path.DirectorySeparatorChar);
        if (OperatingSystem.IsWindows()) folder = folder.ToLowerInvariant();

        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(folder))).Replace('+', '-').Replace('/', '_');

        return $"{safeName}-{hash[..8]}";
    }

    /// <summary>
    /// The folder of the environment of that name, among those for each Python version: the only one, or the one Poetry
    /// recorded as chosen in envs.toml. When there are several and none was recorded, none is taken - that would be a guess.
    /// </summary>
    private static string? Chosen(string environments, string environmentName)
    {
        if (!Directory.Exists(environments)) return null;

        List<string> versions;
        try
        {
            versions = [.. Directory.EnumerateDirectories(environments, environmentName + "-py*")];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (versions.Count == 1) return versions[0];

        return Lines(Path.Combine(environments, "envs.toml")) is { } chosen && ValueIn(chosen, environmentName, "minor") is { } minor
            ? versions.FirstOrDefault(version => Path.GetFileName(version) == $"{environmentName}-py{minor}")
            : null;
    }

    /// <summary>
    /// The folder Poetry keeps environments in, read as Poetry reads a setting - its POETRY_ variable first, then the
    /// project's own poetry.toml, then Poetry's settings file - and only that folder when one is named. Otherwise it is the
    /// virtualenvs folder of Poetry's cache, whose folder is read the same way, or else is where this system keeps a
    /// program's cache.
    /// </summary>
    private static IEnumerable<string> EnvironmentsFolders(string projectFolder, PythonEnvironment.Places places)
    {
        var settingsFolder = places.Variable("POETRY_CONFIG_DIR") is { Length: > 0 } namedSettings ? namedSettings : DefaultSettingsFolder(places);
        var settings = Lines(Path.Combine(settingsFolder, "config.toml")) ?? [];
        var projectsOwn = Lines(Path.Combine(projectFolder, "poetry.toml")) ?? [];

        string? Setting(string variable, string table, string key) =>
            places.Variable(variable) is { Length: > 0 } named ? named : ValueIn(projectsOwn, table, key) ?? ValueIn(settings, table, key);

        var cache = Setting("POETRY_CACHE_DIR", "", "cache-dir");

        if (Setting("POETRY_VIRTUALENVS_PATH", "virtualenvs", "path") is { } configured)
        {
            yield return configured.Replace("{cache-dir}", cache ?? DefaultCacheFolder(places), StringComparison.Ordinal);
            yield break;
        }

        yield return Path.Combine(cache ?? DefaultCacheFolder(places), "virtualenvs");
        if (cache is not null) yield break;

        // Poetry run by the Microsoft Store's Python writes its cache into that Python's own copy of local application data.
        foreach (var storeCache in PythonEnvironment.StorePythonCaches(places))
            yield return Path.Combine(storeCache, "Local", "pypoetry", "Cache", "virtualenvs");
    }

    private static string DefaultSettingsFolder(PythonEnvironment.Places places) =>
        OperatingSystem.IsWindows() ? Path.Combine(places.AppData, "pypoetry")
        : OperatingSystem.IsMacOS() ? Path.Combine(places.Home, "Library", "Application Support", "pypoetry")
        : Path.Combine(places.Variable("XDG_CONFIG_HOME") is { Length: > 0 } config ? config : Path.Combine(places.Home, ".config"), "pypoetry");

    private static string DefaultCacheFolder(PythonEnvironment.Places places) =>
        OperatingSystem.IsWindows() ? Path.Combine(places.LocalAppData, "pypoetry", "Cache")
        : OperatingSystem.IsMacOS() ? Path.Combine(places.Home, "Library", "Caches", "pypoetry")
        : Path.Combine(places.Variable("XDG_CACHE_HOME") is { Length: > 0 } cache ? cache : Path.Combine(places.Home, ".cache"), "pypoetry");

    private static string[]? Lines(string file)
    {
        try
        {
            return File.Exists(file) ? File.ReadAllLines(file) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The text a TOML file gives a key: under its [table] heading, or written as table.key, or before any heading when no
    /// table is asked for - the plain form these settings are written in. A quoted name, as envs.toml's are, is matched too.
    /// </summary>
    internal static string? ValueIn(IReadOnlyList<string> lines, string table, string key)
    {
        var inTable = table.Length == 0;
        var keyPattern = new Regex($@"^(?:{Regex.Escape(table)}\.)?{Regex.Escape(key)}\s*=\s*(?:""(?<value>(?:[^""\\]|\\.)*)""|'(?<value>[^']*)')");

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (line.StartsWith('['))
            {
                var heading = line.Trim('[', ']', ' ').Trim('"', '\'');
                inTable = table.Length > 0 && heading == table;
                continue;
            }

            if ((inTable || (table.Length > 0 && line.StartsWith(table + ".", StringComparison.Ordinal))) && keyPattern.Match(line) is { Success: true } found)
            {
                // A basic string escapes its backslashes; a literal one, in single quotes, is as written.
                return line.Contains('"') ? found.Groups["value"].Value.Replace("\\\\", "\\").Replace("\\\"", "\"") : found.Groups["value"].Value;
            }
        }

        return null;
    }
}
