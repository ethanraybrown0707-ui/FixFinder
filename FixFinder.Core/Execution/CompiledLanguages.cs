using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution.Libraries;

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
        var start = WorkingFolder.For(source);

        var sources = ProgramLayout.NativeSources(source);

        if (Toolchains.FindGnu(cpp) is { } gnu)
        {
            var standard = GnuWarnings(cpp);

            var compile = Spec(
                gnu.Program,
                $"-g -O0 -Wformat -fdiagnostics-parseable-fixits {standard}-o \"{exe}\" {Quoted(sources)}",
                Path.GetDirectoryName(source)!,
                timeout);

            return (new BuildAndRun(compile, Run(exe, start.Folder, timeout),
                $"Building it{Along(sources)} with {gnu.Name}, then running the result{StartsFrom(start, source)}."), null);
        }

        if (Toolchains.FindMsvc() is { SetupScript: { } script } msvc)
        {
            var batch = WriteMsvcBatch(sources, exe, output, script, cpp);

            var compile = Spec("cmd.exe", $"/c \"{batch}\"", output, timeout);

            return (new BuildAndRun(compile, Run(exe, start.Folder, timeout),
                $"Building it{Along(sources)} with {msvc.Name}, then running the result{StartsFrom(start, source)}."), null);
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
        var flags = MsvcFlags(cpp, debugInfo: true);

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

    /// <summary>
    /// The standard the program is held to, then the warnings. Used both to build the program and to check every fix,
    /// so a fix that needs a later standard than the one chosen fails its check and is never offered.
    /// </summary>
    internal static string GnuWarnings(bool cpp) => LanguageStandards.Current.Gnu(cpp) + (cpp
        ? "-Wall -Wextra -Wno-unused-parameter -Wmismatched-new-delete -Wdelete-non-virtual-dtor -Wcatch-value -Waddress "
        : "-Wall -Wextra -Wno-unused-parameter -Wno-missing-field-initializers -Waddress ");

    /// <summary>MSVC's flags, with the standard the program is held to where MSVC has a flag for it.</summary>
    internal static string MsvcFlags(bool cpp, bool debugInfo) =>
        (debugInfo ? "/nologo /Zi /W3" : "/nologo /W3") + (cpp ? " /EHsc" : "") + LanguageStandards.Current.Msvc(cpp);

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
        var flags = MsvcFlags(cpp, debugInfo: true);
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

        var libraries = JavaLibraries.For(source);

        // A class of JUnit tests is run with JUnit, through a launcher of FixFinder's compiled beside it - but only when
        // everything JUnit needs is here; otherwise it is left to say it has no main, and why its tests could not be run.
        var framework = JavaTests.FrameworkOf(source);
        var runner = framework == JavaTests.Framework.None ? null : JavaTests.RunnerFor(framework, libraries.ClassPath, JavaLibraries.CurrentStores);
        var launcher = runner is { CannotRun: null } ? WriteTestLauncher(output, framework) : null;

        IReadOnlyList<string> classPath = [.. libraries.ClassPath, .. launcher is null ? [] : runner!.ExtraJars];
        var sourcePath = string.Join(Path.PathSeparator, [ProgramLayout.JavaSourceRoot(source), .. libraries.OtherSourceRoots(source)]);
        var libraryPath = classPath.Count > 0 ? $" -cp \"{string.Join(Path.PathSeparator, classPath)}\"" : "";
        var launcherSource = launcher is null ? "" : $" \"{launcher}\"";

        var compile = Spec(
            javac.Program,
            ShortEnough($"-g {LanguageStandards.Current.JavaRelease}{JavaLint} -d \"{output}\"{libraryPath} -sourcepath \"{sourcePath}\" \"{source}\"{launcherSource}", output, "javac"),
            Path.GetDirectoryName(source)!,
            timeout);

        var start = WorkingFolder.For(source);
        var runPath = string.Join(Path.PathSeparator, [output, .. libraries.Resources.Select(folder => ProgramCopy.InCopyOf(source, folder)), .. classPath]);
        var entry = launcher is null ? MainClass(source) : $"{JavaTests.LauncherClass} {MainClass(source)}";

        var run = Spec(
            java.Program,
            ShortEnough($"-cp \"{runPath}\" {entry}", output, "java"),
            start.Folder,
            timeout);

        var with = libraries.Described is { } described ? $" and {described}" : "";
        var then = launcher is null ? "running it with java" : $"running its tests with {(framework == JavaTests.Framework.JUnit4 ? "JUnit 4" : "JUnit 5")}";

        return (new BuildAndRun(compile, run,
            $"Building it with {javac.Name}{with}, then {then}{StartsFrom(start, source)}."), null);
    }

    /// <summary>Writes FixFinder's JUnit launcher into the build folder, in a folder of its own, and says where.</summary>
    private static string WriteTestLauncher(string output, JavaTests.Framework framework)
    {
        var folder = Path.Combine(output, "fixfinder-tests");
        Directory.CreateDirectory(folder);

        var launcher = Path.Combine(folder, JavaTests.LauncherClass + ".java");
        File.WriteAllText(launcher, JavaTests.LauncherSource(framework), new UTF8Encoding(false));
        return launcher;
    }

    /// <summary>
    /// The arguments as they are, or - when a project's libraries make them longer than Windows lets a command line be -
    /// an @file holding them, which javac and java both read.
    /// </summary>
    private static string ShortEnough(string arguments, string output, string tool)
    {
        if (arguments.Length < 24_000) return arguments;

        var file = Path.Combine(output, $"{tool}-arguments.txt");
        File.WriteAllText(file, arguments.Replace("\\", "\\\\", StringComparison.Ordinal), new UTF8Encoding(false));
        return $"\"@{file}\"";
    }

    /// <summary>Which folder the program starts from, said only when it is not simply the one the file is in.</summary>
    private static string StartsFrom(WorkingFolder.Choice start, string source)
    {
        if (string.Equals(start.Folder, Path.GetDirectoryName(Path.GetFullPath(source)), StringComparison.OrdinalIgnoreCase)) return "";

        var folder = Path.GetFileName(start.Folder);
        if (start.FileFound is { } file) return $" from {folder}, where {file} is";

        return start.IsProjectFolder ? $" from {folder}, the folder that holds src, as an IDE runs it" : $" from {folder}";
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
