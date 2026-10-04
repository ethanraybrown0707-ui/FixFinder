using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The folder a compiled program is started from - the one a name such as "scores.txt" in it is looked for in - chosen
/// the way the person would have run it, so a program that reads a file of its own finds the file here too.
/// </summary>
/// <remarks>
/// A compiled program used to be started in the folder its build went to, where none of its files are, so a correct
/// program that opened scores.txt was reported as crashing with a FileNotFoundException. The build still goes where it
/// always did; only the folder the program starts from changes.
///
/// Java starts from the project's folder - the one holding src, or src/main/java - because that is where an IDE or a
/// build tool starts it. C and C++ start beside their source, as a program built at a command line does - or, built by a
/// Makefile or CMakeLists.txt, in that file's folder, where make is run. Either way, a
/// file the program names in quotes settles it: if it is not in that folder but is in one of the others the program
/// could have been started from, the program starts where the file is.
/// </remarks>
public static partial class WorkingFolder
{
    private const int MostSourcesRead = 200;

    /// <summary>Where the program starts, and the file it names that decided it, if one did - or else the build file in that folder, if it starts there for that.</summary>
    public sealed record Choice(string Folder, string? FileFound, bool IsProjectFolder, string? BuildFileName = null);

    [GeneratedRegex(@"""(?<text>(?:[^""\\\r\n]|\\.){3,200})""")]
    private static partial Regex QuotedText();

    [GeneratedRegex(@"'(?:[^'\\\r\n]|\\.)'")]
    private static partial Regex CharacterLiteral();

    public static bool Chooses(string file) => Path.GetExtension(file).ToLowerInvariant() is
        ".java" or ".c" or ".cpp" or ".cc" or ".cxx" or ".c++";

    public static Choice For(string chosen)
    {
        var file = Path.GetFullPath(chosen);
        var build = IsJava(file) ? null : NativeBuild.For(file).Build;
        var folders = FoldersToStartFrom(file, build);
        var named = FilesNamedIn(SourcesOf(file));

        foreach (var folder in folders)
        {
            foreach (var name in named)
            {
                if (Exists(folder, name)) return new Choice(folder, name, IsProject(folder, file));
            }
        }

        return IsBuildFolder(folders[0], build, file)
            ? new Choice(folders[0], null, IsProjectFolder: false, build!.BuildFileName)
            : new Choice(folders[0], null, IsProject(folders[0], file));
    }

    /// <summary>
    /// The folder a copy of the program has to start from to hold everything it uses: its source, the folder it starts
    /// in, with the files there - and, for a C or C++ program with a build file, the build file's folder, so the copy is
    /// built the way the program is.
    /// </summary>
    public static string CopyRoot(string chosen)
    {
        var file = Path.GetFullPath(chosen);
        var start = For(file).Folder;
        var sourceRoot = SourceRoot(file);
        var root = IsInside(sourceRoot, start) ? start : sourceRoot;

        if (IsJava(file) || NativeBuild.For(file).Build is not { } build) return root;

        while (!IsInside(build.Folder, root) && Path.GetDirectoryName(root) is { } parent) root = parent;
        return root;
    }

    /// <summary>
    /// The folders the program could have been started from, the most likely first. A C or C++ program built by a build
    /// file starts in that file's folder first, as make, and a build started at a command line, run it.
    /// </summary>
    private static IReadOnlyList<string> FoldersToStartFrom(string file, NativeBuild? build)
    {
        var beside = Path.GetDirectoryName(file)!;

        IEnumerable<string> folders = IsJava(file)
            ? [JavaProjectFolder(ProgramLayout.JavaSourceRoot(file)), ProgramLayout.JavaSourceRoot(file), beside]
            : build is not null ? [build.Folder, beside, FolderHoldingSrc(beside) ?? beside]
            : [beside, FolderHoldingSrc(beside) ?? beside];

        return folders.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsBuildFolder(string folder, NativeBuild? build, string file) =>
        build is not null &&
        string.Equals(Path.GetFullPath(build.Folder), folder, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(folder, Path.GetDirectoryName(file), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The folder an IDE or a build tool starts a Java program from: the one holding src/main/java or src/test/java in a
    /// Maven or Gradle layout, the one holding src when the source is kept in src, and otherwise the source root itself.
    /// </summary>
    public static string JavaProjectFolder(string sourceRoot)
    {
        var root = new DirectoryInfo(sourceRoot);

        if (Named(root, "java") && root.Parent is { } kind && (Named(kind, "main") || Named(kind, "test")) &&
            kind.Parent is { } src && Named(src, "src") && src.Parent is { } project)
        {
            return project.FullName;
        }

        return FolderHoldingSrc(sourceRoot) ?? sourceRoot;
    }

    private static string? FolderHoldingSrc(string folder)
    {
        var directory = new DirectoryInfo(folder);
        return Named(directory, "src") ? directory.Parent?.FullName : null;
    }

    private static bool IsProject(string folder, string file) =>
        !string.Equals(folder, Path.GetDirectoryName(file), StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(folder, SourceRoot(file), StringComparison.OrdinalIgnoreCase);

    private static string SourceRoot(string file) => IsJava(file) ? ProgramLayout.JavaSourceRoot(file) : Path.GetDirectoryName(file)!;

    private static bool IsJava(string file) => Path.GetExtension(file).Equals(".java", StringComparison.OrdinalIgnoreCase);

    private static bool Named(DirectoryInfo directory, string name) => directory.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string inner, string outer)
    {
        var relative = Path.GetRelativePath(outer, inner);
        return relative == "." || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative));
    }

    private static IEnumerable<string> SourcesOf(string file)
    {
        if (!IsJava(file)) return ProgramLayout.NativeSources(file);

        try
        {
            return Directory.EnumerateFiles(ProgramLayout.JavaSourceRoot(file), "*.java", SearchOption.AllDirectories)
                .Prepend(file)
                .Take(MostSourcesRead)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [file];
        }
    }

    /// <summary>
    /// Text in quotes in the program's source that reads as a relative path - scores.txt, data/marks.csv - which is what a
    /// program opens relative to the folder it starts from. A path from the root of a drive does not depend on the
    /// folder, and text with a format in it (%d, {0}) is not a name as written.
    /// </summary>
    private static IReadOnlyList<string> FilesNamedIn(IEnumerable<string> sources)
    {
        var names = new List<string>();

        foreach (var source in sources)
        {
            string text;
            try
            {
                if (new FileInfo(source).Length > 2_000_000) continue;
                text = CharacterLiteral().Replace(File.ReadAllText(source), "' '");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (Match quoted in QuotedText().Matches(text))
            {
                var name = quoted.Groups["text"].Value.Replace(@"\\", @"\", StringComparison.Ordinal).Trim();
                if (LooksLikeARelativePath(name) && !names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
            }
        }

        return names;
    }

    private static bool LooksLikeARelativePath(string name) =>
        name.Length >= 3 &&
        (name.Contains('.') || name.Contains('/') || name.Contains('\\')) &&
        name.Any(char.IsLetterOrDigit) &&
        !name.StartsWith('/') && !name.StartsWith('\\') &&
        name.IndexOfAny([':', '%', '{', '}', '*', '?', '<', '>', '|', '"']) < 0;

    private static bool Exists(string folder, string name)
    {
        try
        {
            var path = Path.GetFullPath(Path.Combine(folder, name));
            return File.Exists(path) || Directory.Exists(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
