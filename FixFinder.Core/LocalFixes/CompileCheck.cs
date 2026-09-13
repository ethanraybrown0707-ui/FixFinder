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
        ".py" or ".java" => true,
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

            var copy = Path.Combine(folder, Path.GetFileName(source.Path));
            await File.WriteAllBytesAsync(copy, source.Render(lines), cancellationToken);

            if (SpecFor(copy, source.Path, folder, pythonInterpreter) is not { } spec) return CheckResult.NotRun;

            var registry = new ParserRegistry();
            var run = await new TargetRunner(registry).RunAsync(spec, cancellationToken);

            if (run.Outcome is RunOutcome.LaunchFailed or RunOutcome.TimedOut or RunOutcome.Cancelled)
                return CheckResult.NotRun;

            return new CheckResult(true, run.ExitCode, run.Lines, ErrorsIn(registry, run.Lines));
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

            default:
                return null;
        }
    }

    /// <summary>The same compiler the build chose, in the same order: GNU first, then MSVC.</summary>
    /// <remarks>
    /// It has to be the same one. A fix that satisfies gcc and not cl, or the reverse, would be
    /// verified against a build the user never runs.
    /// </remarks>
    private static TargetSpec? Native(string copy, string originalFolder, string folder)
    {
        var cpp = !Path.GetExtension(copy).Equals(".c", StringComparison.OrdinalIgnoreCase);
        var exe = Path.Combine(folder, "check.exe");

        if (Toolchains.FindGnu(cpp) is { } gnu)
        {
            var standard = cpp ? "-std=c++17 " : "";
            return Spec(gnu.Program, $"{standard}-I \"{originalFolder}\" -o \"{exe}\" \"{copy}\"", folder);
        }

        if (Toolchains.FindMsvc() is not { SetupScript: { } vcvarsall }) return null;

        var flags = cpp ? "/nologo /W3 /EHsc /std:c++17" : "/nologo /W3";
        var batch = Path.Combine(folder, "check.cmd");

        File.WriteAllText(batch, string.Join("\r\n",
        [
            "@echo off",
            $"call \"{vcvarsall}\" x64 >nul",
            "if errorlevel 1 (echo FixFinder: could not set up the MSVC environment & exit /b 1)",
            $"cd /d \"{folder}\"",
            $"cl {flags} /I \"{originalFolder}\" /Fe:check.exe \"{Path.GetFileName(copy)}\"",
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
