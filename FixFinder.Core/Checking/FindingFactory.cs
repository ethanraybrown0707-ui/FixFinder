using System.Text.RegularExpressions;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;

namespace FixFinder.Core.Checking;

/// <summary>Turns what a compiler, a run or a pattern found into a finding, with the guide's words filled in.</summary>
public static partial class FindingFactory
{
    [GeneratedRegex(@"^\[[\w-]+\]\s*")]
    private static partial Regex LintCategory();

    public static Finding FromError(
        ParsedError error, FindingKind kind, Severity severity, Confidence confidence, string fallbackFile, FixCandidate? fix = null)
    {
        var frame = LocalFixContext.OwnFrame(error);
        var file = frame?.File is { Length: > 0 } named && Path.IsPathRooted(named) ? named : fallbackFile;
        var guide = Guidebook.For(file, kind, fix?.LocalFix?.RuleId, error);

        return Build(kind, severity, confidence, file, frame?.Line, TitleOf(error), guide.Explanation, guide, fix) with
        {
            RuleId = fix?.LocalFix?.RuleId ?? error.ErrorCode ?? error.ShortExceptionType ?? "",
            Error = error,
            Family = CrashExplainedBy.GetValueOrDefault(error.ShortExceptionType ?? ""),
        };
    }

    private static readonly Dictionary<string, string> CrashExplainedBy = new(StringComparer.Ordinal)
    {
        ["KeyNotFoundException"] = "logic-count-from-missing-key",
        ["NullPointerException"] = "logic-count-from-missing-key",
        ["ZeroDivisionError"] = "analysis-division-by-zero",
        ["DivideByZeroException"] = "analysis-division-by-zero",
        ["ArithmeticException"] = "analysis-division-by-zero",
        ["NullReferenceException"] = "analysis-null-used",
    };

    public static Finding FromWarning(ParsedError warning, WarningRating rating, string fallbackFile, FixCandidate? fix = null) =>
        FromError(warning, rating.Kind, rating.Severity, rating.Confidence, fallbackFile, fix) with
        {
            Family = rating.SamePatternAs,
            RuleId = fix?.LocalFix?.RuleId ?? warning.ErrorCode ?? WarningName(warning),
        };

    private static string TitleThen(string title, string explanation)
    {
        var trimmed = title.TrimEnd();
        return (trimmed.Length > 0 && trimmed[^1] is '.' or ':' or ';' ? trimmed : trimmed + ".") + " " + explanation;
    }

    private static string WarningName(ParsedError warning) =>
        LintCategory().Match(warning.Message ?? "") is { Success: true } category
            ? $"{warning.LanguageId}-{category.Value.Trim().Trim('[', ']')}"
            : $"{warning.LanguageId}-warning";

    public static Finding FromPattern(LogicFinding finding, SourceFile source, string? fixCheckedBy, bool fixCompiles)
    {
        var guide = Guidebook.For(source.Path, finding.Kind, finding.PatternId);
        var fix = fixCompiles ? finding.Fix : null;

        var suggested = fix is not null ? TitleThen(fix.Title, fix.Explanation) : guide.SuggestedFix;
        var example = fix is not null ? CorrectedCode.From(fix, source) : guide.Example;

        return new Finding
        {
            Kind = finding.Kind,
            Severity = finding.Severity,
            Confidence = fixCompiles || finding.Fix is null ? finding.Confidence : Lower(finding.Confidence),
            File = source.Path,
            Line = finding.Line,
            Title = guide.Title ?? Sentence(finding.Message),
            Explanation = Sentence(finding.Message),
            WhyItMatters = guide.WhyItMatters,
            SuggestedFix = suggested,
            CorrectedExample = example,
            ExampleIsFromYourCode = fix is not null,
            FixCheckedBy = fix is not null ? fixCheckedBy : null,
            RuleId = finding.PatternId,
            Fix = fix,
            Family = finding.PatternId,
        };
    }

