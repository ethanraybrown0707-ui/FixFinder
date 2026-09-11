using System.Text;

namespace FixFinder.Core.Patching;

/// <summary>Why a patch, a file or a hunk was accepted or refused.</summary>
public enum ApplyOutcome
{
    /// <summary>Every hunk found exactly one place to go.</summary>
    Planned,

    Applied,

    /// <summary>Planned and reported, but nothing was written because dry run was on.</summary>
    DryRun,

    /// <summary>A path resolved outside the source root. Never applied.</summary>
    RejectedOutsideRoot,

    /// <summary>The lines the hunk expects are not in the file.</summary>
    RejectedContextMismatch,

    /// <summary>The hunk fits in more than one place, so its position is not knowable.</summary>
    RejectedAmbiguousLocation,

    /// <summary>The patch names a file that does not exist here, or names it ambiguously.</summary>
    RejectedPathNotFound,

    /// <summary>Read-only, too large, or not a text file.</summary>
    RejectedUnsafeFile,

    /// <summary>Two hunks would change the same lines.</summary>
    RejectedOverlappingHunks,

    /// <summary>No file the patch touches appeared in the crash.</summary>
    RejectedUnrelatedToCrash,

    /// <summary>Something failed while writing. The backup is the way back.</summary>
    Failed,
}

/// <param name="Hunk">The hunk itself.</param>
/// <param name="AtLine">0-based index in the original file where its old side was found.</param>
public sealed record HunkPlan(DiffHunk Hunk, int AtLine, string Explanation)
{
    public int EndLine => AtLine + Hunk.OldSide.Count;
}

/// <summary>What would happen to one file.</summary>
public sealed record FilePlan(
    FilePatch Patch,
    MappedPath Path,
    IReadOnlyList<HunkPlan> Hunks,
    ApplyOutcome Outcome,
    string Explanation)
{
    public bool Ok => Outcome is ApplyOutcome.Planned;

    public string Display => $"{Path.Display}\n    {Explanation}";
}

/// <summary>The whole plan for one patch: every file, every hunk, decided in advance.</summary>
public sealed record ApplyPlan(
    IReadOnlyList<FilePlan> Files,
    ApplyOutcome Outcome,
    string Explanation)
{
    /// <summary>True only when every file and every hunk planned cleanly.</summary>
    public bool CanApply => Outcome == ApplyOutcome.Planned && Files.Count > 0 && Files.All(f => f.Ok);

    public IReadOnlyList<string> TargetPaths =>
        [.. Files.Where(f => f.Path.FullPath is not null).Select(f => f.Path.FullPath!)];

    public static ApplyPlan Refused(ApplyOutcome outcome, string explanation, IReadOnlyList<FilePlan>? files = null) =>
        new(files ?? [], outcome, explanation);
}

/// <summary>What actually happened on disk.</summary>
public sealed record ApplyResult(
    ApplyPlan Plan,
    bool WasDryRun,
    string? BackupFolder,
    IReadOnlyList<string> Written,
    string? Failure = null)
{
    public bool Ok => Failure is null;

    public string Summary
    {
        get
        {
            if (Failure is not null) return $"Nothing was applied: {Failure}";
            if (WasDryRun) return $"Dry run only. {Plan.Files.Count} file(s) would change; nothing was written.";

            return $"Applied to {Written.Count} file(s). Backup: {BackupFolder}";
        }
    }
}

/// <summary>
/// Applies a parsed patch to the working tree, or explains exactly why it will not.
/// </summary>
/// <remarks>
/// <b>Exact context, zero fuzz.</b> Each hunk's old side - its context and removed lines, in
/// order - must be found byte for byte in the file. There is no whitespace-insensitive mode, no
/// partial-context fallback and no equivalent of <c>patch --fuzz</c>. Fuzz is how a patch tool
/// silently corrupts a source file, and unlike a human running <c>patch</c> by hand there is
/// nothing downstream here that would notice: no model reads the result, and the next thing that
/// happens is a build.
/// <para>
/// <b>Atomic at the patch level.</b> Every hunk in every file is located before anything is
/// written. A half-applied cross-file patch is strictly worse than no patch at all - it compiles
/// about as often, and it leaves a tree that matches neither the original nor the fix.
/// </para>
/// </remarks>
public sealed class PatchApplier
{
    /// <summary>How far from the stated line a hunk may be found.</summary>
    /// <remarks>
    /// A patch written against another checkout has drifted by however much that tree differs
    /// from this one. Two hundred lines is generous enough for real drift and tight enough that
    /// a coincidental match somewhere else in a long file is not searched for at all.
    /// </remarks>
    private const int SearchRadius = 200;

