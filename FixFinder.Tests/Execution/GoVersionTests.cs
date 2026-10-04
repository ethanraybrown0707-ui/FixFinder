using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Execution.Versions;
using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Tests;

/// <summary>
/// Which Go a program needs and is built with: what its code uses - each part at the Go that added it, from the release
/// notes at go.dev/doc and the api/go1.N.txt files - what its go.mod declares, and which of the Gos on the computer builds
/// it. The Gos are made here, laid out as their installers lay them out, and nothing is run.
/// </summary>
public class GoVersionTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
        return path;
    }

    private string Folder(string relative) => Path.Combine(_temp.Path, relative);

    /// <summary>A Go as its installer lays one out: bin\go.exe, and the VERSION file at the top that names it.</summary>
    private string FakeGo(string relative, string version)
    {
        Write(Path.Combine(relative, "VERSION"), $"go{version}\ntime 2026-08-18T21:24:23Z\n");
        return Write(Path.Combine(relative, "bin", "go.exe"), "");
    }

    /// <summary>A computer with the given Go on PATH and the Gos made in its folders - looked in until disposed.</summary>
    private IDisposable Computer(string? onPath) => GoToolchains.LookingIn(new GoToolchains.Places(
        Folder("home"),
        [Folder("Program Files")],
        GoRoot: null,
        ModuleCache: Folder(@"home\go\pkg\mod"),
        FindOnPath: name => name == "go" ? onPath : null,
        Ask: _ => null));

    private static ParsedError CompileError(string file, int line, string message) => new()
    {
        LanguageId = "go",
        Confidence = 90,
        RawText = $"{file}:{line}:5: {message}",
        FirstLineSequence = 1,
        ExceptionType = "compile error",
        Message = message,
        Frames = [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = $"{file}:{line}:5: {message}" }],
    };

    private static CapturedLine Said(string text) => new(1, StreamKind.StdErr, text, TimeSpan.Zero);

    [Theory]
    [InlineData("package main\n\ntype Stack[T any] struct{ items []T }\n\nfunc (s *Stack[T]) Map[U any](f func(T) U) []U { return nil }\n", 27,
        "main.go declares a generic method at line 5, which Go 1.27 made part of the language")]
    [InlineData("package main\n\nfunc main() {\n\tcount := new(42)\n\t_ = count\n}\n", 26,
        "main.go passes an expression to new at line 4, which Go 1.26 made part of the language")]
    [InlineData("package main\n\ntype Set[T comparable] = map[T]struct{}\n", 24,
        "main.go declares a generic type alias at line 3, which Go 1.24 made part of the language")]
    [InlineData("package main\n\nfunc main() {\n\tfor n := range func(yield func(int) bool) { yield(1) } {\n\t\t_ = n\n\t}\n}\n", 23,
        "main.go ranges over a function at line 4, which Go 1.23 made part of the language")]
    [InlineData("package main\n\nfunc main() {\n\tfor i := range 10 {\n\t\t_ = i\n\t}\n}\n", 22,
        "main.go ranges over an integer at line 4, which Go 1.22 made part of the language")]
    [InlineData("package main\n\nfunc main() {\n\tbest := max(3, 7)\n\t_ = best\n}\n", 21,
        "main.go calls the built-in max at line 4, which Go 1.21 made part of the language")]
    [InlineData("package main\n\nfunc Sum[T int | float64](values []T) T {\n\tvar total T\n\treturn total\n}\n", 18,
        "main.go declares a type parameter at line 3, which Go 1.18 made part of the language")]
    [InlineData("package main\n\nimport \"uuid\"\n\nfunc main() { _ = uuid.New() }\n", 27,
        "main.go imports uuid at line 3, which Go 1.27 added")]
    [InlineData("package main\n\nimport (\n\t\"fmt\"\n\t\"slices\"\n)\n\nfunc main() { fmt.Println(slices.Contains([]int{1}, 1)) }\n", 21,
        "main.go imports slices at line 5, which Go 1.21 added")]
    [InlineData("package main\n\nimport \"strings\"\n\nfunc main() {\n\tbefore, after, found := strings.CutLast(\"a,b,c\", \",\")\n\t_, _, _ = before, after, found\n}\n", 27,
        "main.go calls strings.CutLast at line 6, which Go 1.27 added")]
    [InlineData("package main\n\nimport text \"strings\"\n\nfunc main() {\n\tfor line := range text.Lines(\"a\\nb\") {\n\t\t_ = line\n\t}\n}\n", 24,
        "main.go calls strings.Lines at line 6, which Go 1.24 added")]
    [InlineData("package main\n\nimport \"maps\"\n\nfunc main() {\n\tages := map[string]int{\"ada\": 36}\n\tfor name := range maps.Keys(ages) {\n\t\t_ = name\n\t}\n}\n", 23,
        "main.go calls maps.Keys at line 7, which Go 1.23 added")]
    [InlineData("package main\n\nimport \"sync\"\n\nfunc main() {\n\tvar wg sync.WaitGroup\n\twg.Go(func() {})\n\twg.Wait()\n}\n", 25,
        "main.go calls a sync.WaitGroup's Go method at line 7, which Go 1.25 added")]
    [InlineData("package main\n\nimport \"errors\"\n\ntype NotFound struct{}\n\nfunc (NotFound) Error() string { return \"not found\" }\n\nfunc main() {\n\t_, ok := errors.AsType[NotFound](nil)\n\t_ = ok\n}\n", 26,
        "main.go calls errors.AsType at line 10, which Go 1.26 added")]
    public void APartOfGoIsFoundWithTheGoThatAddedIt(string code, int minor, string because)
    {
        var file = Write($@"features{minor}-{Guid.NewGuid():N}\main.go", code);

        var needs = GoFeaturesUsed.Of([file]);

        Assert.NotNull(needs);
        Assert.Equal(0, needs!.Version.CompareTo(new LanguageVersion(1, minor)));
        Assert.Equal(because, needs.Because);
    }

    /// <summary>
    /// What only looks like a newer part of Go is not counted: the program's own max, a variable called clear, new of a
    /// type, an array whose length is worked out, maps from golang.org/x/exp, and text and comments.
    /// </summary>
    [Fact]
    public void WhatOnlyLooksLikeANewerPartOfGoIsNotCounted()
    {
        var file = Write(@"quiet\main.go", """
            package main

            import (
            	"fmt"
            	"golang.org/x/exp/maps"
            )

            const Rows, Cols = 3, 4

            type Grid [Rows * Cols]int

            type Point struct{ X, Y int }

            func max(a, b int) int {
            	if a > b {
            		return a
            	}
            	return b
            }

            func main() {
            	// for i := range 10 { min(1, 2) }
            	text := "for i := range 10 and new(42) and type A[T any] = []T"
            	raw := `clear(m) and slices.Concat`
            	p := new(Point)
            	clear := false
            	fmt.Println(max(1, 2), text, raw, p, clear, maps.Keys(map[string]int{}))
            }
            """);

        Assert.Null(GoFeaturesUsed.Of([file]));
    }

    /// <summary>A function literal that returns a generic type is not a generic method: a method is declared at the start of its line.</summary>
    [Fact]
    public void AFunctionLiteralReturningAGenericTypeIsNotAGenericMethod()
    {
        var file = Write(@"literal\main.go", "package main\n\ntype Stack[T any] struct{ items []T }\n\nfunc main() {\n\tbuild := func(size int) Stack[int] { return Stack[int]{} }\n\t_ = build\n}\n");

        Assert.Equal("main.go declares a type parameter at line 3, which Go 1.18 made part of the language", GoFeaturesUsed.Of([file])!.Because);
    }

    [Theory]
    [InlineData("module example.com/marks\n\ngo 1.22\n", "1.22", null)]
    [InlineData("module example.com/marks\n\ngo 1.27.0\n\ntoolchain go1.27.3\n", "1.27.0", "1.27.3")]
    [InlineData("module example.com/marks\n\ngo 1.21rc1\n", "1.21", null)]
    public void WhatTheGoModDeclaresIsRead(string goMod, string goLine, string? toolchain)
    {
        var folder = $"declared{Guid.NewGuid():N}";
        Write($@"{folder}\go.mod", goMod);
        var main = Write($@"{folder}\main.go", "package main\n\nfunc main() {}\n");

        var declared = DeclaredGo.Of(main)!;

        Assert.Equal(goLine, declared.GoLine.ToString());
        Assert.Equal(toolchain, declared.Toolchain?.ToString());
        Assert.Equal($"its go.mod says go {goLine}", Assert.Single(declared.AtLeast).Because);
    }

    /// <summary>A go.mod with no go line is taken as go 1.16, as the go command takes it, and asks for no Go of its own.</summary>
    [Fact]
    public void AGoModWithNoGoLineIsGo116()
    {
        Write(@"noGoLine\go.mod", "module example.com/marks\n");
        var main = Write(@"noGoLine\main.go", "package main\n\nfunc main() {}\n");

        var declared = DeclaredGo.Of(main)!;

        Assert.Null(declared.GoLine);
        Assert.Equal("1.16", declared.LanguageVersion.ToString());
        Assert.Empty(declared.AtLeast);
    }

    /// <summary>The go command finds go.mod in the folders above the one it runs in, and a workspace's go.work there too.</summary>
    [Fact]
    public void AGoModAboveTheFileAndAGoWorkAboveThatAreRead()
    {
        Write(@"workspace\go.work", "go 1.24\n\nuse ./marks\n");
        var module = Write(@"workspace\marks\go.mod", "module example.com/marks\n\ngo 1.22\n");
        var main = Write(@"workspace\marks\cmd\report\main.go", "package main\n\nfunc main() {}\n");

        var declared = DeclaredGo.Of(main)!;

        Assert.Equal(module, declared.Module);
        Assert.Equal(["its go.mod says go 1.22", "its go.work says go 1.24"], declared.AtLeast.Select(need => need.Because));
    }

    [Fact]
    public void AGoIsKnownByItsVersionFile()
    {
        var go = FakeGo(@"Program Files\Go", "1.27.0");

        Assert.Equal("go1.27.0", GoToolchains.VersionFromFiles(go));
        Assert.Equal("1.27.0", GoToolchains.At(go, "installed for all users")!.VersionText);
    }

    [Fact]
    public void EveryGoInTheUsualPlacesIsFoundNewestFirst()
    {
        var usual = FakeGo(@"Program Files\Go", "1.22.5");
        FakeGo(@"home\sdk\go1.24.2", "1.24.2");
        FakeGo(@"home\go\pkg\mod\golang.org\toolchain@v0.0.1-go1.27.0.windows-amd64", "1.27.0");
        using var computer = Computer(onPath: usual);

        var found = GoToolchains.Installed;

        Assert.Equal(["1.27.0", "1.24.2", "1.22.5"], found.Select(go => go.VersionText));
        Assert.Equal(["downloaded by the go command", "golang.org/dl's", "on PATH"], found.Select(go => go.FoundIn));
    }

    /// <summary>The usual Go builds a program it is new enough for - and how it ran still says what the code needs.</summary>
    [Fact]
    public void TheUsualGoBuildsCodeItIsNewEnoughFor()
    {
        var usual = FakeGo(@"Program Files\Go", "1.27.0");
        using var computer = Computer(onPath: usual);
        var main = Write(@"usual\main.go", "package main\n\nfunc main() {\n\tfor i := range 10 {\n\t\t_ = i\n\t}\n}\n");

        var setup = GoSetup.For(main)!;

        Assert.Equal(usual, setup.Go);
        Assert.Equal("Go 1.27.0 (on PATH)", setup.Explained);
        Assert.Equal("Its code needs Go 1.22 or later: main.go ranges over an integer at line 4, which Go 1.22 made part of the language.", setup.CodeNeeds);
    }

    /// <summary>A go line of 1.24.0 rules out the usual Go of 1.22: the oldest Go here that has it builds the program.</summary>
    [Fact]
    public void TheGoLineDecidesAmongTheGos()
    {
        var usual = FakeGo(@"Program Files\Go", "1.22.5");
        var nearest = FakeGo(@"home\sdk\go1.24.2", "1.24.2");
        FakeGo(@"home\go\pkg\mod\golang.org\toolchain@v0.0.1-go1.27.0.windows-amd64", "1.27.0");
        using var computer = Computer(onPath: usual);
        Write(@"goLine\go.mod", "module example.com/marks\n\ngo 1.24.0\n");
        var main = Write(@"goLine\main.go", "package main\n\nfunc main() {}\n");

        var setup = GoSetup.For(main)!;

        Assert.Equal(nearest, setup.Go);
        Assert.Equal("Go 1.24.2 (golang.org/dl's), as its go.mod says go 1.24.0", setup.Explained);
        Assert.Null(setup.CodeNeeds);
    }

    /// <summary>A go line naming a patch - as go mod init writes it - is not met by an earlier patch of the same Go.</summary>
    [Fact]
    public void AGoLineWithAPatchIsNotMetByAnEarlierPatch()
    {
        var usual = FakeGo(@"Program Files\Go", "1.27.0");
        using var computer = Computer(onPath: usual);
        Write(@"patch\go.mod", "module example.com/marks\n\ngo 1.27.3\n");
        var main = Write(@"patch\main.go", "package main\n\nfunc main() {}\n");

        Assert.Equal("Go 1.27.0 (on PATH), though its go.mod says go 1.27.3, and no Go of 1.27.3 or later is on this computer", GoSetup.For(main)!.Explained);
    }

    /// <summary>As the go command does, a toolchain line naming a later Go than the usual one has that Go build the program, when it is here.</summary>
    [Fact]
    public void TheToolchainLineChoosesALaterGoWhenItIsHere()
    {
        var usual = FakeGo(@"Program Files\Go", "1.22.5");
        var named = FakeGo(@"home\sdk\go1.24.2", "1.24.2");
        FakeGo(@"home\go\pkg\mod\golang.org\toolchain@v0.0.1-go1.27.0.windows-amd64", "1.27.0");
        using var computer = Computer(onPath: usual);
        Write(@"toolchainLine\go.mod", "module example.com/marks\n\ngo 1.22.0\n\ntoolchain go1.24.2\n");
        var main = Write(@"toolchainLine\main.go", "package main\n\nfunc main() {}\n");

        var setup = GoSetup.For(main)!;

        Assert.Equal(named, setup.Go);
        Assert.Equal("Go 1.24.2 (golang.org/dl's), as its go.mod's toolchain line names go1.24.2", setup.Explained);
    }

    [Fact]
    public void WithNoGoNewEnoughItSaysSoAndTheNoteSaysWhatToInstall()
    {
        var usual = FakeGo(@"Program Files\Go", "1.21.13");
        using var computer = Computer(onPath: usual);
        var main = Write(@"tooOld\main.go", "package main\n\nfunc main() {\n\tfor i := range 10 {\n\t\t_ = i\n\t}\n}\n");

        var setup = GoSetup.For(main)!;

        Assert.Equal(usual, setup.Go);
        Assert.Equal("Go 1.21.13 (on PATH), though main.go ranges over an integer at line 4, which Go 1.22 made part of the language, and no Go of 1.22 or later is on this computer", setup.Explained);
        Assert.Equal("main.go ranges over an integer at line 4, which Go 1.22 made part of the language - and Go 1.21.13 (on PATH) is the newest Go on this computer, so " +
                     "that part of it cannot be built, which is not a mistake in the code. Installing Go 1.22 or later builds it - go.dev/dl has every Go release, " +
                     "and winget installs the newest with:\n  winget install GoLang.Go",
                     ToolchainVersionErrors.NoteFor(main));
    }

    /// <summary>Go's own words for code newer than its go line: the note names the go line, and what raising it does.</summary>
    [Fact]
    public void CodeNewerThanItsGoLineIsSaidAsThat()
    {
        Write(@"langLine\go.mod", "module example.com/marks\n\ngo 1.20\n");
        var main = Write(@"langLine\main.go", "package main\n\nfunc main() {\n\tbest := max(3, 7)\n\t_ = best\n}\n");

        var note = GoVersionErrors.NoteFor([CompileError(main, 4, "built-in max requires go1.21 or later (-lang was set to go1.20; check go.mod)")], [], main);

        Assert.Equal("main.go:4 uses what Go 1.21 made part of the language, and its go.mod says go 1.20, so Go builds the module as Go 1.20 code, without it - " +
                     "which is not a mistake in the code. Raising the go line in its go.mod to go 1.21 or later builds it.", note);
    }

    [Fact]
    public void CodeNewerThanAGoModWithNoGoLineIsSaidAsThat()
    {
        Write(@"langMissing\go.mod", "module example.com/marks\n");
        var main = Write(@"langMissing\main.go", "package main\n\nfunc Sum[T int | float64](values []T) T {\n\tvar total T\n\treturn total\n}\n");

        var note = GoVersionErrors.NoteFor([CompileError(main, 3, "type parameter requires go1.18 or later (-lang was set to go1.16; check go.mod)")], [], main);

        Assert.Equal("main.go:3 uses what Go 1.18 made part of the language, and its go.mod has no go line, which Go takes as go 1.16, so Go builds the module " +
                     "as Go 1.16 code, without it - which is not a mistake in the code. Adding the line go 1.18 to its go.mod builds it.", note);
    }

    /// <summary>A go line later than any Go here: Go will not build the module at all, and the note says which Go to install.</summary>
    [Fact]
    public void AGoLineLaterThanAnyGoHereSaysWhatToInstall()
    {
        var usual = FakeGo(@"Program Files\Go", "1.27.0");
        using var computer = Computer(onPath: usual);
        Write(@"goLineTooNew\go.mod", "module example.com/marks\n\ngo 1.28\n");
        var main = Write(@"goLineTooNew\main.go", "package main\n\nfunc main() {}\n");

        var note = GoVersionErrors.NoteFor([], [Said("go: go.mod requires go >= 1.28 (running go 1.27.0; GOTOOLCHAIN=local)")], main);

        Assert.Equal("Its go.mod says go 1.28, and the newest Go on this computer is Go 1.27.0 (on PATH), which will not build it - so it was not built, which is " +
                     "not a mistake in the code. Installing Go 1.28 or later builds it - go.dev/dl has every Go release, and winget installs the newest with:\n" +
                     "  winget install GoLang.Go", note);
    }

    /// <summary>A module the program uses that needs a later Go than its go line: a later Go here builds it once the go line asks for it.</summary>
    [Fact]
    public void AModuleNeedingALaterGoSaysWhichGoHereBuildsIt()
    {
        var usual = FakeGo(@"Program Files\Go", "1.22.5");
        FakeGo(@"home\go\pkg\mod\golang.org\toolchain@v0.0.1-go1.27.0.windows-amd64", "1.27.0");
        using var computer = Computer(onPath: usual);
        Write(@"dependency\go.mod", "module example.com/marks\n\ngo 1.22\n");
        var main = Write(@"dependency\main.go", "package main\n\nfunc main() {}\n");

        var note = GoVersionErrors.NoteFor([], [Said("go: example.com/lib@v1.2.0 requires go >= 1.24 (running go 1.22.5; GOTOOLCHAIN=local)")], main);

        Assert.Equal("example.com/lib@v1.2.0, which it uses, needs Go 1.24 or later, and it was built with Go 1.22.5, which will not build it - so it was not " +
                     "built, which is not a mistake in the code. Go 1.27.0 (downloaded by the go command) is on this computer: raising the go line in its go.mod " +
                     "to go 1.24 builds it with that Go.", note);
    }
}

