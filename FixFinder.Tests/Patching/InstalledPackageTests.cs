using FixFinder.Core.Patching;

namespace FixFinder.Tests;

/// <summary>Finding the package a crash went through, and writing no further than its own folder.</summary>
public class InstalledPackageTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

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

        Assert.EndsWith(expected, package.Root, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AScopedPackageIsRootedAtThePackageNotTheScope()
    {
        var package = InstalledPackages.For(@"C:\app\node_modules\@scope\thing\index.js");

        Assert.NotNull(package);
        Assert.Equal("@scope/thing", package!.Name);
        Assert.EndsWith(@"@scope\thing", package.Root, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\go\vendor\github.com\pkg\errors.go")]
    [InlineData("/srv/app/vendor/monolog/monolog/src/Logger.php")]
    public void AVendorFolderWithNoManifestIsNotGuessedAt(string file)
    {
        Assert.Null(InstalledPackages.For(file));
    }

    [Theory]
    [InlineData(@"C:\Users\me\go\pkg\mod\github.com\pkg\errors@v0.9.1\errors.go", "errors")]
    [InlineData(@"C:\Users\me\go\pkg\mod\gopkg.in\yaml.v2@v2.4.0\yaml.go", "yaml.v2")]
    [InlineData("/home/me/go/pkg/mod/golang.org/x/text@v0.3.7/language/parse.go", "text")]
    public void AGoModuleIsFoundByItsVersionMarker(string file, string expected)
    {
        var package = InstalledPackages.For(file);

        Assert.NotNull(package);
        Assert.Equal(expected, package!.Name);

        Assert.Contains('@', Path.GetFileName(package.Root));
    }

    [Fact]
    public void TheGoCacheSaysWhyItCannotBeWrittenTo()
    {
        var package = InstalledPackages.For(
            @"C:\Users\me\go\pkg\mod\github.com\pkg\errors@v0.9.1\errors.go");

        Assert.NotNull(package!.Caveat);
        Assert.Contains("read-only", package.Caveat!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("go mod vendor", package.Caveat!, StringComparison.Ordinal);
    }

    [Fact]
    public void AVendoredGoModuleUsesTheManifestForItsDepth()
    {
        var vendor = Path.Combine(_temp.Path, "vendor");
        Directory.CreateDirectory(Path.Combine(vendor, "github.com", "pkg", "errors"));

        File.WriteAllText(Path.Combine(vendor, "modules.txt"),
            "# github.com/pkg/errors v0.9.1\n## explicit\ngithub.com/pkg/errors\n");

        var file = Path.Combine(vendor, "github.com", "pkg", "errors", "errors.go");
        File.WriteAllText(file, "package errors\n");

        var package = InstalledPackages.For(file);

        Assert.NotNull(package);
        Assert.Equal("errors", package!.Name);
        Assert.Equal(Path.Combine(vendor, "github.com", "pkg", "errors"), package.Root);

        Assert.Null(package.Caveat);
    }

    [Fact]
    public void TheLongestMatchingModulePathWins()
    {
        var vendor = Path.Combine(_temp.Path, "vendor");
        Directory.CreateDirectory(Path.Combine(vendor, "github.com", "org", "repo", "sub"));

        File.WriteAllText(Path.Combine(vendor, "modules.txt"),
            "# github.com/org/repo v1.0.0\n# github.com/org/repo/sub v1.2.0\n");

        var file = Path.Combine(vendor, "github.com", "org", "repo", "sub", "thing.go");
        File.WriteAllText(file, "package sub\n");

        var package = InstalledPackages.For(file);

        Assert.Equal("sub", package!.Name);
        Assert.EndsWith(Path.Combine("repo", "sub"), package.Root, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\Users\me\.cargo\registry\src\index.crates.io-6f17d22bba15001f\serde-1.0.197\src\lib.rs", "serde")]
    [InlineData("/home/me/.cargo/registry/src/github.com-1ecc6299db9ec823/regex-1.10.2/src/lib.rs", "regex")]
    public void ACrateIsFoundUnderTheRegistrySource(string file, string expected)
    {
        var package = InstalledPackages.For(file);

        Assert.NotNull(package);
        Assert.Equal(expected, package!.Name);
    }

    [Theory]
    [InlineData("pin-project-1.1.3", "pin-project")]
    [InlineData("serde-1.0.197", "serde")]
    [InlineData("regex", "regex")]
    public void OnlyARealVersionIsTrimmedFromACrateName(string folder, string expected)
    {
        var package = InstalledPackages.For(
            $"/home/me/.cargo/registry/src/index-abc/{folder}/src/lib.rs");

        Assert.Equal(expected, package!.Name);
    }

    [Fact]
    public void TheCargoRegistrySaysThatAPatchWillNotSurvive()
    {
        var package = InstalledPackages.For(
            "/home/me/.cargo/registry/src/index-abc/serde-1.0.197/src/lib.rs");

        Assert.NotNull(package!.Caveat);
        Assert.Contains("cargo vendor", package.Caveat!, StringComparison.Ordinal);
    }

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

    [Fact]
    public void ALooseModuleBesideThePackagesIsRefused()
    {
        Assert.Null(InstalledPackages.For(@"C:\py\Lib\site-packages\six.py"));
    }

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

        var plan = new PatchPlanner().Plan(
            patch, new SourcePathMapper(found.Root, [adapters]), [adapters]);

        Assert.True(plan.CanApply, plan.Explanation);
    }

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

        var plan = new PatchPlanner().Plan(
            patch, new SourcePathMapper(found.Root, [mine, neighbour]), [mine, neighbour]);

        Assert.False(plan.CanApply);
        Assert.Equal(before, File.ReadAllText(neighbour).ReplaceLineEndings("\n"));
    }
}
