using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>A Java Development Kit on this computer: its folder, which Java it is, and where FixFinder found it.</summary>
/// <param name="Home">The JDK's own folder, the one holding bin.</param>
/// <param name="VersionText">Its version as its release file gives it: 25.0.4.1, or 1.8.0_401.</param>
/// <param name="Version">Its feature release - 25, or 8 for 1.8.0_401 - which is what javac's --release counts in.</param>
/// <param name="FoundIn">Where it was found, in a few words for a person: "on PATH", "Eclipse's own Java".</param>
public sealed record Jdk(string Home, string VersionText, int Version, string FoundIn)
{
    public string Javac => Tool("javac");

    public string Java => Tool("java");

    public string Javap => Tool("javap");

    /// <summary>One of the JDK's own programs, from its bin folder - so javac, java and javap always come from the one JDK.</summary>
    public string Tool(string name) => Path.Combine(Home, "bin", OperatingSystem.IsWindows() ? name + ".exe" : name);

    /// <summary>The JDK as Settings and a run explanation name it: "Java 25.0.4.1, Eclipse's own Java".</summary>
    public string Description => $"Java {VersionText}, {FoundIn}";
}

/// <summary>
/// Every JDK on this computer, found where the tools that install one put it, and the one a program is built with unless
/// it asks for another: the JDK whose javac a terminal would run.
/// </summary>
/// <remarks>
/// A JDK is known by the release file in its folder, which every JDK since 8 has, so its version is read rather than
/// guessed from a folder's name - by name, jdk-8 would come after jdk-27. Only a folder with javac in its bin counts: a
/// JRE can run a program but not build one.
/// </remarks>
public static partial class Jdks
{
    /// <summary>Where JDKs are looked for: this computer's places, or a test's own, as a test cannot install a JDK.</summary>
    /// <param name="Home">The user's home folder, which holds IntelliJ's .jdks, Gradle's jdks, Scoop's apps and Eclipse's.</param>
    /// <param name="ProgramFolders">Where installers put programs: Program Files, Program Files (x86) and the user's own Programs.</param>
    /// <param name="Variable">Reads an environment variable: JAVA_HOME.</param>
    /// <param name="FindOnPath">Finds a program the way a terminal finds it, on PATH.</param>
    /// <param name="ReadRegistry">Whether to read the list of JDKs Oracle's installer writes to the registry.</param>
    public sealed record Places(
        string Home,
        IReadOnlyList<string> ProgramFolders,
        Func<string, string?> Variable,
        Func<string, string?> FindOnPath,
        bool ReadRegistry)
    {
        public static Places OfThisComputer { get; } = new(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            [
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            ],
            Environment.GetEnvironmentVariable,
            TargetFactory.FindOnPath,
            ReadRegistry: OperatingSystem.IsWindows());
    }

    /// <summary>
    /// The folders in Program Files that JDK installers make: Oracle's, Eclipse Temurin's - and the AdoptOpenJDK it was
    /// before - Microsoft's, Amazon's, Azul's, BellSoft's, IBM's, SAP's and Red Hat's. Each holds one folder per JDK.
    /// </summary>
    private static readonly string[] VendorFolders =
    [
        "Java", "Eclipse Adoptium", "Eclipse Foundation", "AdoptOpenJDK", "Microsoft", "Amazon Corretto", "Zulu", "BellSoft",
        "Semeru", "SapMachine", "RedHat", "OpenJDK", "ojdkbuild",
    ];

    /// <summary>The IDEs that bring a JDK of their own, in a jbr folder: IntelliJ's and Android Studio's.</summary>
    private static readonly string[] IdeFolders = ["JetBrains", "Android"];

    private static readonly AsyncLocal<Places?> PlacesLookedIn = new();

    /// <summary>Looks for JDKs in other places than this computer's until disposed - for a test.</summary>
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

    private static Places Current => PlacesLookedIn.Value ?? Places.OfThisComputer;

    /// <summary>
    /// What was found, and when. A JDK installed while FixFinder is open is found on the next check after half a minute,
    /// which keeps a folder of exercises checked one after another from looking through every folder for each of them.
    /// </summary>
    private sealed record Search(Places Places, DateTime When, IReadOnlyList<Jdk> Found, Jdk? Default);

    private static Search? _lastSearch;

    private static readonly object Searching = new();

    private static readonly TimeSpan SearchKeptFor = TimeSpan.FromSeconds(30);

    private static Search SearchNow()
    {
        var places = Current;

        lock (Searching)
        {
            if (_lastSearch is { } last && ReferenceEquals(last.Places, places) && DateTime.UtcNow - last.When < SearchKeptFor) return last;

            var found = Discover(places);
            _lastSearch = new Search(places, DateTime.UtcNow, found, DefaultAmong(found, places));
            return _lastSearch;
        }
    }

    /// <summary>Forgets what was found, so the next question looks again - for Settings, after somebody installs a JDK.</summary>
    public static void LookAgain()
    {
        lock (Searching) _lastSearch = null;
    }

