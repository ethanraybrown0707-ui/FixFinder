using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// C and C++ mistakes that run without failing - found from the build's warnings, or by running the program again under
/// AddressSanitizer - each ending with its fix, checked and ready to copy.
/// </summary>
/// <remarks>
/// Live, and every case runs the program it builds, so on a machine enforcing Application Control a freshly built one can
/// be refused and the case fail with CouldNotRun; the CodeIntegrity log says so. Each skips when its toolchain is missing.
/// </remarks>
public class SilentMistakeTests
{
    /// <summary>The toolchain, the file, the program, what to type into it, the rule, and text the copied fix must contain.</summary>
    public static TheoryData<string, string, string, string, string, string> Cases => new()
    {
        {
            "gcc", "app.c",
            "#include <stdio.h>\nint main(void) {\n    char name[20];\n    gets(name);\n    printf(\"Hello %s\\n\", name);\n    return 0;\n}\n",
            "Ethan",
            "c-gets", "if (fgets(name, sizeof name, stdin) != NULL) { char *end = name; while (*end && *end != '\\n') end++; *end = '\\0'; }"
        },
        {
            "gcc", "app.c",
            "#include <stdio.h>\nint main(void) {\n    char name[5] = \"Ethanol\";\n    printf(\"%s\\n\", name);\n    return 0;\n}\n",
            "",
            "c-string-too-long", "char name[] = \"Ethanol\";"
        },
        {
            "gcc", "app.c",
            "#include <stdio.h>\nchar *greeting(void) {\n    char text[] = \"hello\";\n    return text;\n}\nint main(void) {\n    printf(\"%s\\n\", greeting());\n    return 0;\n}\n",
            "",
            "c-return-local-address", "static char text[] = \"hello\";"
        },
        {
            "gcc", "app.cpp",
            "#include <iostream>\n\nint main() {\n    int *values = new int[10];\n    values[0] = 1;\n    std::cout << values[0] << std::endl;\n    delete values;\n    return 0;\n}\n",
            "",
            "cpp-delete-array", "delete[] values;"
        },
        {
            "msvc", "app.c",
            "#include <stdio.h>\nchar *greeting(void) {\n    char text[] = \"hello\";\n    return text;\n}\nint main(void) {\n    printf(\"%s\\n\", greeting());\n    return 0;\n}\n",
            "",
            "c-return-local-address", "static char text[] = \"hello\";"
        },
        {
            "gcc+asan", "app.c",
            "#include <stdio.h>\n#include <stdlib.h>\nint main(void) {\n    int *values = malloc(10);\n    for (int i = 0; i < 10; i++) values[i] = i;\n    printf(\"%d\\n\", values[9]);\n    free(values);\n    return 0;\n}\n",
            "",
            "c-malloc-element-size", "int *values = malloc(10 * sizeof *values);"
        },
        {
            "gcc+asan", "app.cpp",
            "#include <algorithm>\n#include <iostream>\n#include <vector>\n\nint main() {\n    std::vector<int> values = {1, 2, 3, 4, 5, 6};\n    for (int v : values) {\n        if (v % 2 == 0) {\n            values.erase(std::find(values.begin(), values.end(), v));\n        }\n    }\n    std::cout << values.size() << std::endl;\n    return 0;\n}\n",
            "",
            "cpp-erase-in-loop", "values.erase(std::remove_if(values.begin(), values.end(), [&](int v) { return v % 2 == 0; }), values.end());"
        },
    };

    private static bool Ready(string toolchain) => toolchain switch
    {
        "gcc" => Toolchains.FindGnu(cpp: true) is not null,
        "msvc" => Toolchains.FindMsvc() is not null,
        "gcc+asan" => Toolchains.FindGnu(cpp: true) is not null && Toolchains.FindMsvc() is not null && LocalFixLiveTests.HasAddressSanitizer(),
        _ => false,
    };

    private static async Task<SessionOutcome> Run(string toolchain, string fileName, string source, string input, TempFolder temp)
    {
        using var msvcOnly = toolchain == "msvc" ? Toolchains.WithoutGnu() : null;

        var path = Path.Combine(temp.Path, fileName);
        File.WriteAllText(path, source);

        var plan = TargetFactory.FromFile(path, TimeSpan.FromMinutes(5));
        Assert.True(plan.Ok, plan.Problem);

        using var http = new FixFinderHttpClient();
        return await new FixFinderSession(http, new FixSourceRegistry())
            .RunAsync(plan with { Spec = plan.Spec!.WithInput(input) }, new SearchBudget(Cache: CacheMode.CacheOnly));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task AMistakeThatRanWithoutFailingIsFoundAndFixed(string toolchain, string fileName, string source, string input, string rule, string expected)
    {
        if (!Ready(toolchain)) return;

        using var temp = new TempFolder();
        var outcome = await Run(toolchain, fileName, source, input, temp);

        if (ApplicationControl.Refused(outcome)) return;
        Assert.True(outcome.Result == SessionResult.FoundFix, $"{rule} ({toolchain}): {outcome.Result} - {outcome.Headline}");
        Assert.Equal($"local:{rule}", outcome.Best!.Id);

        var copied = PasteableFix.For(new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));
        Assert.NotNull(copied);
        Assert.Contains(expected, copied!.Text.ReplaceLineEndings("\n") + "\n", StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProgramWithNothingWrongStillRunsFine()
    {
        if (!Ready("gcc")) return;

        using var temp = new TempFolder();
        var outcome = await Run("gcc", "app.c",
            "#include <stdio.h>\n#include <stdlib.h>\nint main(void) {\n    int *values = malloc(10 * sizeof *values);\n    for (int i = 0; i < 10; i++) values[i] = i;\n    printf(\"%d\\n\", values[9]);\n    free(values);\n    return 0;\n}\n",
            "", temp);

        if (ApplicationControl.Refused(outcome)) return;
        Assert.Equal(SessionResult.RanFine, outcome.Result);
    }
}
