using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>Programs made of more than one file - run as the whole of themselves, and fixed in whichever file the mistake is in,
/// with the fix checked against the whole program.</summary>
public class MultiFileProgramTests
{
    private static string Files(TempFolder temp, Dictionary<string, string> files)
    {
        foreach (var (name, text) in files)
        {
            var path = Path.Combine(temp.Path, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        return temp.Path;
    }

    private static bool Ready(string toolchain) => toolchain switch
    {
        "c" => Toolchains.FindGnu(cpp: false) is not null || Toolchains.FindMsvc() is not null,
        "cpp" => Toolchains.FindGnu(cpp: true) is not null || Toolchains.FindMsvc() is not null,
        "java" => Toolchains.FindJavac() is not null && Toolchains.FindJava() is not null,
        "go" => TargetFactory.FindOnPath("go") is not null,
        "dotnet" => TargetFactory.FindOnPath("dotnet") is not null,
        "python" => TargetFactory.FindOnPath("python") is not null || TargetFactory.FindOnPath("py") is not null,
        _ => false,
    };

    private static async Task<SessionOutcome> Run(string chosen)
    {
        var plan = TargetFactory.FromFile(chosen, LiveAllowance.For(chosen, TimeSpan.FromMinutes(5)));
        Assert.True(plan.Ok, plan.Problem);

        using var http = new FixFinderHttpClient();
        return await new FixFinderSession(http, new FixSourceRegistry()).RunAsync(plan, new SearchBudget(Cache: CacheMode.CacheOnly));
    }

    public static TheoryData<string, Dictionary<string, string>, string, string> Working => new()
    {
        {
            "c",
            new() { ["main.c"] = "#include <stdio.h>\n#include \"util.h\"\n\nint main(void) {\n    printf(\"%d\\n\", add(2, 3));\n    return 0;\n}\n", ["util.h"] = "int add(int a, int b);\n", ["util.c"] = "#include \"util.h\"\n\nint add(int a, int b) {\n    return a + b;\n}\n" },
            "main.c", "5"
        },
        {
            "cpp",
            new() { ["main.cpp"] = "#include <iostream>\n#include \"shapes.h\"\n\nint main() {\n    std::cout << area(3) << std::endl;\n    return 0;\n}\n", ["shapes.h"] = "int area(int side);\n", ["shapes.cpp"] = "#include \"shapes.h\"\n\nint area(int side) {\n    return side * side;\n}\n" },
            "main.cpp", "9"
        },
        {
            "java",
            new() { ["src/app/Main.java"] = "package app;\n\nimport app.util.Helper;\n\npublic class Main {\n    public static void main(String[] args) {\n        System.out.println(Helper.greet());\n    }\n}\n", ["src/app/util/Helper.java"] = "package app.util;\n\npublic class Helper {\n    public static String greet() {\n        return \"hello from a package\";\n    }\n}\n" },
            "src/app/Main.java", "hello from a package"
        },
        {
            "go",
            new() { ["main.go"] = "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tfmt.Println(double(21))\n}\n", ["helper.go"] = "package main\n\nfunc double(n int) int {\n\treturn n * 2\n}\n" },
            "main.go", "42"
        },
        {
            "dotnet",
            new() { ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net8.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n", ["Program.cs"] = "Console.WriteLine(Helper.Greet());\n", ["Helper.cs"] = "static class Helper\n{\n    public static string Greet() => \"hello from a project\";\n}\n" },
            "Program.cs", "hello from a project"
        },
        {
            "python",
            new() { ["shop/__init__.py"] = "", ["shop/app.py"] = "from .helper import greet\n\nprint(greet())\n", ["shop/helper.py"] = "def greet():\n    return \"hello from a package\"\n" },
            "shop/app.py", "hello from a package"
        },
    };

    [Theory]
    [MemberData(nameof(Working))]
    public async Task TheWholeProgramIsRun(string toolchain, Dictionary<string, string> files, string chosen, string printed)
    {
        if (!Ready(toolchain)) return;

        using var temp = new TempFolder();
        var root = Files(temp, files);

        var outcome = await Run(Path.Combine(root, chosen));

        if (ApplicationControl.Refused(outcome)) return;
        Assert.True(outcome.Result == SessionResult.RanFine, $"{toolchain}: {outcome.Result} - {outcome.Headline}: {string.Join(" | ", outcome.Run?.Lines.Select(l => l.Text) ?? [])}");
        Assert.Contains(outcome.Run!.Lines, line => line.Text.Trim() == printed);
    }

    public static TheoryData<string, Dictionary<string, string>, string, string, string> Broken => new()
    {
        {
            "c",
            new() { ["main.c"] = "#include <stdio.h>\n#include \"util.h\"\n\nint main(void) {\n    printf(\"%d\\n\", add(2, 3));\n    return 0;\n}\n", ["util.h"] = "int add(int a, int b);\n", ["util.c"] = "#include \"util.h\"\n\nint add(int a, int b) {\n    return a + b\n}\n" },
            "main.c", "c-compiler-fix-it|c-missing-semicolon", "return a + b;"
        },
        {
            "java",
            new() { ["src/app/Main.java"] = "package app;\n\nimport app.util.Helper;\n\npublic class Main {\n    public static void main(String[] args) {\n        System.out.println(Helper.greet());\n    }\n}\n", ["src/app/util/Helper.java"] = "package app.util;\n\npublic class Helper {\n    public static String greet() {\n        return \"hello\"\n    }\n}\n" },
            "src/app/Main.java", "java-missing-semicolon", "return \"hello\";"
        },
        {
            "go",
            new() { ["main.go"] = "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tfmt.Println(double(21))\n}\n", ["helper.go"] = "package main\n\nfunc double(n int) int {\n\tunused := 5\n\treturn n * 2\n}\n" },
            "main.go", "go-unused-variable", "func double(n int) int {\n\treturn n * 2"
        },
        {
            "dotnet",
            new() { ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net8.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n", ["Program.cs"] = "Console.WriteLine(Helper.Greet());\n", ["Helper.cs"] = "static class Helper\n{\n    public static string Greet()\n    {\n        return \"hello\"\n    }\n}\n" },
            "Program.cs", "csharp-missing-semicolon", "return \"hello\";"
        },
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public async Task AMistakeInAnotherFileIsFixedAndCheckedWithTheWholeProgram(string toolchain, Dictionary<string, string> files, string chosen, string rule, string expected)
    {
        if (!Ready(toolchain)) return;

        using var temp = new TempFolder();
        var root = Files(temp, files);

        var outcome = await Run(Path.Combine(root, chosen));

        if (ApplicationControl.Refused(outcome)) return;
        Assert.True(outcome.Result == SessionResult.FoundFix, $"{toolchain}: {outcome.Result} - {outcome.Headline}");
        Assert.Matches($"^local:(?:{rule})$", outcome.Best!.Id);

        var copied = PasteableFix.For(new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));
        Assert.NotNull(copied);
        Assert.Contains(expected, copied!.Text.ReplaceLineEndings("\n") + "\n", StringComparison.Ordinal);
    }

    [Fact]
    public void TwoProgramsSharingAFolderAreNotBuiltTogether()
    {
        using var temp = new TempFolder();
        Files(temp, new() { ["one.c"] = "int main(void) { return 0; }\n", ["two.c"] = "int main(void) { return 1; }\n" });

        Assert.Equal([Path.Combine(temp.Path, "one.c")], ProgramLayout.NativeSources(Path.Combine(temp.Path, "one.c")));
    }

    [Fact]
    public void AFileWithoutMainIsBuiltWithTheProgramThatUsesIt()
    {
        using var temp = new TempFolder();
        Files(temp, new() { ["main.c"] = "int add(int, int);\nint main(void) { return add(1, 2); }\n", ["util.c"] = "int add(int a, int b) { return a + b; }\n", ["notes.cpp"] = "int main() { }\n" });

        Assert.Equal(
            [Path.Combine(temp.Path, "util.c"), Path.Combine(temp.Path, "main.c")],
            ProgramLayout.NativeSources(Path.Combine(temp.Path, "util.c")));
    }

    [Fact]
    public void AJavaFileOutsideItsPackageFolderIsCompiledFromItsOwnFolder()
    {
        using var temp = new TempFolder();
        Files(temp, new() { ["Main.java"] = "package app;\npublic class Main { }\n" });

        Assert.Equal(temp.Path, ProgramLayout.JavaSourceRoot(Path.Combine(temp.Path, "Main.java")));
    }
}
