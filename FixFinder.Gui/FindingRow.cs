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

    public string Explanation => Finding.Explanation;

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

        return string.Join(Environment.NewLine, lines);
    }
}
