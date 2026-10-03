using System.Collections.Concurrent;
using System.IO.Compression;
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

    /// <summary>
    /// Where javac is to look for annotation processors - Lombok's, MapStruct's - which write code the program uses, such
    /// as the getters Lombok makes: the processors the build names, with what they need, or, when it names none, the
    /// program's libraries if one of them holds a processor. From JDK 23 javac runs no processor it is not told where to
    /// find, so without this a program that calls a Lombok getter would not build.
    /// </summary>
    public IReadOnlyList<string> ProcessorPath { get; init; } = [];

    /// <summary>
    /// JavaFX's own modules among the libraries - javafx.base, javafx.controls and the rest - which the program is run with on
    /// the module path, as JavaFX's documentation runs one: java will not start a class that extends
    /// javafx.application.Application with JavaFX on the class path, and says its "runtime components are missing".
    /// </summary>
    public IReadOnlyList<string> JavaFxModules { get; init; } = [];

    /// <summary>
    /// The other projects of its Gradle build whose code the program is built with: :core, for implementation project(':core'),
    /// and those :core uses in turn. Their source is on the source path, their resources beside its own, and their libraries
    /// among its own.
    /// </summary>
    public IReadOnlyList<string> ProjectsUsed { get; init; } = [];

    /// <summary>Projects of its Gradle build the program uses whose code FixFinder could not find, each with why.</summary>
    public IReadOnlyList<MissingProject> MissingProjects { get; init; } = [];

    /// <summary>The modules the projects used declare in a module-info.java of their own, each with the project's path.</summary>
    public IReadOnlyDictionary<string, string> ProjectModules { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>A few words for how a run explanation names the projects used: "the code of the project :core of its Gradle build".</summary>
    public string? ProjectsDescribed => ProjectsUsed.Count == 0 ? null
        : $"the code of {(ProjectsUsed.Count == 1 ? "the project" : "the projects")} {Listed(ProjectsUsed)} of its Gradle build";

    /// <summary>Things named one after another, as a sentence names them: a, b and c.</summary>
    internal static string Listed(IReadOnlyList<string> items) =>
        items.Count <= 1 ? string.Concat(items) : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";

    /// <summary>
    /// The program's source files to give javac by name besides the one it starts from: every one under its source roots
    /// when an annotation processor runs, since javac runs processors only on the files it is given, never on those it finds
    /// on the source path - a Lombok class in another file would get none of its getters; none when no processor runs.
    /// The tests' sources are left out unless the file is one of them, as a build compiles the program before its tests.
    /// </summary>
    public IReadOnlyList<string> SourcesToName(string javaFile)
    {
        if (ProcessorPath.Count == 0) return [];

        var own = ProgramLayout.JavaSourceRoot(javaFile);
        var roots = new[] { own }.Concat(OtherSourceRoots(javaFile)).Where(root => IsTestRoot(own) || !IsTestRoot(root));

        return roots.SelectMany(JavaFilesUnder)
            .Where(file => !string.Equals(Path.GetFullPath(file), Path.GetFullPath(javaFile), StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Whether a source root holds tests: Maven's and Gradle's src\test\java, or a folder an IDE's project calls test.</summary>
    private static bool IsTestRoot(string root)
    {
        var folder = new DirectoryInfo(root);
        bool Named(DirectoryInfo? directory, string name) => string.Equals(directory?.Name, name, StringComparison.OrdinalIgnoreCase);

        return Named(folder, "test") || Named(folder, "tests") || (Named(folder, "java") && Named(folder.Parent, "test"));
    }

    /// <summary>The .java files under a source root - the first 2000, which is more than any course project has.</summary>
    private static IReadOnlyList<string> JavaFilesUnder(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*.java", SearchOption.AllDirectories).Take(2000).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

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

    /// <summary>Whether the project names this library, whether or not it is on this computer.</summary>
    public bool Names(string group, string artifact)
    {
        var coordinate = $"{group}:{artifact}";
        if (Missing.Any(missing => missing.Name == coordinate || missing.Name.StartsWith(coordinate + ":", StringComparison.Ordinal))) return true;

        return ClassPath.Select(Path.GetFileNameWithoutExtension).Any(jar =>
            string.Equals(jar, artifact, StringComparison.OrdinalIgnoreCase) || jar!.StartsWith(artifact + "-", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The libraries the program this Java file belongs to is built with; nothing at all for a file with no project.</summary>
    public static JavaLibraries For(string javaFile)
    {
        var original = ProgramCopy.OriginalOf(Path.GetFullPath(javaFile));
        return ProjectOf(ProgramLayout.JavaSourceRoot(original)) is { } project ? OfProject(project) : None;
    }

    /// <summary>The libraries of the project a Java program ran in, from the folder it started in; nothing for a folder in no project.</summary>
    public static JavaLibraries ForFolder(string folder)
    {
        var original = ProgramCopy.OriginalOf(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar));
        return ProjectOf(original) is { } project ? OfProject(project) : None;
    }

    /// <summary>The libraries of the project in this folder, read once and again only when its build or settings change.</summary>
    private static JavaLibraries OfProject(string project)
    {
        var stamp = StampOf(project);
        if (Found.TryGetValue(project, out var known) && known.Stamp == stamp) return known.Libraries;

        var libraries = Read(project);

        // What is missing may be downloaded at any moment, by opening the project in an IDE - or, for a project of the build,
        // written - so it is never remembered.
        if (libraries.Missing.Count == 0 && libraries.MissingProjects.Count == 0) Found[project] = (stamp, libraries);
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

    /// <summary>
    /// Whether a folder is a project: it has a build file, or an IDE's record of its libraries, or it is one of the projects a
    /// Gradle build's settings include - which need not have a build file of its own.
    /// </summary>
    private static bool IsProject(string folder) =>
        BuildFiles.Any(name => File.Exists(Path.Combine(folder, name))) || IdeLibraries.Records(folder) || IncludedInAGradleBuild(folder);

    private static bool IncludedInAGradleBuild(string folder) => GradleSettings.Of(folder)?.PathOf(folder) is { } path && path != ":";

    private static readonly string[] BuildFiles = ["pom.xml", "build.gradle", "build.gradle.kts"];

    private static JavaLibraries Read(string project)
    {
        var stores = Stores();
        var mavenRepository = stores.Each.OfType<MavenRepository>().FirstOrDefault() ?? MavenRepository.ForThisUser();

        var pom = Path.Combine(project, "pom.xml");
        if (File.Exists(pom))
        {
            var resolver = new MavenResolver(stores);
            return Built(resolver.Resolve(pom), resolver.ProcessorsOf(pom), [], "pom.xml", project, StandardRoots(project));
        }

        if (GradleBuild.BuildFileIn(project) is not null || IncludedInAGradleBuild(project)) return FromGradle(project, GradleSettings.Of(project), stores);

        return Built(IdeLibraries.Read(project, mavenRepository), null, [], IdeLibraries.Describe(project), project, IdeRoots(project));
    }

    /// <summary>A project of its Gradle build that the program uses, with what that project declares, and whether only the program's tests use it.</summary>
    private sealed record UsedProject(string Path, string Folder, GradleBuild.Declared Declared, bool ForTests);

    /// <summary>
    /// The libraries of a Gradle project: those its build file - and the build it is part of - declare, with the code of the
    /// other projects of the build it uses and the libraries those declare for their own code.
    /// </summary>
    private static JavaLibraries FromGradle(string project, GradleSettings.Build? build, LibraryStores stores)
    {
        var declared = GradleBuild.ReadProject(project, build);
        var (used, missingProjects) = ProjectsUsedBy(project, declared, build);

        // What a project used declares for its own code comes with it; for the program's tests alone, when only they use it.
        var dependencies = declared.Dependencies.Concat(used.SelectMany(each => each.Declared.Dependencies
            .Where(dependency => dependency.Scope != "test")
            .Select(dependency => each.ForTests ? dependency with { Scope = "test" } : dependency))).ToList();
        var files = declared.Files.Concat(used.SelectMany(each => each.Declared.Files.Where(file => !file.Test).Select(file => (file.Jar, Test: each.ForTests)))).ToList();
        var platforms = declared.Platforms.Concat(used.SelectMany(each => each.Declared.Platforms)).Distinct().ToList();

        var resolver = new MavenResolver(stores, highestVersionWins: true);
        var managed = resolver.ManagedBy(platforms);
        var resolved = resolver.Resolve(dependencies, managed);

        resolved = resolved with
        {
            Main = [.. resolved.Main, .. files.Where(file => !file.Test).Select(file => file.Jar)],
            Test = [.. resolved.Test, .. files.Where(file => file.Test).Select(file => file.Jar)],
        };

        // A project used is compiled with the program, from its source, so the processors it names run on it too.
        ResolvedLibraries? processors = null;
        if (declared.NamesProcessors || used.Any(each => each.Declared.NamesProcessors))
        {
            var named = resolver.Resolve([.. declared.Processors, .. used.SelectMany(each => each.Declared.Processors)], managed);
            processors = named with { Main = [.. named.Main, .. named.Test, .. declared.ProcessorFiles, .. used.SelectMany(each => each.Declared.ProcessorFiles)] };
        }

        // Files of the build are named from its top folder - app\build.gradle - and a line not read from any but the
        // project's own build file says which it is in.
        var top = build?.RootFolder ?? project;
        var ownBuildFile = GradleBuild.BuildFileIn(project);
        string FromTop(string file) => Path.GetRelativePath(top, file);
        string Written(GradleBuild.UnreadLine line) =>
            string.Equals(line.File, ownBuildFile, StringComparison.OrdinalIgnoreCase) ? line.Text : $"{line.Text} (in {FromTop(line.File)})";

        var notRead = declared.NotRead.Concat(used.SelectMany(each => each.Declared.NotRead)).Select(Written).Distinct(StringComparer.Ordinal).ToList();
        var readFrom = declared.ReadFrom.Concat(used.SelectMany(each => each.Declared.ReadFrom)).Distinct(StringComparer.OrdinalIgnoreCase).Select(FromTop).ToList();
        var declaredIn = readFrom.Count > 0 ? Listed(readFrom) : $"the Gradle build in {Path.GetFileName(top)}";

        var (ownSources, ownResources) = StandardRoots(project);
        IReadOnlyList<string> sources = [.. ownSources, .. used.Select(each => Path.Combine(each.Folder, "src", "main", "java")).Where(Directory.Exists)];
        IReadOnlyList<string> resources = [.. ownResources, .. used.Select(each => Path.Combine(each.Folder, "src", "main", "resources")).Where(Directory.Exists)];

        var projectModules = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var each in used)
        {
            if (JavaModules.In(Path.Combine(each.Folder, "src", "main", "java")) is { } module) projectModules.TryAdd(module.Name, each.Path);
        }

        return Built(resolved, processors, notRead, declaredIn, project, (sources, resources)) with
        {
            ProjectsUsed = used.Select(each => each.Path).ToList(),
            MissingProjects = missingProjects,
            ProjectModules = projectModules,
        };
    }

    /// <summary>
    /// The projects of its Gradle build a project uses - those its build file names, as implementation project(':core'), and
    /// those they use in turn for their own code - and those it names that FixFinder could not find, each with why.
    /// </summary>
    private static (IReadOnlyList<UsedProject> Used, IReadOnlyList<MissingProject> Missing) ProjectsUsedBy(
        string project, GradleBuild.Declared declared, GradleSettings.Build? build)
    {
        var used = new List<UsedProject>();
        var missing = new List<MissingProject>();
        var reached = new HashSet<string>(StringComparer.Ordinal) { build?.PathOf(project) ?? ":" };
        var settings = build is null ? null : Path.GetFileName(build.SettingsFile);

        void Reach(IEnumerable<string> paths, bool forTests)
        {
            foreach (var path in paths)
            {
                if (!reached.Add(path)) continue;

                var folder = build?.FolderOf(path);

                if (folder is null || !Directory.Exists(folder))
                {
                    missing.Add(new MissingProject(path,
                        settings is null ? "FixFinder found no settings.gradle in the project's folder or above it to say which projects its build has"
                        : folder is null ? $"FixFinder did not find it among the projects {settings} includes"
                        : $"{settings} includes it, in {Path.GetRelativePath(build!.RootFolder, folder)}, but that folder is not there"));
                    continue;
                }

                var its = GradleBuild.ReadProject(folder, build);
                used.Add(new UsedProject(path, folder, its, forTests));

                // What a project uses for its own code comes with it; what it uses for its own tests does not.
                Reach(its.Projects.Where(named => !named.Test).Select(named => named.Path), forTests);
            }
        }

        Reach(declared.Projects.Where(named => !named.Test).Select(named => named.Path), forTests: false);
        Reach(declared.Projects.Where(named => named.Test).Select(named => named.Path), forTests: true);

        return (used, missing);
    }

    /// <param name="namedProcessors">The annotation processors the build names, or null when it names none.</param>
    private static JavaLibraries Built(
        ResolvedLibraries resolved, ResolvedLibraries? namedProcessors, IReadOnlyList<string> notRead, string declaredIn, string project,
        (IReadOnlyList<string> Sources, IReadOnlyList<string> Resources) roots)
    {
        var missingProcessors = namedProcessors?.Missing.Where(processor => !resolved.Missing.Any(library => library.Name == processor.Name)) ?? [];

        var libraries = new JavaLibraries(resolved.Main, resolved.Test.Where(jar => !resolved.Main.Contains(jar, StringComparer.OrdinalIgnoreCase)).ToList(),
            roots.Sources, roots.Resources, [.. resolved.Missing, .. missingProcessors], notRead, declaredIn, project);

        // A build that names its processors has javac look for them there and nowhere else. One that names none has javac
        // look among its libraries, as javac did by default until JDK 23 - which is only worth doing when one holds a processor.
        var processorPath = namedProcessors is not null ? namedProcessors.Main
            : libraries.ClassPath.Any(HoldsAProcessor) ? libraries.ClassPath
            : [];

        return libraries with
        {
            ProcessorPath = processorPath,
            JavaFxModules = libraries.ClassPath.Where(IsJavaFxModule).ToList(),
        };
    }

    /// <summary>Whether a jar says it holds an annotation processor, as javac finds one: by the service file naming it.</summary>
    private static bool HoldsAProcessor(string jar) => Holds(jar, "META-INF/services/javax.annotation.processing.Processor");

    /// <summary>
    /// Whether a jar is one of JavaFX's modules: named for JavaFX, with a module-info.class - which the empty jars Maven
    /// Central keeps beside JavaFX's jars for each computer do not have.
    /// </summary>
    private static bool IsJavaFxModule(string jar) =>
        Path.GetFileName(jar).StartsWith("javafx", StringComparison.OrdinalIgnoreCase) && Holds(jar, "module-info.class");

    private static bool Holds(string jar, string entry)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jar);
            return archive.GetEntry(entry) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            return false;
        }
    }

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
    internal static DateTime StampOf(string project)
    {
        string[] recorded =
        [
            "pom.xml", "build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts", "gradle.properties",
            Path.Combine("gradle", "libs.versions.toml"), ".classpath", Path.Combine(".vscode", "settings.json"),
            Path.Combine(".idea", "libraries"), Path.Combine(".idea", "misc.xml"), Path.Combine(".settings", "org.eclipse.jdt.core.prefs"),
            "lib", "libs", "jars",
        ];

        // A project of a Gradle build reads the build's settings and other build files too.
        var paths = recorded.Select(name => Path.Combine(project, name));
        if (GradleSettings.Of(project) is { } build) paths = paths.Concat(GradleBuild.FilesOf(build));

        var stamps = paths.Select(path => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : DateTime.MinValue);

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
