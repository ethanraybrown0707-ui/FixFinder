using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// OCaml on this computer: the bytecode compiler, ocamlc, and the ocamlrun that runs what it builds - the one on PATH, as
/// a terminal set up with opam's environment finds it, or else the one in opam's current switch.
/// </summary>
/// <remarks>
/// opam keeps each switch - a set of an OCaml and its packages - in a folder of its own under its root: OPAMROOT, or
/// %LOCALAPPDATA%\opam on Windows and ~/.opam elsewhere. The switch in use is the one opam's config file names. An OCaml is
/// asked its version once - <c>ocamlc -version</c> prints it and nothing else - and the answer is kept while the program
/// is unchanged.
/// </remarks>
public static partial class OCamlToolchains
{
    /// <summary>Where OCaml is looked for: this computer's places, or a test's own, as a test cannot install an OCaml.</summary>
    /// <param name="OpamRoot">opam's root: the one OPAMROOT names, or opam's own.</param>
    /// <param name="FindOnPath">Finds a program the way a terminal finds it, on PATH.</param>
    /// <param name="Ask">Asks an ocamlc for its version, when it has to be asked.</param>
    public sealed record Places(string OpamRoot, Func<string, string?> FindOnPath, Func<string, string?> Ask)
    {
        public static Places OfThisComputer { get; } = new(OpamRootOfThisComputer(), TargetFactory.FindOnPath, AskForVersion);
    }

    /// <summary>An OCaml: its compiler, the program that runs what it compiles, its version, and where it was found.</summary>
    public sealed record OCaml(string Ocamlc, string Ocamlrun, string VersionText, int Major, int Minor, string FoundIn)
    {
        /// <summary>"OCaml 5.2.0 (on PATH)".</summary>
        public string Description => $"OCaml {VersionText} ({FoundIn})";
    }

    private static readonly AsyncLocal<Places?> PlacesLookedIn = new();

    private static Places Current => PlacesLookedIn.Value ?? Places.OfThisComputer;

    /// <summary>Looks for OCaml in other places than this computer's until disposed - for a test.</summary>
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

    [GeneratedRegex(@"^\s*switch:\s*""(?<switch>[^""]+)""", RegexOptions.Multiline)]
    private static partial Regex CurrentSwitch();

    [GeneratedRegex(@"^(?<major>\d+)\.(?<minor>\d+)")]
    private static partial Regex VersionNumbers();

    private static readonly ConcurrentDictionary<string, (DateTime Written, string? Version)> Asked = new(StringComparer.OrdinalIgnoreCase);

    private static string Executable(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    /// <summary>The OCaml FixFinder builds with: the one on PATH, else the one in opam's current switch - or null.</summary>
    public static OCaml? Usual
    {
        get
        {
            var places = Current;

            if (places.FindOnPath("ocamlc") is { } onPath && At(onPath, "on PATH", places) is { } fromPath) return fromPath;

            return CurrentSwitchFolder(places) is { } switchFolder &&
                   At(Path.Combine(switchFolder, "bin", Executable("ocamlc")), $"opam's switch {Path.GetFileName(switchFolder)}", places) is { } fromOpam
                ? fromOpam
                : null;
        }
    }

    /// <summary>The folder of the switch opam's config names as the one in use, or null.</summary>
    private static string? CurrentSwitchFolder(Places places)
    {
        var config = Path.Combine(places.OpamRoot, "config");

        try
        {
            if (!File.Exists(config) || CurrentSwitch().Match(File.ReadAllText(config)) is not { Success: true } named) return null;

            var folder = Path.Combine(places.OpamRoot, named.Groups["switch"].Value);
            return Directory.Exists(folder) ? folder : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The OCaml whose ocamlc this is, with the ocamlrun beside it, or null when either is missing or it gives no version.</summary>
    private static OCaml? At(string ocamlc, string foundIn, Places places)
    {
        if (!File.Exists(ocamlc)) return null;

        var ocamlrun = Path.Combine(Path.GetDirectoryName(ocamlc)!, Executable("ocamlrun"));
        if (!File.Exists(ocamlrun)) return null;

        if (VersionOf(ocamlc, places) is not { } version || VersionNumbers().Match(version) is not { Success: true } numbers) return null;

        return new OCaml(ocamlc, ocamlrun, version, int.Parse(numbers.Groups["major"].Value), int.Parse(numbers.Groups["minor"].Value), foundIn);
    }

    /// <summary>What an ocamlc says its version is, asked once while the file is as it was.</summary>
    private static string? VersionOf(string ocamlc, Places places)
    {
        DateTime written;
        try
        {
            written = File.GetLastWriteTimeUtc(ocamlc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (Asked.TryGetValue(ocamlc, out var known) && known.Written == written) return known.Version;

        var version = places.Ask(ocamlc);
        Asked[ocamlc] = (written, version);
        return version;
    }

    private static string? AskForVersion(string ocamlc)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(ocamlc, "-version")
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

    private static string OpamRootOfThisComputer() =>
        Environment.GetEnvironmentVariable("OPAMROOT") is { Length: > 0 } named ? named
        : OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opam")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".opam");
}
