using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// Covers the folder a compiled program starts from: where an IDE or a build tool would start it, unless a file the
/// program names is only in another folder it could have been started from.
/// </summary>
public class WorkingFolderTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string ReadsScores = """
        import java.io.File;
        import java.util.Scanner;

        public class Scores {
            public static void main(String[] args) throws Exception {
                try (Scanner reader = new Scanner(new File("scores.txt"))) {
                    System.out.println(reader.nextInt());
                }
            }
        }
        """;

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        return path;
    }

    private string Folder(string relative) => Path.GetFullPath(Path.Combine(_temp.Path, relative));

    [Fact]
    public void AJavaFileOnItsOwnStartsInItsOwnFolder()
    {
        var file = Write(@"week3\Scores.java", ReadsScores);

        Assert.Equal(Folder("week3"), WorkingFolder.For(file).Folder);
    }

    [Fact]
    public void AJavaFileInSrcStartsFromTheFolderHoldingSrcAsAnIdeRunsIt()
    {
        var file = Write(@"project\src\Scores.java", ReadsScores);

        var start = WorkingFolder.For(file);

        Assert.Equal(Folder("project"), start.Folder);
        Assert.True(start.IsProjectFolder);
    }

    [Fact]
    public void APackageInSrcStartsFromTheFolderHoldingSrc()
    {
        var file = Write(@"project\src\uni\app\Main.java", "package uni.app;\n\npublic class Main { public static void main(String[] args) { } }");

        Assert.Equal(Folder("project"), WorkingFolder.For(file).Folder);
    }

    [Fact]
    public void AMavenOrGradleLayoutStartsFromTheProject()
    {
        var file = Write(@"inventory\src\main\java\com\example\App.java", "package com.example;\n\npublic class App { public static void main(String[] args) { } }");

        Assert.Equal(Folder("inventory"), WorkingFolder.For(file).Folder);
    }

    [Fact]
    public void AFileTheProgramNamesDecidesWhenItIsOnlyBesideTheSource()
    {
        var file = Write(@"project\src\Scores.java", ReadsScores);
        Write(@"project\src\scores.txt", "70");

        var start = WorkingFolder.For(file);

        Assert.Equal(Folder(@"project\src"), start.Folder);
        Assert.Equal("scores.txt", start.FileFound);
    }

    [Fact]
    public void AFileTheProgramNamesInTheProjectFolderKeepsItThere()
    {
        var file = Write(@"project\src\Scores.java", ReadsScores);
        Write(@"project\scores.txt", "70");
        Write(@"project\src\scores.txt", "80");

        Assert.Equal(Folder("project"), WorkingFolder.For(file).Folder);
    }

    [Fact]
    public void ACProgramStartsBesideItsSourceUnlessItsFileIsInTheFolderHoldingSrc()
    {
        const string readsInput = "#include <stdio.h>\nint main(void) { FILE *in = fopen(\"input.txt\", \"r\"); return in == NULL; }\n";

        var beside = Write(@"lab\reader.c", readsInput);
        Assert.Equal(Folder("lab"), WorkingFolder.For(beside).Folder);

        var inSrc = Write(@"coursework\src\reader.c", readsInput);
        Write(@"coursework\input.txt", "1 2 3");
        Assert.Equal(Folder("coursework"), WorkingFolder.For(inSrc).Folder);
    }

    [Fact]
    public void TextThatIsNotARelativePathDecidesNothing()
    {
        var file = Write(@"project\src\Report.java", """
            public class Report {
                public static void main(String[] args) {
                    System.out.printf("%.2f scores.txt%n", 1.5);
                    System.out.println("C:\\data\\scores.txt");
                    System.out.println("https://example.org/scores.txt");
                }
            }
            """);
        Write(@"project\src\scores.txt", "70");

        Assert.Equal(Folder("project"), WorkingFolder.For(file).Folder);
    }

    [Fact]
    public void ACopyOfAProjectHoldsTheFolderItStartsFrom()
    {
        var file = Write(@"project\src\uni\app\Main.java", "package uni.app;\n\npublic class Main { public static void main(String[] args) { } }");

        Assert.Equal(Folder("project"), ProgramCopy.RootOf(file));
    }

    [Fact]
    public void TheRunSaysWhereItStartsWhenThatIsNotBesideTheFile()
    {
        if (Toolchains.FindJavac() is null) return;

        var file = Write(@"project\src\Scores.java", ReadsScores);
        Write(@"project\scores.txt", "70");

        var launch = TargetFactory.FromFile(file);

        Assert.True(launch.Ok, launch.Problem);
        Assert.Equal(Folder("project"), launch.Spec!.WorkingDirectory);
        Assert.Contains("from project, where scores.txt is", launch.Explanation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"week3\Scores.java", @"week3\scores.txt")]
    [InlineData(@"project\src\Scores.java", @"project\scores.txt")]
    public async Task AJavaProgramThatReadsItsOwnFileRunsCleanly(string program, string scores)
    {
        if (Toolchains.FindJavac() is null) return;

        var file = Write(program, ReadsScores);
        Write(scores, "72 85");

        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry())
        {
            Expected = ExpectedBehaviour.From([new ExpectedRun(null, "72")], ""),
        };
        var report = await checker.CheckAsync(launch);

        Assert.DoesNotContain(report.Findings, finding => finding.Severity == Severity.Error);
    }
}
