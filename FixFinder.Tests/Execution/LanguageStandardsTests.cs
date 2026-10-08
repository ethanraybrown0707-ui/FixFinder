using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Tests;

/// <summary>
/// The C and C++ standard a program is held to: the one its build file gives it, else the compiler's own for C and C++17
/// for C++. Nothing is chosen in Settings - each program's version is worked out from its project and its code.
/// </summary>
public class LanguageStandardsTests
{
    /// <summary>A program whose build file names no standard is given exactly what every build was always given.</summary>
    [Fact]
    public void WithNoStandardOfItsOwnAProgramGetsTheUsualOne()
    {
        Assert.Equal("", LanguageStandards.Gnu(cpp: false));
        Assert.Equal("-std=c++17 ", LanguageStandards.Gnu(cpp: true));
        Assert.Equal("", LanguageStandards.Msvc(cpp: false));
        Assert.Equal(" /std:c++17", LanguageStandards.Msvc(cpp: true));
    }

    /// <summary>A program is built with the standard its Makefile or CMakeLists.txt gives it.</summary>
    [Theory]
    [InlineData(false, "gnu11", "-std=gnu11 ", " /std:c11")]
    [InlineData(false, "c99", "-std=c99 ", "")]
    [InlineData(true, "gnu++20", "-std=gnu++20 ", " /std:c++20")]
    [InlineData(true, "c++2b", "-std=c++2b ", " /std:c++latest")]
    public void TheProjectsOwnStandardIsUsed(bool cpp, string projects, string gnu, string msvc)
    {
        Assert.Equal(gnu, LanguageStandards.Gnu(cpp, projects));
        Assert.Equal(msvc, LanguageStandards.Msvc(cpp, projects));
    }

    /// <summary>C23 goes to gcc as c2x: gcc 9 to 13 know it by no other name - -std=c23 is refused by gcc 13.2 - and gcc 14 and 15 still take it.</summary>
    [Theory]
    [InlineData("c23", "-std=c2x ")]
    [InlineData("gnu23", "-std=gnu2x ")]
    [InlineData("c11", "-std=c11 ")]
    public void C23ReachesGccByTheNameEveryGccKnows(string projects, string expected)
    {
        Assert.Equal(expected, LanguageStandards.Gnu(cpp: false, projects));
    }

    /// <summary>
    /// A build file's -std= reaches a command line only when it is a standard of the program's own language - so nothing
    /// else written after -std= in a Makefile can get through, and a C standard is never given to a C++ program.
    /// </summary>
    [Theory]
    [InlineData(false, "c11; del *")]
    [InlineData(false, "gnu++17")]
    [InlineData(true, "c11")]
    [InlineData(true, "c++17 -o elsewhere")]
    public void AProjectStandardThatIsNotOneForTheLanguageIsNotGiven(bool cpp, string projects)
    {
        Assert.Equal(cpp ? "-std=c++17 " : "", LanguageStandards.Gnu(cpp, projects));
    }

    /// <summary>MSVC has no flag for some standards, and in that case it is left on its own rather than given a wrong one.</summary>
    [Theory]
    [InlineData("c99", false, "")]
    [InlineData("c11", false, " /std:c11")]
    [InlineData("c++11", true, "")]
    [InlineData("c++20", true, " /std:c++20")]
    [InlineData("c++23", true, " /std:c++latest")]
    public void MsvcIsGivenAFlagOnlyForStandardsItHasOneFor(string projects, bool cpp, string expected)
    {
        Assert.Equal(expected, LanguageStandards.Msvc(cpp, projects));
    }

    /// <summary>
    /// A preferences file written when Settings chose each language's version still opens, with its other choices kept: the
    /// versions it names are passed over, so a choice nobody can see any more never decides how a program is built.
    /// </summary>
    [Fact]
    public void APreferencesFileThatChoseVersionsStillOpensWithItsOtherChoices()
    {
        using var temp = new TempFolder();
        var file = Path.Combine(temp.Path, "preferences.json");
        File.WriteAllText(file,
            """{ "CStandard": "c99", "CppStandard": "c++20", "JavaRelease": "8", "PythonVersion": "3.9", "GoVersion": "1.22", "NodeVersion": "20", "Version": 1, "RunSeconds": 30, "WindowsRunUntilClosed": true }""");

        var read = Preferences.Load(file);

        Assert.Equal(TimeSpan.FromSeconds(30), read.RunTimeLimit);
        Assert.True(read.WindowsRunUntilClosed);

        read.Save(file);
        Assert.DoesNotContain("JavaRelease", File.ReadAllText(file), StringComparison.Ordinal);
    }
}

/// <summary>
/// Tests that change or depend on what the whole application shares while it builds programs - the standard each C or C++
/// file was found to be written to, and the time a run is given. xUnit runs this collection on its own, after everything
/// else, so no other test builds a program under what one of these set.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedLanguageStandards
{
    public const string Name = "the language versions everything shares";
}

public class LanguageStandardsLiveTests
{
    /// <summary>
    /// A fix is only offered once a copy with it compiles, for the Java the program is built for - so a fix written with
    /// var is never offered for a program whose pom.xml says it is written for Java 8.
    /// <para>
    /// The same file is compiled on its own first and then as part of a Java 8 project, in that order, so that a remembered
    /// result answering for the wrong version would show here as var compiling for Java 8.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AFixUsingALaterJavaIsRejectedForAProgramWrittenForJava8()
    {
        if (Toolchains.FindJavac() is null) return;

        using var temp = new TempFolder();

        string[] lines =
        [
            "public class Marks {",
            "    public static void main(String[] args) {",
            "        var total = 70 + 45 + 90;",
            "        System.out.println(total);",
            "    }",
            "}",
        ];

        var alone = Path.Combine(temp.Path, "alone", "Marks.java");
        Directory.CreateDirectory(Path.GetDirectoryName(alone)!);
        await File.WriteAllLinesAsync(alone, lines);

        var inProject = Path.Combine(temp.Path, "project", "src", "main", "java", "Marks.java");
        Directory.CreateDirectory(Path.GetDirectoryName(inProject)!);
        await File.WriteAllLinesAsync(inProject, lines);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "project", "pom.xml"), """
            <project xmlns="http://maven.apache.org/POM/4.0.0">
              <modelVersion>4.0.0</modelVersion>
              <groupId>uni</groupId>
              <artifactId>marks</artifactId>
              <version>1.0</version>
              <properties>
                <maven.compiler.release>8</maven.compiler.release>
              </properties>
            </project>
            """);

        var compiledAlone = await CompileCheck.RunAsync(SourceFile.Read(alone)!, SourceFile.Read(alone)!.Lines, null, CancellationToken.None);
        var compiledForJava8 = await CompileCheck.RunAsync(SourceFile.Read(inProject)!, SourceFile.Read(inProject)!.Lines, null, CancellationToken.None);

        // javac was found, so the check has to have actually run - a test that quietly skips when it did not would pass
        // without proving anything.
        Assert.True(compiledAlone.Ran, "the check ran for the file on its own");
        Assert.True(compiledForJava8.Ran, "the check ran for the Java 8 project");

        Assert.True(compiledAlone.Clean, "var compiles for the JDK's own Java");
        Assert.False(compiledForJava8.Clean, "var does not compile for Java 8, and a remembered result must not say it does");
    }
}
