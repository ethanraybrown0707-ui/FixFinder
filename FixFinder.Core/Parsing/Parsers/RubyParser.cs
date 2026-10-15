using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads what stops a Ruby program: the exception it raised while running, and the syntax error ruby -wc gives before it
/// runs - as Ruby 3.3 writes them, and Ruby 3.4's newer syntax errors.
/// </summary>
/// <remarks>
/// <code>
/// rb_name.rb:2:in `&lt;main&gt;': undefined local variable or method `totl' for main (NameError)
///
/// puts totl
///      ^^^^
/// Did you mean?  total
/// 	from other.rb:5:in `&lt;main&gt;'
/// </code>
/// <para>
/// Ruby 3.3 shows the line, ^ marks under the part it means and "Did you mean?" between an exception and the calls that
/// led to it; all of it is kept, and the calls are read after it. Ruby 3.4 quotes the method 'Integer#/' rather than `/'.
/// A syntax error is <c>ruby.exe: rb_syntax.rb:4: syntax error, unexpected end-of-input, expecting `end' (SyntaxError)</c>,
/// or from Ruby 3.4 <c>ruby: rb_syntax.rb:4: syntax errors found (SyntaxError)</c> with the error under a ^ in the lines
/// after it.
/// </para>
/// </remarks>
public sealed partial class RubyParser : IStackTraceParser
{
    public string LanguageId => "ruby";
    public string DisplayName => "Ruby";

    /// <summary>How many lines - a blank, the code, its ^ marks, "Did you mean?" and its names - come at most before the calls.</summary>
    private const int LinesBeforeTheCalls = 8;

    [GeneratedRegex(@"^(?<file>.+?):(?<line>\d+):in\s+[`'](?<sym>[^']+)':\s*(?<msg>.*?)\s*\((?<type>[\w:]+)\)\s*$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^\s+from\s+(?<file>.+?):(?<line>\d+):in\s+[`'](?<sym>[^']+)'\s*$")]
    private static partial Regex FramePattern();

    /// <summary>A syntax error, after the path of the ruby that gave it - ruby.EXE, as Windows names it from PATH, as well as ruby.exe.</summary>
    [GeneratedRegex(@"^(?:.*?ruby(?:\.exe)?:\s+)?(?<file>[^:]+?\.rb):(?<line>\d+):\s+(?<msg>.+?)\s*\(SyntaxError\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SyntaxHeader();

    /// <summary>Ruby 3.4's line under the code, with the message after its ^ marks: "    |    ^ expected an `end` to close the `def` statement".</summary>
    [GeneratedRegex(@"^\s*\|\s*\^+\s*(?<detail>\S.*?)\s*$")]
    private static partial Regex PrismDetail();

    /// <summary>
    /// A file as Ruby named it, cleaned - and, when it is a full path, written as Windows writes it: Ruby names a file
    /// require_relative brought in as C:/Users/..., with forward slashes.
    /// </summary>
    private static string? FullPathIfRooted(string file)
    {
        var cleaned = ParserHelpers.CleanFilePath(file);
        return cleaned is { Length: > 0 } && Path.IsPathRooted(cleaned) ? Path.GetFullPath(cleaned) : cleaned;
    }

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (HeaderPattern().IsMatch(line)) score += 65;
            if (SyntaxHeader().IsMatch(line)) score += 75;
            if (FramePattern().IsMatch(line)) score += 25;
            if (line.Contains(".rb:", StringComparison.Ordinal)) score += 5;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (HeaderPattern().Match(lines[i].Text) is { Success: true } header) return Raised(lines, i, header);
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (SyntaxHeader().Match(lines[i].Text) is { Success: true } syntax) return Refused(lines, i, syntax);
        }

        return null;
    }

    private ParsedError Raised(IReadOnlyList<CapturedLine> lines, int headerIndex, Match header)
    {
        var frames = new List<ErrorFrame>
        {
            new()
            {
                Order = 0,
                Symbol = header.Groups["sym"].Value,
                File = FullPathIfRooted(header.Groups["file"].Value),
                Line = int.Parse(header.Groups["line"].Value),
                RawLine = lines[headerIndex].Text,
            },
        };

        // The line, its ^ marks and Ruby's "Did you mean?" come between the exception and the calls that led to it.
        var end = headerIndex + 1;
        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            var text = lines[i].Text;

            if (FramePattern().Match(text) is { Success: true } frame)
            {
                frames.Add(new ErrorFrame
                {
                    Order = frames.Count,
                    Symbol = frame.Groups["sym"].Value,
                    File = FullPathIfRooted(frame.Groups["file"].Value),
                    Line = int.Parse(frame.Groups["line"].Value),
                    RawLine = text,
                });
                end = i + 1;
                continue;
            }

            if (frames.Count > 1 || i > headerIndex + LinesBeforeTheCalls || HeaderPattern().IsMatch(text) || SyntaxHeader().IsMatch(text)) break;
            end = i + 1;
        }

        while (end > headerIndex + 1 && lines[end - 1].Text.Trim().Length == 0) end--;

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = 88,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            ExceptionType = header.Groups["type"].Value,
            Message = header.Groups["msg"].Value.Trim() is { Length: > 0 } m ? m : null,
            Frames = frames,
        };
    }

    private ParsedError Refused(IReadOnlyList<CapturedLine> lines, int headerIndex, Match syntax)
    {
        var message = syntax.Groups["msg"].Value.Trim();
        var end = headerIndex + 1;

        // The code and the ^ under it follow - and in Ruby 3.4, the message itself, after the ^.
        for (var i = headerIndex + 1; i < lines.Count && i <= headerIndex + 12; i++)
        {
            var text = lines[i].Text;
            if (text.Trim().Length == 0 || SyntaxHeader().IsMatch(text) || HeaderPattern().IsMatch(text)) break;
            end = i + 1;

            if (message.StartsWith("syntax error", StringComparison.Ordinal) && message.EndsWith("found", StringComparison.Ordinal) &&
                PrismDetail().Match(text) is { Success: true } detail)
                message = detail.Groups["detail"].Value;
        }

        var frame = new ErrorFrame
        {
            Order = 0,
            File = FullPathIfRooted(syntax.Groups["file"].Value),
            Line = int.Parse(syntax.Groups["line"].Value),
            RawLine = lines[headerIndex].Text,
        };

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = 92,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            ExceptionType = "SyntaxError",
            Message = message,
            Frames = [frame],
            CulpritFrame = frame,
        };
    }
}
