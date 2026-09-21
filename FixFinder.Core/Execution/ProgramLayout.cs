using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>What else a chosen file's program is made of: the other source files it is built with, the root its packages are
/// named from, the project that builds it.</summary>
public static partial class ProgramLayout
{
    private const int MostFiles = 200;

    [GeneratedRegex(@"(?m)^\s*(?:int|void)\s+main\s*\(")]
    private static partial Regex NativeMain();

    [GeneratedRegex(@"(?m)^\s*package\s+([A-Za-z_][\w.]*)\s*;")]
    private static partial Regex JavaPackage();

    [GeneratedRegex(@"(?m)^\s*package\s+(\w+)")]
    private static partial Regex GoPackage();

    [GeneratedRegex(@"(?m)^\s*from\s+\.+[\w.]*\s+import\b")]
    private static partial Regex PythonRelativeImport();

    public static IReadOnlyList<string> NativeSources(string chosen)
    {
        var extension = Path.GetExtension(chosen).ToLowerInvariant();
        string[] family = extension == ".c" ? [".c"] : [".cpp", ".cc", ".cxx", ".c++"];

        var others = Siblings(chosen, family);
        if (others is null || others.Count == 0) return [chosen];

        var mains = (Defines(chosen, NativeMain()) ? 1 : 0) + others.Count(f => Defines(f, NativeMain()));
        if (mains != 1) return [chosen];

        return [chosen, .. others];
    }

    public static IReadOnlyList<string> HeaderProgram(string header)
    {
        var folder = Path.GetDirectoryName(header)!;
        var name = Path.GetFileName(header);

        List<string> files;

        try
        {
            files = Directory.EnumerateFiles(folder).Take(MostFiles + 1).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        if (files.Count > MostFiles) return [];

        bool Includes(string file, string included) =>
            Read(file) is { } text && Regex.IsMatch(text, $@"(?m)^\s*#\s*include\s*""{Regex.Escape(included)}""");

        var headers = files.Where(f => Path.GetExtension(f).ToLowerInvariant() is ".h" or ".hpp" or ".hh" or ".hxx").ToList();
        var bringing = headers.Where(h => Includes(h, name)).Select(Path.GetFileName).Append(name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var mains = files
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".c" or ".cpp" or ".cc" or ".cxx" or ".c++")
            .Where(f => bringing.Any(h => Includes(f, h!)) && Defines(f, NativeMain()))
            .ToList();

        return mains is [var main] ? NativeSources(main) : [];
    }

    public static IReadOnlyList<string> HeaderNeighbours(string header)
    {
        try
        {
            return Directory.EnumerateFiles(Path.GetDirectoryName(header)!)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".c" or ".cpp" or ".cc" or ".cxx" or ".c++" or ".h" or ".hpp" or ".hh" or ".hxx" or ".inc")
                .Take(MostFiles)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static string JavaSourceRoot(string chosen)
    {
        var folder = Path.GetDirectoryName(chosen)!;

        if (Read(chosen) is not { } text || JavaPackage().Match(text) is not { Success: true } package) return folder;

        var parts = package.Groups[1].Value.Split('.');
        var directory = new DirectoryInfo(folder);

        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (directory is null || !directory.Name.Equals(parts[i], StringComparison.Ordinal)) return folder;
            directory = directory.Parent;
        }

        return directory?.FullName ?? folder;
    }

    /// <summary>The Go program a file belongs to.</summary>
    public sealed record GoProgram(string? Module, IReadOnlyList<string> Files)
    {
        public bool IsSingleFile => Module is null && Files.Count == 1;
    }

    public static GoProgram GoPackageOf(string chosen)
    {
        var folder = Path.GetDirectoryName(chosen)!;
        var module = File.Exists(Path.Combine(folder, "go.mod")) ? folder : null;

        if (Read(chosen) is not { } text || GoPackage().Match(text) is not { Success: true } package) return new GoProgram(module, [chosen]);

        var name = package.Groups[1].Value;
        var others = Siblings(chosen, [".go"])?
            .Where(f => !f.EndsWith("_test.go", StringComparison.OrdinalIgnoreCase))
            .Where(f => Read(f) is { } other && GoPackage().Match(other) is { Success: true } p && p.Groups[1].Value == name)
            .ToList();

        return new GoProgram(module, others is null ? [chosen] : [chosen, .. others]);
    }

    public static string? CSharpProject(string chosen)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(chosen)!);

        for (var depth = 0; depth < 4 && directory is not null; depth++)
        {
            try
            {
                var projects = directory.GetFiles("*.csproj");
                if (projects.Length == 1) return projects[0].FullName;
                if (projects.Length > 1) return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            directory = directory.Parent;
        }

        return null;
    }

    public static (string Module, string Folder)? PythonModule(string chosen)
    {
        if (Read(chosen) is not { } text || !PythonRelativeImport().IsMatch(text)) return null;

        var names = new List<string> { Path.GetFileNameWithoutExtension(chosen) };
        var directory = new DirectoryInfo(Path.GetDirectoryName(chosen)!);

        while (directory is not null && File.Exists(Path.Combine(directory.FullName, "__init__.py")))
        {
            names.Insert(0, directory.Name);
            directory = directory.Parent;
        }

        return names.Count > 1 && directory is not null ? (string.Join('.', names), directory.FullName) : null;
    }

    private static List<string>? Siblings(string chosen, string[] extensions)
    {
        try
        {
            var files = Directory.EnumerateFiles(Path.GetDirectoryName(chosen)!).Take(MostFiles + 1).ToList();
            if (files.Count > MostFiles) return null;

            return files
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f => !Path.GetFullPath(f).Equals(Path.GetFullPath(chosen), StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool Defines(string file, Regex pattern) => Read(file) is { } text && pattern.IsMatch(text);

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
