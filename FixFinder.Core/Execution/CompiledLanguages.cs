using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>How to build one source file, and how to run what comes out.</summary>
public sealed record BuildAndRun(TargetSpec Compile, TargetSpec Run, string Explanation)
{
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

/// <summary>Builds C, C++ and Java source before running it.</summary>
public static partial class CompiledLanguages
{
    public static string BuildRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FixFinder", "build");

    [GeneratedRegex(@"^\s*package\s+([A-Za-z_][\w.]*)\s*;", RegexOptions.Multiline)]
    private static partial Regex JavaPackagePattern();

    public static bool Handles(string extension) => extension.ToLowerInvariant() switch
    {
        ".c" or ".cpp" or ".cc" or ".cxx" or ".c++" or ".java" => true,
        _ => false,
    };

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

    private static (BuildAndRun?, string?) Native(string source, string output, TimeSpan timeout, bool cpp)
    {
        var language = cpp ? "C++" : "C";
        var exe = Path.Combine(output, Path.GetFileNameWithoutExtension(source) + ".exe");

        var sources = ProgramLayout.NativeSources(source);

        if (Toolchains.FindGnu(cpp) is { } gnu)
        {
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

    private static string WriteMsvcBatch(IReadOnlyList<string> sources, string exe, string output, string vcvarsall, bool cpp)
    {
        var flags = cpp ? "/nologo /Zi /W3 /EHsc /std:c++17" : "/nologo /Zi /W3";

        var batch = Path.Combine(output, "build.cmd");

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

    internal static string GnuWarnings(bool cpp) => cpp
        ? "-std=c++17 -Wall -Wextra -Wno-unused-parameter -Wmismatched-new-delete -Wdelete-non-virtual-dtor -Wcatch-value -Waddress "
        : "-Wall -Wextra -Wno-unused-parameter -Wno-missing-field-initializers -Waddress ";

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

        var compile = Spec(
            javac.Program,
            $"-g {JavaLint} -d \"{output}\" -sourcepath \"{ProgramLayout.JavaSourceRoot(source)}\" \"{source}\"",
            Path.GetDirectoryName(source)!,
            timeout);

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
        }

        return name;
    }

    private static string Quoted(IEnumerable<string> paths) => string.Join(" ", paths.Select(p => $"\"{p}\""));

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

    public static string OutputDirectory(string source)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(source).ToLowerInvariant())))[..8];

        var directory = Path.Combine(BuildRoot, $"{Path.GetFileNameWithoutExtension(source)}-{hash}");
        Directory.CreateDirectory(directory);

        return directory;
    }
}
