using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Execution;

/// <summary>
/// The C or C++ standard a program's own code is written to, when nothing says which: worked out, as every standard is in
/// FixFinder, by the compiler. A program that does not build as the standard FixFinder gives it - C++17, or the
/// compiler's own for C - is compiled, for its syntax only, as the standards either side of it; the first the compiler
/// takes it as, with fewer errors and none new, is the one it is written to, and it is built as that.
/// </summary>
/// <remarks>
/// Newer standards are tried first - C++20 and C++23, C23 - for code that uses what they added, then older ones - C++14,
/// C17 - for code that uses what a later one took out: std::auto_ptr and std::random_shuffle, which C++17 removed, or
/// C written before C23 made bool a keyword. Only a standard chosen by nobody is ever changed: one chosen in Settings,
/// or given by the program's Makefile or CMakeLists.txt, is kept to.
/// </remarks>
public static partial class NativeStandards
{
    /// <summary>A standard the program builds as, and what says so - in the compiler's own words.</summary>
    /// <param name="Standard">The standard, as gcc names it: c++20, c2x.</param>
    /// <param name="Explained">"main.cpp does not compile as C++17 - g++ said of main.cpp:3 ... - and does as C++20, so it was built as C++20."</param>
    public sealed record Found(string Standard, string Explained);

    /// <summary>The standards tried, in order, for C++ and for C.</summary>
    private static readonly string[] CppStandards = ["c++20", "c++23", LanguageStandards.UsualCpp, "c++14"];
    private static readonly string[] CStandards = ["c2x", "gnu17", ""];

    [GeneratedRegex(@"-std=\S+\s?")]
    private static partial Regex StandardFlag();

    [GeneratedRegex(@"\s-o\s+""[^""]*""")]
    private static partial Regex OutputFlag();

    private static readonly ConcurrentDictionary<string, (string Standard, DateTime Written)> Remembered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The standard a source of the program was found to build as, while the source is as it was then - or null. A copy
    /// of the program made to try a fix in is built as the program is, so its sources answer for the program's own.
    /// </summary>
    public static string? RememberedFor(string source)
    {
        var path = Path.GetFullPath(ProgramCopy.OriginalOf(Path.GetFullPath(source)));
        return Remembered.TryGetValue(path, out var known) && known.Written == WrittenAt(path) ? known.Standard : null;
    }

    /// <summary>Whether a build's standard is FixFinder's own choice - none chosen in Settings, and none given by a build file - and so may be worked out.</summary>
    public static bool IsFixFindersChoice(string chosen, bool cpp) =>
        !LanguageStandards.Current.Chooses(cpp) && NativeBuild.For(chosen).Build?.Standard is null;

    /// <summary>
    /// The standard, other than the one the build used, that the compiler takes the program as with fewer errors and no
    /// new one - or null when there is none, or what failed was not a compile by gcc or clang.
    /// </summary>
    /// <param name="errorsIn">Reads the errors in what the compiler printed, as the build's own errors were read.</param>
    public static async Task<Found?> FindAsync(
        string chosen, TargetSpec compile, IReadOnlyList<ParsedError> failed, Func<IReadOnlyList<CapturedLine>, IReadOnlyList<ParsedError>> errorsIn,
        CancellationToken cancellationToken)
    {
        var compiler = Path.GetFileNameWithoutExtension(compile.ExecutablePath);
        if (!compiler.Contains("gcc", StringComparison.OrdinalIgnoreCase) && !compiler.Contains("g++", StringComparison.OrdinalIgnoreCase) &&
            !compiler.StartsWith("clang", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var cpp = !Path.GetExtension(chosen).Equals(".c", StringComparison.OrdinalIgnoreCase);
        if (!IsFixFindersChoice(chosen, cpp)) return null;

        var used = StandardFlag().Match(compile.Arguments) is { Success: true } flag ? flag.Value.Trim()["-std=".Length..] : "";
        var failedAt = failed.Select(Place).ToHashSet();

        foreach (var standard in (cpp ? CppStandards : CStandards).Where(standard => standard != used))
        {
            var probe = new TargetSpec
            {
                ExecutablePath = compile.ExecutablePath,
                Arguments = OutputFlag().Replace(WithStandard(compile.Arguments, standard), "") + " -fsyntax-only",
                WorkingDirectory = compile.WorkingDirectory,
                Timeout = compile.Timeout,
                ExtraEnvironment = compile.ExtraEnvironment,
            };

            var run = await new TargetRunner().RunAsync(probe, cancellationToken);
            if (run.Outcome is RunOutcome.LaunchFailed or RunOutcome.TimedOut) return null;

            var errors = errorsIn(run.Lines);
            var places = errors.Select(Place).ToList();

            // Fewer errors, every one of them among the build's own: the compiler takes the code as this standard.
            if (places.Count >= failedAt.Count || places.Any(place => !failedAt.Contains(place))) continue;

            var explained = failed.FirstOrDefault(error => !places.Contains(Place(error))) is { } answered
                ? $"{Path.GetFileName(chosen)} does not compile as {Shown(used, compiler)} - {compiler} said of {Where(answered)}: \"{answered.Message}\" - and " +
                  $"does as {Shown(standard, compiler)}, so it was built as {Shown(standard, compiler)}. Choosing a standard in Settings holds every program to that one instead."
                : $"{Path.GetFileName(chosen)} does not compile as {Shown(used, compiler)} and does as {Shown(standard, compiler)}, so it was built as {Shown(standard, compiler)}.";

            return new Found(standard, explained);
        }

        return null;
    }

    /// <summary>The build's arguments with this standard in place of the one it had - or with none, for the compiler's own.</summary>
    public static string WithStandard(string arguments, string standard)
    {
        var withoutStandard = StandardFlag().Replace(arguments, "");
        return standard.Length == 0 ? withoutStandard : $"-std={standard} {withoutStandard}";
    }

    /// <summary>Remembers the standard for every source of the program, so its fixes are checked as it and its next build is.</summary>
    public static void Remember(IEnumerable<string> sources, string standard)
    {
        foreach (var source in sources)
        {
            var path = Path.GetFullPath(source);
            Remembered[path] = (standard, WrittenAt(path));
        }
    }

    /// <summary>A standard as a reader knows it: C++20 for c++20, C23 for c2x, C17 for gnu17 - or the compiler's own, for none.</summary>
    internal static string Shown(string standard, string compiler) => standard switch
    {
        "" => $"{compiler}'s own standard",
        "c2x" or "c23" or "gnu2x" or "gnu23" => "C23",
        "gnu17" or "c17" => "C17",
        _ when standard.StartsWith("c++", StringComparison.Ordinal) => "C++" + standard[3..],
        _ => standard,
    };

    private static (string File, int Line) Place(ParsedError error) =>
        error.Frames.FirstOrDefault() is { File: { } file, Line: { } line } ? (Path.GetFileName(file), line) : ("", 0);

    private static string Where(ParsedError error) => Place(error) is ({ Length: > 0 } file, > 0) place ? $"{file}:{place.Line}" : "it";

    private static DateTime WrittenAt(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return DateTime.MinValue;
        }
    }
}
