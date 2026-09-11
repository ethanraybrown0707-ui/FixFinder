using FixFinder.Core.Sources;

namespace FixFinder.Gui;

/// <summary>One scored signal, shaped for the breakdown list in the detail pane.</summary>
/// <param name="Name">The signal's name, matching the weights table.</param>
/// <param name="Contribution">Points this signal contributed, out of 100.</param>
/// <param name="Explanation">Why it scored that, in plain English.</param>
public sealed record ScoreRow(string Name, string Contribution, string Explanation)
{
    /// <summary>True for a penalty, which the window draws in red rather than as a contribution.</summary>
    public required bool IsPenalty { get; init; }
}

/// <summary>
/// One search result, shaped for the candidate list and its detail pane.
/// </summary>
/// <remarks>
/// A view type, like <see cref="OutputRow"/>. <c>FixCandidate</c> is a search result - it knows
/// about votes, close reasons and linked commits - and the window needs strings to draw and
/// flags to colour by. Keeping the two apart is what lets FixFinder.Core stay free of WPF.
/// </remarks>
public sealed class CandidateRow
{
    public required FixCandidate Candidate { get; init; }

    /// <summary>The one-line form shown in the list.</summary>
    public string HeaderLine => Candidate.Display;

    public string Title => Candidate.Title;
    public string Url => Candidate.Url;

    /// <summary>Green in the list: this one carries a diff and scored well enough to act on.</summary>
    public bool IsAutoAppliable => Candidate.Tier == FixTier.AutoAppliable;

    /// <summary>
    /// The tier stated in words rather than as a letter.
    /// </summary>
    /// <remarks>
    /// Spelled out because the distinction is the single most important thing on the screen and
    /// a bare "[B]" conveys none of it. Advisory does not mean second-rate - it means FixFinder
    /// will not be writing this one to your disk, and that is worth saying in full.
    /// </remarks>
    public string TierExplanation => Candidate.Tier switch
    {
        FixTier.AutoAppliable =>
            "Auto-appliable — a unified diff was found, so this one can be previewed and applied.",

        FixTier.Dependency =>
            "Dependency — the fix is a version change rather than an edit to your source.",

        _ => Candidate.LinkedPatchUrls.Count > 0
            ? "Advisory — linked commits exist and will be fetched as patches in a later step."
            : "Advisory — read it and apply it yourself. FixFinder will not edit any file from this.",
    };

    public string Provenance =>
        $"{Candidate.SourceName}  ·  {Candidate.Id}  ·  {Candidate.StateLabel}" +
        (Candidate.LastActivityAt is { } when ? $"  ·  last active {when.ToLocalTime():yyyy-MM-dd}" : "");

    /// <summary>The CC BY-SA line, which must be rendered wherever the body is.</summary>
    public string Attribution => Candidate.Attribution ?? "";
    public bool HasAttribution => !string.IsNullOrWhiteSpace(Candidate.Attribution);

    public string ScoreLine => $"Score {Candidate.Score:0}/100";

    /// <summary>The signals behind the score, contributions first and penalties last.</summary>
    public IReadOnlyList<ScoreRow> Breakdown =>
    [
        .. Candidate.ScoreComponents
            .Where(c => c.Weight > 0)
            .OrderByDescending(c => c.Contribution)
            .Select(c => new ScoreRow(
                c.Name,
                $"{100 * c.Contribution,5:0.0}",
                $"{c.Explanation}  (weight {c.Weight:0.00}, scored {c.Value:0.00})")
            { IsPenalty = false }),

        .. Candidate.ScoreComponents
            .Where(c => c.Weight == 0)
            .Select(c => new ScoreRow(c.Name, "", c.Explanation) { IsPenalty = true }),
    ];

    public string BodyExcerpt => HtmlText.Excerpt(Candidate.BodyText, 1200);

    public bool HasPatches => Candidate.LinkedPatchUrls.Count > 0;

    public string PatchSummary => Candidate.LinkedPatchUrls.Count switch
    {
        0 => "",
        1 => "1 linked patch:",
        var n => $"{n} linked patches:",
    };

    public IReadOnlyList<string> PatchUrls => Candidate.LinkedPatchUrls;

    public bool HasDuplicate => Candidate.DuplicateOfUrl is not null;

    /// <summary>
    /// A duplicate's pointer is worth following rather than merely penalising.
    /// </summary>
    public string DuplicateNote =>
        Candidate.DuplicateOfUrl is null
            ? ""
            : "Closed as a duplicate. The question it points at is usually the better read.";
}
