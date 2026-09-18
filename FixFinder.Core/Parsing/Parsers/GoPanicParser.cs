using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads Go panics and fatal runtime errors.</summary>
public sealed partial class GoPanicParser : IStackTraceParser
{
    public string LanguageId => "go";
    public string DisplayName => "Go";

    [GeneratedRegex(@"^(?<kind>panic|fatal error):\s*(?<msg>.*)$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^goroutine\s+\d+\s+\[[^\]]*\]:\s*$")]
    private static partial Regex GoroutinePattern();

    [GeneratedRegex(@"^(?<sym>[\w./*()\[\]\-]+(?:\.[\w*()\[\]\-]+)+)\(.*\)$")]
    private static partial Regex SymbolLinePattern();

    [GeneratedRegex(@"^\t(?<file>.+?):(?<line>\d+)(?:\s+\+0x[0-9a-fA-F]+)?\s*$")]
    private static partial Regex LocationLinePattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;
        var sawSymbol = false;

        foreach (var line in lines)
        {
            if (HeaderPattern().IsMatch(line)) score += 45;
            if (GoroutinePattern().IsMatch(line)) score += 40;
            if (line.Contains("[recovered]", StringComparison.Ordinal)) score += 15;
            if (line.Contains("runtime.gopanic", StringComparison.Ordinal)) score += 20;

            if (SymbolLinePattern().IsMatch(line)) { sawSymbol = true; continue; }
            if (sawSymbol && LocationLinePattern().IsMatch(line)) score += 12;
            sawSymbol = false;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        var headerIndex = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (HeaderPattern().IsMatch(lines[i].Text)) { headerIndex = i; break; }
        }

        if (headerIndex < 0) return null;

        var header = HeaderPattern().Match(lines[headerIndex].Text);

        var goroutineIndex = -1;
        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            if (!GoroutinePattern().IsMatch(lines[i].Text)) continue;
            goroutineIndex = i;
            break;
        }

        var frames = new List<ErrorFrame>();
        var end = headerIndex + 1;

        if (goroutineIndex >= 0)
        {
            end = goroutineIndex + 1;

            for (var i = goroutineIndex + 1; i < lines.Count - 1; i++)
            {
                if (GoroutinePattern().IsMatch(lines[i].Text)) break;

                var symbol = SymbolLinePattern().Match(lines[i].Text);
                if (!symbol.Success)
                {
                    if (lines[i].Text.Trim().Length == 0) continue;
                    continue;
                }

                var location = LocationLinePattern().Match(lines[i + 1].Text);
                if (!location.Success) continue;

                frames.Add(new ErrorFrame
                {
                    Order = frames.Count,
                    Symbol = symbol.Groups["sym"].Value,
                    File = ParserHelpers.CleanFilePath(location.Groups["file"].Value),
                    Line = int.Parse(location.Groups["line"].Value),
                    RawLine = $"{lines[i].Text}\n{lines[i + 1].Text}",
                });

                i++;
                end = i + 1;
            }
        }

        var message = header.Groups["msg"].Value.Trim();

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = frames.Count > 0 ? 90 : 65,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            ExceptionType = message.StartsWith("runtime error:", StringComparison.Ordinal)
                ? "runtime error"
                : header.Groups["kind"].Value,
            Message = message.Length > 0 ? message : null,
            Frames = frames,
        };
    }
}
