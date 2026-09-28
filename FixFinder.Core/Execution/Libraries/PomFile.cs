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

        return new PomFile(
            Text(project, "groupId"),
            Text(project, "artifactId"),
            Text(project, "version"),
            parent,
            properties,
            DependenciesIn(Child(project, "dependencies")),
            DependenciesIn(Child(Child(project, "dependencyManagement"), "dependencies")),
            Child(project, "modules")?.Elements().Where(module => module.Name.LocalName == "module").Select(module => module.Value.Trim()).ToList() ?? []);
    }

    private static List<DeclaredDependency> DependenciesIn(XElement? dependencies)
    {
        var declared = new List<DeclaredDependency>();
        if (dependencies is null) return declared;

        foreach (var dependency in dependencies.Elements().Where(element => element.Name.LocalName == "dependency"))
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
