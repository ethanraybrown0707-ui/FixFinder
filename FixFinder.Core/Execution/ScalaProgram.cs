using System.Text.RegularExpressions;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Execution;

/// <summary>
/// The files a Scala program is built from, the folder it starts in, and the main it runs - laid out as sbt and Scala CLI
/// lay a program out.
/// </summary>
/// <remarks>
/// <para>
/// A file under an sbt project's <c>src/main/scala</c> is built with every Scala file there, as sbt builds them, and the
/// program starts in the project's folder - the one holding build.sbt - where sbt runs it from.
/// </para>
/// <para>
/// Anywhere else, the program is the chosen file and the Scala files beside it that its code uses, and so on through the
/// files those use: a folder of exercises, each with an <c>object Main</c> of its own, holds several programs, and a file
/// that defines a name the chosen file defines too is another of them, so it is left out rather than reported as defining
/// that name twice. A Scala CLI script (<c>.sc</c>) is a program of its own and is never built into another.
/// </para>
/// </remarks>
public static partial class ScalaProgram
{
    /// <summary>The most files one program is built from: more than this is a whole folder of somebody's work, not a program.</summary>
    public const int MostFiles = 200;

    /// <summary>A program: its files - the chosen one first - the folder it starts in, and the main class to run when it has several.</summary>
    /// <param name="MainClass">The chosen file's main, named only when the program's files hold more than one main.</param>
    /// <param name="BuildFile">The sbt build whose sources these are, when it is one.</param>
    public sealed record Layout(IReadOnlyList<string> Files, string Folder, string? MainClass, string? BuildFile);

    /// <summary>Folders that hold what was built or fetched, or an editor's settings, rather than the program's source.</summary>
    private static readonly HashSet<string> NotSource = new(StringComparer.OrdinalIgnoreCase)
    {
        "target", "project", ".scala-build", ".bsp", ".bloop", ".metals", ".idea", ".vscode", ".git", "out", "bin", "obj", "node_modules",
    };

