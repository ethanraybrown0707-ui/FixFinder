using FixFinder.Core.Execution;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.Parsing;

/// <summary>Picks the right parser for a run's output and hands back what it found.</summary>
public sealed class ParserRegistry
{
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

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines, IReadOnlyList<string>? sourceRoots = null)
    {
        if (lines.Count == 0) return null;

        var stdErr = lines.Where(l => l.Stream == StreamKind.StdErr).ToArray();
        var fromStdErr = TryParse(stdErr, sourceRoots);

        if (fromStdErr is not null && fromStdErr.LanguageId != GenericLanguageId) return fromStdErr;

        var fromBoth = TryParse(lines, sourceRoots);

        if (fromBoth is null) return fromStdErr;
        if (fromStdErr is null) return fromBoth;

        return fromBoth.LanguageId != GenericLanguageId ? fromBoth : fromStdErr;
    }

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

    private static int Score(IStackTraceParser parser, IReadOnlyList<string> text)
    {
        int score;
        try
        {
            score = parser.Detect(text);
        }
        catch (Exception)
        {
            return 0;
        }

        if (parser.LanguageId == GenericLanguageId) score = Math.Min(score, GenericFileLineParser.MaxConfidence);

        return score;
    }

    public IReadOnlyList<(string LanguageId, int Score)> DetectionScores(IReadOnlyList<CapturedLine> lines)
    {
        var text = lines.Select(l => l.Text).ToArray();

        return Parsers
            .Select(parser => (parser.LanguageId, Score: Score(parser, text)))
            .OrderByDescending(entry => entry.Score)
            .ToArray();
    }
}