    /// <summary>Every JDK found, the newest Java first; for one Java, the order they were found in.</summary>
    public static IReadOnlyList<Jdk> Installed => SearchNow().Found;

    /// <summary>
    /// The JDK a program is built with when nothing asks for another: the one whose javac a terminal runs - on PATH - else
    /// the one JAVA_HOME names, which Maven and Gradle run with, else the newest found.
    /// </summary>
    public static Jdk? Default => SearchNow().Default;

    private static readonly AsyncLocal<Jdk?> ChosenForTheProgram = new();

    /// <summary>
    /// The JDK the program being checked is built with, for the work that follows until disposed: reading its code, checking
    /// a fix compiles and asking javap about a class all use the JDK the program itself is built with.
    /// </summary>
    public static IDisposable Using(Jdk jdk)
    {
        var before = ChosenForTheProgram.Value;
        ChosenForTheProgram.Value = jdk;
        return new Restored(() => ChosenForTheProgram.Value = before);
    }

    /// <summary>The JDK of the program being checked, or the default one outside a check.</summary>
    public static Jdk? InUse => ChosenForTheProgram.Value ?? Default;

    /// <summary>The newest JDK of at least this Java, or null when there is none.</summary>
    public static Jdk? AtLeast(int version) => Installed.FirstOrDefault(jdk => jdk.Version >= version);

