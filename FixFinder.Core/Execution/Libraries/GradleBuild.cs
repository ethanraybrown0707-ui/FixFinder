using System.Text;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution.Libraries;

/// <summary>
/// What a Gradle build file declares that the program needs, read from the forms a course project's build file is written
/// in: implementation 'group:artifact:version' and testImplementation("..."), the group:/name:/version: form, a platform()
/// BOM, a version catalog's libs.name, and files() or fileTree() of jars beside it - for the code, for its tests, and for
/// annotationProcessor. Anything written another way is named as not read, rather than guessed at.
/// </summary>
/// <remarks>
/// Gradle itself is not run: it downloads what it needs and takes time to start, and FixFinder neither downloads nor
/// waits on a build tool. What the build file names is looked up among what Gradle and Maven have already downloaded.
///
/// A project of a build of several projects is read with what the build gives it besides: the version catalog and
/// gradle.properties in the build's top folder, the dependencies the build files above it give allprojects { } and
/// subprojects { }, and the build's own convention plugins it applies, written as .gradle files in buildSrc.
/// </remarks>
public static partial class GradleBuild
{
    /// <summary>What a build file declares: libraries, the BOMs that set their versions, jar files, and lines not read.</summary>
    /// <param name="Processors">What it gives annotationProcessor - the only place Gradle looks for annotation processors.</param>
    /// <param name="ProcessorFiles">Jar files it gives annotationProcessor with files() or fileTree().</param>
    /// <param name="NamesProcessors">Whether it gives annotationProcessor anything at all, read or not.</param>
    /// <param name="Projects">The other projects of its build it uses, by path - :core - and whether only for its tests.</param>
    /// <param name="ReadFrom">The files what it declares was read from: its own build file, and any of the build's that give it more.</param>
    public sealed record Declared(
        IReadOnlyList<DeclaredDependency> Dependencies,
        IReadOnlyList<LibraryName> Platforms,
        IReadOnlyList<(string Jar, bool Test)> Files,
        IReadOnlyList<UnreadLine> NotRead,
        IReadOnlyList<DeclaredDependency> Processors,
        IReadOnlyList<string> ProcessorFiles,
        bool NamesProcessors,
        IReadOnlyList<(string Path, bool Test)> Projects,
        IReadOnlyList<string> ReadFrom);

    /// <summary>A line of a build file that FixFinder could not read, and the file it is in.</summary>
    public sealed record UnreadLine(string Text, string File);

    /// <summary>The names a Gradle build file has: Groovy's, then Kotlin's.</summary>
    public static readonly IReadOnlyList<string> BuildFileNames = ["build.gradle", "build.gradle.kts"];

    /// <summary>The build file in a project's folder, or null when it has none - as a project of a bigger build need not.</summary>
    public static string? BuildFileIn(string projectFolder) =>
        BuildFileNames.Select(name => Path.Combine(projectFolder, name)).FirstOrDefault(File.Exists);

    /// <summary>Which projects a dependencies { } block of a build file is for, by the block it is written in.</summary>
    private enum BlockFor
    {
        /// <summary>The build file's own project: a block written at the top of the file.</summary>
        ThisProject,

        /// <summary>The build file's project and every one below it: in allprojects { } or configure(allprojects) { }.</summary>
        AllProjects,

        /// <summary>Every project below the build file's: in subprojects { } or configure(subprojects) { }.</summary>
        SubProjects,

        /// <summary>The one project project(':app') { } names.</summary>
        NamedProject,

        /// <summary>Projects chosen as the build runs - configure(subprojects.findAll { ... }) { } - which cannot be told here.</summary>
        ChosenProjects,
    }

    private sealed record DependencyBlock(string Text, BlockFor For, string? NamedPath);

