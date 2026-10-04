using System.Globalization;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution.Versions;

/// <summary>
/// A version of a language or its toolchain as its own people number it: Python 3.12, Go 1.22, Node 20, C# 12 - the major
/// and minor numbers, which is where new parts of a language arrive. A minor of -1 stands for a whole major release.
/// </summary>
/// <remarks>
/// A patch is kept only where it decides something: Go's go line can name one - go mod init writes go 1.27.0 - and a Go of
/// an earlier patch of that release will not build the module. A patch of -1 stands for any patch of its release, so
/// 1.27 is met by 1.27.0 and by 1.27.3 alike.
/// </remarks>
public readonly partial record struct LanguageVersion(int Major, int Minor = -1, int Patch = -1) : IComparable<LanguageVersion>
{
    [GeneratedRegex(@"(?<major>\d+)(?:\.(?<minor>\d+))?")]
    private static partial Regex Numbers();

    [GeneratedRegex(@"(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?")]
    private static partial Regex NumbersWithPatch();

    /// <summary>The first version written in the text - 3.12 from "3.12.4", 20 from "v20.11.0", 1.22 from "go1.22.1" - or null when there is none.</summary>
    public static LanguageVersion? Find(string? text)
    {
        if (text is null || Numbers().Match(text) is not { Success: true } match) return null;

        var major = int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture);
        var minor = match.Groups["minor"].Success ? int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture) : -1;
        return new LanguageVersion(major, minor);
    }

    /// <summary>The first version written in the text with its patch, when it has one - 1.27.3 from "go1.27.3", 1.22 from "go 1.22" - or null.</summary>
    public static LanguageVersion? FindWithPatch(string? text)
    {
        if (text is null || NumbersWithPatch().Match(text) is not { Success: true } match) return null;

        var major = int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture);
        var minor = int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture);
        var patch = match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].Value, CultureInfo.InvariantCulture) : -1;
        return new LanguageVersion(major, minor, patch);
    }

    /// <summary>The same version with only its major number - Node is compared by major release.</summary>
    public LanguageVersion MajorOnly => new(Major);

    /// <summary>The same version without its patch - the release whose language it is: 1.27 for 1.27.3.</summary>
    public LanguageVersion Release => new(Major, Minor);

    public int CompareTo(LanguageVersion other)
    {
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Math.Max(Minor, 0) != Math.Max(other.Minor, 0)) return Math.Max(Minor, 0).CompareTo(Math.Max(other.Minor, 0));

        return Patch < 0 || other.Patch < 0 ? 0 : Patch.CompareTo(other.Patch);
    }

    public static bool operator <(LanguageVersion left, LanguageVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(LanguageVersion left, LanguageVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(LanguageVersion left, LanguageVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(LanguageVersion left, LanguageVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        Minor < 0 ? Major.ToString(CultureInfo.InvariantCulture) : Patch < 0 ? $"{Major}.{Minor}" : $"{Major}.{Minor}.{Patch}";
}

/// <summary>One toolchain of a language on this computer: the program that runs it, its version, and how it was found.</summary>
/// <param name="Program">The interpreter or tool itself: python.exe, node.exe, go.exe.</param>
/// <param name="Version">Its version, as the language numbers its releases.</param>
/// <param name="VersionText">Its version in full, as it says it: 3.12.4.</param>
/// <param name="FoundIn">Where it was found, as a reader is told: "on PATH", "installed for this user".</param>
public sealed record VersionedToolchain(string Program, LanguageVersion Version, string VersionText, string FoundIn);
