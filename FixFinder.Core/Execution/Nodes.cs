using System.Diagnostics;
using FixFinder.Core.Execution.Versions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Node.js installs on this computer, each with its version: the one on PATH, the one Node.js's installer puts in
/// Program Files\nodejs, and those nvm for Windows keeps in its root folder, one to a folder named for its version.
/// </summary>
/// <remarks>
/// A node.exe says its version in its own file - Windows reads it as the file's product version - so no Node.js is ever
/// started to ask. nvm for Windows keeps its root in settings.txt in the folder NVM_HOME names, and each version in a
/// folder of it named v and the version, as its own source has it.
/// </remarks>
public static class Nodes
{
    /// <summary>Where Node.js installs are looked for: this computer's places, or a test's own, as a test cannot install one.</summary>
    /// <param name="ProgramFolders">Where Node.js's installer puts it for every user: Program Files, 64-bit and 32-bit.</param>
    /// <param name="NvmHome">The folder NVM_HOME names, which holds nvm for Windows's settings.txt.</param>
    /// <param name="FindOnPath">Finds a program the way a terminal finds it, on PATH.</param>
    /// <param name="VersionOf">Reads the version a node.exe's own file gives.</param>
    public sealed record Places(IReadOnlyList<string> ProgramFolders, string? NvmHome, Func<string, string?> FindOnPath, Func<string, string?> VersionOf)
    {
        public static Places OfThisComputer { get; } = new(
            [
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            ],
            Environment.GetEnvironmentVariable("NVM_HOME"),
            TargetFactory.FindOnPath,
            ProductVersionOf);
    }

    private static readonly AsyncLocal<Places?> PlacesLookedIn = new();

    private static Places Current => PlacesLookedIn.Value ?? Places.OfThisComputer;

    /// <summary>Looks for Node.js in other places than this computer's until disposed - for a test.</summary>
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

    /// <summary>The Node.js a terminal runs - node on PATH - with its version, or null when there is none.</summary>
    public static VersionedToolchain? Usual
    {
        get
        {
            var places = Current;
            return places.FindOnPath("node") is { } node ? At(node, "on PATH", places) : null;
        }
    }

    /// <summary>Every Node.js found, the newest first - the usual one among them.</summary>
    public static IReadOnlyList<VersionedToolchain> Installed
    {
        get
        {
            var places = Current;
            var found = new List<VersionedToolchain>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string node, string foundIn)
            {
                // nvm's node on PATH is a link to one of its own folders: the one Node.js, found twice.
                if (!File.Exists(node) || !seen.Add(RealFolderOf(node))) return;
                if (At(node, foundIn, places) is { } toolchain) found.Add(toolchain);
            }

            if (Usual is { } usual) Add(usual.Program, usual.FoundIn);

            foreach (var root in places.ProgramFolders.Where(root => root.Length > 0)) Add(Path.Combine(root, "nodejs", "node.exe"), "installed for all users");

            if (NvmRoot(places.NvmHome) is { } nvmRoot)
            {
                foreach (var folder in ChildFolders(nvmRoot).Where(folder => Path.GetFileName(folder).StartsWith('v')))
                    Add(Path.Combine(folder, "node.exe"), "nvm's");
            }

            return found.OrderByDescending(toolchain => toolchain.Version).ToList();
        }
    }

    /// <summary>A node.exe's version, from its own file - or null when it cannot be told.</summary>
    public static VersionedToolchain? At(string node, string foundIn) => At(node, foundIn, Current);

    private static VersionedToolchain? At(string node, string foundIn, Places places)
    {
        var text = places.VersionOf(node)?.Trim().TrimStart('v');
        return LanguageVersion.FindWithPatch(text) is { } version ? new VersionedToolchain(node, version, text!, foundIn) : null;
    }

    /// <summary>The product version Windows reads from a program's own file: 24.19.0 for Node.js 24.19.0.</summary>
    private static string? ProductVersionOf(string program)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(program).ProductVersion;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>nvm for Windows's root, from the root: line of settings.txt in the folder NVM_HOME names.</summary>
    private static string? NvmRoot(string? nvmHome)
    {
        if (nvmHome is not { Length: > 0 }) return null;

        try
        {
            var settings = Path.Combine(nvmHome, "settings.txt");
            if (!File.Exists(settings)) return null;

            return File.ReadLines(settings)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("root:", StringComparison.OrdinalIgnoreCase))
                .Select(line => line["root:".Length..].Trim())
                .FirstOrDefault(root => root.Length > 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The folder a node.exe is in, through any link to it, so one Node.js reached two ways is counted once.</summary>
    private static string RealFolderOf(string node)
    {
        var folder = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(node))!);

        try
        {
            return folder.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? folder.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return folder.FullName;
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
