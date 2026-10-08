using System.ComponentModel;
using FixFinder.Core.Checking;
using FixFinder.Core.Engine;
using FixFinder.Core.Reporting;

namespace FixFinder.Gui;

/// <summary>One finding as the report shows it.</summary>
public sealed class FindingRow(Finding finding) : INotifyPropertyChanged
{
    private bool _isExpanded = finding.Severity == Severity.Error;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Finding Finding { get; } = finding;

    public string SeverityText => Finding.Severity.ToString();

    public string ConfidenceText => Finding.Confidence.ToString();

    public string ConfidenceDots => Finding.Confidence switch
    {
        Confidence.Certain => "●●●",
        Confidence.Likely => "●●○",
        _ => "●○○",
    };

    /// <summary>
    /// What stands behind this finding's confidence, rather than what the word means in general.
    /// </summary>
    /// <remarks>
    /// The same word covers a compiler refusing the file and a pattern that is usually a mistake, and a reader
    /// deciding whether to act on a finding wants the difference, not the dictionary definition.
    /// </remarks>
    public string ConfidenceTooltip => Evidence.Behind(Finding);

    public string EvidenceText => Evidence.For(Finding);

    public string KindText => FindingText.Kind(Finding);

    /// <summary>The file the finding is in: for a notebook's code, the notebook, not the script FixFinder checked it as.</summary>
    public string FileName => FindingText.FileName(Finding);

    public string LineText => FindingText.Line(Finding);

    public string LocationText => FindingText.Location(Finding);

    public string Title => Finding.Title;

    /// <summary>Where this finding stands against the last check of the same program, or null when there was none to compare.</summary>
    public SinceLastCheck? Status { get; init; }

    public bool HasStatus => Status is not null;

    public bool IsNew => Status == SinceLastCheck.New;

    public string StatusText => Status switch
    {
        SinceLastCheck.New => "New",
        SinceLastCheck.StillThere => "Still there",
        _ => "",
    };

    public string StatusTooltip => Status switch
    {
        SinceLastCheck.New => "The last check of this program did not find this.",
        SinceLastCheck.StillThere => "The last check of this program found this too.",
        _ => "",
    };

    public string Explanation => Finding.Explanation;

    /// <summary>The reader's own lines beside the corrected ones. Empty whenever FixFinder has no fix to show.</summary>
    public IReadOnlyList<ChangeLine> ChangeLines => Finding.Change?.Lines ?? [];

    public bool HasChange => ChangeLines.Count > 0;

    public string ChangeSummary => Finding.Change?.Summary ?? "";

    private bool ChangeIsLong => Finding.Change?.IsLong == true;

    private bool? _changeShown;

