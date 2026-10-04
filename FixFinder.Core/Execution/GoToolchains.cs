using System.Collections.Concurrent;
using System.Diagnostics;
using FixFinder.Core.Execution.Versions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Gos on this computer, each with its version: the one on PATH, the one GOROOT names, those Go's installer puts in
/// Program Files\Go or C:\Go, those golang.org/dl downloads to the sdk folder in the user's home, and those the go command
/// itself has downloaded into its module cache as golang.org/toolchain.
/// </summary>
/// <remarks>
/// A Go is known by the VERSION file at the top of its GOROOT - "go1.27.0" - and only otherwise by asking it, once, with
/// GOTOOLCHAIN=local so it answers for itself rather than for a Go it would download.
/// </remarks>
public static class GoToolchains
{
    /// <summary>Where Gos are looked for: this computer's places, or a test's own, as a test cannot install a Go.</summary>
    /// <param name="Home">The user's home folder, which holds golang.org/dl's sdk folder.</param>
    /// <param name="ProgramFolders">Where Go's installer puts a Go for every user: Program Files, and the root of the system drive.</param>
    /// <param name="GoRoot">The GOROOT this computer's environment names, when it names one.</param>
    /// <param name="ModuleCache">The go command's module cache, where the Gos it downloads are kept.</param>
    /// <param name="FindOnPath">Finds a program the way a terminal finds it, on PATH.</param>
    /// <param name="Ask">Asks a Go for its version when its files do not say it.</param>
    public sealed record Places(
        string Home,
        IReadOnlyList<string> ProgramFolders,
        string? GoRoot,
        string? ModuleCache,
        Func<string, string?> FindOnPath,
        Func<string, string?> Ask)
    {
        public static Places OfThisComputer { get; } = new(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            [
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\",
            ],
            Environment.GetEnvironmentVariable("GOROOT"),
            ModuleCacheOfThisComputer(),
            TargetFactory.FindOnPath,
            AskForVersion);
    }

    private static readonly AsyncLocal<Places?> PlacesLookedIn = new();

    private static Places Current => PlacesLookedIn.Value ?? Places.OfThisComputer;

    /// <summary>Looks for Gos in other places than this computer's until disposed - for a test.</summary>
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

    private static string GoProgram => OperatingSystem.IsWindows() ? "go.exe" : "go";

    /// <summary>The Go a terminal runs - go on PATH - with its version, or null when there is none.</summary>
    public static VersionedToolchain? Usual
    {
        get
        {
            var places = Current;
            return places.FindOnPath("go") is { } go ? At(go, "on PATH", places) : null;
        }
    }

    /// <summary>Every Go found, the newest first - the usual one among them.</summary>
    public static IReadOnlyList<VersionedToolchain> Installed
    {
        get
        {
            var places = Current;
            var found = new List<VersionedToolchain>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string go, string foundIn)
            {
                if (!File.Exists(go) || !seen.Add(Path.GetFullPath(go))) return;
                if (At(go, foundIn, places) is { } toolchain) found.Add(toolchain);
            }

            if (Usual is { } usual) Add(usual.Program, usual.FoundIn);
            if (places.GoRoot is { Length: > 0 } goRoot) Add(Path.Combine(goRoot, "bin", GoProgram), "set as GOROOT");

            foreach (var root in places.ProgramFolders) Add(Path.Combine(root, "Go", "bin", GoProgram), "installed for all users");

            foreach (var folder in ChildFolders(Path.Combine(places.Home, "sdk")).Where(folder => Path.GetFileName(folder).StartsWith("go", StringComparison.OrdinalIgnoreCase)))
                Add(Path.Combine(folder, "bin", GoProgram), "golang.org/dl's");

            if (places.ModuleCache is { Length: > 0 } moduleCache)
            {
                foreach (var folder in ChildFolders(Path.Combine(moduleCache, "golang.org")).Where(folder => Path.GetFileName(folder).StartsWith("toolchain@", StringComparison.OrdinalIgnoreCase)))
                    Add(Path.Combine(folder, "bin", GoProgram), "downloaded by the go command");
            }

            return found.OrderByDescending(toolchain => toolchain.Version).ToList();
        }
    }

    /// <summary>A Go's version, from its VERSION file or by asking it - or null when it cannot be told.</summary>
    public static VersionedToolchain? At(string go, string foundIn) => At(go, foundIn, Current);

    private static VersionedToolchain? At(string go, string foundIn, Places places)
    {
        var text = VersionFromFiles(go);

        if (text is null && Asked.TryGetValue(go, out var known) && known.Written == WrittenAt(go)) text = known.Version;

        if (text is null)
        {
            text = places.Ask(go);
            if (text is not null) Asked[go] = (WrittenAt(go), text);
        }

        var versionText = text?.StartsWith("go", StringComparison.Ordinal) == true ? text[2..] : text;
        return LanguageVersion.FindWithPatch(versionText) is { } version ? new VersionedToolchain(go, version, versionText!, foundIn) : null;
    }

    /// <summary>The first line of the VERSION file at the top of the Go's GOROOT, beside its bin folder: "go1.27.0".</summary>
    internal static string? VersionFromFiles(string go)
    {
        var bin = Path.GetDirectoryName(go);
        var goRoot = bin is null ? null : Path.GetDirectoryName(bin);
        if (goRoot is null) return null;

        try
        {
            var versionFile = Path.Combine(goRoot, "VERSION");
            if (!File.Exists(versionFile)) return null;

            var firstLine = File.ReadLines(versionFile).FirstOrDefault()?.Trim();
            return firstLine is { Length: > 2 } && firstLine.StartsWith("go", StringComparison.Ordinal) ? firstLine : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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

    /// <summary>
    /// The module cache the go command uses: GOMODCACHE, else pkg\mod in the first folder of GOPATH, else go\pkg\mod in the
    /// user's home - each read from the environment and then from the file go env -w writes, as the go command reads them.
    /// </summary>
    private static string? ModuleCacheOfThisComputer()
    {
        var written = GoEnvironmentFile();

        string? Setting(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } set ? set : written.GetValueOrDefault(name);

        if (Setting("GOMODCACHE") is { } moduleCache) return moduleCache;
        if (Setting("GOPATH")?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is { } goPath) return Path.Combine(goPath, "pkg", "mod");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length == 0 ? null : Path.Combine(home, "go", "pkg", "mod");
    }

    /// <summary>The settings go env -w has written: GOENV's file, or go\env in the user's configuration folder.</summary>
    private static Dictionary<string, string> GoEnvironmentFile()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        var file = Environment.GetEnvironmentVariable("GOENV") is { Length: > 0 } named ? named
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "go", "env");

        try
        {
            if (file == "off" || !File.Exists(file)) return settings;

            foreach (var line in File.ReadLines(file))
            {
                var equals = line.IndexOf('=');
                if (equals > 0) settings[line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return settings;
    }

    /// <summary>Asks a Go its version - for one whose GOROOT has no VERSION file - giving it ten seconds.</summary>
    private static string? AskForVersion(string go)
    {
        try
        {
            var start = new ProcessStartInfo(go, "env GOVERSION")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath(),
            };

            foreach (var (name, value) in GoSetup.Environment) start.Environment[name] = value;

            using var process = Process.Start(start);
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
}
