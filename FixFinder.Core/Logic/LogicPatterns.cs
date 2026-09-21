using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Logic;

/// <summary>A logic mistake read straight from the code, with the change that corrects it.</summary>
public sealed record LogicFinding(string PatternId, int Line, string Message, LocalFix? Fix)
{
    public Severity Severity { get; init; } = Severity.Warning;
    public Confidence Confidence { get; init; } = Confidence.Likely;
    public FindingKind Kind { get; init; } = FindingKind.Logic;
}

/// <summary>A shape of code that is a logic mistake wherever it appears, whatever the program was for.</summary>
public interface ILogicPattern
{
    string Id { get; }

    IReadOnlySet<string> Extensions { get; }

    IEnumerable<LogicFinding> Find(SourceFile source);
}

/// <summary>Logic mistakes that can be recognised without knowing what the program was meant to do.</summary>
public static class LogicPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. PythonLogicPatterns.All,
        .. PythonReviewPatterns.All,
        .. CLikeLogicPatterns.All,
        .. CLikeReviewPatterns.All,
        .. ManagedReviewPatterns.All,
    ];

    public static IReadOnlyList<LogicFinding> Scan(SourceFile source, Action<string>? log = null)
    {
        var extension = Path.GetExtension(source.Path).ToLowerInvariant();
        var findings = new List<LogicFinding>();

        foreach (var pattern in All.Where(p => p.Extensions.Contains(extension)))
        {
            try
            {
                findings.AddRange(pattern.Find(source));
            }
            catch (Exception ex)
            {
                log?.Invoke($"{pattern.Id}: gave up reading the code - {ex.Message}");
            }
        }

        return findings.OrderBy(f => f.Line).ThenBy(f => f.PatternId, StringComparer.Ordinal).ToList();
    }

    public static ParsedError ToError(LogicFinding finding, SourceFile source) => new()
    {
        LanguageId = "logic",
        Confidence = 70,
        RawText = finding.Message,
        FirstLineSequence = 0,
        ExceptionType = "logic error",
        ErrorCode = finding.PatternId,
        Message = finding.Message,
        Frames = [new ErrorFrame { Order = 0, File = source.Path, Line = finding.Line, RawLine = "", Origin = FrameOrigin.FirstParty }],
    };

    public static ParsedError WrongOutput(OutputMismatch mismatch, string file, int? line = null) => new()
    {
        LanguageId = "logic",
        Confidence = 90,
        RawText = mismatch.Describe(),
        FirstLineSequence = 0,
        ExceptionType = "wrong output",
        ErrorCode = "wrong-output",
        Message = mismatch.Describe(),
        Frames = [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = "", Origin = FrameOrigin.FirstParty }],
    };
}

/// <summary>Turns a logic finding back into its fix, for the rule engine to check like any other.</summary>
public sealed class LogicPatternRule : ILocalFixRule
{
    public string Id => "logic-pattern";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "logic", ExceptionType: "logic error", ErrorCode: { } pattern }) return null;
        if (context.Frame is not { Line: { } line } frame || context.Read(frame.File) is not { } source) return null;

        return LogicPatterns.All.FirstOrDefault(p => p.Id == pattern)?.Find(source).FirstOrDefault(f => f.Line == line)?.Fix;
    }
}
