using System.Collections.Concurrent;

namespace FixFinder.Core.Execution;

/// <summary>
/// Copies a program somewhere private, laid out as it is, so a changed version of it can be built and run without
/// FixFinder writing anything into the person's own folder.
/// </summary>
/// <remarks>
/// The copy holds the program's files as well as its source: a program that reads scores.txt when it runs needs
/// scores.txt beside it in the copy too, or the copy fails where the original would not and the change being tried is
/// blamed for it.
/// </remarks>
public static class ProgramCopy
{
    /// <summary>Folders that are build output, tools' own state or downloaded packages - never part of the program.</summary>
    public static readonly IReadOnlySet<string> NotCopied = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".idea", "node_modules", "__pycache__", ".venv", "venv", "target", "build", "out",
    };

    /// <summary>Whether a folder of the program is copied: not one of those, nor a cmake-build-debug or other folder CLion builds in.</summary>
    public static bool IsCopied(string folderName) =>
        !NotCopied.Contains(folderName) && !folderName.StartsWith("cmake-build-", StringComparison.OrdinalIgnoreCase);

    /// <summary>More files than this is more than a program's worth - a whole Documents folder, say - and is not copied.</summary>
    public const int MostFiles = 300;

    public const long MostBytes = 50 * 1024 * 1024;

    /// <summary>
    /// The folder a copy of the program has to hold: for Java, C and C++ the one that holds both its source and the
    /// folder it starts from; for C# its project; for Go its module; for a Python package the folder it is run from.
    /// </summary>
    public static string RootOf(string file)
    {
        var extension = Path.GetExtension(file).ToLowerInvariant();

        return extension switch
        {
            _ when WorkingFolder.Chooses(file) => WorkingFolder.CopyRoot(file),
            ".cs" when ProgramLayout.CSharpProject(file) is { } project => Path.GetDirectoryName(project)!,
            ".go" when ProgramLayout.GoPackageOf(file).Module is { } module => module,
            ".py" when NotebookScript.Of(file) is { } notebook => Path.GetDirectoryName(notebook.Notebook)!,
            ".py" when ProgramLayout.PythonModule(file) is { } module => module.Folder,
            _ => Path.GetDirectoryName(file)!,
        };
    }

    /// <summary>
    /// Where the chosen file goes in a copy of the program made at <paramref name="copy"/>: where it is under the program's
    /// folder - or, for the script of a notebook's code, which FixFinder wrote to the temp folder, beside the copies of
    /// the notebook's own files, which is where it runs from.
    /// </summary>
    public static string InCopy(string root, string chosen, string copy)
    {
        var relative = Path.GetRelativePath(root, chosen);
        var outside = relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative);

        return Path.Combine(copy, outside ? Path.GetFileName(chosen) : relative);
    }

    /// <summary>
    /// Copies everything under <paramref name="root"/> into <paramref name="destination"/>, laid out the same way - or
    /// nothing at all, returning false, when it holds more than a program's worth of files.
    /// </summary>
    /// <summary>The copies being run just now, and the folder each was copied from.</summary>
    private static readonly ConcurrentDictionary<string, string> Originals = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records that a folder is a copy of the program's folder, so what is read from the program rather than run - the
    /// libraries its pom.xml or its IDE settings name, which the copy leaves out - is read from the original.
    /// </summary>
    public static void Remember(string copy, string original) => Originals[Path.GetFullPath(copy)] = Path.GetFullPath(original);

    public static void Forget(string copy) => Originals.TryRemove(Path.GetFullPath(copy), out _);

    /// <summary>Where a file or folder in a copy of a program was copied from, or the path itself when it is in no copy.</summary>
    public static string OriginalOf(string path)
    {
        foreach (var (copy, original) in Originals)
        {
            if (string.Equals(path, copy, StringComparison.OrdinalIgnoreCase)) return original;

            if (path.StartsWith(copy + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(original, Path.GetRelativePath(copy, path));
        }

        return path;
    }

    /// <summary>Where a path of the original program is in the copy a file is in, or the path itself when the file is in no copy.</summary>
    public static string InCopyOf(string file, string originalPath)
    {
        foreach (var (copy, original) in Originals)
        {
            if (file.StartsWith(copy + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                (originalPath + Path.DirectorySeparatorChar).StartsWith(original + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(copy, Path.GetRelativePath(original, originalPath));
            }
        }

        return originalPath;
    }

    public static bool TryCopyWhole(string root, string destination)
    {
        var files = new List<string>();
        var pending = new Stack<string>([root]);
        long bytes = 0;

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var sub in Directory.EnumerateDirectories(directory))
                if (IsCopied(Path.GetFileName(sub))) pending.Push(sub);

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                files.Add(file);
                bytes += new FileInfo(file).Length;
                if (files.Count > MostFiles || bytes > MostBytes) return false;
            }
        }

        foreach (var file in files)
        {
            var target = Path.Combine(destination, Path.GetRelativePath(root, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }

        Remember(destination, root);
        return true;
    }
}
