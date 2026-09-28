using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Execution.Libraries;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Checking;

/// <summary>
/// Compile errors that come from a library the program uses not being there - "package org.junit.jupiter.api does not
/// exist", and the "cannot find symbol" errors for what the program imports from it - told apart from mistakes in the code,
/// and said once for what they are: the library, named, and where FixFinder looked for it.
/// </summary>
/// <remarks>
/// A package is taken to come from a library only when it is not Java's and not the program's own, and is not a letter
/// or two from one of those - java.utils and uni.dss are typing mistakes, and stay errors in the code.
/// </remarks>
public static partial class LibraryErrors
{
    /// <summary>The errors that are mistakes in the code, and the note that says what the rest were.</summary>
    public sealed record Sorted(IReadOnlyList<ParsedError> CodeErrors, string? Note, int FromLibraries);

    [GeneratedRegex(@"^package (?<package>[\w.]+) does not exist")]
    private static partial Regex PackageDoesNotExist();

    [GeneratedRegex(@"^cannot find symbol \(symbol:\s*(?:class|interface|method|variable|static)\s+(?<name>[\w$]+)")]
    private static partial Regex CannotFindSymbol();

    [GeneratedRegex(@"^\s*import\s+(?<static>static\s+)?(?<path>[\w.]+(?:\.\*)?)\s*;")]
    private static partial Regex Import();

    /// <summary>The packages under javax that are Java's own; javax.servlet, javax.persistence and javax.xml.bind are libraries.</summary>
    private static readonly string[] JavaxInTheJdk =
    [
        "javax.swing", "javax.sql", "javax.naming", "javax.net", "javax.crypto", "javax.imageio", "javax.sound", "javax.print",
        "javax.script", "javax.xml.parsers", "javax.xml.transform", "javax.xml.stream", "javax.xml.validation", "javax.xml.xpath",
        "javax.xml.datatype", "javax.xml.namespace", "javax.xml.catalog", "javax.xml.crypto", "javax.management",
        "javax.lang.model", "javax.annotation.processing", "javax.tools", "javax.security", "javax.accessibility",
        "javax.transaction.xa", "javax.rmi.ssl", "javax.smartcardio",
    ];

    /// <summary>Libraries by the packages they are known for, to name the library a missing package comes from.</summary>
    private static readonly (string Package, string Library)[] KnownLibraries =
    [
        ("org.junit.jupiter", "JUnit 5"), ("org.junit.platform", "JUnit 5's platform"), ("org.junit", "JUnit 4"), ("junit.framework", "JUnit 3"),
        ("org.mockito", "Mockito"), ("org.hamcrest", "Hamcrest"), ("org.assertj", "AssertJ"), ("com.google.gson", "Gson"),
        ("com.google.common", "Guava"), ("com.fasterxml.jackson", "Jackson"), ("org.apache.commons.lang3", "Apache Commons Lang"),
        ("org.apache.commons.io", "Apache Commons IO"), ("org.apache.commons.csv", "Apache Commons CSV"), ("org.springframework", "Spring"),
        ("javafx", "JavaFX"), ("lombok", "Lombok"), ("org.slf4j", "SLF4J"), ("org.apache.logging.log4j", "Log4j"),
        ("com.mysql", "MySQL Connector/J"), ("org.sqlite", "the SQLite JDBC driver"), ("org.postgresql", "the PostgreSQL JDBC driver"),
        ("org.jsoup", "jsoup"), ("org.json", "JSON-java"), ("javax.servlet", "the Servlet API"), ("jakarta.servlet", "Jakarta Servlet"),
        ("javax.persistence", "JPA"), ("jakarta.persistence", "Jakarta Persistence"), ("org.hibernate", "Hibernate"),
        ("javax.xml.bind", "JAXB"), ("jakarta.xml.bind", "Jakarta XML Binding"),
    ];

    public static Sorted Sort(IReadOnlyList<ParsedError> errors, string chosen)
    {
        if (!chosen.EndsWith(".java", StringComparison.OrdinalIgnoreCase) || errors.Count == 0) return new Sorted(errors, null, 0);

        var libraries = JavaLibraries.For(chosen);
        var ownPackages = OwnPackages(chosen, libraries);

        var absent = errors.Select(error => PackageDoesNotExist().Match(error.Message ?? "")).Where(match => match.Success)
            .Select(match => match.Groups["package"].Value).Distinct(StringComparer.Ordinal)
            .Where(package => FromALibrary(package, ownPackages)).ToList();

        if (absent.Count == 0) return new Sorted(errors, null, 0);

        var fromLibraries = new List<ParsedError>();
        var inTheCode = new List<ParsedError>();
        var imports = new Dictionary<string, IReadOnlyList<(string Path, bool Static)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var error in errors)
        {
            var (file, line) = (error.CulpritFrame?.File ?? error.Frames.FirstOrDefault()?.File, error.CulpritFrame?.Line ?? error.Frames.FirstOrDefault()?.Line);
            var imported = file is null ? [] : imports.TryGetValue(file, out var known) ? known : imports[file] = ImportsIn(file, chosen);

            (CausedBy(error, line, file, imported, absent) ? fromLibraries : inTheCode).Add(error);
        }

