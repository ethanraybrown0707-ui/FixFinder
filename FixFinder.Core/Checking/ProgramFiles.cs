using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Checking;

/// <summary>The source files a chosen file's program is made of: the file itself, then the ones it uses.</summary>
public static partial class ProgramFiles
{
    /// <summary>
    /// The most files of a program whose code is read for logic mistakes. The file chosen and the files its code uses come
    /// first, so a program with more leaves out those furthest from it - and the report says how many.
    /// </summary>
    public const int MostFiles = 200;

    /// <summary>The most files looked through to put them in that order: more than any course project has, never a whole drive.</summary>
    private const int MostFilesLookedAt = 2000;

    /// <summary>A program's files, the file chosen first, and how many more it has that were left out.</summary>
    /// <param name="LeftOutAtLeast">Whether it has more files than were looked through, so at least <paramref name="LeftOut"/> were left out.</param>
    public sealed record Found(IReadOnlyList<string> Files, int LeftOut, bool LeftOutAtLeast);

    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "out", "target", "build", "node_modules", ".git", "__pycache__", ".venv", "venv",
    };

    [GeneratedRegex(@"(?m)^\s*(?:from\s+(?<from>\.*[\w.]*)\s+import\s+(?<names>[\w\s,*()]+)|import\s+(?<modules>[\w.]+(?:\s+as\s+\w+)?(?:\s*,\s*[\w.]+(?:\s+as\s+\w+)?)*))")]
    private static partial Regex PythonImport();

    [GeneratedRegex(@"(?:require\s*\(\s*|import\s*\(\s*|\bfrom\s+|^\s*import\s+)['""](?<path>\.{1,2}/[^'""]+)['""]", RegexOptions.Multiline)]
    private static partial Regex JavaScriptImport();

    [GeneratedRegex(@"(?m)^\s*#\s*include\s*""(?<header>[^""]+)""")]
    private static partial Regex LocalInclude();

    [GeneratedRegex(@"\w+")]
    private static partial Regex Word();

    public static IReadOnlyList<string> Of(string chosen) => Read(chosen).Files;

    public static Found Read(string chosen)
    {
        var path = Path.GetFullPath(chosen);

        var (files, lookedAtAll) = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".py" or ".pyw" or ".ipynb" => Follow(path, PythonNeighbours),
            ".js" or ".mjs" or ".cjs" => Follow(path, JavaScriptNeighbours),
            ".java" => JavaFiles(path),
            ".cs" => CSharpFiles(path),
            ".go" => (ProgramLayout.GoPackageOf(path).Files, true),
            ".c" or ".cpp" or ".cc" or ".cxx" or ".c++" => (NativeFiles(path), true),
            _ => ([path], true),
        };

        var distinct = files.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new Found(distinct.Take(MostFiles).ToList(), Math.Max(0, distinct.Count - MostFiles), !lookedAtAll);
    }

    /// <summary>The chosen file and those it imports, and so on, nearest first - and whether every one was reached.</summary>
    private static (IReadOnlyList<string> Files, bool LookedAtAll) Follow(string chosen, Func<string, IEnumerable<string>> neighbours)
    {
        var found = new List<string> { chosen };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { chosen };
        var next = 0;

        for (; next < found.Count && found.Count < MostFilesLookedAt; next++)
        {
            foreach (var file in neighbours(found[next]))
            {
                if (seen.Add(file)) found.Add(file);
            }
        }

        return (found, next >= found.Count);
    }

    private static IEnumerable<string> PythonNeighbours(string file)
    {
        // A notebook's imports are in the code of its cells, not in the JSON around them.
        var code = Path.GetExtension(file).Equals(".ipynb", StringComparison.OrdinalIgnoreCase) ? NotebookScript.CodeIn(file) : TextOf(file);
        if (code is not { } text) yield break;

        // A notebook's code imports what is beside the notebook, not what is beside the script it was written to.
        var folder = NotebookScript.FolderOfCode(file);

        foreach (Match import in PythonImport().Matches(text))
        {
            var modules = import.Groups["from"].Success
                ? [import.Groups["from"].Value]
                : import.Groups["modules"].Value.Split(',').Select(m => m.Trim().Split(' ')[0]);

            foreach (var module in modules)
            {
                var dots = module.TakeWhile(c => c == '.').Count();
                var start = folder;

                for (var up = 1; up < dots && start is not null; up++) start = Path.GetDirectoryName(start);
                if (start is null) continue;

                var relative = module[dots..].Replace('.', Path.DirectorySeparatorChar);

                if (relative.Length > 0)
                {
                    var asFile = Path.Combine(start, relative + ".py");
                    var asPackage = Path.Combine(start, relative, "__init__.py");

                    if (File.Exists(asFile)) yield return asFile;
                    else if (File.Exists(asPackage)) yield return asPackage;
                }

                if (!import.Groups["from"].Success) continue;

                var package = Path.Combine(start, relative);

                foreach (var name in import.Groups["names"].Value.Split(',', '(', ')').Select(n => n.Trim().Split(' ')[0]).Where(n => n.Length > 0))
                {
                    var submodule = Path.Combine(package, name + ".py");
                    if (File.Exists(submodule)) yield return submodule;
                }
            }
        }
    }

    private static IEnumerable<string> JavaScriptNeighbours(string file)
    {
        if (TextOf(file) is not { } text) yield break;

        var folder = Path.GetDirectoryName(file)!;

        foreach (Match import in JavaScriptImport().Matches(text))
        {
            var target = Path.GetFullPath(Path.Combine(folder, import.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar)));

            foreach (var candidate in new[] { target, target + ".js", target + ".mjs", target + ".cjs", Path.Combine(target, "index.js") })
            {
                if (File.Exists(candidate) && Path.GetExtension(candidate) is ".js" or ".mjs" or ".cjs")
                {
                    yield return candidate;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// A Java program's files: the one chosen, the others under its source root its code reaches - nearest first - and then the
    /// rest, except another program's. A folder of exercises holds several programs, each with a main of its own; a file with a
    /// main that the chosen file's code does not reach is another program, and so is a file only such a one reaches. Other
    /// files stay, since a framework can use a class nothing names.
    /// </summary>
    private static (IReadOnlyList<string> Files, bool LookedAtAll) JavaFiles(string chosen)
    {
        var (sources, lookedAtAll) = SourcesUnder(ProgramLayout.JavaSourceRoot(chosen), ".java");
        var others = sources.Where(file => !file.Equals(chosen, StringComparison.OrdinalIgnoreCase)).ToList();

        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [chosen] = TextOf(chosen) ?? "" };
        foreach (var file in others) texts[file] = TextOf(file) ?? "";
        var words = texts.ToDictionary(text => text.Key, text => WordsIn(text.Value), StringComparer.OrdinalIgnoreCase);

        var reached = Reached([chosen], words, excluded: new HashSet<string>());
        var reachedFiles = new HashSet<string>(reached, StringComparer.OrdinalIgnoreCase);
        var otherPrograms = others.Where(file => !reachedFiles.Contains(file) && JavaMain().IsMatch(texts[file])).ToList();
        var theirs = new HashSet<string>(Reached(otherPrograms, words, excluded: reachedFiles), StringComparer.OrdinalIgnoreCase);

        return ([.. reached, .. others.Where(file => !reachedFiles.Contains(file) && !theirs.Contains(file))], lookedAtAll);
    }

    [GeneratedRegex(@"\bvoid\s+main\s*\(")]
    private static partial Regex JavaMain();

    /// <summary>
    /// The files these start from, then every file whose class their code names - its file's name, as a word of their code -
    /// and so on, nearest first, leaving out those excluded.
    /// </summary>
    private static List<string> Reached(IEnumerable<string> starts, IReadOnlyDictionary<string, IReadOnlyList<string>> words, IReadOnlySet<string> excluded)
    {
        var order = starts.ToList();
        var reached = new HashSet<string>(order, StringComparer.OrdinalIgnoreCase);
        var filesNamed = words.Keys.Where(file => !excluded.Contains(file)).ToLookup(Path.GetFileNameWithoutExtension, StringComparer.Ordinal);

        for (var next = 0; next < order.Count; next++)
        {
            foreach (var word in words[order[next]])
            {
                foreach (var file in filesNamed[word])
                {
                    if (reached.Add(file)) order.Add(file);
                }
            }
        }

        return order;
    }

    /// <summary>The words of a file's code, each once, in the order they first come.</summary>
    private static IReadOnlyList<string> WordsIn(string text) =>
        Word().Matches(text).Select(word => word.Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>A C# project's files: the one chosen, those its code reaches - by the names of their files - nearest first, then the rest.</summary>
    private static (IReadOnlyList<string> Files, bool LookedAtAll) CSharpFiles(string chosen)
    {
        if (ProgramLayout.CSharpProject(chosen) is not { } project) return ([chosen], true);

        var (sources, lookedAtAll) = SourcesUnder(Path.GetDirectoryName(project)!, ".cs");
        var others = sources.Where(file => !file.Equals(chosen, StringComparison.OrdinalIgnoreCase)).ToList();

        var words = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { [chosen] = WordsIn(TextOf(chosen) ?? "") };
        foreach (var file in others) words[file] = WordsIn(TextOf(file) ?? "");

        var reached = Reached([chosen], words, excluded: new HashSet<string>());
        var reachedFiles = new HashSet<string>(reached, StringComparer.OrdinalIgnoreCase);

        return ([.. reached, .. others.Where(file => !reachedFiles.Contains(file))], lookedAtAll);
    }

    /// <summary>
    /// A C or C++ program's files: its sources, then the headers they include in quotes - found beside the file that
    /// includes them, or in the include folders its build file names, as the compiler finds them.
    /// </summary>
    private static IReadOnlyList<string> NativeFiles(string chosen)
    {
        var build = NativeBuild.For(chosen).Build;
        var sources = build?.Sources ?? ProgramLayout.NativeSources(chosen);
        var includeFolders = build?.IncludeFolders ?? [];
        var headers = new List<string>();

        foreach (var source in sources)
        {
            if (TextOf(source) is not { } text) continue;

            foreach (Match include in LocalInclude().Matches(text))
            {
                var name = include.Groups["header"].Value;
                var header = includeFolders.Prepend(Path.GetDirectoryName(source)!)
                    .Select(folder => Path.GetFullPath(Path.Combine(folder, name)))
                    .FirstOrDefault(File.Exists);

                if (header is not null && !headers.Contains(header, StringComparer.OrdinalIgnoreCase)) headers.Add(header);
            }
        }

        return [.. sources, .. headers];
    }

    /// <summary>The source files under a folder - as many as are looked through - and whether that was every one.</summary>
    private static (List<string> Files, bool LookedAtAll) SourcesUnder(string root, string extension)
    {
        var found = new List<string>();
        var pending = new Stack<string>([root]);

        try
        {
            while (pending.Count > 0 && found.Count < MostFilesLookedAt)
            {
                var folder = pending.Pop();

                found.AddRange(Directory.EnumerateFiles(folder, "*" + extension).Order(StringComparer.OrdinalIgnoreCase));

                foreach (var sub in Directory.EnumerateDirectories(folder))
                {
                    if (!SkippedFolders.Contains(Path.GetFileName(sub))) pending.Push(sub);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return (found.Take(MostFilesLookedAt).ToList(), pending.Count == 0 && found.Count <= MostFilesLookedAt);
    }

    private static string? TextOf(string file)
    {
        try
        {
            return new FileInfo(file).Length > 2_000_000 ? null : File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
