using System.Security.Cryptography;
using System.Text.Json;
using FixFinder.Core.Engine;

namespace FixFinder.Core.Patching;

/// <summary>One file as it was before a patch touched it.</summary>
/// <param name="RelativePath">Path relative to the source root, as stored in the backup folder.</param>
/// <param name="Sha256">Hash of the original bytes, re-checked on restore.</param>
/// <param name="Bytes">Size of the original, for the manifest to be readable.</param>
/// <param name="Existed">False when the patch created the file, so restoring means deleting it.</param>
public sealed record BackedUpFile(string RelativePath, string Sha256, long Bytes, bool Existed);

/// <summary>What a backup folder contains, written beside the files as readable JSON.</summary>
public sealed record BackupManifest(
    string CreatedUtc,
    string SourceRoot,
    string? CandidateId,
    string? CandidateTitle,
    string? CandidateUrl,
    IReadOnlyList<BackedUpFile> Files);

/// <summary>
/// Copies files aside before they are patched, and puts them back byte for byte.
/// </summary>
/// <remarks>
/// The thing that makes applying a patch a reversible decision rather than a permanent one.
/// Everything is verified on the way back: each file's SHA-256 is recorded when it is copied and
/// re-checked when it is restored, so a backup that was edited, truncated or partially written
/// is refused rather than silently used to overwrite working code.
/// <para>
/// <b>Backups are never deleted automatically.</b> They are small, they are the only record of
/// what a file looked like before an automated tool changed it, and a cleanup policy is exactly
/// the feature that would remove the one you needed. The window shows the folder and the run log
/// names it.
/// </para>
/// </remarks>
public sealed class BackupStore
{
    private const string ManifestName = "manifest.json";

    /// <summary>Where backup folders are created.</summary>
    public string Root { get; }

    public BackupStore(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FixFinder", "backups");

        Directory.CreateDirectory(Root);
    }

    /// <summary>
    /// Copies the given files into a new timestamped folder and writes its manifest.
    /// </summary>
    /// <param name="sourceRoot">The root the paths are relative to.</param>
    /// <param name="fullPaths">Absolute paths about to be modified or created.</param>
    /// <returns>The backup folder, which is also the handle for restoring.</returns>
    public string Create(
        string sourceRoot,
        IEnumerable<string> fullPaths,
        string? candidateId = null,
        string? candidateTitle = null,
        string? candidateUrl = null)
    {
        var root = Path.GetFullPath(sourceRoot);

        var folder = Path.Combine(Root, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
        Directory.CreateDirectory(folder);

        var files = new List<BackedUpFile>();

        foreach (var path in fullPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(root, path);
            var exists = File.Exists(path);

            if (!exists)
            {
                // A file the patch creates. Recorded so that undoing the patch removes it, which
                // a copy-back alone would not do.
                files.Add(new BackedUpFile(relative, "", 0, Existed: false));
                continue;
            }

            var destination = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(path, destination, overwrite: true);

            var bytes = File.ReadAllBytes(path);
            files.Add(new BackedUpFile(relative, Hash(bytes), bytes.LongLength, Existed: true));
        }

        var manifest = new BackupManifest(
            DateTime.UtcNow.ToString("O"), root, candidateId, candidateTitle, candidateUrl, files);

        File.WriteAllText(
            Path.Combine(folder, ManifestName),
            JsonSerializer.Serialize(manifest, JsonOptions.Default));

        return folder;
    }

    public BackupManifest? ReadManifest(string backupFolder)
    {
        var path = Path.Combine(backupFolder, ManifestName);
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(path), JsonOptions.Default);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>What a restore did, or why it refused to.</summary>
    /// <param name="Restored">Files put back.</param>
    /// <param name="Deleted">Files removed because the patch had created them.</param>
    /// <param name="Failures">Anything that could not be restored, with the reason.</param>
    public sealed record RestoreResult(
        IReadOnlyList<string> Restored, IReadOnlyList<string> Deleted, IReadOnlyList<string> Failures)
    {
        public bool Ok => Failures.Count == 0;

        public string Summary => Ok
            ? $"Restored {Restored.Count} file(s)" + (Deleted.Count > 0 ? $" and removed {Deleted.Count} the patch had created" : "")
            : $"Restored {Restored.Count} file(s), but {Failures.Count} could not be put back: {string.Join("; ", Failures)}";
    }

    /// <summary>
    /// Puts every file in a backup folder back where it came from.
    /// </summary>
    /// <remarks>
    /// Every copy is re-hashed before it is written back. A restore is the operation you reach
    /// for when something has already gone wrong, which is the worst possible moment to discover
    /// that the safety net was itself corrupt.
    /// </remarks>
    public RestoreResult Restore(string backupFolder)
    {
        var manifest = ReadManifest(backupFolder);

        if (manifest is null)
            return new RestoreResult([], [], ["the backup has no readable manifest, so nothing was restored"]);

        var restored = new List<string>();
        var deleted = new List<string>();
        var failures = new List<string>();

        foreach (var file in manifest.Files)
        {
            var target = Path.Combine(manifest.SourceRoot, file.RelativePath);

            if (!file.Existed)
            {
                try
                {
                    if (File.Exists(target)) { File.Delete(target); deleted.Add(file.RelativePath); }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failures.Add($"{file.RelativePath} ({ex.Message})");
                }

                continue;
            }

            var copy = Path.Combine(backupFolder, file.RelativePath);

            if (!File.Exists(copy))
            {
                failures.Add($"{file.RelativePath} (its backup copy is missing)");
                continue;
            }

            try
            {
                var bytes = File.ReadAllBytes(copy);
                var actual = Hash(bytes);

                if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"{file.RelativePath} (the backup copy does not match its recorded checksum)");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
                restored.Add(file.RelativePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{file.RelativePath} ({ex.Message})");
            }
        }

        return new RestoreResult(restored, deleted, failures);
    }

    /// <summary>Backup folders, newest first, for the window's Backups button.</summary>
    public IReadOnlyList<string> List()
    {
        try
        {
            return [.. Directory.EnumerateDirectories(Root).OrderByDescending(d => d, StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
