using FixFinder.Core.Patching;

namespace FixFinder.Tests;

/// <summary>
/// Finding the package a crash went through, and writing no further than its own folder.
/// </summary>
/// <remarks>
/// The containment rule is the whole reason this is safe to offer at all. Rooting at
/// <c>site-packages</c> would put every installed library in range of one patch; rooting at
/// <c>site-packages/requests</c> means a refusal to write outside the root is a refusal to touch
/// anything but the library named in the crash.
/// </remarks>
public class InstalledPackageTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------------------ finding it

    [Theory]
    [InlineData(@"C:\py\Lib\site-packages\requests\sessions.py", "requests")]
    [InlineData(@"C:\py\Lib\site-packages\urllib3\connectionpool.py", "urllib3")]
    [InlineData("/usr/lib/python3/dist-packages/yaml/loader.py", "yaml")]
    [InlineData(@"C:\app\node_modules\express\lib\router\index.js", "express")]
    [InlineData(@"C:\app\bower_components\jquery\dist\jquery.js", "jquery")]
    public void ThePackageFolderIsFoundFromAFileInsideIt(string file, string expected)
    {
        var package = InstalledPackages.For(file);

        Assert.NotNull(package);
        Assert.Equal(expected, package!.Name);

        // The root is the package's own folder, which is what containment is measured against.
        Assert.EndsWith(expected, package.Root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A scoped npm package is two levels down, and rooting at the scope would be too much reach.
    /// </summary>
    /// <remarks>
    /// <c>node_modules/@scope</c> holds every package sharing that scope, so a patch rooted there
    /// could walk into a sibling it has nothing to do with. Caught by a test expectation that was
    /// written before the code and turned out to be describing the safer behaviour.
    /// </remarks>
    [Fact]
    public void AScopedPackageIsRootedAtThePackageNotTheScope()
    {
        var package = InstalledPackages.For(@"C:\app\node_modules\@scope\thing\index.js");

        Assert.NotNull(package);
        Assert.Equal("@scope/thing", package!.Name);
        Assert.EndsWith(@"@scope\thing", package.Root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A vendor folder is deliberately not supported, because its depth varies by ecosystem.
    /// </summary>
    /// <remarks>
    /// Go writes <c>vendor/github.com/org/repo</c> and Composer writes <c>vendor/org/package</c>.
    /// Guessing one level down would root a Go dependency at <c>vendor/github.com</c> and put
    /// every package from that host inside one patch's reach, so nothing is offered rather than
    /// too much.
    /// </remarks>
    [Theory]
    [InlineData(@"C:\go\vendor\github.com\pkg\errors.go")]
    [InlineData("/srv/app/vendor/monolog/monolog/src/Logger.php")]
    public void AVendorFolderIsNotGuessedAt(string file)
    {
        Assert.Null(InstalledPackages.For(file));
    }

    /// <summary>
    /// The language's own installation is not a dependency, and is never offered.
    /// </summary>
    /// <remarks>
    /// A published issue asks you to upgrade Python or .NET, never to hand-edit the standard
    /// library on your own machine. Treating those as patchable packages would turn a fix for
    /// somebody's library into an edit of the runtime.
    /// </remarks>
    [Theory]
    [InlineData(@"C:\Python313\Lib\json\decoder.py")]
    [InlineData("/usr/lib/python3.13/json/decoder.py")]
    [InlineData(@"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.0\System.Private.CoreLib.dll")]
    public void TheRuntimeItselfIsNotAPackage(string file)
    {
        Assert.Null(InstalledPackages.For(file));
    }

    [Fact]
    public void CodeInTheProjectIsNotAPackage()
    {
        Assert.Null(InstalledPackages.For(@"C:\work\shop\cart.py"));
    }

    /// <summary>A loose module has no folder of its own, so there is nothing to contain a patch to.</summary>
    [Fact]
    public void ALooseModuleBesideThePackagesIsRefused()
    {
        Assert.Null(InstalledPackages.For(@"C:\py\Lib\site-packages\six.py"));
    }

    /// <summary>The innermost frame wins, so the library that actually threw is the one offered.</summary>
    [Fact]
    public void TheNearestPackageInTheStackIsTheOneChosen()
    {
        var package = InstalledPackages.From([
            @"C:\py\Lib\site-packages\requests\adapters.py",
            @"C:\py\Lib\site-packages\urllib3\poolmanager.py",
            @"C:\work\shop\cart.py",
        ]);

        Assert.NotNull(package);
        Assert.Equal("requests", package!.Name);
    }

    // ------------------------------------------------------------------ writing into it

    /// <summary>
    /// An upstream fix to a library lands, where before it resolved to nothing at all.
    /// </summary>
    /// <remarks>
    /// The case the whole thing exists for: the bug is in the library, somebody has already fixed
    /// it upstream, and their patch changes the library's own files - which are on this disk,
    /// just not in the project. Before this, that patch was refused with "no file called
    /// adapters.py exists anywhere under the source root", which was true and useless.
    /// </remarks>
    [Fact]
    public void APatchForTheLibraryAppliesInsideThePackage()
    {
        var package = Path.Combine(_temp.Path, "site-packages", "requests");
        Directory.CreateDirectory(package);

        var adapters = Path.Combine(package, "adapters.py");
        File.WriteAllText(adapters, "import os\nraise InvalidSchema(msg)\nimport sys\n");

        var found = InstalledPackages.For(adapters);

        Assert.NotNull(found);
        Assert.Equal("requests", found!.Name);

        var patch = UnifiedDiffParser.Parse("""
            --- a/requests/adapters.py
            +++ b/requests/adapters.py
            @@ -1,3 +1,3 @@
             import os
            -raise InvalidSchema(msg)
            +raise InvalidSchema(msg) from None
             import sys
            """);

        var plan = new PatchApplier().Plan(
            patch, new SourcePathMapper(found.Root, [adapters]), [adapters]);

        Assert.True(plan.CanApply, plan.Explanation);
    }

    /// <summary>
    /// Rooted at the package, not at site-packages: a neighbour is still out of reach.
    /// </summary>
    /// <remarks>
    /// The check that makes this worth offering at all. A patch naming another installed library
    /// is refused by the same structural rule that refuses <c>../../../Windows</c>, because the
    /// root it is measured against is the one package the crash actually came through.
    /// </remarks>
    [Fact]
    public void APatchCannotReachAnotherInstalledPackage()
    {
        var packages = Path.Combine(_temp.Path, "site-packages");
        Directory.CreateDirectory(Path.Combine(packages, "requests"));
        Directory.CreateDirectory(Path.Combine(packages, "urllib3"));

        var mine = Path.Combine(packages, "requests", "adapters.py");
        File.WriteAllText(mine, "import os\n");

        var neighbour = Path.Combine(packages, "urllib3", "poolmanager.py");
        var before = "import os\nold\nimport sys\n";
        File.WriteAllText(neighbour, before);

        var found = InstalledPackages.For(mine)!;

        var patch = UnifiedDiffParser.Parse("""
            --- a/../urllib3/poolmanager.py
            +++ b/../urllib3/poolmanager.py
            @@ -1,3 +1,3 @@
             import os
            -old
            +new
             import sys
            """);

        var plan = new PatchApplier().Plan(
            patch, new SourcePathMapper(found.Root, [mine, neighbour]), [mine, neighbour]);

        Assert.False(plan.CanApply);
        Assert.Equal(before, File.ReadAllText(neighbour).ReplaceLineEndings("\n"));
    }
}
