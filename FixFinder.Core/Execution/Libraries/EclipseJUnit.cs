using System.Globalization;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution.Libraries;

/// <summary>
/// The JUnit that comes with Eclipse, which a project's .classpath takes as org.eclipse.jdt.junit.JUNIT_CONTAINER/5 rather
/// than as jars of its own. The container is made of the bundles Eclipse JDT's JUnitContainerInitializer puts in it, at the
/// versions its BuildPathSupport allows, and they are found where an Eclipse on this computer has them: among the bundles
/// its bundles.info lists, or in the Eclipse installer's shared bundle pool.
/// </summary>
public static partial class EclipseJUnit
{
    /// <summary>A bundle a container is made of, and the versions it may be: from the first, up to but not the second.</summary>
    private sealed record Part(string Bundle, string From, string Before);

    private static readonly Part Hamcrest = new("org.hamcrest", "2.2.0", "3.1.0");
    private static readonly Part Opentest4j = new("org.opentest4j", "1.0.0", "2.0.0");
    private static readonly Part ApiGuardian = new("org.apiguardian.api", "1.0.0", "2.0.0");

    /// <summary>What each JUnit container is made of: its first part is the one tests are written against.</summary>
    private static readonly Dictionary<string, Part[]> Containers = new(StringComparer.Ordinal)
    {
        // JUnit 3's tests are run with JUnit 4's jar, as Eclipse runs them.
        ["3"] = [new("org.junit", "4.13.0", "5.0.0")],
        ["4"] = [new("org.junit", "4.13.0", "5.0.0"), Hamcrest],
        ["5"] =
        [
            new("junit-jupiter-api", "5.0.0", "6.0.0"), new("junit-jupiter-engine", "5.0.0", "6.0.0"),
            new("junit-jupiter-migrationsupport", "5.0.0", "6.0.0"), new("junit-jupiter-params", "5.0.0", "6.0.0"),
            new("junit-platform-commons", "1.0.0", "2.0.0"), new("junit-platform-engine", "1.0.0", "2.0.0"),
            new("junit-platform-launcher", "1.0.0", "2.0.0"), new("junit-platform-runner", "1.0.0", "2.0.0"),
            new("junit-platform-suite-api", "1.0.0", "2.0.0"), new("junit-platform-suite-engine", "1.0.0", "2.0.0"),
            new("junit-platform-suite-commons", "1.0.0", "2.0.0"),
            Opentest4j, ApiGuardian, Hamcrest,
        ],
        ["6"] =
        [
            new("junit-jupiter-api", "6.0.0", "7.0.0"), new("junit-jupiter-engine", "6.0.0", "7.0.0"),
            new("junit-jupiter-params", "6.0.0", "7.0.0"),
            new("junit-platform-commons", "6.0.0", "7.0.0"), new("junit-platform-engine", "6.0.0", "7.0.0"),
            new("junit-platform-launcher", "6.0.0", "7.0.0"), new("junit-platform-suite-api", "6.0.0", "7.0.0"),
            new("junit-platform-suite-engine", "6.0.0", "7.0.0"),
            Opentest4j, ApiGuardian, Hamcrest,
        ],
    };

    /// <summary>The jars of the container, and where they were found - or, when none were, where they were looked for.</summary>
    public sealed record Found(IReadOnlyList<string> Jars, string From);

    /// <summary>A bundle as an Eclipse has it: its name, version and jar.</summary>
    private sealed record Bundle(string Name, Version Version, string Jar);

    [GeneratedRegex(@"^(?<name>[A-Za-z][\w.-]*?)_(?<version>\d+(?:\.\d+){0,2}[^\\/]*)\.jar$")]
    private static partial Regex PoolJar();

    private static readonly AsyncLocal<string?> HomeLookedIn = new();

    /// <summary>Looks in another home folder than this computer's until disposed - for a test, which cannot install an Eclipse.</summary>
    public static IDisposable LookingIn(string home)
    {
        var before = HomeLookedIn.Value;
        HomeLookedIn.Value = home;
        return new Restored(() => HomeLookedIn.Value = before);
    }

