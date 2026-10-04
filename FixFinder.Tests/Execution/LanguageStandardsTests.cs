using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Tests;

/// <summary>
/// Which version of C, C++ and Java a program is held to. The flags are checked here without touching the setting the
/// whole application shares, so these run alongside everything else.
/// </summary>
public class LanguageStandardsTests
{
    /// <summary>Choosing nothing must change nothing: these are exactly the flags every build was given before.</summary>
    [Fact]
    public void TheDefaultsAreWhatTheCompilersWereAlwaysGiven()
    {
        var standards = LanguageStandards.Default;

        Assert.Equal("", standards.Gnu(cpp: false));
        Assert.Equal("-std=c++17 ", standards.Gnu(cpp: true));
        Assert.Equal("", standards.Msvc(cpp: false));
        Assert.Equal(" /std:c++17", standards.Msvc(cpp: true));
        Assert.Equal("", standards.JavaRelease);
    }

    /// <summary>C23 goes to gcc as c2x: gcc 9 to 13 know it by no other name - -std=c23 is refused by gcc 13.2 - and gcc 14 and 15 still take it.</summary>
    [Theory]
    [InlineData("c89", "-std=c89 ")]
    [InlineData("c99", "-std=c99 ")]
    [InlineData("c11", "-std=c11 ")]
    [InlineData("c23", "-std=c2x ")]
    public void ACStandardReachesGccAsItsFlag(string chosen, string expected)
    {
        Assert.Equal(expected, new LanguageStandards { C = chosen }.Gnu(cpp: false));
    }

    /// <summary>With nothing chosen, a program is built with the standard its Makefile or CMakeLists.txt gives it.</summary>
    [Theory]
    [InlineData(false, "gnu11", "-std=gnu11 ", " /std:c11")]
    [InlineData(false, "c99", "-std=c99 ", "")]
    [InlineData(true, "gnu++20", "-std=gnu++20 ", " /std:c++20")]
    [InlineData(true, "c++2b", "-std=c++2b ", " /std:c++latest")]
    public void WithNoneChosenTheProjectsOwnStandardIsUsed(bool cpp, string projects, string gnu, string msvc)
    {
        var standards = LanguageStandards.Default;

        Assert.False(standards.Chooses(cpp));
        Assert.Equal(gnu, standards.Gnu(cpp, projects));
        Assert.Equal(msvc, standards.Msvc(cpp, projects));
    }

    /// <summary>A standard chosen in Settings is the course's, and holds over the one a program's build file gives.</summary>
    [Fact]
    public void AStandardChosenInSettingsHoldsOverTheProjects()
    {
        var standards = new LanguageStandards { C = "c99", Cpp = "c++14" };

        Assert.True(standards.Chooses(cpp: false));
        Assert.Equal("-std=c99 ", standards.Gnu(cpp: false, "gnu11"));
        Assert.Equal("-std=c++14 ", standards.Gnu(cpp: true, "gnu++20"));
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
        Assert.Equal(cpp ? "-std=c++17 " : "", LanguageStandards.Default.Gnu(cpp, projects));
    }

    [Theory]
    [InlineData("8", "--release 8 ")]
    [InlineData("11", "--release 11 ")]
    [InlineData("21", "--release 21 ")]
    public void AJavaReleaseReachesJavacAsItsFlag(string chosen, string expected)
    {
        Assert.Equal(expected, new LanguageStandards { Java = chosen }.JavaRelease);
    }

    /// <summary>MSVC has no flag for some standards, and in that case it is left on its own rather than given a wrong one.</summary>
    [Theory]
    [InlineData("c99", false, "")]
    [InlineData("c11", false, " /std:c11")]
    [InlineData("c++11", true, "")]
    [InlineData("c++20", true, " /std:c++20")]
    [InlineData("c++23", true, " /std:c++latest")]
    public void MsvcIsGivenAFlagOnlyForStandardsItHasOneFor(string chosen, bool cpp, string expected)
    {
        var standards = cpp ? new LanguageStandards { Cpp = chosen } : new LanguageStandards { C = chosen };

        Assert.Equal(expected, standards.Msvc(cpp));
    }

    /// <summary>
    /// The choice comes from a preferences file anybody can edit and is put on a compiler's command line, so anything
    /// that is not one of the listed versions is treated as the default rather than passed along.
    /// </summary>
    [Theory]
    [InlineData("8 && calc")]
    [InlineData("c99; del *")]
    [InlineData("--output=elsewhere")]
    [InlineData("C99")]
    [InlineData("7")]
    public void AnythingNotListedNeverReachesACompiler(string edited)
    {
        var standards = new LanguageStandards { C = edited, Cpp = edited, Java = edited };

        // Exactly the defaults, which says more than "does not contain what was typed": a short value such as 7 turns up
        // inside c++17 by coincidence, while equality leaves no room for anything else to have got through.
        Assert.Equal("", standards.Gnu(cpp: false));
        Assert.Equal("-std=c++17 ", standards.Gnu(cpp: true));
        Assert.Equal("", standards.JavaRelease);
    }

