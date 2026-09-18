using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>How to build one source file, and how to run what comes out.</summary>
/// <param name="Compile">The build step. Runs first; if it fails, its output is the error.</param>
/// <param name="Run">What to launch once the build succeeds.</param>
/// <param name="Explanation">Which compiler was chosen, said out loud.</param>
public sealed record BuildAndRun(TargetSpec Compile, TargetSpec Run, string Explanation)
{
    /// <summary>
    /// The run step with the build step attached to it as its rebuild command.
    /// </summary>
    /// <remarks>
    /// Without this every compiled language verifies as <c>Inconclusive</c>: the verifier refuses
    /// to re-run a binary it knows is stale, and nothing was telling it how to rebuild one. The
    /// command it needs already exists as <see cref="Compile"/>, so deriving it here keeps one
    /// description of how to build a file rather than two that can quietly disagree.
    /// </remarks>
    public TargetSpec Runnable => new()
    {
        ExecutablePath = Run.ExecutablePath,
        Arguments = Run.Arguments,
        WorkingDirectory = Run.WorkingDirectory,
        LaunchViaDotnet = Run.LaunchViaDotnet,
        ExtraEnvironment = Run.ExtraEnvironment,
        Timeout = Run.Timeout,
        OutputEncoding = Run.OutputEncoding,
    };
}

/// <summary>
/// Builds C, C++ and Java source before running it.
/// </summary>
/// <remarks>
/// These languages need a step the scripting languages do not, and that step is worth having for
/// its own sake: <b>a compiler error is an error worth looking up</b>. <c>error C2065</c> and
/// <c>error CS0103</c> are globally unique strings that thousands of people have pasted into a
/// search box, and FixFinder already has parsers that lift the code out as a first-class term.
/// Until now nothing could feed them, because nothing could compile anything.
/// <para>
/// The build runs from a generated <c>.cmd</c> file rather than by launching the compiler
/// directly. On Windows that is not a convenience: <c>cl.exe</c> does not work at all until
/// <c>vcvarsall.bat</c> has set up its environment, and a batch file is also something you can
/// open and read when the build does something surprising.
/// </para>
/// </remarks>
public static partial class CompiledLanguages
{
    /// <summary>Where built output goes. Never beside the source, which is not ours to litter.</summary>
    public static string BuildRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FixFinder", "build");

    [GeneratedRegex(@"^\s*package\s+([A-Za-z_][\w.]*)\s*;", RegexOptions.Multiline)]
    private static partial Regex JavaPackagePattern();

    /// <summary>Extensions this class knows how to build.</summary>
    public static bool Handles(string extension) => extension.ToLowerInvariant() switch
    {
        ".c" or ".cpp" or ".cc" or ".cxx" or ".c++" or ".java" => true,
        _ => false,
    };

    /// <summary>
    /// Works out how to build and run a source file, or says what is missing.
    /// </summary>
    /// <returns>Null when the language is not one of these; otherwise a plan or a problem.</returns>
    public static (BuildAndRun? Plan, string? Problem) Prepare(string source, TimeSpan timeout)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        var output = OutputDirectory(source);

