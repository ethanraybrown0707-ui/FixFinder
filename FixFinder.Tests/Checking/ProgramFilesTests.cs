using System.Diagnostics;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Covers which of a program's files its code is read from for logic mistakes: the file chosen and the files its code uses
/// first, as many as are read - and, for a program with more, how many were left out, said in the report.
/// </summary>
public class ProgramFilesTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string relative, string content)
    {
        var path = Path.GetFullPath(Path.Combine(_temp.Path, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        return path;
    }

    [Fact]
    public void TheFilesAJavaProgramsCodeUsesAreReadBeforeTheRest()
    {
        // More files than are read, with the one the program uses last of all by name.
        for (var number = 0; number < 250; number++)
            Write($@"shop\src\Filler{number:000}.java", $"class Filler{number:000} {{\n}}\n");

        var main = Write(@"shop\src\Main.java", "public class Main {\n    public static void main(String[] args) {\n        Zebra.feed();\n    }\n}\n");
        var zebra = Write(@"shop\src\Zebra.java", "class Zebra {\n    static void feed() {\n    }\n}\n");

        var found = ProgramFiles.Read(main);

        Assert.Equal(ProgramFiles.MostFiles, found.Files.Count);
        Assert.Equal([main, zebra], found.Files.Take(2).ToArray(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(252 - ProgramFiles.MostFiles, found.LeftOut);
        Assert.False(found.LeftOutAtLeast);
    }

    [Fact]
    public void TheFilesACSharpProgramsCodeUsesAreReadBeforeTheRest()
    {
        Write(@"shop\Shop.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n  </PropertyGroup>\n</Project>\n");
        for (var number = 0; number < 250; number++)
            Write($@"shop\Filler{number:000}.cs", $"class Filler{number:000}\n{{\n}}\n");

        var main = Write(@"shop\Program.cs", "Zebra.Feed();\n");
        var zebra = Write(@"shop\Zebra.cs", "static class Zebra\n{\n    public static void Feed()\n    {\n    }\n}\n");

        var found = ProgramFiles.Read(main);

        Assert.Equal([main, zebra], found.Files.Take(2).ToArray(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(252 - ProgramFiles.MostFiles, found.LeftOut);
    }

    [Fact]
    public void APythonProgramsImportsAreCountedThoughNotAllAreRead()
    {
        for (var number = 0; number < 230; number++) Write($@"tool\part{number:000}.py", $"VALUE = {number}\n");
        var main = Write(@"tool\main.py", string.Concat(Enumerable.Range(0, 230).Select(number => $"import part{number:000}\n")));

        var found = ProgramFiles.Read(main);

        Assert.Equal(ProgramFiles.MostFiles, found.Files.Count);
        Assert.Equal(main, found.Files[0], StringComparer.OrdinalIgnoreCase);
        Assert.Equal(231 - ProgramFiles.MostFiles, found.LeftOut);
        Assert.False(found.LeftOutAtLeast);
    }

    [Fact]
    public void AProgramOfMoreFilesThanAreLookedThroughIsSaidToHaveAtLeastSoMany()
    {
        // More files than FixFinder follows imports through: those it found are counted, and what they import is not looked
        // for, so the files left out are said to be at least so many.
        for (var number = 0; number < 2050; number++) Write($@"huge\part{number:0000}.py", "");
        var main = Write(@"huge\main.py", string.Concat(Enumerable.Range(0, 2050).Select(number => $"import part{number:0000}\n")));

        var found = ProgramFiles.Read(main);

        Assert.True(found.LeftOutAtLeast);
        Assert.Equal(2051 - ProgramFiles.MostFiles, found.LeftOut);
    }

    /// <summary>A folder whose name is long enough that 190 paths into it make a line longer than Windows lets a program be started with.</summary>
    private static string LongFolderName => "a-folder-with-a-name-as-long-as-a-course-and-module-and-week-together-sometimes-make-one-" + new string('x', 50);

    [Fact]
    public async Task ACheckOfAProgramWithMoreFilesThanAreReadSaysHowManyWereLeftOut()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        for (var number = 0; number < 230; number++) Write($@"{LongFolderName}\part{number:000}.py", $"def value():\n    return {number}\n");
        var main = Write($@"{LongFolderName}\main.py", string.Concat(Enumerable.Range(0, 230).Select(number => $"import part{number:000}\n")) + "print(part000.value())\n");

        var launch = TargetFactory.FromFile(main);
        using var http = new FixFinderHttpClient();
        var timer = Stopwatch.StartNew();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Python }.CheckAsync(launch);
        output.WriteLine($"checked in {timer.Elapsed.TotalSeconds:0.0} s; {report.SyntaxSummary} | {report.LogicSummary}");
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");

        Assert.Contains(report.Notes, note => note ==
            $"The program has 231 source files, more than the {ProgramFiles.MostFiles} FixFinder reads its code from for logic mistakes: it read main.py " +
            "and the files its code uses first, and left out the other 31, so a mistake only in those was not looked for.");

        // The values were followed, and the code compiled, through every file read: neither failed to start for want of room
        // on a command line, as both once did for a program of many files in a folder of a long name.
        Assert.DoesNotContain(report.Notes, note => note.StartsWith("FixFinder could not follow the values", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Notes, note => note.StartsWith("Could not start", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheValuesAreFollowedThroughAJavaProgramOfManyFiles()
    {
        if (Toolchains.FindJavac() is null) return;

        // All of them read: 190 parts, Broken and Main.
        var calls = new System.Text.StringBuilder();
        for (var number = 0; number < 190; number++)
        {
            Write($@"{LongFolderName}\src\shop\Part{number:000}.java", $"package shop;\n\npublic class Part{number:000} {{\n    public int value() {{\n        return {number};\n    }}\n}}\n");
            calls.Append($"        System.out.println(new Part{number:000}().value());\n");
        }

        // The last of them divides by zero, which only following the values through it finds. Main reaches it, so it is part
        // of the program, but never calls it when run: a crash there would be the same mistake, and be reported as one with it.
        Write($@"{LongFolderName}\src\shop\Broken.java", "package shop;\n\npublic class Broken {\n    public int share() {\n        int count = 0;\n        return 10 / count;\n    }\n}\n");
        var main = Write($@"{LongFolderName}\src\shop\Main.java",
            $"package shop;\n\npublic class Main {{\n    public static void main(String[] args) {{\n{calls}        if (args.length > 99) System.out.println(new Broken().share());\n    }}\n}}\n");

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Java }.CheckAsync(TargetFactory.FromFile(main));
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");

        Assert.DoesNotContain(report.Notes, note => note.StartsWith("FixFinder could not follow the values", StringComparison.Ordinal));
        Assert.Contains(report.Findings, finding => finding.RuleId == "analysis-division-by-zero" && finding.Location.Contains("Broken.java", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CodeWhoseReaderCouldNotRunIsSaidNotToHaveBeenFollowed()
    {
        var main = Write(@"tool\main.py", "def average(values):\n    return sum(values) / len(values)\n\nprint(average([1, 2]))\n");

        // The program's own Python reads its code; one that is not there cannot, and finding nothing then is no clean bill.
        var missingPython = Path.Combine(_temp.Path, "no-python", "python.exe");
        var launch = new LaunchPlan(new TargetSpec { ExecutablePath = missingPython, Arguments = $"\"{main}\"", WorkingDirectory = Path.GetDirectoryName(main)! }, null, "Running it.")
        {
            ChosenFile = main,
        };

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Python }.CheckAsync(launch);
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");

        Assert.Contains(report.Notes, note => note.StartsWith(
            "FixFinder could not follow the values through the code, so it was checked against the logic patterns alone: Could not start", StringComparison.Ordinal));
    }
}
