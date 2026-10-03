using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Java a program's own code needs, read from how it is written, so it is built with a JDK that can build it: a compact
/// source file - methods with no class around them - and <c>import module</c> are Java 25's, an unnamed <c>_</c> is Java
/// 22's; and an applet needs a Java no later than 25, as Java 26 took the Applet API out.
/// </summary>
/// <remarks>
/// Which release made each of these part of the language is javac's own record of it - its Source.Feature table - and the
/// Applet API's removal is JEP 504's. Only what a JDK cannot build without is looked for: anything else the code uses,
/// javac itself names when the JDK is too old for it.
/// </remarks>
public sealed partial record JavaFeaturesUsed(int? AtLeast, string? AtLeastBecause, int? AtMost, string? AtMostBecause)
{
    public static readonly JavaFeaturesUsed None = new(null, null, null, null);

    [GeneratedRegex(@"(?m)^\s*import\s+module\s+[\w.]+\s*;")]
    private static partial Regex ModuleImport();

    [GeneratedRegex(@"(?<![\w$])_(?![\w$])")]
    private static partial Regex Unnamed();

    [GeneratedRegex(@"\bjava\s*\.\s*applet\b|\bjavax\s*\.\s*swing\s*\.\s*JApplet\b|\bextends\s+(?:J?Applet)\b")]
    private static partial Regex Applet();

    [GeneratedRegex(@"(?:^|[\s;}])(?:class|interface|enum|record)\s+[A-Za-z_$]|@interface\b")]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(@"^\s*(?:@[\w.]+(?:\s*\([^)]*\))?\s*)*(?:package|import|(?:open\s+)?module)\b")]
    private static partial Regex PackageOrImport();

    private static readonly ConcurrentDictionary<string, (DateTime Written, Needs Needs)> Remembered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What one file needs: whether it is a compact source file, imports a module, names a variable _, or is an applet.</summary>
    private sealed record Needs(bool CompactSource, bool ModuleImport, bool UnnamedVariable, bool Applet);

    /// <summary>What the program this Java file is part of needs: the file and the others of its source folder.</summary>
    public static JavaFeaturesUsed For(string javaFile)
    {
        var chosen = Path.GetFullPath(javaFile);
        var root = ProgramLayout.JavaSourceRoot(chosen);

        IEnumerable<string> others;
        try
        {
            others = Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.java", SearchOption.AllDirectories).Take(500) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            others = [];
        }

        return Of(others.Prepend(chosen).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>What these files need together: the highest Java any of them needs at least, and the lowest any needs at most.</summary>
    public static JavaFeaturesUsed Of(IEnumerable<string> javaFiles)
    {
        var used = javaFiles.Select(file => (File: file, Needs: NeedsOf(file))).Where(each => each.Needs is not null).ToList();

        var compact = used.FirstOrDefault(each => each.Needs!.CompactSource).File;
        var module = used.FirstOrDefault(each => each.Needs!.ModuleImport).File;
        var unnamed = used.FirstOrDefault(each => each.Needs!.UnnamedVariable).File;
        var applet = used.FirstOrDefault(each => each.Needs!.Applet).File;

        var (atLeast, because) =
            compact is not null ? (25, $"{Path.GetFileName(compact)} is a compact source file - methods with no class around them - which Java 25 made part of the language")
            : module is not null ? (25, $"{Path.GetFileName(module)} uses import module, which Java 25 made part of the language")
            : unnamed is not null ? (22, $"{Path.GetFileName(unnamed)} names a variable _, which Java 22 made part of the language")
            : ((int?)null, (string?)null);

        return new JavaFeaturesUsed(
            atLeast, because,
            applet is null ? null : 25,
            applet is null ? null : $"{Path.GetFileName(applet)} is an applet, and Java 26 took the Applet API out of Java");
    }

    private static Needs? NeedsOf(string file)
    {
        try
        {
            var written = File.GetLastWriteTimeUtc(file);
            if (Remembered.TryGetValue(file, out var known) && known.Written == written) return known.Needs;

            var masked = CodeText.MaskAll(File.ReadAllLines(file), Syntax.CLike);
            var text = string.Join("\n", masked);

            // A module's module-info.java and a package's package-info.java declare no class, and are not programs.
            var declaresAPackageOrModule = Path.GetFileName(file) is "module-info.java" or "package-info.java";

            var needs = new Needs(!declaresAPackageOrModule && IsCompactSource(text), ModuleImport().IsMatch(text), Unnamed().IsMatch(text), Applet().IsMatch(text));
            Remembered[file] = (written, needs);
            return needs;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the file is a compact source file: something at its top level - outside every brace - that is neither its
    /// package, an import, nor a class, interface, enum or record. A method or a field written there is one.
    /// </summary>
    internal static bool IsCompactSource(string masked)
    {
        var depth = 0;
        var unit = new System.Text.StringBuilder();

        bool IsMember(string written)
        {
            var trimmed = written.Trim();
            return trimmed.Length > 0 && !PackageOrImport().IsMatch(trimmed) && !TypeDeclaration().IsMatch(" " + trimmed);
        }

        foreach (var character in masked)
        {
            if (depth == 0)
            {
                if (character == ';')
                {
                    if (IsMember(unit.ToString())) return true;
                    unit.Clear();
                    continue;
                }

                if (character == '{')
                {
                    if (IsMember(unit.ToString())) return true;
                    unit.Clear();
                    depth++;
                    continue;
                }

                unit.Append(character);
                continue;
            }

            if (character == '{') depth++;
            else if (character == '}' && --depth == 0) unit.Clear();
        }

        return false;
    }
}
