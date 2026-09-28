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

        var properties = Child(project, "properties")?.Elements()
            .GroupBy(property => property.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(named => named.Key, named => named.Last().Value.Trim(), StringComparer.Ordinal) ?? [];

        var (processorPaths, processorPathsAdded) = ProcessorPathsIn(Child(project, "build"));

        return new PomFile(
            Text(project, "groupId"),
            Text(project, "artifactId"),
            Text(project, "version"),
            parent,
            properties,
            DependenciesIn(Child(project, "dependencies")),
            DependenciesIn(Child(Child(project, "dependencyManagement"), "dependencies")),
            Child(project, "modules")?.Elements().Where(module => module.Name.LocalName == "module").Select(module => module.Value.Trim()).ToList() ?? [])
        {
            ProcessorPaths = processorPaths,
            ProcessorPathsAdded = processorPathsAdded,
        };
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
