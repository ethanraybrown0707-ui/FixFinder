using System.Globalization;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution.Libraries;

/// <summary>
/// Versions compared as Maven compares them: 1.10 comes after 1.9, a pre-release such as 1.0-beta or 1.0-rc1 before
/// 1.0, and 1.0 is the same as 1.0.0. Used to choose the highest version, as Gradle does, and to pick one from a range.
/// </summary>
public static partial class MavenVersion
{
    [GeneratedRegex(@"\d+|[a-zA-Z]+")]
    private static partial Regex Parts();

    /// <summary>Where each qualifier comes; a release has none, and anything not named here comes after the rest.</summary>
    private static readonly Dictionary<string, int> QualifierOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alpha"] = 1, ["a"] = 1, ["beta"] = 2, ["b"] = 2, ["milestone"] = 3, ["m"] = 3, ["rc"] = 4, ["cr"] = 4,
        ["snapshot"] = 5, [""] = 6, ["ga"] = 6, ["final"] = 6, ["release"] = 6, ["sp"] = 7,
    };

    public static int Compare(string left, string right)
    {
        var (a, b) = (Split(left), Split(right));

        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            var compared = ComparePart(i < a.Count ? a[i] : null, i < b.Count ? b[i] : null);
            if (compared != 0) return compared;
        }

        return 0;
    }

    public static string Highest(IEnumerable<string> versions) => versions.Aggregate((best, next) => Compare(next, best) > 0 ? next : best);

    /// <summary>
    /// The highest of the versions that lies in a range written as Maven writes one - [1.0,2.0), [1.5,), (,1.0] or [1.2] -
    /// or null when none does, or the range is one of the forms not followed here, such as two ranges joined.
    /// </summary>
    public static string? HighestIn(string range, IEnumerable<string> versions)
    {
        range = range.Trim();
        if (range.Length < 3 || range[0] is not ('[' or '(') || range[^1] is not (']' or ')') || range.Contains("],", StringComparison.Ordinal) || range.Contains("),", StringComparison.Ordinal))
            return null;

        var inner = range[1..^1].Split(',');
        if (inner.Length > 2) return null;

        var (lowest, highest) = inner.Length == 1 ? (inner[0].Trim(), inner[0].Trim()) : (inner[0].Trim(), inner[1].Trim());
        var (fromIncluded, toIncluded) = (range[0] == '[', range[^1] == ']');

        var fitting = versions.Where(version =>
            (lowest.Length == 0 || Compare(version, lowest) is var low && (low > 0 || low == 0 && fromIncluded)) &&
            (highest.Length == 0 || Compare(version, highest) is var high && (high < 0 || high == 0 && toIncluded))).ToList();

        return fitting.Count > 0 ? Highest(fitting) : null;
    }

    public static bool IsRange(string version) => version.TrimStart().StartsWith('[') || version.TrimStart().StartsWith('(');

    /// <summary>A version's parts: whole numbers, and words, with trailing zeros and empty qualifiers dropped.</summary>
    private static List<object> Split(string version)
    {
        var parts = Parts().Matches(version)
            .Select(match => int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? (object)number : match.Value.ToLowerInvariant())
            .ToList();

        while (parts.Count > 0 && parts[^1] is 0 or "" or "ga" or "final" or "release") parts.RemoveAt(parts.Count - 1);
        return parts;
    }

    private static int ComparePart(object? left, object? right) => (left, right) switch
    {
        (null, null) => 0,
        (int a, int b) => a.CompareTo(b),
        (int, string) => 1,
        (string, int) => -1,
        (null, int b) => 0.CompareTo(b),
        (int a, null) => a.CompareTo(0),
        (null, string b) => Rank("").CompareTo(Rank(b)),
        (string a, null) => Rank(a).CompareTo(Rank("")),
        (string a, string b) => Rank(a) != Rank(b) ? Rank(a).CompareTo(Rank(b)) : string.CompareOrdinal(a, b),
        _ => 0,
    };

    private static int Rank(string qualifier) => QualifierOrder.TryGetValue(qualifier, out var rank) ? rank : 8;
}
