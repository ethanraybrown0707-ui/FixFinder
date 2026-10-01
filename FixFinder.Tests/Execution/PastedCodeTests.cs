using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Covers code pasted in rather than chosen as a file: saved exactly as pasted, as the file its language needs, and
/// checked as any program is - so the lines the report names are the pasted lines.
/// </summary>
public class PastedCodeTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Folder(string relative) => Path.GetFullPath(Path.Combine(_temp.Path, relative));

    [Theory]
    [InlineData("Python", "print('hi')\n", "pasted.py")]
    [InlineData("C", "int main(void) { return 0; }\n", "pasted.c")]
    [InlineData("C++", "int main() { return 0; }\n", "pasted.cpp")]
    [InlineData("C#", "System.Console.WriteLine(1);\n", "pasted.cs")]
    [InlineData("JavaScript", "console.log(1);\n", "pasted.js")]
    [InlineData("Go", "package main\n\nfunc main() {}\n", "pasted.go")]
    public void PastedCodeIsSavedUnderTheNameItsLanguageRunsBy(string languageName, string code, string expected) =>
        Assert.Equal(expected, PastedCode.RelativePathFor(code, CodeLanguage.All.Single(language => language.Name == languageName)));

    [Theory]
    // The public class has to be in a file of its name, under the folders of its package.
    [InlineData("package com.example.shop;\n\npublic class Till {\n}\n", @"src\com\example\shop\Till.java")]
    [InlineData("public final class Till {\n    public static void main(String[] args) {}\n}\n", "Till.java")]
    // With no public class, the one with main is the one java runs.
    [InlineData("class Item {\n}\n\nclass Shop {\n    static void main(String[] args) {}\n}\n", "Shop.java")]
    [InlineData("record Point(int x, int y) {\n}\n", "Point.java")]
    [InlineData("void main() {\n    System.out.println(1);\n}\n", "Main.java")]
    public void JavaIsSavedAsItsClassAndUnderItsPackage(string code, string expected) =>
        Assert.Equal(expected, PastedCode.RelativePathFor(code, CodeLanguage.Java));

    [Fact]
    public void PastedCodeIsSavedAsPastedAndNothingPastedBeforeIsLeftBesideIt()
    {
        var folder = Folder("pasted");
        var first = PastedCode.Save("class Old {\n}\n", CodeLanguage.Java, folder);

        // Line endings and all, so a line the report names is the pasted line.
        const string pasted = "public class Till {\r\n    public static void main(String[] args) {\r\n    }\r\n}\r\n";
        var second = PastedCode.Save(pasted, CodeLanguage.Java, folder);

        Assert.Equal(Path.Combine(folder, "Till.java"), second);
        Assert.Equal(pasted, File.ReadAllText(second));
        Assert.False(File.Exists(first));
    }

    [Theory]
    [InlineData("import java.util.Scanner;\n\npublic class A {\n    public static void main(String[] args) {\n        System.out.printf(\"%d\", 1);\n    }\n}\n", "Java")]
    [InlineData("using System;\n\nclass A\n{\n    static void Main()\n    {\n        Console.WriteLine(1);\n    }\n}\n", "C#")]
    [InlineData("def average(values):\n    return sum(values) / len(values)\n\nprint(average([1, 2]))\n", "Python")]
    [InlineData("#include <stdio.h>\n\nint main(void) {\n    printf(\"%d\\n\", 1);\n    return 0;\n}\n", "C")]
    [InlineData("#include <iostream>\n#include <stdio.h>\n\nint main() {\n    std::cout << 1;\n}\n", "C++")]
    [InlineData("const total = [1, 2].reduce((a, b) => a + b);\nconsole.log(total);\n", "JavaScript")]
    [InlineData("package main\n\nimport \"fmt\"\n\nfunc main() {\n    fmt.Println(1)\n}\n", "Go")]
    public void TheLanguageIsWorkedOutFromWhatOnlyItWrites(string code, string expected) =>
        Assert.Equal(expected, PastedCode.LanguageOf(code)?.Name);

    [Theory]
    [InlineData("hello there")]
    [InlineData("x = 1\ny = 2\n")]
    // As many marks of one language as of another: which it is is the person's to say.
    [InlineData("console.log(1);\nSystem.out.println(1);\n")]
    public void CodeThatSaysNothingOfItsLanguageIsLeftForThePersonToName(string code) =>
        Assert.Null(PastedCode.LanguageOf(code));

    private async Task<CheckReport> CheckAsync(string file, CodeLanguage language)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);
        output.WriteLine($"how: {launch.Explanation}");

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = language }.CheckAsync(launch);

        foreach (var finding in report.Findings) output.WriteLine($"[{finding.Kind}] {finding.Location}: {finding.Title}");
        return report;
    }

    [Fact]
    public async Task PastedJavaInAPackageIsBuiltAndItsMistakeNamedOnItsPastedLine()
    {
        if (!LocalFixLiveTests.Available("java")) return;

        var file = PastedCode.Save("""
            package shop;

            public class Till {
                public static void main(String[] args) {
                    int[] prices = {3, 4};
                    System.out.println(prices.length());
                }
            }
            """, CodeLanguage.Java, Folder("pasted-java"));

        var report = await CheckAsync(file, CodeLanguage.Java);

        var wrongLength = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Equal(6, wrongLength.Line);
    }

    [Fact]
    public async Task PastedPythonIsRunAndWhereItStopsIsItsPastedLine()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var file = PastedCode.Save("marks = []\n\nprint(sum(marks) / len(marks))\n", CodeLanguage.Python, Folder("pasted-python"));

        var report = await CheckAsync(file, CodeLanguage.Python);

        var byZero = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal(3, byZero.Line);
        Assert.StartsWith("It crashed: ZeroDivisionError", byZero.Title, StringComparison.Ordinal);
    }
}
