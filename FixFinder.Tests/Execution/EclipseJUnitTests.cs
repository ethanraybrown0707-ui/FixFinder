using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Execution.Libraries;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Covers the JUnit that comes with Eclipse, which an Eclipse project's .classpath takes as a container rather than as jars
/// of its own: found among the bundles an Eclipse on this computer uses, and its tests run with it.
/// </summary>
public class EclipseJUnitTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        return path;
    }

    private string Folder(string relative) => Path.GetFullPath(Path.Combine(_temp.Path, relative));

    private async Task<CheckReport> CheckAsync(string file)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);
        output.WriteLine($"how: {launch.Explanation}");

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Java }.CheckAsync(launch);

        output.WriteLine($"syntax: {report.SyntaxSummary}");
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings) output.WriteLine($"[{finding.Severity}] {finding.Location}: {finding.Title}");
        return report;
    }

    private const string Calculator = "public class Calculator {\n    public int divide(int dividend, int divisor) {\n        return dividend / divisor;\n    }\n}\n";

    /// <summary>An Eclipse project's .classpath, as Eclipse writes one for a project given its JUnit.</summary>
    private void EclipseProject(string folder, string junitVersion) => Write(Path.Combine(folder, ".classpath"), $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <classpath>
        	<classpathentry kind="con" path="org.eclipse.jdt.launching.JRE_CONTAINER"/>
        	<classpathentry kind="src" path="src"/>
        	<classpathentry kind="con" path="org.eclipse.jdt.junit.JUNIT_CONTAINER/{junitVersion}"/>
        	<classpathentry kind="output" path="bin"/>
        </classpath>
        """);

    /// <summary>
    /// A stand-in home folder with the Eclipse installer's layout: jars of the bundles named - bundle_version.jar - in the
    /// shared pool, and an Eclipse whose bundles.info lists them there.
    /// </summary>
    private string HomeWith(params string[] bundles)
    {
        var home = Folder("home");
        var pool = Path.Combine(home, ".p2", "pool", "plugins");
        var listed = new List<string> { "#version=1" };

        foreach (var bundle in bundles)
        {
            var jar = Write(Path.Combine(pool, bundle + ".jar"), "");
            var (name, version) = (bundle[..bundle.IndexOf('_')], bundle[(bundle.IndexOf('_') + 1)..]);
            listed.Add($"{name},{version},file:/{jar.Replace('\\', '/')},4,false");
        }

        Write(Path.Combine(home, "eclipse", "java-2026-06", "eclipse", "configuration", "org.eclipse.equinox.simpleconfigurator", "bundles.info"),
            string.Join("\n", listed) + "\n");
        return home;
    }

    private static readonly string[] BundlesOfTwoJUnits =
    [
        "org.junit_4.13.2.v20240929-1000", "org.hamcrest_3.0.0", "org.opentest4j_1.3.0", "org.apiguardian.api_1.1.2",
        "junit-jupiter-api_5.14.4", "junit-jupiter-engine_5.14.4", "junit-jupiter-params_5.14.4", "junit-jupiter-migrationsupport_5.14.4",
        "junit-platform-commons_1.14.4", "junit-platform-engine_1.14.4", "junit-platform-launcher_1.14.4", "junit-platform-runner_1.14.4",
        "junit-platform-suite-api_1.14.4", "junit-platform-suite-engine_1.14.4", "junit-platform-suite-commons_1.14.4",
        "junit-jupiter-api_6.1.3", "junit-jupiter-engine_6.1.3", "junit-jupiter-params_6.1.3",
        "junit-platform-commons_6.1.3", "junit-platform-engine_6.1.3", "junit-platform-launcher_6.1.3",
        "junit-platform-suite-api_6.1.3", "junit-platform-suite-engine_6.1.3",
    ];

    /// <summary>Bundles by name and version, as their jars are named without .jar - a version's own dots kept.</summary>
    private static string[] Names(IEnumerable<string> jars) =>
        [.. jars.Select(Path.GetFileName).Select(name => name!.EndsWith(".jar", StringComparison.Ordinal) ? name[..^4] : name).Order(StringComparer.Ordinal)];

    [Fact]
    public void EachJUnitContainerIsMadeOfTheBundlesEclipseMakesItOf()
    {
        var home = HomeWith(BundlesOfTwoJUnits);

        using (EclipseJUnit.LookingIn(home))
        {
            // JUnit 5's jars are 5.x and its platform's 1.x - not JUnit 6's, which the same Eclipse has beside them.
            Assert.Equal(
                Names(["junit-jupiter-api_5.14.4", "junit-jupiter-engine_5.14.4", "junit-jupiter-migrationsupport_5.14.4", "junit-jupiter-params_5.14.4",
                       "junit-platform-commons_1.14.4", "junit-platform-engine_1.14.4", "junit-platform-launcher_1.14.4", "junit-platform-runner_1.14.4",
                       "junit-platform-suite-api_1.14.4", "junit-platform-suite-engine_1.14.4", "junit-platform-suite-commons_1.14.4",
                       "org.opentest4j_1.3.0", "org.apiguardian.api_1.1.2", "org.hamcrest_3.0.0"]),
                Names(EclipseJUnit.For("5")!.Jars));

            Assert.Equal(Names(["org.junit_4.13.2.v20240929-1000", "org.hamcrest_3.0.0"]), Names(EclipseJUnit.For("4")!.Jars));

            Assert.Equal(
                Names(["junit-jupiter-api_6.1.3", "junit-jupiter-engine_6.1.3", "junit-jupiter-params_6.1.3", "junit-platform-commons_6.1.3",
                       "junit-platform-engine_6.1.3", "junit-platform-launcher_6.1.3", "junit-platform-suite-api_6.1.3",
                       "junit-platform-suite-engine_6.1.3", "org.opentest4j_1.3.0", "org.apiguardian.api_1.1.2", "org.hamcrest_3.0.0"]),
                Names(EclipseJUnit.For("6")!.Jars));

            Assert.StartsWith("the Eclipse in ", EclipseJUnit.For("5")!.From, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheSharedPoolIsLookedInWhenNoEclipseHereListsJUnitAmongItsBundles()
    {
        // The pool keeps what every Eclipse installed from it used: two Hamcrests, of which the later is taken.
        var home = HomeWith("org.junit_4.13.2.v20240929-1000", "org.hamcrest_2.2.0", "org.hamcrest_3.0.0");

        // An Eclipse installed without the Java tools lists bundles of its own, and no JUnit among them.
        Write(Path.Combine(home, "eclipse", "java-2026-06", "eclipse", "configuration", "org.eclipse.equinox.simpleconfigurator", "bundles.info"),
            "#version=1\norg.eclipse.platform,4.40.0.v20260601-0600,plugins/org.eclipse.platform_4.40.0.v20260601-0600.jar,4,false\n");

        using (EclipseJUnit.LookingIn(home))
        {
            var found = EclipseJUnit.For("4");

            Assert.Equal(Names(["org.junit_4.13.2.v20240929-1000", "org.hamcrest_3.0.0"]), Names(found!.Jars));
            Assert.StartsWith("the bundles the Eclipse installer keeps in ", found.From, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnEclipseUnpackedFromAZipListsItsBundlesInItsOwnPluginsFolder()
    {
        // Unpacked rather than installed: its bundles.info names each bundle relative to the Eclipse's own folder.
        var eclipse = Folder(@"home\eclipse");
        var junit = Write(@"home\eclipse\plugins\org.junit_4.13.2.v20240929-1000.jar", "");
        var hamcrest = Write(@"home\eclipse\plugins\org.hamcrest_3.0.0.jar", "");
        Write(@"home\eclipse\configuration\org.eclipse.equinox.simpleconfigurator\bundles.info",
            "#version=1\norg.junit,4.13.2.v20240929-1000,plugins/org.junit_4.13.2.v20240929-1000.jar,4,false\n" +
            "org.hamcrest,3.0.0,plugins/org.hamcrest_3.0.0.jar,4,false\n");

        using (EclipseJUnit.LookingIn(Folder("home")))
        {
            var found = EclipseJUnit.For("4");

            Assert.Equal([junit, hamcrest], found!.Jars.ToArray(), StringComparer.OrdinalIgnoreCase);
            Assert.Equal($"the Eclipse in {eclipse}", found.From);
        }
    }

    [Fact]
    public void AProjectTakingEclipsesJUnitHasItsJarsAsItsTestLibraries()
    {
        EclipseProject("coursework", "5");
        var home = HomeWith(BundlesOfTwoJUnits);

        using (EclipseJUnit.LookingIn(home))
        {
            var libraries = IdeLibraries.Read(Folder("coursework"), new MavenRepository(Folder("repository")));

            Assert.Empty(libraries.Missing);
            Assert.Contains(libraries.Test, jar => Path.GetFileName(jar) == "junit-jupiter-api_5.14.4.jar");
            Assert.Contains(libraries.Test, jar => Path.GetFileName(jar) == "junit-platform-launcher_1.14.4.jar");
        }

        // With no Eclipse that has it, JUnit 5 is named as missing, and where it was looked for is said.
        using (EclipseJUnit.LookingIn(Folder("empty-home")))
        {
            var missing = Assert.Single(IdeLibraries.Read(Folder("coursework"), new MavenRepository(Folder("repository"))).Missing);
            Assert.Equal("JUnit 5", missing.Name);
            Assert.Contains("no Eclipse on this computer has it", missing.Reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AClassOfJUnit5TestsInAnEclipseProjectIsRunWithTheJUnitOfTheEclipseHere()
    {
        // With the real JUnit of an Eclipse on this computer; where there is none, as on the CI runner, this returns early.
        if (Toolchains.FindJavac() is null || EclipseJUnit.For("5") is null) return;

        EclipseProject("shop", "5");
        Write(@"shop\src\Calculator.java", Calculator);
        var tests = Write(@"shop\src\CalculatorTest.java", """
            import static org.junit.jupiter.api.Assertions.assertEquals;

            import org.junit.jupiter.api.Test;

            class CalculatorTest {
                @Test
                void dividesEvenly() {
                    assertEquals(2, new Calculator().divide(4, 2));
                }

                @Test
                void dividesByZero() {
                    assertEquals(0, new Calculator().divide(4, 0));
                }
            }
            """);

        Assert.Contains("from Eclipse's .classpath, then running its tests with JUnit 5", TargetFactory.FromFile(tests).Explanation, StringComparison.Ordinal);

        var report = await CheckAsync(tests);

        Assert.Single(report.Findings, finding => finding.Title.StartsWith("Test dividesByZero stopped with ArithmeticException", StringComparison.Ordinal));
        Assert.Equal("It builds; 1 of 2 tests failed", report.SyntaxSummary);
    }

    [Fact]
    public async Task AClassOfJUnit4TestsInAnEclipseProjectIsRunWithTheJUnitOfTheEclipseHere()
    {
        if (Toolchains.FindJavac() is null || EclipseJUnit.For("4") is null) return;

        EclipseProject("library", "4");
        Write(@"library\src\Calculator.java", Calculator);
        var tests = Write(@"library\src\CalculatorTest.java", """
            import static org.junit.Assert.assertEquals;

            import org.junit.Test;

            public class CalculatorTest {
                @Test
                public void dividesEvenly() {
                    assertEquals(3, new Calculator().divide(4, 2));
                }
            }
            """);

        var report = await CheckAsync(tests);

        var failed = Assert.Single(report.Findings, finding => finding.Title.StartsWith("Test dividesEvenly failed", StringComparison.Ordinal));
        Assert.Contains("expected:<3> but was:<2>", failed.Explanation, StringComparison.Ordinal);
        Assert.Equal("It builds; 1 of 1 test failed", report.SyntaxSummary);
    }
}
