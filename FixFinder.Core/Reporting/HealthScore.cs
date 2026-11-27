using FixFinder.Core.Checking;

namespace FixFinder.Core.Reporting;

/// <summary>
/// How healthy a program's code is, out of 100: 100 with no problem found, and less for each problem, by how bad it is
/// and how sure FixFinder is of it. Efficiency findings are not problems, and take nothing off.
/// </summary>
/// <remarks>
/// The points are FixFinder's own choice, not worked out from any study of programs. They follow what each severity
/// means: an error - the program fails, or gives the wrong answer - takes off most; a warning - it works, but not
/// reliably, or not as intended - two fifths of that; and a suggestion - it works, and there is a better way - a tenth.
/// A finding FixFinder thinks likely takes off three quarters of what a certain one does, and one it thinks possible
/// half, each rounded to a whole point; five certain errors take a program to 0. What each kind of finding took off is
/// said with the score, so the score never stands on its own.
/// </remarks>
public static class HealthScore
{
    /// <summary>The points one finding takes off, by how bad it is and how sure FixFinder is of it.</summary>
    public static int PointsFor(Severity severity, Confidence confidence) => (severity, confidence) switch
    {
        (Severity.Error, Confidence.Certain) => 20,
        (Severity.Error, Confidence.Likely) => 15,
        (Severity.Error, Confidence.Possible) => 10,
        (Severity.Warning, Confidence.Certain) => 8,
        (Severity.Warning, Confidence.Likely) => 6,
        (Severity.Warning, Confidence.Possible) => 4,
        (Severity.Suggestion, Confidence.Certain) => 2,
        (Severity.Suggestion, Confidence.Likely) => 2,
        _ => 1,
    };

    /// <summary>The findings of one kind and certainty, how many of them there are, and what each takes off.</summary>
    public sealed record Part(Severity Severity, Confidence Confidence, int Count, int Each)
    {
        public int Points => Count * Each;

        /// <summary>"2 warnings FixFinder thinks likely took off 12, 6 each".</summary>
        public string Said
        {
            get
            {
                var kind = Severity switch { Severity.Error => "error", Severity.Warning => "warning", _ => "suggestion" };
                var sure = Confidence switch
                {
                    Confidence.Certain => "FixFinder is certain of",
                    Confidence.Likely => "FixFinder thinks likely",
                    _ => "FixFinder thinks possible",
                };
                return Count == 1
                    ? $"1 {kind} {sure} took off {Points}"
                    : $"{Count} {kind}s {sure} took off {Points}, {Each} each";
            }
        }
    }

    /// <summary>The score, and the parts of it each kind of finding took off, the worst first.</summary>
    public sealed record Health(int Score, IReadOnlyList<Part> Parts)
    {
        /// <summary>"72 out of 100: 1 error FixFinder is certain of took off 20; 2 warnings FixFinder thinks likely took off 12, 6 each."</summary>
        public string Said => Parts.Count == 0
            ? $"{Score} out of 100: no problem was found."
            : $"{Score} out of 100: {string.Join("; ", Parts.Select(part => part.Said))}.";
    }

    /// <summary>The health of a check's findings: 100 less what each problem takes off, and never below 0.</summary>
    public static Health Of(IEnumerable<Finding> findings)
    {
        var parts = findings.Where(finding => finding.Kind != FindingKind.Performance)
            .GroupBy(finding => (finding.Severity, finding.Confidence))
            .OrderBy(group => group.Key.Severity).ThenBy(group => group.Key.Confidence)
            .Select(group => new Part(group.Key.Severity, group.Key.Confidence, group.Count(), PointsFor(group.Key.Severity, group.Key.Confidence)))
            .ToList();

        return new Health(Math.Max(0, 100 - parts.Sum(part => part.Points)), parts);
    }
}
