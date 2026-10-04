using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// Go modules a program's go.mod requires that are not in Go's module cache: FixFinder never downloads them - every go
/// command it starts has GOPROXY=off - and the errors go gives for them are said once, as a module not here, rather than
/// as mistakes in the code.
/// </summary>
public class GoModuleTests : IDisposable
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

    private static ParsedError GoError(string file, int line, string message) => new()
    {
        LanguageId = "go",
        Confidence = 90,
        RawText = $"{file}:{line}:8: {message}",
        FirstLineSequence = 1,
        ExceptionType = "compile error",
        Message = message,
        Frames = [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = $"{file}:{line}:8: {message}" }],
    };

    /// <summary>Every go command is given what keeps it from downloading: a Go, a module, or a checksum.</summary>
    [Fact]
    public void EveryGoCommandIsKeptFromDownloading()
    {
        Assert.Equal("local", GoSetup.Environment["GOTOOLCHAIN"]);
        Assert.Equal("off", GoSetup.Environment["GOPROXY"]);
        Assert.Equal("off", GoSetup.Environment["GOSUMDB"]);
    }

    [Fact]
    public void AModuleNotHereIsSaidOnceAndItsErrorsAreNotTakenForMistakes()
    {
        Write(@"notHere\go.mod", "module example.com/app\n\ngo 1.22\n\nrequire example.com/notreal v1.0.0\n");
        var main = Write(@"notHere\main.go", "package main\n\nimport \"example.com/notreal/marks\"\n\nfunc main() { marks.Show() }\n");
        var mistake = GoError(main, 5, "undefined: total");

        var sorted = LibraryErrors.Sort([GoError(main, 3, "module lookup disabled by GOPROXY=off"), mistake], main);

        Assert.Equal([mistake], sorted.CodeErrors);
        Assert.Equal(1, sorted.FromLibraries);
        Assert.Equal("main.go imports example.com/notreal/marks, from the module example.com/notreal v1.0.0 its go.mod requires, which is not in Go's " +
                     "module cache on this computer. FixFinder never downloads anything - it runs go with GOPROXY=off, so the \"go: downloading\" go " +
                     "printed downloaded nothing - and so the program was not built, which is not a mistake in the code. Running go mod download in its " +
                     "module's folder downloads the modules it needs, and FixFinder builds it after that.", sorted.Note);
    }

    /// <summary>The module is the requirement the import's path starts with - the longest, for one module inside another's path.</summary>
    [Fact]
    public void TheModuleIsTheRequirementTheImportsPathStartsWith()
    {
        Write(@"block\go.mod", "module example.com/app\n\ngo 1.22\n\nrequire (\n\texample.com/kit v1.2.0\n\texample.com/kit/v2 v2.0.1 // indirect\n)\n");
        var main = Write(@"block\main.go", "package main\n\nimport (\n\t\"example.com/kit/v2/grades\"\n)\n\nfunc main() { grades.Show() }\n");

        var sorted = LibraryErrors.Sort([GoError(main, 4, "module lookup disabled by GOPROXY=off")], main);

        Assert.StartsWith("main.go imports example.com/kit/v2/grades, from the module example.com/kit/v2 v2.0.1 its go.mod requires,", sorted.Note);
        Assert.Empty(sorted.CodeErrors);
    }

    /// <summary>Errors that are not about a module not here are left as they are, with nothing said.</summary>
    [Fact]
    public void OtherErrorsAreLeftAsTheyAre()
    {
        var main = Write(@"plain\main.go", "package main\n\nfunc main() { total++ }\n");
        var mistake = GoError(main, 3, "undefined: total");

        var sorted = LibraryErrors.Sort([mistake], main);

        Assert.Equal([mistake], sorted.CodeErrors);
        Assert.Null(sorted.Note);
    }
}

/// <summary>A module not in Go's module cache, built as the window builds it: go downloads nothing, and the note says so.</summary>
public class GoModuleLiveTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task AModuleNotHereIsNotDownloadedAndTheNoteSaysSo()
    {
        if (GoToolchains.Usual is not { } usual || usual.Version < new Core.Execution.Versions.LanguageVersion(1, 22)) return;

        File.WriteAllText(Path.Combine(_temp.Path, "go.mod"), "module example.com/app\n\ngo 1.22\n\nrequire example.com/notreal v1.0.0\n");
        File.WriteAllText(Path.Combine(_temp.Path, "go.sum"),
            "example.com/notreal v1.0.0 h1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\nexample.com/notreal v1.0.0/go.mod h1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\n");
        var main = Path.Combine(_temp.Path, "main.go");
        File.WriteAllText(main, "package main\n\nimport \"example.com/notreal/marks\"\n\nfunc main() { marks.Show() }\n");

        var report = await CompilerDiagnostics.CollectAsync(TargetFactory.FromFile(main), [main], CodeLanguage.Go);
        var sorted = LibraryErrors.Sort(report.Errors, main);

        // What the note says go printed, go printed: it names the module it did not download.
        Assert.Contains(report.Output, line => line.Text.Trim() == "go: downloading example.com/notreal v1.0.0");
        Assert.Empty(sorted.CodeErrors);
        Assert.StartsWith("main.go imports example.com/notreal/marks, from the module example.com/notreal v1.0.0 its go.mod requires, which is not in Go's " +
                          "module cache on this computer. FixFinder never downloads anything", sorted.Note);
    }
}
