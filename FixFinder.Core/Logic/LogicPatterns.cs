using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Logic;

/// <summary>A logic mistake read straight from the code, with the change that corrects it.</summary>
/// <param name="PatternId">Which pattern found it.</param>
/// <param name="Line">The 1-based line it is on.</param>
/// <param name="Message">What is wrong, as one sentence about this code.</param>
/// <param name="Fix">The change - offered only once it compiles, and, when expected output was given, only once it prints that.</param>
public sealed record LogicFinding(string PatternId, int Line, string Message, LocalFix Fix);

/// <summary>A shape of code that is a logic mistake wherever it appears, whatever the program was for.</summary>
public interface ILogicPattern
{
    string Id { get; }

    /// <summary>The file extensions this pattern reads.</summary>
    IReadOnlySet<string> Extensions { get; }

    IEnumerable<LogicFinding> Find(SourceFile source);
}

/// <summary>
/// Logic mistakes that can be recognised without knowing what the program was meant to do.
/// </summary>
/// <remarks>
/// Most logic errors need the intended output to see: nothing about <c>i &lt; n</c> says it should have been <c>i &lt;= n</c>.
/// A few need nothing - the code contradicts itself. A loop that returns from its first pass either way never looks at a
/// second item. A total set back to zero inside the loop that adds to it only ever holds the last item. <c>sum / count</c>
/// stored in a <c>double</c> has already thrown the fraction away. <c>name.upper()</c> on a line of its own changes nothing.
/// <para>
/// Each pattern here is one of those: a shape that is wrong wherever it appears, with one change that makes the code do what
/// it visibly set out to. They are written to stay quiet when unsure - a pattern that fires on correct code teaches people to
/// ignore it - and every fix still goes through the same compile check as any other.
/// </para>
/// </remarks>
public static class LogicPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. PythonLogicPatterns.All,
        .. CLikeLogicPatterns.All,
    ];

    /// <summary>Every finding in a file, first line first.</summary>
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
                // A pattern is a handful of regexes over somebody's code; one that throws costs itself, never the run.
                log?.Invoke($"{pattern.Id}: gave up reading the code - {ex.Message}");
            }
        }

        return findings.OrderBy(f => f.Line).ThenBy(f => f.PatternId, StringComparer.Ordinal).ToList();
    }

    /// <summary>A finding as an error, so it travels the same road as any other: shown, searched for nothing, and fixed by a rule.</summary>
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

    /// <summary>An error that came from a program's output not matching what was expected.</summary>
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
