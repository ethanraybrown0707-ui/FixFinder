using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// The C or C++ standard a program's own code is written to, when nothing says which: the compiler is asked, for the
/// program's syntax only, whether it takes the program as the standards either side of the one FixFinder gave it.
/// </summary>
public class NativeStandardsTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("-g -O0 -std=c++17 -Wall -o \"x.exe\" \"a.cpp\"", "c++20", "-std=c++20 -g -O0 -Wall -o \"x.exe\" \"a.cpp\"")]
    [InlineData("-g -O0 -Wall -o \"x.exe\" \"a.c\"", "c2x", "-std=c2x -g -O0 -Wall -o \"x.exe\" \"a.c\"")]
    [InlineData("-g -O0 -std=c2x -Wall \"a.c\"", "", "-g -O0 -Wall \"a.c\"")]
    public void AStandardTakesThePlaceOfTheOneTheBuildHad(string arguments, string standard, string expected) =>
        Assert.Equal(expected, NativeStandards.WithStandard(arguments, standard));

    [Theory]
    [InlineData("c++20", "C++20")]
    [InlineData("c++14", "C++14")]
    [InlineData("c2x", "C23")]
    [InlineData("gnu17", "C17")]
    [InlineData("", "gcc's own standard")]
    public void AStandardIsShownAsAReaderKnowsIt(string standard, string shown) =>
        Assert.Equal(shown, NativeStandards.Shown(standard, "gcc"));

    /// <summary>A standard the program's Makefile gives is kept to: only one nobody chose is ever worked out.</summary>
    [Fact]
    public void AStandardTheBuildFileGivesIsKeptTo()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Path, "made"));
        File.WriteAllText(Path.Combine(_temp.Path, "made", "Makefile"), "CXXFLAGS = -Wall -std=c++17\napp: main.cpp\n\t$(CXX) $(CXXFLAGS) main.cpp -o app\n");
        var main = Path.Combine(_temp.Path, "made", "main.cpp");
        File.WriteAllText(main, "int main() { return 0; }\n");
        var plain = Path.Combine(_temp.Path, "plain.cpp");
        File.WriteAllText(plain, "int main() { return 0; }\n");

        Assert.False(NativeStandards.IsFixFindersChoice(main, cpp: true));
        Assert.True(NativeStandards.IsFixFindersChoice(plain, cpp: true));
    }
}

/// <summary>
/// Programs built with the gcc or g++ on this computer, as the window builds them: one written to a later standard than
/// FixFinder gives it is built as that, and says so in the compiler's own words; one with a mistake of its own is not.
/// </summary>
[Collection(SharedLanguageStandards.Name)]
public class NativeStandardsLiveTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Write(string name, string text)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
        return path;
    }

    private static async Task<CompilerReport?> BuildAsync(string file, CodeLanguage language)
    {
        var plan = TargetFactory.FromFile(file);
        if (!plan.Ok || plan.Compile is not { } compile || !Path.GetFileNameWithoutExtension(compile.ExecutablePath).Contains("g", StringComparison.OrdinalIgnoreCase)) return null;

        return await CompilerDiagnostics.CollectAsync(plan, [file], language);
    }

    [Fact]
    public async Task ConceptsAreBuiltAsCpp20AndHowItRanSaysWhy()
    {
        if (Toolchains.FindGnu(cpp: true) is null) return;

        var main = Write("main.cpp", """
            #include <iostream>

            template <typename T>
            concept Number = requires(T value) { value + value; };

            int twice(Number auto value) { return value + value; }

            int main() { std::cout << twice(21) << "\n"; }
            """);

        if (await BuildAsync(main, CodeLanguage.Cpp) is not { } report) return;

        Assert.Empty(report.Errors);
        Assert.StartsWith("main.cpp does not compile as C++17 - ", report.Problem);
        Assert.Contains(" said of main.cpp:4: ", report.Problem);
        Assert.EndsWith("- and does as C++20, so it was built as C++20. Choosing a standard in Settings holds every program to that one instead.", report.Problem);
        Assert.Equal("c++20", NativeStandards.RememberedFor(main));
        Assert.Contains(" as C++20, the standard its code was found to be written to,", TargetFactory.FromFile(main).Explanation);
    }

    /// <summary>C23's bool and constexpr: built as C23 by a gcc whose own C is older - and as they are by one whose own is C23.</summary>
    [Fact]
    public async Task CodeWrittenToC23IsBuiltAsC23()
    {
        if (Toolchains.FindGnu(cpp: false) is null) return;

        var main = Write("main.c", """
            #include <stdio.h>

            int main(void) {
                constexpr int marks = 3;
                bool passed = true;
                printf("%d %d\n", marks, passed);
                return 0;
            }
            """);

        if (await BuildAsync(main, CodeLanguage.C) is not { } report) return;

        Assert.Empty(report.Errors);
        if (report.Problem is null) return;

        Assert.Contains("- and does as C23, so it was built as C23.", report.Problem);
        Assert.Equal("c2x", NativeStandards.RememberedFor(main));
    }

    /// <summary>A mistake of the program's own is wrong as every standard: its errors are kept, and nothing is said of standards.</summary>
    [Fact]
    public async Task AProgramsOwnMistakeIsNotTakenForAnotherStandard()
    {
        if (Toolchains.FindGnu(cpp: true) is null) return;

        var main = Write("mistake.cpp", "#include <iostream>\n\nint main() {\n    std::cout << total << \"\\n\";\n}\n");

        if (await BuildAsync(main, CodeLanguage.Cpp) is not { } report) return;

        Assert.NotEmpty(report.Errors);
        Assert.Null(report.Problem);
        Assert.Null(NativeStandards.RememberedFor(main));
    }
}
