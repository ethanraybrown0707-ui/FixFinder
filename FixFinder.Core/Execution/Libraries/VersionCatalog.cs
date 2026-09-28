using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution.Libraries;

/// <summary>
/// A Gradle version catalog - gradle/libs.versions.toml, which gradle init writes for a new project - read for what a build
/// file's libs.name stands for. Only its [versions], [libraries] and [bundles] tables are needed, in the forms Gradle's
/// own documentation gives: "group:artifact:version", module = "group:artifact" with version or version.ref, and group,
/// name and version on their own.
/// </summary>
public sealed partial class VersionCatalog
{
    private readonly Dictionary<string, string> _versions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeclaredDependency> _libraries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _bundles = new(StringComparer.Ordinal);

    [GeneratedRegex(@"^(?<key>[\w.-]+)\s*=\s*(?<value>.+)$")]
    private static partial Regex Setting();

    [GeneratedRegex(@"(?<key>[\w.-]+)\s*=\s*(?:""(?<text>[^""]*)""|\{(?<table>[^{}]*)\})")]
    private static partial Regex InlineSetting();

    [GeneratedRegex(@"""(?<text>[^""]*)""")]
    private static partial Regex Quoted();

    public static VersionCatalog Read(string path)
    {
        var catalog = new VersionCatalog();
        if (!File.Exists(path)) return catalog;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return catalog;
        }

        var section = "";
        var entries = new List<(string Section, string Key, string Value)>();

        foreach (var raw in lines)
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            if (Setting().Match(line) is { Success: true } setting) entries.Add((section, setting.Groups["key"].Value, setting.Groups["value"].Value.Trim()));
        }

        foreach (var (_, key, value) in entries.Where(entry => entry.Section == "versions"))
        {
            if (Text(value) is { } version) catalog._versions[key] = version;
            else if (Table(value) is { } rich && (rich.GetValueOrDefault("strictly") ?? rich.GetValueOrDefault("require") ?? rich.GetValueOrDefault("prefer")) is { } chosen)
                catalog._versions[key] = chosen;
        }

        foreach (var (_, key, value) in entries.Where(entry => entry.Section == "libraries"))
        {
            if (catalog.LibraryFrom(value) is { } library) catalog._libraries[Accessor(key)] = library;
        }

        foreach (var (_, key, value) in entries.Where(entry => entry.Section == "bundles"))
            catalog._bundles[Accessor(key)] = Quoted().Matches(value).Select(match => Accessor(match.Groups["text"].Value)).ToList();

        return catalog;
    }

    /// <summary>The library libs.alias stands for, or null when the catalog does not have it.</summary>
    public DeclaredDependency? Library(string alias) => _libraries.GetValueOrDefault(Accessor(alias));

    /// <summary>The libraries libs.bundles.alias stands for, or null when the catalog does not have that bundle.</summary>
    public IReadOnlyList<DeclaredDependency>? Bundle(string alias) =>
        _bundles.TryGetValue(Accessor(alias), out var members) && members.All(_libraries.ContainsKey) ? members.Select(member => _libraries[member]).ToList() : null;

    private DeclaredDependency? LibraryFrom(string value)
    {
        if (Text(value) is { } notation)
        {
            var parts = notation.Split(':');
            return parts.Length >= 2 ? new DeclaredDependency(parts[0], parts[1], parts.Length > 2 ? parts[2] : null) : null;
        }

        if (Table(value) is not { } table) return null;

        var (group, artifact) = table.GetValueOrDefault("module") is { } module && module.Split(':') is [var g, var a]
            ? (g, a)
            : (table.GetValueOrDefault("group"), table.GetValueOrDefault("name"));

        if (group is null || artifact is null) return null;

        var version = table.GetValueOrDefault("version.ref") is { } reference ? _versions.GetValueOrDefault(reference) : table.GetValueOrDefault("version");
        return new DeclaredDependency(group, artifact, version);
    }

    /// <summary>A catalog key as a build file writes it: junit-jupiter, junit_jupiter and junit.jupiter are all libs.junit.jupiter.</summary>
    private static string Accessor(string key) => Regex.Replace(key, "[-_.]", ".");

    private static string? Text(string value) => value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2 ? value[1..^1] : null;

    /// <summary>An inline table's settings - module = "...", version.ref = "..." - with a nested version = { strictly = ... } flattened.</summary>
    private static Dictionary<string, string>? Table(string value)
    {
        if (!value.StartsWith('{') || !value.EndsWith('}')) return null;

        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match setting in InlineSetting().Matches(value[1..^1]))
        {
            var key = setting.Groups["key"].Value;
            if (setting.Groups["text"].Success) settings[key] = setting.Groups["text"].Value;
            else if (Table("{" + setting.Groups["table"].Value + "}") is { } nested)
            {
                foreach (var (inner, text) in nested) settings[$"{key}.{inner}"] = text;
                if (key == "version" && (nested.GetValueOrDefault("strictly") ?? nested.GetValueOrDefault("require") ?? nested.GetValueOrDefault("prefer")) is { } chosen)
                    settings["version"] = chosen;
            }
        }

        return settings;
    }
}
