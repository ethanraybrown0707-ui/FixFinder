using System.Text.RegularExpressions;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Teaching;

/// <summary>
/// How to make a finding happen for yourself, built only from what happened when FixFinder looked: the call or the input a
/// run confirmed it with, the arguments and input the program's own run was given, the values worked out from the code that
/// make it fail, or the language refusing the code. Null when nothing ran it and no input was worked out - a way of writing
/// code that is usually a mistake, or a warning - as there is then nothing true to say about how to make it happen.
/// </summary>
public static partial class Reproduction
{
    /// <summary>How many lines of typed input are named before the rest are counted: enough to type, few enough to read.</summary>
    private const int MostLinesNamed = 6;

    /// <summary>
    /// What a confirming run ran: "Running `take(flag=False)` stopped with ...", or "Running the program, typing `5` then
    /// `0` stopped with ...".
    /// </summary>
    [GeneratedRegex(@"^Running (?<ran>.+?) stopped with ")]
    private static partial Regex ConfirmedBy();

    public static string? For(Finding finding)
    {
        // A run that broke as the analysis said it would: what it ran is what makes it happen.
        if (finding.Confirmation is { } confirmation && ConfirmedBy().Match(confirmation) is { Success: true } confirmed)
        {
            var ran = confirmed.Groups["ran"].Value;
            return ran.StartsWith('`')
                ? $"Call {ran} - FixFinder did, and that call stopped here."
                : $"Run {ran} - FixFinder did, and that run stopped here.";
        }

        // The program's own run, as it was given its arguments and its input - kept, with what the run said, when the crash
        // and a check that found the same mistake were made one finding.
        if (finding.RanWith is { } given && finding.Error is not null)
            return $"Run {given.Program} {Given(given)}, as FixFinder did - that run " +
                   (finding.Severity == Severity.Error ? "stopped here." : "printed this here, and carried on.");

        if (finding.Witness is { Length: > 0 } witness)
            return $"Worked out from the code, it fails here when {witness}: run it with values like those to see it happen.";

        if (finding.Kind == FindingKind.Syntax && finding.Severity == Severity.Error && finding.Error is not null)
            return $"Check or run it again: {LanguageOf(finding)} refuses this before the program starts, every time, until it is changed.";

        return null;
    }

    /// <summary>"with the arguments `a b`, typing `5` then `0`", or "with no arguments and nothing typed in".</summary>
    private static string Given(RunGiven given)
    {
        var said = new List<string>();
        if (given.Arguments.Trim().Length > 0) said.Add($"with the arguments `{given.Arguments.Trim()}`");

        var typed = (given.Input ?? "").Replace("\r\n", "\n").TrimEnd('\n');
        if (given.Input is { Length: > 0 })
        {
            var lines = typed.Split('\n');
            var named = lines.Take(MostLinesNamed).Select(line => line.Length == 0 ? "an empty line" : $"`{line}`");
            var rest = lines.Length > MostLinesNamed ? $", then {lines.Length - MostLinesNamed} more lines" : "";
            said.Add($"typing {string.Join(" then ", named)}{rest}");
        }

        return said.Count switch
        {
            0 => "with no arguments and nothing typed in",
            _ => string.Join(", ", said),
        };
    }

    private static string LanguageOf(Finding finding) =>
        CodeLanguage.Of(finding.InNotebook?.Notebook ?? finding.File) is { IsAny: false } language ? language.Name : "the language";
}
