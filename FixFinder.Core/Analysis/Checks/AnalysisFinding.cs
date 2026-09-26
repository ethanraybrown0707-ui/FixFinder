using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Symbolic;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>A mistake one of the analyses proved, or showed is possible, with the technique that found it.</summary>
public sealed record AnalysisFinding(
    string CheckId, SourceSpan Span, string Message, Severity Severity, Confidence Confidence, FindingKind Kind, string FoundBy)
{
    /// <summary>Inputs that make the line fail, when symbolic execution found some: "`values` is empty".</summary>
    public string? Witness { get; init; }

    /// <summary>The lines that decide the value that goes wrong, found by slicing the function backwards from it.</summary>
    public IReadOnlyList<int>? Slice { get; init; }

    /// <summary>The witness as values precise enough to run the code with.</summary>
    public IReadOnlyList<WitnessValue>? WitnessValues { get; init; }

    /// <summary>The function the finding is in, as the IR names it; <c>&lt;module&gt;</c> for top-level code.</summary>
    public string? Function { get; init; }

    /// <summary>What running the code with the witness showed, when it failed just as predicted.</summary>
    public string? Confirmation { get; init; }

    /// <summary>What the variables held each time the line ran, recorded during the run that confirmed it.</summary>
    public StateTrace? State { get; init; }

    /// <summary>A change to the program that removes what was found, when one can be written that changes nothing else.</summary>
    public LocalFixes.LocalFix? Fix { get; init; }
}

/// <summary>The program's own lines, for quoting the exact code a finding is about.</summary>
public sealed class SourceText
{
    private readonly Dictionary<string, string[]> _lines = new(StringComparer.OrdinalIgnoreCase);

    public string Of(SourceSpan span)
    {
        var line = Line(span.File, span.Line);
        if (line is null) return "";

        if (span.EndLine == span.Line && span.EndColumn > span.Column && span.EndColumn <= line.Length)
            return line[span.Column..span.EndColumn];

        return line.Trim();
    }

    public string? Line(string file, int number)
    {
        var lines = Lines(file);
        return number >= 1 && number <= lines.Count ? lines[number - 1] : null;
    }

    /// <summary>Every line of the file, or none when it cannot be read.</summary>
    public IReadOnlyList<string> Lines(string file)
    {
        if (!_lines.TryGetValue(file, out var lines))
        {
            try { lines = File.ReadAllLines(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { lines = []; }
            _lines[file] = lines;
        }

        return lines;
    }
}
