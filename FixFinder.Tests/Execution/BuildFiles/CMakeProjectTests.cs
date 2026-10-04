using FixFinder.Core.Execution.BuildFiles;

namespace FixFinder.Tests;

/// <summary>
/// The programs a CMake project builds, found by running its CMakeLists.txt files as CMake runs them - and what only
/// running something could say, or a package CMake would look for, named rather than guessed.
/// </summary>
public class CMakeProjectTests : IDisposable
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

    private CMakeProject Read(NativeCompilerKind compiler, params string[] cmakeLists) => CMakeProject.Read(Write("CMakeLists.txt", cmakeLists), compiler);

    private IReadOnlyList<BuiltProgram> ProgramsOf(params string[] cmakeLists) => Read(NativeCompilerKind.Gnu, cmakeLists).Programs();

    private string At(string relative) => Path.GetFullPath(Path.Combine(_temp.Path, relative));

    /// <summary>The project CLion makes for a new C program. CMake gives gcc -std=gnu11 for C_STANDARD 11, as its extensions are on unless turned off.</summary>
    [Fact]
    public void TheProjectAnIdeMakesIsBuiltFromItsMainWithItsStandard()
    {
        Source("main.c");

        var program = Assert.Single(ProgramsOf(
            "cmake_minimum_required(VERSION 3.29)",
            "project(untitled C)",
            "",
            "set(CMAKE_C_STANDARD 11)",
            "",
            "add_executable(untitled main.c)"));

        Assert.Equal("untitled", program.Name);
        Assert.Equal([At("main.c")], program.Sources);
        Assert.Equal("gnu11", program.Standards[At("main.c")]);
        Assert.True(program.MadeByDefault);
        Assert.Null(program.CannotBeBuilt);
    }

    [Fact]
    public void WithExtensionsOffTheStandardIsTheIsoOne()
    {
        Source("main.cpp");

        var program = Assert.Single(ProgramsOf(
            "project(shapes CXX)",
            "set(CMAKE_CXX_STANDARD 20)",
            "set(CMAKE_CXX_EXTENSIONS OFF)",
            "add_executable(shapes main.cpp)"));

        Assert.Equal("c++20", program.Standards[At("main.cpp")]);
    }

    [Fact]
    public void ASrcIncludeLayoutIsBuiltFromItsGlobAndIncludeFolder()
    {
        Source("src/main.c");
        Source("src/list.c");
        Write("include/list.h", "int size(void);");

        var program = Assert.Single(ProgramsOf(
            "project(lists C)",
            "include_directories(include)",
            "file(GLOB SOURCES ${PROJECT_SOURCE_DIR}/src/*.c)",
            "add_executable(lists ${SOURCES})",
            "target_link_libraries(lists m)"));

        Assert.Equal([At("src/list.c"), At("src/main.c")], program.Sources);
        Assert.Equal([At("include")], program.IncludeFolders);
        Assert.Equal(["m"], program.Libraries);
    }

    /// <summary>A library's sources are built into each program that links it, and its PUBLIC include folders go with it.</summary>
    [Fact]
    public void ALibraryItLinksBringsItsSourcesAndItsPublicIncludeFolders()
    {
        Source("src/main.c");
        Source("src/list.c");
        Source("tests/test_list.c");
        Directory.CreateDirectory(At("include"));

        var programs = ProgramsOf(
            "project(lists C)",
            "add_library(core STATIC src/list.c)",
            "target_include_directories(core PUBLIC include)",
            "add_executable(app src/main.c)",
            "target_link_libraries(app PRIVATE core)",
            "enable_testing()",
            "add_executable(tests tests/test_list.c)",
            "target_link_libraries(tests PRIVATE core)",
            "add_test(NAME list COMMAND tests)");

        var app = programs.Single(program => program.Name == "app");
        Assert.Equal([At("src/main.c"), At("src/list.c")], app.Sources);
        Assert.Equal([At("include")], app.IncludeFolders);
        Assert.False(app.IsATest);

        Assert.True(programs.Single(program => program.Name == "tests").IsATest);
    }

    /// <summary>A PRIVATE include folder is the library's own: a program linking it does not get it.</summary>
    [Fact]
    public void APrivateIncludeFolderStaysWithItsLibrary()
    {
        Source("main.c");
        Source("lib/shapes.c");
        Directory.CreateDirectory(At("lib/internal"));

        var program = Assert.Single(ProgramsOf(
            "add_library(shapes lib/shapes.c)",
            "target_include_directories(shapes PRIVATE lib/internal)",
            "add_executable(app main.c)",
            "target_link_libraries(app shapes)"));

        Assert.Empty(program.IncludeFolders);
        Assert.Contains(At("lib/shapes.c"), program.Sources);
    }

    [Fact]
    public void ASubdirectoryAddsItsTargetsAndTheirBuildInterface()
    {
        Source("app/main.c");
        Source("lib/shapes.c");
        Directory.CreateDirectory(At("lib/include"));

        Write("lib/CMakeLists.txt",
            "add_library(shapes shapes.c)",
            "target_include_directories(shapes PUBLIC",
            "    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>",
            "    $<INSTALL_INTERFACE:include>)");

        Write("app/CMakeLists.txt",
            "add_executable(app main.c)",
            "target_link_libraries(app PRIVATE shapes)");

        var program = Assert.Single(ProgramsOf(
            "project(drawing C)",
            "add_subdirectory(lib)",
            "add_subdirectory(app)"));

        Assert.Equal([At("app/main.c"), At("lib/shapes.c")], program.Sources);
        Assert.Equal([At("lib/include")], program.IncludeFolders);
    }

    /// <summary>A loop that makes a program of each exercise in the folder, each from its own file.</summary>
    [Fact]
    public void ALoopMakesAProgramOfEachExercise()
    {
        Source("ex1.c");
        Source("ex2.c");

        var programs = ProgramsOf(
            "file(GLOB EXERCISES ${CMAKE_CURRENT_SOURCE_DIR}/*.c)",
            "foreach(exercise ${EXERCISES})",
            "    get_filename_component(name ${exercise} NAME_WE)",
            "    add_executable(${name} ${exercise})",
            "endforeach()");

        Assert.Equal(["ex1", "ex2"], programs.Select(program => program.Name));
        Assert.Equal([At("ex2.c")], programs[1].Sources);
    }

    [Theory]
    [InlineData(false, "NOT_MSVC")]
    [InlineData(true, "ON_MSVC")]
    public void AConditionOnTheCompilerTakesTheBranchForTheOneUsed(bool withMsvc, string defined)
    {
        Source("main.c");

        var program = Assert.Single(Read(withMsvc ? NativeCompilerKind.Msvc : NativeCompilerKind.Gnu,
            "if(MSVC)",
            "    add_compile_definitions(ON_MSVC)",
            "else()",
            "    add_compile_definitions(NOT_MSVC)",
            "endif()",
            "add_executable(app main.c)").Programs());

        Assert.Equal([defined], program.Definitions);
    }

    [Fact]
    public void AFunctionOfTheProjectsOwnIsRun()
    {
        Source("lab1.c");

        var program = Assert.Single(ProgramsOf(
            "function(add_lab name)",
            "    add_executable(${name} ${name}.c)",
            "    target_compile_definitions(${name} PRIVATE LAB_${name})",
            "endfunction()",
            "add_lab(lab1)"));

        Assert.Equal("lab1", program.Name);
        Assert.Equal(["LAB_lab1"], program.Definitions);
    }

    [Fact]
    public void ListsAndVariablesAreFollowed()
    {
        Source("main.c");
        Source("util.c");

        var program = Assert.Single(ProgramsOf(
            "set(SOURCES main.c)",
            "list(APPEND SOURCES util.c)",
            "option(FAST_MATHS \"Use the fast maths\" OFF)",
            "if(FAST_MATHS)",
            "    add_compile_definitions(FAST)",
            "endif()",
            "add_executable(app ${SOURCES})"));

        Assert.Equal([At("main.c"), At("util.c")], program.Sources);
        Assert.Empty(program.Definitions);
    }

    /// <summary>if("NAME") is the text NAME, while if(NAME) is the variable - as CMake reads them since policy CMP0054.</summary>
    [Fact]
    public void AQuotedNameInAConditionIsTextAndAnUnquotedOneIsAVariable()
    {
        Source("main.c");

        var program = Assert.Single(ProgramsOf(
            "set(KIND console)",
            "if(\"KIND\" STREQUAL \"console\")",
            "    add_compile_definitions(QUOTED_WAS_A_VARIABLE)",
            "endif()",
            "if(KIND STREQUAL \"console\")",
            "    add_compile_definitions(UNQUOTED_IS_A_VARIABLE)",
            "endif()",
            "add_executable(app main.c)"));

        Assert.Equal(["UNQUOTED_IS_A_VARIABLE"], program.Definitions);
    }

    [Fact]
    public void ThreadsAreTheCompilersOwn()
    {
        Source("main.c");

        var program = Assert.Single(ProgramsOf(
            "find_package(Threads REQUIRED)",
            "add_executable(app main.c)",
            "target_link_libraries(app PRIVATE Threads::Threads)"));

        Assert.True(program.Threads);
        Assert.Empty(program.NotFollowed);
    }

    [Fact]
    public void APackageFixFinderDoesNotLookForIsNamedAndNotGuessed()
    {
        Source("main.c");

        var program = Assert.Single(ProgramsOf(
            "find_package(SDL2 REQUIRED)",
            "add_executable(game main.c)",
            "target_link_libraries(game PRIVATE SDL2::SDL2)"));

        Assert.Contains(program.NotFollowed, note => note.Contains("SDL2::SDL2", StringComparison.Ordinal));
        Assert.Contains(program.NotFollowed, note => note.Contains("find_package(SDL2 REQUIRED)", StringComparison.Ordinal));
        Assert.Empty(program.Libraries);
    }

    /// <summary>configure_file writes a header into the folder CMake builds in, which FixFinder does not make - and says so.</summary>
    [Fact]
    public void AHeaderCMakeWouldWriteIsNamed()
    {
        Source("main.c");
        Write("config.h.in", "#define VERSION \"@PROJECT_VERSION@\"");

        var program = Assert.Single(ProgramsOf(
            "project(versioned VERSION 1.2 LANGUAGES C)",
            "configure_file(config.h.in config.h)",
            "include_directories(${CMAKE_CURRENT_BINARY_DIR})",
            "add_executable(versioned main.c)"));

        Assert.Contains(program.NotFollowed, note => note.Contains("config.h", StringComparison.Ordinal));
        Assert.Empty(program.IncludeFolders);
    }

    /// <summary>A compile feature asks for at least a standard: gcc from 11 is already at C++17, so CMake only adds a flag past that.</summary>
    [Theory]
    [InlineData("cxx_std_20", "gnu++20")]
    [InlineData("cxx_std_14", null)]
    public void ACompileFeatureRaisesTheStandardOnlyPastTheCompilersOwn(string feature, string? expected)
    {
        Source("main.cpp");

        var program = Assert.Single(ProgramsOf(
            "add_executable(app main.cpp)",
            $"target_compile_features(app PRIVATE {feature})"));

        Assert.Equal(expected, program.Standards.GetValueOrDefault(At("main.cpp")));
    }

    [Fact]
    public void AFatalErrorStopsTheReadingWhereCMakeWouldStop()
    {
        Source("main.c");

        var project = Read(NativeCompilerKind.Gnu,
            "if(WIN32)",
            "    message(FATAL_ERROR \"This lab builds on Linux only\")",
            "endif()",
            "add_executable(app main.c)");

        Assert.Empty(project.Programs());
        Assert.Contains("This lab builds on Linux only", project.StoppedBecause, StringComparison.Ordinal);
    }

    [Fact]
    public void ASourceACommandMakesWhileBuildingCannotBeBuiltFrom()
    {
        Source("main.c");

        var program = Assert.Single(ProgramsOf(
            "add_custom_command(OUTPUT generated.c COMMAND python make_table.py > generated.c)",
            "add_executable(app main.c ${CMAKE_CURRENT_SOURCE_DIR}/generated.c)"));

        Assert.Contains("generated.c", program.CannotBeBuilt, StringComparison.Ordinal);
    }

    [Fact]
    public void AnObjectLibrarysObjectsAreBuiltFromItsSources()
    {
        Source("main.c");
        Source("parts.c");

        var program = Assert.Single(ProgramsOf(
            "add_library(parts OBJECT parts.c)",
            "add_executable(app main.c $<TARGET_OBJECTS:parts>)"));

        Assert.Equal([At("main.c"), At("parts.c")], program.Sources);
    }

    /// <summary>The project is read as a debugging build, which is how FixFinder builds: with debugging information and no optimisation.</summary>
    [Fact]
    public void AGeneratorExpressionForTheDebugConfigurationHolds()
    {
        Source("main.c");

        var program = Assert.Single(ProgramsOf(
            "add_executable(app main.c)",
            "target_compile_definitions(app PRIVATE $<$<CONFIG:Debug>:DEBUG_BUILD> $<$<CONFIG:Release>:RELEASE_BUILD>)"));

        Assert.Equal(["DEBUG_BUILD"], program.Definitions);
    }

    /// <summary>
    /// A test program built with a test framework the project downloads: the download and the target it gives are named,
    /// and the program is still a test - the one checking a file of the project's own is the program, not this.
    /// </summary>
    [Fact]
    public void ATestFrameworkTheProjectDownloadsIsNamedNotGuessed()
    {
        Source("hello_test.cc");

        var program = Assert.Single(ProgramsOf(
            "cmake_minimum_required(VERSION 3.14)",
            "project(my_project)",
            "set(CMAKE_CXX_STANDARD 14)",
            "set(CMAKE_CXX_STANDARD_REQUIRED ON)",
            "include(FetchContent)",
            "FetchContent_Declare(",
            "  googletest",
            "  URL https://github.com/google/googletest/archive/refs/heads/main.zip",
            ")",
            "FetchContent_MakeAvailable(googletest)",
            "enable_testing()",
            "add_executable(hello_test hello_test.cc)",
            "target_link_libraries(hello_test GTest::gtest_main)",
            "include(GoogleTest)",
            "gtest_discover_tests(hello_test)"));

        Assert.True(program.IsATest);
        Assert.Equal("gnu++14", program.Standards[At("hello_test.cc")]);
        Assert.Contains("it downloads googletest with FetchContent_Declare, which FixFinder never does", program.NotFollowed);
        Assert.Contains(program.NotFollowed, note => note.Contains("GTest::gtest_main", StringComparison.Ordinal));
    }

    [Fact]
    public void WhatExecuteProcessWouldGetIsNotKnown()
    {
        Source("main.c");

        var program = Assert.Single(ProgramsOf(
            "execute_process(COMMAND git describe OUTPUT_VARIABLE GIT_VERSION)",
            "if(GIT_VERSION STREQUAL \"\")",
            "    add_compile_definitions(NO_VERSION)",
            "endif()",
            "add_executable(app main.c)"));

        Assert.Empty(program.Definitions);
        Assert.Contains(program.NotFollowed, note => note.Contains("execute_process", StringComparison.Ordinal));
    }
}
