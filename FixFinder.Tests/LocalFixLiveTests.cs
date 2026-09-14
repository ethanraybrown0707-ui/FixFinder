using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// A real mistake, run through the real session with a real compiler, and the fix that comes out.
/// </summary>
/// <remarks>
/// No web source is registered, so whatever comes back was worked out from the code - and checked
/// by compiling a copy - on this machine. Each case asserts the fix was found, that it was this rule
/// that found it, and what a person would actually get when they press Copy.
/// <para>
/// <b>Each case skips when its toolchain is missing or too old</b> - Python before 3.12 does not
/// print the hints the Python rules read, and Java before 11 does not print the index and length
/// the off-by-one rule reads. A skip looks exactly like a pass, which is the trap recorded on
/// <see cref="LiveRuntimeTests"/>: a C case that ran is seconds, not milliseconds.
/// </para>
/// </remarks>
public class LocalFixLiveTests
{
    public static TheoryData<string, string, string, string, string> Cases => new()
    {
        // ---------------------------------------------------------------- Python
        { "python", "app.py", "print(math.sqrt(16))\n", "python-forgotten-import", "import math" },
        { "python", "app.py", "x = 3\nif x > 1\n    print(x)\n", "python-expected-colon", "if x > 1:" },
        { "python", "app.py", "name = \"sam\"\nprint \"hello\", name\n", "python-print-statement", "print(\"hello\", name)" },
        { "python", "app.py", "x = 3\nif x = 3:\n    print(x)\n", "python-assignment-in-condition", "if x == 3:" },
        { "python", "app.py", "def total(values):\nreturn sum(values)\n\nprint(total([1, 2]))\n", "python-indented-block", "    return sum(values)" },
        { "python", "app.py", "total = 42\nprint(\"Total: \" + total)\n", "python-str-concatenation", "print(\"Total: \" + str(total))" },
        { "python", "app.py", "count = 0\ndef bump():\n    count += 1\nbump()\n", "python-unbound-global", "    global count" },

        // ---------------------------------------------------------------- Java
        {
            "java", "App.java",
            "public class App {\n    public static void main(String[] args) {\n        List<String> names = new ArrayList<>();\n        names.add(\"sam\");\n        System.out.println(names);\n    }\n}\n",
            "java-missing-import", "import java.util.List;"
        },
        {
            "java", "App.java",
            "public class App {\n    public static void main(String[] args) {\n        int average = 3;\n        System.out.println(avarage);\n    }\n}\n",
            "java-nearest-name", "System.out.println(average);"
        },
        {
            "java", "App.java",
            "public class App {\n    public static void main(String[] args) {\n        int x = 3\n        System.out.println(x);\n    }\n}\n",
            "java-missing-semicolon", "int x = 3;"
        },
        {
            "java", "App.java",
            "public class App {\n    public static void main(String[] args) {\n        Thread.sleep(10);\n        System.out.println(\"done\");\n    }\n}\n",
            "java-unreported-exception", "main(String[] args) throws InterruptedException {"
        },
        {
            "java", "App.java",
            "public class App {\n    int total() { return 3; }\n    public static void main(String[] args) {\n        System.out.println(total());\n    }\n}\n",
            "java-non-static-member", "static int total()"
        },
        {
            "java", "App.java",
            "public class Main {\n    public static void main(String[] args) {\n        System.out.println(\"hello\");\n    }\n}\n",
            "java-public-class-name", "public class App {"
        },
        {
            "java", "App.java",
            "public class App {\n    public static void main(String[] args) {\n        int[] values = {1, 2, 3};\n        for (int i = 0; i <= values.length; i++) {\n            System.out.println(values[i]);\n        }\n    }\n}\n",
            "java-off-by-one-loop", "i < values.length"
        },
        {
            "java", "App.java",
            "public class App {\n    public static void main(String[] args) {\n        int count = \"5\";\n        System.out.println(count);\n    }\n}\n",
            "java-string-conversion", "Integer.parseInt(\"5\")"
        },

        // ---------------------------------------------------------------- C, with whichever compiler the build picks
        // uint8_t rather than bool, which is what this case first used. C23 made bool a keyword, gcc 15
        // defaults to C23, and on the CI runner the program without <stdbool.h> simply compiled and ran
        // - correctly reported as nothing to fix. A missing <stdint.h> is an error under every standard
        // and both compilers.
        {
            "c", "app.c",
            "#include <stdio.h>\nint main(void) {\n    uint8_t count = 3;\n    printf(\"%d\\n\", count);\n    return 0;\n}\n",
            "c-missing-standard-header", "#include <stdint.h>"
        },
        {
            "c", "app.c",
            "#include <stdio.h>\nint main(void) {\n    int average = 3;\n    printf(\"%d\\n\", avarage);\n    return 0;\n}\n",
            "c-nearest-name", "printf(\"%d\\n\", average);"
        },
        {
            "c", "app.c",
            "#include <stdio.h>\nstruct parcel { int weight; };\nint main(void) {\n    struct parcel p = { 4 };\n    printf(\"%d\\n\", p.wieght);\n    return 0;\n}\n",
            "c-nearest-name", "p.weight"
        },
        {
            "c", "app.c",
            "#include <stdio.h>\nint main(void) {\n    prinft(\"hello\\n\");\n    return 0;\n}\n",
            "c-nearest-name", "printf(\"hello\\n\");"
        },
        { "c", "app.c", "#include <stdoi.h>\nint main(void) { printf(\"hi\\n\"); return 0; }\n", "c-header-typo", "#include <stdio.h>" },
        {
            "c", "app.c",
            "#include <stdio.h>\nint main(void) {\n    int x = 3\n    printf(\"%d\\n\", x);\n    return 0;\n}\n",
            "c-missing-semicolon", "int x = 3;"
        },
        { "c", "app.c", "#include <stdio.h>\nint main(void) {\n    printf(\"hello\\n\");\n    return 0;\n", "c-missing-closing-brace", "}" },

        // ---------------------------------------------------------------- C++, with whichever compiler the build picks
        {
            "cpp", "app.cpp",
            "int main() {\n    std::cout << \"hello\" << std::endl;\n    return 0;\n}\n",
            "c-missing-standard-header", "#include <iostream>"
        },
        {
            "cpp", "app.cpp",
            "#include <iostream>\nint main() {\n    std::vector<int> values = {1, 2, 3};\n    std::cout << values.size() << std::endl;\n    return 0;\n}\n",
            "c-missing-standard-header", "#include <vector>"
        },

        // Compiles with a warning, then dies without a word - the warning is the whole story. MSVC
        // only: gcc knows malloc as a built-in, and the program it builds does not crash.
        {
            "msvc", "app.c",
            "#include <stdio.h>\nint main(void) {\n    char *buffer = malloc(64);\n    buffer[0] = 'x';\n    printf(\"%c\\n\", buffer[0]);\n    return 0;\n}\n",
            "c-missing-standard-header", "#include <stdlib.h>"
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TheFixIsWorkedOutCheckedAndReadyToCopy(
        string toolchain, string fileName, string source, string rule, string expected)
    {
        if (!Available(toolchain)) return;

        // An MSVC-only case hides gcc for its own flow, as the other MSVC tests do. Waiting for a machine without gcc meant
        // waiting for one that neither this machine nor CI is, and the case never ran anywhere.
        using var msvcOnly = toolchain == "msvc" ? Toolchains.WithoutGnu() : null;
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, fileName);
        File.WriteAllText(path, source);

        var outcome = await RunOffline(path);

        Assert.True(outcome.Result == SessionResult.FoundFix, $"{rule}: {outcome.Result} - {outcome.Headline}");
        // gcc says more than MSVC does: its own did-you-mean and fix-its answer several C cases
        // before a rule is needed, and either answer is the right one.
        string[] acceptable = toolchain is "c" or "cpp"
            ? [$"local:{rule}", "local:c-compiler-fix-it", "gcc:did-you-mean"]
            : [$"local:{rule}"];

        Assert.Contains(outcome.Best!.Id, acceptable);

        var copied = PasteableFix.For(
            new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));

        Assert.NotNull(copied);
        Assert.Contains(expected, copied!.Text, StringComparison.Ordinal);
    }

