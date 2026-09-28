using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FixFinder.Core.Execution.Libraries;

/// <summary>
/// The libraries a Java program is built and run with, found as its own tools find them - a Maven project's pom.xml, a
/// Gradle build file, the libraries IntelliJ, Eclipse or VS Code record, or jars kept in a lib folder - among what is
/// already on this computer, with those it declares that are not here named rather than left for javac to trip over.
/// </summary>
/// <param name="Jars">The jars the program's own code is built and run with.</param>
/// <param name="TestJars">The jars its tests need besides: JUnit and the like.</param>
/// <param name="SourceRoots">Every folder of the project's source, so a test in src\test\java can use src\main\java.</param>
/// <param name="Resources">Folders of files the program reads as resources, such as src\main\resources.</param>
/// <param name="DeclaredIn">What the libraries were read from - pom.xml, build.gradle, the project's IDE settings.</param>
public sealed partial record JavaLibraries(
    IReadOnlyList<string> Jars,
    IReadOnlyList<string> TestJars,
    IReadOnlyList<string> SourceRoots,
    IReadOnlyList<string> Resources,
    IReadOnlyList<MissingLibrary> Missing,
    IReadOnlyList<string> NotRead,
    string? DeclaredIn,
    string? ProjectFolder)
{
    public static JavaLibraries None { get; } = new([], [], [], [], [], [], null, null);

    private static readonly ConcurrentDictionary<string, (DateTime Stamp, JavaLibraries Libraries)> Found = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Stores used in place of this user's, by the work that set them and nothing running beside it.</summary>
    private static readonly AsyncLocal<LibraryStores?> StoresInUse = new();

    /// <summary>Where downloaded libraries are looked for: Maven's and Gradle's folders for this user, unless set here.</summary>
    private static LibraryStores Stores() => StoresInUse.Value ?? LibraryStores.ForThisUser();

    /// <summary>Where downloaded libraries are looked for just now - for JUnit's launcher, which a project seldom names.</summary>
    public static LibraryStores CurrentStores => Stores();

    /// <summary>
    /// Looks for libraries in these stores instead of this user's, for the work that follows until the result is disposed
    /// - a test's own repository, which other work running at the same time never sees.
    /// </summary>
    public static IDisposable UsingStores(LibraryStores stores)
    {
        var before = StoresInUse.Value;
        StoresInUse.Value = stores;
        return new StoresRestored(() => StoresInUse.Value = before);
    }

    private sealed class StoresRestored(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    [GeneratedRegex(@"\$MODULE_DIR\$|\$PROJECT_DIR\$")]
    private static partial Regex IdeFolderMacro();

    /// <summary>Everything the program is compiled and run with: its own libraries and its tests' together.</summary>
    public IReadOnlyList<string> ClassPath => [.. Jars, .. TestJars];

    /// <summary>The source roots besides the one a file is in, where in a copy of the program they are in that copy.</summary>
    public IReadOnlyList<string> OtherSourceRoots(string file)
    {
        var own = ProgramLayout.JavaSourceRoot(file);
        return SourceRoots.Select(root => ProgramCopy.InCopyOf(file, root))
            .Where(root => !string.Equals(Path.GetFullPath(root), Path.GetFullPath(own), StringComparison.OrdinalIgnoreCase) && Directory.Exists(root))
            .ToList();
    }

    /// <summary>A few words for how a run explanation names the libraries: "3 libraries from pom.xml".</summary>
    public string? Described => ClassPath.Count == 0 || DeclaredIn is null ? null : $"{ClassPath.Count} librar{(ClassPath.Count == 1 ? "y" : "ies")} from {DeclaredIn}";

    /// <summary>The libraries the program this Java file belongs to is built with; nothing at all for a file with no project.</summary>
    public static JavaLibraries For(string javaFile)
    {
        var original = ProgramCopy.OriginalOf(Path.GetFullPath(javaFile));
        if (ProjectOf(ProgramLayout.JavaSourceRoot(original)) is not { } project) return None;

        var stamp = StampOf(project);
        if (Found.TryGetValue(project, out var known) && known.Stamp == stamp) return known.Libraries;

        var libraries = Read(project);

        // What is missing may be downloaded at any moment, by opening the project in an IDE, so it is never remembered.
        if (libraries.Missing.Count == 0) Found[project] = (stamp, libraries);
        return libraries;
    }

    /// <summary>
    /// The folder the project's build or IDE settings are in: the one an IDE takes for the project - holding src, or
    /// src\main\java - or the source root itself. Never the user's own folder, or the root of a drive, which hold
    /// everything rather than a project.
    /// </summary>
    public static string? ProjectOf(string sourceRoot)
    {
        var home = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).TrimEnd(Path.DirectorySeparatorChar);

        return new[] { WorkingFolder.JavaProjectFolder(sourceRoot), sourceRoot }
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(folder => new DirectoryInfo(folder).Parent is not null &&
                                      !string.Equals(folder.TrimEnd(Path.DirectorySeparatorChar), home, StringComparison.OrdinalIgnoreCase) &&
                                      IsProject(folder));
    }

    private static bool IsProject(string folder) =>
        BuildFiles.Any(name => File.Exists(Path.Combine(folder, name))) || IdeLibraries.Records(folder);

    private static readonly string[] BuildFiles = ["pom.xml", "build.gradle", "build.gradle.kts"];

    private static JavaLibraries Read(string project)
    {
        var stores = Stores();
        var mavenRepository = stores.Each.OfType<MavenRepository>().FirstOrDefault() ?? MavenRepository.ForThisUser();

        var pom = Path.Combine(project, "pom.xml");
        if (File.Exists(pom))
        {
            var resolved = new MavenResolver(stores).Resolve(pom);
            return Built(resolved, [], "pom.xml", project, StandardRoots(project));
        }

        if (BuildFiles.Skip(1).Select(name => Path.Combine(project, name)).FirstOrDefault(File.Exists) is { } gradleFile)
        {
            var declared = GradleBuild.Read(gradleFile);
            var resolver = new MavenResolver(stores, highestVersionWins: true);
            var resolved = resolver.Resolve(declared.Dependencies, resolver.ManagedBy(declared.Platforms));

            resolved = resolved with
            {
                Main = [.. resolved.Main, .. declared.Files.Where(file => !file.Test).Select(file => file.Jar)],
                Test = [.. resolved.Test, .. declared.Files.Where(file => file.Test).Select(file => file.Jar)],
            };

            return Built(resolved, declared.NotRead, Path.GetFileName(gradleFile), project, StandardRoots(project));
        }

        return Built(IdeLibraries.Read(project, mavenRepository), [], "the project's library settings", project, IdeRoots(project));
    }

    private static JavaLibraries Built(ResolvedLibraries resolved, IReadOnlyList<string> notRead, string declaredIn, string project, (IReadOnlyList<string> Sources, IReadOnlyList<string> Resources) roots) =>
        new(resolved.Main, resolved.Test.Where(jar => !resolved.Main.Contains(jar, StringComparer.OrdinalIgnoreCase)).ToList(),
            roots.Sources, roots.Resources, resolved.Missing, notRead, declaredIn, project);

    /// <summary>Maven's and Gradle's layout: src\main\java and src\test\java, with their resources beside them.</summary>
    private static (IReadOnlyList<string>, IReadOnlyList<string>) StandardRoots(string project)
    {
        var sources = new[] { "main", "test" }.Select(kind => Path.Combine(project, "src", kind, "java")).Where(Directory.Exists).ToList();
        var resources = new[] { "main", "test" }.Select(kind => Path.Combine(project, "src", kind, "resources")).Where(Directory.Exists).ToList();
        return (sources, resources);
    }

    /// <summary>The source folders IntelliJ's module files or Eclipse's .classpath name - src and test, often.</summary>
    private static (IReadOnlyList<string>, IReadOnlyList<string>) IdeRoots(string project)
    {
        var sources = new List<string>();
        var resources = new List<string>();

        try
        {
            foreach (var module in Directory.EnumerateFiles(project, "*.iml").Take(20))
            {
                var moduleFolder = Path.GetDirectoryName(module)!;
                foreach (var folder in XDocument.Load(module).Descendants().Where(element => element.Name.LocalName == "sourceFolder"))
                {
                    if (folder.Attribute("url")?.Value is not { } url || !url.StartsWith("file://", StringComparison.Ordinal)) continue;

                    var path = Path.GetFullPath(IdeFolderMacro().Replace(url["file://".Length..], match => match.Value == "$MODULE_DIR$" ? moduleFolder : project));
                    var type = folder.Attribute("type")?.Value ?? "";
                    (type.Contains("resource", StringComparison.Ordinal) ? resources : sources).Add(path);
                }
            }

            var classpath = Path.Combine(project, ".classpath");
            if (File.Exists(classpath))
            {
                foreach (var entry in XDocument.Load(classpath).Descendants().Where(element => element.Name.LocalName == "classpathentry" && element.Attribute("kind")?.Value == "src"))
                {
                    if (entry.Attribute("path")?.Value is { } path && !path.StartsWith('/')) sources.Add(Path.GetFullPath(Path.Combine(project, path)));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException)
        {
        }

        return (sources.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), resources.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>When the project's own record of its libraries last changed - its build file, its IDE settings, its lib folders.</summary>
    private static DateTime StampOf(string project)
    {
        string[] recorded =
        [
            "pom.xml", "build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts", "gradle.properties",
            Path.Combine("gradle", "libs.versions.toml"), ".classpath", Path.Combine(".vscode", "settings.json"),
            Path.Combine(".idea", "libraries"), "lib", "libs", "jars",
        ];

        var stamps = recorded.Select(name => Path.Combine(project, name))
            .Select(path => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : DateTime.MinValue);

        try
        {
            stamps = stamps.Concat(Directory.EnumerateFiles(project, "*.iml").Select(File.GetLastWriteTimeUtc));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return stamps.Max();
    }
}
