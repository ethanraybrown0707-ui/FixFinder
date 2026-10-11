using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// The warnings a script language's own syntax check gives - each a message of its own, at its file and line - as each
/// language was seen to write them.
/// </summary>
/// <remarks>
/// <para>Ruby 3.3's ruby -wc: <c>rb_warn.rb:2: warning: assigned but unused variable - unused</c>.</para>
/// <para>
/// Perl 5.42's perl -wc: <c>Name "main::totl" used only once: possible typo at marks.pl line 3.</c> Perl writes its errors
/// the same way, so its warnings are read only when the check passed - when it said syntax OK.
/// </para>
/// </remarks>
public static partial class ScriptWarnings
{
    [GeneratedRegex(@"^(?<file>.+?\.rb):(?<line>\d+): warning: (?<message>.+?)\s*$")]
    private static partial Regex RubyWarning();

    [GeneratedRegex(@"^(?<message>.+?) at (?<file>\S+?\.p[lm]) line (?<line>\d+)\.\s*$")]
    private static partial Regex PerlWarning();

    public static IReadOnlyList<ParsedError> In(ScriptLanguages.ScriptLanguage language, IReadOnlyList<CapturedLine> lines) => language.LanguageId switch
    {
        "ruby" => Read(lines, RubyWarning(), "ruby"),
        "perl" when lines.Any(line => line.Text.TrimEnd().EndsWith("syntax OK", StringComparison.Ordinal)) => Read(lines, PerlWarning(), "perl"),
        _ => [],
    };

    private static IReadOnlyList<ParsedError> Read(IReadOnlyList<CapturedLine> lines, Regex warning, string languageId)
    {
        var found = new List<ParsedError>();

        foreach (var line in lines)
        {
            if (warning.Match(line.Text) is not { Success: true } match) continue;

            var frame = new ErrorFrame
            {
                Order = 0,
                File = ParserHelpers.CleanFilePath(match.Groups["file"].Value),
                Line = int.Parse(match.Groups["line"].Value),
                RawLine = line.Text,
            };

            found.Add(new ParsedError
            {
                LanguageId = languageId,
                Confidence = 90,
                RawText = line.Text.TrimEnd(),
                FirstLineSequence = line.Sequence,
                ExceptionType = "compile warning",
                Message = match.Groups["message"].Value,
                Frames = [frame],
                CulpritFrame = frame,
            });
        }

        return found;
    }
}
