using System.Xml.Linq;

namespace FixFinder.Core.Execution.Libraries;

/// <summary>One dependency as a pom.xml or a build file declares it, before anything is filled in from elsewhere.</summary>
public sealed record DeclaredDependency(
    string Group,
    string Artifact,
    string? Version,
    string? Scope = null,
    bool Optional = false,
    string Type = "jar",
    string Classifier = "",
    IReadOnlyList<(string Group, string Artifact)>? Exclusions = null,
    string? SystemPath = null)
{
    public string Key => Classifier.Length > 0 ? $"{Group}:{Artifact}:{Classifier}" : $"{Group}:{Artifact}";

    public IReadOnlyList<(string Group, string Artifact)> Excluding => Exclusions ?? [];
}

/// <summary>The parent a pom.xml names, and where beside it the parent's own pom.xml is expected.</summary>
public sealed record PomParent(string Group, string Artifact, string Version, string RelativePath);

/// <summary>What one pom.xml says, as written: nothing inherited from its parent, and no ${property} filled in.</summary>
public sealed record PomFile(
    string? Group,
    string? Artifact,
    string? Version,
    PomParent? Parent,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<DeclaredDependency> Dependencies,
    IReadOnlyList<DeclaredDependency> Managed,
    IReadOnlyList<string> Modules)
{
    /// <summary>
    /// The annotationProcessorPaths this pom.xml gives maven-compiler-plugin - the only place javac then looks for
    /// annotation processors - or null when it gives none, and javac looks among the project's libraries instead.
    /// </summary>
    public IReadOnlyList<DeclaredDependency>? ProcessorPaths { get; init; }

    /// <summary>Whether those paths are added to the ones a parent gives, as combine.children="append" asks, not put in their place.</summary>
    public bool ProcessorPathsAdded { get; init; }

    /// <summary>The pom.xml at this path, or null when it cannot be read as one.</summary>
    public static PomFile? Read(string path)
    {
        try
        {
            return From(XDocument.Load(path).Root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    public static PomFile? From(XElement? project)
    {
        if (project is null || project.Name.LocalName != "project") return null;

        PomParent? parent = Child(project, "parent") is { } declared && Text(declared, "groupId") is { } group &&
                            Text(declared, "artifactId") is { } artifact && Text(declared, "version") is { } version
            ? new PomParent(group, artifact, version, Text(declared, "relativePath") ?? "../pom.xml")
            : null;

        // What a profile Maven would switch on here adds is read as if written in the pom.xml itself, after what it says.
        var sections = new[] { project }.Concat(ActiveProfiles(project)).ToList();

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in sections.SelectMany(section => Child(section, "properties")?.Elements() ?? []))
            properties[property.Name.LocalName] = property.Value.Trim();

        var (processorPaths, processorPathsAdded) = ProcessorPathsIn(Child(project, "build"));

        return new PomFile(
            Text(project, "groupId"),
            Text(project, "artifactId"),
            Text(project, "version"),
            parent,
            properties,
            sections.SelectMany(section => DependenciesIn(Child(section, "dependencies"))).ToList(),
            sections.SelectMany(section => DependenciesIn(Child(Child(section, "dependencyManagement"), "dependencies"))).ToList(),
            Child(project, "modules")?.Elements().Where(module => module.Name.LocalName == "module").Select(module => module.Value.Trim()).ToList() ?? [])
        {
            ProcessorPaths = processorPaths,
            ProcessorPathsAdded = processorPathsAdded,
        };
    }

    /// <summary>
    /// The profiles of a pom.xml that Maven would switch on here with nothing set on its command line: those whose os and
    /// property conditions this computer meets - all of them, as Maven asks - or, when none is on, those marked
    /// activeByDefault. JavaFX's pom.xml picks the jars for this computer this way. A profile that asks about a JDK, a file
    /// or anything else FixFinder cannot tell stays off.
    /// </summary>
    private static List<XElement> ActiveProfiles(XElement project)
    {
        var profiles = Child(project, "profiles")?.Elements().Where(element => element.Name.LocalName == "profile").ToList() ?? [];

        var switchedOn = profiles.Where(profile => Child(profile, "activation") is { } activation && ConditionsHold(activation)).ToList();
        if (switchedOn.Count > 0) return switchedOn;

        return profiles.Where(profile => Child(profile, "activation") is { } activation && Text(activation, "activeByDefault") == "true").ToList();
    }

    private static bool ConditionsHold(XElement activation)
    {
        var conditions = activation.Elements().Where(condition => condition.Name.LocalName != "activeByDefault").ToList();

        return conditions.Count > 0 && conditions.All(condition => condition.Name.LocalName switch
        {
            "os" => condition.Elements().All(OperatingSystemTestHolds),
            "property" => PropertyTestHolds(condition),
            _ => false,
        });
    }

    /// <summary>One test of the computer's operating system - family, arch or name, "!" before it to mean "not" - as Maven makes it.</summary>
    private static bool OperatingSystemTestHolds(XElement test)
    {
        var written = test.Value.Trim();
        var negated = written.StartsWith('!');
        var wanted = negated ? written[1..] : written;

        bool? holds = test.Name.LocalName switch
        {
            "family" => ThisComputer.Families.Contains(wanted, StringComparer.OrdinalIgnoreCase),
            "arch" => string.Equals(wanted, ThisComputer.Arch, StringComparison.OrdinalIgnoreCase),
            "name" => string.Equals(wanted, ThisComputer.Name, StringComparison.OrdinalIgnoreCase),
            _ => null,
        };

        return holds is { } known && known != negated;
    }

    /// <summary>
    /// A property test, with no property set, as none is when FixFinder reads a build: one that asks for a property to be set,
    /// or to have a value, fails; one that asks for it not to be set, or not to have a value - a name or value after "!" - holds.
    /// </summary>
    private static bool PropertyTestHolds(XElement property) =>
        Text(property, "value") is { } value ? value.StartsWith('!') : (Text(property, "name") ?? "").StartsWith('!');

    /// <summary>This computer as Maven's os tests see it, through the names the JVM gives it.</summary>
    private static class ThisComputer
    {
        public static readonly string[] Families =
            OperatingSystem.IsWindows() ? ["windows", "dos"] : OperatingSystem.IsMacOS() ? ["unix", "mac"] : ["unix"];

        public static readonly string Arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => OperatingSystem.IsMacOS() ? "x86_64" : "amd64",
            System.Runtime.InteropServices.Architecture.Arm64 => "aarch64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            System.Runtime.InteropServices.Architecture.Arm => "arm",
            var other => other.ToString().ToLowerInvariant(),
        };

        public static readonly string Name =
            OperatingSystem.IsWindows() ? (Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10")
            : OperatingSystem.IsMacOS() ? "Mac OS X"
            : "Linux";
    }

    /// <summary>
    /// The annotationProcessorPaths maven-compiler-plugin is set up with - on the plugin as the build uses it, else as
    /// pluginManagement sets it up, else on one of its executions - and whether they add to a parent's.
    /// </summary>
    private static (IReadOnlyList<DeclaredDependency>? Paths, bool Added) ProcessorPathsIn(XElement? build)
    {
        var compilerPlugins = new[] { Child(build, "plugins"), Child(Child(build, "pluginManagement"), "plugins") }
            .SelectMany(plugins => plugins?.Elements() ?? [])
            .Where(plugin => plugin.Name.LocalName == "plugin" && Text(plugin, "artifactId") == "maven-compiler-plugin");

        foreach (var plugin in compilerPlugins)
        {
            var settings = new[] { Child(plugin, "configuration") }
                .Concat(Child(plugin, "executions")?.Elements().Select(execution => Child(execution, "configuration")) ?? []);

            if (settings.Select(setting => Child(setting, "annotationProcessorPaths")).FirstOrDefault(paths => paths is not null) is { } named)
                return (DependenciesIn(named, "path"), named.Attribute("combine.children")?.Value == "append");
        }

        return (null, false);
    }

    /// <summary>Each library listed in this element, one to a child named <paramref name="entry"/> - a dependency, or a processor's path.</summary>
    private static List<DeclaredDependency> DependenciesIn(XElement? dependencies, string entry = "dependency")
    {
        var declared = new List<DeclaredDependency>();
        if (dependencies is null) return declared;

        foreach (var dependency in dependencies.Elements().Where(element => element.Name.LocalName == entry))
        {
            if (Text(dependency, "groupId") is not { } group || Text(dependency, "artifactId") is not { } artifact) continue;

            var type = Text(dependency, "type") ?? "jar";
            var exclusions = Child(dependency, "exclusions")?.Elements()
                .Where(exclusion => exclusion.Name.LocalName == "exclusion")
                .Select(exclusion => (Text(exclusion, "groupId") ?? "*", Text(exclusion, "artifactId") ?? "*"))
                .ToList() ?? [];

            declared.Add(new DeclaredDependency(
                group,
                artifact,
                Text(dependency, "version"),
                Text(dependency, "scope"),
                string.Equals(Text(dependency, "optional"), "true", StringComparison.OrdinalIgnoreCase),
                type,
                Text(dependency, "classifier") ?? (type == "test-jar" ? "tests" : ""),
                exclusions,
                Text(dependency, "systemPath")));
        }

        return declared;
    }

    private static XElement? Child(XElement? parent, string name) => parent?.Elements().FirstOrDefault(element => element.Name.LocalName == name);

    private static string? Text(XElement parent, string name) => Child(parent, name)?.Value.Trim() is { Length: > 0 } text ? text : null;
}
