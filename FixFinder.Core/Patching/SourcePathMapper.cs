namespace FixFinder.Core.Patching;

/// <summary>What became of one path in a patch.</summary>
public enum MapOutcome
{
    Mapped,

    WouldCreate,

    NotFound,

    Ambiguous,

    OutsideRoot,
}

public sealed record MappedPath(
    string PatchPath, string? FullPath, MapOutcome Outcome, string Explanation)
{
    public bool Usable => Outcome is MapOutcome.Mapped or MapOutcome.WouldCreate;

    public string Display => FullPath is null
        ? $"{PatchPath}  →  {Explanation}"
        : $"{PatchPath}  →  {FullPath}  ({Explanation})";
}

/// <summary>Works out which file on this disk a path inside a patch refers to.</summary>
public sealed class SourcePathMapper
{
    private static readonly string[] SkippedFolders =
    [
        ".git", ".svn", ".hg", "node_modules", "bin", "obj", "dist", "build", "out",
        "__pycache__", ".venv", "venv", "env", ".tox", "target", "vendor", "packages",
        ".idea", ".vs", ".vscode", ".gradle", ".mypy_cache", ".pytest_cache", "site-packages",
    ];

    private const int MaximumIndexedFiles = 60_000;

    private readonly string _root;
    private readonly Dictionary<string, List<string>> _byFileName;
    private readonly HashSet<string> _fromStackTrace;

    public string Root => _root;

    public int IndexedFiles { get; }

    public SourcePathMapper(string sourceRoot, IEnumerable<string>? stackTraceFiles = null)
    {
        _root = Path.GetFullPath(sourceRoot);
        _byFileName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        _fromStackTrace = (stackTraceFiles ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => { try { return Path.GetFullPath(f); } catch (ArgumentException) { return f; } })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EnumerateSourceFiles(_root))
        {
            var name = Path.GetFileName(file);

            if (!_byFileName.TryGetValue(name, out var list))
                _byFileName[name] = list = [];

            list.Add(file);
            IndexedFiles++;

            if (IndexedFiles >= MaximumIndexedFiles) break;
        }
    }

    public IReadOnlyList<MappedPath> MapAll(ParsedPatch patch) =>
        [.. patch.Files.Select(Map)];

    public MappedPath Map(FilePatch file)
    {
        var path = file.TargetPath;

        return path is null
            ? new MappedPath("(no path)", null, MapOutcome.NotFound, "the patch names no file")
            : Map(path, file.IsNewFile);
    }

    public MappedPath Map(string patchPath, bool isNewFile = false)
    {
        var relative = patchPath.Replace('\\', '/').Trim();

        if (Path.IsPathRooted(relative))
        {
            return new MappedPath(patchPath, null, MapOutcome.OutsideRoot,
                "the patch names an absolute path, which would point outside the source root");
        }

        var direct = Combine(relative);

        if (direct is null)
        {
            return new MappedPath(patchPath, null, MapOutcome.OutsideRoot,
                "the path escapes the source root once resolved");
        }

        if (isNewFile)
        {
            return File.Exists(direct)
                ? new MappedPath(patchPath, direct, MapOutcome.Mapped,
                    "the patch creates this file, but it already exists here")
                : new MappedPath(patchPath, direct, MapOutcome.WouldCreate,
                    "a new file, which would be created at this path");
        }

        if (File.Exists(direct))
        {
            return new MappedPath(patchPath, direct, MapOutcome.Mapped,
                "the patch path matches this tree exactly");
        }

        return MatchBySuffix(patchPath, relative);
    }

    private MappedPath MatchBySuffix(string patchPath, string relative)
    {
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return new MappedPath(patchPath, null, MapOutcome.NotFound, "the patch path was empty");

        var fileName = segments[^1];

        if (!_byFileName.TryGetValue(fileName, out var sameName) || sameName.Count == 0)
        {
            return new MappedPath(patchPath, null, MapOutcome.NotFound,
                $"no file called {fileName} exists anywhere under the source root");
        }

        for (var take = segments.Length; take >= 1; take--)
        {
            var suffix = string.Join('/', segments[^take..]);

            var matches = sameName
                .Where(candidate => EndsWithSuffix(candidate, suffix))
                .ToList();

            if (matches.Count == 0) continue;

            if (matches.Count == 1)
            {
                return Contained(patchPath, matches[0],
                    take == segments.Length
                        ? "matched on the whole path"
                        : $"matched on the last {take} path segment(s): {suffix}");
            }

            var fromTrace = matches.Where(_fromStackTrace.Contains).ToList();

            if (fromTrace.Count == 1)
            {
                return Contained(patchPath, fromTrace[0],
                    $"{matches.Count} files matched {suffix}; this is the one that appeared in the stack trace");
            }

            return new MappedPath(patchPath, null, MapOutcome.Ambiguous,
                $"{matches.Count} files match {suffix} and nothing distinguishes them: " +
                string.Join(", ", matches.Take(4).Select(m => Path.GetRelativePath(_root, m))));
        }

        return new MappedPath(patchPath, null, MapOutcome.NotFound,
            $"{sameName.Count} file(s) called {fileName} exist, but none sits at a matching path");
    }

    private static bool EndsWithSuffix(string fullPath, string suffix)
    {
        var normalised = fullPath.Replace('\\', '/');

        return normalised.EndsWith("/" + suffix, StringComparison.OrdinalIgnoreCase) ||
               normalised.Equals(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private string? Combine(string relative)
    {
        try
        {
            var full = Path.GetFullPath(Path.Combine(_root, relative));
            return IsInsideRoot(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public bool IsInsideRoot(string fullPath)
    {
        var root = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                   Path.DirectorySeparatorChar;

        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private MappedPath Contained(string patchPath, string fullPath, string how) =>
        IsInsideRoot(fullPath)
            ? new MappedPath(patchPath, fullPath, MapOutcome.Mapped, how)
            : new MappedPath(patchPath, null, MapOutcome.OutsideRoot,
                "resolved to a file outside the source root");

    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            string[] entries;
            try { entries = Directory.GetFiles(directory); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { continue; }

            foreach (var file in entries) yield return file;

            string[] children;
            try { children = Directory.GetDirectories(directory); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { continue; }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (SkippedFolders.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

                pending.Push(child);
            }
        }
    }
}
