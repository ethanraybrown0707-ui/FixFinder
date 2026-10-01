using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Execution.Libraries;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Checking;

/// <summary>
/// Compile errors that come from a library the program uses not being there - "package org.junit.jupiter.api does not
/// exist", the "cannot find symbol" errors for what the program imports from it, and uses of what such a library would
/// write into the program's own classes, such as the getters Lombok adds - told apart from mistakes in the code, and said
/// once for what they are: the library, named, and where FixFinder looked for it.
/// </summary>
/// <remarks>
/// A package is taken to come from a library only when it is not Java's and not the program's own, and is not a letter
/// or two from one of those - java.utils and uni.dss are typing mistakes, and stay errors in the code. Nor is a one-word
/// package that nothing imports: javac takes Sytem.out and system.out for a class "out" in packages called Sytem and
/// system, and both are mistypings of System.
/// </remarks>
public static partial class LibraryErrors
{
    /// <summary>The errors that are mistakes in the code, and the note that says what the rest were.</summary>
    /// <param name="NotBuilt">Why the program was not built, when the errors set aside were the only ones - or null for a library not here.</param>
    public sealed record Sorted(IReadOnlyList<ParsedError> CodeErrors, string? Note, int FromLibraries, string? NotBuilt = null);

    /// <summary>javac's error for a module that a module-info.java requires and is nowhere to be found.</summary>
    [GeneratedRegex(@"^module not found: (?<module>[\w.]+)")]
    private static partial Regex ModuleNotFound();

    [GeneratedRegex(@"^package (?<package>[\w.]+) does not exist")]
    private static partial Regex PackageDoesNotExist();

    [GeneratedRegex(@"^cannot find symbol \(symbol:\s*(?<kind>class|interface|method|variable|static)\s+(?<name>[\w$]+)")]
    private static partial Regex CannotFindSymbol();

    /// <summary>Where javac looked for a symbol: inside a type - "location: class Person" - or on a variable of one.</summary>
    [GeneratedRegex(@"location:\s*(?:(?<inside>class|interface|enum|record)\s+|variable\s+[\w$]+\s+of\s+type\s+)(?<type>[\w$.]+)")]
    private static partial Regex SymbolLocation();

    [GeneratedRegex(@"^(?:constructor [\w$]+ in (?:class|enum|record) (?<type>[\w$.]+) cannot be applied to given types|no suitable constructor found for (?<type>[\w$.]+)\()")]
    private static partial Regex ConstructorNotFound();

    [GeneratedRegex(@"^\s*import\s+(?<static>static\s+)?(?<path>[\w.]+(?:\.\*)?)\s*;")]
    private static partial Regex Import();

    /// <summary>An annotation where it is used: @Data, or @lombok.Data - never a Javadoc tag, which starts with a small letter.</summary>
    [GeneratedRegex(@"(?<![\w$@.])@(?<name>(?:[a-z_$][\w$]*\.)*[A-Z][\w$]*)")]
    private static partial Regex AnnotationUse();

    [GeneratedRegex(@"\b(?:class|interface|enum|record)\s+(?<name>[A-Z_$][\w$]*)")]
    private static partial Regex TypeDeclaration();

    /// <summary>The packages under javax that are Java's own; javax.servlet, javax.persistence and javax.xml.bind are libraries.</summary>
    private static readonly string[] JavaxInTheJdk =
    [
        "javax.swing", "javax.sql", "javax.naming", "javax.net", "javax.crypto", "javax.imageio", "javax.sound", "javax.print",
        "javax.script", "javax.xml.parsers", "javax.xml.transform", "javax.xml.stream", "javax.xml.validation", "javax.xml.xpath",
        "javax.xml.datatype", "javax.xml.namespace", "javax.xml.catalog", "javax.xml.crypto", "javax.management",
        "javax.lang.model", "javax.annotation.processing", "javax.tools", "javax.security", "javax.accessibility",
        "javax.transaction.xa", "javax.rmi.ssl", "javax.smartcardio",
    ];

