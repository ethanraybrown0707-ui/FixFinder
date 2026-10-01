using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Execution.Libraries;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Covers a Gradle build of several projects: the projects its settings include, found where Gradle finds them; what the
/// build gives each of them; and a program built with the code of the projects it uses - or, when one cannot be found,
/// that said, rather than what it would have given the program taken for a missing library.
/// </summary>
public class GradleBuildOfProjectsTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string relative, string content)
    {
        var path = Path.GetFullPath(Path.Combine(_temp.Path, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        return path;
    }

    private string Folder(string relative) => Path.GetFullPath(Path.Combine(_temp.Path, relative));

    private async Task<CheckReport> CheckAsync(string file, string? expected = null)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);
        output.WriteLine($"how: {launch.Explanation}");

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry())
        {
            Language = CodeLanguage.Java,
            Expected = expected is null ? null : ExpectedBehaviour.From([new ExpectedRun(null, expected)], ""),
        };

        var report = await checker.CheckAsync(launch);
        output.WriteLine($"syntax: {report.SyntaxSummary} | logic: {report.LogicSummary}");
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
            output.WriteLine($"[{finding.Severity}/{finding.Kind}] {finding.Location}: {finding.Title} | {finding.Verified.Summary}: {string.Join(" / ", finding.Verified.Steps.Select(step => step.Detail))}");

        return report;
    }

    private const string Greeter = """
        package com.example.core;

        public final class Greeter {
            private Greeter() {
            }

            public static String greet(String name) {
                return "Hello, " + name;
            }
        }
        """;

    [Fact]
    public void TheProjectsASettingsFileIncludesAreFoundWhereGradleFindsThem()
    {
        Write(@"shop\settings.gradle", """
            rootProject.name = 'shop'

            // include 'commented-out'
            include 'app', 'libs:core'
            include('reports')
            project(':reports').projectDir = file('modules/reports')
            includeBuild('build-logic')
            """);

        var build = GradleSettings.Of(Folder(@"shop\libs\core"));

        Assert.NotNull(build);
        Assert.Equal(Folder("shop"), build.RootFolder);

        // Including :libs:core includes :libs too, as in Gradle.
        Assert.Equal(
            [(":app", Folder(@"shop\app")), (":libs", Folder(@"shop\libs")), (":libs:core", Folder(@"shop\libs\core")), (":reports", Folder(@"shop\modules\reports"))],
            build.ProjectFolders.OrderBy(project => project.Key, StringComparer.Ordinal).Select(project => (project.Key, project.Value)).ToArray());
        Assert.Equal([Folder(@"shop\build-logic")], build.IncludedBuilds.ToArray());

        // The top folder is the build's own project; a folder the settings do not include is no project of the build.
        Assert.Equal(":", GradleSettings.Of(Folder("shop"))?.PathOf(Folder("shop")));
        Assert.Null(GradleSettings.Of(Folder(@"shop\scratch")));

        Write(@"kotlin\settings.gradle.kts", "include(\":app\", \":core\")\nproject(\":core\").projectDir = file(\"lib/core\")\n");
        Assert.Equal(Folder(@"kotlin\lib\core"), GradleSettings.Of(Folder(@"kotlin\app"))?.FolderOf(":core"));
    }

    [Theory]
    // A path with a colon first is from the top of the build; any other is below the project naming it, as Gradle reads project().
    [InlineData(":core", ":app", ":core")]
    [InlineData("core", ":", ":core")]
    [InlineData("core", ":app", ":app:core")]
    public void AProjectNamedInABuildFileIsFoundFromWhereGradleLooks(string written, string namedFrom, string expected) =>
        Assert.Equal(expected, GradleSettings.Build.PathNamed(written, namedFrom));

    [Fact]
    public void AProjectOfABuildIsReadWithWhatTheBuildGivesIt()
    {
        Write(@"shop\settings.gradle", "include 'app', 'core', 'testing'\n");
        Write(@"shop\gradle.properties", "commonsVersion=3.14.0\n");
        Write(@"shop\gradle\libs.versions.toml", """
            [libraries]
            gson = { module = "com.google.code.gson:gson", version = "2.11.0" }
            """);
        var top = Write(@"shop\build.gradle", """
            ext {
                guavaVersion = '33.0.0-jre'
            }

            dependencies {
                implementation 'top:own:1.0'
            }

            allprojects {
                dependencies {
                    implementation 'every:project:1.0'
                }
            }

            subprojects {
                dependencies {
                    testImplementation 'org.junit.jupiter:junit-jupiter:5.10.2'
                    implementation files('libs/local.jar')
                }
            }

            project(':app') {
                dependencies {
                    implementation 'only:app:1.0'
                }
            }

            project(':core') {
                dependencies {
                    implementation 'only:core:1.0'
                }
            }

            configure(subprojects.findAll { it.name != 'testing' }) {
                dependencies {
                    implementation 'chosen:some:1.0'
                }
            }

            configure(subprojects) {
                dependencies {
                    implementation 'configured:below:1.0'
                }
            }
            """);
        var app = Write(@"shop\app\build.gradle", """
            plugins {
                id 'application'
            }

            dependencies {
                implementation project(':core')
                implementation "com.google.guava:guava:$guavaVersion"
                implementation "org.apache.commons:commons-lang3:$commonsVersion"
                implementation libs.gson
                implementation files("$rootDir/shared/shared.jar")
                testImplementation project(':testing')
            }
            """);
        var local = Write(@"shop\app\libs\local.jar", "");
        var shared = Write(@"shop\shared\shared.jar", "");

        var declared = GradleBuild.ReadProject(Folder(@"shop\app"), GradleSettings.Of(Folder(@"shop\app")));

        // Values, the version catalog and what is given below come from the top folder; its own dependencies { } is its own.
        Assert.Equal(
            ["com.google.guava:guava:33.0.0-jre:", "org.apache.commons:commons-lang3:3.14.0:", "com.google.code.gson:gson:2.11.0:",
             "every:project:1.0:", "org.junit.jupiter:junit-jupiter:5.10.2:test", "only:app:1.0:", "configured:below:1.0:"],
            declared.Dependencies.Select(dependency => $"{dependency.Group}:{dependency.Artifact}:{dependency.Version}:{dependency.Scope}").ToArray());
        Assert.Equal([(":core", false), (":testing", true)], declared.Projects.ToArray());

        // files() in subprojects { } is the folder of the project it is for, as in Gradle; $rootDir is the top folder.
        Assert.Equal([shared, local], declared.Files.Select(file => file.Jar).ToArray(), StringComparer.OrdinalIgnoreCase);

        // Which projects configure(subprojects.findAll { ... }) picks is worked out as Gradle runs, so its lines are named as not read.
        var unread = Assert.Single(declared.NotRead);
        Assert.Equal(("implementation 'chosen:some:1.0'", top), (unread.Text, unread.File));
        Assert.Equal([app, top], declared.ReadFrom.ToArray());
    }

    [Fact]
    public void AnApplicationLaidOutAsGradleInitLaysItOutHasItsLibrariesFromTheCatalogInTheTopFolder()
    {
        // gradle init writes the version catalog beside app, not in it, and app's build file names its libraries from it.
        Write(@"tutorial\settings.gradle.kts", "rootProject.name = \"tutorial\"\ninclude(\"app\")\n");
        Write(@"tutorial\gradle\libs.versions.toml", """
            [versions]
            guava = "33.0.0-jre"
            junit-jupiter = "5.10.2"

            [libraries]
            guava = { module = "com.google.guava:guava", version.ref = "guava" }
            junit-jupiter = { module = "org.junit.jupiter:junit-jupiter", version.ref = "junit-jupiter" }
            """);
        Write(@"tutorial\app\build.gradle.kts", """
            plugins {
                application
            }

            dependencies {
                testImplementation(libs.junit.jupiter)
                testRuntimeOnly("org.junit.platform:junit-platform-launcher")
                implementation(libs.guava)
            }
            """);

        var declared = GradleBuild.ReadProject(Folder(@"tutorial\app"), GradleSettings.Of(Folder(@"tutorial\app")));

        Assert.Equal(
            ["org.junit.jupiter:junit-jupiter:5.10.2:test", "org.junit.platform:junit-platform-launcher::test", "com.google.guava:guava:33.0.0-jre:"],
            declared.Dependencies.Select(dependency => $"{dependency.Group}:{dependency.Artifact}:{dependency.Version}:{dependency.Scope}").ToArray());
        Assert.Empty(declared.NotRead);
    }

    [Fact]
    public void SpringBootsVersionIsTakenFromTheTopOfTheBuildForAProjectThatAppliesIt()
    {
        Write(@"shop\settings.gradle", "include 'web', 'model'\n");
        Write(@"shop\build.gradle", "plugins {\n    id 'org.springframework.boot' version '3.2.0' apply false\n}\n");
        Write(@"shop\web\build.gradle", "plugins {\n    id 'org.springframework.boot'\n}\n\ndependencies {\n    implementation 'org.springframework.boot:spring-boot-starter-web'\n}\n");
        Write(@"shop\model\build.gradle", "plugins {\n    id 'java-library'\n}\n");

        var settings = GradleSettings.Of(Folder(@"shop\web"));
        var bootDependencies = new LibraryName("org.springframework.boot", "spring-boot-dependencies", "3.2.0");

        Assert.Contains(bootDependencies, GradleBuild.ReadProject(Folder(@"shop\web"), settings).Platforms);
        Assert.DoesNotContain(bootDependencies, GradleBuild.ReadProject(Folder(@"shop\model"), settings).Platforms);
    }

    [Fact]
    public void AConventionPluginOfTheBuildIsReadForTheProjectsThatApplyIt()
    {
        Write(@"shop\settings.gradle", "include('app')\n");
        Write(@"shop\buildSrc\src\main\groovy\shop.java-conventions.gradle", """
            plugins {
                id 'java'
            }

            dependencies {
                testImplementation 'org.junit.jupiter:junit-jupiter:5.10.2'
            }
            """);

        // A convention plugin applying another, as an application's often applies the one every project has.
        Write(@"shop\buildSrc\src\main\groovy\shop.application-conventions.gradle", "plugins {\n    id 'shop.java-conventions'\n    id 'application'\n}\n");
        Write(@"shop\app\build.gradle", """
            plugins {
                id 'shop.application-conventions'
            }

            dependencies {
                implementation 'org.apache.commons:commons-text:1.11.0'
            }
            """);

        var declared = GradleBuild.ReadProject(Folder(@"shop\app"), GradleSettings.Of(Folder(@"shop\app")));

        Assert.Equal(["org.apache.commons:commons-text:", "org.junit.jupiter:junit-jupiter:test"],
            declared.Dependencies.Select(dependency => $"{dependency.Group}:{dependency.Artifact}:{dependency.Scope}").ToArray());
    }

    /// <summary>Puts an empty jar of a library in a Gradle cache, laid out as Gradle lays out what it downloads.</summary>
    private static string Cached(string cache, string group, string artifact, string version)
    {
        var folder = Path.Combine(cache, group, artifact, version, "0123abcd");
        Directory.CreateDirectory(folder);

        var jar = Path.Combine(folder, $"{artifact}-{version}.jar");
        File.WriteAllBytes(jar, []);
        return jar;
    }

    [Fact]
    public void AProgramIsGivenTheCodeAndLibrariesOfTheProjectsItUses()
    {
        var cache = Folder("gradle-cache");
        var jar = Cached(cache, "com.example", "greeting", "1.0");
        Cached(cache, "com.example", "checking", "1.0");

        Write(@"shop\settings.gradle", "include 'app', 'core', 'util', 'testing'\n");
        Write(@"shop\app\build.gradle", "dependencies {\n    implementation project(':core')\n}\n");
        Write(@"shop\core\build.gradle", """
            dependencies {
                implementation 'com.example:greeting:1.0'
                implementation project(':util')
                testImplementation 'com.example:checking:1.0'
                testImplementation project(':testing')
            }
            """);

        // util has no build file of its own: the settings including it make it a project.
        Write(@"shop\util\src\main\java\util\Strings.java", "package util;\n\npublic class Strings {\n}\n");
        Write(@"shop\testing\src\main\java\testing\Checks.java", "package testing;\n\npublic class Checks {\n}\n");
        Write(@"shop\core\src\main\java\core\Greeter.java", "package core;\n\npublic class Greeter {\n}\n");
        Write(@"shop\core\src\main\resources\greeting.txt", "Hello\n");
        var app = Write(@"shop\app\src\main\java\app\App.java", "package app;\n\npublic class App {\n}\n");

        using var stores = JavaLibraries.UsingStores(new LibraryStores([new GradleCache(cache)]));
        var libraries = JavaLibraries.For(app);

        // What core uses for its own code comes with it; what it uses for its tests does not.
        Assert.Equal([":core", ":util"], libraries.ProjectsUsed.ToArray());
        Assert.Equal([Folder(@"shop\app\src\main\java"), Folder(@"shop\core\src\main\java"), Folder(@"shop\util\src\main\java")],
            libraries.SourceRoots.ToArray(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal([Folder(@"shop\core\src\main\resources")], libraries.Resources.ToArray(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal([jar], libraries.ClassPath.ToArray(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(@"app\build.gradle and core\build.gradle", libraries.DeclaredIn);
        Assert.Empty(libraries.MissingProjects);

        Assert.Equal(Folder(@"shop\util"), JavaLibraries.ProjectOf(Folder(@"shop\util\src\main\java")));
    }

    [Fact]
    public void ALibraryAddedToAProjectUsedIsSeenTheNextTimeTheProgramIsChecked()
    {
        var cache = Folder("gradle-cache");
        var jar = Cached(cache, "com.example", "greeting", "1.0");

        Write(@"shop\settings.gradle", "include 'app', 'core'\n");
        Write(@"shop\app\build.gradle", "dependencies {\n    implementation project(':core')\n}\n");
        var core = Write(@"shop\core\build.gradle", "plugins {\n    id 'java-library'\n}\n");
        var app = Write(@"shop\app\src\main\java\app\App.java", "package app;\n\npublic class App {\n}\n");

        using var stores = JavaLibraries.UsingStores(new LibraryStores([new GradleCache(cache)]));
        Assert.Empty(JavaLibraries.For(app).ClassPath);

        // The libraries of a project are remembered until a file they were read from changes - core's build file among them.
        File.WriteAllText(core, "dependencies {\n    implementation 'com.example:greeting:1.0'\n}\n");
        File.SetLastWriteTimeUtc(core, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal([jar], JavaLibraries.For(app).ClassPath.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AProjectTheBuildDoesNotHaveIsNamedWithWhy()
    {
        Write(@"shop\settings.gradle", "include 'app', 'reports'\n");
        Write(@"shop\app\build.gradle", "dependencies {\n    implementation project(':core')\n    implementation project(':reports')\n}\n");
        var app = Write(@"shop\app\src\main\java\app\App.java", "package app;\n\npublic class App {\n}\n");

        Assert.Equal(
            [":core (FixFinder did not find it among the projects settings.gradle includes)", ":reports (settings.gradle includes it, in reports, but that folder is not there)"],
            JavaLibraries.For(app).MissingProjects.Select(missing => missing.ToString()).ToArray());

        // With no settings file at all, nothing says where a project is.
        Write(@"alone\build.gradle", "dependencies {\n    implementation project(':core')\n}\n");
        var alone = Write(@"alone\src\main\java\Main.java", "public class Main {\n}\n");

        Assert.Equal("FixFinder found no settings.gradle in the project's folder or above it to say which projects its build has",
            Assert.Single(JavaLibraries.For(alone).MissingProjects).Reason);
    }

    [Fact]
    public async Task AProjectIsBuiltAndRunWithTheCodeOfTheProjectsItUses()
    {
        if (Toolchains.FindJavac() is null) return;

        Write(@"shop\settings.gradle", "rootProject.name = 'shop'\ninclude 'app', 'core'\n");
        Write(@"shop\app\build.gradle", "plugins {\n    id 'application'\n}\n\ndependencies {\n    implementation project(':core')\n}\n");
        Write(@"shop\core\build.gradle", "plugins {\n    id 'java-library'\n}\n");
        Write(@"shop\core\src\main\java\com\example\core\Greeter.java", Greeter);
        var app = Write(@"shop\app\src\main\java\app\App.java", """
            package app;

            import com.example.core.Greeter;

            public class App {
                public static void main(String[] args) {
                    System.out.println(Greeter.greet("Sam"));
                }
            }
            """);

        Assert.Contains("with the code of the project :core of its Gradle build", TargetFactory.FromFile(app).Explanation);

        var report = await CheckAsync(app, "Hello, Sam");

        Assert.DoesNotContain(report.Findings, finding => finding.Severity == Severity.Error);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    [Fact]
    public async Task AMistakeInAProjectThatUsesAnotherIsFixedOnACopyBuiltWithThatProjectsCode()
    {
        if (Toolchains.FindJavac() is null) return;

        Write(@"shop\settings.gradle", "include 'app', 'core'\n");
        Write(@"shop\app\build.gradle", "dependencies {\n    implementation project(':core')\n}\n");
        Write(@"shop\core\src\main\java\com\example\core\Greeter.java", Greeter);
        var app = Write(@"shop\app\src\main\java\app\App.java", """
            package app;

            import com.example.core.Greeter;

            public class App {
                public static void main(String[] args) {
                    String[] names = {"Sam", "Kim"};
                    System.out.println(Greeter.greet(names[0]) + " of " + names.length());
                }
            }
            """);

        var report = await CheckAsync(app);

        // The copy the change is made in holds app alone: core's code is still found where it is, and the copy builds and runs.
        var wrongLength = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Equal(8, wrongLength.Line);
        Assert.True(wrongLength.Verified.IsVerified, string.Join(" / ", wrongLength.Verified.Steps.Select(step => step.Detail)));
    }

    /// <summary>A build of an app and a core that is a module of its own, the app itself a module or not.</summary>
    private string ModularBuild(bool appIsAModule)
    {
        Write(@"shop\settings.gradle", "include 'app', 'core'\n");
        Write(@"shop\app\build.gradle", "dependencies {\n    implementation project(':core')\n}\n");
        Write(@"shop\core\src\main\java\module-info.java", "module com.example.core {\n    exports com.example.core;\n}\n");
        Write(@"shop\core\src\main\java\com\example\core\Greeter.java", Greeter);
        if (appIsAModule) Write(@"shop\app\src\main\java\module-info.java", "module app {\n    requires com.example.core;\n}\n");

        return Write(@"shop\app\src\main\java\app\App.java", """
            package app;

            import com.example.core.Greeter;

            public class App {
                public static void main(String[] args) {
                    System.out.println(Greeter.greet("Sam"));
                }
            }
            """);
    }

    [Fact]
    public async Task AProjectThatIsNotAModuleIsBuiltWithTheCodeOfOneThatIs()
    {
        if (Toolchains.FindJavac() is null) return;

        var app = ModularBuild(appIsAModule: false);
        Assert.Contains("as part of the module com.example.core the project :core declares", TargetFactory.FromFile(app).Explanation);

        var report = await CheckAsync(app, "Hello, Sam");

        Assert.DoesNotContain(report.Findings, finding => finding.Severity == Severity.Error);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    [Fact]
    public async Task AModuleThatRequiresAnotherProjectsModuleIsSaidToBeBeyondFixFinderNotReportedAsAMistake()
    {
        if (Toolchains.FindJavac() is null) return;

        // javac, given every project's code as one module, says it cannot find com.example.core - which Gradle builds as a module of its own.
        var report = await CheckAsync(ModularBuild(appIsAModule: true));

        Assert.DoesNotContain(report.Findings, finding => finding.Severity == Severity.Error);
        Assert.Equal("It needs another project's module, which FixFinder cannot build with it, so it was not built", report.SyntaxSummary);
        Assert.Contains(report.Notes, note => note.StartsWith(
            "javac could not find the module com.example.core, which the project :core of its Gradle build declares.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AProjectTheBuildDoesNotHaveIsNamedRatherThanTakenForAMissingLibrary()
    {
        if (Toolchains.FindJavac() is null) return;

        Write(@"shop\settings.gradle", "include 'app'\n");
        Write(@"shop\app\build.gradle", "dependencies {\n    implementation project(':core')\n}\n");
        var app = Write(@"shop\app\src\main\java\app\App.java", """
            package app;

            import com.example.core.Greeter;

            public class App {
                public static void main(String[] args) {
                    System.out.println(Greeter.greet("Sam"));
                }
            }
            """);

        var report = await CheckAsync(app);

        Assert.DoesNotContain(report.Findings, finding => finding.Severity == Severity.Error);
        Assert.Equal("It uses code FixFinder could not find, so it was not built", report.SyntaxSummary);

        var note = Assert.Single(report.Notes, note => note.StartsWith("com.example.core is not part of Java or of the code FixFinder found for this program", StringComparison.Ordinal));
        Assert.Contains("The Gradle build names the project :core, but FixFinder did not find it among the projects settings.gradle includes, " +
                        "so its code was not there to build the program with.", note);
        Assert.DoesNotContain("None of the libraries", note);
    }

    [Fact]
    public async Task AClassThatAMissingProjectWouldHaveGivenTheProgramsOwnPackageIsAnErrorSaidToBeUncertain()
    {
        if (Toolchains.FindJavac() is null) return;

        // core's Greeter would be in the package app too, so nothing is imported, and javac says only that it cannot find it.
        Write(@"shop\settings.gradle", "include 'app'\n");
        Write(@"shop\app\build.gradle", "dependencies {\n    implementation project(':core')\n}\n");
        var app = Write(@"shop\app\src\main\java\app\App.java", """
            package app;

            public class App {
                public static void main(String[] args) {
                    System.out.println(Greeter.greet("Sam"));
                }
            }
            """);

        var report = await CheckAsync(app);

        Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Contains(report.Notes, note => note ==
            "The Gradle build names the project :core, but FixFinder did not find it among the projects settings.gradle includes, so its code was not " +
            "there to build the program with. So an error about a class or method from that code may not be a mistake in the code.");
    }
}
