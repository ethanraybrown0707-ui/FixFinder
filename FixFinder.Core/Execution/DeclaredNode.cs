using System.Text.Json;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution.Versions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Node.js a program's project says it is written for, from the files its tools keep it in: .nvmrc, which nvm reads,
/// .node-version, which other version managers read, package.json's volta pin, and package.json's engines - the nearest
/// above the program, up to the project's own folder.
/// </summary>
/// <remarks>
/// A version manager can also be given a name - lts/iron, node - which only it can turn into a version, so only versions
/// written as numbers are read.
/// </remarks>
public static partial class DeclaredNode
{
    /// <summary>What the project says: the Node.js it needs at least, at most, or both, and which file says so.</summary>
    public sealed record Declared(ToolchainChoice.AtLeast? AtLeast, ToolchainChoice.AtMost? AtMost);

    /// <summary>What marks a project's own folder, beyond which a file belongs to something else.</summary>
    private static readonly string[] ProjectMarkers = [".git", ".idea", ".vscode", "package.json"];

    /// <summary>One comparison of npm's semver ranges: an operator, and a version that may leave out its minor or patch, or write x or * for them.</summary>
    [GeneratedRegex(@"(?<operator>>=|<=|>|<|=|\^|~)?\s*v?(?<major>\d+)(?:\.(?<minor>\d+|[xX*]))?(?:\.(?<patch>\d+|[xX*]))?")]
    private static partial Regex Comparison();

    [GeneratedRegex(@"^\s*v?(?<major>\d+)(?:\.(?<minor>\d+|[xX*]))?(?:\.(?<patch>\d+|[xX*]))?\s*-\s*v?(?<toMajor>\d+)(?:\.(?<toMinor>\d+|[xX*]))?(?:\.(?<toPatch>\d+|[xX*]))?\s*$")]
    private static partial Regex Hyphenated();