    /// <summary>A class, trait, object, enum or type declared, with whatever modifiers come before it.</summary>
    [GeneratedRegex(@"(?m)^[ \t]*(?:(?:private|protected|sealed|abstract|final|implicit|case|open|transparent|inline|opaque|lazy)\s+)*(?:class|object|trait|enum|type)\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex TypeDeclaration();

    /// <summary>A definition at the very start of a line - top-level in a Scala 3 file, where a def or val needs no object around it.</summary>
    [GeneratedRegex(@"(?m)^(?:(?:private|inline|transparent|lazy)\s+)*(?:def|val|var|given)\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex TopLevelDefinition();

    [GeneratedRegex(@"(?m)^[ \t]*package\s+(?<name>[A-Za-z_][\w.]*)\s*$")]
    private static partial Regex PackageClause();

    /// <summary>Scala 3's <c>@main def greet()</c>: a main of its own, run as the class the method is named after.</summary>
    [GeneratedRegex(@"@main\s+def\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex MainAnnotation();

    [GeneratedRegex(@"\bobject\s+(?<name>[A-Za-z_]\w*)\s+extends\s+App\b")]
    private static partial Regex ObjectExtendingApp();

    [GeneratedRegex(@"\bdef\s+main\s*\(\s*\w+\s*:\s*Array\s*\[\s*String\s*\]\s*\)")]
    private static partial Regex MainMethod();

    [GeneratedRegex(@"\bobject\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex ObjectDeclaration();

    [GeneratedRegex(@"[A-Za-z_]\w*")]
    private static partial Regex Word();

    public static bool IsScala(string file) => Path.GetExtension(file).ToLowerInvariant() is ".scala" or ".sc";

    public static bool IsScript(string file) => Path.GetExtension(file).Equals(".sc", StringComparison.OrdinalIgnoreCase);

    public static Layout Of(string chosen)
    {
        var file = Path.GetFullPath(chosen);

        if (SbtSourceRoot(file) is { } sourceRoot)
        {
            var project = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(sourceRoot)!)!)!;
            var buildFile = Path.Combine(project, "build.sbt");
            var sources = ScalaFilesUnder(sourceRoot).Where(other => !IsScript(other));
            var files = OwnFirst(file, sources);

            return new Layout(files, project, MainClassIfSeveral(file, files), File.Exists(buildFile) ? buildFile : null);
        }

        var folder = Path.GetDirectoryName(file)!;
        var reached = Reached(file, ScalaFilesBeside(file));

        return new Layout(reached, folder, MainClassIfSeveral(file, reached), null);
    }

    /// <summary>The sbt <c>src/main/scala</c> a file is under - or null when it is not in one.</summary>
    public static string? SbtSourceRoot(string file)
    {
        for (var folder = Path.GetDirectoryName(file); folder is not null; folder = Path.GetDirectoryName(folder))
        {
            if (!Path.GetFileName(folder).Equals("scala", StringComparison.OrdinalIgnoreCase)) continue;

            var main = Path.GetDirectoryName(folder);
            var src = main is null ? null : Path.GetDirectoryName(main);

            if (main is not null && src is not null &&
                Path.GetFileName(main).Equals("main", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(src).Equals("src", StringComparison.OrdinalIgnoreCase))
                return folder;
        }

        return null;
    }

    /// <summary>The Scala files beside the chosen one - not in the folders under it, which hold other programs as often as parts of this one.</summary>
    private static IReadOnlyList<string> ScalaFilesBeside(string file)
    {
        try
        {
            return Directory.EnumerateFiles(Path.GetDirectoryName(file)!, "*.scala")
                .Where(other => !other.Equals(file, StringComparison.OrdinalIgnoreCase))
                .Take(MostFiles)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Every Scala file in a source folder and the folders under it, short of those that hold built or fetched files.</summary>
    private static IReadOnlyList<string> ScalaFilesUnder(string sourceRoot)
    {
        var found = new List<string>();
        var waiting = new Stack<string>([sourceRoot]);

        while (waiting.Count > 0 && found.Count < MostFiles)
        {
            var folder = waiting.Pop();

            try
            {
                found.AddRange(Directory.EnumerateFiles(folder, "*.scala").Take(MostFiles - found.Count));

                foreach (var inner in Directory.EnumerateDirectories(folder))
                    if (!NotSource.Contains(Path.GetFileName(inner)) && !Path.GetFileName(inner).StartsWith('.')) waiting.Push(inner);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return found;
    }

    private static IReadOnlyList<string> OwnFirst(string file, IEnumerable<string> sources) =>
        [file, .. sources.Where(other => !other.Equals(file, StringComparison.OrdinalIgnoreCase))];

    /// <summary>
    /// The chosen file and the files beside it that its code uses, and the files those use - nearest first. A file that
    /// defines a name the chosen file defines too belongs to another program, so it is never reached.
    /// </summary>
    private static IReadOnlyList<string> Reached(string chosen, IReadOnlyList<string> beside)
    {
        var program = new List<string> { chosen };
        if (IsScript(chosen) && beside.Count == 0) return program;

        var definedBy = beside.ToDictionary(file => file, file => NamesDefinedIn(TextOf(file)), StringComparer.OrdinalIgnoreCase);
        var chosenDefines = NamesDefinedIn(TextOf(chosen));

        for (var next = 0; next < program.Count; next++)
        {
            var used = WordsUsedIn(TextOf(program[next]));

            foreach (var (other, names) in definedBy)
            {
                if (program.Contains(other, StringComparer.OrdinalIgnoreCase)) continue;
                if (names.Overlaps(chosenDefines)) continue;
                if (names.Overlaps(used)) program.Add(other);
            }
        }

        return program;
    }

    /// <summary>The names a file defines at its top: its classes, objects, traits, enums and types, and Scala 3's top-level definitions.</summary>
    internal static HashSet<string> NamesDefinedIn(string text)
    {
        var code = CodeText.MaskAll(text.Split('\n'), Syntax.Scala);
        var joined = string.Join("\n", code);

        return TypeDeclaration().Matches(joined).Select(match => match.Groups["name"].Value)
            .Concat(TopLevelDefinition().Matches(joined).Select(match => match.Groups["name"].Value))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Every word in a file's code, outside its strings and comments.</summary>
    private static HashSet<string> WordsUsedIn(string text)
    {
        var code = string.Join("\n", CodeText.MaskAll(text.Split('\n'), Syntax.Scala));
        return Word().Matches(code).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The chosen file's main, when the program's files hold more than one - Scala CLI is told which to run only then.</summary>
    private static string? MainClassIfSeveral(string chosen, IReadOnlyList<string> files)
    {
        if (files.Sum(file => MainsIn(TextOf(file)).Count) < 2) return null;

        return MainsIn(TextOf(chosen)) is [var only] ? only : null;
    }

    /// <summary>
    /// The mains a file holds, each as the class Scala CLI runs it as: an object with <c>def main(args: Array[String])</c>
    /// or that extends App, and a method marked <c>@main</c> - each in its package, when the file has one.
    /// </summary>
    internal static IReadOnlyList<string> MainsIn(string text)
    {
        var code = string.Join("\n", CodeText.MaskAll(text.Split('\n'), Syntax.Scala));
        var package = string.Join(".", PackageClause().Matches(code).Select(match => match.Groups["name"].Value));
        string InPackage(string name) => package.Length > 0 ? $"{package}.{name}" : name;

        var mains = new List<string>();

        foreach (Match annotated in MainAnnotation().Matches(code)) mains.Add(InPackage(annotated.Groups["name"].Value));
        foreach (Match app in ObjectExtendingApp().Matches(code)) mains.Add(InPackage(app.Groups["name"].Value));

        foreach (Match method in MainMethod().Matches(code))
        {
            // The object a main method is in: the last one declared before it.
            if (ObjectDeclaration().Matches(code[..method.Index]).LastOrDefault() is { } owner) mains.Add(InPackage(owner.Groups["name"].Value));
        }

        return mains.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string TextOf(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
