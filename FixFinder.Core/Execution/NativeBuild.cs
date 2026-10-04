using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution.BuildFiles;

namespace FixFinder.Core.Execution;

/// <summary>
/// How a C or C++ program is built when a Makefile or CMakeLists.txt beside it, or in a folder above it, says how: the
/// sources it is built from, the folders its headers are in, what it defines and links, and the standard it is written to.
/// </summary>
/// <remarks>
/// The build file is read, never run: FixFinder runs neither make nor CMake nor anything they would run, and builds the
/// program itself with the compiler it found, giving that compiler what the build file gives it. What the build file does
/// that only running something could say is named in <see cref="NotFollowed"/> rather than guessed at. When no build
/// file builds the chosen file - or one does but cannot be followed, or builds it into more than one program - the
/// program is built as it would be with no build file, and <see cref="Lookup.Note"/> says why.
/// </remarks>
/// <param name="BuildFile">The Makefile or CMakeLists.txt, as a full path.</param>
/// <param name="Program">The program's name in it.</param>
/// <param name="Sources">Its C or C++ files: the chosen one first, then the rest in the build file's order.</param>
/// <param name="IncludeFolders">The folders its headers are looked for in, in order.</param>
/// <param name="Definitions">What it defines for the preprocessor: NAME or NAME=value.</param>
/// <param name="Libraries">The libraries it links by name: m for -lm.</param>
/// <param name="LibraryFolders">The folders those libraries are looked for in.</param>
/// <param name="LinkedFiles">Library and object files it links by their path.</param>
/// <param name="Threads">Whether it is built with -pthread.</param>
/// <param name="Standard">The -std= it is built with, as gcc names it, when its build file gives one.</param>
/// <param name="NotFollowed">What the build file does for it that FixFinder did not do, each said as part of a sentence.</param>
public sealed partial record NativeBuild(
    string BuildFile,
    string Program,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> IncludeFolders,
    IReadOnlyList<string> Definitions,
    IReadOnlyList<string> Libraries,
    IReadOnlyList<string> LibraryFolders,
    IReadOnlyList<string> LinkedFiles,
    bool Threads,
    string? Standard,
    IReadOnlyList<string> NotFollowed)
{
    /// <summary>How many folders above the chosen file's own a build file is looked for in.</summary>
    private const int FoldersAboveLookedIn = 4;

    /// <summary>How many files are followed through #include when finding which program a header is part of.</summary>
    private const int MostFilesFollowed = 400;

    /// <summary>How long what a build file says is kept before it is read again, even unchanged - for a file added that it finds by a pattern.</summary>
    private static readonly TimeSpan KeptFor = TimeSpan.FromSeconds(30);

    /// <summary>A definition put on a command line only when it is plain: NAME, or NAME= followed by nothing a shell would act on.</summary>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(?:=[A-Za-z0-9_.,:+\-*/()\[\]{} '""]*)?$")]
    private static partial Regex PlainDefinition();

    [GeneratedRegex(@"^[A-Za-z0-9_.+\-]+$")]
    private static partial Regex LibraryName();

    [GeneratedRegex(@"(?m)^\s*#\s*include\s*(?:""(?<quoted>[^""]+)""|<(?<angled>[^>]+)>)")]
    private static partial Regex IncludeLine();

    /// <summary>The build, or why there is none to follow when that is worth saying.</summary>
    public sealed record Lookup(NativeBuild? Build, string? Note);

    /// <summary>What a build file says, kept with the files it was read from and when each was last written.</summary>
    private sealed record Reading(IReadOnlyList<BuiltProgram> Programs, IReadOnlyList<(string File, DateTime Written)> FilesRead, DateTime ReadAt, string? Problem);

    private static readonly ConcurrentDictionary<(string BuildFile, NativeCompilerKind Compiler), Reading> Readings = new();

    public string Folder => Path.GetDirectoryName(BuildFile)!;

    /// <summary>The build file's name as it reads in a sentence: Makefile, or CMakeLists.txt.</summary>
    public string BuildFileName => Path.GetFileName(BuildFile);

    /// <summary>The program a C or C++ file is part of, as the build file nearest above it builds it.</summary>
    public static Lookup For(string source)
    {
        var chosen = Path.GetFullPath(source);
        var original = Path.GetFullPath(ProgramCopy.OriginalOf(chosen));
        var lookup = LookupFor(original);

        return lookup.Build is { } build && !original.Equals(chosen, StringComparison.OrdinalIgnoreCase)
            ? lookup with { Build = build.Translated(path => ProgramCopy.InCopyOf(chosen, path)) }
            : lookup;
    }

    /// <summary>The program a header is part of: the one a build file builds from sources that include it, when there is just one.</summary>
    public static NativeBuild? ForHeader(string header)
    {
        var chosen = Path.GetFullPath(header);
        var original = Path.GetFullPath(ProgramCopy.OriginalOf(chosen));
        var compiler = CompilerFor(Path.GetExtension(original).ToLowerInvariant() is ".hpp" or ".hh" or ".hxx");

        foreach (var buildFile in BuildFilesAbove(original))
        {
            var including = ProgramsIn(buildFile, compiler).Programs.Where(program => program.CannotBeBuilt is null && Includes(program, original)).ToList();
            if (including.Count == 0) continue;

            if (Chosen(including) is not { } program) return null;

            var build = From(program, buildFile, first: null);
            return original.Equals(chosen, StringComparison.OrdinalIgnoreCase) ? build : build.Translated(path => ProgramCopy.InCopyOf(chosen, path));
        }

        return null;
    }

    private static Lookup LookupFor(string source)
    {
        var compiler = CompilerFor(NativeFileKinds.IsCpp(source));
        var name = Path.GetFileName(source);
        string? note = null;

        foreach (var buildFile in BuildFilesAbove(source))
        {
            var reading = ProgramsIn(buildFile, compiler);
            var building = reading.Programs.Where(program => program.Builds(source)).ToList();
            var buildFileName = Path.GetFileName(buildFile);

            if (building.Count == 0)
            {
                if (reading.Problem is { } problem) note ??= $"FixFinder could not follow its {buildFileName}: {problem}. So {name} was built as if there were none.";
                else if (reading.Programs.FirstOrDefault(program => program.CannotBeBuilt is not null) is { } unfollowed)
                    note ??= $"Its {buildFileName} builds {unfollowed.Name}, but {unfollowed.CannotBeBuilt}, so {name} was built as if there were no {buildFileName}.";
                continue;
            }

            if (Chosen(building) is not { } program)
            {
                return new Lookup(null,
                    $"Its {buildFileName} builds {name} into more than one program - {Listed(building.Select(each => each.Name).Distinct(StringComparer.Ordinal).ToList())} - " +
                    $"so it was built as if there were no {buildFileName}. Checking the file with the main of the one wanted builds it as that program.");
            }

            if (program.CannotBeBuilt is { } why)
                return new Lookup(null, $"Its {buildFileName} builds {name} into {program.Name}, but {why}, so it was built as if there were no {buildFileName}.");

            return new Lookup(From(program, buildFile, first: source), null);
        }

        return new Lookup(null, note);
    }

    /// <summary>
    /// The build files that could build a file, in the order they are tried. A CMake project is read from its highest
    /// CMakeLists.txt, which reaches the lower ones through add_subdirectory; a lower one is tried after, as a project of
    /// its own. Makefiles are tried nearest first, as make is run in the folder its Makefile is in. Whichever kind is
    /// nearer the file goes first.
    /// </summary>
    private static List<string> BuildFilesAbove(string file)
    {
        var folders = new List<string>();

        for (var directory = new DirectoryInfo(Path.GetDirectoryName(file)!); directory is not null && folders.Count <= FoldersAboveLookedIn; directory = directory.Parent)
            folders.Add(directory.FullName);

        var cmakeFolders = folders.Where(folder => File.Exists(Path.Combine(folder, "CMakeLists.txt"))).ToList();
        var makefiles = folders.Select(Makefile.In).OfType<string>().ToList();

        var cmakeLists = cmakeFolders.AsEnumerable().Reverse().Select(folder => Path.Combine(folder, "CMakeLists.txt")).ToList();
        var nearestCMake = cmakeFolders.Count > 0 ? folders.IndexOf(cmakeFolders[0]) : int.MaxValue;
        var nearestMakefile = makefiles.Count > 0 ? folders.IndexOf(Path.GetDirectoryName(makefiles[0])!) : int.MaxValue;

        return nearestCMake <= nearestMakefile ? [.. cmakeLists, .. makefiles] : [.. makefiles, .. cmakeLists];
    }

    /// <summary>
    /// The program meant, of those built from the file: the one built by default that is not a test, then one that is
    /// not a test, then one built by default - or, when all of them are built from the same files, the first. When two are
    /// equally likely there is none: guessing which would build the wrong program half the time.
    /// </summary>
    private static BuiltProgram? Chosen(List<BuiltProgram> building)
    {
        if (building.Select(program => string.Join("|", program.Sources.Order(StringComparer.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            return building.OrderBy(program => program.IsATest).ThenBy(program => !program.MadeByDefault).First();

        Func<BuiltProgram, bool>[] preferences =
        [
            program => program.MadeByDefault && !program.IsATest,
            program => !program.IsATest,
            program => program.MadeByDefault,
        ];

        foreach (var preferred in preferences)
        {
            var those = building.Where(preferred).ToList();
            if (those.Count == 1) return those[0];
            if (those.Count > 1) return null;
        }

        return null;
    }

    /// <summary>The compiler FixFinder builds with - gcc or clang if it found one, otherwise MSVC - which a CMakeLists.txt may ask about.</summary>
    private static NativeCompilerKind CompilerFor(bool cpp) =>
        Toolchains.FindGnu(cpp) is { } gnu ? gnu.Name.StartsWith("clang", StringComparison.OrdinalIgnoreCase) ? NativeCompilerKind.Clang : NativeCompilerKind.Gnu
        : Toolchains.FindMsvc() is not null ? NativeCompilerKind.Msvc
        : NativeCompilerKind.None;

    private static Reading ProgramsIn(string buildFile, NativeCompilerKind compiler)
    {
        var key = (Path.GetFullPath(buildFile).ToLowerInvariant(), compiler);

        if (Readings.TryGetValue(key, out var kept) && DateTime.UtcNow - kept.ReadAt < KeptFor && kept.FilesRead.All(read => WrittenAt(read.File) == read.Written))
            return kept;

        Reading reading;

        try
        {
            if (Path.GetFileName(buildFile).Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase))
            {
                var project = CMakeProject.Read(buildFile, compiler);
                reading = new Reading(project.Programs(), Stamped(project.FilesRead), DateTime.UtcNow, null);
            }
            else
            {
                var makefile = Makefile.Read(buildFile);
                reading = new Reading(makefile.Programs(), Stamped(makefile.FilesRead), DateTime.UtcNow, null);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A build file FixFinder misreads must never stop it checking the program: it is built as if there were none,
            // and the note says so.
            reading = new Reading([], Stamped([buildFile]), DateTime.UtcNow, ex.Message.TrimEnd('.'));
        }

        Readings[key] = reading;
        return reading;
    }

    private static List<(string File, DateTime Written)> Stamped(IEnumerable<string> files) => files.Select(file => (file, WrittenAt(file))).ToList();

    private static DateTime WrittenAt(string file)
    {
        try
        {
            return File.GetLastWriteTimeUtc(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>The build of one program, its sources with <paramref name="first"/> first - and only what is safe to put on a command line.</summary>
    private static NativeBuild From(BuiltProgram program, string buildFile, string? first)
    {
        var cpp = program.Sources.Any(NativeFileKinds.IsCpp);
        var notFollowed = program.NotFollowed.ToList();

        var definitions = new List<string>();

        foreach (var definition in program.Definitions)
        {
            if (PlainDefinition().IsMatch(definition)) definitions.Add(definition);
            else notFollowed.Add($"it defines {definition}, which FixFinder does not put on a command line");
        }

        var standard = first is not null && program.Standards.TryGetValue(first, out var own) ? own : program.Standards.Values.FirstOrDefault();

        if (standard is not null && !LanguageStandards.IsGnuStandard(standard, cpp))
        {
            notFollowed.Add($"it asks for -std={standard}, which is not a {(cpp ? "C++" : "C")} standard FixFinder gives a compiler");
            standard = null;
        }

        IReadOnlyList<string> sources = first is null ? program.Sources : [first, .. program.Sources.Where(source => !source.Equals(first, StringComparison.OrdinalIgnoreCase))];

        return new NativeBuild(
            Path.GetFullPath(buildFile),
            program.Name,
            sources,
            program.IncludeFolders,
            definitions,
            program.Libraries.Where(library => LibraryName().IsMatch(library)).ToList(),
            program.LibraryFolders,
            program.LinkedFiles,
            program.Threads,
            standard,
            notFollowed);
    }

    /// <summary>Whether any of the program's sources includes the header, directly or through the headers they include.</summary>
    private static bool Includes(BuiltProgram program, string header)
    {
        var pending = new Queue<string>(program.Sources);
        var seen = new HashSet<string>(program.Sources, StringComparer.OrdinalIgnoreCase);

        while (pending.TryDequeue(out var file) && seen.Count < MostFilesFollowed)
        {
            string text;

            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (Match include in IncludeLine().Matches(text))
            {
                var quoted = include.Groups["quoted"].Success;
                var name = quoted ? include.Groups["quoted"].Value : include.Groups["angled"].Value;

                // gcc looks for "name" beside the file first, then in the include folders; <name> only in the include folders.
                var folders = quoted ? program.IncludeFolders.Prepend(Path.GetDirectoryName(file)!) : program.IncludeFolders;
                var found = folders.Select(folder => Path.GetFullPath(Path.Combine(folder, name))).FirstOrDefault(File.Exists);

                if (found is null) continue;
                if (found.Equals(header, StringComparison.OrdinalIgnoreCase)) return true;
                if (seen.Add(found)) pending.Enqueue(found);
            }
        }

        return false;
    }

    /// <summary>The same build with each path moved: into a copy of the program, where the copy holds it.</summary>
    public NativeBuild Translated(Func<string, string> moved) => this with
    {
        BuildFile = moved(BuildFile),
        Sources = Sources.Select(moved).ToList(),
        IncludeFolders = IncludeFolders.Select(moved).ToList(),
        LibraryFolders = LibraryFolders.Select(moved).ToList(),
        LinkedFiles = LinkedFiles.Select(moved).ToList(),
    };

    /// <summary>The same build with every path under one folder moved to the same place under another - as when the project is copied there.</summary>
    public NativeBuild MovedTo(string fromFolder, string toFolder)
    {
        var from = Path.GetFullPath(fromFolder).TrimEnd(Path.DirectorySeparatorChar);
        var to = Path.GetFullPath(toFolder).TrimEnd(Path.DirectorySeparatorChar);

        return Translated(path =>
        {
            var full = Path.GetFullPath(path);
            return full.Equals(from, StringComparison.OrdinalIgnoreCase) ? to
                : full.StartsWith(from + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? to + full[from.Length..]
                : full;
        });
    }

    /// <summary>gcc's flags for where the headers are and what is defined, each followed by a space.</summary>
    public string GnuCompileFlags =>
        string.Concat(IncludeFolders.Select(folder => $"-I {ArgumentQuoting.Quote(folder)} ")) +
        string.Concat(Definitions.Select(definition => $"{ArgumentQuoting.Quote("-D" + definition)} "));

    /// <summary>gcc's flags for what is linked, each after a space, to go after the sources: library files, folders, libraries by name and threads.</summary>
    public string GnuLinkFlags =>
        string.Concat(LinkedFiles.Select(file => $" {ArgumentQuoting.Quote(file)}")) +
        string.Concat(LibraryFolders.Select(folder => $" -L {ArgumentQuoting.Quote(folder)}")) +
        string.Concat(Libraries.Select(library => $" -l{library}")) +
        (Threads ? " -pthread" : "");

    /// <summary>MSVC's flags for where the headers are and what is defined, each after a space.</summary>
    public string MsvcCompileFlags =>
        string.Concat(IncludeFolders.Select(folder => $" /I {ArgumentQuoting.Quote(folder)}")) +
        string.Concat(Definitions.Select(definition => $" {ArgumentQuoting.Quote("/D" + definition)}"));

    /// <summary>
    /// What MSVC links, each after a space, to go last on cl's command line: .lib and .obj files, each library by name as
    /// its .lib, and the folders they are in. The maths library is part of MSVC's C runtime, and pthread and gcc's own
    /// libraries are not MSVC's, so those are left out.
    /// </summary>
    public string MsvcLinkFlags
    {
        get
        {
            var files = LinkedFiles.Where(file => Path.GetExtension(file).ToLowerInvariant() is ".lib" or ".obj").Select(file => $" {ArgumentQuoting.Quote(file)}");
            var libraries = Libraries.Where(library => library is not ("m" or "pthread" or "rt" or "dl" or "stdc++" or "gcc" or "c")).Select(library => $" {library}.lib");
            var folders = LibraryFolders.Select(folder => $" /LIBPATH:{ArgumentQuoting.Quote(folder)}").ToList();

            return string.Concat(files) + string.Concat(libraries) + (folders.Count > 0 ? " /link" + string.Concat(folders) : "");
        }
    }

    /// <summary>A path as the build file's folder sees it - include, ../common/include - for saying what a program was built with.</summary>
    public string Shown(string path) => Path.GetRelativePath(Folder, path).Replace('\\', '/');

    /// <summary>What the program is given besides its sources, as the flags that give it: -I include -DDEBUG -lm -pthread.</summary>
    public string FlagsShown =>
        string.Join(" ", IncludeFolders.Select(folder => $"-I {Shown(folder)}")
            .Concat(Definitions.Select(definition => $"-D{definition}"))
            .Concat(LinkedFiles.Select(Shown))
            .Concat(Libraries.Select(library => $"-l{library}"))
            .Concat(Threads ? ["-pthread"] : []));

    private static string Listed(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => $"{string.Join(", ", names.SkipLast(1))} and {names[^1]}",
    };
}
