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
        ParsedError error, FindingKind kind, Severity severity, Confidence confidence, string fallbackFile, FixCandidate? fix = null) =>
        FromReported(error, kind, severity, confidence, fallbackFile, fix, isWarning: false);

    /// <summary>A finding from what a compiler or a run reported - an error, or a compiler's warning.</summary>
    private static Finding FromReported(
        ParsedError error, FindingKind kind, Severity severity, Confidence confidence, string fallbackFile, FixCandidate? fix, bool isWarning)
    {
        var frame = LocalFixContext.OwnFrame(error);
        var file = frame?.File is { Length: > 0 } named ? FileNamed(named, fallbackFile) : fallbackFile;
        var guide = Guidebook.For(file, kind, fix?.LocalFix?.RuleId, error, isWarning);

        // A line of a file that is not there - node:internal/modules/esm/resolve:271 - is no line of the chosen file's. With no
        // line of its own, the finding is at the line its change makes, when the change is to that file: the import it fixes.
        var line = frame?.File is { Length: > 0 } framed && !string.Equals(Path.GetFileName(framed), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase)
            ? null
            : frame?.Line;
        if (line is null && fix?.LocalFix is { } change && string.Equals(Path.GetFullPath(change.File), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
            line = change.StartLine;

        // An error the program raised itself, on purpose - the line it stopped at is its own raise or throw - is explained as
        // that: a check refusing a value. The guide for the error's name says what goes wrong when the language raises it -
        // ValueError's is int("forty-two") - which a raise ValueError("count must not be 0") in a check is not.
        if (!isWarning && kind == FindingKind.Runtime && fix?.LocalFix is null && line is { } stopped && RaisedHere(file, stopped) is { } raises)
            guide = GeneralGuides.RaisedOnPurpose(Guidebook.LanguageName(file), raises);

        return Build(kind, severity, confidence, file, line, TitleOf(error), guide.Explanation, guide, fix) with
        {
            RuleId = fix?.LocalFix?.RuleId ?? error.ErrorCode ?? error.ShortExceptionType ?? "",
            Error = error,
            Family = CrashFamily(error, file),
        };
    }

    [GeneratedRegex(@"^\s*(?<raises>raise|throw)\b")]
    private static partial Regex RaiseOrThrow();

    /// <summary>"raise" or "throw" when the line is one in the program's own code - raise ValueError(...), throw new ... - or null.</summary>
    private static string? RaisedHere(string file, int line) =>
        SourceFile.Read(file)?.Line(line) is { } written && RaiseOrThrow().Match(written) is { Success: true } said ? said.Groups["raises"].Value : null;

    [GeneratedRegex(@"^pop from (?:an )?empty (?:list|deque)$")]
    private static partial Regex PoppedFromEmpty();

    /// <summary>
    /// The check that finds the mistake a crash is, so the run that crashed and the analysis that proved it are one finding:
    /// by the exception's name, and by its message where one name means more than one mistake - Python's IndexError is a
    /// list read past its end, or pop() on an empty list; NoSuchElementException is Scala's None.get, or Java's getFirst()
    /// on an empty list. Each message is the one Python 3.13, Java 21 and .NET 10 were seen to give.
    /// </summary>
    private static string? CrashFamily(ParsedError error, string file) =>
        (error.ShortExceptionType, error.Message ?? "", Path.GetExtension(file).ToLowerInvariant()) switch
        {
            ("IndexError", var said, _) when PoppedFromEmpty().IsMatch(said) => "analysis-empty-collection",
            ("IndexError", "list index out of range", _) => "analysis-index-out-of-range",
            ("EmptyStackException", _, ".java") => "analysis-empty-collection",
            ("NoSuchElementException", not "No value present", ".java") => "analysis-empty-collection",
            // Scala's None.get: the crash, and the check that finds .get on an Option nothing checked first, are one mistake.
            ("NoSuchElementException", _, ".scala" or ".sc") => "logic-scala-option-get",
            ("InvalidOperationException", "Stack empty." or "Queue empty." or "Sequence contains no elements", ".cs") => "analysis-empty-collection",

            // A file, a connection or a socket used when closed, and a socket used before it was connected - as Python 3.13 on
            // Windows, Java 21 and .NET 10 said it.
            ("ValueError", "I/O operation on closed file.", _) => "analysis-used-after-close",
            ("ProgrammingError", "Cannot operate on a closed database.", _) => "analysis-connection-used-after-close",
            ("OSError", var said, ".py" or ".pyw") when said.StartsWith("[WinError 10057]", StringComparison.Ordinal) => "analysis-used-before-connect",
            ("OSError", var said, ".py" or ".pyw") when said.StartsWith("[WinError 10038]", StringComparison.Ordinal) => "analysis-connection-used-after-close",
            ("SocketException", "Socket is not connected", ".java") => "analysis-used-before-connect",
            ("SocketException", "Socket is closed", ".java") => "analysis-connection-used-after-close",
            ("InvalidOperationException", "The operation is not allowed on non-connected sockets.", ".cs") => "analysis-used-before-connect",
            ("SocketException", var said, ".cs") when said.Contains("the socket is not connected", StringComparison.Ordinal) => "analysis-used-before-connect",
            ("ObjectDisposedException", var said, ".cs") when said.Contains("System.Net.Sockets.", StringComparison.Ordinal) => "analysis-connection-used-after-close",

            // A function that called itself until there was no room left for the calls.
            ("RecursionError", _, ".py" or ".pyw") => "analysis-recursion-never-ends",
            ("StackOverflowError", _, ".java") => "analysis-recursion-never-ends",
            ("RangeError", "Maximum call stack size exceeded", _) => "analysis-recursion-never-ends",
            var (type, _, _) => CrashExplainedBy.GetValueOrDefault(type ?? ""),
        };

    /// <summary>
    /// The file a frame names: as named, when that is a whole path; else the file of that name beside the chosen one, when
    /// there is one - an OCaml backtrace, or a Java stack trace, names only the file - else the chosen file.
    /// </summary>
    private static string FileNamed(string named, string chosen)
    {
        if (Path.IsPathRooted(named)) return named;

        try
        {
            var beside = Path.Combine(Path.GetDirectoryName(chosen) ?? "", named);
            return File.Exists(beside) ? Path.GetFullPath(beside) : chosen;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return chosen;
        }
    }

    /// <summary>
    /// The pattern check that reports the same mistake at the same line, so the two become one finding. A collection
    /// changed while a loop walks it is found by a pattern where the code says so plainly and by analysis where it does not.
    /// </summary>
    private static string? SameMistakeAs(string checkId, string file) => (checkId, Path.GetExtension(file).ToLowerInvariant()) switch
    {
        ("analysis-changed-while-looping", ".py" or ".pyw") => "logic-python-modified-while-looping",
        ("analysis-changed-while-looping", ".java" or ".cs") => "logic-modified-while-looping",
        ("analysis-resource-not-closed", ".py" or ".pyw") => "logic-python-file-not-closed",
        ("analysis-text-built-in-loop", ".java" or ".cs") => "logic-string-built-in-loop",
        ("analysis-loop-never-ends", ".py" or ".pyw") => "logic-python-loop-never-advances",
        ("analysis-loop-never-ends", ".c" or ".cpp" or ".cc" or ".cxx" or ".c++" or ".java" or ".cs" or ".js" or ".mjs" or ".cjs") => "logic-loop-never-advances",
        _ => null,
    };

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
        FromReported(warning, rating.Kind, rating.Severity, rating.Confidence, fallbackFile, fix, isWarning: true) with
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
            Explanation = FoundThenExplained(Sentence(finding.Message), guide),
            Found = Sentence(finding.Message),
            WhyItMatters = guide.WhyItMatters,
            SuggestedFix = suggested,
            CorrectedExample = example,
            ExampleIsFromYourCode = fix is not null,
            FixCheckedBy = fix is not null ? fixCheckedBy : null,
            RuleId = finding.PatternId,
            Fix = fix,
            Change = fix is not null ? CodeChange.From(fix, source) : null,
            CameFrom = FixOrigin.OwnRule(fix?.RuleId),
            Family = finding.PatternId,
        };
    }

    /// <param name="source">The file the finding is in, needed to show a change to it side by side.</param>
    /// <param name="fixCheckedBy">What checking the change showed, when it was checked.</param>
    /// <param name="fixCompiles">False when a copy of the file with the change did not compile, so it is not offered.</param>
    public static Finding FromAnalysis(AnalysisFinding finding, SourceFile? source = null, string? fixCheckedBy = null, bool fixCompiles = false)
    {
        var guide = Guidebook.For(finding.Span.File, finding.Kind, finding.CheckId);

        // A change worked out for this very code shows what it would be, in place of the guide's general example - but
        // only once a copy of the file with it has been checked, and never when that copy did not compile.
        var fix = source is not null && fixCompiles ? finding.Fix : null;

        return new Finding
        {
            Kind = finding.Kind,
            Severity = finding.Severity,
            Confidence = finding.Confidence,
            File = finding.Span.File,
            Line = finding.Span.Line,
            Title = guide.Title ?? Sentence(finding.Message),
            Explanation = FoundThenExplained(Sentence(finding.Message), guide),
            Found = Sentence(finding.Message),
            WhyItMatters = guide.WhyItMatters,
            SuggestedFix = fix is not null ? TitleThen(fix.Title, fix.Explanation) : guide.SuggestedFix,
            CorrectedExample = fix is not null ? CorrectedCode.From(fix, source!) : guide.Example,
            ExampleIsFromYourCode = fix is not null,
            FixCheckedBy = fix is not null ? fixCheckedBy : null,
            Fix = fix,
            Change = fix is not null ? CodeChange.From(fix, source) : null,
            CameFrom = fix is not null ? FixOrigin.OwnRule(fix.RuleId) : null,
            RuleId = finding.CheckId,
            Family = SameMistakeAs(finding.CheckId, finding.Span.File) ?? finding.CheckId,
            FoundBy = finding.FoundBy,
            Witness = finding.Witness,
            Slice = finding.Slice,
            Confirmation = finding.Confirmation,
            State = finding.State,
        };
    }

    public static Finding FromWrongOutput(LogicRepairResult result, int runs, string file, SourceFile? source)
    {
        var stopped = result.Mismatch.Ending != RunEnding.Finished;
        var rule = stopped ? "stopped-before-expected-output" : "wrong-output";
        var guide = Guidebook.For(file, FindingKind.Logic, rule);
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
            Title = stopped ? $"{run} stopped before printing what you expected" : $"{run} printed the wrong output",
            Explanation = $"{guide.Explanation} {Sentence(result.Mismatch.Describe())}",
            Found = Sentence(result.Mismatch.Describe()),
            WhyItMatters = guide.WhyItMatters,
            SuggestedFix = suggested,
            CorrectedExample = result.Fix is { } found ? CorrectedCode.From(found, source) : "",
            ExampleIsFromYourCode = result.Fix is not null,
            FixCheckedBy = result.Fix is not null ? LogicRepair.Describe(result) : null,
            RuleId = rule,
            Fix = result.Fix,
            Change = result.Fix is { } repaired ? CodeChange.From(repaired, source) : null,
            CameFrom = result.Fix is null ? null : FixOrigin.OwnRule(rule),
            Family = rule,
        };
    }

    /// <summary>
    /// What a finding of a check says: what was found in this program first, then the guide's account of this kind of
    /// mistake, so what is particular to this program is never hidden behind the general idea.
    /// </summary>
    private static string FoundThenExplained(string found, MistakeGuide guide) =>
        string.IsNullOrWhiteSpace(guide.Explanation) ? found : $"{found} {guide.Explanation}";

    private static Finding Build(
        FindingKind kind, Severity severity, Confidence confidence, string file, int? line, string title, string explanation,
        MistakeGuide guide, FixCandidate? fix)
    {
        var example = fix is not null ? CorrectedCode.From(fix) : null;

        // A candidate from a source other than a local rule carries no structural edit, and without one there is
        // nothing to lay side by side: the example is shown on its own rather than as a change to the user's code.
        var edit = fix?.LocalFix;

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
            Fix = edit,
            Change = edit is not null ? CodeChange.From(edit, SourceFile.Read(edit.File)) : null,
            CameFrom = Origin(fix, edit),
        };
    }

    /// <summary>
    /// Where a fix came from: the page, when it was taken from one, and otherwise the rule of FixFinder's own that
    /// worked it out. A candidate with neither is left unattributed rather than credited to something plausible.
    /// </summary>
    private static FixOrigin? Origin(FixCandidate? candidate, LocalFix? edit)
    {
        if (candidate is { Url.Length: > 0 } page)
        {
            return new FixOrigin { SourceName = page.SourceName, Title = page.Title, Url = page.Url };
        }

        // What the language suggested itself is credited to it, not to a rule of FixFinder's.
        if (candidate is not null && edit is not null && RuntimeSuggestion.SuggestedBy(candidate) is { } language)
        {
            return FixOrigin.FromTheLanguage(language);
        }

        return FixOrigin.OwnRule(edit?.RuleId);
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
