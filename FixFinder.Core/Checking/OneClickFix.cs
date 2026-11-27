using FixFinder.Core.Engine;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Reporting;

namespace FixFinder.Core.Checking;

/// <summary>
/// The one change FixFinder makes to a person's own code, and only when they press for it: a fix FixFinder is certain
/// solves the mistake - a copy of the program with it printed what the person said the program should print - and small,
/// at most <see cref="MostLines"/> lines taken out and as many put in. Before writing, it makes sure the file is as it was
/// when it was checked, and keeps a copy of it as it was.
/// </summary>
/// <remarks>
/// The file is written by the same SourceFile.Render that wrote the copy the fix was shown to work in, so what is written
/// is what was run, with the file's own line endings and byte order mark.
/// </remarks>
public static class OneClickFix
{
    /// <summary>The most lines a fix applied with one click may take out, and the most it may put in.</summary>
    public const int MostLines = 3;

    /// <summary>A fix written into its file: where, and where the file as it was is kept.</summary>
    public sealed record Applied(string File, int Line, string KeptAt);

    /// <summary>
    /// Why the finding's fix cannot be applied with one click, in words for the person; null when it can. FixFinder is
    /// certain of a fix - as the finding's Fix confidence says it - only when a copy of the program with it printed what the
    /// person said the program should print: running without the error is likely, as the program may still go wrong after.
    /// </summary>
    public static string? WhyNot(Finding finding)
    {
        if (finding.Fix is not { } fix) return "It has no change to make.";
        if (finding.InNotebook is not null) return "It is in a notebook, whose cells FixFinder does not write.";
        if (fix.OnlyStopsTheFailure) return "Its change only stops the failure, and leaves what made it as it is.";
        if (FindingLayout.FixConfidence(finding) is not { Level: Confidence.Certain })
            return "FixFinder is not certain its fix solves it: it is, only when a copy of the program with the fix prints what you said the program should print.";
        if (fix.RemoveCount > MostLines || fix.NewLines.Count > MostLines) return $"Its change is more than {MostLines} lines.";
        if (!File.Exists(fix.File)) return "Its file is not there to change.";
        return null;
    }

    /// <summary>Where the window keeps each file as it was before a fix was written into it: FixFinder's data folder, under backups.</summary>
    public static string KeptIn => Path.Combine(DataFolder.Root, "backups", "fixes");

    /// <summary>
    /// Writes the finding's fix into its file, when it can be applied with one click and the file has not been written to
    /// since <paramref name="checkedAt"/>, when its check began; the file as it was is kept first, in
    /// <paramref name="keepIn"/>. Null, with why, when it is not written.
    /// </summary>
    public static (Applied? Done, string? WhyNot) Apply(Finding finding, DateTime checkedAt, string keepIn)
    {
        if (WhyNot(finding) is { } refused) return (null, refused);
        var fix = finding.Fix!;

        if (File.GetLastWriteTimeUtc(fix.File) > checkedAt.ToUniversalTime())
            return (null, "The file has been changed since it was checked, so the lines this fix was worked out from may have moved. Check it again first.");

        if (SourceFile.Read(fix.File) is not { } source || fix.ApplyTo(source) is not { } changed)
            return (null, "The file could not be read as it was when it was checked.");

        string? keptAt = null;
        try
        {
            Directory.CreateDirectory(keepIn);

            // A second fix to the same file in the same second is kept beside the first, never over it.
            var stamp = $"{DateTime.Now:yyyy-MM-dd-HHmmss}-{Path.GetFileName(fix.File)}";
            var keepAt = Path.Combine(keepIn, stamp);
            for (var again = 2; File.Exists(keepAt); again++) keepAt = Path.Combine(keepIn, $"{again}-{stamp}");

            File.Copy(fix.File, keepAt);
            keptAt = keepAt;
            File.WriteAllBytes(fix.File, source.Render(changed));
            return (new Applied(fix.File, fix.StartLine, keptAt), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Once the file as it was is kept, a write that stopped part way has it to go back to, so where it is is said.
            return (null, keptAt is null
                ? $"The file could not be written: {ex.Message}"
                : $"The file could not be written: {ex.Message} The file as it was is kept at {keptAt}.");
        }
    }
}
