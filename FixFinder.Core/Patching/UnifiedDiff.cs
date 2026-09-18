namespace FixFinder.Core.Patching;

/// <summary>What one line inside a hunk does.</summary>
public enum DiffLineKind
{
    Context,

    Added,
    Removed,
}

public sealed record DiffLine(DiffLineKind Kind, string Text, bool NoNewlineAtEnd = false);

/// <summary>One contiguous change region within a file.</summary>
public sealed record DiffHunk(
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    IReadOnlyList<DiffLine> Lines,
    string Heading = "")
{
    public IReadOnlyList<DiffLine> OldSide =>
        [.. Lines.Where(l => l.Kind is DiffLineKind.Context or DiffLineKind.Removed)];

    public IReadOnlyList<DiffLine> NewSide =>
        [.. Lines.Where(l => l.Kind is DiffLineKind.Context or DiffLineKind.Added)];

    public int AddedCount => Lines.Count(l => l.Kind == DiffLineKind.Added);
    public int RemovedCount => Lines.Count(l => l.Kind == DiffLineKind.Removed);

    public string Header =>
        $"@@ -{OldStart},{OldCount} +{NewStart},{NewCount} @@" + (Heading.Length > 0 ? " " + Heading : "");
}

/// <summary>Every change a patch makes to one file.</summary>
public sealed record FilePatch(
    string? OldPath,
    string? NewPath,
    IReadOnlyList<DiffHunk> Hunks)
{
    public bool IsNewFile { get; init; }
    public bool IsDeletion { get; init; }
    public bool IsRename { get; init; }

    public bool IsBinary { get; init; }

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
public sealed record ParsedPatch(IReadOnlyList<FilePatch> Files, string? Rejection = null)
{
    public bool Ok => Rejection is null && Files.Count > 0;

    public static ParsedPatch Refused(string reason) => new([], reason);

    public int TotalHunks => Files.Sum(f => f.Hunks.Count);

    public string Summary => Rejection is not null
        ? $"Refused: {Rejection}"
        : $"{Files.Count} file(s), {TotalHunks} hunk(s), +{Files.Sum(f => f.AddedCount)} -{Files.Sum(f => f.RemovedCount)}";
}
