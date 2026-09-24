namespace FixFinder.Core.Checking;

/// <summary>One program found in a folder: the file to hand to the checker, and everything that program is made of.</summary>
/// <param name="Entry">The file the check starts from.</param>
/// <param name="Files">Every file of that program, the entry included.</param>
public sealed record ScannedProgram(string Entry, IReadOnlyList<string> Files)
{
    public string Name => Path.GetFileName(Entry);

    /// <summary>Whether this program is more than the one file, which is why the others are not checked separately.</summary>
    public bool IsSeveralFiles => Files.Count > 1;
}

/// <summary>What a folder holds, and what was left out of it.</summary>
public sealed record ScanPlan
{
    public required string Folder { get; init; }

    public required IReadOnlyList<ScannedProgram> Programs { get; init; }

    /// <summary>How many source files were found before any limit was applied.</summary>
    public required int FilesFound { get; init; }

    /// <summary>Whether a limit stopped the scan short, so what is listed is not everything there is.</summary>
    public required bool StoppedEarly { get; init; }

    public string Summary => StoppedEarly
        ? $"Checking the first {Programs.Count} of {FilesFound} programs found in {Path.GetFileName(Folder)}"
        : Programs.Count == 1
            ? $"One program in {Path.GetFileName(Folder)}"
            : $"{Programs.Count} programs in {Path.GetFileName(Folder)}";
}

/// <summary>
/// Works out which programs a folder holds, so a whole project can be checked rather than one file at a time.
/// </summary>
/// <remarks>
/// Two things stop this being a file listing. A program is usually several files, and checking each of them on its own
/// would compile the same program once per file and report every mistake as many times as it has files - so once a
/// file is claimed by a program, it is not a program of its own. And a folder of real work is mostly not the work:
/// dependencies, build output and caches outnumber the source, and checking those means checking somebody else's code.
/// <para>
/// Nothing here runs or compiles anything. It decides what is worth handing to the checker, and the checker does what
/// it has always done with one file at a time.
/// </para>
/// </remarks>
public static class ProjectScan
{
    /// <summary>Folders that hold what was fetched, built or cached rather than what was written.</summary>
    private static readonly HashSet<string> NotOurs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".svn", ".hg", "__pycache__", ".venv", "venv", "env", ".env",
        "target", "dist", "build", "out", ".vs", ".vscode", ".idea", "vendor", "packages", ".gradle", ".mypy_cache",
        ".pytest_cache", ".tox", "site-packages", "Pods", "DerivedData", ".next", ".nuxt", "coverage", "bower_components",
    };

    /// <summary>The kinds of file FixFinder can check.</summary>
    private static readonly HashSet<string> Source = new(StringComparer.OrdinalIgnoreCase)
    {
        ".py", ".java", ".cs", ".c", ".h", ".cpp", ".cc", ".cxx", ".hpp", ".js", ".mjs", ".cjs", ".go",
    };

    /// <summary>Headers are part of a program rather than programs, so they never start a check of their own.</summary>
    private static readonly HashSet<string> NeverAnEntry = new(StringComparer.OrdinalIgnoreCase) { ".h", ".hpp", ".hh" };

    /// <summary>How many programs are checked from one folder. A whole repository would take longer than anyone waits.</summary>
    public const int MostPrograms = 100;

    /// <summary>How deep into a folder to look, so a scan cannot wander off into something enormous.</summary>
    public const int Deepest = 12;

    /// <summary>A file larger than this is generated, minified or data, whatever its extension says.</summary>
    public const long LargestFile = 2 * 1024 * 1024;

    /// <summary>
    /// Works out what to check in a folder. Reads the names of files and how big they are, and nothing else.
    /// </summary>
    /// <param name="partOfProgram">
    /// Which files belong to the program a file starts; <see cref="ProgramFiles.Of"/> unless a test says otherwise.
    /// </param>
    public static ScanPlan Of(string folder, Func<string, IReadOnlyList<string>>? partOfProgram = null)
    {
        var belongsWith = partOfProgram ?? ProgramFiles.Of;

        var candidates = SourceFilesIn(folder)
            .Where(file => !NeverAnEntry.Contains(Path.GetExtension(file)))
            .Take(MostPrograms * 5)
            .ToList();

        // Which files each one would bring with it, worked out before any of them is chosen. A program's entry is not
        // the file that happens to sort first: helper.py comes before main.py alphabetically and is part of it, so
        // taking them in that order would check the helper on its own and then check it again as part of the program.
        var brings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in candidates)
        {
            try { brings[file] = belongsWith(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { brings[file] = [file]; }
        }

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var programs = new List<ScannedProgram>();

        // The file that accounts for the most is the one to start from; ties keep the order the folder was read in.
        foreach (var file in candidates.OrderByDescending(f => brings[f].Count).ThenBy(candidates.IndexOf))
        {
            if (programs.Count >= MostPrograms) break;
            if (claimed.Contains(file)) continue;

            var together = brings[file];

            // A program reaching outside the folder is still this folder's program, but what it reaches is not this
            // folder's business to check - and claiming it costs nothing, since it was never a candidate.
            foreach (var part in together) claimed.Add(part);

            programs.Add(new ScannedProgram(file, [file, .. together.Where(p => !string.Equals(p, file, StringComparison.OrdinalIgnoreCase))]));
        }

        programs.Sort((a, b) => string.Compare(a.Entry, b.Entry, StringComparison.OrdinalIgnoreCase));

        return new ScanPlan
        {
            Folder = folder,
            Programs = programs,
            FilesFound = candidates.Count,
            StoppedEarly = programs.Count >= MostPrograms,
        };
    }

    /// <summary>Every file worth reading in a folder, nearest the top first, with what is not ours left out.</summary>
    private static IEnumerable<string> SourceFilesIn(string folder)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((folder, 0));

        while (queue.Count > 0)
        {
            var (at, depth) = queue.Dequeue();

            string[] found;
            try
            {
                found = Directory.GetFiles(at);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in found.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (!Source.Contains(Path.GetExtension(file))) continue;

                long size;
                try { size = new FileInfo(file).Length; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

                if (size > LargestFile) continue;

                yield return file;
            }

            if (depth >= Deepest) continue;

            string[] below;
            try
            {
                below = Directory.GetDirectories(at);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var under in below.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(under);
                if (NotOurs.Contains(name) || name.StartsWith('.')) continue;

                queue.Enqueue((under, depth + 1));
            }
        }
    }
}
