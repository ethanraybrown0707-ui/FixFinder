using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// Finding the JDKs on a computer, in folders laid out as each installer, IDE and tool lays them out - made here, as a test
/// cannot install a JDK. A folder counts as a JDK by its release file and the javac in its bin, as a real JDK's do.
/// </summary>
public class JdksTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string Executable(string tool) => OperatingSystem.IsWindows() ? tool + ".exe" : tool;

    /// <summary>A JDK's folder as an installer leaves one: bin\javac and bin\java, and a release file saying its version.</summary>
    internal static string FakeJdk(string home, string version, bool withJavac = true)
    {
        Directory.CreateDirectory(Path.Combine(home, "bin"));
        File.WriteAllText(Path.Combine(home, "bin", Executable("java")), "");
        if (withJavac) File.WriteAllText(Path.Combine(home, "bin", Executable("javac")), "");
        File.WriteAllText(Path.Combine(home, "release"), $"IMPLEMENTOR=\"Test\"\nJAVA_VERSION=\"{version}\"\nOS_NAME=\"Windows\"\n");
        return home;
    }

    /// <summary>The places of a computer made in a folder: its home, its Program Files, PATH's javac and JAVA_HOME.</summary>
    internal static Jdks.Places PlacesIn(string root, string? jdkOnPath = null, string? javaHome = null) => new(
        Path.Combine(root, "home"),
        [Path.Combine(root, "Program Files")],
        name => name == "JAVA_HOME" ? javaHome : null,
        tool => jdkOnPath is not null && tool is "javac" or "java" ? Path.Combine(jdkOnPath, "bin", Executable(tool)) : null,
        ReadRegistry: false);

    private string ProgramFiles(string vendor, string folder) => Path.Combine(_temp.Path, "Program Files", vendor, folder);

    [Theory]
    [InlineData("1.8.0_402", 8)]
    [InlineData("11.0.22", 11)]
    [InlineData("21.0.12.1", 21)]
    [InlineData("25.0.4.1", 25)]
    [InlineData("27", 27)]
    [InlineData("27-ea", 27)]
    public void AVersionIsReadAsTheFeatureReleaseJavacCountsIn(string written, int feature)
    {
        Assert.Equal(feature, Jdks.FeatureOf(written));
    }

    /// <summary>By name jdk-8 sorts after jdk-27, and jdk1.8.0 after both: the version in each one's release file decides.</summary>
    [Fact]
    public void JdksAreOrderedByTheirVersionNotByTheNamesOfTheirFolders()
    {
        FakeJdk(ProgramFiles("Java", "jdk1.8.0_402"), "1.8.0_402");
        FakeJdk(ProgramFiles("Java", "jdk-27"), "27.0.1");
        FakeJdk(ProgramFiles("Eclipse Adoptium", "jdk-8.0.402.6-hotspot"), "1.8.0_402");
        FakeJdk(ProgramFiles("Microsoft", "jdk-21.0.12.101-hotspot"), "21.0.12.1");

        using var places = Jdks.LookingIn(PlacesIn(_temp.Path));

        Assert.Equal([27, 21, 8, 8], Jdks.Installed.Select(jdk => jdk.Version).ToArray());
        Assert.Equal(27, Jdks.Default!.Version);
    }

    [Fact]
    public void AFolderWithoutJavacIsARuntimeAndNotCounted()
    {
        FakeJdk(ProgramFiles("Java", "jre-21"), "21.0.2", withJavac: false);
        var jdk = FakeJdk(ProgramFiles("Java", "jdk-17"), "17.0.10");

        using var places = Jdks.LookingIn(PlacesIn(_temp.Path));

        Assert.Equal([jdk], Jdks.Installed.Select(found => found.Home).ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The javac a terminal would run is the default, even when a newer JDK is installed beside it.</summary>
    [Fact]
    public void TheJdkOnPathIsTheDefaultEvenWhenANewerOneIsInstalled()
    {
        var older = FakeJdk(ProgramFiles("Microsoft", "jdk-21.0.12.101-hotspot"), "21.0.12.1");
        FakeJdk(ProgramFiles("Eclipse Adoptium", "jdk-25.0.4+7"), "25.0.4");

        using var places = Jdks.LookingIn(PlacesIn(_temp.Path, jdkOnPath: older));

        Assert.Equal(older, Jdks.Default!.Home, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("on PATH", Jdks.Default.FoundIn);
        Assert.Equal(25, Jdks.Installed[0].Version);
    }

    [Fact]
    public void WithNothingOnPathJavaHomeIsTheDefault()
    {
        var named = FakeJdk(Path.Combine(_temp.Path, "tools", "jdk-17"), "17.0.10");
        FakeJdk(ProgramFiles("Java", "jdk-25"), "25.0.1");

        using var places = Jdks.LookingIn(PlacesIn(_temp.Path, javaHome: named + Path.DirectorySeparatorChar));

        Assert.Equal(named, Jdks.Default!.Home, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("named by JAVA_HOME", Jdks.Default.FoundIn);
    }

    /// <summary>
    /// The JDKs an IDE or a tool downloaded for itself: IntelliJ's in .jdks, Gradle's toolchains' in .gradle\jdks - one
    /// folder further down - Scoop's current version of each app, and the Java runtime the Eclipse installer brings, whose
    /// full runtime holds javac.
    /// </summary>
    [Fact]
    public void JdksThatIdesAndToolsDownloadedAreFound()
    {
        var home = Path.Combine(_temp.Path, "home");
        FakeJdk(Path.Combine(home, ".jdks", "openjdk-26"), "26.0.1");
        FakeJdk(Path.Combine(home, ".gradle", "jdks", "eclipse_adoptium-17-amd64-windows.2", "jdk-17.0.10+7"), "17.0.10");
        FakeJdk(Path.Combine(home, "scoop", "apps", "temurin22-jdk", "current"), "22.0.2");
        FakeJdk(Path.Combine(home, ".p2", "pool", "plugins", "org.eclipse.justj.openjdk.hotspot.jre.full.win32.x86_64_25.0.4.v20260826-0822", "jre"), "25.0.4.1");

        using var places = Jdks.LookingIn(PlacesIn(_temp.Path));

        Assert.Equal(
            ["26 downloaded by IntelliJ", "25 Eclipse's own Java", "22 installed by Scoop", "17 downloaded by Gradle"],
            Jdks.Installed.Select(jdk => $"{jdk.Version} {jdk.FoundIn}").ToArray());
    }

    /// <summary>javac and java come from the one JDK, so a class javac compiles is always run by a java that can read it.</summary>
    [Fact]
    public void JavacAndJavaComeFromTheJdkInUse()
    {
        var usual = FakeJdk(ProgramFiles("Microsoft", "jdk-21"), "21.0.12.1");
        var chosen = FakeJdk(ProgramFiles("Java", "jdk-25"), "25.0.1");

        using var places = Jdks.LookingIn(PlacesIn(_temp.Path, jdkOnPath: usual));

        Assert.Equal(Path.Combine(usual, "bin", Executable("javac")), Toolchains.FindJavac()!.Program, StringComparer.OrdinalIgnoreCase);

        using (Jdks.Using(Jdks.Installed.Single(jdk => jdk.Version == 25)))
        {
            Assert.Equal(Path.Combine(chosen, "bin", Executable("javac")), Toolchains.FindJavac()!.Program, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(Path.Combine(chosen, "bin", Executable("java")), Toolchains.FindJava()!.Program, StringComparer.OrdinalIgnoreCase);
        }

        Assert.Equal(Path.Combine(usual, "bin", Executable("java")), Toolchains.FindJava()!.Program, StringComparer.OrdinalIgnoreCase);
    }
}
