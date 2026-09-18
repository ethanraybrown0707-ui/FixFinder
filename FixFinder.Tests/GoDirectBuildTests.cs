using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// Building a Go file with the tools go build runs, and the proof that it says what go build says.
/// </summary>
/// <remarks>
/// The live cases build the same file both ways and require the same exit code and every line of output the
/// same: a compile error, a clean build, an unused import, a missing main (which only the linker reports), a
/// syntax error, and more errors than the compiler lists. Each skips when Go is not installed.
/// </remarks>
public class GoDirectBuildTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------------------ what decides the plan

    [Fact]
    public void ImportsAreReadInEveryPlainForm()
    {
        var header = GoDirectBuild.Header(
            "// Package main does things.\npackage main\n\nimport \"os\"\nimport (\n\t\"fmt\" // printing\n\tstr \"strings\"\n\t. \"math\"\n\t_ \"embed\"\n\t/* raw */ `sort`\n)\n\nfunc main() {}\n");

        Assert.NotNull(header);
        Assert.Equal("main", header.Value.Package);
        Assert.Equal(["os", "fmt", "strings", "math", "embed", "sort"], header.Value.Imports);
    }

    [Fact]
    public void AFileWithNoImportsHasAnEmptyList()
    {
        var header = GoDirectBuild.Header("package main\n\nfunc main() { println(1) }\n");

        Assert.NotNull(header);
        Assert.Empty(header.Value.Imports);
    }

    [Theory]
    [InlineData("package main\n\nimport \"f\\u006dt\"\n")]
    [InlineData("import \"fmt\"\n")]
    [InlineData("package main\nimport (\n\t\"fmt\"\n")]
    public void AnythingButThePlainFormsIsNotRead(string text) =>
        Assert.Null(GoDirectBuild.Header(text));

    [Theory]
    [InlineData("app.go", "//go:build windows\n\npackage main\n\nfunc main() {}\n")]
    [InlineData("app.go", "// +build ignore\n\npackage main\n\nfunc main() {}\n")]
    [InlineData("app.go", "package main\n\n// #include <stdio.h>\nimport \"C\"\n\nfunc main() {}\n")]
    [InlineData("app.go", "package main\n\nimport _ \"embed\"\n\n//go:embed hello.txt\nvar s string\n\nfunc main() {}\n")]
    [InlineData("app.go", "//go:debug panicnil=1\npackage main\n\nfunc main() {}\n")]
    [InlineData("app_test.go", "package main\n\nfunc main() {}\n")]
    public void AFileWhoseContentsChangeTheBuildIsLeftToGoBuild(string name, string text) =>
        Assert.Null(GoDirectBuild.KeyFor(name, text));

    [Fact]
    public void TheKeyIsTheNameThePackageAndTheImportsAndNothingElse()
    {
        var one = GoDirectBuild.KeyFor("app.go", "package main\n\nimport (\n\t\"strings\"\n\t\"fmt\"\n)\n\nfunc main() { fmt.Println(strings.ToUpper(\"a\")) }\n");
        var two = GoDirectBuild.KeyFor("app.go", "package main\n\nimport \"fmt\"\nimport \"strings\"\n\nfunc main() { x := 1 }\n");
        var other = GoDirectBuild.KeyFor("main.go", "package main\n\nimport \"fmt\"\nimport \"strings\"\n\nfunc main() { x := 1 }\n");

        Assert.NotNull(one);
        Assert.Equal(one, two);
        Assert.NotEqual(one, other);
    }

    // ------------------------------------------------------------------ reading the plan and the output

    [Fact]
    public void PrintedCommandsAreSplitIntoWords()
    {
        var words = GoDirectBuild.Words("GOROOT='C:\\Program Files\\Go' \"C:\\\\Program Files\\\\Go\\\\pkg\\\\tool\\\\link.exe\" -o \"$WORK\\\\b001\\\\exe\\\\a.out.exe\" -buildmode=pie");

        Assert.Equal(["GOROOT='C:\\Program Files\\Go'", "C:\\Program Files\\Go\\pkg\\tool\\link.exe", "-o", "$WORK\\b001\\exe\\a.out.exe", "-buildmode=pie"], words);
    }

    [Theory]
    [InlineData("C:\\w\\app.go:3:2: undefined: x", "C:\\w", ".", ".\\app.go:3:2: undefined: x")]
    [InlineData("\tC:\\w\\app.go:3:2: have x", "C:\\w", ".", "\t.\\app.go:3:2: have x")]
    [InlineData("see C:\\w\\app.go", "C:\\w", ".", "see .\\app.go")]
    [InlineData("x:C:\\w\\app.go", "C:\\w", ".", "x:C:\\w\\app.go")]
    public void ToolOutputIsShortenedTheWayTheGoCommandShortensIt(string line, string old, string replacement, string expected) =>
        Assert.Equal(expected, GoDirectBuild.ReplacePrefix(line, old, replacement));

    private string[] Printed(string probe, string cached) =>
    [
        "mkdir -p $WORK\\b001\\",
        "",
        "#",
        "# command-line-arguments",
        "#",
        "",
        "cat >$WORK\\b001\\importcfg << 'EOF' # internal",
        "# import config",
        $"packagefile fmt={cached}",
        "EOF",
        $"cd {Path.GetDirectoryName(probe)}",
        $"\"C:\\\\Go\\\\pkg\\\\tool\\\\windows_amd64\\\\compile.exe\" -o \"$WORK\\\\b001\\\\_pkg_.a\" -trimpath \"$WORK\\\\b001=>\" -p main -importcfg \"$WORK\\\\b001\\\\importcfg\" -pack \"{probe.Replace("\\", "\\\\")}\"",
        "go tool buildid -w \"$WORK\\\\b001\\\\_pkg_.a\" # internal",
        "cat >$WORK\\b001\\importcfg.link << 'EOF' # internal",
        "packagefile command-line-arguments=$WORK\\b001\\_pkg_.a",
        $"packagefile fmt={cached}",
        "EOF",
        "mkdir -p $WORK\\b001\\exe\\",
        "cd .",
        "GOROOT='C:\\Go' \"C:\\\\Go\\\\pkg\\\\tool\\\\windows_amd64\\\\link.exe\" -o \"$WORK\\\\b001\\\\exe\\\\a.out.exe\" -importcfg \"$WORK\\\\b001\\\\importcfg.link\" \"$WORK\\\\b001\\\\_pkg_.a\"",
    ];

    [Fact]
    public void APlanIsReadButOnlyWhenItsCachedPackagesAreThere()
    {
        var probe = Path.Combine(_temp.Path, "probe", "app.go");
        var cached = Path.Combine(_temp.Path, "cache", "fmt-d");
        Directory.CreateDirectory(Path.GetDirectoryName(cached)!);

        // The tools must exist too; without Go at C:\Go the plan is refused for that reason instead.
        Assert.Null(GoDirectBuild.Read(Printed(probe, cached), probe));

        File.WriteAllText(cached, "");

        if (File.Exists("C:\\Go\\pkg\\tool\\windows_amd64\\compile.exe"))
        {
            var plan = GoDirectBuild.Read(Printed(probe, cached), probe);

            Assert.NotNull(plan);
            Assert.Equal(2, plan.Commands.Count);
            Assert.True(plan.Commands[0].InPackageFolder);
            Assert.Equal("C:\\Go", plan.Commands[1].Environment["GOROOT"]);
        }
    }

    [Fact]
    public void APlanThatImportsAPackageNotBuiltYetIsNotUsed()
    {
        // What go build -n prints on a machine whose build cache is empty: fmt is to be built first, in the
        // same work folder, and a plan run on its own would find nothing there to import.
        var probe = Path.Combine(_temp.Path, "probe2", "app.go");
        var printed = Printed(probe, "$WORK\\b002\\_pkg_.a");

        Assert.Null(GoDirectBuild.Read(printed, probe, out var unbuilt));
        Assert.True(unbuilt);
    }

    // ------------------------------------------------------------------ the same answer as go build

    public static TheoryData<string, string, string> Programs => new()
    {
        { "undefined", "undefined: undefinedName", "package main\n\nimport (\n\t\"fmt\"\n\t\"strings\"\n)\n\nfunc main() {\n\tfmt.Println(strings.ToUpper(\"a\"), undefinedName)\n\tx := 1\n}\n" },
        { "clean", "clean", "package main\n\nimport (\n\t\"fmt\"\n\t\"strings\"\n)\n\nfunc main() {\n\tfmt.Println(strings.ToUpper(\"a\"))\n}\n" },
        { "unused-import", "\"strings\"", "package main\n\nimport (\n\t\"fmt\"\n\t\"strings\"\n)\n\nfunc main() {\n\tfmt.Println(\"a\")\n}\n" },
        { "no-main", "function main is undeclared", "package main\n\nimport (\n\t\"fmt\"\n\t\"strings\"\n)\n\nfunc Main() {\n\tfmt.Println(strings.ToUpper(\"a\"))\n}\n" },
        { "syntax", "syntax error", "package main\n\nimport (\n\t\"fmt\"\n\t\"strings\"\n)\n\nfunc main()\n{\n\tfmt.Println(strings.ToUpper(\"a\"))\n}\n" },
        { "many", "too many errors", "package main\n\nimport (\n\t\"fmt\"\n\t\"strings\"\n)\n\nfunc main() {\n\tfmt.Println(strings.ToUpper(\"a\"))\n" + string.Concat(Enumerable.Range(0, 15).Select(i => $"\tv{i} := u{i}\n")) + "}\n" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task BuildingDirectlySaysWhatGoBuildSays(string name, string expected, string program)
    {
        if (TargetFactory.FindOnPath("go") is not { } go) return;

        var content = System.Text.Encoding.UTF8.GetBytes(program);
        var key = GoDirectBuild.KeyFor("app.go", program);
        Assert.NotNull(key);

        // go build first, as a check does: on a machine whose build cache starts empty - CI's - it is what
        // builds the standard packages the plan then names.
        var usualFolder = Path.Combine(_temp.Path, name + "-build");
        Directory.CreateDirectory(usualFolder);
        var usualCopy = Path.Combine(usualFolder, "app.go");
        await File.WriteAllBytesAsync(usualCopy, content);

        var registry = new ParserRegistry();
        var run = await new TargetRunner(registry).RunAsync(new TargetSpec
        {
            ExecutablePath = go,
            Arguments = $"build -o \"{Path.Combine(usualFolder, "check.exe")}\" \"{usualCopy}\"",
            WorkingDirectory = usualFolder,
            // Generous: on a cold runner the first build of fmt and strings, with every other test building at once, has
            // taken more than two minutes.
            Timeout = TimeSpan.FromMinutes(5),
        }, CancellationToken.None);

        var usual = new CheckResult(true, run.ExitCode, run.Lines, CompileCheck.ErrorsIn(registry, run.Lines));

        var plan = await GoDirectBuild.PlanFor(go, key, "app.go", content);
        Assert.NotNull(plan);

        var directFolder = Path.Combine(_temp.Path, name + "-direct");
        Directory.CreateDirectory(directFolder);
        var directCopy = Path.Combine(directFolder, "app.go");
        await File.WriteAllBytesAsync(directCopy, content);

        var direct = await GoDirectBuild.RunAsync(plan, directCopy, directFolder, TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.NotNull(direct);
        Assert.True(FasterCheck.Agree(usual, usualFolder, direct, directFolder, everyLine: true, out var difference), difference);

        if (expected == "clean") Assert.True(usual.Clean);
        else Assert.Contains(usual.Lines, line => line.Text.Contains(expected, StringComparison.Ordinal));
    }
}