/// <summary>Programs built with the Go on this computer, as the window builds them, saying the Go their code needs - and what Go says of its go line.</summary>
public class GoVersionLiveTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
        return path;
    }

    private const string RangesOverAnInteger = "package main\n\nfunc main() {\n\tfor i := range 3 {\n\t\tprintln(i)\n\t}\n}\n";

    private static bool GoOfAtLeast122 => GoToolchains.Usual is { } usual && usual.Version >= new LanguageVersion(1, 22);

    [Fact]
    public void HowItRanSaysTheGoTheCodeNeeds()
    {
        if (!GoOfAtLeast122) return;

        var main = Write(@"plain\main.go", RangesOverAnInteger);

        var plan = TargetFactory.FromFile(main);

        Assert.True(plan.Ok, plan.Problem);
        Assert.Equal($"Running it with Go {GoToolchains.Usual!.VersionText} (on PATH). Its code needs Go 1.22 or later: main.go ranges over an integer at line 4, " +
                     "which Go 1.22 made part of the language.", plan.Explanation);
        Assert.Equal("local", plan.Spec!.ExtraEnvironment["GOTOOLCHAIN"]);
    }

    /// <summary>The real Go's words for code newer than its go line are the words the note is made from.</summary>
    [Fact]
    public async Task GoSaysWhatItsGoLineLeavesOut()
    {
        if (!GoOfAtLeast122) return;

        Write(@"oldGoLine\go.mod", "module example.com/marks\n\ngo 1.21\n");
        var main = Write(@"oldGoLine\main.go", RangesOverAnInteger);

        var build = await BuildAsync(main);

        Assert.Equal("main.go:4 uses what Go 1.22 made part of the language, and its go.mod says go 1.21, so Go builds the module as Go 1.21 code, without it - " +
                     "which is not a mistake in the code. Raising the go line in its go.mod to go 1.22 or later builds it.",
                     GoVersionErrors.NoteFor(new GoCompileParser().ParseAll(build.Lines), build.Lines, main));
    }

    /// <summary>The real Go's words for a go line later than itself, with FixFinder's GOTOOLCHAIN=local, are the words the note is made from.</summary>
    [Fact]
    public async Task GoSaysWhenItsGoLineIsLaterThanAnyGoHere()
    {
        if (!GoOfAtLeast122) return;

        Write(@"futureGoLine\go.mod", "module example.com/marks\n\ngo 1.99\n");
        var main = Write(@"futureGoLine\main.go", "package main\n\nfunc main() {}\n");

        var build = await BuildAsync(main);
        var newest = GoToolchains.Installed[0];

        Assert.StartsWith($"Its go.mod says go 1.99, and the newest Go on this computer is Go {newest.VersionText} ({newest.FoundIn}), which will not build it",
                          GoVersionErrors.NoteFor([], build.Lines, main));
    }

    private static async Task<TargetRunResult> BuildAsync(string main) =>
        await new TargetRunner().RunAsync(new TargetSpec
        {
            ExecutablePath = GoSetup.For(main)!.Go,
            Arguments = $"build -o \"{Path.Combine(Path.GetDirectoryName(main)!, "check.exe")}\" .",
            WorkingDirectory = Path.GetDirectoryName(main)!,
            Timeout = TimeSpan.FromMinutes(3),
            ExtraEnvironment = GoSetup.Environment,
        }, CancellationToken.None);
}
