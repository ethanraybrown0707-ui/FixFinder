using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Patching;

namespace FixFinder.Tests;

/// <summary>
/// Covers where FixFinder decides it is allowed to write. Everything the patching stages do is
/// bounded by this answer, so a wrong one here is the mistake with the worst consequences in
/// the whole tool - and the tests care as much about <i>how</i> a root was found, and whether
/// it asks first, as about the path itself.
/// </summary>
public class SourceRootResolverTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "fixfinder-tests-" + Guid.NewGuid().ToString("N")[..8]);

    public SourceRootResolverTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string MakeProject(string name, params string[] relativeFiles)
    {
        var root = Path.Combine(_temp, name);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, $"{name}.csproj"), "<Project />");

        foreach (var relative in relativeFiles)
        {
            var full = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "// test file");
        }

        return root;
    }

    private static ParsedError ErrorWithFrameAt(string file) => new()
    {
        LanguageId = "csharp",
        Confidence = 90,
        RawText = "boom",
        FirstLineSequence = 1,
        ExceptionType = "System.InvalidOperationException",
        Message = "boom",
        Frames = [new ErrorFrame { Order = 0, File = file, Line = 10, RawLine = "   at X() in " + file }],
    };

    // ------------------------------------------------------------------ precedence

    [Fact]
    public void WhatTheUserTypedWinsOverEverythingElse()
    {
        var chosen = MakeProject("Chosen");
        var other = MakeProject("Other", Path.Combine("src", "Thing.cs"));

        var result = SourceRootResolver.Resolve(
            chosen, ErrorWithFrameAt(Path.Combine(other, "src", "Thing.cs")), spec: null);

        Assert.Equal(SourceRootOrigin.UserSpecified, result.Origin);
        Assert.Equal(chosen, result.Path);
        Assert.False(result.NeedsConfirmation);
    }

    [Fact]
    public void RejectsAUserPathThatIsNotAFolder()
    {
        var result = SourceRootResolver.Resolve(
            Path.Combine(_temp, "does-not-exist"), error: null, spec: null);

        Assert.Equal(SourceRootOrigin.NotFound, result.Origin);
        Assert.Null(result.Path);
    }

    // ------------------------------------------------------------------ from the stack trace

    /// <summary>
    /// The highest-value strategy: anything built or run on this machine has real paths in its
    /// trace, so walking up to the project file finds the actual source rather than a guess.
    /// </summary>
    [Fact]
    public void WalksUpFromAStackTracePathToTheProjectFile()
    {
        var root = MakeProject("Walkable", Path.Combine("src", "deep", "Cart.cs"));

        var result = SourceRootResolver.Resolve(
            userSpecified: null, ErrorWithFrameAt(Path.Combine(root, "src", "deep", "Cart.cs")), spec: null);

        Assert.Equal(SourceRootOrigin.StackTracePaths, result.Origin);
        Assert.Equal(root, result.Path);
        Assert.False(result.NeedsConfirmation);
    }

    /// <summary>
    /// A loose script with no manifest above it. Its own folder is the only defensible answer -
    /// but it is narrower than a project root, so it asks before being used.
    /// </summary>
    [Fact]
    public void FallsBackToTheCrashingFilesOwnFolderWhenThereIsNoProjectFile()
    {
        var loose = Path.Combine(_temp, "loose");
        Directory.CreateDirectory(loose);
        var script = Path.Combine(loose, "run.py");
        File.WriteAllText(script, "print('hi')");

        var result = SourceRootResolver.Resolve(userSpecified: null, ErrorWithFrameAt(script), spec: null);

        Assert.Equal(loose, result.Path);
        Assert.True(result.NeedsConfirmation, "a folder with no project marker is a weaker answer and must be confirmed");
    }

    /// <summary>
    /// A trace from another machine names files that do not exist here. Resolving to something
    /// anyway would point FixFinder's writes at a folder that has nothing to do with the crash.
    /// </summary>
    [Fact]
    public void IgnoresStackTracePathsThatDoNotExistOnThisMachine()
    {
        var result = SourceRootResolver.Resolve(
            userSpecified: null,
            ErrorWithFrameAt(@"D:\build-agent\work\3\s\src\Cart.cs"),
            spec: null);

        Assert.Equal(SourceRootOrigin.NotFound, result.Origin);
    }

    [Fact]
    public void ReturnsNotFoundWhenThereIsNothingToGoOn()
    {
        var result = SourceRootResolver.Resolve(userSpecified: null, error: null, spec: null);

        Assert.Equal(SourceRootOrigin.NotFound, result.Origin);
        Assert.Contains("Browse", result.Explanation);
    }

    /// <summary>
    /// A crash inside an installed package must not root the tool in that package.
    /// </summary>
    /// <remarks>
    /// The most dangerous wrong answer this class can give. The innermost frame of a crash
    /// inside a library lives in site-packages or node_modules, and rooting there would bound
    /// every file FixFinder may write to the inside of an installed dependency - which the next
    /// install overwrites, and where the change did not belong in the first place.
    /// </remarks>
    [Fact]
    public void AFrameInsideAnInstalledPackageIsNeverUsedAsTheSourceRoot()
    {
        var app = MakeProject("MyApp", Path.Combine("src", "main.py"));

        var vendored = Path.Combine(_temp, "env", "Lib", "site-packages", "requests", "sessions.py");
        Directory.CreateDirectory(Path.GetDirectoryName(vendored)!);
        File.WriteAllText(vendored, "# the library");

        // Innermost first, exactly as a parsed traceback orders them.
        var error = new ParsedError
        {
            LanguageId = "python",
            Confidence = 90,
            RawText = "boom",
            FirstLineSequence = 1,
            ExceptionType = "requests.exceptions.InvalidSchema",
            Message = "No connection adapters were found",
            Frames =
            [
                new ErrorFrame { Order = 0, File = vendored, Line = 881, RawLine = vendored },
                new ErrorFrame { Order = 1, File = Path.Combine(app, "src", "main.py"), Line = 12, RawLine = "main" },
            ],
        };

        var result = SourceRootResolver.Resolve(userSpecified: null, error, spec: null);

        Assert.Equal(app, result.Path);
        Assert.DoesNotContain("site-packages", result.Path!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnErrorEntirelyInsideAPackageFindsNoRootRatherThanTheWrongOne()
    {
        var vendored = Path.Combine(_temp, "env", "Lib", "site-packages", "requests", "sessions.py");
        Directory.CreateDirectory(Path.GetDirectoryName(vendored)!);
        File.WriteAllText(vendored, "# the library");

        var result = SourceRootResolver.Resolve(
            userSpecified: null, ErrorWithFrameAt(vendored), spec: null);

        Assert.Equal(SourceRootOrigin.NotFound, result.Origin);
    }

    // ------------------------------------------------------------------ the sibling guess

    /// <summary>
    /// The "-broken next to the real thing" convention. Useful, but it is a guess from a folder
    /// name and nothing more - so it must never be applied without being shown to the user.
    /// </summary>
    [Fact]
    public void SiblingFolderGuessAlwaysAsksBeforeItIsUsed()
    {
        var real = MakeProject("Widget");
        var broken = Path.Combine(_temp, "Widget-broken");
        Directory.CreateDirectory(broken);
        var dll = Path.Combine(broken, "Widget.dll");
        File.WriteAllText(dll, "not really a dll");

        var spec = new TargetSpec { ExecutablePath = dll, WorkingDirectory = broken, LaunchViaDotnet = true };

        var result = SourceRootResolver.Resolve(userSpecified: null, error: null, spec);

        Assert.Equal(SourceRootOrigin.SiblingHeuristic, result.Origin);
        Assert.Equal(real, result.Path);
        Assert.True(result.NeedsConfirmation, "a guess from a folder name must never be applied silently");
    }

    [Fact]
    public void DoesNotGuessASiblingThatIsNotAProject()
    {
        var broken = Path.Combine(_temp, "Orphan-broken");
        Directory.CreateDirectory(broken);
        Directory.CreateDirectory(Path.Combine(_temp, "Orphan"));   // exists, but has no project marker
        var dll = Path.Combine(broken, "Orphan.dll");
        File.WriteAllText(dll, "not really a dll");

        var result = SourceRootResolver.Resolve(
            userSpecified: null, error: null,
            new TargetSpec { ExecutablePath = dll, WorkingDirectory = broken });

        Assert.Equal(SourceRootOrigin.NotFound, result.Origin);
    }

    // ------------------------------------------------------------------ portable PDB

    /// <summary>
    /// Reads the PDB of a target built in this very repository, so the test exercises a real
    /// portable PDB rather than a hand-made one.
    /// </summary>
    [Fact]
    public void ReadsSourcePathsFromARealPortablePdb()
    {
        var assembly = FindCrashDotNetAssembly();
        if (assembly is null) return;   // not built in this checkout; nothing to assert against

        var documents = PortablePdbReader.ReadDocumentPaths(assembly);

        Assert.NotEmpty(documents);
        Assert.Contains(documents, d => d.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PdbReaderReturnsEmptyRatherThanThrowingWhenThereIsNoPdb()
    {
        var fake = Path.Combine(_temp, "NoPdb.dll");
        File.WriteAllText(fake, "not really a dll");

        Assert.Empty(PortablePdbReader.ReadDocumentPaths(fake));
        Assert.Null(PortablePdbReader.FindCommonRoot(fake));
    }

    [Fact]
    public void PdbReaderReturnsEmptyForAFileThatIsNotAPdbAtAll()
    {
        var fake = Path.Combine(_temp, "Garbage.dll");
        File.WriteAllText(fake, "not really a dll");
        File.WriteAllText(Path.Combine(_temp, "Garbage.pdb"), "definitely not a portable pdb");

        Assert.Empty(PortablePdbReader.ReadDocumentPaths(fake));
    }

    /// <summary>Walks up from the test binaries to the CrashDotNet target, if it has been built.</summary>
    private static string? FindCrashDotNetAssembly()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && directory.Name != "FixFinder") directory = directory.Parent;
        if (directory is null) return null;

        var assembly = Path.Combine(
            directory.FullName, "TestTargets", "CrashDotNet", "bin", "Debug", "net8.0", "CrashDotNet.dll");

        return File.Exists(assembly) ? assembly : null;
    }
}
