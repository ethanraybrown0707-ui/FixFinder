using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>The C and C++ rules that read MSVC's diagnostics, run live through MSVC with gcc hidden.</summary>
public class MsvcLocalFixLiveTests
{
    public static TheoryData<string, string, string, string, string> Cases => new()
    {
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

        {
            "app.c",
            "#include <stdio.h>\nint main(void) {\n    prinft(\"hello\\n\");\n    return 0;\n}\n",
            "c-nearest-name", "printf(\"hello\\n\");", "msvc"
        },

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

        {
            "app.c",
            "#include <stdio.h>\nint main(void) {\n    char *buffer = malloc(64);\n    buffer[0] = 'x';\n    printf(\"%c\\n\", buffer[0]);\n    return 0;\n}\n",
            "c-missing-standard-header", "#include <stdlib.h>", "gcc"
        },

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

        if (readAs == "gcc" && !LocalFixLiveTests.HasAddressSanitizer()) return;

        using var msvcOnly = Toolchains.WithoutGnu();
        using var temp = new TempFolder();

        var path = Path.Combine(temp.Path, fileName);
        File.WriteAllText(path, source);

        var plan = TargetFactory.FromFile(path, TimeSpan.FromMinutes(5));
        Assert.True(plan.Ok, plan.Problem);

        Assert.Equal("cmd.exe", Path.GetFileName(plan.Compile?.ExecutablePath), StringComparer.OrdinalIgnoreCase);

        using var http = new FixFinderHttpClient();

        var outcome = await new FixFinderSession(http, new FixSourceRegistry())
            .RunAsync(plan, new SearchBudget(Cache: CacheMode.CacheOnly));

        if (ApplicationControl.Refused(outcome)) return;
        Assert.True(outcome.Result == SessionResult.FoundFix, $"{rule} ({fileName}): {outcome.Result} - {outcome.Headline}");
        Assert.Equal($"local:{rule}", outcome.Best!.Id);
        Assert.Equal(readAs, outcome.Error?.LanguageId);

        var copied = PasteableFix.For(
            new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));

        Assert.NotNull(copied);
        Assert.Contains(expected, copied!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HidingGccLastsForTheScopeAndOnlyInItsOwnFlow()
    {
        var before = Toolchains.FindGnu(cpp: false);
        Task<Toolchain?> outside;

        using (Toolchains.WithoutGnu())
        {
            Assert.Null(Toolchains.FindGnu(cpp: false));

            Assert.Null(await Task.Run(() => Toolchains.FindGnu(cpp: true)));

            using (ExecutionContext.SuppressFlow())
            {
                outside = Task.Run(() => Toolchains.FindGnu(cpp: false));
            }
        }

        Assert.Equal(before?.Program, (await outside)?.Program);
        Assert.Equal(before?.Program, Toolchains.FindGnu(cpp: false)?.Program);
    }
}
