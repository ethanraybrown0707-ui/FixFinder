using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Checking;

/// <summary>The source files a chosen file's program is made of: the file itself, then the ones it uses.</summary>
public static partial class ProgramFiles
{
    private const int MostFiles = 40;

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

    public static IReadOnlyList<string> Of(string chosen)
    {
        var path = Path.GetFullPath(chosen);

        var files = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".py" or ".pyw" => Follow(path, PythonNeighbours),
            ".js" or ".mjs" or ".cjs" => Follow(path, JavaScriptNeighbours),
            ".java" => JavaFiles(path),
            ".cs" => CSharpFiles(path),
            ".go" => ProgramLayout.GoPackageOf(path).Files,
            ".c" or ".cpp" or ".cc" or ".cxx" or ".c++" => NativeFiles(path),
            _ => [path],
        };

        return files
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MostFiles)
            .ToList();
    }

    private static List<string> Follow(string chosen, Func<string, IEnumerable<string>> neighbours)
    {
        var found = new List<string> { chosen };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { chosen };

        for (var next = 0; next < found.Count && found.Count < MostFiles; next++)
        {
            foreach (var file in neighbours(found[next]))
            {
                if (seen.Add(file)) found.Add(file);
            }
        }

        return found;
    }

    private static IEnumerable<string> PythonNeighbours(string file)
    {
        if (Read(file) is not { } text) yield break;

        var folder = Path.GetDirectoryName(file)!;

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
        if (Read(file) is not { } text) yield break;

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
    /// A Java program's files: the one chosen, and the others under its source root - except another program's. A folder of
    /// exercises holds several programs, each with a main of its own; a file with a main that the chosen file's code does not
    /// reach is another program, and so is a file only such a one reaches. Other files stay, since a framework can use a
    /// class nothing names.
    /// </summary>
    private static IReadOnlyList<string> JavaFiles(string chosen)
    {
        var root = ProgramLayout.JavaSourceRoot(chosen);
        var others = SourcesUnder(root, ".java").Where(file => !file.Equals(chosen, StringComparison.OrdinalIgnoreCase)).ToList();

        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [chosen] = Read(chosen) ?? "" };
        foreach (var file in others) texts[file] = Read(file) ?? "";

        var reached = Reached([chosen], texts, excluded: new HashSet<string>());
        var otherPrograms = others.Where(file => !reached.Contains(file) && JavaMain().IsMatch(texts[file])).ToList();
        var theirs = Reached(otherPrograms, texts, excluded: reached);

        return [chosen, .. others.Where(file => !theirs.Contains(file))];
    }

    [GeneratedRegex(@"\bvoid\s+main\s*\(")]
    private static partial Regex JavaMain();

    /// <summary>The files these start from, and every file whose class their code names, and so on, leaving out those excluded.</summary>
    private static HashSet<string> Reached(IEnumerable<string> starts, IReadOnlyDictionary<string, string> texts, IReadOnlySet<string> excluded)
    {
        var reached = new HashSet<string>(starts, StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(reached);

        while (pending.Count > 0)
        {
            var text = texts[pending.Dequeue()];

            foreach (var file in texts.Keys.Where(file => !reached.Contains(file) && !excluded.Contains(file)).ToList())
            {
                if (!Regex.IsMatch(text, $@"\b{Regex.Escape(Path.GetFileNameWithoutExtension(file))}\b")) continue;

                reached.Add(file);
                pending.Enqueue(file);
            }
        }

        return reached;
    }

    private static IReadOnlyList<string> CSharpFiles(string chosen)
    {
        if (ProgramLayout.CSharpProject(chosen) is not { } project) return [chosen];

        var files = SourcesUnder(Path.GetDirectoryName(project)!, ".cs");

        return [chosen, .. files.Where(f => !f.Equals(chosen, StringComparison.OrdinalIgnoreCase))];
    }

    private static IReadOnlyList<string> NativeFiles(string chosen)
    {
        var sources = ProgramLayout.NativeSources(chosen);
        var headers = new List<string>();

        foreach (var source in sources)
        {
            if (Read(source) is not { } text) continue;

            foreach (Match include in LocalInclude().Matches(text))
            {
                var header = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, include.Groups["header"].Value));
                if (File.Exists(header)) headers.Add(header);
            }
        }

        return [.. sources, .. headers];
    }

    private static List<string> SourcesUnder(string root, string extension)
    {
        var found = new List<string>();
        var pending = new Stack<string>([root]);

        try
        {
            while (pending.Count > 0 && found.Count < MostFiles)
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

        return found.Take(MostFiles).ToList();
    }

    private static string? Read(string file)
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
