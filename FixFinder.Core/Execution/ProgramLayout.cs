using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// What else a chosen file's program is made of: the other source files it is built with, the root its packages are
/// named from, the project that builds it.
/// </summary>
/// <remarks>
/// A program is often more than the file that was picked: a <c>main.c</c> calling functions in <c>util.c</c>, a Java class
/// in <c>src/app/Main.java</c> using <c>app.util.Helper</c>, a Go <c>package main</c> split across three files, a
/// <c>Program.cs</c> in a project, a Python module importing <c>.helper</c> from its own package. Run as a lone file,
/// each of those fails for reasons that are about how it was run, not about the code.
/// <para>
/// <b>Only what the files themselves settle is read here</b> - which files share the folder and the package, which one
/// has <c>main</c>, what a <c>package</c> line and <c>__init__.py</c> say - and when that is not clear-cut, the file is
/// run on its own as before. Two C files in one folder that both define <c>main</c> are two programs, not one.
/// </para>
/// </remarks>
public static partial class ProgramLayout
{
    /// <summary>More files than this in a folder, and it is not treated as one program.</summary>
    private const int MostFiles = 200;

    [GeneratedRegex(@"(?m)^\s*(?:int|void)\s+main\s*\(")]
    private static partial Regex NativeMain();

    [GeneratedRegex(@"(?m)^\s*package\s+([A-Za-z_][\w.]*)\s*;")]
    private static partial Regex JavaPackage();

    [GeneratedRegex(@"(?m)^\s*package\s+(\w+)")]
    private static partial Regex GoPackage();

    [GeneratedRegex(@"(?m)^\s*from\s+\.+[\w.]*\s+import\b")]
    private static partial Regex PythonRelativeImport();

    // ------------------------------------------------------------------ C and C++

    /// <summary>
    /// Every C or C++ source file the chosen one is built with: itself first, then the others beside it - when exactly one
    /// file there defines <c>main</c>, whether that is the chosen one or another (a fix to <c>util.c</c> is built with the
    /// <c>main.c</c> that uses it). Otherwise just itself.
    /// </summary>
    public static IReadOnlyList<string> NativeSources(string chosen)
    {
        var extension = Path.GetExtension(chosen).ToLowerInvariant();
        string[] family = extension == ".c" ? [".c"] : [".cpp", ".cc", ".cxx", ".c++"];

        var others = Siblings(chosen, family);
        if (others is null || others.Count == 0) return [chosen];

        // Two files with a main are two programs that happen to share a folder; none is a folder of loose files.
        var mains = (Defines(chosen, NativeMain()) ? 1 : 0) + others.Count(f => Defines(f, NativeMain()));
        if (mains != 1) return [chosen];

        return [chosen, .. others];
    }

    /// <summary>
    /// The program a header belongs to, to build when the header changes: the C or C++ file beside it that has <c>main</c> and
    /// includes the header - directly, or through another header in the folder - with the rest of its sources. Empty when there is
    /// no such file, and a header on its own is not something that can be built.
    /// </summary>
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

        // The headers that bring this one in, one level up, and then every source that includes any of them.
        var headers = files.Where(f => Path.GetExtension(f).ToLowerInvariant() is ".h" or ".hpp" or ".hh" or ".hxx").ToList();
        var bringing = headers.Where(h => Includes(h, name)).Select(Path.GetFileName).Append(name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var mains = files
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".c" or ".cpp" or ".cc" or ".cxx" or ".c++")
            .Where(f => bringing.Any(h => Includes(f, h!)) && Defines(f, NativeMain()))
            .ToList();

        return mains is [var main] ? NativeSources(main) : [];
    }

    /// <summary>The C and C++ sources and headers beside a header - what building its program reads from that folder.</summary>
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

    // ------------------------------------------------------------------ Java

    /// <summary>
    /// The folder a Java file's package is named from: <c>src</c> for <c>src/app/util/Helper.java</c> declaring
    /// <c>package app.util;</c>. The file's own folder when it declares no package, or the path does not match it.
    /// </summary>
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

    // ------------------------------------------------------------------ Go

    /// <summary>The Go program a file belongs to.</summary>
    /// <param name="Module">The folder holding go.mod, when the program is built as a module; null otherwise.</param>
    /// <param name="Files">Every file of its package in the folder, the chosen one first.</param>
    public sealed record GoProgram(string? Module, IReadOnlyList<string> Files)
    {
        public bool IsSingleFile => Module is null && Files.Count == 1;
    }

    /// <summary>
    /// The files of the chosen file's package in its folder, and whether a go.mod there makes it a module. Test files and
    /// files of other packages are left out.
    /// </summary>
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

    // ------------------------------------------------------------------ C#

    /// <summary>
    /// The project that builds a C# file: the one .csproj in its folder or a folder above, whose folder therefore holds the
    /// file - which an SDK-style project compiles along with every other .cs beneath it. Null for a file on its own.
    /// </summary>
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

    // ------------------------------------------------------------------ Python

    /// <summary>
    /// How to run a Python file that imports from its own package - <c>from .helper import greet</c> - which only works as
    /// a module: the dotted name (<c>shop.app</c>) and the folder above the top package to run it from. Null for a script.
    /// </summary>
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

        // Not inside a package at all: running it as a module would not help, so it is left to fail as a script does.
        return names.Count > 1 && directory is not null ? (string.Join('.', names), directory.FullName) : null;
    }

    // ------------------------------------------------------------------ plumbing

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
