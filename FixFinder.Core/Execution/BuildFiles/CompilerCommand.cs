using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution.BuildFiles;

/// <summary>What one gcc-style compiler command in a build file asks for, in the parts FixFinder builds a program with.</summary>
/// <param name="Inputs">The files it is given - sources, object files, libraries given as files - as they are written.</param>
/// <param name="Output">What -o names, if anything.</param>
/// <param name="CompilesOnly">Whether it stops before linking - -c, -S, -E or -M - so it makes no program.</param>
/// <param name="MakesALibrary">Whether it links a shared library (-shared) rather than a program.</param>
/// <param name="IncludeFolders">What -I, -isystem, -iquote and -idirafter add, in order, as written.</param>
/// <param name="Definitions">What -D defines: NAME or NAME=value.</param>
/// <param name="Libraries">The names -l links: m for -lm.</param>
/// <param name="LibraryFolders">What -L adds, as written.</param>
/// <param name="Threads">Whether it is given -pthread.</param>
/// <param name="Standard">What the last -std= says, as written.</param>
/// <param name="NotKnown">Words that stand for something only running a command could say.</param>
internal sealed partial record CompilerCommand(
    IReadOnlyList<string> Inputs,
    string? Output,
    bool CompilesOnly,
    bool MakesALibrary,
    IReadOnlyList<string> IncludeFolders,
    IReadOnlyList<string> Definitions,
    IReadOnlyList<string> Libraries,
    IReadOnlyList<string> LibraryFolders,
    bool Threads,
    string? Standard,
    IReadOnlyList<string> NotKnown)
{
    /// <summary>
    /// gcc, g++, cc, c++, clang and clang++ - with a version (gcc-13), a target in front (x86_64-w64-mingw32-gcc), a folder
    /// or .exe - which are the compilers whose options this reads.
    /// </summary>
    [GeneratedRegex(@"^(?:[\w.+]+-)*(?:cc|gcc|g\+\+|c\+\+|clang|clang\+\+)(?:-\d+(?:\.\d+)*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex CompilerName();

    [GeneratedRegex(@"^[A-Za-z_]\w*=")]
    private static partial Regex EnvironmentAssignment();

    /// <summary>Programs that run the compiler named after them: ccache gcc, time g++.</summary>
    private static readonly HashSet<string> RunsTheNextWord = new(StringComparer.OrdinalIgnoreCase) { "ccache", "distcc", "sccache", "time", "nice", "env" };

    /// <summary>gcc's options that take the next word as their value, so that word is not mistaken for a file to build.</summary>
    private static readonly HashSet<string> TakesTheNextWord = new(StringComparer.Ordinal)
    {
        "-o", "-I", "-D", "-U", "-l", "-L", "-x", "-include", "-imacros", "-isystem", "-iquote", "-idirafter", "-iprefix",
        "-iwithprefix", "-iwithprefixbefore", "-isysroot", "-MF", "-MT", "-MQ", "-Xlinker", "-Xassembler", "-Xpreprocessor",
        "-T", "-u", "-z", "-e", "-aux-info", "-dumpbase", "-dumpdir", "--param",
    };

    private static readonly string[] IncludeOptions = ["-isystem", "-iquote", "-idirafter", "-I"];

    public static bool IsCompiler(string word)
    {
        var name = word.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

        return CompilerName().IsMatch(name);
    }

    /// <summary>The command's options and files, or null when the words are not a command running a compiler.</summary>
    public static CompilerCommand? Read(IReadOnlyList<string> words)
    {
        var first = 0;

        while (first < words.Count && (EnvironmentAssignment().IsMatch(words[first]) || RunsTheNextWord.Contains(words[first]))) first++;

        if (first >= words.Count || !IsCompiler(words[first])) return null;

        var inputs = new List<string>();
        var includes = new List<string>();
        var definitions = new List<string>();
        var libraries = new List<string>();
        var libraryFolders = new List<string>();
        var notKnown = new List<string>();
        string? output = null;
        string? standard = null;
        var compilesOnly = false;
        var makesALibrary = false;
        var threads = false;

        for (var index = first + 1; index < words.Count; index++)
        {
            var word = words[index];
            var value = TakesTheNextWord.Contains(word) && index + 1 < words.Count ? words[++index] : null;

            if (Unknowable.IsIn(word) || (value is not null && Unknowable.IsIn(value)))
            {
                notKnown.Add(value is null ? word : $"{word} {value}");
                continue;
            }

            switch (word)
            {
                case "-o":
                    output = value;
                    continue;
                case "-c" or "-S" or "-E" or "-M" or "-MM":
                    compilesOnly = true;
                    continue;
                case "-shared":
                    makesALibrary = true;
                    continue;
                case "-pthread":
                    threads = true;
                    continue;
                case "-D":
                    if (value is not null) definitions.Add(value);
                    continue;
                case "-l":
                    if (value is not null) libraries.Add(value);
                    continue;
                case "-L":
                    if (value is not null) libraryFolders.Add(value);
                    continue;
            }

            if (value is not null)
            {
                if (IncludeOptions.Contains(word)) includes.Add(value);
                continue;
            }

            if (!word.StartsWith('-') || word == "-")
            {
                if (word != "-") inputs.Add(word);
                continue;
            }

            if (word.StartsWith("-o", StringComparison.Ordinal)) output = word[2..];
            else if (word.StartsWith("-std=", StringComparison.Ordinal)) standard = word[5..];
            else if (word.StartsWith("--std=", StringComparison.Ordinal)) standard = word[6..];
            else if (word.StartsWith("-D", StringComparison.Ordinal)) definitions.Add(word[2..]);
            else if (word.StartsWith("-l", StringComparison.Ordinal)) libraries.Add(word[2..]);
            else if (word.StartsWith("-L", StringComparison.Ordinal)) libraryFolders.Add(word[2..]);
            else if (IncludeOptions.FirstOrDefault(option => word.StartsWith(option, StringComparison.Ordinal)) is { } include)
                includes.Add(word[include.Length..]);
        }

        return new CompilerCommand(inputs, output, compilesOnly, makesALibrary, includes, definitions, libraries, libraryFolders,
            threads, standard, notKnown);
    }
}
