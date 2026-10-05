using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Execution.Versions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Checking;

/// <summary>
/// What Go says when a program asks for a later Go than the one building it - the module's go line naming a later Go than
/// any here - or when its code is newer than its go line lets it be: said once, as a note naming the Go it needs and what
/// to change, beside the errors themselves, so they are not read as mistakes in the code.
/// </summary>
/// <remarks>
/// Both are read from Go's own words, as Go 1.27 prints them: "go: go.mod requires go &gt;= 1.28 (running go 1.27.0;
/// GOTOOLCHAIN=local)", and "built-in max requires go1.21 or later (-lang was set to go1.20; check go.mod)".
/// </remarks>
public static partial class GoVersionErrors
{
    /// <summary>How to install a later Go: winget has one package of Go, which is always its newest.</summary>
    internal const string InstallAdvice = "go.dev/dl has every Go release, and winget installs the newest with:\n  winget install GoLang.Go";

    [GeneratedRegex(@"^go: (?<who>.+?) requires go >= (?<needed>\d+\.\d+(?:\.\d+)?)\S* \(running go (?<running>\d+\.\d+(?:\.\d+)?)\S*; GOTOOLCHAIN=local\)")]
    private static partial Regex GoTooOld();

    [GeneratedRegex(@"requires go(?<needed>\d+\.\d+(?:\.\d+)?) or later \(-lang was set to go(?<set>\d+\.\d+(?:\.\d+)?); check go\.mod\)")]
    private static partial Regex GoLineTooOld();

    /// <summary>The note for what go said, or null when nothing it said is about the Go the program asks for.</summary>
    public static string? NoteFor(IReadOnlyList<ParsedError> errors, IReadOnlyList<CapturedLine> output, string chosen)
    {
        if (!chosen.EndsWith(".go", StringComparison.OrdinalIgnoreCase)) return null;

        foreach (var line in output)
        {
            if (GoTooOld().Match(line.Text.Trim()) is { Success: true } tooOld)
                return TooOldNote(tooOld.Groups["who"].Value, LanguageVersion.FindWithPatch(tooOld.Groups["needed"].Value)!.Value, tooOld.Groups["running"].Value);
        }

        foreach (var error in errors)
        {
            if (GoLineTooOld().Match(error.Message ?? "") is not { Success: true } tooOld) continue;

            var where = error.Frames.FirstOrDefault() is { File: { } file, Line: > 0 } frame ? $"{Path.GetFileName(file)}:{frame.Line}" : Path.GetFileName(chosen);
            return GoLineNote(where, tooOld.Groups["needed"].Value, LanguageVersion.FindWithPatch(tooOld.Groups["set"].Value)!.Value, chosen);
        }

        return null;
    }

    /// <summary>A go.mod, a go.work or a module the program uses that asks for a later Go than the one that built it.</summary>
    private static string TooOldNote(string who, LanguageVersion needed, string running)
    {
        var itsOwnFile = Path.GetFileName(who) is "go.mod" or "go.work";
        var asking = itsOwnFile ? $"Its {Path.GetFileName(who)} says go {needed}" : $"{who}, which it uses, needs Go {needed} or later";

        // A Go chosen in Settings is why it was built with that one, so the choice is what to change.
        if (LanguageStandards.Current.GoRelease is { } chosen)
        {
            var newerHere = GoToolchains.Installed.FirstOrDefault(toolchain => toolchain.Version >= needed);
            return $"{asking}, and it was built with Go {running}, as Go {chosen} is chosen in Settings, which will not build it - so it was not built, " +
                   $"which is not a mistake in the code. Choosing Go {needed.Release} or later in Settings, or Detect automatically, builds it " +
                   (newerHere is not null
                       ? $"with Go {newerHere.VersionText} ({newerHere.FoundIn}), which is on this computer."
                       : $"once one is installed - {InstallAdvice}");
        }

        if (GoToolchains.Installed.FirstOrDefault(toolchain => toolchain.Version >= needed) is { } newer)
        {
            return $"{asking}, and it was built with Go {running}, which will not build it - so it was not built, which is not a mistake in the code. " +
                   (itsOwnFile
                       ? $"Go {newer.VersionText} ({newer.FoundIn}) is on this computer and builds it."
                       : $"Go {newer.VersionText} ({newer.FoundIn}) is on this computer: raising the go line in its go.mod to go {needed} builds it with that Go.");
        }

        var newest = GoToolchains.Installed.FirstOrDefault();
        var whatIsHere = newest is null ? $"Go {running}" : $"Go {newest.VersionText} ({newest.FoundIn})";

        return $"{asking}, and the newest Go on this computer is {whatIsHere}, which will not build it - so it was not built, which is not " +
               $"a mistake in the code. Installing Go {needed} or later builds it - {InstallAdvice}";
    }

    /// <summary>Code that uses what a later Go than its go line made part of the language: the go line decides which Go's language the module is.</summary>
    private static string GoLineNote(string where, string needed, LanguageVersion setTo, string chosen)
    {
        var start = $"{where} uses what Go {needed} made part of the language";

        return DeclaredGo.Of(ProgramCopy.OriginalOf(chosen)) switch
        {
            { GoLine: { } goLine } when goLine.Release == setTo.Release =>
                $"{start}, and its go.mod says go {goLine}, so Go builds the module as Go {setTo.Release} code, without it - which is not a mistake " +
                $"in the code. Raising the go line in its go.mod to go {needed} or later builds it.",

            { GoLine: null } when setTo.Release == new LanguageVersion(1, 16) =>
                $"{start}, and its go.mod has no go line, which Go takes as go 1.16, so Go builds the module as Go 1.16 code, without it - which is " +
                $"not a mistake in the code. Adding the line go {needed} to its go.mod builds it.",

            // Something other than the go line set the Go it was built as - a //go:build line can - so only what Go said is said.
            _ => $"{start}, and Go built it as Go {setTo.Release} code, without it - which is not a mistake in the code.",
        };
    }
}
