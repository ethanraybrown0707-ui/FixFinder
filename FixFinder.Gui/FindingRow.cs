using System.ComponentModel;
using System.IO;
using FixFinder.Core.Checking;

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

    public string KindText => Finding.Kind switch
    {
        FindingKind.Syntax => "Syntax",
        FindingKind.Runtime => "Runtime",
        FindingKind.Logic => "Logic",
        _ => "Style",
    };

    public string FileName => Path.GetFileName(Finding.File);

    public string LineText => Finding.Line is { } line ? $"Line {line}" : "";

    public string LocationText => Finding.Line is { } line ? $"{FileName}  ·  line {line}" : FileName;

    public string Title => Finding.Title;

    private ExplanationLevel _level = ExplanationLevel.Student;

    /// <summary>
    /// How much the reader wants explained. The window sets it on every row when the choice changes, and the only
    /// thing that moves is the wording: nothing here is worked out again, and the program is not checked again.
    /// </summary>
    public ExplanationLevel Level
    {
        get => _level;
        set
        {
            if (_level == value) return;

            _level = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Explanation)));
        }
    }

    public string Explanation => Finding.Explanations.At(Level);

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
    public string SliceCode => _sliceCode ??= Render(Finding);

    public bool HasSlice => SliceCode.Length > 0;

    private string? _sliceCode;

    private static string Render(Finding finding)
    {
        if (finding.Slice is not { Count: > 1 } lines) return "";

        string[] source;
        try
        {
            source = File.ReadAllLines(finding.File);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "";
        }

        var shown = lines.Where(line => line >= 1 && line <= source.Length).Select(line => (Line: line, Text: source[line - 1].TrimEnd())).ToList();
        if (shown.Count < 2) return "";

        var indent = shown.Where(s => s.Text.Length > 0).Select(s => s.Text.Length - s.Text.TrimStart().Length).DefaultIfEmpty(0).Min();
        var width = shown[^1].Line.ToString().Length;
        return string.Join("\n", shown.Select(s => $"{s.Line.ToString().PadLeft(width)}  {(s.Text.Length >= indent ? s.Text[indent..] : s.Text.TrimStart())}"));
    }

    public string CheckedText => Finding.FixCheckedBy is { Length: > 0 } how ? how : "";

    public bool HasCheck => CheckedText.Length > 0;

    /// <summary>One tested stage, as a mark and a sentence the reader can hold the claim against.</summary>
    public sealed record VerificationLine(string Mark, string Text, bool Passed, bool Failed);

    /// <summary>The line of the finding this one follows from, filled in by the window, which can see them all.</summary>
    public int? FollowsLine { get; init; }

    /// <summary>The lines of the findings that follow from this one.</summary>
    public IReadOnlyList<int> ExplainsLines { get; init; } = [];

    public bool Follows => FollowsLine is not null;

    public string FollowsText => FollowsLine is { } line
        ? $"Follows from the problem on line {line} - fixing that one should remove this."
        : "";

    public bool Explains => ExplainsLines.Count > 0;

    public string ExplainsText => ExplainsLines.Count switch
    {
        0 => "",
        1 => $"The problem on line {ExplainsLines[0]} looks like a consequence of this one, so fixing this may remove it too.",
        _ => $"The problems on lines {string.Join(", ", ExplainsLines.Take(ExplainsLines.Count - 1))} and {ExplainsLines[^1]} " +
             "look like consequences of this one, so fixing this may remove them too.",
    };

    public bool HasState => Finding.State is { Rows.Count: > 0 };

    public string StateHeading => Finding.State is { } state ? $"WHAT LINE {state.Line} DID, EACH TIME IT RAN" : "";

    public IReadOnlyList<string> StateColumns => Finding.State?.Columns ?? [];

    public IReadOnlyList<StateRow> StateRows => Finding.State?.Rows ?? [];

    public bool StateWasCut => Finding.State?.WasCut == true;

    public string StateCutNote => Finding.State?.CutNote ?? "";

    /// <summary>Where to read more about what this finding is about, on the language's own documentation.</summary>
    public bool HasFurtherReading => Finding.FurtherReading is not null;

    public string FurtherReadingText => Finding.FurtherReading is { } reading
        ? $"Search {reading.SiteName} for {reading.Term}"
        : "";

    public string FurtherReadingUrl => Finding.FurtherReading?.Url ?? "";

    /// <summary>The CWE entry this finding is an instance of, when one fits exactly.</summary>
    public bool HasWeakness => Finding.Weakness is not null;

    public string WeaknessText => Finding.Weakness is { } weakness ? $"CWE-{weakness.Id}: {weakness.Title}" : "";

    public string WeaknessUrl => Finding.Weakness?.Url ?? "";

    /// <summary>Where the fix came from, so it can be checked rather than taken on trust.</summary>
    public bool HasOrigin => Finding.CameFrom is not null && Finding.Fix is not null;

    public string OriginText => Finding.CameFrom is { } came
        ? came.HasLink
            ? $"Taken from {came.SourceName}: {came.Title}"
            : $"Worked out by FixFinder's own rule `{came.Title}`"
        : "";

    public bool HasOriginLink => Finding.CameFrom?.HasLink == true;

    public string OriginUrl => Finding.CameFrom?.Url ?? "";

    public bool HasVerification => Finding.Verified.WasTested;

    public bool IsVerified => Finding.Verified.IsVerified;

    public string VerificationSummary => Finding.Verified.Summary;

    public IReadOnlyList<VerificationLine> VerificationLines => Finding.Verified.Steps
        .OrderBy(step => step.Stage)
        .Select(step => new VerificationLine(
            step.Result switch
            {
                StageResult.Passed => "✓",
                StageResult.Failed => "✕",
                StageResult.Inconclusive => "?",
                _ => "–",
            },
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

    public string FoundByText => Finding.FoundBy is { } technique ? $"Found by {technique}" : "";

    public bool HasWitness => Finding.Witness is not null;

    public string WitnessText => Finding.Witness is { } witness ? $"Fails when {witness}" : "";

    public bool HasConfirmation => Finding.Confirmation is not null;

    public string ConfirmationText => Finding.Confirmation is { } ran ? $"Confirmed: {ran}" : "";

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
