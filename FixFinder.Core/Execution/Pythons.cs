using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution.Versions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Pythons on this computer, each with its version: the one on PATH, those registered with Windows as PEP 514 says
/// installers register them - the Microsoft Store's among them - those python.org's installer puts in the user's Programs
/// or in Program Files, and those pyenv, uv, the Python install manager and Anaconda keep.
/// </summary>
/// <remarks>
/// A Python is known by its own files where they say its version - the python3XY.dll beside it, an environment's
/// pyvenv.cfg, the version in a Store alias's name, the version PEP 514's registration gives - and only otherwise by
/// asking it, once, so a check does not start every Python on the computer.
/// </remarks>
public static partial class Pythons
{
    /// <summary>Where Pythons are looked for: this computer's places, or a test's own, as a test cannot install a Python.</summary>
    /// <param name="Home">The user's home folder, which holds pyenv's versions and Anaconda's.</param>
    /// <param name="AppData">The roaming application data folder, which holds uv's Pythons.</param>
    /// <param name="LocalAppData">The local one, which holds python.org's per-user installs, the Store's aliases and the install manager's Pythons.</param>
    /// <param name="ProgramFolders">Where installers put a Python for every user: Program Files, and the root of the system drive.</param>
    /// <param name="FindOnPath">Finds a program the way a terminal finds it, on PATH.</param>
    /// <param name="ReadRegistry">Whether to read the Pythons installers register with Windows.</param>
    /// <param name="Ask">Asks a Python for its version when none of its files says it.</param>
    public sealed record Places(
        string Home,
        string AppData,
        string LocalAppData,
        IReadOnlyList<string> ProgramFolders,
        Func<string, string?> FindOnPath,
        bool ReadRegistry,
        Func<string, string?> Ask)
    {
        public static Places OfThisComputer { get; } = new(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            [
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\",
            ],
            TargetFactory.FindOnPath,
            ReadRegistry: OperatingSystem.IsWindows(),
            AskForVersion);
    }