    /// <summary>Largest file worth rewriting. Above this it is data, not source.</summary>
    private const int MaximumFileBytes = 2 * 1024 * 1024;

    /// <summary>How much of a file to check for NUL bytes before calling it text.</summary>
    private const int BinarySniffBytes = 8 * 1024;

    public event Action<string>? Log;

    // ================================================================== planning

    /// <summary>
    /// Works out where every hunk would go, without touching anything.
    /// </summary>
    /// <param name="stackTraceFiles">
    /// Files named in the crash. A patch touching none of them is refused outright.
    /// </param>
    /// <remarks>
    /// That last rule is not about correctness, it is about relevance. A diff harvested from a
    /// stranger's repository may apply perfectly cleanly to files that have nothing to do with
    /// your crash - context matching cannot tell the difference - and writing it would be a
    /// confident, well-formed change nobody asked for.
    /// </remarks>
    public ApplyPlan Plan(
        ParsedPatch patch, SourcePathMapper mapper, IEnumerable<string>? stackTraceFiles = null)
    {
        if (!patch.Ok)
            return ApplyPlan.Refused(ApplyOutcome.Failed, patch.Rejection ?? "the patch could not be parsed");

        var plans = new List<FilePlan>();

        foreach (var file in patch.Files)
        {
            plans.Add(PlanFile(file, mapper));
        }

        var firstBad = plans.FirstOrDefault(p => !p.Ok);

        if (firstBad is not null)
        {
            return ApplyPlan.Refused(firstBad.Outcome,
                $"{firstBad.Path.PatchPath}: {firstBad.Explanation}", plans);
        }

        var crashFiles = (stackTraceFiles ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // No crash files means relevance cannot be established, which is a reason to refuse
        // rather than a reason to skip the check. Treating "nothing to compare against" as
        // "nothing to worry about" is how a bug-bounty issue scoring 30/100 came to offer to
        // write a file into somebody's source folder: the error had no file references, so the
        // one rule standing between an unrelated patch and the disk simply did not run.
        if (crashFiles.Count == 0)
        {
            return ApplyPlan.Refused(ApplyOutcome.RejectedUnrelatedToCrash,
                "this error names no source file, so there is no way to tell whether the patch " +
                "has anything to do with it. It is shown for you to read, but FixFinder will not " +
                "apply a change it cannot connect to the problem.",
                plans);
        }

        var touched = plans
            .Select(p => Path.GetFileName(p.Path.FullPath ?? p.Path.PatchPath))
            .Where(name => crashFiles.Contains(name))
            .ToList();

        if (touched.Count == 0)
        {
            return ApplyPlan.Refused(ApplyOutcome.RejectedUnrelatedToCrash,
                "none of the files this patch changes appeared in the crash. It may be a " +
                "perfectly good patch for a different problem, but FixFinder will not write " +
                "a change that has no connection to the error you are looking at.",
                plans);
        }

        // A patch that only creates files fixes nothing: there is no context to match, so it
        // "applies cleanly" by definition, and the thing it adds was not what crashed.
        if (plans.All(p => p.Path.Outcome == MapOutcome.WouldCreate))
        {
            return ApplyPlan.Refused(ApplyOutcome.RejectedUnrelatedToCrash,
                "this patch only adds new files and changes none of the existing ones. Creating " +
                "a file always 'applies', because there is nothing for it to conflict with - " +
                "which makes it the one kind of patch that proves nothing by fitting.",
                plans);
        }

        var hunks = plans.Sum(p => p.Hunks.Count);

        return new ApplyPlan(plans, ApplyOutcome.Planned,
            $"{plans.Count} file(s), {hunks} hunk(s), every one located exactly.");
    }

    private FilePlan PlanFile(FilePatch patch, SourcePathMapper mapper)
    {
        var mapped = mapper.Map(patch);

        if (mapped.Outcome == MapOutcome.OutsideRoot)
            return new FilePlan(patch, mapped, [], ApplyOutcome.RejectedOutsideRoot, mapped.Explanation);

        if (mapped.Outcome is MapOutcome.NotFound or MapOutcome.Ambiguous)
            return new FilePlan(patch, mapped, [], ApplyOutcome.RejectedPathNotFound, mapped.Explanation);

        var full = mapped.FullPath!;

        if (patch.IsDeletion)
        {
            // Deleting source is not something a harvested patch gets to do unattended. The
            // change is shown; carrying it out is left to the user.
            return new FilePlan(patch, mapped, [], ApplyOutcome.RejectedUnsafeFile,
                "this patch deletes the file, which FixFinder will not do on your behalf");
        }

        if (mapped.Outcome == MapOutcome.WouldCreate)
        {
            return new FilePlan(patch, mapped, [.. patch.Hunks.Select(h => new HunkPlan(h, 0, "new file"))],
                ApplyOutcome.Planned, $"creates a new file with {patch.Hunks.Sum(h => h.AddedCount)} line(s)");
        }

        if (Unsafe(full, out var why))
            return new FilePlan(patch, mapped, [], ApplyOutcome.RejectedUnsafeFile, why);

        SourceFile source;
        try
        {
            source = SourceFile.Read(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FilePlan(patch, mapped, [], ApplyOutcome.RejectedUnsafeFile, $"could not be read: {ex.Message}");
        }

        var located = new List<HunkPlan>();

        foreach (var hunk in patch.Hunks)
        {
            var found = Locate(hunk, source.Lines, out var outcome, out var explanation);

            if (found < 0)
                return new FilePlan(patch, mapped, located, outcome, explanation);

            located.Add(new HunkPlan(hunk, found, explanation));
        }

        // Two hunks that overlap would each be planned against the original file and then fight
        // over the same lines when written.
        var ordered = located.OrderBy(h => h.AtLine).ToList();

        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].AtLine < ordered[i - 1].EndLine)
            {
                return new FilePlan(patch, mapped, located, ApplyOutcome.RejectedOverlappingHunks,
                    $"two hunks both want to change lines around {ordered[i].AtLine + 1}");
            }
        }