    private sealed class Restored(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    private static string Home => HomeLookedIn.Value ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>The jars of the JUnit container of that version - 4, 5 or 6 - from the first Eclipse on this computer that has them.</summary>
    public static Found? For(string containerVersion)
    {
        if (!Containers.TryGetValue(containerVersion, out var parts)) return null;

        foreach (var (from, bundles) in Sources())
        {
            var chosen = parts.Select(part => bundles
                    .Where(bundle => bundle.Name == part.Bundle && bundle.Version >= Version.Parse(part.From) && bundle.Version < Version.Parse(part.Before))
                    .MaxBy(bundle => bundle.Version))
                .ToList();

            // The part tests are written against has to be there; any other missing is left out, as Eclipse leaves it out.
            if (chosen[0] is null) continue;

            return new Found([.. chosen.OfType<Bundle>().Select(bundle => bundle.Jar)], from);
        }

        return null;
    }

    /// <summary>Where Eclipse's bundles are, each described for a note: each installation's bundles.info, then the shared pool.</summary>
    public static IEnumerable<string> PlacesLookedIn() =>
        Installations().Select(installation => installation.Folder).Append(Path.Combine(Home, ".p2", "pool", "plugins"));

    private static IEnumerable<(string From, IReadOnlyList<Bundle> Bundles)> Sources()
    {
        foreach (var (folder, list) in Installations())
            yield return ($"the Eclipse in {folder}", Listed(folder, list));

        var pool = Path.Combine(Home, ".p2", "pool", "plugins");
        yield return ($"the bundles the Eclipse installer keeps in {pool}", Pooled(pool));
    }

    /// <summary>
    /// The Eclipses installed where the Eclipse installer puts them - in the eclipse folder of the home folder, one folder
    /// each, with the product in an eclipse folder inside - or unpacked into that folder itself; the newest first.
    /// </summary>
    private static IReadOnlyList<(string Folder, string List)> Installations()
    {
        var eclipse = Path.Combine(Home, "eclipse");
        var candidates = new List<string> { eclipse };

        try
        {
            if (Directory.Exists(eclipse))
            {
                foreach (var product in Directory.EnumerateDirectories(eclipse))
                {
                    candidates.Add(product);
                    candidates.Add(Path.Combine(product, "eclipse"));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return [.. candidates
            .Select(folder => (Folder: folder, List: Path.Combine(folder, "configuration", "org.eclipse.equinox.simpleconfigurator", "bundles.info")))
            .Where(installation => File.Exists(installation.List))
            .OrderByDescending(installation => File.GetLastWriteTimeUtc(installation.List))];
    }

    /// <summary>The bundles an installation's bundles.info lists: name,version,location,start level,started - each an existing jar.</summary>
    private static IReadOnlyList<Bundle> Listed(string installation, string list)
    {
        var bundles = new List<Bundle>();

        try
        {
            foreach (var line in File.ReadLines(list))
            {
                if (line.StartsWith('#')) continue;

                var fields = line.Split(',');
                if (fields.Length < 3 || VersionOf(fields[1]) is not { } version || JarAt(installation, fields[2]) is not { } jar) continue;

                bundles.Add(new Bundle(fields[0], version, jar));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return bundles;
    }

    /// <summary>A bundles.info location as a file: file:/C:/... in full, or a path relative to the installation.</summary>
    private static string? JarAt(string installation, string location)
    {
        try
        {
            var path = location.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                ? new Uri(location).LocalPath
                : Path.GetFullPath(Path.Combine(installation, location.Replace('/', Path.DirectorySeparatorChar)));

            return path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>The jars in the shared pool, named bundle_version.jar.</summary>
    private static IReadOnlyList<Bundle> Pooled(string pool)
    {
        try
        {
            return Directory.Exists(pool)
                ? [.. Directory.EnumerateFiles(pool, "*.jar")
                    .Select(jar => (Jar: jar, Named: PoolJar().Match(Path.GetFileName(jar))))
                    .Where(each => each.Named.Success && VersionOf(each.Named.Groups["version"].Value) is not null)
                    .Select(each => new Bundle(each.Named.Groups["name"].Value, VersionOf(each.Named.Groups["version"].Value)!, each.Jar))]
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>A bundle's version by its numbers alone: 4.13.2.v20240929-1000 is 4.13.2.</summary>
    private static Version? VersionOf(string written)
    {
        var numbers = written.Split('.').TakeWhile(part => part.Length > 0 && part.All(char.IsAsciiDigit)).Take(3).ToList();
        while (numbers.Count < 3) numbers.Add("0");

        return numbers.All(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)) && written.Length > 0 && char.IsAsciiDigit(written[0])
            ? new Version(int.Parse(numbers[0], CultureInfo.InvariantCulture), int.Parse(numbers[1], CultureInfo.InvariantCulture), int.Parse(numbers[2], CultureInfo.InvariantCulture))
            : null;
    }
}
