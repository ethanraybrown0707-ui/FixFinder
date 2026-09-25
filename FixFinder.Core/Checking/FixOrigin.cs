namespace FixFinder.Core.Checking;

/// <summary>
/// Where a fix came from, so a reader can go and satisfy themselves it is a correct way to fix the code.
/// </summary>
/// <remarks>
/// FixFinder's own fixes come from rules in this repository, and the rule's name is the reference: it can be looked up,
/// read, and disagreed with. A fix taken from the web comes with the page it was taken from, because a stranger's
/// answer is worth exactly as much as the reader's own judgement of it - and they cannot judge it without the link.
/// <para>
/// Nothing is invented here. Where a fix arrived with no source, this is null and the report says nothing rather than
/// offering a plausible-looking citation.
/// </para>
/// </remarks>
public sealed record FixOrigin
{
    /// <summary>Who it came from: a rule of FixFinder's, or the name of the site.</summary>
    public required string SourceName { get; init; }

    /// <summary>What to call it - a rule's name, or the title of the page.</summary>
    public required string Title { get; init; }

    /// <summary>The page it came from, for a fix that came from one.</summary>
    public string? Url { get; init; }

    /// <summary>Whether there is a link worth offering, which means an http or https one and no other kind.</summary>
    public bool HasLink =>
        Uri.TryCreate(Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    /// <summary>A fix FixFinder worked out itself, named by the rule that did it.</summary>
    public static FixOrigin? OwnRule(string? ruleId) =>
        string.IsNullOrWhiteSpace(ruleId)
            ? null
            : new FixOrigin { SourceName = "FixFinder", Title = ruleId, Url = null };

    public string Describe => HasLink ? $"{SourceName}: {Title}" : $"{SourceName}'s rule {Title}";
}
