using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution.Libraries;

/// <summary>What a project needs to be built and run, as files on this computer, and what it declares that is not here.</summary>
/// <param name="Main">The jars the program's own code is compiled and run with.</param>
/// <param name="Test">The jars its tests need besides those: JUnit and the like, which a test scope keeps from the program.</param>
public sealed record ResolvedLibraries(
    IReadOnlyList<string> Main,
    IReadOnlyList<string> Test,
    IReadOnlyList<MissingLibrary> Missing,
    IReadOnlyList<LibraryName> Chosen)
{
    public static ResolvedLibraries Nothing { get; } = new([], [], [], []);
}

/// <summary>
/// Works out what a Maven project needs the way Maven does: its pom.xml with everything it inherits from its parents, its
/// ${properties} filled in, the versions its dependencyManagement and imported BOMs set, then each library's own
/// dependencies from that library's own pom.xml - the nearest declaration of a library winning, or, as Gradle chooses,
/// the highest version. Only what is already downloaded is used, and what is not is named as missing, never guessed at.
/// </summary>
public sealed partial class MavenResolver(LibraryStore store, bool highestVersionWins = false)
{
    /// <summary>How far parents and imported BOMs are followed, which only a pom.xml that names itself would exceed.</summary>
    private const int MostDepth = 30;

    private const int MostLibraries = 2000;

    private readonly Dictionary<string, EffectivePom?> _fromStore = new(StringComparer.Ordinal);

    [GeneratedRegex(@"\$\{(?<name>[^}]+)\}")]
    private static partial Regex Placeholder();

    public LibraryStore Store => store;

    /// <summary>What the project whose pom.xml this is needs, for its main code and for its tests.</summary>
    public ResolvedLibraries Resolve(string pomPath)
    {
        if (PomFile.Read(pomPath) is not { } pom || Load(pom, pomPath, 0) is not { } project)
            return ResolvedLibraries.Nothing with { Missing = [new MissingLibrary(Path.GetFileName(pomPath), "it could not be read as a pom.xml")] };

        return Resolve(project.Dependencies, project.Managed);
    }

    /// <summary>
    /// The annotation processors the project's pom.xml, or a parent of it, gives maven-compiler-plugin in
    /// annotationProcessorPaths, with what each needs, as files on this computer - or null when none are given, and javac
    /// looks for processors among the project's own libraries. A path takes its own version, or the one the project manages
    /// for it; what a processor needs besides is settled from the processor's own pom.xml alone, as the plugin settles it
    /// unless it is told to use the project's.
    /// </summary>
    public ResolvedLibraries? ProcessorsOf(string pomPath)
    {
        if (PomFile.Read(pomPath) is not { } pom || Load(pom, pomPath, 0) is not { RawProcessorPaths: { Count: > 0 } paths } project) return null;

        var versioned = paths.Select(path => Filled(path, project.Properties))
            .Select(path => path with { Version = path.Version ?? project.Managed.GetValueOrDefault(path.Key)?.Version, Scope = "compile" })
            .ToList();

        return Walk(versioned, new Dictionary<string, DeclaredDependency>(StringComparer.Ordinal), forced: null).Libraries;
    }

