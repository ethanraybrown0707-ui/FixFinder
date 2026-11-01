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

    /// <summary>
    /// The C or C++ files a chosen file is built with: those its Makefile or CMakeLists.txt builds its program from, or,
    /// with no build file to follow, the others of its kind beside it when exactly one of them all has a main.
    /// </summary>
    public static IReadOnlyList<string> NativeSources(string chosen)
    {
        if (NativeBuild.For(chosen).Build is { } build) return build.Sources;

        var extension = Path.GetExtension(chosen).ToLowerInvariant();
        string[] family = extension == ".c" ? [".c"] : [".cpp", ".cc", ".cxx", ".c++"];

        var others = Siblings(chosen, family);
        if (others is null || others.Count == 0) return [chosen];

        var mains = (Defines(chosen, NativeMain()) ? 1 : 0) + others.Count(f => Defines(f, NativeMain()));
        if (mains != 1) return [chosen];

        return [chosen, .. others];
    }

    /// <summary>
    /// The sources of the program a header is part of: the program its build file builds from sources that include it, or,
    /// with no build file, the one main beside it that includes it and the files built with that main.
    /// </summary>
    public static IReadOnlyList<string> HeaderProgram(string header)
    {
        if (NativeBuild.ForHeader(header) is { } build) return build.Sources;

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

    [GeneratedRegex(@"^\s*from\s+(?<module>[A-Za-z_]\w*)[\w.]*\s+import\b")]
    private static partial Regex PythonFromImport();

    [GeneratedRegex(@"^\s*import\s+(?<modules>[A-Za-z_][\w.]*(?:\s+as\s+\w+)?(?:\s*,\s*[A-Za-z_][\w.]*(?:\s+as\s+\w+)?)*)\s*$")]
    private static partial Regex PythonImport();

    /// <summary>What being in a folder marks it as a Python project's own folder - the one its tests are run from.</summary>
    private static readonly string[] PythonProjectMarkers =
        [".git", "pyproject.toml", "setup.py", "setup.cfg", "requirements.txt", "Pipfile", "tox.ini", "pytest.ini", "environment.yml", ".idea", ".vscode"];

    /// <summary>How many folders above a Python file its program's own modules are looked for in: tests\unit\test_marks.py is two below them.</summary>
    private const int MostFoldersUp = 3;

    /// <summary>
    /// The folders, other than a file of tests' own, that hold modules of its program it imports by name - grades.py in the
    /// folder above the tests folder test_grades.py is in, or in the src folder beside it. Running the tests from the
    /// project's folder, as python -m pytest and an IDE do, has Python look for modules there; running the file from its
    /// own folder does not, and the import fails. Looked for in the folders above the file, the nearest first, up to its
    /// project's own folder; a module beside the file is the one Python finds, so nothing is looked for above it. None for
    /// a file that is not one of unittest's or pytest's tests: python scripts\run.py looks only in scripts, wherever it is
    /// started, so a program that imports from the folder above fails there - which is said, not hidden.
    /// </summary>
    /// <param name="interpreter">
    /// The Python that runs the file, when known: a module it finds by itself - random, or an installed package - is the
    /// one the program means, never a random.py written for last week's exercise in a folder above. Without it, every
    /// folder that could be needed is given, as a copy of the program has to hold them all.
    /// </param>
    public static IReadOnlyList<string> PythonImportFolders(string chosen, string? interpreter = null)
    {
        var full = Path.GetFullPath(chosen);
        var original = ProgramCopy.OriginalOf(full);

        if (!IsRunFromTheProjectsFolder(original) || ImportedModuleNames(original) is not { Count: > 0 } imported) return [];

        var ownFolder = Path.GetDirectoryName(original)!;
        var packageFolder = PythonModule(original)?.Folder;
        var heldAbove = new List<(string Module, string Folder)>();

        foreach (var module in imported)
        {
            if (HoldsModule(ownFolder, module) || packageFolder is not null && HoldsModule(packageFolder, module)) continue;
            if (FolderHolding(module, ownFolder) is { } folder) heldAbove.Add((module, folder));
        }

        var foundByPython = interpreter is null || heldAbove.Count == 0
            ? new HashSet<string>()
            : PythonModulesFound.Among(interpreter, heldAbove.Select(held => held.Module).ToList());

        return AsInCopy(full, heldAbove.Where(held => !foundByPython.Contains(held.Module)).Select(held => held.Folder));
    }

    /// <summary>
    /// Folders of a program found from the original of a file, as they are where the file is: a copy made to try a change in
    /// is looked at as the program it was copied from, and the folders given are the copy's - only those the copy holds,
    /// never the program's own, which would have the copy run the original's unchanged modules.
    /// </summary>
    private static List<string> AsInCopy(string full, IEnumerable<string> foldersOfOriginal)
    {
        var originals = foldersOfOriginal.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (string.Equals(ProgramCopy.OriginalOf(full), full, StringComparison.OrdinalIgnoreCase)) return originals;

        return originals.Select(folder => ProgramCopy.InCopyOf(full, folder))
            .Where(inCopy => !originals.Contains(inCopy, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// The folders a file of tests imports the program's modules from, other than its own, as the Python it is likely run
    /// with sees them - which Python is asked only for a file of tests.
    /// </summary>
    public static IReadOnlyList<string> PythonImportFoldersOfTests(string file) =>
        IsRunFromTheProjectsFolder(ProgramCopy.OriginalOf(Path.GetFullPath(file))) ? PythonImportFolders(file, PythonFor(file)) : [];

    /// <summary>
    /// Whether a file is one of unittest's or pytest's tests, which are run from the project's folder; a program, or a
    /// module's doctests - python -m doctest marks.py - look for modules only in the file's own folder.
    /// </summary>
    private static bool IsRunFromTheProjectsFolder(string file) =>
        PythonTests.FrameworkOf(file) is PythonTests.Framework.Unittest or PythonTests.Framework.Pytest;

    /// <summary>The Python a file is run with, near enough to say which modules it has itself: its project's environment's, or the one on PATH.</summary>
    public static string? PythonFor(string file) =>
        PythonEnvironment.For(ProgramCopy.OriginalOf(Path.GetFullPath(file)))?.Interpreter ?? TargetFactory.FindOnPath("python");

    /// <summary>PYTHONPATH with folders put first, ahead of what it already holds, so Python looks for modules in them too.</summary>
    public static string PythonPathWith(IEnumerable<string> folders)
    {
        var already = Environment.GetEnvironmentVariable("PYTHONPATH") is { Length: > 0 } given ? new[] { given } : [];
        return string.Join(Path.PathSeparator, folders.Concat(already));
    }

    /// <summary>The first name of each module a Python file imports by name - not from its own package with a dot - outside its comments and text.</summary>
    private static List<string>? ImportedModuleNames(string file)
    {
        if (Read(file) is not { } text) return null;

        var names = new List<string>();
        foreach (var line in LocalFixes.CodeText.MaskAll(text.ReplaceLineEndings("\n").Split('\n'), LocalFixes.Syntax.Python))
        {
            if (PythonFromImport().Match(line) is { Success: true } from) names.Add(from.Groups["module"].Value);
            else if (PythonImport().Match(line) is { Success: true } plain)
                names.AddRange(plain.Groups["modules"].Value.Split(',').Select(module => module.Trim().Split([' ', '.'])[0]));
        }

        return names.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>The folder above a Python file's own, or its src folder, that holds a module, the nearest first - stopping at the project's own folder.</summary>
    private static string? FolderHolding(string module, string ownFolder)
    {
        if (HoldsModule(Path.Combine(ownFolder, "src"), module)) return Path.Combine(ownFolder, "src");

        var directory = Directory.GetParent(ownFolder);
        for (var up = 0; up < MostFoldersUp && directory is not null; up++, directory = directory.Parent)
        {
            if (HoldsModule(directory.FullName, module)) return directory.FullName;
            if (HoldsModule(Path.Combine(directory.FullName, "src"), module)) return Path.Combine(directory.FullName, "src");

            if (PythonProjectMarkers.Any(marker => Path.Exists(Path.Combine(directory.FullName, marker)))) break;
        }

        return null;
    }

    /// <summary>
    /// Where pytest reads its settings for a test file: the files it takes them from, each with the section they are in,
    /// in the order pytest looks for them in a folder - pytest.ini whatever it holds, the others only with that section.
    /// </summary>
    private static readonly (string File, string? Section)[] PytestSettingsFiles =
        [("pytest.ini", null), ("pyproject.toml", "tool.pytest.ini_options"), ("tox.ini", "pytest"), ("setup.cfg", "tool:pytest")];

    /// <summary>How many folders above a test file pytest's settings and conftest.py files are looked for in.</summary>
    private const int MostFoldersUpForPytest = 4;

    [GeneratedRegex(@"^\s*\[(?<section>[^\]]+)\]\s*$")]
    private static partial Regex SettingsSection();

    [GeneratedRegex(@"^\s*pythonpath\s*=\s*(?<value>.*)$")]
    private static partial Regex PythonPathSetting();

    [GeneratedRegex(@"[""'](?<folder>[^""']+)[""']")]
    private static partial Regex QuotedText();

    /// <summary>
    /// The folders pytest's settings for a test file tell it to look for modules in - pythonpath = src app in pytest.ini,
    /// or pythonpath = ["src"] in pyproject.toml - each from the folder of the file that says so, as pytest reads them.
    /// </summary>
    public static IReadOnlyList<string> PytestPythonPath(string testFile)
    {
        var full = Path.GetFullPath(testFile);
        if (PytestSettings(ProgramCopy.OriginalOf(full)) is not { } settings || Read(settings.File) is not { } text) return [];

        var inSection = settings.Section is null;
        var value = new List<string>();
        var reading = false;

        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            if (SettingsSection().Match(line) is { Success: true } header)
            {
                inSection = settings.Section is null ? header.Groups["section"].Value.Trim() == "pytest" : header.Groups["section"].Value.Trim() == settings.Section;
                reading = false;
                continue;
            }

            if (!inSection) continue;

            if (PythonPathSetting().Match(line) is { Success: true } setting)
            {
                value.Add(setting.Groups["value"].Value);
                reading = true;
            }

            // A setting in an .ini file goes on over the indented lines below it; a list in pyproject.toml, to its closing bracket.
            else if (reading && line.Length > 0 && (char.IsWhiteSpace(line[0]) || !line.Contains('='))) value.Add(line);
            else reading = false;
        }

        var joined = string.Join(" ", value);
        var folders = settings.File.EndsWith(".toml", StringComparison.OrdinalIgnoreCase)
            ? QuotedText().Matches(joined).Select(match => match.Groups["folder"].Value)
            : joined.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var settingsFolder = Path.GetDirectoryName(settings.File)!;
        return AsInCopy(full, folders.Select(folder => Path.GetFullPath(Path.Combine(settingsFolder, folder))).Where(Directory.Exists));
    }

    /// <summary>
    /// The folders above a test file whose files pytest uses when it runs it: the one its settings are in, and those of the
    /// conftest.py files above it - their fixtures, and the folders they put where Python looks.
    /// </summary>
    public static IReadOnlyList<string> PytestFoldersAbove(string testFile)
    {
        var full = Path.GetFullPath(testFile);
        var original = ProgramCopy.OriginalOf(full);
        var settings = PytestSettings(original);

        return AsInCopy(full, [.. settings is { } found ? [Path.GetDirectoryName(found.File)!] : Array.Empty<string>(), .. ConftestFoldersAbove(original)]);
    }

    /// <summary>
    /// The folder pytest is told is the project's - its rootdir - for a test file below a conftest.py, when no settings file
    /// says where the project is. Started in the project's folder, as python -m pytest is, pytest uses the fixtures of the
    /// conftest.py files from there down; given the test file alone, it would start from the file's own folder and use none
    /// above it. Null when pytest's settings say where the project is, or there is no conftest.py above the file.
    /// </summary>
    public static string? PytestRootFolder(string testFile)
    {
        var full = Path.GetFullPath(testFile);
        var original = ProgramCopy.OriginalOf(full);
        if (PytestSettings(original) is not null || ConftestFoldersAbove(original) is not [.., var topmost]) return null;

        return AsInCopy(full, [topmost]).FirstOrDefault();
    }

    /// <summary>The folders above a test file's own with a conftest.py in them, the nearest first, up to the project's own folder.</summary>
    private static List<string> ConftestFoldersAbove(string testFile)
    {
        var folders = new List<string>();
        var directory = Directory.GetParent(testFile)?.Parent;

        for (var up = 0; up < MostFoldersUpForPytest && directory is not null; up++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "conftest.py"))) folders.Add(directory.FullName);
            if (PythonProjectMarkers.Any(marker => Path.Exists(Path.Combine(directory.FullName, marker)))) break;
        }

        return folders;
    }

    /// <summary>The file pytest takes its settings from for a test file, and the section they are in: the nearest above it that has them.</summary>
    private static (string File, string? Section)? PytestSettings(string testFile)
    {
        var directory = Directory.GetParent(Path.GetFullPath(testFile));

        for (var up = 0; up <= MostFoldersUpForPytest && directory is not null; up++, directory = directory.Parent)
        {
            foreach (var (name, section) in PytestSettingsFiles)
            {
                var file = Path.Combine(directory.FullName, name);
                if (!File.Exists(file)) continue;
                if (section is null || Read(file) is { } text && text.ReplaceLineEndings("\n").Split('\n').Any(line => SettingsSection().Match(line) is { Success: true } header && header.Groups["section"].Value.Trim() == section))
                    return (file, section);
            }
        }

        return null;
    }

    /// <summary>How many files of a project are looked through for a module of its own that Python could not find.</summary>
    private const int MostFilesLookedThrough = 2000;

    /// <summary>
    /// A module of the program's own that Python could not find when it ran a file - grades.py in an app folder beside the
    /// tests folder - looked for in the file's project: the folder above it with a project's files in it, or the one just
    /// above it. Null when there is none, so what is missing is not part of the program. Environments, build output and
    /// tools' own folders are not looked in: the packages installed in .venv are not the program's own.
    /// </summary>
    /// <returns>The module's .py file, or its package's folder.</returns>
    public static string? PythonModuleInProject(string importingFile, string module)
    {
        var ownFolder = Directory.GetParent(Path.GetFullPath(importingFile));
        if (ownFolder?.Parent is not { } project) return null;

        for (var (above, up) = (project, 0); above is not null && up < MostFoldersUp; above = above.Parent, up++)
        {
            if (!PythonProjectMarkers.Any(marker => Path.Exists(Path.Combine(above.FullName, marker)))) continue;

            project = above;
            break;
        }

        var pending = new Queue<string>([project.FullName]);
        var lookedThrough = 0;

        while (pending.Count > 0 && lookedThrough < MostFilesLookedThrough)
        {
            var folder = pending.Dequeue();
            if (HoldsModule(folder, module))
                return File.Exists(Path.Combine(folder, module + ".py")) ? Path.Combine(folder, module + ".py") : Path.Combine(folder, module);

            try
            {
                lookedThrough += Directory.EnumerateFiles(folder).Count();
                foreach (var inside in Directory.EnumerateDirectories(folder))
                {
                    var name = Path.GetFileName(inside);
                    if (ProgramCopy.IsCopied(name) && !name.StartsWith('.') && !File.Exists(Path.Combine(inside, "pyvenv.cfg"))) pending.Enqueue(inside);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    /// <summary>Whether a folder holds a module by the name: its .py file, or a folder of Python files - a package.</summary>
    private static bool HoldsModule(string folder, string module)
    {
        try
        {
            var package = Path.Combine(folder, module);
            return File.Exists(Path.Combine(folder, module + ".py")) ||
                   Directory.Exists(package) && Directory.EnumerateFiles(package, "*.py").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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
