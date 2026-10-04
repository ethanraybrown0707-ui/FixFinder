using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FixFinder.Core.Execution.Libraries;

/// <summary>
/// The Java a project says it is written for, as its build or its IDE settings say it: the release it compiles for, whether
/// Java's preview features are on, and the JDK it is built with when they name one.
/// </summary>
/// <param name="Release">The release it compiles for - 21 - or null when it names none.</param>
/// <param name="StrictRelease">
/// Whether it compiles with javac's --release, which holds the program to that release's language and library alike;
/// false for -source and -target, which hold only the language to it, as Maven's source and Gradle's sourceCompatibility do.
/// </param>
/// <param name="Preview">Whether it turns on Java's preview features.</param>
/// <param name="SaidBy">Where the release is said, for an explanation: "pom.xml's maven.compiler.release".</param>
public sealed partial record DeclaredJava(int? Release, bool StrictRelease, bool Preview, string SaidBy)
{
    /// <summary>The Java of the JDK the build or IDE compiles with, when it says - a Gradle toolchain's, an IDE's chosen JDK's.</summary>
    public int? JdkVersion { get; init; }

    /// <summary>The folder of the JDK the project's IDE settings choose, when they choose one that is on this computer.</summary>
    public string? JdkHome { get; init; }

    /// <summary>Where the JDK is chosen, for an explanation: "IntelliJ's project settings".</summary>
    public string? JdkSaidBy { get; init; }

    [GeneratedRegex(@"^\s*(?:1\.(?<legacy>\d+)|(?<release>\d+))(?:\.0)*\s*$")]
    private static partial Regex WrittenRelease();

    /// <summary>A Java release as builds write it - 21, "21", 1.8 - or null for anything else, such as a ${property} left unset.</summary>
    public static int? ReleaseNumber(string? written)
    {
        if (written is null || WrittenRelease().Match(written) is not { Success: true } match) return null;

        var digits = match.Groups["legacy"].Success ? match.Groups["legacy"].Value : match.Groups["release"].Value;
        return int.TryParse(digits, out var release) && release is > 0 and < 1000 ? release : null;
    }

    private static readonly ConcurrentDictionary<string, (DateTime Stamp, DeclaredJava? Declared)> Read = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the project this Java file belongs to says of its Java, or null for a file with no project, or one that says nothing.</summary>
    public static DeclaredJava? Of(string javaFile)
    {
        var original = ProgramCopy.OriginalOf(Path.GetFullPath(javaFile));
        if (JavaLibraries.ProjectOf(ProgramLayout.JavaSourceRoot(original)) is not { } project) return null;

        var stamp = JavaLibraries.StampOf(project);
        if (Read.TryGetValue(project, out var known) && known.Stamp == stamp) return known.Declared;

        var declared = OfProject(project);
        Read[project] = (stamp, declared);
        return declared;
    }

    /// <summary>
    /// The build says it first - pom.xml, else the Gradle build - as the build is what compiles the program; the IDE's
    /// settings say it for a project that has no build, and name the JDK it is built with for one that has.
    /// </summary>
    private static DeclaredJava? OfProject(string project)
    {
        var pom = Path.Combine(project, "pom.xml");
        var fromBuild = File.Exists(pom) ? new MavenResolver(JavaLibraries.CurrentStores).JavaOf(pom)
            : GradleBuild.BuildFileIn(project) is not null || GradleSettings.Of(project) is not null ? GradleBuild.JavaOf(project, GradleSettings.Of(project))
            : null;

        var fromIde = IntelliJ(project) ?? Eclipse(project) ?? VisualStudioCode(project);
        if (fromBuild is null) return fromIde;
        if (fromIde is null) return fromBuild;

        // The JDK the IDE chose is the one to build with, unless it is too old for what the build compiles for.
        var ideJdkFits = fromBuild.Release is not { } release || fromIde.JdkVersion is not { } ideJdk || ideJdk >= release;
        return ideJdkFits && fromBuild.JdkHome is null && fromBuild.JdkVersion is null
            ? fromBuild with { JdkHome = fromIde.JdkHome, JdkVersion = fromIde.JdkVersion, JdkSaidBy = fromIde.JdkSaidBy }
            : fromBuild;
    }

    [GeneratedRegex(@"^JDK_(?:1_)?(?<release>\d+)(?<preview>_PREVIEW)?$")]
    private static partial Regex IntelliJLanguageLevel();

    [GeneratedRegex(@"(?<![\d.])(?<release>\d{1,2})(?![\d])")]
    private static partial Regex NumberInAName();

    /// <summary>
    /// IntelliJ's settings: .idea\misc.xml's language level - JDK_21, or JDK_21_PREVIEW with preview features on - which a
    /// module's .iml may set for itself, and the project's JDK, by the name IntelliJ's own list of JDKs, jdk.table.xml, gives it.
    /// </summary>
    private static DeclaredJava? IntelliJ(string project)
    {
        var misc = Load(Path.Combine(project, ".idea", "misc.xml"));
        var manager = misc?.Descendants("component").FirstOrDefault(component => (string?)component.Attribute("name") == "ProjectRootManager");

        var moduleLevel = SafeFiles(project, "*.iml")
            .Select(Load)
            .Select(module => module?.Descendants("component")
                .FirstOrDefault(component => (string?)component.Attribute("name") == "NewModuleRootManager")?.Attribute("LanguageLevel")?.Value)
            .FirstOrDefault(level => level is not null);

        var level = moduleLevel ?? manager?.Attribute("languageLevel")?.Value;
        var sdkName = manager?.Attribute("project-jdk-name")?.Value;
        if (level is null && sdkName is null) return null;

        var match = level is null ? null : IntelliJLanguageLevel().Match(level);
        int? release = match is { Success: true } && int.TryParse(match.Groups["release"].Value, out var number) ? number : null;
        var said = moduleLevel is not null ? "the language level of the IntelliJ module" : "IntelliJ's project language level";

        var home = sdkName is null ? null : IntelliJJdkHome(sdkName);
        var jdkVersion = home is not null ? Jdks.VersionIn(home)?.Feature
            : sdkName is not null && NumberInAName().Match(sdkName) is { Success: true } named && int.TryParse(named.Groups["release"].Value, out var fromName) ? fromName
            : null;

        return new DeclaredJava(release, StrictRelease: true, Preview: match?.Groups["preview"].Success ?? false, said)
        {
            JdkVersion = jdkVersion,
            JdkHome = home,
            JdkSaidBy = sdkName is null ? null : $"IntelliJ's project JDK, {sdkName}",
        };
    }