    /// <summary>A null write prints nothing on Windows; the rerun under AddressSanitizer names the line.</summary>
    [Fact]
    public async Task ACrashThatPrintedNothingIsLocatedByAddressSanitizer()
    {
        if (Toolchains.FindMsvc() is null || !HasAddressSanitizer()) return;

        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "app.c");
        File.WriteAllText(path, "#include <stdio.h>\nint main(void) {\n    int *p = NULL;\n    printf(\"starting\\n\");\n    *p = 5;\n    return 0;\n}\n");

        var outcome = await RunOffline(path);

        Assert.NotNull(outcome.Error);
        Assert.Contains("access-violation", outcome.Error!.RawText, StringComparison.Ordinal);
        Assert.Equal(5, (outcome.Error.CulpritFrame ?? outcome.Error.Frames[0]).Line);
    }

    private static async Task<SessionOutcome> RunOffline(string path)
    {
        var plan = TargetFactory.FromFile(path, TimeSpan.FromMinutes(3));
        Assert.True(plan.Ok, plan.Problem);

        using var http = new FixFinderHttpClient();

        return await new FixFinderSession(http, new FixSourceRegistry())
            .RunAsync(plan, new SearchBudget(Cache: CacheMode.CacheOnly));
    }

    // ------------------------------------------------------------------ what this machine has

    private static readonly Lazy<int?> PythonMinor = new(() =>
        TargetFactory.FindOnPath("python") is { } python &&
        Output(python, "-c \"import sys; print(sys.version_info[0] * 100 + sys.version_info[1])\"") is { } text &&
        int.TryParse(text.Trim(), out var version) ? version : null);

    private static readonly Lazy<int?> JavaMajor = new(() =>
        Toolchains.FindJavac() is { } javac &&
        Output(javac.Program, "-version") is { } text &&
        Regex.Match(text, @"javac (?<major>\d+)(?:\.(?<minor>\d+))?") is { Success: true } match
            ? int.Parse(match.Groups["major"].Value) == 1 ? int.Parse(match.Groups["minor"].Value) : int.Parse(match.Groups["major"].Value)
            : null);

    internal static bool Available(string toolchain) => toolchain switch
    {
        "python" => PythonMinor.Value >= 312,
        "java" => JavaMajor.Value >= 11 && Toolchains.FindJava() is not null,

        "c" => Toolchains.FindGnu(cpp: false) is not null || Toolchains.FindMsvc() is not null,
        "cpp" => Toolchains.FindGnu(cpp: true) is not null || Toolchains.FindMsvc() is not null,

        // The build prefers gcc when it is on PATH, so an MSVC-only case hides it for its own run.
        "msvc" => Toolchains.FindMsvc() is not null,
        _ => false,
    };

    internal static bool HasAddressSanitizer()
    {
        if (Toolchains.FindMsvc()?.SetupScript is not { } vcvarsall) return false;

        // vcvarsall.bat lives in VC\Auxiliary\Build; the runtime is under VC\Tools\MSVC\<version>\bin.
        var vc = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(vcvarsall)!, "..", ".."));
        var tools = Path.Combine(vc, "Tools", "MSVC");

        return Directory.Exists(tools) && Directory
            .EnumerateFiles(tools, "clang_rt.asan_dynamic-x86_64.dll", SearchOption.AllDirectories)
            .Any();
    }

    private static string? Output(string program, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(program, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });

            if (process is null) return null;

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(30_000)) return null;

            return stdout.Result + stderr.Result;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
