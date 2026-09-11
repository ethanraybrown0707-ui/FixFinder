namespace FixFinder.Core.Sources;

/// <summary>What FixFinder is allowed to do with a candidate.</summary>
public enum FixTier
{
    /// <summary>
    /// Shown and explained, never written. Everything from Stack Overflow lands here.
    /// </summary>
    /// <remarks>
    /// This is the normal outcome and the UI says so. Turning prose into a patch needs a
    /// language model, and there is not one in this tool by design - so an answer that
    /// describes the fix perfectly is still advisory.
    /// </remarks>
    Advisory,

    /// <summary>
    /// Carries at least one parsed unified diff, so it can be previewed and applied.
    /// </summary>
    AutoAppliable,

    /// <summary>
    /// The fix is a version bump, not a source change - "fixed in 2.1.3".
    /// </summary>
    /// <remarks>
    /// Reserved from the start rather than added later. It is one of the most common correct
    /// answers to a third-party crash, and it is not a patch to any file in your tree, so
    /// forcing it into either of the other two tiers would misrepresent it.
    /// </remarks>
    Dependency,
}

/// <summary>One term in a candidate's score, kept so the UI can show why it ranked where it did.</summary>
/// <param name="Name">Component name, matching the weights table.</param>
/// <param name="Weight">Its share of the total.</param>
/// <param name="Value">0-1 for this candidate.</param>
/// <param name="Explanation">Plain-English reason, shown in the detail pane.</param>
public sealed record ScoreComponent(string Name, double Weight, double Value, string Explanation)
{
    public double Contribution => Weight * Value;
}

/// <summary>
/// A possible fix found by one of the sources, in the shape the ranker and the UI both use.
/// </summary>
/// <remarks>
/// Flattened deliberately: a GitHub issue and a Stack Overflow answer have almost nothing in
/// common structurally, and keeping two shapes would push that difference into the ranker and
/// the window. The fields the ranker needs - how settled the discussion is, how much the
/// community endorsed it, how old it is - exist for both, so those are what this records.
/// <para>
/// Everything here came off the public internet and is treated as untrusted text throughout.
/// It is displayed, searched and scored; nothing in it reaches the disk until a unified diff
/// inside it has been parsed, path-checked and confirmed by hand.
/// </para>
/// </remarks>
/// <summary>Why a thread being closed is good news, or bad.</summary>
public enum ClosureMeaning
{
    /// <summary>Closed because it was dealt with. A GitHub issue.</summary>
    Resolved,

    /// <summary>Closed because it should not have been asked. A Stack Overflow question.</summary>
    Rejected,
}

public sealed class FixCandidate
{
    /// <summary>Which source produced it - "GitHub" or "Stack Overflow".</summary>
    public required string SourceName { get; init; }

    /// <summary>Short identifier shown in the list, such as "gh#1234" or "SO 4712".</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }
    public required string Url { get; init; }

    /// <summary>Body as plain text: HTML decoded and stripped, or markdown as written.</summary>
    public string BodyText { get; init; } = "";

    /// <summary>The body exactly as the API returned it, kept for code-block extraction in M5.</summary>
    public string? RawBody { get; init; }

    /// <summary>True when <see cref="RawBody"/> is HTML rather than markdown.</summary>
    public bool RawBodyIsHtml { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? LastActivityAt { get; init; }

    // ---------------------------------------------------------------- endorsement and settledness

    /// <summary>Score on Stack Overflow, or reaction count on a GitHub issue.</summary>
    public int Votes { get; init; }

    /// <summary>Answers on a Stack Overflow question, or comments on a GitHub issue.</summary>
    public int AnswerCount { get; init; }

    /// <summary>
    /// What <see cref="AnswerCount"/> counts, for display: "answers" or "comments".
    /// </summary>
    /// <remarks>
    /// Carried rather than derived from the source name, because the distinction is real and
    /// gets the reading of a row wrong when it is lost. A GitHub issue labelled "998 answers"
    /// suggests a thread full of solutions; it was a busy tracking issue with 998 comments and
    /// no answer at all.
    /// </remarks>
    public string AnswerNoun { get; init; } = "comments";

    public bool HasAcceptedAnswer { get; init; }

    public bool IsClosed { get; init; }

    /// <summary>GitHub's state reason ("completed", "not_planned") or the Stack Overflow close reason.</summary>
    public string? ClosedReason { get; init; }

    /// <summary>
    /// What being closed means on the site this came from.
    /// </summary>
    /// <remarks>
    /// The most misleading field on this record if it is not carried, because the word is shared
    /// and the meaning is opposite. GitHub closes an issue when it is <i>done</i>; Stack Overflow
    /// closes a question when the community has decided it should not have been asked - off
    /// topic, opinion-based, or without enough detail to answer. Reading both as "settled" ranks
    /// the questions a site rejected above the ones it answered.
    /// <para>
    /// Set by the source rather than worked out from <see cref="SourceName"/>, so that a source
    /// added later has to say which it means instead of inheriting whichever happened to be the
    /// default.
    /// </para>
    /// </remarks>
    public ClosureMeaning Closure { get; init; } = ClosureMeaning.Resolved;

    /// <summary>
    /// Where a duplicate points, when the API named it.
    /// </summary>
    /// <remarks>
    /// Worth following rather than merely penalising: on Stack Overflow the duplicate target is
    /// usually the canonical answer to the question you actually asked.
    /// </remarks>
    public string? DuplicateOfUrl { get; init; }

    // ---------------------------------------------------------------- provenance

    /// <summary>"owner/name" when the candidate came from a known repository.</summary>
    public string? Repository { get; init; }

    /// <summary>
    /// Attribution line that must be rendered wherever the body is shown.
    /// </summary>
    /// <remarks>
    /// Required by the Stack Exchange API terms, which is why it is carried on the candidate
    /// rather than assembled in the window: the obligation travels with the content.
    /// </remarks>
    public string? Attribution { get; init; }

    // ---------------------------------------------------------------- set later

    /// <summary>
    /// Commits and pull requests linked from the issue, fetched and parsed into patches in M5.
    /// </summary>
    /// <remarks>
    /// Settable rather than init-only because it is filled in a second pass: the search returns
    /// thirty issues, and only the top few are worth spending a request from the hourly
    /// allowance to follow.
    /// </remarks>
    public IReadOnlyList<string> LinkedPatchUrls { get; set; } = [];

    /// <summary>Set by the ranker in M4, and raised to AutoAppliable in M5 once a diff parses.</summary>
    public FixTier Tier { get; set; } = FixTier.Advisory;

    public double Score { get; set; }

    public IReadOnlyList<ScoreComponent> ScoreComponents { get; set; } = [];

    /// <summary>One-line form for the candidate list.</summary>
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

    /// <summary>Short description of how settled the discussion is.</summary>
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
