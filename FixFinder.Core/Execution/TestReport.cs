using System.Globalization;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// What a test launcher of FixFinder's printed about each test it ran - JUnit's for Java, unittest's or pytest's for
/// Python - one line to a test, read back here. Whether a test passed is the test framework's to say, not FixFinder's.
/// </summary>
public static class TestReport
{
    /// <summary>What one test did, as its framework reported it.</summary>
    /// <param name="Status">
    /// SUCCESSFUL, FAILED, ABORTED or SKIPPED - or, from Python's frameworks, SETUP-FAILED: setting up for tests failed,
    /// and they did not run; TEARDOWN-FAILED: cleaning up after tests that ran failed; and, from pytest, FILE-SKIPPED: the
    /// whole file was skipped, as pytest.importorskip at its top does.
    /// </param>
    /// <param name="Frames">Where the failure was raised, innermost first: class or module, method, file and line.</param>
    public sealed record TestResult(
        string Status, string Name, string ClassName, string Method, string? Exception, string? Message,
        IReadOnlyList<(string Class, string Method, string? File, int Line)> Frames);

    /// <summary>What begins a launcher's line about one test.</summary>
    public const string ResultMarker = "FIXFINDER-TEST";

    /// <summary>What begins a launcher's line saying how many tests it found.</summary>
    public const string CountMarker = "FIXFINDER-TESTS-FOUND";

    /// <summary>What each test did, read from a launcher's lines; empty when the run was not a run of tests.</summary>
    public static IReadOnlyList<TestResult> ResultsIn(IEnumerable<string> lines)
    {
        var results = new List<TestResult>();

        foreach (var line in lines)
        {
            if (!line.StartsWith(ResultMarker + "\t", StringComparison.Ordinal)) continue;

            var fields = line.Split('\t').Select(Unescaped).ToArray();
            if (fields.Length < 8) continue;

            var frames = fields[7].Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(frame => frame.Split('#').Select(Decoded).ToArray())
                .Where(parts => parts.Length == 4)
                .Select(parts => (parts[0], parts[1], parts[2].Length > 0 && parts[2] != "null" ? parts[2] : null,
                    int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : -1))
                .ToList();

            results.Add(new TestResult(fields[1], fields[2], fields[3], fields[4], fields[5].Length > 0 ? fields[5] : null, fields[6].Length > 0 ? fields[6] : null, frames));
        }

        return results;
    }

    /// <summary>How many tests the framework found, when the launcher got as far as saying.</summary>
    public static int? TestsFound(IEnumerable<string> lines) =>
        lines.FirstOrDefault(line => line.StartsWith(CountMarker + "\t", StringComparison.Ordinal)) is { } found &&
        int.TryParse(found[(CountMarker.Length + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? count
            : null;

    /// <summary>
    /// A part of a place back as it was. A path can hold the # and ; that separate the parts, so Python's launchers write
    /// them as %23 and %3B, and % itself as %25; the JUnit launcher's class, method and file names never hold a %.
    /// </summary>
    private static string Decoded(string part) => Regex.Replace(part, "%(?:23|3B|25)", encoded => encoded.Value switch
    {
        "%23" => "#",
        "%3B" => ";",
        _ => "%",
    });

    /// <summary>A launcher's field back as it was: \t, \n, \r and \\ undone.</summary>
    private static string Unescaped(string field) => Regex.Replace(field, @"\\(.)", match => match.Groups[1].Value switch
    {
        "t" => "\t",
        "n" => "\n",
        "r" => "\r",
        var other => other,
    });
}