        return extension switch
        {
            ".c" => Native(source, output, timeout, cpp: false),
            ".cpp" or ".cc" or ".cxx" or ".c++" => Native(source, output, timeout, cpp: true),
            ".java" => Java(source, output, timeout),
            _ => (null, null),
        };
    }

    // ------------------------------------------------------------------ C and C++

    private static (BuildAndRun?, string?) Native(string source, string output, TimeSpan timeout, bool cpp)
    {
        var language = cpp ? "C++" : "C";
        var exe = Path.Combine(output, Path.GetFileNameWithoutExtension(source) + ".exe");

        // The other files of the program, when it is split across several - see ProgramLayout.
        var sources = ProgramLayout.NativeSources(source);

        if (Toolchains.FindGnu(cpp) is { } gnu)
        {
            // -g keeps debug info, and -O0 stops the optimiser from rearranging the very lines
            // a stack trace would name. -fdiagnostics-parseable-fixits prints the fixes the
            // compiler already knows - the missing header, the member a typo meant - a second
            // time, in a form that can be applied exactly rather than read and retyped. -Wformat
            // reports a printf conversion that does not match its argument, which crashes rather
            // than failing to compile, and which gcc stays quiet about without it.
            // The warnings beyond the defaults each name a mistake a run usually survives - see GnuWarnings.
            var standard = GnuWarnings(cpp);

            var compile = Spec(
                gnu.Program,
                $"-g -O0 -Wformat -fdiagnostics-parseable-fixits {standard}-o \"{exe}\" {Quoted(sources)}",
                Path.GetDirectoryName(source)!,
                timeout);

            return (new BuildAndRun(compile, Run(exe, output, timeout),
                $"Building it{Along(sources)} with {gnu.Name}, then running the result."), null);
        }

        if (Toolchains.FindMsvc() is { SetupScript: { } script } msvc)
        {
            var batch = WriteMsvcBatch(sources, exe, output, script, cpp);

            var compile = Spec("cmd.exe", $"/c \"{batch}\"", output, timeout);

            return (new BuildAndRun(compile, Run(exe, output, timeout),
                $"Building it{Along(sources)} with {msvc.Name}, then running the result."), null);
        }

        return (null,
            $"{Path.GetFileName(source)} is {language}, which has to be compiled before it can run, " +
            "and no compiler was found.\n\n" +
            "Install one of:\n" +
            "  •  Visual Studio Build Tools (gives you cl.exe)\n" +
            "  •  MSYS2, MinGW-w64 or LLVM (gives you gcc or clang on PATH)\n\n" +
            "FixFinder finds Visual Studio automatically, so it does not need to be on PATH.");
    }

    /// <summary>
    /// Writes the batch file that sets up MSVC and then compiles.
    /// </summary>
    /// <remarks>
    /// <c>call</c> rather than plain invocation, or the batch file would exit inside vcvarsall
    /// and never reach the compiler. The output is redirected nowhere: FixFinder wants the
    /// compiler's diagnostics on stdout and stderr exactly as written, because those lines are
    /// what gets parsed and searched.
    /// </remarks>
    private static string WriteMsvcBatch(IReadOnlyList<string> sources, string exe, string output, string vcvarsall, bool cpp)
    {
        // /Zi debug info, /W3 the usual warning level, /EHsc the standard C++ exception model.
        var flags = cpp ? "/nologo /Zi /W3 /EHsc /std:c++17" : "/nologo /Zi /W3";

        var batch = Path.Combine(output, "build.cmd");

        // Builds from inside the output folder rather than passing /Fo, and that is not a
        // stylistic choice. A quoted Windows path ending in a separator - "C:\out\obj\" - has a
        // backslash immediately before the closing quote, which the command-line parser reads as
        // an escaped quote; the argument then swallows the next token and cl reports
        // "Command line error D8003: missing source filename" while pointing at nothing obviously
        // wrong. Working from the directory avoids needing the path at all.
        var text = string.Join("\r\n",
        [
            "@echo off",
            "rem Written by FixFinder. cl.exe cannot run until vcvarsall has set up the environment.",
            $"call \"{vcvarsall}\" x64 >nul",
            "if errorlevel 1 (echo FixFinder: could not set up the MSVC environment & exit /b 1)",
            $"cd /d \"{output.TrimEnd(Path.DirectorySeparatorChar)}\"",
            $"cl {flags} /Fe:\"{Path.GetFileName(exe)}\" {Quoted(sources)}",
            "exit /b %errorlevel%",
            "",
        ]);

        File.WriteAllText(batch, text, new UTF8Encoding(false));

        return batch;
    }

    // ------------------------------------------------------------------ finding a silent crash

    /// <summary>
    /// A second build of a C or C++ file with AddressSanitizer, for when the first crashed without a word.
    /// </summary>
    /// <remarks>
    /// A native crash on Windows prints nothing: an access violation, a divide by zero, a write past
    /// the end of an array all end in an exit code and silence. AddressSanitizer ships with Visual
    /// Studio and catches each of those at the instruction that did it, printing the file and line -
    /// <c>stack-buffer-overflow ... app.c:4</c> - which turns "it crashed" back into something that can
    /// be looked at.
    /// <para>
    /// Always MSVC, even when gcc did the first build. MinGW's gcc has no AddressSanitizer on
    /// Windows, and a build that asks for it fails to link rather than quietly going without. A
    /// second compiler for a second build is fine here: this build exists only to find a line, and
    /// if MSVC cannot compile something gcc accepted, the rerun says so and nothing else changes.
    /// The sanitizer's runtime DLL is copied next to the program, because the program runs outside
    /// the environment vcvarsall sets up and would otherwise not start at all.
    /// </para>
    /// </remarks>
    /// <param name="normalRun">How the program was run the first time; the second run matches it.</param>
    /// <summary>The language standard and the warnings beyond gcc's defaults, the same for a build and for checking a fix.</summary>
    /// <remarks>
    /// Each one names a mistake that a run usually survives, so the program looks fine and is not: <c>-Waddress</c> a string
    /// compared with <c>==</c>, which compares where the strings are; for C++, <c>-Wmismatched-new-delete</c> <c>delete</c> for
    /// memory from <c>new[]</c>, <c>-Wdelete-non-virtual-dtor</c> a derived object deleted through a base with no virtual
    /// destructor, and <c>-Wcatch-value</c> an exception caught by value, which slices off what was really thrown.
    /// </remarks>
    internal static string GnuWarnings(bool cpp) => cpp
        ? "-std=c++17 -Wall -Wextra -Wno-unused-parameter -Wmismatched-new-delete -Wdelete-non-virtual-dtor -Wcatch-value -Waddress "
        : "-Wall -Wextra -Wno-unused-parameter -Wno-missing-field-initializers -Waddress ";

    /// <summary>The javac warnings that point at real mistakes in a student's program, and none about build settings.</summary>
    internal const string JavaLint = "-Xlint:cast,divzero,empty,fallthrough,finally,overrides,rawtypes,static,unchecked,deprecation";

    public static BuildAndRun? PrepareSanitized(string source, TargetSpec normalRun)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".c" or ".cpp" or ".cc" or ".cxx" or ".c++")) return null;

        var cpp = extension != ".c";

        if (Toolchains.FindMsvc() is not { SetupScript: { } vcvarsall } msvc) return null;

        var output = Path.Combine(OutputDirectory(source), "asan");
        Directory.CreateDirectory(output);

        var exe = Path.Combine(output, Path.GetFileNameWithoutExtension(source) + ".exe");
        var flags = cpp ? "/nologo /Zi /W3 /EHsc /std:c++17" : "/nologo /Zi /W3";
        var batch = Path.Combine(output, "build.cmd");

        // C++ also gets a second file compiled in: a terminate handler that prints what g++'s runtime prints. An
        // exception nothing caught, or a std::thread never joined, ends an MSVC program with a bare 0xC0000409 and
        // not a word - AddressSanitizer has nothing to say about either - where g++ names what was thrown.
        var sources = Quoted(ProgramLayout.NativeSources(source));

        if (cpp)
        {
            var reporter = Path.Combine(output, "fixfinder_terminate.cpp");
            File.WriteAllText(reporter, TerminateReporter, new UTF8Encoding(false));
            sources += $" \"{reporter}\"";
        }

        File.WriteAllText(batch, string.Join("\r\n",
        [
            "@echo off",
            "rem Written by FixFinder: the same build with AddressSanitizer, to find where a silent crash happened.",
            $"call \"{vcvarsall}\" x64 >nul",
            "if errorlevel 1 (echo FixFinder: could not set up the MSVC environment & exit /b 1)",
            $"cd /d \"{output}\"",
            // A label rather than a bracketed block, which a bracket in any path would end early.
            "where clang_rt.asan_dynamic-x86_64.dll >nul 2>nul",
            "if errorlevel 1 goto without_sanitizer",
            "for /f \"delims=\" %%d in ('where clang_rt.asan_dynamic-x86_64.dll') do copy /y \"%%d\" . >nul",
            $"cl {flags} /fsanitize=address /Fe:\"{Path.GetFileName(exe)}\" {sources}",
            "exit /b %errorlevel%",
            ":without_sanitizer",
            cpp
                ? $"rem No AddressSanitizer here: the terminate handler alone can still say what ended a C++ program.\r\ncl {flags} /Fe:\"{Path.GetFileName(exe)}\" {sources}\r\nexit /b %errorlevel%"
                : "echo FixFinder: the AddressSanitizer runtime was not found & exit /b 1",
            "",
        ]), new UTF8Encoding(false));

        var compile = new TargetSpec
        {
            ExecutablePath = "cmd.exe",
            Arguments = $"/c \"{batch}\"",
            WorkingDirectory = output,
            Timeout = normalRun.Timeout,
        };

        // The same arguments and the same typed answers, or the second run can fail somewhere the first never reached.
        var run = new TargetSpec
        {
            ExecutablePath = exe,
            Arguments = normalRun.Arguments,
            WorkingDirectory = normalRun.WorkingDirectory,
            Timeout = normalRun.Timeout,
            ExtraEnvironment = normalRun.ExtraEnvironment,
            StandardInput = normalRun.StandardInput,
        };

        return new BuildAndRun(compile, run, cpp
            ? $"Rebuilding it with {msvc.Name}, AddressSanitizer and a handler that names an exception nothing caught, then running it again."
            : $"Rebuilding it with {msvc.Name} and AddressSanitizer, then running it again.");
    }

    /// <summary>
    /// A terminate handler that prints what libstdc++ prints - <c>terminate called after throwing an instance of
    /// 'std::out_of_range'</c> and its <c>what()</c>, or <c>terminate called without an active exception</c> -
    /// so the parser and rules that already read g++'s words read MSVC's program too.
    /// </summary>
    /// <remarks>
    /// Installed by a static object before <c>main</c> runs. It never touches the program's own source: it is a
    /// separate file, compiled only into this diagnostic build, in FixFinder's build folder.
    /// </remarks>
    internal const string TerminateReporter = """
        // Written by FixFinder: says what ended the program, in the words g++'s runtime uses, because MSVC's says nothing.
        #include <cstdio>
        #include <cstdlib>
        #include <cstring>
        #include <exception>
        #include <typeinfo>

        namespace
        {
            const char* FixFinderNamed(const char* name)
            {
                if (std::strncmp(name, "class ", 6) == 0) return name + 6;
                if (std::strncmp(name, "struct ", 7) == 0) return name + 7;
                return name;
            }

            [[noreturn]] void FixFinderReport()
            {
                if (std::exception_ptr current = std::current_exception())
                {
                    try
                    {
                        std::rethrow_exception(current);
                    }
                    catch (const std::exception& e)
                    {
                        std::fprintf(stderr, "terminate called after throwing an instance of '%s'\n  what():  %s\n", FixFinderNamed(typeid(e).name()), e.what());
                    }
                    catch (...)
                    {
                        std::fprintf(stderr, "terminate called after throwing an instance of 'unknown'\n");
                    }
                }
                else
                {
                    std::fprintf(stderr, "terminate called without an active exception\n");
                }

                std::fflush(stderr);
                std::_Exit(3);
            }

            struct FixFinderInstall
            {
                FixFinderInstall() { std::set_terminate(FixFinderReport); }
            } fixFinderInstall;
        }
        """;

    // ------------------------------------------------------------------ Java

    private static (BuildAndRun?, string?) Java(string source, string output, TimeSpan timeout)
    {
        var javac = Toolchains.FindJavac();
        var java = Toolchains.FindJava();

        if (javac is null || java is null)
        {
            return (null,
                $"{Path.GetFileName(source)} is Java, which has to be compiled before it can run, " +
                "and no JDK was found.\n\n" +
                "Install one - for example:\n" +
                "  winget install Microsoft.OpenJDK.21\n\n" +
                "FixFinder looks on PATH and in the usual Program Files locations, so it does not " +
                "need to be on PATH.");
        }

        // -sourcepath at the root the package is named from, so classes the file uses - beside it, or in other packages
        // under the same root - are found and compiled with it.
        var compile = Spec(
            javac.Program,
            $"-g {JavaLint} -d \"{output}\" -sourcepath \"{ProgramLayout.JavaSourceRoot(source)}\" \"{source}\"",
            Path.GetDirectoryName(source)!,
            timeout);

        // The class to launch is the file name, qualified by whatever package it declares -
        // running "Main" when the file says "package app;" fails with NoClassDefFoundError, and
        // that error would be about FixFinder rather than about the program.
        var run = Spec(
            java.Program,
            $"-cp \"{output}\" {MainClass(source)}",
            output,
            timeout);

        return (new BuildAndRun(compile, run,
            $"Building it with {javac.Name}, then running it with java."), null);
    }

    private static string MainClass(string source)
    {
        var name = Path.GetFileNameWithoutExtension(source);

        try
        {
            var match = JavaPackagePattern().Match(File.ReadAllText(source));
            if (match.Success) return $"{match.Groups[1].Value}.{name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable source is the compiler's problem to report, not ours.
        }

        return name;
    }

    // ------------------------------------------------------------------ plumbing

    private static string Quoted(IEnumerable<string> paths) => string.Join(" ", paths.Select(p => $"\"{p}\""));

    /// <summary>" together with util.c and list.c", when the program is more than one file.</summary>
    private static string Along(IReadOnlyList<string> sources) => sources.Count switch
    {
        1 => "",
        2 => $" together with {Path.GetFileName(sources[1])}",
        _ => $" together with {string.Join(", ", sources.Skip(1).SkipLast(1).Select(Path.GetFileName))} and {Path.GetFileName(sources[^1])}",
    };

    private static TargetSpec Spec(string program, string arguments, string workingDirectory, TimeSpan timeout) =>
        new()
        {
            ExecutablePath = program,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            Timeout = timeout,
        };

    private static TargetSpec Run(string exe, string workingDirectory, TimeSpan timeout) =>
        new()
        {
            ExecutablePath = exe,
            WorkingDirectory = workingDirectory,
            Timeout = timeout,
        };

    /// <summary>
    /// A build folder per source file, outside the source tree.
    /// </summary>
    /// <remarks>
    /// Named from a hash of the full path so two files called <c>main.c</c> in different folders
    /// do not overwrite each other's output, and so re-running the same file reuses one place
    /// rather than filling the disk.
    /// </remarks>
    public static string OutputDirectory(string source)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(source).ToLowerInvariant())))[..8];

        var directory = Path.Combine(BuildRoot, $"{Path.GetFileNameWithoutExtension(source)}-{hash}");
        Directory.CreateDirectory(directory);

        return directory;
    }
}
