using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// Scala on this computer: the Scala CLI that builds and runs a Scala program, and the versions of Scala it can build with
/// without downloading anything - those whose compiler and library are already in Coursier's cache, where Scala CLI, sbt
/// and Metals keep what they download.
/// </summary>
/// <remarks>
/// Scala CLI is looked for on PATH, then where its installers put it: the Windows installer's folder in Program Files,
/// Coursier's folder of installed applications, and Scoop's shims. A version of Scala is known by its folders in the
/// cache and the jars in them, so nothing is started to ask - asking Scala CLI its version also asks the internet whether
/// there is a newer one.
/// </remarks>
public static partial class ScalaToolchains
{
    /// <summary>Where Scala is looked for: this computer's places, or a test's own, as a test cannot install Scala CLI.</summary>
    /// <param name="LocalApplicationData">The user's local application data, which holds Coursier's cache and installed applications.</param>
    /// <param name="Home">The user's home folder, which holds Scoop's shims.</param>
    /// <param name="ProgramFolders">Where Scala CLI's Windows installer puts it for every user: Program Files.</param>
    /// <param name="CoursierCache">The cache COURSIER_CACHE names in place of Coursier's own, when it names one.</param>
    /// <param name="FindOnPath">Finds a program the way a terminal finds it, on PATH.</param>
    public sealed record Places(
        string LocalApplicationData,
        string Home,
        IReadOnlyList<string> ProgramFolders,
        string? CoursierCache,
        Func<string, string?> FindOnPath)
    {
        public static Places OfThisComputer { get; } = new(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            [Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)],
            Environment.GetEnvironmentVariable("COURSIER_CACHE"),
            TargetFactory.FindOnPath);
    }

    private static readonly AsyncLocal<Places?> PlacesLookedIn = new();

    private static Places Current => PlacesLookedIn.Value ?? Places.OfThisComputer;

    /// <summary>Looks for Scala in other places than this computer's until disposed - for a test.</summary>
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

    /// <summary>Scala CLI, and where it was found, as a reader is told: "on PATH", "in Program Files".</summary>
    public sealed record ScalaCli(string Program, string FoundIn);

    /// <summary>A version of Scala in the cache: its number as Scala writes it - 3.8.4, 2.13.18 - and the series it belongs to.</summary>
    public sealed record CachedScala(string Version, int Major, int Minor, int Patch)
    {
        /// <summary>The versions whose code is the same language: every Scala 3 for a Scala 3, and 2.13 or 2.12 for a Scala 2.</summary>
        public string Series => Major >= 3 ? Major.ToString() : $"{Major}.{Minor}";

        public bool IsScala3 => Major >= 3;
    }

    [GeneratedRegex(@"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$")]
    private static partial Regex ReleaseNumber();

    /// <summary>Scala CLI, or null when it is nowhere FixFinder looks.</summary>
    public static ScalaCli? Cli
    {
        get
        {
            var places = Current;

            if (places.FindOnPath("scala-cli") is { } onPath) return new ScalaCli(onPath, "on PATH");

            foreach (var programFolder in places.ProgramFolders)
            {
                var installed = Path.Combine(programFolder, "scala-cli-x86_64-pc-win32", "scala-cli.exe");
                if (File.Exists(installed)) return new ScalaCli(installed, "in Program Files");
            }

            foreach (var name in new[] { "scala-cli.exe", "scala-cli.bat" })
            {
                var fromCoursier = Path.Combine(places.LocalApplicationData, "Coursier", "data", "bin", name);
                if (File.Exists(fromCoursier)) return new ScalaCli(fromCoursier, "installed by Coursier");
            }

            var fromScoop = Path.Combine(places.Home, "scoop", "shims", "scala-cli.exe");
            return File.Exists(fromScoop) ? new ScalaCli(fromScoop, "installed by Scoop") : null;
        }
    }

    /// <summary>The cache Scala CLI reads what it has downloaded from: the one COURSIER_CACHE names, or Coursier's own.</summary>
    public static string CacheFolder
    {
        get
        {
            var places = Current;
            return places.CoursierCache is { Length: > 0 } named ? named : Path.Combine(places.LocalApplicationData, "Coursier", "cache", "v1");
        }
    }

    /// <summary>Where Maven Central's Scala artifacts are kept in the cache.</summary>
    private static string ScalaLangFolder => Path.Combine(CacheFolder, "https", "repo1.maven.org", "maven2", "org", "scala-lang");

    /// <summary>
    /// The versions of Scala that can be built with here, newest first: those whose compiler and library jars are both in
    /// the cache. Releases only - a release candidate is not taken for the version a program asks for.
    /// </summary>
    public static IReadOnlyList<CachedScala> CachedVersions
    {
        get
        {
            var found = new List<CachedScala>();

            found.AddRange(Cached("scala3-compiler_3", "scala3-library_3"));
            found.AddRange(Cached("scala-compiler", "scala-library"));

            return found
                .OrderByDescending(version => version.Major)
                .ThenByDescending(version => version.Minor)
                .ThenByDescending(version => version.Patch)
                .ToList();
        }
    }

    /// <summary>The versions whose compiler jar and library jar are both in the cache.</summary>
    private static IEnumerable<CachedScala> Cached(string compiler, string library)
    {
        var compilers = Path.Combine(ScalaLangFolder, compiler);
        if (!Directory.Exists(compilers)) yield break;

        IEnumerable<string> versionFolders;
        try
        {
            versionFolders = Directory.EnumerateDirectories(compilers).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var versionFolder in versionFolders)
        {
            var version = Path.GetFileName(versionFolder);
            if (ReleaseNumber().Match(version) is not { Success: true } numbers) continue;

            var compilerJar = Path.Combine(versionFolder, $"{compiler}-{version}.jar");
            var libraryJar = Path.Combine(ScalaLangFolder, library, version, $"{library}-{version}.jar");
            if (!File.Exists(compilerJar) || !File.Exists(libraryJar)) continue;

            yield return new CachedScala(
                version,
                int.Parse(numbers.Groups["major"].Value),
                int.Parse(numbers.Groups["minor"].Value),
                int.Parse(numbers.Groups["patch"].Value));
        }
    }

    /// <summary>A version as Scala writes it, read into its numbers - or null when it is not a release's number.</summary>
    public static CachedScala? Read(string version) =>
        ReleaseNumber().Match(version.Trim()) is { Success: true } numbers
            ? new CachedScala(version.Trim(), int.Parse(numbers.Groups["major"].Value), int.Parse(numbers.Groups["minor"].Value), int.Parse(numbers.Groups["patch"].Value))
            : null;
}
