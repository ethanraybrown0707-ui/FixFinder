using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// The C and C++ rules that read MSVC's diagnostics, run live through MSVC with gcc hidden.
/// </summary>
/// <remarks>
/// The build prefers gcc whenever it is on PATH, and both this machine and the CI image have MinGW
/// there - so without hiding it, every MSVC path here would have gone unexercised while its tests
/// quietly skipped. <see cref="Toolchains.WithoutGnu"/> hides gcc for one test's async flow, leaving
/// every other test that runs at the same time untouched.
/// <para>
/// <b>Each case asserts that MSVC really did the build.</b> The worst way for this suite to fail is
/// the way it nearly did: green, having tested a different compiler. A build through MSVC runs
/// <c>cmd.exe</c> on a generated batch file, because cl.exe needs vcvarsall first; a gcc build
/// launches gcc directly.
/// </para>
/// <para>
/// These are slow by C standards - each one sets up the MSVC environment twice, once to build and
/// once to check the fix - and a case that finished in milliseconds skipped.
/// </para>
/// </remarks>
public class MsvcLocalFixLiveTests
{
    /// <summary>File name, source, the rule that should answer, what the copied fix contains, and which parser reads the error.</summary>
    public static TheoryData<string, string, string, string, string> Cases => new()
    {
        // ---------------------------------------------------------------- C: MSVC never suggests a name
        {
            "app.c",
            "#include <stdio.h>\nint main(void) {\n    int average = 3;\n    printf(\"%d\\n\", avarage);\n    return 0;\n}\n",
            "c-nearest-name", "printf(\"%d\\n\", average);", "msvc"
        },
        {
            "app.c",
            "#include <stdio.h>\nstruct parcel { int weight; };\nint main(void) {\n    struct parcel p = { 4 };\n    printf(\"%d\\n\", p.wieght);\n    return 0;\n}\n",
            "c-nearest-name", "p.weight", "msvc"
        },

        // A misspelt call is only a warning to cl; the error is LNK2019 from the linker, which names no line.
        {
            "app.c",
            "#include <stdio.h>\nint main(void) {\n    prinft(\"hello\\n\");\n    return 0;\n}\n",
            "c-nearest-name", "printf(\"hello\\n\");", "msvc"
        },

        // MSVC compiles C as C17, where bool still needs its header - unlike gcc 15's default of C23.
        {
            "app.c",
            "#include <stdio.h>\nint main(void) {\n    bool ready = true;\n    printf(\"%d\\n\", ready);\n    return 0;\n}\n",
            "c-missing-standard-header", "#include <stdbool.h>", "msvc"
        },
        { "app.c", "#include <stdoi.h>\nint main(void) { printf(\"hi\\n\"); return 0; }\n", "c-header-typo", "#include <stdio.h>", "msvc" },
        {
            "app.c",
            "#include <stdio.h>\nint main(void) {\n    int x = 3\n    printf(\"%d\\n\", x);\n    return 0;\n}\n",
            "c-missing-semicolon", "int x = 3;", "msvc"
        },
        { "app.c", "#include <stdio.h>\nint main(void) {\n    printf(\"hello\\n\");\n    return 0;\n", "c-missing-closing-brace", "}", "msvc" },

        // Builds with only a warning and crashes without a word. AddressSanitizer locates the crash -
        // its report is read by the gcc parser - and the build's C4013 warning supplies the fix.
        {
            "app.c",
            "#include <stdio.h>\nint main(void) {\n    char *buffer = malloc(64);\n    buffer[0] = 'x';\n    printf(\"%c\\n\", buffer[0]);\n    return 0;\n}\n",
            "c-missing-standard-header", "#include <stdlib.h>", "gcc"
        },

        // ---------------------------------------------------------------- C++: the std table
        {
            "app.cpp",
            "int main() {\n    std::cout << \"hello\" << std::endl;\n    return 0;\n}\n",
            "c-missing-standard-header", "#include <iostream>", "msvc"
        },
        {
            "app.cpp",
            "#include <iostream>\nint main() {\n    std::vector<int> values = {1, 2, 3};\n    std::cout << values.size() << std::endl;\n    return 0;\n}\n",
            "c-missing-standard-header", "#include <vector>", "msvc"
        },
        {
            "app.cpp",
            "int main() {\n    printf(\"hello\");\n    return 0;\n}\n",
            "c-missing-standard-header", "#include <stdio.h>", "msvc"
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task MsvcsErrorIsFixedFromTheCode(string fileName, string source, string rule, string expected, string readAs)
    {
        if (Toolchains.FindMsvc() is null) return;

        // The silent crash has to be located before its warning is read as the explanation.
        if (readAs == "gcc" && !LocalFixLiveTests.HasAddressSanitizer()) return;

        using var msvcOnly = Toolchains.WithoutGnu();
        using var temp = new TempFolder();

        var path = Path.Combine(temp.Path, fileName);
        File.WriteAllText(path, source);

        var plan = TargetFactory.FromFile(path, TimeSpan.FromMinutes(5));
        Assert.True(plan.Ok, plan.Problem);

        // MSVC builds through a generated batch file; gcc would be launched directly.
        Assert.Equal("cmd.exe", Path.GetFileName(plan.Compile?.ExecutablePath), StringComparer.OrdinalIgnoreCase);

        using var http = new FixFinderHttpClient();

        var outcome = await new FixFinderSession(http, new FixSourceRegistry())
            .RunAsync(plan, new SearchBudget(Cache: CacheMode.CacheOnly));

        Assert.True(outcome.Result == SessionResult.FoundFix, $"{rule} ({fileName}): {outcome.Result} - {outcome.Headline}");
        Assert.Equal($"local:{rule}", outcome.Best!.Id);
        Assert.Equal(readAs, outcome.Error?.LanguageId);

        var copied = PasteableFix.For(
            new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));

        Assert.NotNull(copied);
        Assert.Contains(expected, copied!.Text, StringComparison.Ordinal);
    }

    /// <summary>The switch itself: gcc is hidden inside the scope, and back exactly as it was afterwards.</summary>
    [Fact]
    public async Task HidingGccLastsForTheScopeAndOnlyInItsOwnFlow()
    {
        var before = Toolchains.FindGnu(cpp: false);
        Task<Toolchain?> outside;

        using (Toolchains.WithoutGnu())
        {
            Assert.Null(Toolchains.FindGnu(cpp: false));

            // Work this flow starts carries the setting with it, which is what the session relies on.
            Assert.Null(await Task.Run(() => Toolchains.FindGnu(cpp: true)));

            // A flow that does not descend from this one - as another test running alongside does
            // not - still sees gcc. Suppressing the flow is what makes this task such a stranger;
            // started normally it would inherit the setting and prove nothing.
            using (ExecutionContext.SuppressFlow())
            {
                outside = Task.Run(() => Toolchains.FindGnu(cpp: false));
            }
        }

        Assert.Equal(before?.Program, (await outside)?.Program);
        Assert.Equal(before?.Program, Toolchains.FindGnu(cpp: false)?.Program);
    }
}