    /// <summary>The configurations a program's own code and its tests are built and run with; others belong to the build.</summary>
    private static readonly Dictionary<string, bool> ConfigurationIsTest = new(StringComparer.Ordinal)
    {
        ["implementation"] = false, ["api"] = false, ["compileOnly"] = false, ["compileOnlyApi"] = false, ["runtimeOnly"] = false,
        ["compile"] = false, ["runtime"] = false, ["testImplementation"] = true, ["testCompileOnly"] = true,
        ["testRuntimeOnly"] = true, ["testCompile"] = true, ["testRuntime"] = true,
    };

    /// <summary>The configurations that give javac its annotation processors - Lombok's, MapStruct's - for the code and its tests.</summary>
    private static readonly HashSet<string> ProcessorConfigurations = new(StringComparer.Ordinal) { "annotationProcessor", "testAnnotationProcessor" };

    [GeneratedRegex(@"(?m)^\s*(?<configuration>[a-zA-Z]+)\s*(?:\(\s*)?(?<rest>.+?)\s*$")]
    private static partial Regex DeclarationLine();

    [GeneratedRegex(@"^(?:platform|enforcedPlatform)\s*\(\s*(?<what>.+?)\s*\)\s*\)?\s*$")]
    private static partial Regex Platform();

    [GeneratedRegex(@"^(?<quote>['""])(?<notation>[^'""]+)\k<quote>\s*\)?(?:\s*\{.*)?$")]
    private static partial Regex StringNotation();

    [GeneratedRegex(@"group\s*[:=]\s*['""](?<group>[^'""]+)['""]\s*,\s*name\s*[:=]\s*['""](?<name>[^'""]+)['""](?:\s*,\s*version\s*[:=]\s*['""](?<version>[^'""]+)['""])?")]
    private static partial Regex MapNotation();

    [GeneratedRegex(@"^libs\.(?<alias>[\w.]+?)\s*\)?\s*$")]
    private static partial Regex CatalogAlias();

    [GeneratedRegex(@"^(?<kind>files|fileTree)\s*\((?<arguments>.*)\)\s*\)?\s*$")]
    private static partial Regex FileDependency();