    /// <summary>What the nearest of the project's files says, or null when none says anything.</summary>
    public static Declared? Of(string javaScriptFile)
    {
        for (var folder = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(javaScriptFile))!); folder is not null; folder = folder.Parent)
        {
            if (InFolder(folder.FullName) is { } declared) return declared;

            if (ProjectMarkers.Any(marker => File.Exists(Path.Combine(folder.FullName, marker)) || Directory.Exists(Path.Combine(folder.FullName, marker)))) break;
        }

        return null;
    }

    private static Declared? InFolder(string folder)
    {
        // A version manager's own file names the one Node.js to use, so it comes first.
        foreach (var pinFile in new[] { ".nvmrc", ".node-version" })
        {
            if (Read(folder, pinFile) is { } pinned &&
                pinned.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0 && !line.StartsWith('#')) is { } version &&
                Pinned(version, $"{pinFile} names Node.js") is { } fromPin)
            {
                return fromPin;
            }
        }

        if (Read(folder, "package.json") is not { } packageJson) return null;

        try
        {
            using var package = JsonDocument.Parse(packageJson, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (package.RootElement.ValueKind != JsonValueKind.Object) return null;

            if (package.RootElement.TryGetProperty("volta", out var volta) && volta.ValueKind == JsonValueKind.Object &&
                volta.TryGetProperty("node", out var voltaNode) && voltaNode.ValueKind == JsonValueKind.String &&
                Pinned(voltaNode.GetString()!, "package.json's volta pins Node.js") is { } fromVolta)
            {
                return fromVolta;
            }

            if (package.RootElement.TryGetProperty("engines", out var engines) && engines.ValueKind == JsonValueKind.Object &&
                engines.TryGetProperty("node", out var enginesNode) && enginesNode.ValueKind == JsonValueKind.String)
            {
                return Range(enginesNode.GetString()!, "package.json's engines say node");
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>A version a version manager pins - 20, v20.11.0 - which asks for that release of Node.js: at least it, and no later major.</summary>
    internal static Declared? Pinned(string written, string saidBy)
    {
        if (Comparison().Match(written.Trim()) is not { Success: true, Index: 0 } version || version.Groups["operator"].Success) return null;
        if (version.Length != written.Trim().Length) return null;

        var because = $"{saidBy} {written.Trim()}";
        var pinned = Version(version.Groups["major"], version.Groups["minor"], version.Groups["patch"]);
        return new Declared(new ToolchainChoice.AtLeast(pinned, because), new ToolchainChoice.AtMost(pinned.MajorOnly, because));
    }

    /// <summary>
    /// A range as npm's semver writes one - >=18, ^20.11.0, ~20.11, 20.x, >=18 &lt;21, 18 - 20 - as the lowest and highest
    /// Node.js it lets in. Of alternatives joined by ||, the lowest and highest of them all are taken.
    /// </summary>
    internal static Declared? Range(string written, string saidBy)
    {
        var lowests = new List<LanguageVersion?>();
        var highests = new List<LanguageVersion?>();

        foreach (var alternative in written.Split("||"))
        {
            var (lowest, highest) = Alternative(alternative);
            lowests.Add(lowest);
            highests.Add(highest);
        }

        // Every alternative has to have a bound for the whole range to have it.
        var atLeast = lowests.All(bound => bound is not null) ? lowests.Min(bound => bound!.Value) : (LanguageVersion?)null;
        var atMost = highests.All(bound => bound is not null) ? highests.Max(bound => bound!.Value) : (LanguageVersion?)null;

        if (atLeast is null && atMost is null) return null;

        var because = $"{saidBy} {written.Trim()}";
        return new Declared(
            atLeast is { } lowestOfAll ? new ToolchainChoice.AtLeast(lowestOfAll, because) : null,
            atMost is { } highestOfAll ? new ToolchainChoice.AtMost(highestOfAll, because) : null);
    }

    /// <summary>The lowest and highest Node.js one alternative lets in - every comparison in it holding at once.</summary>
    private static (LanguageVersion? Lowest, LanguageVersion? Highest) Alternative(string written)
    {
        if (Hyphenated().Match(written) is { Success: true } hyphenated)
        {
            return (Version(hyphenated.Groups["major"], hyphenated.Groups["minor"], hyphenated.Groups["patch"]),
                    Version(hyphenated.Groups["toMajor"], hyphenated.Groups["toMinor"], hyphenated.Groups["toPatch"]));
        }

        LanguageVersion? lowest = null;
        LanguageVersion? highest = null;

        foreach (Match comparison in Comparison().Matches(written))
        {
            var version = Version(comparison.Groups["major"], comparison.Groups["minor"], comparison.Groups["patch"]);

            switch (comparison.Groups["operator"].Value)
            {
                case ">=":
                    lowest = Larger(lowest, version);
                    break;
                case ">":
                    lowest = Larger(lowest, Next(version));
                    break;
                case "<=":
                    highest = Smaller(highest, version);
                    break;
                case "<":
                    if (Previous(version) is { } below) highest = Smaller(highest, below);
                    break;
                case "^":
                    lowest = Larger(lowest, version);
                    highest = Smaller(highest, version.MajorOnly);
                    break;
                case "~":
                    lowest = Larger(lowest, version);
                    highest = Smaller(highest, version.Minor < 0 ? version.MajorOnly : new LanguageVersion(version.Major, version.Minor));
                    break;
                default:
                    // 20, 20.x, 20.11 and =20.11.0 let in that release, or that one version.
                    lowest = Larger(lowest, version);
                    highest = Smaller(highest, version);
                    break;
            }
        }

        return (lowest, highest);
    }

    /// <summary>A version as written, with what it leaves out - or writes as x or * - standing for any.</summary>
    private static LanguageVersion Version(Group major, Group minor, Group patch)
    {
        static int Number(Group part) => part.Success && int.TryParse(part.Value, out var number) ? number : -1;

        var minorNumber = Number(minor);
        return new LanguageVersion(int.Parse(major.Value), minorNumber, minorNumber < 0 ? -1 : Number(patch));
    }

    /// <summary>The first version after all of this one: after 20, 21; after 20.11, 20.12; after 20.11.1, 20.11.2.</summary>
    private static LanguageVersion Next(LanguageVersion version) =>
        version.Minor < 0 ? new LanguageVersion(version.Major + 1)
        : version.Patch < 0 ? new LanguageVersion(version.Major, version.Minor + 1)
        : new LanguageVersion(version.Major, version.Minor, version.Patch + 1);

    /// <summary>The last version before this one, as far as it can be said: before 21, any 20; before 20.12, any 20.11; before 20.0, any 19.</summary>
    private static LanguageVersion? Previous(LanguageVersion version)
    {
        if (version.Patch > 0) return new LanguageVersion(version.Major, version.Minor, version.Patch - 1);
        if (version.Minor > 0) return new LanguageVersion(version.Major, version.Minor - 1);
        return version.Major > 0 ? new LanguageVersion(version.Major - 1) : null;
    }

    private static LanguageVersion Larger(LanguageVersion? current, LanguageVersion candidate) =>
        current is { } known && known.CompareTo(candidate) >= 0 ? known : candidate;

    private static LanguageVersion Smaller(LanguageVersion? current, LanguageVersion candidate) =>
        current is { } known && known.CompareTo(candidate) <= 0 ? known : candidate;

    private static string? Read(string folder, string name)
    {
        var path = Path.Combine(folder, name);

        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
