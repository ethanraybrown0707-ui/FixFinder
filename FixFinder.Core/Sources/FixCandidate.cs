namespace FixFinder.Core.Sources;

/// <summary>What FixFinder is allowed to do with a candidate.</summary>
public enum FixTier
{
    Advisory,

    AutoAppliable,

    Dependency,
}

/// <summary>One term in a candidate's score, kept so the UI can show why it ranked where it did.</summary>
public sealed record ScoreComponent(string Name, double Weight, double Value, string Explanation)
{
    public double Contribution => Weight * Value;
}

/// <summary>Why a thread being closed is good news, or bad.</summary>
public enum ClosureMeaning
{
    Resolved,

    Rejected,
}

/// <summary>A possible fix found by one of the sources, in the shape the ranker and the UI both use.</summary>
public sealed class FixCandidate
{
    public required string SourceName { get; init; }

    public required string Id { get; init; }

    public required string Title { get; init; }
    public required string Url { get; init; }

    public string BodyText { get; init; } = "";

    public string? RawBody { get; init; }

    public bool RawBodyIsHtml { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? LastActivityAt { get; init; }

    public int Votes { get; init; }

    public int AnswerCount { get; init; }

    public string AnswerNoun { get; init; } = "comments";

    public bool HasAcceptedAnswer { get; init; }

    public bool IsClosed { get; init; }

    public string? ClosedReason { get; init; }

    public ClosureMeaning Closure { get; init; } = ClosureMeaning.Resolved;

    public string? DuplicateOfUrl { get; init; }

    public string? Repository { get; init; }

    public string? Attribution { get; init; }

    public IReadOnlyList<string> LinkedPatchUrls { get; set; } = [];

    public FixTier Tier { get; set; } = FixTier.Advisory;

    public string? Command { get; init; }

    public string CommandDescription { get; init; } = "the command that installs it";

    public LocalFixes.LocalFix? LocalFix { get; init; }

    public string? CheckedBy { get; init; }

    public double Score { get; set; }

    public IReadOnlyList<ScoreComponent> ScoreComponents { get; set; } = [];

    public string Display
    {
        get
        {
            var tier = Tier switch
            {
                FixTier.AutoAppliable => "[A]",
                FixTier.Dependency => "[D]",
                _ => "[B]",
            };

            return $"{tier} {Score,3:0}  {Id} · {StateLabel} · {Title}";
        }
    }

    public string StateLabel
    {
        get
        {
            if (HasAcceptedAnswer) return "accepted";
            if (DuplicateOfUrl is not null) return "duplicate";
            if (IsClosed) return ClosedReason is { Length: > 0 } reason ? $"closed ({reason})" : "closed";
            if (AnswerCount > 0) return $"{AnswerCount} {AnswerNoun}";

            return "open";
        }
    }
}
