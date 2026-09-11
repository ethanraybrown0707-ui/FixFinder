namespace FixFinder.Core.Patching;

/// <summary>What became of one path in a patch.</summary>
public enum MapOutcome
{
    /// <summary>Resolved to exactly one existing file inside the source root.</summary>
    Mapped,

    /// <summary>The patch creates this file, and the place it would go is inside the root.</summary>
    WouldCreate,

    /// <summary>No file under the source root matches.</summary>
    NotFound,

    /// <summary>Several files match and nothing distinguishes them. Never guessed at.</summary>
    Ambiguous,

    /// <summary>The path resolves outside the source root. Always refused.</summary>
    OutsideRoot,
}

/// <param name="PatchPath">The path exactly as the patch stated it.</param>
/// <param name="FullPath">Where it resolved to, or null.</param>
/// <param name="Explanation">How it was matched, or why it was not. Always shown.</param>
public sealed record MappedPath(
    string PatchPath, string? FullPath, MapOutcome Outcome, string Explanation)
{
    public bool Usable => Outcome is MapOutcome.Mapped or MapOutcome.WouldCreate;

    public string Display => FullPath is null
        ? $"{PatchPath}  →  {Explanation}"
        : $"{PatchPath}  →  {FullPath}  ({Explanation})";
}

/// <summary>
/// Works out which file on this disk a path inside a patch refers to.
/// </summary>
/// <remarks>
/// A diff written against someone else's checkout says <c>src/cart/basket.py</c>; this tree may
/// hold it at <c>app/cart/basket.py</c>, or hold three files called <c>basket.py</c>, or not
/// hold it at all. Matching progressively shorter suffixes handles the first case; requiring a
/// unique match handles the second by refusing rather than choosing.
/// <para>
/// <b>The containment check is the one rule that cannot be relaxed.</b> A patch is attacker
/// -controlled text fetched from the public internet, and a path of
/// <c>a/../../../../Windows/System32/drivers/etc/hosts</c> is a perfectly well-formed diff. The
/// defence is structural: every resolved path is expanded with
/// <see cref="Path.GetFullPath(string)"/> and must then sit beneath the expanded source root.
/// Scanning the patch text for "<c>..</c>" is not a substitute - it misses symlinks, absolute
/// paths, alternate separators and every encoding trick - and it is the version of this check
/// that people write when they are thinking about strings rather than about files.
/// </para>
/// </remarks>
public sealed class SourcePathMapper
{
    /// <summary>Folders never searched: build output, dependencies and version-control metadata.</summary>
    private static readonly string[] SkippedFolders =
    [
        ".git", ".svn", ".hg", "node_modules", "bin", "obj", "dist", "build", "out",
        "__pycache__", ".venv", "venv", "env", ".tox", "target", "vendor", "packages",
        ".idea", ".vs", ".vscode", ".gradle", ".mypy_cache", ".pytest_cache", "site-packages",
    ];

    /// <summary>Cap on the file index, so a huge tree cannot make this hang.</summary>
    private const int MaximumIndexedFiles = 60_000;

    private readonly string _root;
    private readonly Dictionary<string, List<string>> _byFileName;
    private readonly HashSet<string> _fromStackTrace;

    public string Root => _root;

    /// <summary>How many files were indexed, for the preview's explanation.</summary>
    public int IndexedFiles { get; }

    /// <param name="sourceRoot">The confirmed source root. Nothing outside it is ever returned.</param>
    /// <param name="stackTraceFiles">
    /// Files named in the crash, used to break ties.
    /// </param>
    /// <remarks>
    /// The stack trace is the best tie-breaker available: of three files called
    /// <c>basket.py</c>, the one that appeared in the traceback is the one that actually ran.
    /// </remarks>
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

    /// <summary>Maps every file a patch touches.</summary>
    public IReadOnlyList<MappedPath> MapAll(ParsedPatch patch) =>
        [.. patch.Files.Select(Map)];

    public MappedPath Map(FilePatch file)
    {
        var path = file.TargetPath;

        return path is null
            ? new MappedPath("(no path)", null, MapOutcome.NotFound, "the patch names no file")
            : Map(path, file.IsNewFile);
    }

    /// <summary>Resolves one path from a patch to a full path inside the root, or refuses it.</summary>
    public MappedPath Map(string patchPath, bool isNewFile = false)
    {
        var relative = patchPath.Replace('\\', '/').Trim();

        // An absolute path in a patch refers to somebody else's machine. Even when it happens
        // to exist here, honouring it would let a diff name any file on this disk.
        if (Path.IsPathRooted(relative))
        {
            return new MappedPath(patchPath, null, MapOutcome.OutsideRoot,
                "the patch names an absolute path, which would point outside the source root");
        }

        // Checked before any search, so a traversal is refused on its own terms rather than
        // only if it happens not to match something.
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

    /// <summary>
    /// Falls back to progressively shorter suffixes of the path.
    /// </summary>
    /// <remarks>
    /// <c>src/cart/basket.py</c>, then <c>cart/basket.py</c>, then <c>basket.py</c>. The longest
    /// suffix that matches exactly one file wins, because a longer suffix carries more evidence.
    /// Ties are broken only by the stack trace; beyond that, ambiguity is reported, never
    /// resolved by picking one.
    /// </remarks>
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

            // Several files fit. The stack trace is the only evidence allowed to break the tie.
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

    /// <summary>
    /// Resolves a relative path against the root, returning null if it escapes.
    /// </summary>
    /// <remarks>
    /// <see cref="Path.GetFullPath(string)"/> is what collapses <c>..</c> segments, alternate
    /// separators and the rest into a single canonical answer - which is exactly why the check
    /// happens on its output rather than on the text that went in.
    /// </remarks>
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

    /// <summary>
    /// The containment test. Every path FixFinder writes has passed through here.
    /// </summary>
    /// <remarks>
    /// The trailing separator matters: without it, a root of <c>C:\work\app</c> would accept
    /// <c>C:\work\app-secrets\config.env</c>, because the one string does begin with the other.
    /// </remarks>
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