    [GeneratedRegex(@"['""](?<text>[^'""]+)['""]")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"(?m)^\s*(?:def|val|var|ext\.|extra\[""|set\(\s*"")\s*(?<name>\w+)[""\]]*\s*(?:=|,)\s*['""](?<value>[^'""$]+)['""]")]
    private static partial Regex Variable();

    [GeneratedRegex(@"\$\{(?<name>\w+)\}|\$(?<name>\w+)")]
    private static partial Regex Interpolation();

    [GeneratedRegex(@"id\s*\(?\s*['""]org\.springframework\.boot['""]\s*\)?\s*version\s*\(?\s*['""](?<version>[^'""]+)['""]")]
    private static partial Regex SpringBootPlugin();

    /// <summary>Spring Boot's plugin applied, with its version or without - as a project of a bigger build applies it, the version given above.</summary>
    [GeneratedRegex(@"(?:\bid\s*\(?\s*|\bapply\s+plugin\s*:\s*)['""]org\.springframework\.boot['""]")]
    private static partial Regex AppliesSpringBoot();

    /// <summary>project(':core') as a dependency, or project(path: ':core'); the path is named from the project naming it unless it starts with a colon.</summary>
    [GeneratedRegex(@"^project\s*\(\s*(?:path\s*[:=]\s*)?['""](?<path>[^'""]+)['""]\s*\)\s*\)?\s*$")]
    private static partial Regex ProjectDependency();

    /// <summary>A plugin a build file applies: id 'name' in its plugins { } block, or apply plugin: 'name'.</summary>
    [GeneratedRegex(@"(?:\bid\s*\(?\s*|\bapply\s+plugin\s*:\s*)['""](?<id>[A-Za-z][\w.-]*)['""]")]
    private static partial Regex PluginId();

    /// <summary>project(':app') { at the place it starts: a block that configures that one project.</summary>
    [GeneratedRegex(@"\Gproject\s*\(\s*['""](?<path>[^'""]+)['""]\s*\)\s*\{")]
    private static partial Regex NamedProjectBlock();

    /// <summary>configure(...) { at the place it starts, with what it configures: subprojects, allprojects, or projects chosen some other way.</summary>
    [GeneratedRegex(@"\Gconfigure\s*\(\s*(?:(?<which>subprojects|allprojects)\s*\)|[^{}]*?(?:\{[^{}]*\}[^{}]*?)*\))\s*\{")]
    private static partial Regex ConfigureBlock();

    /// <summary>
    /// JavaFX's own modules as the JavaFX plugin, org.openjfx.javafxplugin, knows them, each with the modules it needs - from the
    /// plugin's own list of them.
    /// </summary>
    private static readonly Dictionary<string, string[]> JavaFxModulesNeeded = new(StringComparer.Ordinal)
    {
        ["javafx.base"] = [],
        ["javafx.graphics"] = ["javafx.base"],
        ["javafx.controls"] = ["javafx.base", "javafx.graphics"],
        ["javafx.fxml"] = ["javafx.base", "javafx.graphics"],
        ["javafx.media"] = ["javafx.base", "javafx.graphics"],
        ["javafx.swing"] = ["javafx.base", "javafx.graphics"],
        ["javafx.web"] = ["javafx.base", "javafx.controls", "javafx.graphics", "javafx.media"],
    };

    /// <summary>version = "21", version '21', or version = fxVersion - a value set elsewhere in the build file.</summary>
    [GeneratedRegex(@"\bversion\s*(?:=\s*)?\(?\s*(?:['""](?<version>[^'""]+)['""]|(?<name>[A-Za-z_]\w*)\s*\)?\s*$)", RegexOptions.Multiline)]
    private static partial Regex JavaFxVersion();

    [GeneratedRegex(@"\bsdk\s*(?:=\s*)?\(?\s*['""](?<sdk>[^'""]+)['""]")]
    private static partial Regex JavaFxSdk();

    [GeneratedRegex(@"\bconfigurations?\s*(?:=\s*)?(?<names>[^\r\n]*)")]
    private static partial Regex JavaFxConfigurations();

    /// <summary>What a build file declares, for the project in its folder on its own.</summary>
    public static Declared Read(string buildFile)
    {
        var path = Path.GetFullPath(buildFile);
        return Read(path, Path.GetDirectoryName(path)!, build: null);
    }

    /// <summary>
    /// What a project of a Gradle build declares: what its own build file - if it has one - declares, with what the build
    /// gives it besides. Null for <paramref name="build"/> reads it as a project on its own.
    /// </summary>
    public static Declared ReadProject(string projectFolder, GradleSettings.Build? build)
    {
        var folder = Path.GetFullPath(projectFolder).TrimEnd('\\', '/');
        return Read(BuildFileIn(folder), folder, build);
    }

    private static Declared Read(string? buildFile, string projectFolder, GradleSettings.Build? build)
    {
        var top = build?.RootFolder ?? projectFolder;
        var ownPath = build?.PathOf(projectFolder) ?? ":";
        var ownText = TextOf(buildFile);

        // The projects above this one, from the top of the build down, each with its build file if it has one.
        var above = build is null ? [] : GradleSettings.Build.Above(ownPath)
            .Select(path => (Path: path, Folder: build.FolderOf(path)))
            .Where(project => project.Folder is not null)
            .Select(project => (project.Path, Folder: project.Folder!, File: BuildFileIn(project.Folder!)))
            .Select(project => (project.Path, project.Folder, project.File, Text: TextOf(project.File)))
            .ToList();

        // Values set above - in gradle.properties or with ext - are seen below, unless set again there.
        var variables = new Dictionary<string, string>(StringComparer.Ordinal) { ["rootDir"] = top, ["projectDir"] = projectFolder };
        foreach (var project in above) AddVariables(variables, project.Text, project.Folder);
        AddVariables(variables, ownText, projectFolder);

        var catalog = VersionCatalog.Read(Path.Combine(top, "gradle", "libs.versions.toml"));
        var conventions = buildFile is null ? [] : ConventionPlugins(ownText, top, build);

        // Each dependencies block that is this project's, with the file it is in - or that may be, which cannot be told.
        var blocks = new List<(string Text, string File, bool Unsure)>();
        var readFrom = new List<string>();

        // isThisProjects says, of a block, true, false, or null for "it may be".
        void Take(string file, string text, Func<DependencyBlock, bool?> isThisProjects)
        {
            var taken = DependencyBlocks(text).Select(block => (block.Text, IsThisProjects: isThisProjects(block))).Where(block => block.IsThisProjects != false).ToList();
            if (taken.Count == 0 && file != buildFile) return;

            readFrom.Add(file);
            blocks.AddRange(taken.Select(block => (block.Text, file, Unsure: block.IsThisProjects is null)));
        }

        if (buildFile is not null) Take(buildFile, ownText, block => block.For is BlockFor.ThisProject or BlockFor.AllProjects);

        foreach (var (plugin, pluginText) in conventions) Take(plugin, pluginText, block => block.For is BlockFor.ThisProject or BlockFor.AllProjects);

        foreach (var project in above.Where(project => project.File is not null))
        {
            Take(project.File!, project.Text, block => block.For switch
            {
                BlockFor.AllProjects or BlockFor.SubProjects => true,
                BlockFor.NamedProject => GradleSettings.Build.PathNamed(block.NamedPath!, project.Path) == ownPath,
                BlockFor.ChosenProjects => null,
                _ => false,
            });
        }

        var dependencies = new List<DeclaredDependency>();
        var platforms = new List<LibraryName>();
        var files = new List<(string, bool)>();
        var notRead = new List<UnreadLine>();
        var processors = new List<DeclaredDependency>();
        var processorFiles = new List<string>();
        var projects = new List<(string, bool)>();
        var namesProcessors = false;

        // Spring Boot's plugin sets the versions of what it brings. A project of a bigger build often applies it without a
        // version, which the build file of the top folder gives.
        var applying = new[] { ownText }.Concat(conventions.Select(plugin => plugin.Text)).ToList();
        if (applying.Any(AppliesSpringBoot().IsMatch) &&
            applying.Concat(above.Select(project => project.Text)).Select(text => SpringBootPlugin().Match(text)).FirstOrDefault(match => match.Success) is { } boot)
        {
            platforms.Add(new LibraryName("org.springframework.boot", "spring-boot-dependencies", boot.Groups["version"].Value));
        }

        // The JavaFX plugin adds the JavaFX modules its javafx { } block names - in the build file, or a convention plugin.
        if (applying.Any(text => PluginId().Matches(text).Any(plugin => plugin.Groups["id"].Value == "org.openjfx.javafxplugin")) &&
            conventions.Prepend((File: buildFile ?? "", Text: ownText)).FirstOrDefault(each => each.File.Length > 0 && BlockNamed(each.Text, "javafx") is not null) is
                { File.Length: > 0 } configuring)
        {
            var (javaFx, unreadJavaFx) = JavaFxOf(BlockNamed(configuring.Text, "javafx")!, configuring.File, variables, projectFolder);
            dependencies.AddRange(javaFx);
            notRead.AddRange(unreadJavaFx);
            if (!readFrom.Contains(configuring.File)) readFrom.Add(configuring.File);
        }

        foreach (var (block, file, unsure) in blocks)
        {
            foreach (Match line in DeclarationLine().Matches(block))
            {
                var configuration = line.Groups["configuration"].Value;
                var givesProcessors = ProcessorConfigurations.Contains(configuration);
                var test = false;
                if (!givesProcessors && !ConfigurationIsTest.TryGetValue(configuration, out test)) continue;

                var unread = new UnreadLine(line.Value.Trim(), file);
                if (unsure)
                {
                    notRead.Add(unread);
                    continue;
                }

                namesProcessors |= givesProcessors;
                var libraries = givesProcessors ? processors : dependencies;
                var rest = Fill(line.Groups["rest"].Value.Trim(), variables);

                if (Platform().Match(rest) is { Success: true } platform)
                {
                    var what = platform.Groups["what"].Value;
                    if (Library(what, catalog) is { Version: { } version } bom) platforms.Add(new LibraryName(bom.Group, bom.Artifact, version));
                    else notRead.Add(unread);
                }
                else if (FileDependency().Match(rest) is { Success: true } local)
                {
                    // files('libs/a.jar') is the folder of the project it is for, wherever the block is written - as Gradle reads it.
                    var jars = Jars(local.Groups["kind"].Value, local.Groups["arguments"].Value, projectFolder);
                    if (givesProcessors) processorFiles.AddRange(jars);
                    else files.AddRange(jars.Select(jar => (jar, test)));
                }
                else if (!givesProcessors && ProjectDependency().Match(rest) is { Success: true } project)
                {
                    projects.Add((GradleSettings.Build.PathNamed(project.Groups["path"].Value, ownPath), test));
                }
                else if (CatalogAlias().Match(rest) is { Success: true } alias && alias.Groups["alias"].Value.StartsWith("bundles.", StringComparison.Ordinal))
                {
                    var bundle = catalog.Bundle(alias.Groups["alias"].Value["bundles.".Length..]);
                    if (bundle is null) notRead.Add(unread);
                    else libraries.AddRange(bundle.Select(library => library with { Scope = test ? "test" : null }));
                }
                else if (Library(rest, catalog) is { } library)
                {
                    libraries.Add(library with { Scope = test ? "test" : null });
                }
                else
                {
                    notRead.Add(unread);
                }
            }
        }

        return new Declared(dependencies, platforms, files, notRead, processors, processorFiles, namesProcessors, projects.Distinct().ToList(), readFrom);
    }

    /// <summary>
    /// The build's own plugins a build file applies - convention plugins, written as .gradle files in buildSrc or in a build
    /// the settings include, such as build-logic - with each one's text, and those they apply in turn. Gradle's own plugins,
    /// and those it downloads, are not among them.
    /// </summary>
    private static IReadOnlyList<(string File, string Text)> ConventionPlugins(string text, string top, GradleSettings.Build? build)
    {
        var places = new[] { Path.Combine(top, "buildSrc") }.Concat(build?.IncludedBuilds ?? []).ToList();
        var found = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var applying = new Queue<string>([text]);

        while (applying.TryDequeue(out var next))
        {
            foreach (var id in PluginId().Matches(next).Select(plugin => plugin.Groups["id"].Value))
            {
                var file = places
                    .SelectMany(place => new[] { Path.Combine(place, "src", "main", "groovy", id + ".gradle"), Path.Combine(place, "src", "main", "kotlin", id + ".gradle.kts") })
                    .FirstOrDefault(File.Exists);

                if (file is null || !seen.Add(file)) continue;

                var pluginText = TextOf(file);
                found.Add((file, pluginText));
                applying.Enqueue(pluginText);
            }
        }

        return found;
    }

    /// <summary>
    /// The name the JavaFX plugin gives this computer's jars of JavaFX - win, mac, mac-aarch64, linux or linux-aarch64, the
    /// classifier of each jar of JavaFX on Maven Central - or null for a computer it has none for.
    /// </summary>
    internal static string? JavaFxPlatform()
    {
        var architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
        var x64 = architecture == System.Runtime.InteropServices.Architecture.X64;
        var arm64 = architecture == System.Runtime.InteropServices.Architecture.Arm64;

        return OperatingSystem.IsWindows() && x64 ? "win"
            : OperatingSystem.IsMacOS() && x64 ? "mac"
            : OperatingSystem.IsMacOS() && arm64 ? "mac-aarch64"
            : OperatingSystem.IsLinux() && x64 ? "linux"
            : OperatingSystem.IsLinux() && arm64 ? "linux-aarch64"
            : null;
    }

    /// <summary>
    /// What the JavaFX plugin adds for a javafx { } block: the JavaFX modules it names, and those they need, at the version it
    /// gives - each as the jar of it for this computer, which is what the plugin has Gradle download, or from the lib folder
    /// of the JavaFX SDK it names with sdk. The jars are taken as they are, with nothing their pom.xml would bring, as the
    /// plugin's own list says what each module needs.
    /// </summary>
    private static (IReadOnlyList<DeclaredDependency> Dependencies, IReadOnlyList<UnreadLine> NotRead) JavaFxOf(
        string block, string file, IReadOnlyDictionary<string, string> variables, string projectFolder)
    {
        var filled = Fill(block, variables);
        var lines = block.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        UnreadLine LineNaming(string what) => new(lines.FirstOrDefault(line => line.Contains(what, StringComparison.Ordinal)) ?? $"javafx {{ {what} }}", file);

        var named = Quoted().Matches(filled).Select(quoted => quoted.Groups["text"].Value)
            .Where(name => name.StartsWith("javafx.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var notRead = named.Where(name => !JavaFxModulesNeeded.ContainsKey(name)).Select(LineNaming).ToList();

        var modules = named.Where(JavaFxModulesNeeded.ContainsKey)
            .SelectMany(name => JavaFxModulesNeeded[name].Append(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (modules.Count == 0) return ([], notRead);

        static string ArtifactOf(string module) => "javafx-" + module["javafx.".Length..];

        if (JavaFxSdk().Match(filled) is { Success: true } sdk)
        {
            // An SDK's lib folder holds a jar for each module, named for it - javafx.controls.jar - taken as a jar named by its path is.
            var lib = Path.Combine(Path.GetFullPath(Path.Combine(projectFolder, sdk.Groups["sdk"].Value)), "lib");
            return (modules.Select(module => new DeclaredDependency("org.openjfx", ArtifactOf(module), null, Scope: "system", SystemPath: Path.Combine(lib, module + ".jar"))).ToList(), notRead);
        }

        // The plugin adds them to implementation unless told otherwise; to the tests alone only when every configuration it is given is the tests'.
        var configurations = JavaFxConfigurations().Match(filled) is { Success: true } given
            ? Quoted().Matches(given.Groups["names"].Value).Select(quoted => quoted.Groups["text"].Value).ToList()
            : [];
        var forTests = configurations.Count > 0 && configurations.All(configuration => ConfigurationIsTest.TryGetValue(configuration, out var test) && test);

        // Without a version it can read, or a name for this computer's jars, which jars they are cannot be told.
        var version = JavaFxVersion().Match(filled) is { Success: true } written
            ? written.Groups["version"].Success ? written.Groups["version"].Value : variables.GetValueOrDefault(written.Groups["name"].Value)
            : null;

        if (version is null || version.Contains('$') || JavaFxPlatform() is not { } platform) return ([], [.. notRead, LineNaming("modules")]);

        return (modules.Select(module => new DeclaredDependency("org.openjfx", ArtifactOf(module), version,
            Scope: forTests ? "test" : null, Classifier: platform, Exclusions: [("*", "*")])).ToList(), notRead);
    }

    /// <summary>The text inside the first block of this name - javafx { } - or null when the build file has none.</summary>
    private static string? BlockNamed(string text, string name)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (IsWordAt(text, i) && Starts(text, i, name) && OpensBlock(text, i + name.Length, out var opening) && ClosingBrace(text, opening) is var close and >= 0)
                return text[(opening + 1)..close];
        }

        return null;
    }

    /// <summary>
    /// The files of a Gradle build whose changes can change what its projects declare: its settings, each project's build
    /// file and gradle.properties, its version catalog, and its convention plugins.
    /// </summary>
    public static IEnumerable<string> FilesOf(GradleSettings.Build build)
    {
        yield return build.SettingsFile;
        yield return Path.Combine(build.RootFolder, "gradle", "libs.versions.toml");

        foreach (var folder in build.ProjectFolders.Values.Prepend(build.RootFolder))
        {
            foreach (var name in BuildFileNames) yield return Path.Combine(folder, name);
            yield return Path.Combine(folder, "gradle.properties");
        }

        foreach (var place in build.IncludedBuilds.Prepend(Path.Combine(build.RootFolder, "buildSrc")))
        {
            foreach (var language in new[] { "groovy", "kotlin" })
            {
                var plugins = Path.Combine(place, "src", "main", language);
                yield return plugins;

                string[] written;
                try
                {
                    written = Directory.Exists(plugins) ? Directory.GetFiles(plugins, "*.gradle*") : [];
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    written = [];
                }

                foreach (var plugin in written) yield return plugin;
            }
        }
    }

    private static string TextOf(string? buildFile) => buildFile is null ? "" : WithoutComments(ReadAllText(buildFile));

    /// <summary>One library, written as "group:artifact:version", as group:/name:/version:, or as a version catalog alias.</summary>
    private static DeclaredDependency? Library(string written, VersionCatalog catalog)
    {
        if (StringNotation().Match(written) is { Success: true } notation)
        {
            var coordinates = notation.Groups["notation"].Value;
            var extension = coordinates.Contains('@') ? coordinates[(coordinates.IndexOf('@') + 1)..] : "jar";
            var parts = coordinates.Split('@')[0].Split(':');

            if (parts.Length < 2 || parts.Any(part => part.Contains('$'))) return null;

            return new DeclaredDependency(
                parts[0], parts[1], parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null,
                Type: extension == "jar" ? "jar" : extension, Classifier: parts.Length > 3 ? parts[3] : "");
        }

        if (MapNotation().Match(written) is { Success: true } map)
        {
            return new DeclaredDependency(map.Groups["group"].Value, map.Groups["name"].Value, map.Groups["version"].Success ? map.Groups["version"].Value : null);
        }

        return CatalogAlias().Match(written) is { Success: true } alias ? catalog.Library(alias.Groups["alias"].Value) : null;
    }

    /// <summary>The jars files('lib/a.jar') or fileTree(dir: 'libs', include: ['*.jar']) names, as files beside the build.</summary>
    private static IEnumerable<string> Jars(string kind, string arguments, string folder)
    {
        var named = Quoted().Matches(arguments).Select(match => match.Groups["text"].Value).ToList();

        if (kind == "files") return named.Select(file => Path.GetFullPath(Path.Combine(folder, file))).Where(File.Exists);

        // fileTree: the first path is the folder; its jars are taken whatever the include says, as every jar is what one is for.
        var tree = named.FirstOrDefault(name => !name.Contains('*') && name is not ("dir" or "include")) is { } dir ? Path.Combine(folder, dir) : null;
        if (tree is null || !Directory.Exists(tree)) return [];

        try
        {
            return Directory.EnumerateFiles(tree, "*.jar", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The text inside each dependencies { } block - not the one in buildscript { }, which is Gradle's own - with which
    /// projects it is for, by the outermost block it is in: allprojects { }, subprojects { }, project(':app') { }, a
    /// configure(...) { } of some of them, or none of those, for the build file's own project.
    /// </summary>
    private static IReadOnlyList<DependencyBlock> DependencyBlocks(string text)
    {
        var blocks = new List<DependencyBlock>();
        var depth = 0;
        var insideBuildscript = -1;
        (BlockFor For, string? NamedPath, int Depth)? configuring = null;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (insideBuildscript >= 0 && depth < insideBuildscript) insideBuildscript = -1;
                if (configuring is { } open && depth < open.Depth) configuring = null;
            }

            if (!IsWordAt(text, i)) continue;

            if (Starts(text, i, "buildscript") && OpensBlock(text, i + "buildscript".Length, out _)) insideBuildscript = depth + 1;

            if (configuring is null)
            {
                if (Starts(text, i, "allprojects") && OpensBlock(text, i + "allprojects".Length, out _)) configuring = (BlockFor.AllProjects, null, depth + 1);
                else if (Starts(text, i, "subprojects") && OpensBlock(text, i + "subprojects".Length, out _)) configuring = (BlockFor.SubProjects, null, depth + 1);
                else if (NamedProjectBlock().Match(text, i) is { Success: true } named) configuring = (BlockFor.NamedProject, named.Groups["path"].Value, depth + 1);
                else if (ConfigureBlock().Match(text, i) is { Success: true } configure)
                {
                    var which = configure.Groups["which"].Value switch
                    {
                        "allprojects" => BlockFor.AllProjects,
                        "subprojects" => BlockFor.SubProjects,
                        _ => BlockFor.ChosenProjects,
                    };

                    // The braces of a findAll { } before the block's own are passed over, as they open and close within it.
                    configuring = (which, null, depth + 1);
                    i = configure.Index + configure.Length - 1;
                    depth++;
                    continue;
                }
            }

            if (insideBuildscript < 0 && Starts(text, i, "dependencies") && OpensBlock(text, i + "dependencies".Length, out var opening) &&
                ClosingBrace(text, opening) is var close and >= 0)
            {
                blocks.Add(new DependencyBlock(text[(opening + 1)..close], configuring?.For ?? BlockFor.ThisProject, configuring?.NamedPath));
                i = close;
            }
        }

        return blocks;
    }

    private static bool IsWordAt(string text, int index) => index == 0 || !(char.IsLetterOrDigit(text[index - 1]) || text[index - 1] == '_' || text[index - 1] == '.');

    private static bool Starts(string text, int index, string word) =>
        string.CompareOrdinal(text, index, word, 0, word.Length) == 0 &&
        (index + word.Length == text.Length || !char.IsLetterOrDigit(text[index + word.Length]));

    private static bool OpensBlock(string text, int from, out int brace)
    {
        brace = from;
        while (brace < text.Length && char.IsWhiteSpace(text[brace])) brace++;
        return brace < text.Length && text[brace] == '{';
    }

    private static int ClosingBrace(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return i;
        }

        return -1;
    }

    /// <summary>Adds the names a build file gives values to - def, val, ext. - and those gradle.properties beside it sets.</summary>
    private static void AddVariables(Dictionary<string, string> variables, string text, string folder)
    {
        var properties = Path.Combine(folder, "gradle.properties");
        if (File.Exists(properties))
        {
            foreach (var line in ReadAllText(properties).Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] is '#' or '!' || trimmed.IndexOf('=') is not (> 0 and var equals)) continue;
                variables[trimmed[..equals].Trim()] = trimmed[(equals + 1)..].Trim();
            }
        }

        foreach (Match variable in Variable().Matches(text)) variables[variable.Groups["name"].Value] = variable.Groups["value"].Value;

        // ext { junitVersion = '5.10.2' } sets names in a block of its own.
        foreach (Match block in Regex.Matches(text, @"\bext\s*\{(?<body>[^{}]*)\}"))
        {
            foreach (Match setting in Regex.Matches(block.Groups["body"].Value, @"(?m)^\s*(?<name>\w+)\s*=\s*['""](?<value>[^'""$]+)['""]"))
                variables[setting.Groups["name"].Value] = setting.Groups["value"].Value;
        }
    }

    private static string Fill(string text, IReadOnlyDictionary<string, string> variables) =>
        Interpolation().Replace(text, match => variables.TryGetValue(match.Groups["name"].Value, out var value) ? value : match.Value);

    /// <summary>A Gradle file - a build file or settings - without its comments, keeping anything in quotes: a URL's // is not a comment.</summary>
    internal static string WithoutComments(string text)
    {
        var kept = new StringBuilder(text.Length);
        char? quote = null;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (quote is { } open)
            {
                kept.Append(c);
                if (c == '\\' && i + 1 < text.Length) kept.Append(text[++i]);
                else if (c == open) quote = null;
                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
                kept.Append(c);
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                kept.Append('\n');
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 1;
            }
            else
            {
                kept.Append(c);
            }
        }

        return kept.ToString();
    }

    private static string ReadAllText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