    public static Finding FromAnalysis(AnalysisFinding finding)
    {
        var guide = Guidebook.For(finding.Span.File, finding.Kind, finding.CheckId);

        return new Finding
        {
            Kind = finding.Kind,
            Severity = finding.Severity,
            Confidence = finding.Confidence,
            File = finding.Span.File,
            Line = finding.Span.Line,
            Title = guide.Title ?? Sentence(finding.Message),
            Explanation = Sentence(finding.Message),
            WhyItMatters = guide.WhyItMatters,
            SuggestedFix = guide.SuggestedFix,
            CorrectedExample = guide.Example,
            RuleId = finding.CheckId,
            Family = finding.CheckId,
            FoundBy = finding.FoundBy,
            Witness = finding.Witness,
        };
    }

    public static Finding FromWrongOutput(LogicRepairResult result, int runs, string file, SourceFile? source)
    {
        var guide = Guidebook.For(file, FindingKind.Logic, "wrong-output");
        var run = runs == 1 ? "The program" : $"Run {result.FailingRun} of {runs}";
        var line = result.Fix?.StartLine ?? result.Suspicious.FirstOrDefault()?.Line;

        var suspects = result.Suspicious.Take(5).Select(s => s.Line.ToString()).ToList();

        var suggested = result.Fix is { } fix
            ? TitleThen(fix.Title, fix.Explanation)
            : suspects.Count > 0
                ? $"Look first at line{(suspects.Count == 1 ? "" : "s")} {string.Join(", ", suspects)} - the wrong runs went through " +
                  "them more than the right ones. FixFinder tried small changes there and none made every run right, so the mistake " +
                  "probably needs more than a one-word change."
                : guide.SuggestedFix;

        return new Finding
        {
            Kind = FindingKind.Logic,
            Severity = Severity.Error,
            Confidence = result.Fix is null ? Confidence.Certain : result.Runs > 1 ? Confidence.Likely : Confidence.Possible,
            File = file,
            Line = line,
            Title = $"{run} printed the wrong output",
            Explanation = $"{guide.Explanation} {Sentence(result.Mismatch.Describe())}",
            WhyItMatters = guide.WhyItMatters,
            SuggestedFix = suggested,
            CorrectedExample = result.Fix is { } found ? CorrectedCode.From(found, source) : "",
            ExampleIsFromYourCode = result.Fix is not null,
            FixCheckedBy = result.Fix is not null ? LogicRepair.Describe(result) : null,
            RuleId = "wrong-output",
            Fix = result.Fix,
            Family = "wrong-output",
        };
    }

    private static Finding Build(
        FindingKind kind, Severity severity, Confidence confidence, string file, int? line, string title, string explanation,
        MistakeGuide guide, FixCandidate? fix)
    {
        var example = fix is not null ? CorrectedCode.From(fix) : null;

        return new Finding
        {
            Kind = kind,
            Severity = severity,
            Confidence = confidence,
            File = file,
            Line = line,
            Title = title,
            Explanation = explanation,
            WhyItMatters = guide.WhyItMatters,
            SuggestedFix = fix is null ? guide.SuggestedFix : FixText(fix),
            CorrectedExample = example ?? guide.Example,
            ExampleIsFromYourCode = example is not null,
            FixCheckedBy = example is not null ? fix?.CheckedBy : null,
            Fix = fix?.LocalFix,
        };
    }

    private static string FixText(FixCandidate fix) => fix.LocalFix is { } local
        ? TitleThen(local.Title, local.Explanation)
        : fix.Title;

    public static string TitleOf(ParsedError error)
    {
        var message = (error.Message ?? "").ReplaceLineEndings(" ").Trim();
        message = LintCategory().Replace(message, "");

        if (message.Length > 160) message = message[..157] + "...";

        return error.ExceptionType switch
        {
            "compile error" or "compile warning" or "link error" when error.ErrorCode is { Length: > 0 } code => $"{code}: {message}",
            "compile error" or "compile warning" or "link error" => Capitalised(message),
            _ when message.Length == 0 => error.ShortExceptionType ?? "Error",
            _ => $"{error.ShortExceptionType ?? error.ErrorCode ?? "Error"}: {message}",
        };
    }

    private static Confidence Lower(Confidence confidence) => confidence switch
    {
        Confidence.Certain => Confidence.Likely,
        _ => Confidence.Possible,
    };

    private static string Capitalised(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Sentence(string text)
    {
        var trimmed = Capitalised(text.Trim());
        return trimmed.Length == 0 || trimmed.EndsWith('.') || trimmed.EndsWith('?') ? trimmed : trimmed + ".";
    }
}