    private static IReadOnlyList<Jdk> Discover(Places places)
    {
        var found = new List<Jdk>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Take(string? home, string foundIn)
        {
            if (string.IsNullOrWhiteSpace(home)) return;

            string full;
            try { full = Path.GetFullPath(home.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return; }

            if (seen.Contains(full) || At(full, foundIn) is not { } jdk) return;

            seen.Add(full);
            found.Add(jdk);
        }

        if (HomeOfTool(places.FindOnPath("javac")) is { } onPath) Take(onPath, "on PATH");
        Take(places.Variable("JAVA_HOME"), "named by JAVA_HOME");

        foreach (var registered in RegisteredHomes(places)) Take(registered, "listed in the Windows registry");

        foreach (var programs in places.ProgramFolders.Where(folder => !string.IsNullOrEmpty(folder)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var vendor in VendorFolders)
                foreach (var home in ChildFolders(Path.Combine(programs, vendor)))
                    Take(home, "installed in " + Path.GetFileName(programs.TrimEnd(Path.DirectorySeparatorChar)));

            // IntelliJ IDEA 2025.2\jbr, Android Studio\jbr: the runtime an IDE brings counts only when it has javac.
            foreach (var ide in IdeFolders)
                foreach (var product in ChildFolders(Path.Combine(programs, ide)))
                    Take(Path.Combine(product, "jbr"), $"{Path.GetFileName(product)}'s own Java");
        }

        // IntelliJ keeps the JDKs it downloads in .jdks; Gradle unpacks the ones its toolchains download into .gradle\jdks,
        // sometimes a folder further down; Scoop's current folder of each app is the version in use.
        foreach (var home in ChildFolders(Path.Combine(places.Home, ".jdks"))) Take(home, "downloaded by IntelliJ");

        foreach (var download in ChildFolders(Path.Combine(places.Home, ".gradle", "jdks")))
        {
            Take(download, "downloaded by Gradle");
            foreach (var inner in ChildFolders(download)) Take(inner, "downloaded by Gradle");
        }

        foreach (var app in ChildFolders(Path.Combine(places.Home, "scoop", "apps"))) Take(Path.Combine(app, "current"), "installed by Scoop");

        foreach (var runtime in EclipseRuntimes(places.Home)) Take(runtime, "Eclipse's own Java");

        // Newest Java first; the sort is stable, so among JDKs of one Java the one found first - on PATH - stays first.
        return [.. found.OrderByDescending(jdk => jdk.Version)];
    }

    /// <summary>The JDK at this folder, with its version read from its release file, or null when it is not a JDK.</summary>
    internal static Jdk? At(string home, string foundIn)
    {
        var javac = Path.Combine(home, "bin", OperatingSystem.IsWindows() ? "javac.exe" : "javac");
        if (!File.Exists(javac)) return null;

        return VersionIn(home) is { } version ? new Jdk(home, version.Text, version.Feature, foundIn) : null;
    }

    [GeneratedRegex(@"^JAVA_VERSION=""(?<text>[^""]+)""", RegexOptions.Multiline)]
    private static partial Regex ReleaseVersion();

    /// <summary>The version a JDK's release file gives, as written and as its feature release: 1.8.0_401 is Java 8.</summary>
    internal static (string Text, int Feature)? VersionIn(string home)
    {
        try
        {
            var release = Path.Combine(home, "release");
            if (!File.Exists(release) || ReleaseVersion().Match(File.ReadAllText(release)) is not { Success: true } match) return null;

            var text = match.Groups["text"].Value;
            return FeatureOf(text) is { } feature ? (text, feature) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^(?:1\.(?<legacy>\d+)|(?<feature>\d+))")]
    private static partial Regex FeatureNumber();

    /// <summary>The feature release a version is: 25 for 25.0.4.1 or 25-ea, 8 for 1.8.0_401.</summary>
    internal static int? FeatureOf(string version)
    {
        if (FeatureNumber().Match(version.Trim()) is not { Success: true } match) return null;

        var digits = match.Groups["legacy"].Success ? match.Groups["legacy"].Value : match.Groups["feature"].Value;
        return int.TryParse(digits, out var feature) && feature > 0 ? feature : null;
    }

    /// <summary>
    /// The JDK folder a javac found on PATH belongs to: the folder above its bin. Oracle's installer puts small launchers in a
    /// javapath folder instead, which start the JDK it last installed; that JDK is found in Program Files all the same, and is
    /// picked out as the one whose version the launcher's javac gives.
    /// </summary>
    private static string? HomeOfTool(string? tool)
    {
        if (tool is null) return null;

        var bin = Path.GetDirectoryName(Path.GetFullPath(tool));
        var home = bin is null ? null : Path.GetDirectoryName(bin);
        return home is not null && File.Exists(Path.Combine(home, "release")) ? home : null;
    }

    /// <summary>
    /// The default among those found: the one whose javac is first on PATH, then the one JAVA_HOME names, then the newest.
    /// A javac on PATH that is only a launcher - Oracle's javapath - is asked its version, and the JDK of that version is it.
    /// </summary>
    private static Jdk? DefaultAmong(IReadOnlyList<Jdk> found, Places places)
    {
        if (found.Count == 0) return null;

        if (places.FindOnPath("javac") is { } javac)
        {
            if (found.FirstOrDefault(jdk => jdk.FoundIn == "on PATH") is { } onPath) return onPath;

            if (VersionTheLauncherGives(javac) is { } launched &&
                found.FirstOrDefault(jdk => jdk.VersionText == launched) is { } same)
                return same;
        }

        return found.FirstOrDefault(jdk => jdk.FoundIn == "named by JAVA_HOME") ?? found[0];
    }

    [GeneratedRegex(@"^javac\s+(?<version>\S+)", RegexOptions.Multiline)]
    private static partial Regex JavacVersion();

    /// <summary>What a javac says its version is - "javac 25.0.4.1" - or null when it does not say within a few seconds.</summary>
    private static string? VersionTheLauncherGives(string javac)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(javac, "-version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(TimeSpan.FromSeconds(10)) || !Task.WaitAll([output, errors], ToolOutput.AfterExit))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }

                return null;
            }

            return JavacVersion().Match(output.Result + "\n" + errors.Result) is { Success: true } match ? match.Groups["version"].Value : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// The JDK folders Oracle's installer lists in the registry - JavaSoft\JDK for 9 and later, JavaSoft\Java Development Kit
    /// for 8 - which find a JDK installed somewhere other than Program Files.
    /// </summary>
    private static IEnumerable<string> RegisteredHomes(Places places)
    {
        if (!places.ReadRegistry || !OperatingSystem.IsWindows()) return [];

        var homes = new List<string>();

        try
        {
            using var machine = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);

            foreach (var listing in new[] { @"SOFTWARE\JavaSoft\JDK", @"SOFTWARE\JavaSoft\Java Development Kit" })
            {
                using var versions = machine.OpenSubKey(listing);
                if (versions is null) continue;

                foreach (var version in versions.GetSubKeyNames())
                {
                    using var entry = versions.OpenSubKey(version);
                    if (entry?.GetValue("JavaHome") is string home) homes.Add(home);
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        return homes;
    }

    /// <summary>
    /// The full Java runtimes Eclipse brings - JustJ's, which hold javac - in the shared pool the Eclipse installer keeps in
    /// .p2, and in the plugins of each Eclipse the installer put in the eclipse folder of the home folder.
    /// </summary>
    private static IEnumerable<string> EclipseRuntimes(string home)
    {
        var pluginFolders = new List<string> { Path.Combine(home, ".p2", "pool", "plugins") };
        var eclipse = Path.Combine(home, "eclipse");
        pluginFolders.Add(Path.Combine(eclipse, "plugins"));

        foreach (var product in ChildFolders(eclipse))
        {
            pluginFolders.Add(Path.Combine(product, "plugins"));
            pluginFolders.Add(Path.Combine(product, "eclipse", "plugins"));
        }

        foreach (var plugins in pluginFolders)
        {
            foreach (var runtime in ChildFolders(plugins)
                         .Where(folder => Path.GetFileName(folder).StartsWith("org.eclipse.justj.openjdk.hotspot.jre.full.", StringComparison.OrdinalIgnoreCase)))
                yield return Path.Combine(runtime, "jre");
        }
    }

    private static IEnumerable<string> ChildFolders(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetDirectories(folder) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