    /// <summary>Where the JDK of that name is, as IntelliJ's list of JDKs in its settings folder says - the newest settings first.</summary>
    private static string? IntelliJJdkHome(string name)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var lists = new[] { Path.Combine(appData, "JetBrains"), Path.Combine(appData, "Google") }
            .SelectMany(vendor => SafeFolders(vendor))
            .Select(product => Path.Combine(product, "options", "jdk.table.xml"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc);

        foreach (var list in lists)
        {
            var written = Load(list)?.Descendants("jdk")
                .Where(jdk => (string?)jdk.Element("name")?.Attribute("value") == name && (string?)jdk.Element("type")?.Attribute("value") == "JavaSDK")
                .Select(jdk => (string?)jdk.Element("homePath")?.Attribute("value"))
                .FirstOrDefault(path => path is { Length: > 0 });

            if (written is null) continue;

            var folder = written.Replace("$USER_HOME$", home, StringComparison.Ordinal).Replace('/', Path.DirectorySeparatorChar);
            if (Directory.Exists(folder)) return folder;
        }

        return null;
    }

    [GeneratedRegex(@"^\s*org\.eclipse\.jdt\.core\.(?<key>compiler\.compliance|compiler\.source|compiler\.release|compiler\.problem\.enablePreviewFeatures)\s*=\s*(?<value>\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex EclipseSetting();

    [GeneratedRegex(@"JRE_CONTAINER/[^""]*?/JavaSE-(?<release>[\d.]+)")]
    private static partial Regex EclipseExecutionEnvironment();

    /// <summary>
    /// Eclipse's settings: the compliance level in .settings\org.eclipse.jdt.core.prefs - held as --release when its release
    /// setting is on - with preview features when they are enabled there, and the JavaSE-21 its .classpath asks for a JRE of.
    /// </summary>
    private static DeclaredJava? Eclipse(string project)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        var prefs = Path.Combine(project, ".settings", "org.eclipse.jdt.core.prefs");

        try
        {
            if (File.Exists(prefs))
                foreach (Match setting in EclipseSetting().Matches(File.ReadAllText(prefs)))
                    settings[setting.Groups["key"].Value] = setting.Groups["value"].Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        var classpath = Path.Combine(project, ".classpath");
        int? environment = null;

        try
        {
            if (File.Exists(classpath) && EclipseExecutionEnvironment().Match(File.ReadAllText(classpath)) is { Success: true } wanted)
                environment = ReleaseNumber(wanted.Groups["release"].Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        var release = ReleaseNumber(settings.GetValueOrDefault("compiler.compliance") ?? settings.GetValueOrDefault("compiler.source"));
        if (release is null && environment is null) return null;

        return new DeclaredJava(
            release,
            StrictRelease: settings.GetValueOrDefault("compiler.release") == "enabled",
            Preview: settings.GetValueOrDefault("compiler.problem.enablePreviewFeatures") == "enabled",
            release is null ? "Eclipse's .classpath" : "Eclipse's compiler compliance level")
        {
            JdkVersion = environment,
            JdkSaidBy = environment is null ? null : $"the JavaSE-{environment} Eclipse's .classpath asks for",
        };
    }

    /// <summary>
    /// VS Code's settings: the runtime java.configuration.runtimes marks as the default, which VS Code builds a project of
    /// plain folders with - its folder, and the Java its name gives, JavaSE-21.
    /// </summary>
    private static DeclaredJava? VisualStudioCode(string project)
    {
        var settings = Path.Combine(project, ".vscode", "settings.json");
        if (!File.Exists(settings)) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settings), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("java.configuration.runtimes", out var runtimes) || runtimes.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var runtime in runtimes.EnumerateArray())
            {
                if (runtime.ValueKind != JsonValueKind.Object || !runtime.TryGetProperty("default", out var chosen) || chosen.ValueKind != JsonValueKind.True) continue;

                var name = runtime.TryGetProperty("name", out var named) && named.ValueKind == JsonValueKind.String ? named.GetString() ?? "" : "";
                var path = runtime.TryGetProperty("path", out var folder) && folder.ValueKind == JsonValueKind.String ? folder.GetString() : null;
                var home = path is not null && Directory.Exists(path) ? Path.GetFullPath(path) : null;

                return new DeclaredJava(null, StrictRelease: true, Preview: false, "VS Code's java.configuration.runtimes")
                {
                    JdkHome = home,
                    JdkVersion = home is not null ? Jdks.VersionIn(home)?.Feature : ReleaseNumber(name.Replace("JavaSE-", "", StringComparison.Ordinal)),
                    JdkSaidBy = $"VS Code's default Java runtime, {name}",
                };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return null;
    }

    private static XDocument? Load(string file)
    {
        try
        {
            return File.Exists(file) ? XDocument.Load(file) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeFiles(string folder, string pattern)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetFiles(folder, pattern) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeFolders(string folder)
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
