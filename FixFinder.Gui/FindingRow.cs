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

    public string ConfidenceTooltip => Finding.Confidence switch
    {
        Confidence.Certain => "Certain: the compiler, the run, your expected output or following every value through the code shows this is wrong.",
        Confidence.Likely => "Likely: code written this way is almost always a mistake.",
        _ => "Possible: this is often a mistake, but it can be what was meant.",
    };

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
