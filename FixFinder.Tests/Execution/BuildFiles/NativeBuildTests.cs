using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// Which program a C or C++ file is part of, by the Makefile or CMakeLists.txt above it - and, when that is not one
/// program, building it as if there were no build file and saying why, rather than choosing one at random.
/// </summary>
public class NativeBuildTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Write(string relative, params string[] lines)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        return path;
    }

    private string At(string relative) => Path.GetFullPath(Path.Combine(_temp.Path, relative));

    /// <summary>A src/include project with a Makefile at its top, as courses hand them out.</summary>
    private void SrcIncludeProject(string folder = "project")
    {
        Write($"{folder}/include/list.h", "int sum(const int *values, int count);");
        Write($"{folder}/src/list.c", "#include \"list.h\"", "int sum(const int *values, int count) { int total = 0; for (int i = 0; i < count; i++) total += values[i]; return total; }");
        Write($"{folder}/src/main.c", "#include <stdio.h>", "#include \"list.h\"", "int main(void) { int marks[] = {1, 2, 3}; printf(\"%d\\n\", sum(marks, 3)); return 0; }");
        Write($"{folder}/Makefile",
            "CFLAGS = -Wall -std=c11 -Iinclude",
            "SRC = $(wildcard src/*.c)",
            "OBJ = $(SRC:src/%.c=obj/%.o)",
            "app: $(OBJ)",
            "\t$(CC) $^ -o $@ -lm",
            "obj/%.o: src/%.c",
            "\t$(CC) $(CFLAGS) -c $< -o $@");
    }

    [Fact]
    public void TheProgramAFileIsPartOfIsFoundInTheMakefileAboveIt()
    {
        SrcIncludeProject();

        var lookup = NativeBuild.For(At("project/src/main.c"));
        var build = Assert.IsType<NativeBuild>(lookup.Build);

        Assert.Equal(At("project/Makefile"), build.BuildFile, ignoreCase: true);
        Assert.Equal("app", build.Program);
        Assert.Equal([At("project/src/main.c"), At("project/src/list.c")], build.Sources);
        Assert.Equal([At("project/include")], build.IncludeFolders);
        Assert.Equal("c11", build.Standard);
        Assert.Equal(["m"], build.Libraries);
        Assert.Null(lookup.Note);
    }

    /// <summary>The file chosen comes first, whichever of the program's files it is: it is the one being checked.</summary>
    [Fact]
    public void TheChosenFileComesFirstAmongTheSources()
    {
        SrcIncludeProject();

        var build = NativeBuild.For(At("project/src/list.c")).Build!;

        Assert.Equal([At("project/src/list.c"), At("project/src/main.c")], build.Sources);
    }

    [Fact]
    public void AFileInMoreThanOneProgramIsBuiltAsIfThereWereNoBuildFileAndTheNoteSaysWhy()
    {
        Write("labs/ex1.c", "int main(void) { return 0; }");
        Write("labs/ex2.c", "int main(void) { return 0; }");
        Write("labs/helper.c", "int helper(void) { return 1; }");
        Write("labs/Makefile",
            "all: ex1 ex2",
            "ex1: ex1.c helper.c",
            "\tgcc -o ex1 ex1.c helper.c",
            "ex2: ex2.c helper.c",
            "\tgcc -o ex2 ex2.c helper.c");

        var shared = NativeBuild.For(At("labs/helper.c"));
        var own = NativeBuild.For(At("labs/ex2.c"));

        Assert.Null(shared.Build);
        Assert.Contains("more than one program - ex1 and ex2", shared.Note, StringComparison.Ordinal);
        Assert.Equal("ex2", own.Build?.Program);
    }

    /// <summary>A file of a library both the program and its tests are built with is checked as part of the program.</summary>
    [Fact]
    public void AFileOfTheProgramAndItsTestsIsBuiltAsTheProgram()
    {
        Write("shop/src/main.c", "int main(void) { return 0; }");
        Write("shop/src/basket.c", "int total(void) { return 0; }");
        Write("shop/tests/test_basket.c", "int main(void) { return 0; }");
        Write("shop/CMakeLists.txt",
            "project(shop C)",
            "add_library(basket src/basket.c)",
            "add_executable(shop src/main.c)",
            "target_link_libraries(shop PRIVATE basket)",
            "add_executable(basket_tests tests/test_basket.c)",
            "target_link_libraries(basket_tests PRIVATE basket)",
            "add_test(NAME basket COMMAND basket_tests)");

        Assert.Equal("shop", NativeBuild.For(At("shop/src/basket.c")).Build?.Program);
        Assert.Equal("basket_tests", NativeBuild.For(At("shop/tests/test_basket.c")).Build?.Program);
    }

    [Fact]
    public void AHeaderIsBuiltWithTheProgramWhoseSourcesIncludeIt()
    {
        SrcIncludeProject();

        var build = Assert.IsType<NativeBuild>(NativeBuild.ForHeader(At("project/include/list.h")));

        Assert.Equal("app", build.Program);
        Assert.Contains(At("project/src/main.c"), build.Sources);
    }

    /// <summary>A copy FixFinder runs a change in is built from the copy's files, never the person's own.</summary>
    [Fact]
    public void ACopyOfTheProgramIsBuiltFromTheCopy()
    {
        SrcIncludeProject();
        var copy = At("copy-of-project");
        CopyFolder(At("project"), copy);

        ProgramCopy.Remember(copy, At("project"));

        try
        {
            var build = NativeBuild.For(Path.Combine(copy, "src", "main.c")).Build!;

            Assert.All(build.Sources, source => Assert.StartsWith(copy, source, StringComparison.OrdinalIgnoreCase));
            Assert.Equal([Path.Combine(copy, "include")], build.IncludeFolders);
        }
        finally
        {
            ProgramCopy.Forget(copy);
        }
    }

    /// <summary>With a CMakeLists.txt and a Makefile in the same folder, CMake's is read: an IDE builds with it.</summary>
    [Fact]
    public void ACMakeListsBesideAMakefileIsTheOneRead()
    {
        Write("both/main.c", "int main(void) { return 0; }");
        Write("both/CMakeLists.txt", "add_executable(from_cmake main.c)");
        Write("both/Makefile", "from_make: main.c", "\tgcc -o from_make main.c");

        Assert.Equal("from_cmake", NativeBuild.For(At("both/main.c")).Build?.Program);
    }

    /// <summary>A build file that does not build the file at all - a scratch file beside a project - leaves it built on its own.</summary>
    [Fact]
    public void AFileNoProgramIsBuiltFromIsNotGivenABuild()
    {
        SrcIncludeProject();
        Write("project/scratch/try.c", "int main(void) { return 0; }");

        var lookup = NativeBuild.For(At("project/scratch/try.c"));

        Assert.Null(lookup.Build);
        Assert.Null(lookup.Note);
    }

    /// <summary>A definition with a character a shell or cmd would act on is not put on a command line, and the explanation says so.</summary>
    [Fact]
    public void ADefinitionAShellWouldActOnIsNotPassedOn()
    {
        Write("risky/main.c", "int main(void) { return 0; }");
        Write("risky/Makefile",
            "app: main.c",
            "\tgcc -DSAFE=1 '-DRISKY=a&calc' -o app main.c");

        var build = NativeBuild.For(At("risky/main.c")).Build!;

        Assert.Equal(["SAFE=1"], build.Definitions);
        Assert.Contains(build.NotFollowed, note => note.Contains("RISKY=a&calc", StringComparison.Ordinal));
    }

    /// <summary>Paths with spaces and a definition with quotes reach gcc exactly as written, each one argument.</summary>
    [Fact]
    public void TheFlagsAreWrittenSoTheCompilerReadsThemBackAsTheyWere()
    {
        var build = new NativeBuild(
            BuildFile: @"C:\My Course\lab\Makefile",
            Program: "app",
            Sources: [@"C:\My Course\lab\main.c"],
            IncludeFolders: [@"C:\My Course\lab\include"],
            Definitions: ["VERSION=\"1.0\"", "DEBUG"],
            Libraries: ["m", "ws2_32"],
            LibraryFolders: [@"C:\My Course\lab\lib"],
            LinkedFiles: [],
            Threads: true,
            Standard: "c11",
            NotFollowed: []);

        Assert.Equal("-I \"C:\\My Course\\lab\\include\" \"-DVERSION=\\\"1.0\\\"\" -DDEBUG ", build.GnuCompileFlags);
        Assert.Equal(" -L \"C:\\My Course\\lab\\lib\" -lm -lws2_32 -pthread", build.GnuLinkFlags);

        // MSVC's C runtime has the maths functions already, and has no -pthread.
        Assert.Equal(" ws2_32.lib /link /LIBPATH:\"C:\\My Course\\lab\\lib\"", build.MsvcLinkFlags);
        Assert.Equal("-I include -DVERSION=\"1.0\" -DDEBUG -lm -lws2_32 -pthread", build.FlagsShown);
    }

    /// <summary>How it ran says the program was built as its Makefile builds it, with what, and where it starts.</summary>
    [Fact]
    public void HowItRanSaysWhichBuildFileBuiltItAndWithWhat()
    {
        if (Toolchains.FindGnu(cpp: false) is null && Toolchains.FindMsvc() is null) return;

        SrcIncludeProject();

        var plan = TargetFactory.FromFile(At("project/src/main.c"));

        Assert.True(plan.Ok, plan.Problem);
        Assert.StartsWith("Building it as its Makefile builds app, together with list.c, given -std=c11 -I include -lm, using ", plan.Explanation, StringComparison.Ordinal);
        Assert.EndsWith("then running the result from project, where its Makefile is.", plan.Explanation, StringComparison.Ordinal);
        Assert.Equal(At("project"), plan.Spec!.WorkingDirectory, ignoreCase: true);
    }

    /// <summary>A file shared by two programs is built on its own, and how it ran says why rather than leaving it to a link error.</summary>
    [Fact]
    public void HowItRanSaysWhyAFileWasBuiltAsIfThereWereNoBuildFile()
    {
        if (Toolchains.FindGnu(cpp: false) is null && Toolchains.FindMsvc() is null) return;

        Write("labs/ex1.c", "int main(void) { return 0; }");
        Write("labs/ex2.c", "int main(void) { return 0; }");
        Write("labs/helper.c", "int helper(void) { return 1; }");
        Write("labs/Makefile", "all: ex1 ex2", "ex1: ex1.c helper.c", "\tgcc -o ex1 ex1.c helper.c", "ex2: ex2.c helper.c", "\tgcc -o ex2 ex2.c helper.c");

        var plan = TargetFactory.FromFile(At("labs/helper.c"));

        Assert.Contains("Its Makefile builds helper.c into more than one program - ex1 and ex2 - so it was built as if there were no Makefile.", plan.Explanation, StringComparison.Ordinal);
    }

    /// <summary>Of two programs a file is part of, the one plain make builds - its first target - is the one meant.</summary>
    [Fact]
    public void OfTwoProgramsTheOneMakeBuildsByDefaultIsTheOneMeant()
    {
        Write("labs/ex1.c", "int main(void) { return 0; }");
        Write("labs/ex2.c", "int main(void) { return 0; }");
        Write("labs/helper.c", "int helper(void) { return 1; }");
        Write("labs/Makefile", "ex1: ex1.c helper.c", "\tgcc -o ex1 ex1.c helper.c", "ex2: ex2.c helper.c", "\tgcc -o ex2 ex2.c helper.c");

        Assert.Equal("ex1", NativeBuild.For(At("labs/helper.c")).Build?.Program);
    }

    /// <summary>
    /// A change to a header in include is checked by compiling the program whose sources in src include it - with the
    /// changed header, so a mistake in it fails the check and a correct header passes it.
    /// </summary>
    [Fact]
    public async Task AChangeToAHeaderIsCheckedWithTheProgramThatIncludesIt()
    {
        if (Toolchains.FindGnu(cpp: false) is null && Toolchains.FindMsvc() is null) return;

        SrcIncludeProject();
        var header = Core.LocalFixes.SourceFile.Read(At("project/include/list.h"))!;

        var unchanged = await Core.LocalFixes.CompileCheck.RunAsync(header, header.Lines, null, CancellationToken.None);
        var broken = await Core.LocalFixes.CompileCheck.RunAsync(header, ["int sum(const int *values, int count"], null, CancellationToken.None);

        Assert.True(unchanged.Ran, "the header was checked with the program that includes it");
        Assert.True(unchanged.Clean, string.Join(" | ", unchanged.Lines.Select(line => line.Text)));
        Assert.True(broken.Ran);
        Assert.False(broken.Clean, "a header missing its closing bracket does not compile");
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
