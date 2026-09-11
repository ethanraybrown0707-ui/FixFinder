using System.Text.RegularExpressions;

namespace FixFinder.Core.Fingerprinting;

/// <summary>One named scrubbing step applied to an error message before it becomes a query.</summary>
/// <remarks>
/// Named, ordered and individually reportable on purpose. With no model in the loop, the only
/// way a person can tell why a search returned nothing is to see exactly what was removed from
/// their error text and why - so every rule records what it changed into
/// <see cref="AppliedNormalization"/> and the window renders that trace.
/// </remarks>
public sealed class NormalizationRule
{
    public required string Name { get; init; }

    public required Regex Pattern { get; init; }

    public required Func<Match, string> Replace { get; init; }

    /// <summary>Why this rule exists, shown beside it in the normalisation trace.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// When true, the rule is applied only to the relaxed query.
    /// </summary>
    /// <remarks>
    /// Exists for exactly one rule - quoted literals - and it is the difference between a
    /// search that works and one that does not. See <see cref="ErrorNormalizer"/>.
    /// </remarks>
    public bool RelaxedOnly { get; init; }
}

/// <summary>A record of one rule actually changing something, for the audit trail.</summary>
public sealed record AppliedNormalization(string RuleName, string Reason, int Replacements, string Result);