    /// <summary>Java's own annotations, which need no import: an annotation with no import is not taken from a library for these.</summary>
    private static readonly HashSet<string> JavaLangAnnotations = new(StringComparer.Ordinal) { "Override", "Deprecated", "SuppressWarnings", "FunctionalInterface", "SafeVarargs" };

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

    /// <summary>
    /// Fields a library is known to add to a class marked with its annotations, by the package the annotation is from:
    /// Lombok's @Slf4j, @Log and their like add a logger named log.
    /// </summary>
    private static readonly (string Package, string Field)[] KnownAddedFields = [("lombok", "log")];

    public static Sorted Sort(IReadOnlyList<ParsedError> errors, string chosen)
    {
        if (!chosen.EndsWith(".java", StringComparison.OrdinalIgnoreCase) || errors.Count == 0) return new Sorted(errors, null, 0);

        var libraries = JavaLibraries.For(chosen);

        // The code of the other projects of its build is compiled with the program's, from source, as one module - so a module
        // another of them is never there for the program to require.
        if (libraries.ProjectsUsed.Count > 0 && errors.Where(error => ModuleNotFound().IsMatch(error.Message ?? "")).ToList() is { Count: > 0 } notFound)
            return ModulesOfProjectsNotBuilt(errors, notFound, libraries);

        var ownPackages = OwnPackages(chosen, libraries);
        var importsByFile = new Dictionary<string, IReadOnlyList<(string Path, bool Static)>>(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<(string Path, bool Static)> ImportsFor(ParsedError error) =>
            FileOf(error, chosen) is not { } file ? [] : importsByFile.TryGetValue(file, out var known) ? known : importsByFile[file] = ImportsIn(file);

        var absent = errors
            .Select(error => (Error: error, Named: PackageDoesNotExist().Match(error.Message ?? "")))
            .Where(missing => missing.Named.Success)
            .GroupBy(missing => missing.Named.Groups["package"].Value, missing => missing.Error, StringComparer.Ordinal)
            .Where(package => FromALibrary(package.Key, ownPackages, imported: package.Any(error => ImportsFor(error).Any(import => Within(import.Path, package.Key)))))
            .Select(package => package.Key)
            .ToList();

        var projectsMissing = libraries.MissingProjects.Count > 0;
        var notBuilt = projectsMissing ? "It uses code FixFinder could not find, so it was not built" : null;

        // A project's code that was not there may have held what the code uses in a package of the program's own - so a
        // class javac cannot find there may not be a mistake. The errors stay, with that said beside them.
        if (absent.Count == 0)
        {
            return projectsMissing
                ? new Sorted(errors, $"{ProjectsNotFound(libraries)} So an error about a class or method from that code may not be a mistake in the code.", 0, notBuilt)
                : new Sorted(errors, null, 0);
        }

        var marked = new Lazy<MarkedClasses>(() => MarkedBy(absent, chosen, libraries));
        var fromLibraries = new List<ParsedError>();
        var inTheCode = new List<ParsedError>();
        var addedTo = new List<string>();

        foreach (var error in errors)
        {
            if (CausedBy(error, ImportsFor(error), absent, chosen))
            {
                fromLibraries.Add(error);
            }
            else if (WouldBeAddedTo(error, marked) is { } type)
            {
                fromLibraries.Add(error);
                addedTo.Add(type);
            }
            else
            {
                inTheCode.Add(error);
            }
        }

        return new Sorted(inTheCode, Note(absent, libraries, fromLibraries.Count, addedTo), fromLibraries.Count, notBuilt);
    }

    /// <summary>
    /// The errors of a program that is a module and uses other projects of its Gradle build, with javac's errors for the
    /// modules it could not find set aside. A module another project declares is not there to be found, as that project's
    /// code is compiled as part of the program's own module; a module named nowhere may be the name the build gives one of
    /// those projects as a jar, or may be mistyped, which cannot be told here - so neither is shown as a mistake in the code.
    /// </summary>
    private static Sorted ModulesOfProjectsNotBuilt(IReadOnlyList<ParsedError> errors, IReadOnlyList<ParsedError> notFound, JavaLibraries libraries)
    {
        var modules = notFound.Select(error => ModuleNotFound().Match(error.Message!).Groups["module"].Value).Distinct(StringComparer.Ordinal).ToList();
        var ofProjects = modules.Where(libraries.ProjectModules.ContainsKey).ToList();
        var namedNowhere = modules.Except(ofProjects).ToList();

        var said = new List<string>();

        if (ofProjects.Count > 0)
        {
            said.Add($"javac could not find {And(ofProjects.Select(module => $"the module {module}, which the project {libraries.ProjectModules[module]} of its Gradle build declares").ToList())}.");
        }

        if (namedNowhere.Count > 0)
        {
            var one = namedNowhere.Count == 1;
            var projects = libraries.ProjectsUsed.Count == 1 ? $"the project {libraries.ProjectsUsed[0]}" : $"one of the projects {And(libraries.ProjectsUsed)}";
            var orALibrary = libraries.Missing.Count > 0 ? ", or a library's that is not on this computer" : "";

            said.Add($"javac could not find the module{(one ? "" : "s")} {And(namedNowhere)}, which no project or library FixFinder found declares: " +
                     $"{(one ? "it" : "they")} may be the name the build gives {projects}{orALibrary}, or mistyped, which cannot be told here.");
        }

        said.Add("FixFinder builds a program with the code of the other projects of its build from their source, compiled with its own as one module, " +
                 "so another project's module is never there to be required. So the program could not be built here, and javac's " +
                 $"error{(notFound.Count == 1 ? " that the module is" : "s that the modules are")} not found {(notFound.Count == 1 ? "is" : "are")} not shown as " +
                 "a mistake in the code. The code was still read for mistakes.");

        return new Sorted(errors.Except(notFound).ToList(), string.Join(" ", said), notFound.Count,
            "It needs another project's module, which FixFinder cannot build with it, so it was not built");
    }

    /// <summary>What to say of the projects of its Gradle build a program uses whose code FixFinder could not find.</summary>
    private static string ProjectsNotFound(JavaLibraries libraries) => libraries.MissingProjects switch
    {
        [var only] => $"The Gradle build names the project {only.Path}, but {only.Reason}, so its code was not there to build the program with.",
        var several => $"The Gradle build names projects whose code was not there to build the program with: {And(several.Select(missing => missing.ToString()).ToList())}.",
    };

    /// <summary>Whether an error is about a missing library: its package, an import of it, or a name imported from it.</summary>
    private static bool CausedBy(ParsedError error, IReadOnlyList<(string Path, bool Static)> imported, IReadOnlyList<string> absent, string chosen)
    {
        var message = error.Message ?? "";

        if (PackageDoesNotExist().Match(message) is { Success: true } package) return absent.Contains(package.Groups["package"].Value, StringComparer.Ordinal);

        bool FromAbsent(string path) => absent.Any(missing => Within(path, missing));

        if (message.StartsWith("static import only from classes and interfaces", StringComparison.Ordinal))
            return FileOf(error, chosen) is { } file && FrameOf(error)?.Line is { } number &&
                   LineOf(file, number) is { } text && Import().Match(text) is { Success: true } import && FromAbsent(import.Groups["path"].Value);

        if (CannotFindSymbol().Match(message) is not { Success: true } symbol) return false;

        var name = symbol.Groups["name"].Value;
        return imported.Any(import => FromAbsent(import.Path) &&
            (import.Path.EndsWith("." + name, StringComparison.Ordinal) || import.Path.EndsWith(".*", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The class an error is about when it uses what a missing library would write into that class - a getter or builder()
    /// Lombok adds to a class marked @Data or @Builder, the constructor @AllArgsConstructor writes, the logger @Slf4j adds,
    /// a class written beside it such as Person.PersonBuilder - or null when it is about anything else. Only a class
    /// declared in a file that uses the missing library's annotations counts, and only for what such a library can add: a
    /// mistyped variable, or a class named nothing like the marked one, stays a mistake in the code.
    /// </summary>
    private static string? WouldBeAddedTo(ParsedError error, Lazy<MarkedClasses> marked)
    {
        var message = error.Message ?? "";

        string? IfMarked(string written) => marked.Value.Types.Contains(SimpleName(written)) ? SimpleName(written) : null;

        if (ConstructorNotFound().Match(message) is { Success: true } constructor) return IfMarked(constructor.Groups["type"].Value);

        if (CannotFindSymbol().Match(message) is not { Success: true } symbol || SymbolLocation().Match(message) is not { Success: true } location) return null;

        var name = symbol.Groups["name"].Value;
        var type = SimpleName(location.Groups["type"].Value);

        var couldBeAdded = symbol.Groups["kind"].Value switch
        {
            "method" => true,
            "class" or "interface" => name.Contains(type, StringComparison.Ordinal),
            "variable" => location.Groups["inside"].Success && marked.Value.AddedFields.Contains($"{type}.{name}"),
            _ => false,
        };

        return couldBeAdded ? IfMarked(type) : null;
    }

    /// <summary>
    /// The program's classes declared in files that use an annotation from a missing package, and the fields such a
    /// library is known to add to them, as "Type.field".
    /// </summary>
    private sealed record MarkedClasses(IReadOnlySet<string> Types, IReadOnlySet<string> AddedFields);

    private static MarkedClasses MarkedBy(IReadOnlyList<string> absent, string chosen, JavaLibraries libraries)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        var addedFields = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in SourceRoots(chosen, libraries).SelectMany(JavaFilesIn))
        {
            var text = ReadOrEmpty(file);
            var annotationPackages = MissingAnnotationPackagesIn(text, absent);
            if (annotationPackages.Count == 0) continue;

            var declared = TypeDeclaration().Matches(text).Select(declaration => declaration.Groups["name"].Value).ToList();
            types.UnionWith(declared);

            foreach (var (package, field) in KnownAddedFields.Where(known => annotationPackages.Any(used => Within(used, known.Package))))
                addedFields.UnionWith(declared.Select(type => $"{type}.{field}"));
        }

        return new MarkedClasses(types, addedFields);
    }

    /// <summary>
    /// The missing packages a file's annotations come from: an annotation imported from one by name, one written with its
    /// package as @lombok.Data, or - under an import of a whole missing package - one imported from nowhere else and not
    /// one of Java's own.
    /// </summary>
    private static HashSet<string> MissingAnnotationPackagesIn(string text, IReadOnlyList<string> absent)
    {
        bool Missing(string package) => absent.Any(missing => Within(package, missing));

        var imports = text.Split('\n').Select(line => Import().Match(line))
            .Where(import => import.Success && !import.Groups["static"].Success)
            .Select(import => import.Groups["path"].Value)
            .ToList();

        var packageOfImportedName = imports.Where(path => !path.EndsWith(".*", StringComparison.Ordinal) && path.Contains('.'))
            .GroupBy(path => path[(path.LastIndexOf('.') + 1)..], StringComparer.Ordinal)
            .ToDictionary(named => named.Key, named => named.Last()[..named.Last().LastIndexOf('.')], StringComparer.Ordinal);

        var wholeMissingPackage = imports.Where(path => path.EndsWith(".*", StringComparison.Ordinal)).Select(path => path[..^2]).FirstOrDefault(Missing);

        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in AnnotationUse().Matches(text).Select(annotation => annotation.Groups["name"].Value))
        {
            var package = name.Contains('.') ? name[..name.LastIndexOf('.')]
                : packageOfImportedName.TryGetValue(name, out var imported) ? imported
                : JavaLangAnnotations.Contains(name) ? null
                : wholeMissingPackage;

            if (package is not null && Missing(package)) used.Add(package);
        }

        return used;
    }

    /// <summary>The note that stands for the errors a missing library caused, naming it and saying where it was looked for.</summary>
    /// <param name="addedTo">For each error that uses what the library would add to a class of the program, that class.</param>
    private static string Note(IReadOnlyList<string> absent, JavaLibraries libraries, int errors, IReadOnlyList<string> addedTo)
    {
        var packages = And(absent);
        var libraryNames = absent.Select(LibraryOf).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var named = And(libraryNames);
        var one = absent.Count == 1;
        var oneLibrary = absent.Select(package => LibraryOf(package) ?? package).Distinct(StringComparer.Ordinal).Count() == 1;

        // With a project of the build not found, what is missing may be that project's code rather than a library's.
        var projectsMissing = libraries.MissingProjects.Count > 0;
        var ofThisProgram = projectsMissing ? "of the code FixFinder found for this program" : "of this program";

        var comesFrom = named.Length > 0 ? $"{(one ? "it comes" : "they come")} from a library - {named}"
            : projectsMissing ? $"{(one ? "it" : "they")} may be part of a project of its Gradle build that FixFinder could not find, or come from a library"
            : $"{(one ? "it comes" : "they come")} from a library";

        var whereLibraries = libraries switch
        {
            { Missing.Count: > 0, DeclaredIn: { } declared } =>
                $"{And(libraries.Missing.Select(missing => missing.Name).Distinct(StringComparer.Ordinal).Take(6).ToList())} " +
                $"{(libraries.Missing.Count == 1 ? "is" : "are")} named in {declared} but not on this computer: {libraries.Missing[0].Reason}. Opening the " +
                "project in its IDE, or building it once with its build tool, downloads what it needs - FixFinder never downloads anything itself.",
            { MissingProjects.Count: > 0 } => "",
            { DeclaredIn: { } declared } =>
                $"None of the libraries from {declared} has {(one ? "it" : "them")}, so the library {(one ? "it comes" : "they come")} from is not named there.",
            _ =>
                "Nothing says where that library is: FixFinder looks for a pom.xml or a build.gradle, jars in a lib, libs or jars folder, and the " +
                "libraries IntelliJ, Eclipse and VS Code record for the project, and found none of them.",
        };

        var where = string.Join(" ", new[] { projectsMissing ? ProjectsNotFound(libraries) : "", whereLibraries }.Where(part => part.Length > 0));

        var unread = libraries is { NotRead: [var first, ..], DeclaredIn: { } file }
            ? $" FixFinder could not read {Count(libraries.NotRead.Count, "line")} of {file}, which may be where {(one ? "it is" : "they are")} named: {first}."
            : "";

        var theErrors = $"the {errors} error{(errors == 1 ? "" : "s")} javac gave because of it {(errors == 1 ? "is" : "are")}";

        if (addedTo.Count == 0)
        {
            return $"{packages} {(one ? "is" : "are")} not part of Java or {ofThisProgram}: {comesFrom}. {where}{unread} So the program could not be built, and " +
                   $"{theErrors} not mistakes in the code and {(errors == 1 ? "is" : "are")} not shown as such. The code was still read for mistakes.";
        }

        var classes = addedTo.Distinct(StringComparer.Ordinal).ToList();
        var oneClass = classes.Count == 1;
        var uses = addedTo.Count == 1 ? "1 of them uses" : $"{addedTo.Count} of them use";

        var added = $"{uses} what {(oneLibrary ? "that library" : "those libraries")} would add to {And(classes)}, which {(oneClass ? "is" : "are")} " +
                    $"declared in {(oneClass ? "a file" : "files")} that use{(oneClass ? "s" : "")} {(oneLibrary ? "its" : "their")} annotations - a getter, " +
                    $"say, or a constructor. Javac cannot see what {(oneLibrary ? "it adds" : "they add")} without {(oneLibrary ? "it" : "them")}, so whether " +
                    $"{(addedTo.Count == 1 ? "that use is" : "those uses are")} right cannot be told until {(oneLibrary ? "it is" : "they are")} on this computer.";

        return $"{packages} {(one ? "is" : "are")} not part of Java or {ofThisProgram}: {comesFrom}. {where}{unread} So the program could not be built, and " +
               $"{theErrors} not shown as mistakes in the code. {added} The code was still read for mistakes.";
    }

    private static string? LibraryOf(string package) =>
        KnownLibraries.FirstOrDefault(known => Within(package, known.Package)).Library;

    /// <summary>
    /// Whether a package that javac says does not exist comes from a library: not Java's own, not the program's own, and
    /// not a letter or two away from either, which would make it a typing mistake. One that nothing imports, written in
    /// the code as one word or as a word with a capital - Sytem.out, system.out - is a mistyped name, not a package at all.
    /// </summary>
    private static bool FromALibrary(string package, IReadOnlySet<string> own, bool imported)
    {
        if (LibraryOf(package) is not null) return true;
        if (!imported && (!package.Contains('.') || package.Split('.').Any(segment => char.IsUpper(segment[0])))) return false;
        if (package.StartsWith("java.", StringComparison.Ordinal) || package.StartsWith("jdk.", StringComparison.Ordinal) || package.StartsWith("sun.", StringComparison.Ordinal))
            return false;

        var nearby = own.Concat(JavaxInTheJdk);
        if (JavaxInTheJdk.Any(jdk => Within(package, jdk))) return false;

        return !nearby.Any(known => Distance(package, known) <= 2 || known.StartsWith(package + ".", StringComparison.Ordinal) || package.StartsWith(known + ".", StringComparison.Ordinal));
    }

    /// <summary>Whether a name is this package, or something in it: lombok.Data is within lombok.</summary>
    private static bool Within(string name, string package) =>
        name == package || name.StartsWith(package + ".", StringComparison.Ordinal);

    private static string SimpleName(string written) => written[(written.LastIndexOf('.') + 1)..];

    /// <summary>The packages the program's own source is in, from the folders its .java files are in under each source root.</summary>
    private static HashSet<string> OwnPackages(string chosen, JavaLibraries libraries)
    {
        var packages = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in SourceRoots(chosen, libraries))
        {
            foreach (var file in JavaFilesIn(root))
            {
                var folder = Path.GetRelativePath(root, Path.GetDirectoryName(file)!);
                if (folder != ".") packages.Add(folder.Replace(Path.DirectorySeparatorChar, '.'));
            }
        }

        return packages;
    }

    private static IEnumerable<string> SourceRoots(string chosen, JavaLibraries libraries) =>
        new[] { ProgramLayout.JavaSourceRoot(chosen) }.Concat(libraries.OtherSourceRoots(chosen));

    /// <summary>The .java files under a source root - the first 2000, which is more than any course project has.</summary>
    private static IReadOnlyList<string> JavaFilesIn(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*.java", SearchOption.AllDirectories).Take(2000).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static ErrorFrame? FrameOf(ParsedError error) => error.CulpritFrame ?? error.Frames.FirstOrDefault();

    /// <summary>The file an error is in, as a full path - javac names a file as it was given, which may be from where it ran.</summary>
    private static string? FileOf(ParsedError error, string chosen) => FrameOf(error)?.File is { } file
        ? Path.IsPathRooted(file) ? file : Path.Combine(Path.GetDirectoryName(chosen)!, file)
        : null;

    private static IReadOnlyList<(string Path, bool Static)> ImportsIn(string file)
    {
        try
        {
            return File.ReadLines(file).Take(400)
                .Select(line => Import().Match(line)).Where(match => match.Success)
                .Select(match => (match.Groups["path"].Value, match.Groups["static"].Success)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static string ReadOrEmpty(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "";
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
