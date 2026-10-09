using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// Where Scala CLI and the versions of Scala it can build with are found, and which version a program is built with and
/// why - in folders laid out as Scala CLI's installer and Coursier's cache lay them out, as a test cannot install either.
/// </summary>
public class ScalaSetupTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string LocalAppData => Path.Combine(_temp.Path, "local");

    private string ProgramFiles => Path.Combine(_temp.Path, "Program Files");

    private string Write(string relative, string text = "")
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>Scala CLI as its Windows installer puts it.</summary>
    private string InstallScalaCli() => Write(Path.Combine("Program Files", "scala-cli-x86_64-pc-win32", "scala-cli.exe"));

    /// <summary>A version of Scala in Coursier's cache: its compiler's jar and its library's, where Scala CLI keeps them.</summary>
    private void Cache(string version, bool withLibrary = true, string? cache = null)
    {
        var scalaLang = Path.Combine(cache ?? Path.Combine(LocalAppData, "Coursier", "cache", "v1"), "https", "repo1.maven.org", "maven2", "org", "scala-lang");
        var (compiler, library) = version.StartsWith('3') ? ("scala3-compiler_3", "scala3-library_3") : ("scala-compiler", "scala-library");

        Write(Path.Combine(scalaLang, compiler, version, $"{compiler}-{version}.jar"));
        if (withLibrary) Write(Path.Combine(scalaLang, library, version, $"{library}-{version}.jar"));
    }

    private IDisposable Places(string? onPath = null, string? coursierCache = null) =>
        ScalaToolchains.LookingIn(new ScalaToolchains.Places(
            LocalAppData, Path.Combine(_temp.Path, "home"), [ProgramFiles], coursierCache, name => name == "scala-cli" ? onPath : null));

    [Fact]
    public void ScalaCliIsFoundOnPathFirstThenWhereItsInstallerPutsIt()
    {
        var installed = InstallScalaCli();
        var onPath = Write(Path.Combine("bin", "scala-cli.exe"));

        using (Places())
            Assert.Equal((installed, "in Program Files"), ScalaToolchains.Cli is { } cli ? (cli.Program, cli.FoundIn) : default);

        using (Places(onPath))
            Assert.Equal((onPath, "on PATH"), ScalaToolchains.Cli is { } cli ? (cli.Program, cli.FoundIn) : default);
    }

    [Fact]
    public void WithoutScalaCliThereIsNone()
    {
        using var looking = Places();
        Assert.Null(ScalaToolchains.Cli);
    }

    [Fact]
    public void AVersionIsCachedOnlyWithItsCompilerAndItsLibraryAndReleasesComeNewestFirst()
    {
        Cache("3.8.4");
        Cache("2.13.18");
        Cache("3.3.1", withLibrary: false);
        Cache("3.8.0-RC1");

        using var looking = Places();

        Assert.Equal(new[] { "3.8.4", "2.13.18" }, ScalaToolchains.CachedVersions.Select(version => version.Version));
        Assert.Equal(new[] { "3", "2.13" }, ScalaToolchains.CachedVersions.Select(version => version.Series));
    }

    [Fact]
    public void TheCacheCoursierCacheNamesIsReadInPlaceOfCoursiersOwn()
    {
        var elsewhere = Path.Combine(_temp.Path, "elsewhere");
        Cache("3.8.4");
        Cache("3.4.2", cache: elsewhere);

        using var looking = Places(coursierCache: elsewhere);

        Assert.Equal("3.4.2", Assert.Single(ScalaToolchains.CachedVersions).Version);
    }

    [Theory]
    [InlineData("scalaVersion := \"3.3.1\"\n", "3.3.1")]
    [InlineData("ThisBuild / scalaVersion := \"2.13.12\"\n", "2.13.12")]
    [InlineData("scalaVersion in ThisBuild := \"2.12.18\"\n", "2.12.18")]
    [InlineData("val scala3Version = \"3.4.0\"\n\nlazy val root = project\n  .in(file(\".\"))\n  .settings(\n    scalaVersion := scala3Version\n  )\n", "3.4.0")]
    [InlineData("name := \"marks\"\n", null)]
    public void ABuildSbtsScalaVersionIsReadWrittenOutOrAsAValItGives(string buildSbt, string? expected) =>
        Assert.Equal(expected, ScalaSetup.ScalaVersionIn(buildSbt));

    [Theory]
    [InlineData("3.3.1", "3")]
    [InlineData("3", "3")]
    [InlineData("2.13.12", "2.13")]
    [InlineData("2.12.18", "2.12")]
    public void AVersionsSeriesIsEveryScala3OrOneScala2(string version, string series) =>
        Assert.Equal(series, ScalaSetup.SeriesOf(version));

    [Fact]
    public void AProgramThatAsksForNoVersionIsBuiltWithTheNewestScala3()
    {
        InstallScalaCli();
        Cache("3.8.4");
        Cache("3.3.1");
        Cache("2.13.18");
        var program = Write(Path.Combine("marks", "Marks.scala"), "object Marks {\n  def main(args: Array[String]): Unit = println(1)\n}\n");

        using var looking = Places();
        var (setup, problem) = ScalaSetup.For(program);

        Assert.Null(problem);
        Assert.Equal("3.8.4", setup!.Version.Version);
        Assert.False(setup.IsAskedFor);
        Assert.Contains("the newest Scala 3 here", setup.Explained);
    }

    [Fact]
    public void AVersionAUsingDirectiveAsksForIsUsedWhenItIsCached()
    {
        InstallScalaCli();
        Cache("3.8.4");
        Cache("3.3.1");
        var program = Write(Path.Combine("marks", "Marks.scala"), "//> using scala 3.3.1\nobject Marks {\n  def main(args: Array[String]): Unit = println(1)\n}\n");

        using var looking = Places();
        var setup = ScalaSetup.For(program).Setup!;

        Assert.Equal("3.3.1", setup.Version.Version);
        Assert.True(setup.IsAskedFor);
        Assert.Contains("which the using directive in Marks.scala asks for", setup.Explained);
    }

    [Fact]
    public void AVersionNotInTheCacheIsBuiltWithTheNewestOfItsSeriesAndSaysSo()
    {
        InstallScalaCli();
        Cache("3.8.4");
        Write(Path.Combine("marks", "build.sbt"), "scalaVersion := \"3.3.1\"\n");
        var program = Write(Path.Combine("marks", "Marks.scala"), "object Marks {\n  def main(args: Array[String]): Unit = println(1)\n}\n");

        using var looking = Places();
        var setup = ScalaSetup.For(program).Setup!;

        Assert.Equal("3.8.4", setup.Version.Version);
        Assert.Contains("its build.sbt asks for Scala 3.3.1, which is not in Scala CLI's cache on this computer, so it is built with the newest Scala 3 here", setup.Explained);
    }

    [Fact]
    public void AVersionOfASeriesNotInTheCacheIsNotBuiltAndWhatToDoIsSaid()
    {
        InstallScalaCli();
        Cache("3.8.4");
        Cache("2.13.18");
        Write(Path.Combine("marks", "build.sbt"), "ThisBuild / scalaVersion := \"2.12.18\"\n");
        var program = Write(Path.Combine("marks", "Marks.scala"), "object Marks extends App {\n  println(1)\n}\n");

        using var looking = Places();
        var (setup, problem) = ScalaSetup.For(program);

        Assert.Null(setup);
        Assert.Contains("needs Scala 2.12.18, as its build.sbt asks for it, and no Scala 2.12 is in Scala CLI's cache on this computer - only 3.8.4, 2.13.18", problem);
        Assert.Contains("FixFinder never downloads anything", problem);
    }

    [Fact]
    public void WithNothingInTheCacheOrNoScalaCliTheProgramIsNotBuiltAndWhyIsSaid()
    {
        var program = Write(Path.Combine("marks", "Marks.scala"), "object Marks extends App\n");

        using (Places())
            Assert.Contains("Scala CLI was not found", ScalaSetup.For(program).Problem);

        InstallScalaCli();
        using (Places())
            Assert.Contains("no version of Scala is in Scala CLI's cache on this computer", ScalaSetup.For(program).Problem);
    }

    [Fact]
    public void ScalaCliIsRunOfflineWithItsBuildKeptOutOfTheProgramsFolderAndEachCompilersOwnOptions()
    {
        var cli = new ScalaToolchains.ScalaCli(@"C:\Program Files\scala-cli-x86_64-pc-win32\scala-cli.exe", "in Program Files");
        ScalaSetup.Setup Built(string version) => new(cli, ScalaToolchains.Read(version)!, @"C:\jdk", "", IsAskedFor: false);

        var scala3 = ScalaSetup.Arguments("run", Built("3.8.4"), @"C:\build\Marks", [@"C:\marks\Marks.scala"], "Marks");
        Assert.Equal(
            @"--power run --server=false --offline --suppress-experimental-feature-warning --workspace ""C:\build\Marks"" --java-home ""C:\jdk"" " +
            @"--scala 3.8.4 -O -color:never -O -deprecation -O -Wunused:imports,privates,locals --main-class Marks ""C:\marks\Marks.scala""",
            scala3);

        Assert.DoesNotContain("-color", ScalaSetup.Arguments("compile", Built("2.13.18"), @"C:\build", [@"C:\a.scala"], null));
        Assert.EndsWith(@"--scala 2.12.18 -O -deprecation ""C:\a.scala""", ScalaSetup.Arguments("compile", Built("2.12.18"), @"C:\build", [@"C:\a.scala"], null));
    }

    [Fact]
    public void AProgramsArgumentsComeAfterTheDashesScalaCliRunNeedsAndNotAfterItsCompile()
    {
        TargetSpec Spec(string arguments) => new() { ExecutablePath = @"C:\Program Files\scala-cli-x86_64-pc-win32\scala-cli.exe", Arguments = arguments, WorkingDirectory = @"C:\marks" };

        Assert.Equal("--power run \"Marks.scala\" -- Ada 36", Spec("--power run \"Marks.scala\"").WithArguments("Ada 36").Arguments);
        Assert.Equal("--power compile \"Marks.scala\" Ada", Spec("--power compile \"Marks.scala\"").WithArguments("Ada").Arguments);
    }
}
