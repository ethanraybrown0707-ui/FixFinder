using FixFinder.Core.Execution.BuildFiles;

namespace FixFinder.Tests;

/// <summary>
/// The programs a Makefile builds, read the way GNU make reads it: variables, functions, conditionals, pattern rules and
/// make's own rules - and what only running something could say left unknown rather than guessed.
/// </summary>
public class MakefileTests : IDisposable
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

    private string Source(string relative) => Write(relative, "int placeholder(void) { return 0; }");

    private IReadOnlyList<BuiltProgram> ProgramsOf(params string[] makefile) => Makefile.Read(Write("Makefile", makefile)).Programs();

    private string At(string relative) => Path.GetFullPath(Path.Combine(_temp.Path, relative));

    /// <summary>main.o has no rule of the Makefile's own: make's built-in one compiles it from main.c with CFLAGS.</summary>
    [Fact]
    public void AnObjectFileIsFollowedToTheSourceMakesOwnRuleCompiles()
    {
        Source("main.c");
        Source("list.c");
        Directory.CreateDirectory(At("include"));

        var program = Assert.Single(ProgramsOf(
            "CC = gcc",
            "CFLAGS = -Wall -std=c99 -Iinclude",
            "prog: main.o list.o",
            "\t$(CC) $(CFLAGS) -o prog main.o list.o"));

        Assert.Equal("prog", program.Name);
        Assert.Equal([At("main.c"), At("list.c")], program.Sources);
        Assert.Equal("c99", program.Standards[At("main.c")]);
        Assert.Equal([At("include")], program.IncludeFolders);
        Assert.Null(program.CannotBeBuilt);
    }

    [Fact]
    public void ASrcIncludeLayoutIsBuiltFromItsWildcardAndPatternRule()
    {
        Source("src/main.c");
        Source("src/list.c");
        Directory.CreateDirectory(At("include"));

        var program = Assert.Single(ProgramsOf(
            "CC = gcc",
            "CFLAGS = -Wall -Wextra -std=c11 -Iinclude",
            "SRC = $(wildcard src/*.c)",
            "OBJ = $(patsubst src/%.c,obj/%.o,$(SRC))",
            "TARGET = bin/app",
            "",
            "all: $(TARGET)",
            "",
            "$(TARGET): $(OBJ)",
            "\t$(CC) $(OBJ) -o $@ -lm",
            "",
            "obj/%.o: src/%.c",
            "\t$(CC) $(CFLAGS) -c $< -o $@",
            "",
            "clean:",
            "\trm -rf obj bin"));

        Assert.Equal("app", program.Name);
        Assert.Equal([At("src/list.c"), At("src/main.c")], program.Sources);
        Assert.Equal([At("include")], program.IncludeFolders);
        Assert.Equal(["m"], program.Libraries);
        Assert.Equal("c11", program.Standards[At("src/main.c")]);
        Assert.True(program.MadeByDefault);
    }

    /// <summary>A folder of exercises, each its own program: each is built from its own files, not from every file there.</summary>
    [Fact]
    public void EachProgramOfAFolderOfExercisesIsBuiltFromItsOwnFiles()
    {
        Source("ex1.c");
        Source("ex2.c");
        Source("helper.c");

        var programs = ProgramsOf(
            "all: ex1 ex2",
            "ex1: ex1.c",
            "\tgcc -Wall -o ex1 ex1.c",
            "ex2: ex2.c helper.c",
            "\tgcc -Wall -std=c99 -o ex2 ex2.c helper.c -lm");

        Assert.Equal(["ex1", "ex2"], programs.Select(program => program.Name));
        Assert.Equal([At("ex1.c")], programs[0].Sources);
        Assert.Equal([At("ex2.c"), At("helper.c")], programs[1].Sources);
        Assert.Equal(["m"], programs[1].Libraries);
    }

    /// <summary>prog: prog.o util.o with no recipe is linked by make's own rule, which names the program after its first object.</summary>
    [Fact]
    public void MakesOwnRuleLinksAProgramNamedLikeItsObject()
    {
        Source("prog.c");
        Source("util.c");

        var program = Assert.Single(ProgramsOf(
            "CFLAGS = -std=c99",
            "prog: prog.o util.o"));

        Assert.Equal([At("prog.c"), At("util.c")], program.Sources);
        Assert.Equal("c99", program.Standards[At("util.c")]);
    }

    [Fact]
    public void AConditionChoosesWhatTheMakefileGives()
    {
        Source("app.c");

        var program = Assert.Single(ProgramsOf(
            "PLATFORM = windows",
            "ifeq ($(PLATFORM),windows)",
            "  CFLAGS += -DON_WINDOWS",
            "else",
            "  CFLAGS += -DELSEWHERE",
            "endif",
            "ifdef NOT_SET_ANYWHERE",
            "  CFLAGS += -DNEVER",
            "endif",
            "app: app.c",
            "\t$(CC) $(CFLAGS) -o app app.c"));

        Assert.Equal(["ON_WINDOWS"], program.Definitions);
    }

    /// <summary>The files a $(shell find ...) would list are not known without running it, so the program is not built from a guess.</summary>
    [Fact]
    public void WhatOnlyAShellCouldSayIsNotGuessed()
    {
        Source("src/main.c");

        var program = Assert.Single(ProgramsOf(
            "SRC = $(shell find src -name '*.c')",
            "OBJ = $(SRC:.c=.o)",
            "app: $(OBJ)",
            "\t$(CC) -o $@ $^"));

        Assert.Empty(program.Sources);
        Assert.Contains("$(shell find src -name '*.c')", program.CannotBeBuilt, StringComparison.Ordinal);
    }

    [Fact]
    public void AStaticPatternRuleCompilesEachObjectFromItsSource()
    {
        Source("src/a.c");
        Source("src/b.c");

        var program = Assert.Single(ProgramsOf(
            "OBJECTS = obj/a.o obj/b.o",
            "app: $(OBJECTS)",
            "\tgcc -o app $(OBJECTS)",
            "$(OBJECTS): obj/%.o: src/%.c",
            "\tgcc -DSTATIC_PATTERN -c $< -o $@"));

        Assert.Equal([At("src/a.c"), At("src/b.c")], program.Sources);
        Assert.Equal(["STATIC_PATTERN"], program.Definitions);
    }

    /// <summary>.c.o: is how Makefiles written before pattern rules say %.o: %.c.</summary>
    [Fact]
    public void AnOldSuffixRuleIsReadAsThePatternRuleItIs()
    {
        Source("main.c");

        var program = Assert.Single(ProgramsOf(
            "CFLAGS = -DFROM_SUFFIX_RULE",
            "app: main.o",
            "\tcc -o app main.o",
            ".c.o:",
            "\tcc $(CFLAGS) -c $<"));

        Assert.Equal([At("main.c")], program.Sources);
        Assert.Equal(["FROM_SUFFIX_RULE"], program.Definitions);
    }

    [Fact]
    public void VpathFindsSourcesInTheFolderItNames()
    {
        Source("src/main.c");
        Source("src/list.c");

        var program = Assert.Single(ProgramsOf(
            "vpath %.c src",
            "app: main.o list.o",
            "\t$(CC) -o $@ $^"));

        Assert.Equal([At("src/main.c"), At("src/list.c")], program.Sources);
    }

    [Fact]
    public void AValueGivenToOneTargetReachesItsCommands()
    {
        Source("app.c");

        var program = Assert.Single(ProgramsOf(
            "CFLAGS = -Wall",
            "app: CFLAGS += -DFOR_APP",
            "app: app.c",
            "\t$(CC) $(CFLAGS) -o $@ $<"));

        Assert.Equal(["FOR_APP"], program.Definitions);
    }

    /// <summary>A template written with define and made into a rule for each program with $(eval $(call ...)).</summary>
    [Fact]
    public void ATemplateEvaluatedForEachProgramMakesEachOne()
    {
        Source("one.c");
        Source("two.c");

        var programs = ProgramsOf(
            "PROGRAMS = one two",
            "define PROGRAM_template",
            "$(1): $(1).c",
            "\t$$(CC) -o $$@ $$^",
            "endef",
            "$(foreach program,$(PROGRAMS),$(eval $(call PROGRAM_template,$(program))))");

        Assert.Equal(["one", "two"], programs.Select(program => program.Name));
        Assert.Equal([At("two.c")], programs[1].Sources);
    }

    /// <summary>Each recipe line runs in a shell of its own, so a cd counts for the rest of its line only.</summary>
    [Fact]
    public void ACommandRunAfterCdTakesItsFilesFromThere()
    {
        Source("src/main.c");
        Source("src/list.c");

        var program = Assert.Single(ProgramsOf(
            "app:",
            "\tcd src && gcc -o ../app main.c list.c"));

        Assert.Equal([At("src/main.c"), At("src/list.c")], program.Sources);
    }

    /// <summary>An archive the Makefile makes from object files is built from their sources.</summary>
    [Fact]
    public void AnArchiveTheMakefileMakesIsFollowedToItsSources()
    {
        Source("main.c");
        Source("circle.c");
        Source("square.c");

        var program = Assert.Single(ProgramsOf(
            "app: main.o libshapes.a",
            "\t$(CC) -o $@ main.o libshapes.a",
            "libshapes.a: circle.o square.o",
            "\tar rcs $@ $^"));

        Assert.Equal([At("main.c"), At("circle.c"), At("square.c")], program.Sources);
    }

    [Fact]
    public void WhatPlainMakeBuildsIsMadeByDefaultAndTheRestIsNot()
    {
        Source("main.c");
        Source("test_main.c");

        var programs = ProgramsOf(
            "all: app",
            "app: main.c",
            "\tgcc -o app main.c",
            "test: test_main.c",
            "\tgcc -o test_runner test_main.c");

        Assert.True(programs.Single(program => program.Name == "app").MadeByDefault);
        Assert.False(programs.Single(program => program.Name == "test_runner").MadeByDefault);
    }

    [Fact]
    public void AProgramOfCAndCppTogetherIsNotBuiltAsOne()
    {
        Source("main.cpp");
        Source("legacy.c");

        var program = Assert.Single(ProgramsOf(
            "app: main.cpp legacy.c",
            "\tg++ -o app main.cpp legacy.c"));

        Assert.Contains("C and C++", program.CannotBeBuilt, StringComparison.Ordinal);
    }

    /// <summary>A Makefile CMake wrote in a build folder says how CMake builds, not how a person wrote it to: it is not read as theirs.</summary>
    [Fact]
    public void AMakefileCMakeWroteIsNotTakenForOneAPersonWrote()
    {
        Write("Makefile", "# CMAKE generated file: DO NOT EDIT!", "all:", "\t$(MAKE) -f CMakeFiles/Makefile2 all");

        Assert.Null(Makefile.In(_temp.Path));
    }

    [Fact]
    public void AHandWrittenMakefileIsFound()
    {
        var path = Write("Makefile", "app: app.c", "\tgcc -o app app.c");

        // Windows finds Makefile when asked for makefile, which make looks for first; the name is said as it is on disk.
        Assert.Equal(path, Makefile.In(_temp.Path));
    }

    [Fact]
    public void AMakefileWrittenInLowerCaseIsFoundUnderItsOwnName()
    {
        var path = Write("makefile", "app: app.c", "\tgcc -o app app.c");

        Assert.Equal(path, Makefile.In(_temp.Path));
    }
}
