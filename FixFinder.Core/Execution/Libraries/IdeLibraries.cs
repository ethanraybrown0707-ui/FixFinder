using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FixFinder.Core.Execution.Libraries;

/// <summary>
/// The libraries a project without a build file uses, found where the tools students use record them: IntelliJ's
/// .idea\libraries and module .iml files, Eclipse's .classpath, VS Code's java.project.referencedLibraries - lib\**\*.jar
/// when it says nothing - and jars kept in a lib, libs or jars folder.
/// </summary>
public static partial class IdeLibraries
{
    /// <summary>Folders people keep a project's jars in by hand.</summary>
    private static readonly string[] JarFolders = ["lib", "libs", "jars"];

    [GeneratedRegex(@"\$(?<macro>[A-Z_]+)\$")]
    private static partial Regex Macro();

    /// <summary>Whether the folder records libraries in any of the ways read here.</summary>
    public static bool Records(string project) =>
        Directory.Exists(Path.Combine(project, ".idea")) || File.Exists(Path.Combine(project, ".classpath")) ||
        File.Exists(Path.Combine(project, ".vscode", "settings.json")) || JarFolders.Any(folder => Directory.Exists(Path.Combine(project, folder))) ||
        SafeFiles(project, "*.iml").Any();

    /// <summary>
    /// What a sentence calls where the libraries came from: the tool whose settings the project keeps - IntelliJ's, then
    /// Eclipse's, then VS Code's, as that is the order they are looked in - or the folder of jars it keeps by hand.
    /// </summary>
    public static string Describe(string project) =>
        Directory.Exists(Path.Combine(project, ".idea")) || SafeFiles(project, "*.iml").Any() ? "IntelliJ's project files"
        : File.Exists(Path.Combine(project, ".classpath")) ? "Eclipse's .classpath"
        : File.Exists(Path.Combine(project, ".vscode", "settings.json")) ? "VS Code's settings"
        : JarFolders.FirstOrDefault(folder => Directory.Exists(Path.Combine(project, folder))) is { } jars ? $"its {jars} folder"
        : "the project's settings";

    public static ResolvedLibraries Read(string project, MavenRepository mavenRepository)
    {
        var main = new List<string>();
        var test = new List<string>();
        var missing = new List<MissingLibrary>();

        void Take(string jar, bool isTest, string declaredBy)
        {
            if (File.Exists(jar) || Directory.Exists(jar)) (isTest ? test : main).Add(Path.GetFullPath(jar));
            else missing.Add(new MissingLibrary(Path.GetFileName(jar), $"{declaredBy} names it at {jar}, and it is not there"));
        }

        IntelliJ(project, mavenRepository, Take, missing);
        Eclipse(project, mavenRepository, Take, missing);
        VisualStudioCode(project, Take);

        foreach (var folder in JarFolders.Skip(1))
        {
            foreach (var jar in SafeFiles(Path.Combine(project, folder), "*.jar", SearchOption.AllDirectories)) main.Add(jar);
        }

        return new ResolvedLibraries(
            main.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            test.Distinct(StringComparer.OrdinalIgnoreCase).Where(jar => !main.Contains(jar, StringComparer.OrdinalIgnoreCase)).ToList(),
            missing, []);
    }

    /// <summary>
    /// IntelliJ keeps the project's libraries in .idea\libraries, one file each, and each module's own in its .iml, both
    /// as jar:// or file:// addresses written with $PROJECT_DIR$, $MODULE_DIR$ and $MAVEN_REPOSITORY$.
    /// </summary>
    private static void IntelliJ(string project, MavenRepository mavenRepository, Action<string, bool, string> take, List<MissingLibrary> missing)
    {
        var projectLibraries = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);

        foreach (var file in SafeFiles(Path.Combine(project, ".idea", "libraries"), "*.xml"))
        {
            foreach (var library in Load(file)?.Descendants().Where(element => element.Name.LocalName == "library") ?? [])
            {
                if (library.Attribute("name")?.Value is { } name) projectLibraries[name] = [library];
            }
        }