    /// <summary>A short change is open; a long one waits to be asked for, so the card stays readable.</summary>
    public bool ChangeShown
    {
        get => _changeShown ?? !ChangeIsLong;
        set
        {
            if (ChangeShown == value) return;

            _changeShown = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChangeShown)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChangeToggleText)));
        }
    }

    public string ChangeToggleText => ChangeShown ? "Hide the change" : $"Show the change  ·  {ChangeSummary}";

    public string WhyItMatters => Finding.WhyItMatters;

    public string SuggestedFix => Finding.SuggestedFix;

    public string CorrectedExample => Finding.CorrectedExample;

    public bool HasExample => Finding.CorrectedExample.Trim().Length > 0;

    public string ExampleLabel => Finding.ExampleIsFromYourCode ? "CORRECTED CODE  ·  FROM YOUR FILE" : "EXAMPLE OF CORRECTED CODE";

    /// <summary>The lines that decide the value that goes wrong, numbered, with their shared indent taken off.</summary>
    public string SliceCode => _sliceCode ??= FindingText.Slice(Finding);

    public bool HasSlice => SliceCode.Length > 0;

    private string? _sliceCode;
    public string CheckedText => Finding.FixCheckedBy is { Length: > 0 } how ? how : "";

    public bool HasCheck => CheckedText.Length > 0;

    /// <summary>One tested stage, as a mark and a sentence the reader can hold the claim against.</summary>
    public sealed record VerificationLine(string Mark, string Text, bool Passed, bool Failed);

    /// <summary>The finding this one follows from, filled in by the window, which can see them all.</summary>
    public Finding? FollowsFrom { get; init; }

    /// <summary>The findings that follow from this one, in the order of their lines.</summary>
    public IReadOnlyList<Finding> LeadsTo { get; init; } = [];

    public bool Follows => FollowsFrom is not null;

    public string FollowsText => FollowsFrom is { } cause ? FindingText.Follows(cause) : "";

    public bool Explains => LeadsTo.Count > 0;

    public string ExplainsText => FindingText.Explains(LeadsTo);

    public bool HasState => Finding.State is { Rows.Count: > 0 };

    public string StateHeading => FindingText.StateHeading(Finding).ToUpperInvariant();

    public IReadOnlyList<string> StateColumns => Finding.State?.Columns ?? [];

    public IReadOnlyList<StateRow> StateRows => Finding.State?.Rows ?? [];

    public bool StateWasCut => Finding.State?.WasCut == true;

    public string StateCutNote => Finding.State?.CutNote ?? "";

    /// <summary>Where to read more about what this finding is about, on the language's own documentation.</summary>
    public bool HasFurtherReading => Finding.FurtherReading is not null;

    public string FurtherReadingText => FindingText.FurtherReading(Finding) ?? "";

    public string FurtherReadingUrl => Finding.FurtherReading?.Url ?? "";

    /// <summary>The CWE entry this finding is an instance of, when one fits exactly.</summary>
    public bool HasWeakness => Finding.Weakness is not null;

    public string WeaknessText => FindingText.Weakness(Finding) ?? "";

    public string WeaknessUrl => Finding.Weakness?.Url ?? "";

    /// <summary>Where the fix came from, so it can be checked rather than taken on trust.</summary>
    public bool HasOrigin => Finding.CameFrom is not null && Finding.Fix is not null;

    public string OriginText => FindingText.Origin(Finding) ?? "";

    public bool HasOriginLink => Finding.CameFrom?.HasLink == true;

    public string OriginUrl => Finding.CameFrom?.Url ?? "";

    public bool HasVerification => Finding.Verified.WasTested;

    public bool IsVerified => Finding.Verified.IsVerified;

    public string VerificationSummary => Finding.Verified.Summary;

    public IReadOnlyList<VerificationLine> VerificationLines => Finding.Verified.Steps
        .OrderBy(step => step.Stage)
        .Select(step => new VerificationLine(
            FindingText.Mark(step.Result),
            step.Detail,
            step.Result == StageResult.Passed,
            step.Result == StageResult.Failed))
        .ToList();

    public bool CanSearch => Finding.Error is not null && Finding.Kind is FindingKind.Syntax or FindingKind.Runtime;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;

            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToggleText)));
        }
    }

    public string ToggleText => IsExpanded ? "Hide details" : "Show details";

    public bool HasFoundBy => Finding.FoundBy is not null;

    public string FoundByText => FindingText.FoundBy(Finding) ?? "";

    public bool HasWitness => Finding.Witness is not null;

    public string WitnessText => FindingText.Witness(Finding) ?? "";

    public bool HasConfirmation => Finding.Confirmation is not null;

    public string ConfirmationText => FindingText.Confirmation(Finding) ?? "";

    /// <summary>What the fix changes in what the program does, from comparing it with the original path by path.</summary>
    public IReadOnlyList<string> FixChanges => Finding.FixChanges ?? [];

    public bool HasFixChanges => FixChanges.Count > 0;

    public string AsText()
    {
        var lines = new List<string>
        {
            $"{SeverityText} ({ConfidenceText}, {KindText}) - {LocationText}",
            Title,
            "",
            $"What is wrong: {Explanation}",
            $"Why it matters: {WhyItMatters}",
            $"How to fix it: {SuggestedFix}",
        };

        if (HasSlice)
        {
            lines.Insert(lines.Count - 1, "The lines that decide it:");
            lines.InsertRange(lines.Count - 1, SliceCode.Split('\n').Select(line => "    " + line));
        }

        if (HasConfirmation) lines.Insert(2, ConfirmationText);
        if (HasWitness) lines.Insert(2, WitnessText);
        if (HasFoundBy) lines.Insert(2, FoundByText);

        if (HasExample)
        {
            lines.Add(Finding.ExampleIsFromYourCode ? "Corrected code:" : "Example:");
            lines.AddRange(CorrectedExample.Split('\n').Select(line => "    " + line));
        }

        if (HasCheck) lines.Add($"Checked: {CheckedText}");

        if (HasFixChanges)
        {
            lines.Add("What the fix changes:");
            lines.AddRange(FixChanges.Select(change => "    " + change));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