        return new Sorted(inTheCode, Note(absent, libraries, fromLibraries.Count), fromLibraries.Count);
    }

    /// <summary>Whether an error is about a missing library: its package, an import of it, or a name imported from it.</summary>
    private static bool CausedBy(ParsedError error, int? line, string? file, IReadOnlyList<(string Path, bool Static)> imported, IReadOnlyList<string> absent)
    {
        var message = error.Message ?? "";

        if (PackageDoesNotExist().Match(message) is { Success: true } package) return absent.Contains(package.Groups["package"].Value, StringComparer.Ordinal);

        bool FromAbsent(string path) => absent.Any(missing => path == missing || path.StartsWith(missing + ".", StringComparison.Ordinal));

        if (message.StartsWith("static import only from classes and interfaces", StringComparison.Ordinal) && file is not null && line is { } number)
            return LineOf(file, number) is { } text && Import().Match(text) is { Success: true } import && FromAbsent(import.Groups["path"].Value);

        if (CannotFindSymbol().Match(message) is not { Success: true } symbol) return false;

        var name = symbol.Groups["name"].Value;
        return imported.Any(import => FromAbsent(import.Path) &&
            (import.Path.EndsWith("." + name, StringComparison.Ordinal) || import.Path.EndsWith(".*", StringComparison.Ordinal)));
    }

    /// <summary>The note that stands for the errors a missing library caused, naming it and saying where it was looked for.</summary>
    private static string Note(IReadOnlyList<string> absent, JavaLibraries libraries, int errors)
    {
        var packages = And(absent);
        var named = And(absent.Select(LibraryOf).OfType<string>().Distinct(StringComparer.Ordinal).ToList());
        var one = absent.Count == 1;

        var comesFrom = named.Length > 0
            ? $"{(one ? "it comes" : "they come")} from a library - {named}"
            : $"{(one ? "it comes" : "they come")} from a library";

        var where = libraries switch
        {
            { Missing.Count: > 0, DeclaredIn: { } declared } =>
                $"{declared} names {And(libraries.Missing.Select(missing => missing.Name).Distinct(StringComparer.Ordinal).Take(6).ToList())}, which " +
                $"{(libraries.Missing.Count == 1 ? "is" : "are")} not on this computer: {libraries.Missing[0].Reason}. Opening the project in its IDE, " +
                "or building it once with its build tool, downloads what it needs - FixFinder never downloads anything itself.",
            { DeclaredIn: { } declared } =>
                $"None of the libraries {declared} names has {(one ? "it" : "them")}, so {declared} does not name the library {(one ? "it comes" : "they come")} from.",
            _ =>
                "Nothing says where that library is: FixFinder looks for a pom.xml or a build.gradle, jars in a lib, libs or jars folder, and the " +
                "libraries IntelliJ, Eclipse and VS Code record for the project, and found none of them.",
        };

        var unread = libraries is { NotRead: [var first, ..], DeclaredIn: { } file }
            ? $" FixFinder could not read {Count(libraries.NotRead.Count, "line")} of {file}, which may be where {(one ? "it is" : "they are")} named: {first}."
            : "";

        return $"{packages} {(one ? "is" : "are")} not part of Java or of this program: {comesFrom}. {where}{unread} So the program could not be built, and " +
               $"the {errors} error{(errors == 1 ? "" : "s")} javac gave because of it {(errors == 1 ? "is" : "are")} not mistakes in the code and " +
               $"{(errors == 1 ? "is" : "are")} not shown as such. The code was still read for mistakes.";
    }

    private static string? LibraryOf(string package) =>
        KnownLibraries.FirstOrDefault(known => package == known.Package || package.StartsWith(known.Package + ".", StringComparison.Ordinal)).Library;

    /// <summary>
    /// Whether a package that javac says does not exist comes from a library: not Java's own, not the program's own, and
    /// not a letter or two away from either, which would make it a typing mistake.
    /// </summary>
    private static bool FromALibrary(string package, IReadOnlySet<string> own)
    {
        if (LibraryOf(package) is not null) return true;
        if (package.StartsWith("java.", StringComparison.Ordinal) || package.StartsWith("jdk.", StringComparison.Ordinal) || package.StartsWith("sun.", StringComparison.Ordinal))
            return false;

        var nearby = own.Concat(JavaxInTheJdk);
        if (JavaxInTheJdk.Any(jdk => package == jdk || package.StartsWith(jdk + ".", StringComparison.Ordinal))) return false;

        return !nearby.Any(known => Distance(package, known) <= 2 || known.StartsWith(package + ".", StringComparison.Ordinal) || package.StartsWith(known + ".", StringComparison.Ordinal));
    }

    /// <summary>The packages the program's own source is in, from the folders its .java files are in under each source root.</summary>
    private static HashSet<string> OwnPackages(string chosen, JavaLibraries libraries)
    {
        var packages = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in new[] { ProgramLayout.JavaSourceRoot(chosen) }.Concat(libraries.OtherSourceRoots(chosen)))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.java", SearchOption.AllDirectories).Take(2000))
                {
                    var folder = Path.GetRelativePath(root, Path.GetDirectoryName(file)!);
                    if (folder != ".") packages.Add(folder.Replace(Path.DirectorySeparatorChar, '.'));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return packages;
    }

    private static IReadOnlyList<(string Path, bool Static)> ImportsIn(string file, string chosen)
    {
        var path = Path.IsPathRooted(file) ? file : Path.Combine(Path.GetDirectoryName(chosen)!, file);

        try
        {
            return File.ReadLines(path).Take(400)
                .Select(line => Import().Match(line)).Where(match => match.Success)
                .Select(match => (match.Groups["path"].Value, match.Groups["static"].Success)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static string? LineOf(string file, int number)
    {
        try
        {
            return File.ReadLines(file).Skip(number - 1).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string Count(int count, string what) => count == 1 ? $"1 {what}" : $"{count} {what}s";

    private static string And(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
    };

    /// <summary>How many letters must change to turn one package name into another.</summary>
    private static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2) return 3;

        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = current;
        }

        return previous[b.Length];
    }
}