    /// <summary>
    /// The Java the project's build compiles for, as maven-compiler-plugin works it out: its release - javac's --release -
    /// when one is set, else its source; each as the plugin is configured by this pom.xml or a parent, else as the
    /// maven.compiler properties the plugin reads by default set it. Null when nothing says.
    /// </summary>
    public DeclaredJava? JavaOf(string pomPath)
    {
        if (PomFile.Read(pomPath) is not { } pom || Load(pom, pomPath, 0) is not { } project) return null;

        var properties = project.Properties;
        var compiler = project.Compiler;

        // Where a value is set, for the explanation: on the plugin, or in a property - this pom.xml's own, or a parent's.
        string Origin(string? onThePlugin, string property) =>
            onThePlugin is not null ? $"maven-compiler-plugin's {property.Split('.')[^1]} in pom.xml"
            : pom.Properties.ContainsKey(property) ? $"pom.xml's {property}"
            : $"the {property} pom.xml's parent sets";

        string? Setting(string? onThePlugin, string property) =>
            onThePlugin is not null ? Fill(onThePlugin, properties) : properties.GetValueOrDefault(property) is { } value ? Fill(value, properties) : null;

        var preview = string.Equals(Setting(compiler?.EnablePreview, "maven.compiler.enablePreview"), "true", StringComparison.OrdinalIgnoreCase) ||
                      (compiler?.Arguments.Any(argument => Fill(argument, properties) == "--enable-preview") ?? false);

        if (DeclaredJava.ReleaseNumber(Setting(compiler?.Release, "maven.compiler.release")) is { } release)
            return new DeclaredJava(release, StrictRelease: true, preview, Origin(compiler?.Release, "maven.compiler.release"));

        var source = Setting(compiler?.Source, "maven.compiler.source") ?? Setting(compiler?.Target, "maven.compiler.target");
        if (DeclaredJava.ReleaseNumber(source) is { } level)
        {
            var said = compiler?.Source is not null || properties.ContainsKey("maven.compiler.source")
                ? Origin(compiler?.Source, "maven.compiler.source")
                : Origin(compiler?.Target, "maven.compiler.target");
            return new DeclaredJava(level, StrictRelease: false, preview, said);
        }

        // Spring Boot's parent passes java.version to the compiler - as the release from Spring Boot 3, as the source and target
        // before it - so a project whose parent has not been downloaded still says its Java there.
        if (pom.Parent is { Group: "org.springframework.boot", Artifact: "spring-boot-starter-parent" } boot &&
            DeclaredJava.ReleaseNumber(properties.GetValueOrDefault("java.version") is { } written ? Fill(written, properties) : null) is { } bootJava)
        {
            var fromBoot3 = int.TryParse(boot.Version.Split('.')[0], out var bootMajor) && bootMajor >= 3;
            return new DeclaredJava(bootJava, StrictRelease: fromBoot3, preview, "pom.xml's java.version, which Spring Boot's parent passes to the compiler");
        }

        return preview ? new DeclaredJava(null, StrictRelease: true, preview, "pom.xml's maven-compiler-plugin") : null;
    }

    /// <summary>The versions each of these BOMs sets, as one table - what a Gradle platform() brings in.</summary>
    public IReadOnlyDictionary<string, DeclaredDependency> ManagedBy(IEnumerable<LibraryName> boms)
    {
        var managed = new Dictionary<string, DeclaredDependency>(StringComparer.Ordinal);

        foreach (var bom in boms)
        {
            if (FromStore(bom, 1) is not { } read) continue;
            foreach (var (key, entry) in read.Managed) managed.TryAdd(key, entry);
        }

        return managed;
    }

    /// <summary>What these declared dependencies need, taking any version not given from <paramref name="managed"/>.</summary>
    public ResolvedLibraries Resolve(IReadOnlyList<DeclaredDependency> direct, IReadOnlyDictionary<string, DeclaredDependency> managed)
    {
        var first = Walk(direct, managed, forced: null);
        if (!highestVersionWins) return first.Libraries;

        // Gradle chooses the highest version of each library found anywhere in the graph, then walks it again with those.
        var highest = first.Seen.ToDictionary(seen => seen.Key, seen => MavenVersion.Highest(seen.Value), StringComparer.Ordinal);
        return Walk(direct, managed, highest).Libraries;
    }

