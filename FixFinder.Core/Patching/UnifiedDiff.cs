namespace FixFinder.Core.Patching;

/// <summary>What one line inside a hunk does.</summary>
public enum DiffLineKind
{
    /// <summary>Unchanged. Must match the file exactly for the hunk to apply.</summary>
    Context,

    Added,
    Removed,
}

/// <param name="Kind">Whether the line is context, an addition or a removal.</param>
/// <param name="Text">The line without its leading marker, and without any newline.</param>
/// <param name="NoNewlineAtEnd">
/// True when the diff marked this line with <c>\ No newline at end of file</c>.
/// </param>
/// <remarks>
/// That marker belongs to the line <b>before</b> it, not to itself, and dropping it silently
/// appends a newline the original file never had - a one-byte change that shows up in every
/// later diff of that file and in any checksum taken of it.
/// </remarks>
public sealed record DiffLine(DiffLineKind Kind, string Text, bool NoNewlineAtEnd = false);

/// <summary>One contiguous change region within a file.</summary>
/// <param name="OldStart">1-based first line in the original file. 0 when the file is new.</param>
/// <param name="OldCount">Lines of the original this hunk covers.</param>
/// <param name="NewStart">1-based first line in the patched file.</param>
/// <param name="NewCount">Lines of the result this hunk produces.</param>
/// <param name="Heading">The text after the second <c>@@</c>, usually the enclosing function.</param>
public sealed record DiffHunk(
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    IReadOnlyList<DiffLine> Lines,
    string Heading = "")
{
    /// <summary>
    /// The lines this hunk expects to find, in order: context and removals.
    /// </summary>
    /// <remarks>
    /// This sequence is what the applier matches against the real file. Everything about whether
    /// a patch is safe to apply comes down to finding these exact lines, in this exact order.
    /// </remarks>
    public IReadOnlyList<DiffLine> OldSide =>
        [.. Lines.Where(l => l.Kind is DiffLineKind.Context or DiffLineKind.Removed)];

    /// <summary>The lines this hunk leaves behind: context and additions.</summary>
    public IReadOnlyList<DiffLine> NewSide =>
        [.. Lines.Where(l => l.Kind is DiffLineKind.Context or DiffLineKind.Added)];

    public int AddedCount => Lines.Count(l => l.Kind == DiffLineKind.Added);
    public int RemovedCount => Lines.Count(l => l.Kind == DiffLineKind.Removed);

    public string Header =>
        $"@@ -{OldStart},{OldCount} +{NewStart},{NewCount} @@" + (Heading.Length > 0 ? " " + Heading : "");
}

/// <summary>Every change a patch makes to one file.</summary>
/// <param name="OldPath">Path before, or null when the file is being created.</param>
/// <param name="NewPath">Path after, or null when the file is being deleted.</param>
public sealed record FilePatch(
    string? OldPath,
    string? NewPath,
    IReadOnlyList<DiffHunk> Hunks)
{
    public bool IsNewFile { get; init; }
    public bool IsDeletion { get; init; }
    public bool IsRename { get; init; }

    /// <summary>True for a patch carrying binary content, which FixFinder always refuses.</summary>
    public bool IsBinary { get; init; }

    /// <summary>The path this patch would write to.</summary>
    public string? TargetPath => NewPath ?? OldPath;

    public int AddedCount => Hunks.Sum(h => h.AddedCount);
    public int RemovedCount => Hunks.Sum(h => h.RemovedCount);

    public string Summary
    {
        get
        {
            var what =
                IsBinary ? "binary" :
                IsNewFile ? "new file" :
                IsDeletion ? "deleted" :
                IsRename ? $"renamed from {OldPath}" :
                $"+{AddedCount} -{RemovedCount} in {Hunks.Count} hunk(s)";

            return $"{TargetPath ?? "(no path)"}  ({what})";
        }
    }
}

/// <summary>A parsed unified diff, or the reason it was refused.</summary>
/// <param name="Files">One entry per file the patch touches.</param>
/// <param name="Rejection">Set when the diff was not usable. Null on success.</param>
public sealed record ParsedPatch(IReadOnlyList<FilePatch> Files, string? Rejection = null)
{
    public bool Ok => Rejection is null && Files.Count > 0;

    public static ParsedPatch Refused(string reason) => new([], reason);

    public int TotalHunks => Files.Sum(f => f.Hunks.Count);

    public string Summary => Rejection is not null
        ? $"Refused: {Rejection}"
        : $"{Files.Count} file(s), {TotalHunks} hunk(s), +{Files.Sum(f => f.AddedCount)} -{Files.Sum(f => f.RemovedCount)}";
}