    [Fact]
    public void TheChosenVersionsAreKeptBetweenSessions()
    {
        using var temp = new TempFolder();
        var file = Path.Combine(temp.Path, "preferences.json");

        new Preferences { CStandard = "c99", CppStandard = "c++20", JavaRelease = "8" }.Save(file);
        var read = Preferences.Load(file);

        Assert.Equal(new LanguageStandards { C = "c99", Cpp = "c++20", Java = "8" }, read.Standards);
    }

    /// <summary>
    /// A preferences file from before "the project's own" was a C++ choice holds C++17 only because nobody chose: it is
    /// read as nobody choosing, which still gives C++17 to a program whose build file names no standard. A file written
    /// since keeps C++17 as the choice it then is.
    /// </summary>
    [Fact]
    public void AnOlderFilesCpp17IsReadAsNoChoiceAndANewerOnesAsAChoice()
    {
        using var temp = new TempFolder();
        var older = Path.Combine(temp.Path, "older.json");
        var newer = Path.Combine(temp.Path, "newer.json");

        File.WriteAllText(older, "{ \"Explanations\": 1, \"CStandard\": \"\", \"CppStandard\": \"c\\u002B\\u002B17\", \"JavaRelease\": \"\" }");
        new Preferences { CppStandard = "c++17" }.Save(newer);

        Assert.Equal("", Preferences.Load(older).CppStandard);
        Assert.Equal("c++17", Preferences.Load(newer).CppStandard);
    }

    /// <summary>An older file's C++ choice other than C++17 was somebody choosing, and stays.</summary>
    [Fact]
    public void AnOlderFilesOtherCppChoiceStays()
    {
        using var temp = new TempFolder();
        var older = Path.Combine(temp.Path, "older.json");

        File.WriteAllText(older, "{ \"CppStandard\": \"c++20\" }");

        Assert.Equal("c++20", Preferences.Load(older).CppStandard);
    }

    /// <summary>The worked-out value is not written into the file as well, where it could disagree with the three it comes from.</summary>
    [Fact]
    public void TheCombinedValueIsNotWrittenDownTwice()
    {
        using var temp = new TempFolder();
        var file = Path.Combine(temp.Path, "preferences.json");

        new Preferences { JavaRelease = "8" }.Save(file);

        Assert.DoesNotContain("\"Standards\"", File.ReadAllText(file), StringComparison.Ordinal);
    }
}

/// <summary>
/// Tests that change the language versions the whole application shares. xUnit runs this collection on its own, after
/// everything else, so no other test ever compiles under a version it did not ask for.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedLanguageStandards
{
    public const string Name = "the language versions everything shares";
}

[Collection(SharedLanguageStandards.Name)]
public class LanguageStandardsLiveTests
{
    /// <summary>
    /// The point of the whole setting. A fix is only offered once a copy with it compiles, and under Java 8 a fix written
    /// with var does not - so it is never offered to somebody whose course is Java 8.
    /// <para>
    /// It is compiled under the installed JDK first and then under Java 8, in that order, so that a remembered result
    /// answering for the wrong version would show here as var compiling under Java 8.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AFixUsingALaterJavaIsRejectedWhenTheCourseUsesJava8()
    {
        if (Toolchains.FindJavac() is null) return;

        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "Marks.java");

        string[] lines =
        [
            "public class Marks {",
            "    public static void main(String[] args) {",
            "        var total = 70 + 45 + 90;",
            "        System.out.println(total);",
            "    }",
            "}",
        ];

        await File.WriteAllLinesAsync(path, lines);
        var source = SourceFile.Read(path)!;

        var before = LanguageStandards.Current;

        try
        {
            LanguageStandards.Current = LanguageStandards.Default;
            var installed = await CompileCheck.RunAsync(source, source.Lines, null, CancellationToken.None);

            LanguageStandards.Current = new LanguageStandards { Java = "8" };
            var java8 = await CompileCheck.RunAsync(source, source.Lines, null, CancellationToken.None);

            // javac was found, so the check has to have actually run - a test that quietly skips when it did not would
            // pass without proving anything.
            Assert.True(installed.Ran, "the check ran under the installed JDK");
            Assert.True(java8.Ran, "the check ran under Java 8");

            Assert.True(installed.Clean, "var compiles under the installed JDK");
            Assert.False(java8.Clean, "var does not compile under Java 8, and a remembered result must not say it does");
        }
        finally
        {
            LanguageStandards.Current = before;
        }
    }
}
