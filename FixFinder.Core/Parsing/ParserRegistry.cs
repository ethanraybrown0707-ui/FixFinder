using FixFinder.Core.Execution;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.Parsing;

/// <summary>Picks the right parser for a run's output and hands back what it found.</summary>
/// <remarks>
/// Selection is <b>score-based over the top three</b> rather than first-match or
/// highest-score-wins. First-match is order-dependent and fragile - Ruby's
/// <c>file:line:in 'x'</c> shape also matches half a gcc diagnostic. Highest-score-only is
/// brittle in the other direction: a confident detector whose <c>Parse</c> then fails on
/// slightly malformed output would leave the run with no error at all, when the second-best
/// parser would have read it fine. Trying the best three and keeping the first that produces
/// something is the cheap middle.
/// </remarks>
public sealed class ParserRegistry
{
    /// <summary>
    /// Below this, a detection is treated as noise rather than a weak signal.
    /// </summary>
    /// <remarks>
    /// Must stay at or below <see cref="GenericFileLineParser.MaxConfidence"/>, or the fallback
    /// parser is filtered out before it can ever be used and "works with any language" quietly
    /// stops being true. A specific parser recognising its own language scores far higher than
    /// 20, so keeping the bar this low costs nothing in precision.
    /// </remarks>
    private const int MinimumUsefulScore = GenericFileLineParser.MaxConfidence;

    private const int ParseAttempts = 3;

    private const string GenericLanguageId = "generic";

    public IReadOnlyList<IStackTraceParser> Parsers { get; }

    public ParserRegistry(IEnumerable<IStackTraceParser>? parsers = null)
    {
        Parsers = parsers?.ToArray() ??
        [
            new DotNetStackTraceParser(),
            new PythonTracebackParser(),
            new NodeStackTraceParser(),
            new JavaStackTraceParser(),
            new GoPanicParser(),
            new GoCompileParser(),
            new RustPanicParser(),
            new GccClangParser(),
            new MsvcParser(),
            new RubyParser(),
            new PhpParser(),
            new PowerShellParser(),
            new DartParser(),
            new ElixirParser(),
            new PerlParser(),
            new LuaParser(),
            new GenericFileLineParser(),
        ];
    }

