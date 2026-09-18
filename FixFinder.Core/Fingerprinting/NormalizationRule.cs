using System.Text.RegularExpressions;

namespace FixFinder.Core.Fingerprinting;

/// <summary>One named scrubbing step applied to an error message before it becomes a query.</summary>
public sealed class NormalizationRule
{
    public required string Name { get; init; }

    public required Regex Pattern { get; init; }

    public required Func<Match, string> Replace { get; init; }

    public required string Reason { get; init; }

    public bool RelaxedOnly { get; init; }
}

/// <summary>A record of one rule actually changing something, for the audit trail.</summary>
public sealed record AppliedNormalization(string RuleName, string Reason, int Replacements, string Result);
