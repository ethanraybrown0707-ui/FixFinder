using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes;

/// <summary>What compiling the changed copy produced.</summary>
/// <param name="Ran">False when there was nothing to compile it with; the fix is then not offered.</param>
/// <param name="ExitCode">The compiler's exit code.</param>
/// <param name="Lines">Everything the compiler printed.</param>
/// <param name="Errors">Every error in that output, as the parsers read it.</param>
public sealed record CheckResult(
    bool Ran, int? ExitCode, IReadOnlyList<CapturedLine> Lines, IReadOnlyList<ParsedError> Errors)
{
    public bool Clean => Ran && ExitCode == 0 && Errors.Count == 0;

    public static CheckResult NotRun { get; } = new(false, null, [], []);
}

/// <summary>
/// Compiles a copy of a file with a fix made in it, somewhere that is not the user's source tree.
/// </summary>
/// <remarks>
/// This is what makes a fix worked out by rules safe to offer. A rule reads an error and a line of
/// code and proposes a change; the compiler then says whether the change did what it claims. The
/// nearest name to a misspelt one, an import picked from a table, a brace appended at the end of a
/// file - each is a claim about code the rule has only partly read, and each is checked by the one
/// thing that has read all of it.
/// <para>
/// <b>Nothing is run.</b> Python is byte-compiled with <c>py_compile</c>, which parses the file and
/// executes none of it; Java and C are compiled and linked but the result is never started. Every
/// check happens in a fresh folder under the temp directory that is deleted afterwards, with the
/// original folder added to the include or source path so that the file's neighbours still resolve.
/// </para>
/// </remarks>
public static class CompileCheck
{
    /// <summary>Where the copies are compiled.</summary>
    /// <remarks>
    /// The temp folder rather than FixFinder's own folder under LocalAppData, and that is not a
    /// preference. The Python most Windows 11 machines have is the Microsoft Store build, which is a
    /// packaged app, and a packaged app sees its own private view of LocalAppData: a file FixFinder
    /// writes there simply does not exist as far as that python.exe can tell. py_compile answered
    /// every copy with "No such file or directory" and exit 1, so every Python fix was refused - and
    /// the syntax fixes that were accepted had never really been compiled. The temp folder is shared
    /// with packaged apps.
    /// </remarks>
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "FixFinder-check");

    /// <summary>How long one check may take. A cold MSVC environment alone is a few seconds.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>True when there is a way to check a file of this kind at all.</summary>
    public static bool CanCheck(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".py" or ".java" or ".cs" => true,
        ".js" or ".mjs" or ".cjs" or ".go" => true,
        ".c" or ".cpp" or ".cc" or ".cxx" or ".c++" => true,
        _ => false,
    };

    public static async Task<CheckResult> RunAsync(
        SourceFile source,
        IReadOnlyList<string> lines,
        string? pythonInterpreter,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Root, Guid.NewGuid().ToString("N")[..12]);

        try
        {
            Directory.CreateDirectory(folder);

            var bytes = source.Render(lines);
            var copy = Path.Combine(folder, Path.GetFileName(source.Path));
            await File.WriteAllBytesAsync(copy, bytes, cancellationToken);

            if (SpecFor(copy, source.Path, folder, pythonInterpreter) is not { } spec) return CheckResult.NotRun;

            var key = KeyFor(spec, folder, source.Path, bytes);

            if (key is not null && Remembered.TryGetValue(key, out var known)) return known;

            var registry = new ParserRegistry();
            var run = await new TargetRunner(registry).RunAsync(spec, cancellationToken);

            if (run.Outcome is RunOutcome.LaunchFailed or RunOutcome.TimedOut or RunOutcome.Cancelled)
                return CheckResult.NotRun;

            var result = new CheckResult(true, run.ExitCode, run.Lines, ErrorsIn(registry, run.Lines));

            if (key is not null && WorthRemembering(result, copy))
            {
                if (Remembered.Count >= MostRemembered) Remembered.Clear();
                Remembered[key] = result;
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CheckResult.NotRun;
        }
        finally
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A compiler that has not quite let go of its output. The folder is inside
                // FixFinder's own data, so leaving it behind costs disk and nothing else.
            }
        }
    }

    /// <summary>Checks already compiled, by everything that decided how they came out.</summary>
    /// <remarks>
    /// The same change to the same file comes up again more often than it sounds: two rules that
    /// arrive at the same edit, the next error in a run that one fix would also have cured, a
    /// warning checked after its error, or the same program run twice. A compiler given the same
    /// input answers the same way, so the answer is kept rather than asked for again.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, CheckResult> Remembered = new(StringComparer.Ordinal);

    /// <summary>Enough for every proposal in a long session; past it, the lot is forgotten and relearned.</summary>
    private const int MostRemembered = 256;

    /// <summary>More files than this next to the one being checked, and nothing is remembered for it.</summary>
    private const int MostNeighbours = 2000;

    /// <summary>
    /// Everything a check's outcome depends on, as one hash, or null when that cannot be pinned down.
    /// </summary>
    /// <remarks>
    /// The compiler and its arguments, the file it was given, the byte-for-byte content, and - for
    /// Java, C and C++, which read the files beside it through the source or include path - the name,
    /// size and last change of every file in that folder. A header edited between two checks is a
    /// different check. A C# file with <c>#:</c> directives can name packages and projects anywhere,
    /// so it is never remembered at all.
    /// </remarks>
    internal static string? KeyFor(TargetSpec spec, string folder, string original, byte[] content)
    {
        var extension = Path.GetExtension(original).ToLowerInvariant();
        var directives = Encoding.UTF8.GetString(content).Split('\n')
            .Select(line => line.TrimStart())
            .Where(line => line.StartsWith('#'))
            .ToList();

        if (extension == ".cs" && directives.Any(line => line.StartsWith("#:", StringComparison.Ordinal)))
            return null;

        var key = new StringBuilder()
            .Append(spec.ExecutablePath).Append('\n')
            .Append(spec.Arguments.Replace(folder, "<copy>", StringComparison.OrdinalIgnoreCase)).Append('\n')
            .Append(spec.LaunchViaDotnet).Append('\n')
            .Append(original).Append('\n')
            .Append(Convert.ToHexString(SHA256.HashData(content))).Append('\n');

        foreach (var (name, value) in spec.ExtraEnvironment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            key.Append(name).Append('=').Append(value).Append('\n');

        if (extension is ".java" or ".c" or ".cpp" or ".cc" or ".cxx" or ".c++")
        {
            // `#include "../shared.h"` reaches out of the folder through the include path, to files
            // the listing below does not cover.
            if (extension != ".java" && directives.Any(line => line.Contains("..", StringComparison.Ordinal)))
                return null;

            if (Neighbours(Path.GetDirectoryName(original)!) is not { } listing) return null;
            key.Append(listing);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString())));
    }

    /// <summary>Every file under a folder, with its size and last change, or null when there are too many to list.</summary>
    private static string? Neighbours(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        var listing = new StringBuilder();
        var count = 0;

        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
            {
                if (++count > MostNeighbours) return null;

                listing.Append(file.FullName).Append('|').Append(file.Length).Append('|').Append(file.LastWriteTimeUtc.Ticks).Append('\n');
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }

        return listing.ToString();
    }

    /// <summary>
    /// Whether a check's answer came from the compiler reading the file, and not from something
    /// around it that could be different next time.
    /// </summary>
    /// <remarks>
    /// A clean compile is always the file's own doing, and so is an error the compiler placed inside
    /// the file. Anything else - a linker complaint, a package that failed to download, a compiler that
    /// exited without a word - might not happen again, and remembering it would refuse a good fix for
    /// as long as FixFinder stays open.
    /// </remarks>
    internal static bool WorthRemembering(CheckResult result, string copy)
    {
        if (!result.Ran) return false;
        if (result.Clean) return true;
        if (result.Errors.Count == 0) return false;

        var name = Path.GetFileName(copy);

        return result.Errors.All(error =>
            (error.CulpritFrame ?? error.Frames.FirstOrDefault())?.File is { } file &&
            Path.GetFileName(file).Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every error in some compiler output: the first, then the rest.</summary>
    public static IReadOnlyList<ParsedError> ErrorsIn(ParserRegistry registry, IReadOnlyList<CapturedLine> lines)
    {
        if (registry.Parse(lines) is not { } first) return [];

        // The generic parser reads any line with a file and a number in it, which a clean build's
        // banner can have. A check only cares about what a real compiler parser recognised.
        if (first.LanguageId == "generic") return [];

        return [first, .. registry.Others(first, lines)];
    }

    private static TargetSpec? SpecFor(string copy, string original, string folder, string? python)
    {
        var originalFolder = Path.GetDirectoryName(original)!;

        switch (Path.GetExtension(copy).ToLowerInvariant())
        {
            case ".py":
                var interpreter = python ?? TargetFactory.FindOnPath("python") ?? TargetFactory.FindOnPath("py");
                if (interpreter is null) return null;

                // -X utf8 so a file with non-ASCII in it reports the same errors here as it did
                // when it ran, rather than a decoding error of its own.
                return Spec(interpreter, $"-X utf8 -m py_compile \"{copy}\"", folder);

            case ".java":
                if (Toolchains.FindJavac() is not { } javac) return null;

                return Spec(
                    javac.Program,
                    $"-proc:none -Xmaxerrs 500 -d \"{Path.Combine(folder, "out")}\" " +
                    $"-sourcepath \"{originalFolder}\" \"{copy}\"",
                    folder);

            case ".c" or ".cpp" or ".cc" or ".cxx" or ".c++":
                return Native(copy, originalFolder, folder);

            case ".js" or ".mjs" or ".cjs":
                // --check parses the file and runs none of it. It cannot see what only happens when the file runs - a
                // misspelt name, a module that does not export something - so for those it proves the file still parses.
                if (TargetFactory.FindOnPath("node") is not { } node) return null;

                return Spec(node, $"--check \"{copy}\"", folder);

            case ".go":
                // A build, not a run: go build compiles and links, and the binary it leaves is deleted with the folder.
                if (TargetFactory.FindOnPath("go") is not { } go) return null;

                return Spec(go, $"build -o \"{Path.Combine(folder, "check.exe")}\" \"{copy}\"", folder);

            case ".cs":
                // The .NET SDK builds a single .cs file on its own, the same way FixFinder runs one.
                if (TargetFactory.FindOnPath("dotnet") is not { } dotnet) return null;

                return Spec(dotnet, $"build \"{copy}\" -nologo -v q", folder);

            default:
                return null;
        }
    }

    /// <summary>The same compiler the build chose, in the same order: GNU first, then MSVC.</summary>
    /// <remarks>
    /// It has to be the same one. A fix that satisfies gcc and not cl, or the reverse, would be
    /// verified against a build the user never runs.
    /// </remarks>
    /// <param name="reuseMsvcEnvironment">False calls vcvarsall for this check alone, as every check once did; a test compares the two.</param>
    internal static TargetSpec? Native(string copy, string originalFolder, string folder, bool reuseMsvcEnvironment = true)
    {
        var cpp = !Path.GetExtension(copy).Equals(".c", StringComparison.OrdinalIgnoreCase);
        var exe = Path.Combine(folder, "check.exe");

        if (Toolchains.FindGnu(cpp) is { } gnu)
        {
            var standard = cpp ? "-std=c++17 " : "";
            return Spec(gnu.Program, $"{standard}-Wformat -I \"{originalFolder}\" -o \"{exe}\" \"{copy}\"", folder);
        }

        if (Toolchains.FindMsvc() is not { SetupScript: { } vcvarsall }) return null;

        var flags = cpp ? "/nologo /W3 /EHsc /std:c++17" : "/nologo /W3";
        var compile = $"{flags} /I \"{originalFolder}\" /Fe:check.exe \"{Path.GetFileName(copy)}\"";

        // The same cl.exe with the same arguments in the same folder, in the environment the script
        // sets up - captured once rather than rebuilt for every check, which took ten times longer
        // than the compile. When it could not be captured, the script is called here as it always was.
        if (reuseMsvcEnvironment && Toolchains.MsvcEnvironment() is { } environment && Toolchains.ClIn(environment) is { } cl)
        {
            return new TargetSpec
            {
                ExecutablePath = cl,
                Arguments = compile,
                WorkingDirectory = folder,
                Timeout = Timeout,
                ExtraEnvironment = environment,
            };
        }

        var batch = Path.Combine(folder, "check.cmd");

        File.WriteAllText(batch, string.Join("\r\n",
        [
            "@echo off",
            $"call \"{vcvarsall}\" x64 >nul",
            "if errorlevel 1 (echo FixFinder: could not set up the MSVC environment & exit /b 1)",
            $"cd /d \"{folder}\"",
            $"cl {compile}",
            "exit /b %errorlevel%",
            "",
        ]), new UTF8Encoding(false));

        return Spec("cmd.exe", $"/c \"{batch}\"", folder);
    }

    private static TargetSpec Spec(string program, string arguments, string folder) => new()
    {
        ExecutablePath = program,
        Arguments = arguments,
        WorkingDirectory = folder,
        Timeout = Timeout,
    };
}
