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

    private static IReadOnlyList<string> JavaFiles(string chosen)
    {
        var root = ProgramLayout.JavaSourceRoot(chosen);
        var files = SourcesUnder(root, ".java");

        return [chosen, .. files.Where(f => !f.Equals(chosen, StringComparison.OrdinalIgnoreCase))];
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
