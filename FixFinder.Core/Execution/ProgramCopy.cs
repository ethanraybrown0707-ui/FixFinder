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
            ".py" when ProgramLayout.PythonModule(file) is { } module => module.Folder,
            _ => Path.GetDirectoryName(file)!,
        };
    }

    /// <summary>
    /// Copies everything under <paramref name="root"/> into <paramref name="destination"/>, laid out the same way - or
    /// nothing at all, returning false, when it holds more than a program's worth of files.
    /// </summary>
    public static bool TryCopyWhole(string root, string destination)
    {
        var files = new List<string>();
        var pending = new Stack<string>([root]);
        long bytes = 0;

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var sub in Directory.EnumerateDirectories(directory))
                if (!NotCopied.Contains(Path.GetFileName(sub))) pending.Push(sub);

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

        return true;
    }
}