        return new FilePlan(patch, mapped, ordered, ApplyOutcome.Planned,
            $"+{patch.AddedCount} -{patch.RemovedCount} across {patch.Hunks.Count} hunk(s)");
    }

    /// <summary>
    /// Finds the one place a hunk's old side occurs, or reports why it cannot.
    /// </summary>
    /// <remarks>
    /// The whole window is searched rather than stopping at the first hit, because stopping
    /// early cannot tell "this is where it goes" from "this is the first of several places it
    /// would fit". The second case has no safe answer, so it is refused.
    /// </remarks>
    private static int Locate(DiffHunk hunk, IReadOnlyList<string> lines, out ApplyOutcome outcome, out string explanation)
    {
        var expected = Math.Clamp(hunk.OldStart - 1, 0, Math.Max(0, lines.Count));
        var wanted = hunk.OldSide;

        if (wanted.Count == 0)
        {
            // A pure insertion has nothing to match on, so the stated line is all there is.
            outcome = ApplyOutcome.Planned;
            explanation = $"inserted at line {expected + 1} (the hunk removes nothing, so there is no context to match)";
            return expected;
        }

        var matches = new List<int>();
        var from = Math.Max(0, expected - SearchRadius);
        var to = Math.Min(lines.Count - wanted.Count, expected + SearchRadius);

        for (var offset = from; offset <= to; offset++)
        {
            var ok = true;

            for (var k = 0; k < wanted.Count; k++)
            {
                if (!string.Equals(lines[offset + k], wanted[k].Text, StringComparison.Ordinal))
                {
                    ok = false;
                    break;
                }
            }

            if (ok) matches.Add(offset);
            if (matches.Count > 1) break;
        }

        if (matches.Count == 0)
        {
            outcome = ApplyOutcome.RejectedContextMismatch;

            explanation =
                $"the {wanted.Count} line(s) this hunk expects around line {hunk.OldStart} are not in the file. " +
                $"First line looked for: \"{Trim(wanted[0].Text)}\"";

            return -1;
        }

        if (matches.Count > 1)
        {
            outcome = ApplyOutcome.RejectedAmbiguousLocation;

            explanation =
                $"this hunk fits at line {matches[0] + 1} and again at line {matches[1] + 1}. " +
                "With two equally good positions there is no way to know which was meant.";

            return -1;
        }

        outcome = ApplyOutcome.Planned;

        var drift = matches[0] - expected;

        explanation = drift == 0
            ? $"matched exactly at line {matches[0] + 1}"
            : $"matched at line {matches[0] + 1}, {Math.Abs(drift)} line(s) {(drift > 0 ? "below" : "above")} where the patch said";

        return matches[0];
    }

    private static bool Unsafe(string path, out string why)
    {
        var info = new FileInfo(path);

        if (info.IsReadOnly)
        {
            why = "the file is read-only";
            return true;
        }

        if (info.Length > MaximumFileBytes)
        {
            why = $"the file is {info.Length / 1024} KB, which is past anything FixFinder will rewrite";
            return true;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[Math.Min(BinarySniffBytes, (int)info.Length)];
            var read = stream.Read(buffer, 0, buffer.Length);

            if (Array.IndexOf(buffer, (byte)0, 0, read) >= 0)
            {
                why = "the file contains NUL bytes, so it is not text";
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            why = $"the file could not be read: {ex.Message}";
            return true;
        }

        why = "";
        return false;
    }

    // ================================================================== applying

    /// <summary>
    /// Carries out a plan, or reports it without writing when <paramref name="dryRun"/> is set.
    /// </summary>
    /// <remarks>
    /// Dry run is the default everywhere it is offered. The first time anyone applies anything
    /// it should be a preview, and making that the default rather than an option is the
    /// difference between a safety feature and a safety feature people forget to switch on.
    /// </remarks>
    public ApplyResult Apply(
        ApplyPlan plan,
        BackupStore backups,
        string sourceRoot,
        bool dryRun = true,
        string? candidateId = null,
        string? candidateTitle = null,
        string? candidateUrl = null)
    {
        if (!plan.CanApply)
            return new ApplyResult(plan, dryRun, null, [], plan.Explanation);

        if (dryRun)
        {
            Log?.Invoke($"Dry run: {plan.Files.Count} file(s) would be written, nothing was.");
            return new ApplyResult(plan with { Outcome = ApplyOutcome.DryRun }, true, null, []);
        }

        // Taken before a single byte is written, and it covers files the patch would create as
        // well as ones it changes - otherwise undoing the patch would leave the new files behind.
        var backupFolder = backups.Create(
            sourceRoot, plan.TargetPaths, candidateId, candidateTitle, candidateUrl);

        Log?.Invoke($"Backed up {plan.TargetPaths.Count} file(s) to {backupFolder}");

        var written = new List<string>();

        foreach (var file in plan.Files)
        {
            var path = file.Path.FullPath!;

            try
            {
                var content = file.Path.Outcome == MapOutcome.WouldCreate
                    ? SourceFile.ForNewFile(file.Patch)
                    : SourceFile.Read(path).WithHunks(file.Hunks);

                content.WriteTo(path);
                written.Add(path);

                Log?.Invoke($"Wrote {path}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Roll straight back. A tree that is half-patched is worse than an unpatched one,
                // and the backup exists for exactly this moment.
                Log?.Invoke($"Failed writing {path}: {ex.Message} - rolling back.");

                var restore = backups.Restore(backupFolder);
                Log?.Invoke($"Rollback: {restore.Summary}");

                return new ApplyResult(plan with { Outcome = ApplyOutcome.Failed }, false, backupFolder, written,
                    $"{Path.GetFileName(path)} could not be written ({ex.Message}). " +
                    $"Everything was rolled back: {restore.Summary}");
            }
        }

        return new ApplyResult(plan with { Outcome = ApplyOutcome.Applied }, false, backupFolder, written);
    }

    private static string Trim(string text) => text.Length <= 60 ? text : text[..59] + "…";
}

/// <summary>
/// A source file read for patching, and rewritten the way it was found.
/// </summary>
/// <remarks>
/// Line endings and the byte-order mark are properties of <i>the file</i>, never of the patch.
/// A diff is normalised to newlines when it is parsed, so writing a file back the way the diff
/// happened to be formatted would rewrite every line of a CRLF file - producing a diff of the
/// whole file for a one-line change, and a merge conflict for whoever touches it next.
/// </remarks>
internal sealed record SourceFile(
    IReadOnlyList<string> Lines, bool UsesCrLf, bool HasBom, bool EndsWithNewline)
{
    public static SourceFile Read(string path)
    {
        var bytes = File.ReadAllBytes(path);

        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = new UTF8Encoding(false).GetString(hasBom ? bytes[3..] : bytes);

        var crlf = CountOccurrences(text, "\r\n");
        var lf = text.Count(c => c == '\n');

        // The dominant ending wins, so a file with a couple of stray endings is not rewritten
        // wholesale on the strength of them.
        var usesCrLf = crlf > 0 && crlf * 2 >= lf;

        var normalised = text.Replace("\r\n", "\n");
        var endsWithNewline = normalised.EndsWith('\n');

        if (endsWithNewline) normalised = normalised[..^1];

        return new SourceFile(normalised.Split('\n'), usesCrLf, hasBom, endsWithNewline);
    }

    /// <summary>Builds the content of a file the patch creates.</summary>
    public static SourceFile ForNewFile(FilePatch patch)
    {
        var lines = patch.Hunks
            .SelectMany(h => h.Lines)
            .Where(l => l.Kind is DiffLineKind.Added or DiffLineKind.Context)
            .ToList();

        var endsWithNewline = lines.Count == 0 || !lines[^1].NoNewlineAtEnd;

        return new SourceFile([.. lines.Select(l => l.Text)], UsesCrLf: false, HasBom: false, endsWithNewline);
    }

    /// <summary>Produces the patched content, splicing each hunk into the original.</summary>
    public SourceFile WithHunks(IReadOnlyList<HunkPlan> hunks)
    {
        var result = new List<string>();
        var cursor = 0;
        var endsWithNewline = EndsWithNewline;

        foreach (var plan in hunks.OrderBy(h => h.AtLine))
        {
            for (; cursor < plan.AtLine; cursor++) result.Add(Lines[cursor]);

            foreach (var line in plan.Hunk.NewSide)
            {
                result.Add(line.Text);

                // A no-newline marker on the last line of the last hunk decides how the file ends.
                if (line.NoNewlineAtEnd && plan.EndLine >= Lines.Count) endsWithNewline = false;
            }

            cursor = plan.EndLine;
        }

        for (; cursor < Lines.Count; cursor++) result.Add(Lines[cursor]);

        return this with { Lines = result, EndsWithNewline = endsWithNewline };
    }

    /// <summary>
    /// Writes through a temporary file in the same folder, then swaps it in.
    /// </summary>
    /// <remarks>
    /// Writing in place means a crash or a full disk halfway through leaves a truncated source
    /// file and no way back. The swap is the last step and is as close to atomic as the
    /// filesystem allows.
    /// </remarks>
    public void WriteTo(string path)
    {
        var ending = UsesCrLf ? "\r\n" : "\n";
        var text = string.Join(ending, Lines) + (EndsWithNewline ? ending : "");

        var bytes = new UTF8Encoding(false).GetBytes(text);

        if (HasBom) bytes = [0xEF, 0xBB, 0xBF, .. bytes];

        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(directory, $".fixfinder-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(temp, bytes);

        try
        {
            if (File.Exists(path)) File.Replace(temp, path, destinationBackupFileName: null);
            else File.Move(temp, path);
        }
        catch (IOException)
        {
            // File.Replace is unavailable across some filesystems and on some network shares.
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) { try { File.Delete(temp); } catch (IOException) { } }
        }
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = text.IndexOf(needle, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
