using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// A class that never writes a member its interface or abstract base requires, built for real in C#, Java and C++ -
/// each must end with that member added, with the declared signature and a body that says it is not written yet.
/// </summary>
/// <remarks>
/// Live: each case skips when its toolchain is missing. None of them runs the program, so none is affected by
/// Application Control blocking a freshly built executable.
/// </remarks>
public class UnwrittenMemberTests
{
    /// <summary>The toolchain, the file, the program, the rule (or null for no fix), and text the copied fix must contain.</summary>
    public static TheoryData<string, string, string, string?, string[]> Cases => new()
    {
        {
            "dotnet", "Program.cs",
            "using System;\n\ninterface IShape\n{\n    double Area();\n}\n\nclass Square : IShape\n{\n}\n\nclass Program\n{\n    static void Main()\n    {\n        Console.WriteLine(new Square());\n    }\n}\n",
            "csharp-unwritten-member",
            ["{\n    public double Area() => throw new System.NotImplementedException();\n}"]
        },
        {
            "dotnet", "Program.cs",
            "using System;\nusing System.Collections.Generic;\n\ninterface IShape\n{\n    double Area();\n    string Name(int width, string label = \"x\");\n    int Sides { get; }\n    string Colour { get; set; }\n}\n\nabstract class Base\n{\n    protected abstract IEnumerable<T> Items<T>(T seed) where T : class;\n}\n\nclass Square : Base, IShape\n{\n}\n\nclass Program\n{\n    static void Main() => Console.WriteLine(new Square());\n}\n",
            "csharp-unwritten-member",
            [
                "protected override IEnumerable<T> Items<T>(T seed) => throw new System.NotImplementedException();",
                "public double Area() => throw new System.NotImplementedException();",
                "public string Name(int width, string label = \"x\") => throw new System.NotImplementedException();",
                "public int Sides => throw new System.NotImplementedException();",
                "public string Colour { get; set; }",
            ]
        },
        {
            "javac", "App.java",
            "abstract class Shape {\n    abstract double area();\n}\n\nclass Square extends Shape {\n}\n\npublic class App {\n    public static void main(String[] args) {\n        System.out.println(new Square());\n    }\n}\n",
            "java-unwritten-method",
            ["class Square extends Shape {\n    @Override\n    double area() {\n        throw new UnsupportedOperationException(\"area is not written yet\");\n    }\n}"]
        },
        {
            "javac", "App.java",
            "interface Shape {\n    double area();\n}\n\nclass Square implements Shape {\n}\n\npublic class App {\n    public static void main(String[] args) {\n        System.out.println(new Square());\n    }\n}\n",
            "java-unwritten-method",
            ["    @Override\n    public double area() {\n        throw new UnsupportedOperationException(\"area is not written yet\");\n    }"]
        },
        {
            "gcc", "app.cpp",
            "#include <iostream>\n\nclass Shape {\npublic:\n    virtual ~Shape() = default;\n    virtual double area() const = 0;\n};\n\nclass Square : public Shape {\npublic:\n    double side = 2.0;\n};\n\nint main() {\n    Square s;\n    std::cout << s.area() << std::endl;\n    return 0;\n}\n",
            "cpp-unwritten-override",
            ["    double side = 2.0;\n    double area() const override { throw std::logic_error(\"area is not written yet\"); }\n};"]
        },
        {
            "msvc", "app.cpp",
            "#include <iostream>\n\nclass Shape {\npublic:\n    virtual ~Shape() = default;\n    virtual double area() const = 0;\n};\n\nclass Square : public Shape {\npublic:\n    double side = 2.0;\n};\n\nint main() {\n    Square s;\n    std::cout << s.area() << std::endl;\n    return 0;\n}\n",
            "cpp-unwritten-override",
            ["    double area() const override { throw std::logic_error(\"area is not written yet\"); }\n};"]
        },
        {
            "gcc", "app.cpp",
            "#include <iostream>\n#include <string>\n\nclass Shape {\npublic:\n    virtual ~Shape() = default;\n    virtual double area() const = 0;\n    virtual std::string name(int width, const char* label) = 0;\n};\n\nclass Square : public Shape {\n    double side = 2.0;\n};\n\nint main() {\n    Shape* s = new Square();\n    std::cout << s->area() << std::endl;\n    delete s;\n    return 0;\n}\n",
            "cpp-unwritten-override",
            ["    double side = 2.0;\npublic:\n    double area() const override { throw std::logic_error(\"area is not written yet\"); }\n    std::string name(int width, const char* label) override { throw std::logic_error(\"name is not written yet\"); }\n};"]
        },
        {
            // The abstract class itself: there is no derived class to add anything to.
            "gcc", "app.cpp",
            "#include <iostream>\n\nclass Shape {\npublic:\n    virtual double area() const = 0;\n};\n\nint main() {\n    Shape s;\n    std::cout << s.area() << std::endl;\n    return 0;\n}\n",
            null,
            []
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TheUnwrittenMemberIsAddedToBeWritten(string toolchain, string fileName, string source, string? rule, string[] expected)
    {
        var ready = toolchain switch
        {
            "dotnet" => TargetFactory.FindOnPath("dotnet") is not null,
            "javac" => Toolchains.FindJavac() is not null && Toolchains.FindJava() is not null,
            "gcc" => Toolchains.FindGnu(cpp: true) is not null,
            "msvc" => Toolchains.FindMsvc() is not null,
            _ => false,
        };

        if (!ready) return;

        using var msvcOnly = toolchain == "msvc" ? Toolchains.WithoutGnu() : null;
        using var temp = new TempFolder();

        var path = Path.Combine(temp.Path, fileName);
        File.WriteAllText(path, source);

        var plan = TargetFactory.FromFile(path, TimeSpan.FromMinutes(5));
        Assert.True(plan.Ok, plan.Problem);

        using var http = new FixFinderHttpClient();
        var outcome = await new FixFinderSession(http, new FixSourceRegistry()).RunAsync(plan, new SearchBudget(Cache: CacheMode.CacheOnly));

        if (rule is null)
        {
            Assert.NotEqual(SessionResult.FoundFix, outcome.Result);
            return;
        }

        if (ApplicationControl.Refused(outcome)) return;
        Assert.True(outcome.Result == SessionResult.FoundFix, $"{fileName} ({toolchain}): {outcome.Result} - {outcome.Headline}");
        Assert.Equal($"local:{rule}", outcome.Best!.Id);

        var copied = PasteableFix.For(new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));
        Assert.NotNull(copied);

        var text = copied!.Text.ReplaceLineEndings("\n") + "\n";
        Assert.All(expected, e => Assert.Contains(e, text, StringComparison.Ordinal));
    }
}
