using System.Text.RegularExpressions;
using FixFinder.Core.Execution.Versions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Python a program's project says it is written for, from the files its tools keep it in: pyproject.toml's
/// requires-python or Poetry's python dependency, .python-version, a Pipfile's python_version, setup.cfg's or setup.py's
/// python_requires, runtime.txt, and conda's environment.yml - the nearest above the program, up to the project's own folder.
/// </summary>
public static partial class DeclaredPython
{
    /// <summary>What the project says: the Python it needs at least, at most, or both, and which file says so.</summary>
    public sealed record Declared(ToolchainChoice.AtLeast? AtLeast, ToolchainChoice.AtMost? AtMost);

    /// <summary>What marks a project's own folder, beyond which a file belongs to something else - as PythonEnvironment stops.</summary>
    private static readonly string[] ProjectMarkers = [".git", ".idea", ".vscode", "pyproject.toml", "setup.py", "setup.cfg", "Pipfile", "environment.yml", "environment.yaml"];

    [GeneratedRegex(@"(?m)^\s*requires-python\s*=\s*[""'](?<range>[^""']+)[""']")]
    private static partial Regex RequiresPython();

    [GeneratedRegex(@"(?ms)^\[tool\.poetry\.dependencies\][^\[]*?^\s*python\s*=\s*[""'](?<range>[^""']+)[""']")]
    private static partial Regex PoetryPython();

    [GeneratedRegex(@"(?m)^\s*python_(?:full_)?version\s*=\s*[""'](?<range>[^""']+)[""']")]
    private static partial Regex PipfilePython();

    [GeneratedRegex(@"(?m)^\s*python_requires\s*=\s*(?<range>[^\n#]+)")]
    private static partial Regex SetupCfgPython();

    [GeneratedRegex(@"python_requires\s*=\s*[""'](?<range>[^""']+)[""']")]
    private static partial Regex SetupPyPython();

    [GeneratedRegex(@"(?m)^\s*-\s*python\s*(?<range>(?:[=<>!~]=?|==)\s*\d[\w.*]*(?:\s*,\s*[=<>!~]=?\s*\d[\w.*]*)*)\s*$")]
    private static partial Regex CondaPython();

    [GeneratedRegex(@"(?<operator>===|==|!=|~=|>=|<=|>|<|\^|~|=)?\s*v?(?<version>\d+(?:\.\d+)?(?:\.(?:\d+|\*))?)")]
    private static partial Regex Specifier();

    /// <summary>What the nearest of the project's files says, or null when none says anything.</summary>
    public static Declared? Of(string pythonFile)
    {
        for (var folder = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(pythonFile))!); folder is not null; folder = folder.Parent)
        {
            if (InFolder(folder.FullName) is { } declared) return declared;

            if (ProjectMarkers.Any(marker => File.Exists(Path.Combine(folder.FullName, marker)) || Directory.Exists(Path.Combine(folder.FullName, marker)))) break;
        }

        return null;
    }

    private static Declared? InFolder(string folder)
    {
        // pyenv's and uv's own file names the one Python to use, so it comes first.
        if (Read(folder, ".python-version") is { } pinned &&
            pinned.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0 && !line.StartsWith('#')) is { } version &&
            Range(version, ".python-version names Python") is { } fromPin)
        {
            return fromPin;
        }

        if (Read(folder, "pyproject.toml") is { } pyproject)
        {
            if (RequiresPython().Match(pyproject) is { Success: true } requires && Range(requires.Groups["range"].Value, "pyproject.toml's requires-python says") is { } fromProject)
                return fromProject;
            if (PoetryPython().Match(pyproject) is { Success: true } poetry && Range(poetry.Groups["range"].Value, "pyproject.toml's Poetry dependencies say python") is { } fromPoetry)
                return fromPoetry;
        }

        if (Read(folder, "Pipfile") is { } pipfile && PipfilePython().Match(pipfile) is { Success: true } pipenv &&
            Range(pipenv.Groups["range"].Value, "the Pipfile's python_version says") is { } fromPipfile)
            return fromPipfile;

        if (Read(folder, "setup.cfg") is { } setupCfg && SetupCfgPython().Match(setupCfg) is { Success: true } cfg &&
            Range(cfg.Groups["range"].Value, "setup.cfg's python_requires says") is { } fromSetupCfg)
            return fromSetupCfg;

        if (Read(folder, "setup.py") is { } setupPy && SetupPyPython().Match(setupPy) is { Success: true } py &&
            Range(py.Groups["range"].Value, "setup.py's python_requires says") is { } fromSetupPy)
            return fromSetupPy;

        if (Read(folder, "runtime.txt") is { } runtime && runtime.Trim().StartsWith("python-", StringComparison.OrdinalIgnoreCase) &&
            Range(runtime.Trim()["python-".Length..], "runtime.txt names Python") is { } fromRuntime)
            return fromRuntime;

        foreach (var name in new[] { "environment.yml", "environment.yaml" })
        {
            if (Read(folder, name) is { } environment && CondaPython().Match(environment) is { Success: true } conda &&
                Range(conda.Groups["range"].Value, $"{name} asks conda for python") is { } fromConda)
                return fromConda;
        }

        return null;
    }

    /// <summary>
    /// A version range as Python's packaging writes one - >=3.10, &lt;3.13, ~=3.11, ==3.12.*, joined by commas - or as Poetry
    /// writes one - ^3.11, ~3.12, >=3.8 &lt;3.12 - or a bare version, which names that one: the lowest and highest Python it
    /// lets in. Of Poetry's alternatives joined by ||, the first is taken.
    /// </summary>
    internal static Declared? Range(string written, string saidBy)
    {
        LanguageVersion? lowest = null;
        LanguageVersion? highest = null;

        foreach (Match specifier in Specifier().Matches(written.Split("||")[0]))
        {
            if (LanguageVersion.Find(specifier.Groups["version"].Value) is not { } version) continue;

            var minor = new LanguageVersion(version.Major, Math.Max(version.Minor, 0));

            switch (specifier.Groups["operator"].Value)
            {
                case ">=" or ">" or "~=" or "^":
                    lowest = Larger(lowest, minor);
                    break;
                case "<":
                    // Below 3.13 is 3.12 at most; below 4 leaves every Python 3.
                    if (version.Minor > 0) highest = Smaller(highest, new LanguageVersion(version.Major, version.Minor - 1));
                    break;
                case "<=":
                    highest = Smaller(highest, minor);
                    break;
                case "!=":
                    break;
                default:
                    // ==3.12.*, ==3.12, ~3.12, =3.12 and a bare 3.12 all name 3.12.
                    lowest = Larger(lowest, minor);
                    highest = Smaller(highest, minor);
                    break;
            }
        }

        if (lowest is null && highest is null) return null;

        var because = $"{saidBy} {written.Trim()}";
        return new Declared(
            lowest is { } atLeast ? new ToolchainChoice.AtLeast(atLeast, because) : null,
            highest is { } atMost ? new ToolchainChoice.AtMost(atMost, because) : null);
    }

    private static LanguageVersion Larger(LanguageVersion? current, LanguageVersion candidate) => current is { } known && known >= candidate ? known : candidate;

    private static LanguageVersion Smaller(LanguageVersion? current, LanguageVersion candidate) => current is { } known && known <= candidate ? known : candidate;

    private static string? Read(string folder, string name)
    {
        var path = Path.Combine(folder, name);

        try
        {
            return File.Exists(path) && new FileInfo(path).Length < 1_000_000 ? File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