    private (ResolvedLibraries Libraries, Dictionary<string, HashSet<string>> Seen) Walk(
        IReadOnlyList<DeclaredDependency> direct, IReadOnlyDictionary<string, DeclaredDependency> managed, IReadOnlyDictionary<string, string>? forced)
    {
        var chosen = new Dictionary<string, (LibraryName Name, bool Test, string? Jar)>(StringComparer.Ordinal);
        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var missing = new List<MissingLibrary>();
        var systemJars = new List<(string Jar, bool Test)>();
        var pending = new Queue<(DeclaredDependency Dependency, int Depth, bool Test, HashSet<string> Excluded)>();

        foreach (var dependency in direct)
        {
            var scope = dependency.Scope ?? managed.GetValueOrDefault(dependency.Key)?.Scope ?? "compile";
            if (scope == "import") continue;

            if (scope == "system")
            {
                if (dependency.SystemPath is { } jar && File.Exists(jar)) systemJars.Add((jar, false));
                else missing.Add(new MissingLibrary($"{dependency.Group}:{dependency.Artifact}", $"its systemPath {dependency.SystemPath ?? "is not given"} is not a file on this computer"));
                continue;
            }

            pending.Enqueue((dependency with { Scope = scope }, 1, scope == "test", Excluded(dependency, [])));
        }

        while (pending.Count > 0 && chosen.Count < MostLibraries)
        {
            var (dependency, depth, test, excluded) = pending.Dequeue();
            var key = dependency.Key;

            if (VersionOf(dependency, depth, managed, forced) is not { } version)
            {
                missing.Add(new MissingLibrary($"{dependency.Group}:{dependency.Artifact}", dependency.Version is null
                    ? "no version is given for it, and nothing FixFinder can read sets one"
                    : $"its version {dependency.Version} is not one FixFinder could settle from what is downloaded"));
                continue;
            }

            (seen.TryGetValue(key, out var versions) ? versions : seen[key] = new HashSet<string>(StringComparer.Ordinal)).Add(version);

            if (chosen.TryGetValue(key, out var already))
            {
                // Nearest wins: a library the tests reached first and the main code needs too is the main code's.
                if (already.Test && !test) chosen[key] = already with { Test = false };
                continue;
            }

            var name = new LibraryName(dependency.Group, dependency.Artifact, version, dependency.Classifier);
            string? jar = null;

            if (dependency.Type != "pom")
            {
                jar = store.FileOf(name, "jar");
                if (jar is null) missing.Add(new MissingLibrary(name.ToString(), $"it is not in {store.Name}"));
            }

            chosen[key] = (name, test, jar);

            if (FromStore(name, 1) is not { } library) continue;

            foreach (var transitive in library.Dependencies)
            {
                var scope = transitive.Scope ?? library.Managed.GetValueOrDefault(transitive.Key)?.Scope ?? "compile";
                if (transitive.Optional || scope is "test" or "provided" or "system" or "import") continue;
                if (excluded.Contains($"{transitive.Group}:{transitive.Artifact}") || excluded.Contains($"{transitive.Group}:*") || excluded.Contains("*:*")) continue;

                var inherited = transitive.Version ?? library.Managed.GetValueOrDefault(transitive.Key)?.Version;
                pending.Enqueue((transitive with { Version = inherited, Scope = scope }, depth + 1, test, Excluded(transitive, excluded)));
            }
        }

        var main = systemJars.Select(system => system.Jar).Concat(chosen.Values.Where(c => !c.Test && c.Jar is not null).Select(c => c.Jar!)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var tests = chosen.Values.Where(c => c.Test && c.Jar is not null).Select(c => c.Jar!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return (new ResolvedLibraries(main, tests, missing, chosen.Values.Select(c => c.Name).ToList()), seen);
    }

    /// <summary>
    /// The version a dependency is taken at: the one the project manages for it when it is not declared directly - Maven
    /// lets a project's dependencyManagement settle every library below it - or the one it gives itself, or the one a
    /// range picks from what is downloaded.
    /// </summary>
    private string? VersionOf(DeclaredDependency dependency, int depth, IReadOnlyDictionary<string, DeclaredDependency> managed, IReadOnlyDictionary<string, string>? forced)
    {
        if (forced?.GetValueOrDefault(dependency.Key) is { } chosen) return chosen;

        var managedVersion = managed.GetValueOrDefault(dependency.Key)?.Version;
        var version = depth > 1 ? managedVersion ?? dependency.Version : dependency.Version ?? managedVersion;

        if (version is null || version.Contains("${", StringComparison.Ordinal)) return null;
        return MavenVersion.IsRange(version) ? MavenVersion.HighestIn(version, store.VersionsOf(dependency.Group, dependency.Artifact)) : version;
    }

    private static HashSet<string> Excluded(DeclaredDependency dependency, HashSet<string> already)
    {
        var excluded = new HashSet<string>(already, StringComparer.Ordinal);
        foreach (var (group, artifact) in dependency.Excluding) excluded.Add($"{group}:{artifact}");
        return excluded;
    }

    /// <summary>A library's pom.xml from the store, with its parents and managed versions, read once for each library.</summary>
    private EffectivePom? FromStore(LibraryName library, int depth)
    {
        var key = $"{library.Group}:{library.Artifact}:{library.Version}";
        if (_fromStore.TryGetValue(key, out var known)) return known;

        _fromStore[key] = null;
        var loaded = depth <= MostDepth && store.FileOf(library with { Classifier = "" }, "pom") is { } file && PomFile.Read(file) is { } pom
            ? Load(pom, file, depth)
            : null;

        return _fromStore[key] = loaded;
    }

    private EffectivePom? Load(PomFile pom, string? path, int depth)
    {
        if (depth > MostDepth) return null;

        EffectivePom? parent = null;
        if (pom.Parent is { } declared)
            parent = ParentBeside(declared, path, depth) ?? FromStore(new LibraryName(declared.Group, declared.Artifact, declared.Version), depth + 1);

        var properties = new Dictionary<string, string>(parent?.Properties ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        foreach (var (name, value) in pom.Properties) properties[name] = value;

        var group = pom.Group ?? pom.Parent?.Group ?? "";
        var artifact = pom.Artifact ?? "";

        if (pom.Parent is { } named)
        {
            foreach (var prefix in new[] { "project.parent.", "parent." })
            {
                properties[prefix + "groupId"] = named.Group;
                properties[prefix + "artifactId"] = named.Artifact;
                properties[prefix + "version"] = named.Version;
            }
        }

        foreach (var prefix in new[] { "project.", "pom.", "" })
        {
            properties[prefix + "groupId"] = group;
            properties[prefix + "artifactId"] = artifact;
        }

        var version = Fill(pom.Version ?? pom.Parent?.Version ?? "", properties);
        foreach (var prefix in new[] { "project.", "pom.", "" }) properties[prefix + "version"] = version;

        var effective = new EffectivePom
        {
            Name = new LibraryName(Fill(group, properties), Fill(artifact, properties), version),
            Properties = properties,
            Dependencies = Merged(parent?.RawDependencies, pom.Dependencies).Select(dependency => Filled(dependency, properties)).ToList(),
            RawDependencies = Merged(parent?.RawDependencies, pom.Dependencies),
            RawManaged = Merged(parent?.RawManaged, pom.Managed),
            RawProcessorPaths = pom.ProcessorPaths is not { } ownPaths ? parent?.RawProcessorPaths
                : pom.ProcessorPathsAdded ? [.. parent?.RawProcessorPaths ?? [], .. ownPaths]
                : ownPaths,
            Compiler = Inherited(pom.Compiler, parent?.Compiler),
        };

        var filledManaged = effective.RawManaged.Select(entry => Filled(entry, properties)).ToList();

        foreach (var entry in filledManaged.Where(entry => entry.Scope != "import"))
            effective.Managed.TryAdd(entry.Key, entry);

        // An imported BOM only sets what the pom.xml and its parents leave unset, in the order they are imported.
        foreach (var bom in filledManaged.Where(entry => entry.Scope == "import" && entry.Type == "pom"))
        {
            if (bom.Version is not { } bomVersion || bomVersion.Contains("${", StringComparison.Ordinal)) continue;
            if (FromStore(new LibraryName(bom.Group, bom.Artifact, bomVersion), depth + 1) is not { } imported) continue;

            foreach (var (key, entry) in imported.Managed) effective.Managed.TryAdd(key, entry);
        }

        return effective;
    }

    /// <summary>
    /// The parent a pom.xml names, read from beside it at relativePath when the file there is that parent - as it is in
    /// a project made of modules - rather than from the store, where a project's own parent is seldom installed.
    /// </summary>
    private EffectivePom? ParentBeside(PomParent declared, string? path, int depth)
    {
        if (path is null || Path.GetDirectoryName(path) is not { } folder || declared.RelativePath.Length == 0) return null;

        try
        {
            var candidate = Path.GetFullPath(Path.Combine(folder, declared.RelativePath));
            if (Directory.Exists(candidate)) candidate = Path.Combine(candidate, "pom.xml");
            if (!File.Exists(candidate) || string.Equals(candidate, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) return null;

            if (PomFile.Read(candidate) is not { } parent || parent.Artifact != declared.Artifact || (parent.Group ?? parent.Parent?.Group) != declared.Group)
                return null;

            return Load(parent, candidate, depth + 1);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>A child's declarations over its parent's: one for the same library replaces the parent's.</summary>
    private static List<DeclaredDependency> Merged(IReadOnlyList<DeclaredDependency>? inherited, IReadOnlyList<DeclaredDependency> own)
    {
        var keys = own.Select(dependency => dependency.Key).ToHashSet(StringComparer.Ordinal);
        return [.. own, .. (inherited ?? []).Where(dependency => !keys.Contains(dependency.Key))];
    }

    private static DeclaredDependency Filled(DeclaredDependency dependency, IReadOnlyDictionary<string, string> properties) => dependency with
    {
        Group = Fill(dependency.Group, properties),
        Artifact = Fill(dependency.Artifact, properties),
        Version = dependency.Version is { } version ? Fill(version, properties) : null,
        Scope = dependency.Scope is { } scope ? Fill(scope, properties) : null,
        Classifier = Fill(dependency.Classifier, properties),
        Type = Fill(dependency.Type, properties),
        SystemPath = dependency.SystemPath is { } path ? Fill(path, properties) : null,
    };

    /// <summary>Text with every ${property} it names filled in, as far as the properties go; one nobody sets stays as written.</summary>
    private static string Fill(string text, IReadOnlyDictionary<string, string> properties)
    {
        for (var pass = 0; pass < 10 && text.Contains("${", StringComparison.Ordinal); pass++)
        {
            var filled = Placeholder().Replace(text, match =>
            {
                var name = match.Groups["name"].Value;
                if (properties.TryGetValue(name, out var value)) return value;
                if (name == "user.home") return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (name.StartsWith("env.", StringComparison.Ordinal) && Environment.GetEnvironmentVariable(name[4..]) is { } variable) return variable;
                return match.Value;
            });

            if (filled == text) break;
            text = filled;
        }

        return text;
    }

    /// <summary>A plugin's configuration as Maven merges a child's with its parent's: each setting the child gives in place of the parent's.</summary>
    private static PomCompiler? Inherited(PomCompiler? own, PomCompiler? parents)
    {
        if (own is null || parents is null) return own ?? parents;

        return new PomCompiler(
            own.Release ?? parents.Release,
            own.Source ?? parents.Source,
            own.Target ?? parents.Target,
            own.EnablePreview ?? parents.EnablePreview,
            own.Arguments.Count > 0 ? own.Arguments : parents.Arguments);
    }

    /// <summary>A pom.xml with its parents' declarations, its properties, and the versions managed for it.</summary>
    private sealed class EffectivePom
    {
        public required LibraryName Name { get; init; }

        public required IReadOnlyDictionary<string, string> Properties { get; init; }

        public required IReadOnlyList<DeclaredDependency> Dependencies { get; init; }

        public required IReadOnlyList<DeclaredDependency> RawDependencies { get; init; }

        public required IReadOnlyList<DeclaredDependency> RawManaged { get; init; }

        /// <summary>The annotationProcessorPaths it gives maven-compiler-plugin, its own or its parents', or null for none.</summary>
        public IReadOnlyList<DeclaredDependency>? RawProcessorPaths { get; init; }

        /// <summary>What it, or a parent, tells maven-compiler-plugin about the Java it compiles, unfilled.</summary>
        public PomCompiler? Compiler { get; init; }

        public Dictionary<string, DeclaredDependency> Managed { get; } = new(StringComparer.Ordinal);
    }
}
