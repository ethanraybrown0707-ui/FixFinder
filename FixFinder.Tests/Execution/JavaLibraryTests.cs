using System.Diagnostics;
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
/// Covers finding the libraries a Java program uses the way its own tools do - a pom.xml, a Gradle build file, IntelliJ's,
/// Eclipse's and VS Code's settings, a lib folder - among what is already downloaded, and building and running the program
/// with them; and, when one is not there, saying so once, by name, instead of reporting javac's errors about it as
/// mistakes in the code.
/// </summary>
public class JavaLibraryTests(ITestOutputHelper output) : IDisposable
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

    /// <summary>A pom.xml for a library in a repository, naming what it depends on.</summary>
    private static string Pom(string group, string artifact, string version, string dependencies = "", string extra = "") => $"""
        <project xmlns="http://maven.apache.org/POM/4.0.0">
          <modelVersion>4.0.0</modelVersion>
          <groupId>{group}</groupId>
          <artifactId>{artifact}</artifactId>
          <version>{version}</version>
          {extra}
          <dependencies>{dependencies}</dependencies>
        </project>
        """;

    private static string Dependency(string group, string artifact, string? version = null, string scope = "", bool optional = false, string exclusions = "") =>
        $"<dependency><groupId>{group}</groupId><artifactId>{artifact}</artifactId>" +
        (version is null ? "" : $"<version>{version}</version>") +
        (scope.Length > 0 ? $"<scope>{scope}</scope>" : "") +
        (optional ? "<optional>true</optional>" : "") +
        (exclusions.Length > 0 ? $"<exclusions>{exclusions}</exclusions>" : "") + "</dependency>";

    /// <summary>Puts a library in a Maven-style repository: its pom.xml, and a jar - empty unless one is given.</summary>
    private string Publish(string repository, string group, string artifact, string version, string dependencies = "", string? jar = null, bool withJar = true, string extra = "")
    {
        var folder = Path.Combine([repository, .. group.Split('.'), artifact, version]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, $"{artifact}-{version}.pom"), Pom(group, artifact, version, dependencies, extra));

        var jarPath = Path.Combine(folder, $"{artifact}-{version}.jar");
        if (jar is not null) File.Copy(jar, jarPath, overwrite: true);
        else if (withJar) File.WriteAllBytes(jarPath, []);

        return jarPath;
    }

    /// <summary>A real jar holding one class, built with the JDK's own javac and jar, or null when there is no JDK here.</summary>
    private string? GreetingJar()
    {
        if (Toolchains.FindJavac() is not { } javac) return null;

        var jarTool = Path.Combine(Path.GetDirectoryName(javac.Program)!, "jar.exe");
        if (!File.Exists(jarTool)) return null;

        var source = Write(@"greeting-src\com\example\greeting\Greeter.java", """
            package com.example.greeting;

            public final class Greeter {
                private Greeter() {
                }

                public static String greet(String name) {
                    return "Hello, " + name;
                }
            }
            """);

        var classes = Folder("greeting-classes");
        var jar = Path.Combine(_temp.Path, "greeting-1.0.jar");

        Run(javac.Program, $"-d \"{classes}\" \"{source}\"");
        Run(jarTool, $"cf \"{jar}\" -C \"{classes}\" .");

        return File.Exists(jar) ? jar : null;
    }

    private static void Run(string program, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(program, arguments) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true })!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);
    }

    private const string UsesGreeter = """
        package app;

        import com.example.greeting.Greeter;

        public class App {
            public static void main(String[] args) {
                System.out.println(Greeter.greet("Sam"));
            }
        }
        """;

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
        foreach (var finding in report.Findings) output.WriteLine($"[{finding.Severity}] {finding.Title}");
        return report;
    }

    [Theory]
    [InlineData("1.10", "1.9", 1)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1.0-beta", "1.0", -1)]
    [InlineData("1.0-rc1", "1.0-beta2", 1)]
    [InlineData("2.11.0", "2.9.1", 1)]
    [InlineData("1.0-SNAPSHOT", "1.0", -1)]
    [InlineData("33.0.0-jre", "33.0.0-android", 1)]
    public void VersionsAreComparedAsMavenComparesThem(string left, string right, int sign) =>
        Assert.Equal(sign, Math.Sign(MavenVersion.Compare(left, right)));

    [Theory]
    [InlineData("[1.0,2.0)", "1.5")]
    [InlineData("[1.0,)", "2.0")]
    [InlineData("(,1.5]", "1.5")]
    [InlineData("[1.2]", null)]
    [InlineData("[1.0,1.5),[1.8,)", null)]
    public void ARangePicksTheHighestDownloadedVersionInIt(string range, string? chosen) =>
        Assert.Equal(chosen, MavenVersion.HighestIn(range, ["0.9", "1.0", "1.5", "2.0"]));

    [Fact]
    public void AProjectsLibrariesAreFollowedAsMavenFollowsThem()
    {
        var repository = Folder("repository");

        var needed = Publish(repository, "com.example", "core", "2.0", Dependency("com.example", "util", "1.1") + Dependency("com.example", "extra", "1.0", optional: true) + Dependency("com.example", "tool", "1.0", scope: "test"));
        var util = Publish(repository, "com.example", "util", "1.1", Dependency("com.example", "unwanted", "1.0"));
        Publish(repository, "com.example", "util", "1.0");
        Publish(repository, "com.example", "extra", "1.0");
        Publish(repository, "com.example", "tool", "1.0");
        Publish(repository, "com.example", "unwanted", "1.0");
        var junit = Publish(repository, "org.junit", "junit-api", "5.0");

        var pom = Write(@"project\pom.xml", Pom("uni", "coursework", "1.0",
            Dependency("com.example", "core", "2.0", exclusions: "<exclusion><groupId>com.example</groupId><artifactId>unwanted</artifactId></exclusion>") +
            Dependency("org.junit", "junit-api", "5.0", scope: "test") +
            Dependency("com.example", "missing", "3.0")));

        var resolved = new MavenResolver(new LibraryStores([new MavenRepository(repository)])).Resolve(pom);

        Assert.Equal([needed, util], resolved.Main.Order(StringComparer.Ordinal).ToArray(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal([junit], resolved.Test.ToArray(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(resolved.Missing, missing => missing.Name == "com.example:missing:3.0");
        Assert.DoesNotContain(resolved.Chosen, library => library.Artifact is "extra" or "tool" or "unwanted");
    }

    [Fact]
    public void TheNearestDeclarationOfALibraryWinsItsVersion()
    {
        var repository = Folder("repository");
        Publish(repository, "com.example", "core", "1.0", Dependency("com.example", "util", "1.0"));
        Publish(repository, "com.example", "util", "1.0");
        var chosen = Publish(repository, "com.example", "util", "2.0");

        var pom = Write(@"project\pom.xml", Pom("uni", "coursework", "1.0", Dependency("com.example", "core", "1.0") + Dependency("com.example", "util", "2.0")));
        var resolved = new MavenResolver(new LibraryStores([new MavenRepository(repository)])).Resolve(pom);

        Assert.Contains(chosen, resolved.Main, StringComparer.OrdinalIgnoreCase);
        Assert.Single(resolved.Chosen, library => library.Artifact == "util");
    }

    [Fact]
    public void AParentsPropertiesAndManagedVersionsAndAnImportedBomAreFollowed()
    {
        var repository = Folder("repository");
        var fromBom = Publish(repository, "org.junit.jupiter", "junit-jupiter", "5.10.2");
        Publish(repository, "org.junit", "junit-bom", "5.10.2", extra: """
            <packaging>pom</packaging>
            <dependencyManagement><dependencies>
              <dependency><groupId>org.junit.jupiter</groupId><artifactId>junit-jupiter</artifactId><version>5.10.2</version></dependency>
            </dependencies></dependencyManagement>
            """, withJar: false);
        var managed = Publish(repository, "com.google.code.gson", "gson", "2.11.0");

        Write(@"project\pom.xml", """
            <project xmlns="http://maven.apache.org/POM/4.0.0">
              <modelVersion>4.0.0</modelVersion>
              <groupId>uni</groupId>
              <artifactId>parent</artifactId>
              <version>1.0</version>
              <packaging>pom</packaging>
              <properties><gson.version>2.11.0</gson.version></properties>
              <dependencyManagement><dependencies>
                <dependency><groupId>com.google.code.gson</groupId><artifactId>gson</artifactId><version>${gson.version}</version></dependency>
                <dependency><groupId>org.junit</groupId><artifactId>junit-bom</artifactId><version>5.10.2</version><type>pom</type><scope>import</scope></dependency>
              </dependencies></dependencyManagement>
            </project>
            """);

        var module = Write(@"project\app\pom.xml", """
            <project xmlns="http://maven.apache.org/POM/4.0.0">
              <modelVersion>4.0.0</modelVersion>
              <parent><groupId>uni</groupId><artifactId>parent</artifactId><version>1.0</version></parent>
              <artifactId>app</artifactId>
              <dependencies>
                <dependency><groupId>com.google.code.gson</groupId><artifactId>gson</artifactId></dependency>
                <dependency><groupId>org.junit.jupiter</groupId><artifactId>junit-jupiter</artifactId><scope>test</scope></dependency>
              </dependencies>
            </project>
            """);

        var resolved = new MavenResolver(new LibraryStores([new MavenRepository(repository)])).Resolve(module);

        Assert.Equal([managed], resolved.Main.ToArray(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal([fromBom], resolved.Test.ToArray(), StringComparer.OrdinalIgnoreCase);
        Assert.Empty(resolved.Missing);
    }

    [Fact]
    public void AGradleBuildFileIsReadInEachOfItsUsualForms()
    {
        Write(@"project\gradle\libs.versions.toml", """
            [versions]
            junit = "5.10.2"

            [libraries]
            junit-jupiter = { module = "org.junit.jupiter:junit-jupiter", version.ref = "junit" }
            commons-lang = { group = "org.apache.commons", name = "commons-lang3", version = "3.14.0" }
            """);
        var jar = Write(@"project\libs\local.jar", "");

        var build = Write(@"project\build.gradle", """
            plugins {
                id 'java'
            }

            // implementation 'not:read:1.0' is a comment, and so is this
            def gsonVersion = '2.11.0'

            dependencies {
                implementation "com.google.code.gson:gson:$gsonVersion"
                implementation group: 'com.google.guava', name: 'guava', version: '33.0.0-jre'
                implementation(libs.commons.lang)
                implementation fileTree(dir: 'libs', include: ['*.jar'])
                testImplementation platform('org.junit:junit-bom:5.10.2')
                testImplementation libs.junit.jupiter
                testRuntimeOnly 'org.junit.platform:junit-platform-launcher'
                implementation project(':core')
            }
            """);

        var declared = GradleBuild.Read(build);

        Assert.Equal(
            ["com.google.code.gson:gson:2.11.0:", "com.google.guava:guava:33.0.0-jre:", "org.apache.commons:commons-lang3:3.14.0:",
             "org.junit.jupiter:junit-jupiter:5.10.2:test", "org.junit.platform:junit-platform-launcher::test"],
            declared.Dependencies.Select(dependency => $"{dependency.Group}:{dependency.Artifact}:{dependency.Version}:{dependency.Scope}").ToArray());
        Assert.Equal([new LibraryName("org.junit", "junit-bom", "5.10.2")], declared.Platforms.ToArray());
        Assert.Equal([Path.GetFullPath(jar)], declared.Files.Select(file => file.Jar).ToArray(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(["implementation project(':core')"], declared.NotRead.ToArray());
    }

    [Fact]
    public void AKotlinBuildFileIsReadToo()
    {
        var build = Write(@"project\build.gradle.kts", """
            plugins {
                java
                id("org.springframework.boot") version "3.2.0"
            }

            dependencies {
                implementation("org.springframework.boot:spring-boot-starter-web")
                testImplementation(platform("org.junit:junit-bom:5.10.0"))
                testImplementation("org.junit.jupiter:junit-jupiter")
            }
            """);

        var declared = GradleBuild.Read(build);

        Assert.Equal(["org.springframework.boot:spring-boot-starter-web", "org.junit.jupiter:junit-jupiter"],
            declared.Dependencies.Select(dependency => $"{dependency.Group}:{dependency.Artifact}").ToArray());
        Assert.Contains(new LibraryName("org.springframework.boot", "spring-boot-dependencies", "3.2.0"), declared.Platforms);
        Assert.Contains(new LibraryName("org.junit", "junit-bom", "5.10.0"), declared.Platforms);
    }

    [Fact]
    public void TheLibrariesIntelliJEclipseAndVsCodeRecordAreFound()
    {
        var repository = Folder("repository");
        var fromMaven = Publish(repository, "junit", "junit", "4.13.2");
        var projectJar = Write(@"intellij\third-party\gson.jar", "");

        Write(@"intellij\.idea\libraries\gson.xml", """
            <component name="libraryTable">
              <library name="gson">
                <CLASSES><root url="jar://$PROJECT_DIR$/third-party/gson.jar!/" /></CLASSES>
              </library>
            </component>
            """);
        Write(@"intellij\coursework.iml", """
            <module type="JAVA_MODULE" version="4">
              <component name="NewModuleRootManager">
                <content url="file://$MODULE_DIR$"><sourceFolder url="file://$MODULE_DIR$/src" isTestSource="false" /></content>
                <orderEntry type="library" name="gson" level="project" />
                <orderEntry type="module-library" scope="TEST">
                  <library><CLASSES><root url="jar://$MAVEN_REPOSITORY$/junit/junit/4.13.2/junit-4.13.2.jar!/" /></CLASSES></library>
                </orderEntry>
              </component>
            </module>
            """);

        var intellij = IdeLibraries.Read(Folder("intellij"), new MavenRepository(repository));
        Assert.Equal([Path.GetFullPath(projectJar)], intellij.Main.ToArray(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal([fromMaven], intellij.Test.ToArray(), StringComparer.OrdinalIgnoreCase);

        var eclipseJar = Write(@"eclipse\lib\csv.jar", "");
        Write(@"eclipse\.classpath", """
            <classpath>
              <classpathentry kind="src" path="src"/>
              <classpathentry kind="lib" path="lib/csv.jar"/>
              <classpathentry kind="con" path="org.eclipse.jdt.junit.JUNIT_CONTAINER/5"/>
            </classpath>
            """);

        var eclipse = IdeLibraries.Read(Folder("eclipse"), new MavenRepository(repository));
        Assert.Contains(Path.GetFullPath(eclipseJar), eclipse.Main, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(eclipse.Missing, missing => missing.Name == "JUnit 5");

        var vscodeJar = Write(@"vscode\jars-here\nested\json.jar", "");
        Write(@"vscode\.vscode\settings.json", """
            {
                // a comment, as VS Code allows
                "java.project.referencedLibraries": ["jars-here/**/*.jar"],
            }
            """);

        Assert.Equal([Path.GetFullPath(vscodeJar)], IdeLibraries.Read(Folder("vscode"), new MavenRepository(repository)).Main.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AProjectsFolderIsTheOneAnIdeTakesAndNeverTheUsersOwn()
    {
        Write(@"project\pom.xml", Pom("uni", "coursework", "1.0"));
        Write(@"project\src\main\java\app\App.java", UsesGreeter);

        Assert.Equal(Folder("project"), JavaLibraries.ProjectOf(Folder(@"project\src\main\java")));
        Assert.Null(JavaLibraries.ProjectOf(Folder("nothing-here")));
    }

    [Fact]
    public async Task AMavenProjectIsBuiltAndRunWithTheLibrariesItsPomNames()
    {
        if (GreetingJar() is not { } jar) return;

        var repository = Folder("repository");
        Publish(repository, "com.example", "greeting", "1.0", jar: jar);
        Write(@"project\pom.xml", Pom("uni", "coursework", "1.0", Dependency("com.example", "greeting", "1.0")));
        var app = Write(@"project\src\main\java\app\App.java", UsesGreeter);

        using var stores = JavaLibraries.UsingStores(new LibraryStores([new MavenRepository(repository)]));
        var report = await CheckAsync(app, "Hello, Sam");

        Assert.DoesNotContain(report.Findings, finding => finding.Severity == Severity.Error);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    [Fact]
    public async Task AGradleProjectIsBuiltAndRunWithTheLibrariesItsBuildFileNames()
    {
        if (GreetingJar() is not { } jar) return;

        var cache = Folder("gradle-cache");
        var folder = Path.Combine(cache, "com.example", "greeting", "1.0", "0123abcd");
        Directory.CreateDirectory(folder);
        File.Copy(jar, Path.Combine(folder, "greeting-1.0.jar"));

        Write(@"project\build.gradle", "plugins { id 'application' }\n\ndependencies {\n    implementation 'com.example:greeting:1.0'\n}\n");
        var app = Write(@"project\src\main\java\app\App.java", UsesGreeter);

        using var stores = JavaLibraries.UsingStores(new LibraryStores([new GradleCache(cache)]));
        var report = await CheckAsync(app, "Hello, Sam");

        Assert.DoesNotContain(report.Findings, finding => finding.Severity == Severity.Error);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    [Fact]
    public async Task AProgramWithItsJarsInALibFolderIsBuiltAndRunWithThem()
    {
        if (GreetingJar() is not { } jar) return;

        Directory.CreateDirectory(Folder(@"week5\lib"));
        File.Copy(jar, Path.Combine(Folder(@"week5\lib"), "greeting.jar"));
        var app = Write(@"week5\app\App.java", UsesGreeter);

        var report = await CheckAsync(app, "Hello, Sam");

        Assert.DoesNotContain(report.Findings, finding => finding.Severity == Severity.Error);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    [Fact]
    public async Task ALibraryThatIsNotHereIsNamedOnceAndNotReportedAsMistakesInTheCode()
    {
        if (Toolchains.FindJavac() is null) return;

        var test = Write(@"coursework\CalculatorTest.java", """
            import static org.junit.jupiter.api.Assertions.assertEquals;

            import org.junit.jupiter.api.Test;

            class CalculatorTest {
                @Test
                void addsTwoNumbers() {
                    assertEquals(4, 2 + 2);
                }
            }
            """);

        var report = await CheckAsync(test);

        Assert.DoesNotContain(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Contains(report.Notes, note => note.StartsWith("org.junit.jupiter.api is not part of Java or of this program: it comes from a library - JUnit 5.", StringComparison.Ordinal));
        Assert.Equal("It needs a library that is not on this computer, so it was not built", report.SyntaxSummary);
    }

    [Fact]
    public async Task AMistakeInTheCodeBesideAMissingLibraryIsStillReported()
    {
        if (Toolchains.FindJavac() is null) return;

        var app = Write(@"coursework\Report.java", """
            import com.google.gson.Gson;
            import java.utils.List;

            public class Report {
                public static void main(String[] args) {
                    Gson gson = new Gson();
                    int count = "three";
                    System.out.println(gson.toJson(count));
                }
            }
            """);

        var report = await CheckAsync(app);

        Assert.Contains(report.Notes, note => note.StartsWith("com.google.gson is not part of Java or of this program", StringComparison.Ordinal));
        Assert.Contains(report.Findings, finding => finding.Kind == FindingKind.Syntax && finding.Title.Contains("java.utils", StringComparison.Ordinal));
        Assert.Contains(report.Findings, finding => finding.Kind == FindingKind.Syntax && finding.Title.Contains("incompatible types", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AMistypedPackageOfTheProgramsOwnIsAMistakeNotAMissingLibrary()
    {
        if (Toolchains.FindJavac() is null) return;

        Write(@"coursework\src\uni\ds\Stack.java", "package uni.ds;\n\npublic class Stack {\n}\n");
        var app = Write(@"coursework\src\uni\app\Main.java", "package uni.app;\n\nimport uni.dss.Stack;\n\npublic class Main {\n    public static void main(String[] args) {\n        System.out.println(new Stack());\n    }\n}\n");

        var report = await CheckAsync(app);

        Assert.DoesNotContain(report.Notes, note => note.Contains("not part of Java or of this program", StringComparison.Ordinal));
        Assert.Contains(report.Findings, finding => finding.Kind == FindingKind.Syntax && finding.Title.Contains("uni.dss", StringComparison.Ordinal));
    }
}
