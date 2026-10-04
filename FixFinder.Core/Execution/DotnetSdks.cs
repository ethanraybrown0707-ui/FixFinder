using System.Collections.Concurrent;
using System.Diagnostics;
using FixFinder.Core.Execution.Versions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The .NET SDKs on this computer - one folder each, named for its version, in the sdk folder beside the dotnet on PATH -
/// and the one dotnet builds a project with: the newest, unless a global.json above the project asks for another, which
/// dotnet itself is asked, as its rules for global.json are its own.
/// </summary>
public static class DotnetSdks
{
    /// <summary>Where .NET SDKs are looked for: this computer's, or a test's own, as a test cannot install one.</summary>
    /// <param name="FindOnPath">Finds a program the way a terminal finds it, on PATH.</param>
    /// <param name="AskVersionIn">Asks the dotnet given which SDK it uses in the folder given, as dotnet --version says.</param>
    public sealed record Places(Func<string, string?> FindOnPath, Func<string, string, string?> AskVersionIn)
    {
        public static Places OfThisComputer { get; } = new(TargetFactory.FindOnPath, AskVersionIn: AskForVersion);
    }

    private static readonly AsyncLocal<Places?> PlacesLookedIn = new();

    private static Places Current => PlacesLookedIn.Value ?? Places.OfThisComputer;

    /// <summary>Looks for .NET SDKs in other places than this computer's until disposed - for a test.</summary>
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

    private static readonly ConcurrentDictionary<string, string?> Asked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The dotnet a terminal runs, or null when there is none.</summary>
    public static string? Dotnet => Current.FindOnPath("dotnet");

    /// <summary>Every .NET SDK beside the dotnet on PATH, the newest first.</summary>
    public static IReadOnlyList<VersionedToolchain> Installed
    {
        get
        {
            if (Dotnet is not { } dotnet || Path.GetDirectoryName(dotnet) is not { } root) return [];

            try
            {
                var sdks = Path.Combine(root, "sdk");
                if (!Directory.Exists(sdks)) return [];

                return Directory.EnumerateDirectories(sdks)
                    .Select(folder => (Folder: folder, Name: Path.GetFileName(folder)))
                    .Where(sdk => File.Exists(Path.Combine(sdk.Folder, "dotnet.dll")) && LanguageVersion.FindWithPatch(sdk.Name) is not null)
                    .Select(sdk => new VersionedToolchain(dotnet, LanguageVersion.FindWithPatch(sdk.Name)!.Value, sdk.Name, "installed"))
                    .OrderByDescending(sdk => sdk.Version)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }

    /// <summary>The .NET SDK dotnet builds what is in this folder with, or null when there is no dotnet or it cannot say.</summary>
    public static VersionedToolchain? UsedIn(string folder)
    {
        var places = Current;
        if (Dotnet is not { } dotnet) return null;

        // dotnet's answer can only change with the global.json it reads, so it is asked again only when that changes.
        var globalJson = GlobalJsonAbove(folder);
        var key = $"{dotnet}|{folder}|{globalJson}|{(globalJson is null ? 0 : File.GetLastWriteTimeUtc(globalJson).Ticks)}";
        var said = ReferenceEquals(places, Places.OfThisComputer) ? Asked.GetOrAdd(key, _ => places.AskVersionIn(dotnet, folder)) : places.AskVersionIn(dotnet, folder);

        return LanguageVersion.FindWithPatch(said) is { } version && said!.Trim() is var versionText
            ? new VersionedToolchain(dotnet, version, versionText, "on PATH")
            : null;
    }

    /// <summary>The nearest global.json in this folder or one above it, which dotnet reads to choose an SDK.</summary>
    internal static string? GlobalJsonAbove(string folder)
    {
        for (var directory = new DirectoryInfo(folder); directory is not null; directory = directory.Parent)
        {
            var globalJson = Path.Combine(directory.FullName, "global.json");
            if (File.Exists(globalJson)) return globalJson;
        }

        return null;
    }

    /// <summary>Asks dotnet which SDK it uses in a folder - giving it ten seconds - or null when it cannot say, as when global.json asks for one that is not here.</summary>
    private static string? AskForVersion(string dotnet, string folder)
    {
        try
        {
            var start = new ProcessStartInfo(dotnet, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = folder,
            };
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            start.Environment["DOTNET_NOLOGO"] = "1";

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

            var said = output.Result.Trim();
            return process.ExitCode == 0 && LanguageVersion.FindWithPatch(said) is not null ? said : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
