using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Core.Checking;

/// <summary>A mistake the code-reading checks find, as a file is compared before and after a fix: the check that finds it, and its line.</summary>
public readonly record struct CodeFindingPlace(string RuleId, int Line);

/// <summary>What the code-reading checks found in one file read on its own.</summary>
/// <param name="Found">Each mistake found, by its check and its line.</param>
/// <param name="AnalysesRead">
/// Whether the file's language could be read for the analyses that follow every value through it; when it could not, only
/// its logic patterns were checked, and nothing follows about an analysis's findings.
/// </param>
/// <param name="PartsNotRead">How many parts of the file its language's parser could not read, and so were not checked.</param>
public sealed record CodeReading(IReadOnlySet<CodeFindingPlace> Found, bool AnalysesRead, int PartsNotRead = 0);

/// <summary>
/// What making a finding's fix does to the other findings in its file. The file is read again by the same code-reading
/// checks - its logic patterns and the analyses that follow every value through it - from a copy with the fix made, and
/// from a copy as it is; each mistake they find in the copy as it is, and no longer find in the copy with the fix, is one
/// the fix removes too.
/// </summary>
/// <remarks>
/// Both copies are read on their own and the same way, so the fix is the only difference between them: a finding the
/// check of the whole program made only through another of its files is found in neither copy, and is never said to be
/// removed. A finding on a line the fix changes is left out, since that line is no longer there to be checked as it
/// was. Nothing is run: the copies are only read, in a folder of their own under the temporary directory, deleted
/// afterwards, and the program's own files are never written to.
/// </remarks>
public static class FixEffects
{
    private static string Root => Path.Combine(Path.GetTempPath(), "FixFinder-effects");

    /// <summary>
    /// Reads <paramref name="lines"/> - the file as it is, or with a fix made - from a copy of <paramref name="source"/>
    /// under its own name, with its logic patterns and, when <paramref name="readProgram"/> can read its language, its
    /// analyses. Null when the copy could not be made.
    /// </summary>
    /// <param name="readProgram">Reads files with their own language's parser; null when that language cannot be read.</param>
    public static async Task<CodeReading?> ReadAsync(
        SourceFile source, IReadOnlyList<string> lines, Func<IReadOnlyList<string>, Task<IrProgram>?> readProgram, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Root, Guid.NewGuid().ToString("N")[..12]);

        try
        {
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, Path.GetFileName(source.Path));
            await File.WriteAllBytesAsync(copy, source.Render(lines), cancellationToken);
            if (SourceFile.Read(copy) is not { } copied) return null;

            var found = new HashSet<CodeFindingPlace>(LogicPatterns.Scan(copied).Select(pattern => new CodeFindingPlace(pattern.PatternId, pattern.Line)));
            var analysesRead = false;
            var partsNotRead = 0;

            if (readProgram([copy]) is { } reading && await reading is { NotRead: null } program)
            {
                analysesRead = true;
                partsNotRead = program.Problems.Count;
                foreach (var analysed in AbstractChecks.Run(program, new SourceText()))
                {
                    if (SamePath(analysed.Span.File, copy)) found.Add(new CodeFindingPlace(analysed.CheckId, analysed.Span.Line));
                }
            }

            return new CodeReading(found, analysesRead, partsNotRead);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// The mistakes found in the file as it is that are no longer found with <paramref name="fix"/> made, each on its line
    /// as the file has it now, in the order of their lines. One on a line the fix changes is never among them, and an
    /// analysis's finding is only when both readings could follow the values. A fix after which the file reads less well
    /// than it did - its parser could no longer read it, or read less of it - removes nothing: the checks could not see
    /// what they saw before, which says nothing about the mistakes there.
    /// </summary>
    public static IReadOnlyList<CodeFindingPlace> RemovedBy(LocalFix fix, SourceFile source, CodeReading asItIs, CodeReading withTheFix)
    {
        if (asItIs.AnalysesRead && !withTheFix.AnalysesRead || withTheFix.PartsNotRead > asItIs.PartsNotRead) return [];

        var (firstChanged, lastChanged, shift) = ChangedLines(fix, source);
        var analysesCompared = asItIs.AnalysesRead && withTheFix.AnalysesRead;
        var removed = new List<CodeFindingPlace>();

        foreach (var place in asItIs.Found)
        {
            if (place.RuleId.StartsWith("analysis-", StringComparison.Ordinal) && !analysesCompared) continue;
            if (place.Line >= firstChanged && place.Line <= lastChanged) continue;

            var lineWithTheFix = place.Line < firstChanged ? place.Line : place.Line + shift;
            if (!withTheFix.Found.Contains(place with { Line = lineWithTheFix })) removed.Add(place);
        }

        return [.. removed.OrderBy(place => place.Line).ThenBy(place => place.RuleId, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The lines of the file a fix really changes - its own lines, less those it keeps as they were at either end - and how
    /// many lines further down the lines after them are with the fix made. A fix that only adds lines changes none of the
    /// file's lines: the first changed is then one past the last.
    /// </summary>
    public static (int FirstChanged, int LastChanged, int Shift) ChangedLines(LocalFix fix, SourceFile source)
    {
        var replaced = source.Lines.Skip(fix.StartLine - 1).Take(fix.RemoveCount).ToList();
        var written = fix.NewLines;

        var keptAtTheStart = 0;
        while (keptAtTheStart < replaced.Count && keptAtTheStart < written.Count && replaced[keptAtTheStart] == written[keptAtTheStart]) keptAtTheStart++;

        var keptAtTheEnd = 0;
        while (keptAtTheEnd < replaced.Count - keptAtTheStart && keptAtTheEnd < written.Count - keptAtTheStart &&
               replaced[replaced.Count - 1 - keptAtTheEnd] == written[written.Count - 1 - keptAtTheEnd]) keptAtTheEnd++;

        var firstChanged = fix.StartLine + keptAtTheStart;
        var lastChanged = fix.StartLine + replaced.Count - keptAtTheEnd - 1;
        return (firstChanged, lastChanged, written.Count - replaced.Count);
    }

    private static bool SamePath(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
}
