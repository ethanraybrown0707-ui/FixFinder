using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// Rust on this computer: the compiler, rustc - the one on PATH, as a terminal finds it, or else the one rustup installs
/// in the .cargo\bin folder of the home folder.
/// </summary>
/// <remarks>
/// rustc is asked its version once - <c>rustc --version</c> prints "rustc 1.98.1 (48a229cea 2026-09-01)" - and the answer
/// is kept while the program is unchanged. The version decides the newest edition of Rust it can build: 2018 from 1.31,
/// 2021 from 1.56 and 2024 from 1.85, as each edition's release notes give them.
/// </remarks>
public static partial class RustToolchains
{
    /// <summary>Where Rust is looked for: this computer's places, or a test's own.</summary>
    /// <param name="Home">The home folder, whose .cargo\bin rustup installs into.</param>
    /// <param name="FindOnPath">Finds a program the way a terminal finds it, on PATH.</param>
    /// <param name="Ask">Asks a rustc for its version, when it has to be asked.</param>
    public sealed record Places(string Home, Func<string, string?> FindOnPath, Func<string, string?> Ask)
    {
        public static Places OfThisComputer { get; } =
            new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), TargetFactory.FindOnPath, AskForVersion);
    }

    /// <summary>A Rust compiler: where it is, its version, and where it was found.</summary>
    public sealed record Rustc(string Program, string VersionText, int Major, int Minor, string FoundIn)
    {
        /// <summary>"rustc 1.98.1 (on PATH)".</summary>
        public string Description => $"rustc {VersionText} ({FoundIn})";

        /// <summary>The newest edition of Rust this rustc builds.</summary>
        public string NewestEdition => (Major, Minor) switch
        {
            (> 1, _) or (1, >= 85) => "2024",
            (1, >= 56) => "2021",
            (1, >= 31) => "2018",
            _ => "2015",
        };

        /// <summary>Whether this rustc builds an edition of Rust.</summary>
        public bool Builds(string edition) => string.CompareOrdinal(edition, NewestEdition) <= 0 && edition is "2015" or "2018" or "2021" or "2024";
    }

    private static readonly AsyncLocal<Places?> PlacesLookedIn = new();

    private static Places Current => PlacesLookedIn.Value ?? Places.OfThisComputer;

    /// <summary>Looks for Rust in other places than this computer's until disposed - for a test.</summary>
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

    [GeneratedRegex(@"^rustc\s+(?<version>(?<major>\d+)\.(?<minor>\d+)\.\d+\S*)")]
    private static partial Regex VersionLine();

    private static readonly ConcurrentDictionary<string, (DateTime Written, string? Version)> Asked = new(StringComparer.OrdinalIgnoreCase);

    private static string Executable(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    /// <summary>The rustc FixFinder builds with: the one on PATH, else the one rustup put in .cargo\bin - or null.</summary>
    public static Rustc? Usual
    {
        get
        {
            var places = Current;

            if (places.FindOnPath("rustc") is { } onPath && At(onPath, "on PATH", places) is { } fromPath) return fromPath;

            return At(Path.Combine(places.Home, ".cargo", "bin", Executable("rustc")), "installed by rustup", places);
        }
    }

    private static Rustc? At(string rustc, string foundIn, Places places)
    {
        if (!File.Exists(rustc)) return null;
        if (VersionOf(rustc, places) is not { } said || VersionLine().Match(said) is not { Success: true } version) return null;

        return new Rustc(rustc, version.Groups["version"].Value, int.Parse(version.Groups["major"].Value), int.Parse(version.Groups["minor"].Value), foundIn);
    }

    /// <summary>What a rustc says its version is, asked once while the file is as it was.</summary>
    private static string? VersionOf(string rustc, Places places)
    {
        DateTime written;
        try
        {
            written = File.GetLastWriteTimeUtc(rustc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (Asked.TryGetValue(rustc, out var known) && known.Written == written) return known.Version;

        var version = places.Ask(rustc);
        Asked[rustc] = (written, version);
        return version;
    }

    private static string? AskForVersion(string rustc)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(rustc, "--version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });

            if (process is null) return null;

            var said = process.StandardOutput.ReadToEnd().Trim();
            return process.WaitForExit(30_000) && process.ExitCode == 0 && said.Length > 0 ? said : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
