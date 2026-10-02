using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// A conda environment a project declares in its environment.yml - by the name it gives, or the prefix conda's export
/// writes - found where conda keeps environments: the list it keeps of every one it made, and its environments folders.
/// </summary>
public static partial class CondaEnvironments
{
    private static readonly string[] DeclarationNames = ["environment.yml", "environment.yaml"];

    /// <summary>The folders conda's installers put it in, each keeping its environments in an envs folder of its own.</summary>
    private static readonly string[] InstallationNames = ["anaconda3", "miniconda3", "miniforge3", "mambaforge"];

    [GeneratedRegex(@"^name:\s*['""]?(?<name>[^'""\s#]+)")]
    private static partial Regex NameLine();

    [GeneratedRegex(@"^prefix:\s*['""]?(?<prefix>[^'""#]+?)['""]?\s*$")]
    private static partial Regex PrefixLine();

    /// <summary>The Python of the conda environment the folder's environment.yml declares, when it is on this computer.</summary>
    public static PythonEnvironment.Found? DeclaredIn(string folder, PythonEnvironment.Places places)
    {
        var declaration = DeclarationNames.Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
        if (declaration is null) return null;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(declaration);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var name = lines.Select(line => NameLine().Match(line)).FirstOrDefault(match => match.Success)?.Groups["name"].Value;
        var prefix = lines.Select(line => PrefixLine().Match(line)).FirstOrDefault(match => match.Success)?.Groups["prefix"].Value.Trim();

        var environment = prefix is { Length: > 0 } && IsCondaEnvironment(prefix) ? prefix
            : name is { Length: > 0 } ? Named(name, places)
            : null;

        if (environment is null || InterpreterIn(environment) is not { } interpreter) return null;

        return new PythonEnvironment.Found(interpreter,
            $"the Python in the conda environment {name ?? Path.GetFileName(environment)}, which {Path.GetFileName(declaration)} names");
    }

    /// <summary>The environment of that name: in the list conda keeps of the ones it made, or in one of its environments folders.</summary>
    private static string? Named(string name, PythonEnvironment.Places places)
    {
        // base is conda's own installation, not an environment in its envs folder.
        if (name == "base") return Installations(places).FirstOrDefault(IsCondaEnvironment);

        var listed = Listed(places).Where(path => string.Equals(Path.GetFileName(path.TrimEnd('\\', '/')), name, StringComparison.OrdinalIgnoreCase));
        var inFolders = EnvironmentsFolders(places).Select(folder => Path.Combine(folder, name));

        return listed.Concat(inFolders).FirstOrDefault(IsCondaEnvironment);
    }

    /// <summary>The environments conda made, from ~/.conda/environments.txt, where it writes each one's folder on a line of its own.</summary>
    private static IReadOnlyList<string> Listed(PythonEnvironment.Places places)
    {
        var list = Path.Combine(places.Home, ".conda", "environments.txt");

        try
        {
            return File.Exists(list) ? [.. File.ReadAllLines(list).Select(line => line.Trim()).Where(line => line.Length > 0)] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The folders conda keeps environments in: those CONDA_ENVS_DIRS or CONDA_ENVS_PATH name, ~/.conda/envs, and each installation's envs.</summary>
    private static IEnumerable<string> EnvironmentsFolders(PythonEnvironment.Places places)
    {
        foreach (var variable in new[] { "CONDA_ENVS_DIRS", "CONDA_ENVS_PATH" })
        {
            foreach (var folder in (places.Variable(variable) ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return folder;
        }

        yield return Path.Combine(places.Home, ".conda", "envs");

        foreach (var installation in Installations(places)) yield return Path.Combine(installation, "envs");
    }

    /// <summary>Where conda's installers put it: in the home folder, in local application data, and for everyone in ProgramData.</summary>
    private static IEnumerable<string> Installations(PythonEnvironment.Places places)
    {
        foreach (var name in InstallationNames)
        {
            yield return Path.Combine(places.Home, name);
            yield return Path.Combine(places.LocalAppData, name);
            if (places.Variable("ProgramData") is { Length: > 0 } everyones) yield return Path.Combine(everyones, name);
        }
    }

    /// <summary>A conda environment keeps a conda-meta folder of what is installed in it; a folder without one is not one.</summary>
    private static bool IsCondaEnvironment(string folder) => Directory.Exists(Path.Combine(folder, "conda-meta"));

    /// <summary>A conda environment's python: at its top on Windows, in bin elsewhere.</summary>
    private static string? InterpreterIn(string environment)
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? [Path.Combine(environment, "python.exe")]
            : [Path.Combine(environment, "bin", "python3"), Path.Combine(environment, "bin", "python")];

        return candidates.FirstOrDefault(File.Exists);
    }
}