    [GeneratedRegex(@"^python(?<major>\d)(?<minor>\d{1,2})\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionedLibrary();

    [GeneratedRegex(@"^python(?<version>\d+\.\d+)\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionedAlias();

    [GeneratedRegex(@"(?m)^\s*version(?:_info)?\s*=\s*(?<version>\d+\.\d+(?:\.\d+)?)")]
    private static partial Regex EnvironmentVersion();

    private static readonly AsyncLocal<Places?> PlacesLookedIn = new();

    private static Places Current => PlacesLookedIn.Value ?? Places.OfThisComputer;

    /// <summary>Looks for Pythons in other places than this computer's until disposed - for a test.</summary>
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

    private static readonly ConcurrentDictionary<string, (DateTime Written, string? Version)> Asked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The Python a terminal runs - python, else py, else python3 on PATH - with its version, or null when there is none.</summary>
    public static VersionedToolchain? Usual
    {
        get
        {
            var places = Current;
            var interpreter = new[] { "python", "py", "python3" }.Select(places.FindOnPath).FirstOrDefault(found => found is not null);
            return interpreter is null ? null : At(interpreter, "on PATH", places);
        }
    }

    /// <summary>Every Python found, the newest first - the usual one among them.</summary>
    public static IReadOnlyList<VersionedToolchain> Installed
    {
        get
        {
            var places = Current;
            var found = new List<VersionedToolchain>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string interpreter, string foundIn)
            {
                if (!File.Exists(interpreter) || !seen.Add(Path.GetFullPath(interpreter))) return;
                if (At(interpreter, foundIn, places) is not { } python) return;

                // The Store's python.exe on PATH and its python3.13.exe are the one Python, under two of its aliases.
                if (IsStoreAlias(python.Program) && found.Any(other => IsStoreAlias(other.Program) && other.Version.CompareTo(python.Version) == 0)) return;

                found.Add(python);
            }

            if (Usual is { } usual) Add(usual.Program, usual.FoundIn);

            foreach (var (interpreter, foundIn) in Registered(places)) Add(interpreter, foundIn);

            foreach (var folder in ChildFolders(Path.Combine(places.LocalAppData, "Programs", "Python"))) Add(Path.Combine(folder, "python.exe"), "installed for this user");

            foreach (var root in places.ProgramFolders)
            {
                foreach (var folder in ChildFolders(root).Where(folder => Path.GetFileName(folder).StartsWith("Python", StringComparison.OrdinalIgnoreCase)))
                    Add(Path.Combine(folder, "python.exe"), "installed for all users");
            }

            foreach (var folder in ChildFolders(Path.Combine(places.Home, ".pyenv", "pyenv-win", "versions"))) Add(Path.Combine(folder, "python.exe"), "pyenv's");
            foreach (var folder in ChildFolders(Path.Combine(places.AppData, "uv", "python"))) Add(Path.Combine(folder, "python.exe"), "uv's");
            foreach (var folder in ChildFolders(Path.Combine(places.LocalAppData, "Python")).Where(folder => Path.GetFileName(folder).StartsWith("pythoncore", StringComparison.OrdinalIgnoreCase)))
                Add(Path.Combine(folder, "python.exe"), "the Python install manager's");

            foreach (var distribution in new[] { "anaconda3", "miniconda3", "miniforge3" }) Add(Path.Combine(places.Home, distribution, "python.exe"), $"{distribution}'s");

            return found.OrderByDescending(python => python.Version).ToList();
        }
    }

    /// <summary>A Python's version, from its own files where they say it, or by asking it - or null when it cannot be told.</summary>
    public static VersionedToolchain? At(string interpreter, string foundIn) => At(interpreter, foundIn, Current);

    private static VersionedToolchain? At(string interpreter, string foundIn, Places places)
    {
        var text = VersionFromFiles(interpreter);

        if (text is null && Asked.TryGetValue(interpreter, out var known) && known.Written == WrittenAt(interpreter)) text = known.Version;

        if (text is null)
        {
            text = places.Ask(interpreter);
            if (text is not null) Asked[interpreter] = (WrittenAt(interpreter), text);
        }

        return LanguageVersion.Find(text) is { Minor: >= 0 } version ? new VersionedToolchain(interpreter, version, text!, foundIn) : null;
    }

    private static DateTime WrittenAt(string file)
    {
        try
        {
            return File.GetLastWriteTimeUtc(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>The version a Python's own files give: python3XY.dll beside it, pyvenv.cfg, or a Store alias's name - python3.13.exe.</summary>
    internal static string? VersionFromFiles(string interpreter)
    {
        var folder = Path.GetDirectoryName(interpreter)!;

        if (VersionedAlias().Match(Path.GetFileName(interpreter)) is { Success: true } alias) return alias.Groups["version"].Value;

        foreach (var configuration in new[] { Path.Combine(folder, "pyvenv.cfg"), Path.Combine(Path.GetDirectoryName(folder) ?? folder, "pyvenv.cfg") })
        {
            try
            {
                if (File.Exists(configuration) && EnvironmentVersion().Match(File.ReadAllText(configuration)) is { Success: true } version)
                    return version.Groups["version"].Value;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        foreach (var file in Files(folder))
        {
            if (VersionedLibrary().Match(Path.GetFileName(file)) is { Success: true } library)
                return $"{library.Groups["major"].Value}.{library.Groups["minor"].Value}";
        }

        return null;
    }

    /// <summary>
    /// The Pythons registered with Windows, as PEP 514 sets out - under Software\Python in the user's registry and the
    /// machine's. One the Microsoft Store installed lives where it cannot be started directly, so its alias is used.
    /// </summary>
    private static IEnumerable<(string Interpreter, string FoundIn)> Registered(Places places)
    {
        if (!places.ReadRegistry || !OperatingSystem.IsWindows()) return [];

        var found = new List<(string, string)>();

        try
        {
            foreach (var (hive, view) in new[]
                     {
                         (Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Registry64),
                         (Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64),
                         (Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32),
                     })
            {
                using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
                using var python = root.OpenSubKey(@"Software\Python");
                if (python is null) continue;

                foreach (var company in python.GetSubKeyNames().Where(name => name != "PyLauncher"))
                {
                    using var companyKey = python.OpenSubKey(company);
                    if (companyKey is null) continue;

                    foreach (var tag in companyKey.GetSubKeyNames())
                    {
                        using var installPath = companyKey.OpenSubKey($@"{tag}\InstallPath");
                        using var tagKey = companyKey.OpenSubKey(tag);

                        var executable = installPath?.GetValue("ExecutablePath") as string
                                         ?? (installPath?.GetValue(null) is string folder ? Path.Combine(folder, "python.exe") : null);
                        if (executable is null) continue;

                        if (executable.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) && tagKey?.GetValue("SysVersion") is string storeVersion)
                            found.Add((Path.Combine(places.LocalAppData, "Microsoft", "WindowsApps", $"python{storeVersion}.exe"), "from the Microsoft Store"));
                        else
                            found.Add((executable, "registered with Windows"));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        return found;
    }

    private static bool IsStoreAlias(string interpreter) => interpreter.Contains(@"\Microsoft\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Asks a Python its version - for the one on PATH, whose alias or launcher names none - giving it ten seconds.</summary>
    private static string? AskForVersion(string interpreter)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(interpreter, "-c \"import sys; print('%d.%d.%d' % sys.version_info[:3])\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null) return null;

            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(TimeSpan.FromSeconds(10)) || !output.Wait(ToolOutput.AfterExit))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }

                return null;
            }

            return process.ExitCode == 0 ? output.Result.Trim() : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static IEnumerable<string> ChildFolders(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> Files(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "python*.dll").ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
