using System.Text;

namespace FixFinder.Core.Patching;

/// <summary>Why a patch, a file or a hunk was accepted or refused.</summary>
public enum ApplyOutcome
{
    Planned,

    Applied,

    DryRun,

    RejectedOutsideRoot,

    RejectedContextMismatch,

    RejectedAmbiguousLocation,

    RejectedPathNotFound,

    RejectedUnsafeFile,

    RejectedOverlappingHunks,

    RejectedUnrelatedToCrash,

    Failed,
}

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
    public bool CanApply => Outcome == ApplyOutcome.Planned && Files.Count > 0 && Files.All(f => f.Ok);

    public IReadOnlyList<string> TargetPaths =>
        [.. Files.Where(f => f.Path.FullPath is not null).Select(f => f.Path.FullPath!)];

    public static ApplyPlan Refused(ApplyOutcome outcome, string explanation, IReadOnlyList<FilePlan>? files = null) =>
        new(files ?? [], outcome, explanation);
}

/// <summary>Works out where each hunk of a patch lands in the working tree, or explains exactly why it will not fit.</summary>
public sealed class PatchPlanner
{
    private const int SearchRadius = 200;

    private const int MaximumFileBytes = 2 * 1024 * 1024;

    private const int BinarySniffBytes = 8 * 1024;


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

        string[] lines;
        try
        {
            lines = ReadLines(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FilePlan(patch, mapped, [], ApplyOutcome.RejectedUnsafeFile, $"could not be read: {ex.Message}");
        }

        var located = new List<HunkPlan>();

        foreach (var hunk in patch.Hunks)
        {
            var found = Locate(hunk, lines, out var outcome, out var explanation);

            if (found < 0)
                return new FilePlan(patch, mapped, located, outcome, explanation);

            located.Add(new HunkPlan(hunk, found, explanation));
        }

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

    private static int Locate(DiffHunk hunk, IReadOnlyList<string> lines, out ApplyOutcome outcome, out string explanation)
    {
        var expected = Math.Clamp(hunk.OldStart - 1, 0, Math.Max(0, lines.Count));
        var wanted = hunk.OldSide;

        if (wanted.Count == 0)
        {
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

    private static string Trim(string text) => text.Length <= 60 ? text : text[..59] + "…";

    private static string[] ReadLines(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = new UTF8Encoding(false).GetString(hasBom ? bytes[3..] : bytes).Replace("\r\n", "\n");

        return (text.EndsWith('\n') ? text[..^1] : text).Split('\n');
    }
}