    /// <summary>
    /// Finds the error in a run's captured output, or null if there is nothing to find.
    /// </summary>
    /// <param name="lines">Everything the target printed, in sequence order.</param>
    /// <param name="sourceRoots">Folders holding the user's own code, for culprit selection.</param>
    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines, IReadOnlyList<string>? sourceRoots = null)
    {
        if (lines.Count == 0) return null;

        // stderr first. Every runtime here writes its fatal trace to stderr, and a program's
        // stdout is full of things that merely look like errors - log lines quoting a
        // traceback, test names containing the word "exception", progress output. Restricting
        // the first pass is what stops those lookalikes being reported as this run's crash.
        var stdErr = lines.Where(l => l.Stream == StreamKind.StdErr).ToArray();
        var fromStdErr = TryParse(stdErr, sourceRoots);

        // A specific parser recognising the language on stderr is the best possible answer.
        if (fromStdErr is not null && fromStdErr.LanguageId != GenericLanguageId) return fromStdErr;

        // Only the fallback matched, or nothing did. That happens when a program splits its
        // trace across both streams, or writes the crash to stdout entirely - so it is worth
        // asking the merged stream, and preferring a specific parser there over a vague
        // generic read of stderr alone.
        var fromBoth = TryParse(lines, sourceRoots);

        if (fromBoth is null) return fromStdErr;
        if (fromStdErr is null) return fromBoth;

        return fromBoth.LanguageId != GenericLanguageId ? fromBoth : fromStdErr;
    }

    /// <summary>
    /// The independent errors behind the one <see cref="Parse"/> returned, if the output holds any.
    /// </summary>
    /// <remarks>
    /// Empty for everything except compiler output, and that is the honest answer rather than a
    /// gap: a program that crashed has one error, because the first one stopped it. Whatever
    /// would have failed next is unreachable until that one is fixed, so there is nothing to
    /// return and no way to find it. See <see cref="IMultiErrorParser"/>.
    /// <para>
    /// The error already reported is excluded, matched by fingerprint rather than by position so
    /// that the same diagnostic reported twice does not come back as something new to look at.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ParsedError> Others(
        ParsedError reported, IReadOnlyList<CapturedLine> lines, IReadOnlyList<string>? sourceRoots = null)
    {
        if (lines.Count == 0) return [];

        var parser = Parsers.FirstOrDefault(p =>
            p.LanguageId == reported.LanguageId && p is IMultiErrorParser);

        if (parser is not IMultiErrorParser multi) return [];

        IReadOnlyList<ParsedError> all;

        try
        {
            all = multi.ParseAll(lines);
        }
        catch (Exception)
        {
            // Same rule as Detect: a parser is regexes over hostile input, and one throwing
            // costs the skip button rather than the run.
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal) { FingerprintOf(reported) };
        var others = new List<ParsedError>();

        foreach (var error in all)
        {
            if (!seen.Add(FingerprintOf(error))) continue;

            CulpritFrameSelector.Select(error, sourceRoots ?? []);
            others.Add(error);
        }

        return others;
    }

    /// <summary>
    /// Identity for "is this the same diagnostic", without reaching for the fingerprinter.
    /// </summary>
    /// <remarks>
    /// Parsing lives below fingerprinting and should not start depending on it for this. The
    /// code, message and location together are exact enough here: these all came out of one
    /// compiler's output in one run, so there is no normalisation to do.
    /// </remarks>
    private static string FingerprintOf(ParsedError error) =>
        $"{error.ErrorCode}|{error.Message}|{error.Frames.FirstOrDefault()?.File}|{error.Frames.FirstOrDefault()?.Line}";

    private ParsedError? TryParse(IReadOnlyList<CapturedLine> lines, IReadOnlyList<string>? sourceRoots)
    {
        if (lines.Count == 0) return null;

        var text = lines.Select(l => l.Text).ToArray();

        var ranked = Parsers
            .Select(parser => (Parser: parser, Score: Score(parser, text)))
            .Where(candidate => candidate.Score >= MinimumUsefulScore)
            .OrderByDescending(candidate => candidate.Score)
            .Take(ParseAttempts)
            .ToArray();

        foreach (var (parser, _) in ranked)
        {
            var parsed = parser.Parse(lines);
            if (parsed is null) continue;

            CulpritFrameSelector.Select(parsed, sourceRoots ?? []);
            return parsed;
        }

        return null;
    }

    /// <summary>Runs a parser's Detect, capping the generic fallback and swallowing its failures.</summary>
    private static int Score(IStackTraceParser parser, IReadOnlyList<string> text)
    {
        int score;
        try
        {
            score = parser.Detect(text);
        }
        catch (Exception)
        {
            // A parser is a pile of regexes over hostile input. One throwing must not take the
            // whole run down - it just loses its turn.
            return 0;
        }

        // Enforced here as well as inside the parser, so a future parser cannot accidentally
        // outrank a real one by being generous with its own score.
        if (parser.LanguageId == GenericLanguageId) score = Math.Min(score, GenericFileLineParser.MaxConfidence);

        return score;
    }

    /// <summary>Every parser's raw detection score, for diagnosing why a run was read the way it was.</summary>
    public IReadOnlyList<(string LanguageId, int Score)> DetectionScores(IReadOnlyList<CapturedLine> lines)
    {
        var text = lines.Select(l => l.Text).ToArray();

        return Parsers
            .Select(parser => (parser.LanguageId, Score: Score(parser, text)))
            .OrderByDescending(entry => entry.Score)
            .ToArray();
    }
}