        var modules = SafeFiles(project, "*.iml").Concat(SafeFiles(Path.Combine(project, ".idea", "modules"), "*.iml")).ToList();

        if (modules.Count == 0)
        {
            // Without a module file to say which, every project library is taken as the program's.
            foreach (var library in projectLibraries.Values.SelectMany(libraries => libraries))
                Roots(library, project, project, mavenRepository, root => take(root, false, "IntelliJ's library list"), missing);
            return;
        }

        foreach (var module in modules)
        {
            var moduleFolder = Path.GetDirectoryName(module)!;

            foreach (var entry in Load(module)?.Descendants().Where(element => element.Name.LocalName == "orderEntry") ?? [])
            {
                var test = string.Equals(entry.Attribute("scope")?.Value, "TEST", StringComparison.OrdinalIgnoreCase);
                var type = entry.Attribute("type")?.Value;

                IEnumerable<XElement> libraries = type switch
                {
                    "library" when entry.Attribute("name")?.Value is { } name => projectLibraries.GetValueOrDefault(name) ?? [],
                    "module-library" => entry.Elements().Where(element => element.Name.LocalName == "library"),
                    _ => [],
                };

                foreach (var library in libraries)
                    Roots(library, project, moduleFolder, mavenRepository, root => take(root, test, $"IntelliJ's module {Path.GetFileName(module)}"), missing);
            }
        }
    }

    private static void Roots(XElement library, string project, string module, MavenRepository mavenRepository, Action<string> take, List<MissingLibrary> missing)
    {
        var classes = library.Elements().FirstOrDefault(element => element.Name.LocalName == "CLASSES");
        if (classes is null) return;

        foreach (var root in classes.Elements().Where(element => element.Name.LocalName == "root"))
        {
            if (root.Attribute("url")?.Value is not { } url) continue;

            if (Expand(url, project, module, mavenRepository) is { } path) take(path);
            else missing.Add(new MissingLibrary(library.Attribute("name")?.Value ?? url, $"IntelliJ names it as {url}, somewhere FixFinder cannot find"));
        }

        foreach (var directory in library.Elements().Where(element => element.Name.LocalName == "jarDirectory"))
        {
            if (directory.Attribute("url")?.Value is not { } url || Expand(url, project, module, mavenRepository) is not { } folder) continue;

            var recursive = string.Equals(directory.Attribute("recursive")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            foreach (var jar in SafeFiles(folder, "*.jar", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)) take(jar);
        }
    }

    /// <summary>A jar://...!/ or file://... address with IntelliJ's $MACROS$ filled in, or null when one of them is not known here.</summary>
    private static string? Expand(string url, string project, string module, MavenRepository mavenRepository)
    {
        var path = url.StartsWith("jar://", StringComparison.Ordinal) ? url["jar://".Length..] : url.StartsWith("file://", StringComparison.Ordinal) ? url["file://".Length..] : url;
        if (path.EndsWith("!/", StringComparison.Ordinal)) path = path[..^2];

        string? Known(string macro) => macro switch
        {
            "PROJECT_DIR" => project,
            "MODULE_DIR" => module,
            "MAVEN_REPOSITORY" => mavenRepository.Root,
            "USER_HOME" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            _ => null,
        };

        if (Macro().Matches(path).Any(match => Known(match.Groups["macro"].Value) is null)) return null;

        path = Macro().Replace(path, match => Known(match.Groups["macro"].Value)!);
        return Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// Eclipse's .classpath: kind="lib" entries are jars, relative to the project or written in full; M2_REPO in a
    /// kind="var" entry is Maven's repository. Eclipse's own JUnit container lives inside Eclipse, and is named as missing.
    /// </summary>
    private static void Eclipse(string project, MavenRepository mavenRepository, Action<string, bool, string> take, List<MissingLibrary> missing)
    {
        var classpath = Path.Combine(project, ".classpath");
        if (Load(classpath) is not { } document) return;

        foreach (var entry in document.Descendants().Where(element => element.Name.LocalName == "classpathentry"))
        {
            var kind = entry.Attribute("kind")?.Value;
            var path = entry.Attribute("path")?.Value;
            if (path is null) continue;

            var test = entry.Descendants().Any(attribute => attribute.Name.LocalName == "attribute" &&
                attribute.Attribute("name")?.Value == "test" && attribute.Attribute("value")?.Value == "true");

            switch (kind)
            {
                case "lib" when !path.StartsWith('/'):
                    take(Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(project, path)), test, "Eclipse's .classpath");
                    break;

                case "var" when path.StartsWith("M2_REPO/", StringComparison.Ordinal):
                    take(Path.GetFullPath(Path.Combine(mavenRepository.Root, path["M2_REPO/".Length..])), test, "Eclipse's .classpath");
                    break;

                case "con" when path.StartsWith("org.eclipse.jdt.junit.JUNIT_CONTAINER", StringComparison.Ordinal):
                    missing.Add(new MissingLibrary($"JUnit {path.Split('/').ElementAtOrDefault(1) ?? ""}".Trim(),
                        "Eclipse's .classpath uses the JUnit that comes inside Eclipse, which FixFinder does not look inside"));
                    break;
            }
        }
    }

    /// <summary>VS Code's java.project.referencedLibraries - a list of paths and globs, or an include list - or lib\**\*.jar when it has none.</summary>
    private static void VisualStudioCode(string project, Action<string, bool, string> take)
    {
        var patterns = new List<string>();
        var settings = Path.Combine(project, ".vscode", "settings.json");

        if (File.Exists(settings))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(settings), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("java.project.referencedLibraries", out var referenced))
                {
                    var list = referenced.ValueKind == JsonValueKind.Object && referenced.TryGetProperty("include", out var include) ? include : referenced;
                    if (list.ValueKind == JsonValueKind.Array)
                        patterns.AddRange(list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        if (patterns.Count == 0) patterns.Add("lib/**/*.jar");

        foreach (var pattern in patterns)
        {
            foreach (var jar in Matching(project, pattern)) take(jar, false, "VS Code's referenced libraries");
        }
    }

    /// <summary>The files a glob such as lib/**/*.jar names, under the folder it starts from.</summary>
    private static IEnumerable<string> Matching(string project, string pattern)
    {
        var normalized = pattern.Replace('\\', '/');
        var wild = normalized.IndexOfAny(['*', '?']);

        if (wild < 0)
        {
            var single = Path.IsPathRooted(normalized) ? normalized : Path.Combine(project, normalized);
            return File.Exists(single) ? [Path.GetFullPath(single)] : [];
        }

        var fixedPart = normalized[..wild];
        var baseFolder = fixedPart.Contains('/') ? fixedPart[..fixedPart.LastIndexOf('/')] : "";
        var root = Path.GetFullPath(Path.IsPathRooted(baseFolder) ? baseFolder : Path.Combine(project, baseFolder));
        var remainder = normalized[(baseFolder.Length > 0 ? baseFolder.Length + 1 : 0)..];

        var expression = "^" + Regex.Escape(remainder).Replace(@"\*\*/", "(?:.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]") + "$";
        var matcher = new Regex(expression, RegexOptions.IgnoreCase);

        return SafeFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => matcher.IsMatch(Path.GetRelativePath(root, file).Replace('\\', '/')))
            .ToList();
    }

    private static XDocument? Load(string file)
    {
        try
        {
            return File.Exists(file) ? XDocument.Load(file) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeFiles(string folder, string pattern, SearchOption option = SearchOption.TopDirectoryOnly)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, pattern, option).Take(2000).ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
