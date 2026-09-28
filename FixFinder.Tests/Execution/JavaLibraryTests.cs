using System.Diagnostics;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Engine;
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

    /// <summary>
    /// Two real jars built with the JDK: one holding an annotation processor that, for a class marked @MakeGreeting, writes
    /// gen.Greeting; and one holding only the annotation, as a library's annotations and its processor are often kept apart.
    /// </summary>
    private (string Processor, string Annotations)? ProcessorJars()
    {
        if (Toolchains.FindJavac() is not { } javac) return null;

        var jarTool = Path.Combine(Path.GetDirectoryName(javac.Program)!, "jar.exe");
        if (!File.Exists(jarTool)) return null;

        var annotation = Write(@"processor-src\gen\MakeGreeting.java", "package gen;\n\npublic @interface MakeGreeting {\n}\n");
        var processor = Write(@"processor-src\gen\GreetingProcessor.java", """
            package gen;

            import java.io.IOException;
            import java.io.Writer;
            import java.util.Set;
            import javax.annotation.processing.AbstractProcessor;
            import javax.annotation.processing.RoundEnvironment;
            import javax.annotation.processing.SupportedAnnotationTypes;
            import javax.lang.model.SourceVersion;
            import javax.lang.model.element.TypeElement;

            @SupportedAnnotationTypes("gen.MakeGreeting")
            public class GreetingProcessor extends AbstractProcessor {
                private boolean written;

                @Override
                public SourceVersion getSupportedSourceVersion() {
                    return SourceVersion.latestSupported();
                }

                @Override
                public boolean process(Set<? extends TypeElement> annotations, RoundEnvironment round) {
                    if (written || annotations.isEmpty()) {
                        return false;
                    }
                    written = true;
                    try (Writer out = processingEnv.getFiler().createSourceFile("gen.Greeting").openWriter()) {
                        out.write("package gen;\n\npublic final class Greeting {\n    public static String hello() {\n        return \"Hello from a processor\";\n    }\n}\n");
                    } catch (IOException problem) {
                        throw new IllegalStateException(problem);
                    }
                    return true;
                }
            }
            """);

        var classes = Folder("processor-classes");
        Run(javac.Program, $"-d \"{classes}\" \"{annotation}\" \"{processor}\"");
        Directory.CreateDirectory(Path.Combine(classes, "META-INF", "services"));
        File.WriteAllText(Path.Combine(classes, "META-INF", "services", "javax.annotation.processing.Processor"), "gen.GreetingProcessor\n");

        var processorJar = Path.Combine(_temp.Path, "greeting-processor.jar");
        var annotationsJar = Path.Combine(_temp.Path, "greeting-annotations.jar");
        Run(jarTool, $"cf \"{processorJar}\" -C \"{classes}\" .");
        Run(jarTool, $"cf \"{annotationsJar}\" -C \"{classes}\" gen/MakeGreeting.class");

        return File.Exists(processorJar) && File.Exists(annotationsJar) ? (processorJar, annotationsJar) : null;
    }

    private const string UsesAGeneratedGreeting = """
        package app;

        import gen.MakeGreeting;

        @MakeGreeting
        public class App {
            public static void main(String[] args) {
                System.out.println(gen.Greeting.hello());
            }
        }
        """;

    [Fact]
    public async Task AnAnnotationProcessorAmongTheLibrariesWritesItsCodeWhenTheProgramIsBuilt()
    {
        if (ProcessorJars() is not { } jars) return;

        Directory.CreateDirectory(Folder(@"generated\lib"));
        File.Copy(jars.Processor, Path.Combine(Folder(@"generated\lib"), "greeting-processor.jar"));
        var app = Write(@"generated\app\App.java", UsesAGeneratedGreeting);

        var launch = TargetFactory.FromFile(app);
        Assert.Contains("-processorpath", launch.Compile!.Arguments, StringComparison.Ordinal);
        Assert.Contains("-processorpath", FixFinder.Core.LocalFixes.CompileCheck.JavacArguments(app, app, Folder("fix-check")));

        var report = await CheckAsync(app, "Hello from a processor");

        Assert.DoesNotContain(report.Findings, finding => finding.Severity == Severity.Error);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    [Fact]
    public async Task AProcessorSeesAClassInAnotherFileOfTheProgramNotJustTheOneStartedFrom()
    {
        if (ProcessorJars() is not { } jars) return;

        // javac runs processors only on the files it is given by name - not on those it finds on the source path - so a
        // class marked in another file, as a Lombok @Data class usually is, needs its file named too.
        Directory.CreateDirectory(Folder(@"elsewhere\lib"));
        File.Copy(jars.Processor, Path.Combine(Folder(@"elsewhere\lib"), "greeting-processor.jar"));
        Write(@"elsewhere\src\app\Marked.java", "package app;\n\n@gen.MakeGreeting\npublic class Marked {\n}\n");
        var app = Write(@"elsewhere\src\app\App.java", """
            package app;

            public class App {
                public static void main(String[] args) {
                    System.out.println(new Marked() != null ? gen.Greeting.hello() : "");
                }
            }
            """);

        var report = await CheckAsync(app, "Hello from a processor");

        Assert.Empty(report.Findings);
        Assert.Equal("It printed what you expected", report.LogicSummary);
        Assert.Contains("Marked.java", FixFinder.Core.LocalFixes.CompileCheck.JavacArguments(app, app, Folder("fix-check")).Last(), StringComparison.Ordinal);
    }

    [Fact]
    public void ALibraryWithoutAProcessorLeavesProcessingOff()
    {
        Directory.CreateDirectory(Folder(@"plain\lib"));
        File.WriteAllBytes(Path.Combine(Folder(@"plain\lib"), "empty.jar"), []);
        var app = Write(@"plain\app\App.java", "package app;\n\npublic class App {\n    public static void main(String[] args) {\n    }\n}\n");

        var arguments = FixFinder.Core.LocalFixes.CompileCheck.JavacArguments(app, app, Folder("fix-check"));

        Assert.Contains("-proc:none", arguments);
        Assert.DoesNotContain("-processorpath", arguments);
    }

    [Fact]
    public async Task AProcessorAPomNamesInAnnotationProcessorPathsIsRunAndNoneFromTheLibraries()
    {
        if (ProcessorJars() is not { } jars) return;

        var repository = Folder("repository");
        Publish(repository, "gen", "greeting-annotations", "1.0", jar: jars.Annotations);
        var named = Publish(repository, "gen", "greeting-processor", "1.0", jar: jars.Processor);
        var unnamed = Publish(repository, "gen", "other-processor", "1.0", jar: jars.Processor);

        // The path gives no version of its own: maven-compiler-plugin takes the one dependencyManagement sets.
        Write(@"named\pom.xml", Pom("uni", "coursework", "1.0", Dependency("gen", "greeting-annotations", "1.0") + Dependency("gen", "other-processor", "1.0"), extra: """
            <dependencyManagement><dependencies>
              <dependency><groupId>gen</groupId><artifactId>greeting-processor</artifactId><version>1.0</version></dependency>
            </dependencies></dependencyManagement>
            <build><plugins><plugin>
              <artifactId>maven-compiler-plugin</artifactId>
              <configuration><annotationProcessorPaths>
                <path><groupId>gen</groupId><artifactId>greeting-processor</artifactId></path>
              </annotationProcessorPaths></configuration>
            </plugin></plugins></build>
            """));
        var app = Write(@"named\src\main\java\app\App.java", UsesAGeneratedGreeting);

        // The tests are not the program: a build compiles the program without them, however broken they are.
        Write(@"named\src\test\java\app\AppTest.java", "package app;\n\nclass AppTest {\n    NotAType unfinished;\n}\n");

        using var stores = JavaLibraries.UsingStores(new LibraryStores([new MavenRepository(repository)]));

        var libraries = JavaLibraries.For(app);
        Assert.Equal([Path.GetFullPath(named)], libraries.ProcessorPath.Select(Path.GetFullPath).ToArray(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(unnamed), libraries.ClassPath.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);

        var report = await CheckAsync(app, "Hello from a processor");

        Assert.DoesNotContain(report.Findings, finding => finding.Severity == Severity.Error);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    [Fact]
    public void ProcessorPathsAreInheritedFromAParentPomAndAddedToOnlyWhenTheChildSaysSo()
    {
        var repository = Folder("repository");
        var fromParent = Publish(repository, "gen", "parent-processor", "2.0");
        var fromChild = Publish(repository, "gen", "child-processor", "1.0", Dependency("gen", "processor-helper", "1.0"));
        var helper = Publish(repository, "gen", "processor-helper", "1.0");

        Write(@"family\pom.xml", """
            <project>
              <groupId>uni</groupId><artifactId>family</artifactId><version>1</version><packaging>pom</packaging>
              <build><pluginManagement><plugins><plugin>
                <artifactId>maven-compiler-plugin</artifactId>
                <configuration><annotationProcessorPaths>
                  <path><groupId>gen</groupId><artifactId>parent-processor</artifactId><version>${processor.version}</version></path>
                </annotationProcessorPaths></configuration>
              </plugin></plugins></pluginManagement></build>
            </project>
            """);

        string Child(string name, string paths) => Write($@"family\{name}\pom.xml", $"""
            <project>
              <parent><groupId>uni</groupId><artifactId>family</artifactId><version>1</version></parent>
              <artifactId>{name}</artifactId>
              <properties><processor.version>2.0</processor.version></properties>
              <build><plugins><plugin><artifactId>maven-compiler-plugin</artifactId><configuration>{paths}</configuration></plugin></plugins></build>
            </project>
            """);

        const string childPath = "<path><groupId>gen</groupId><artifactId>child-processor</artifactId><version>1.0</version></path>";
        var inherits = Child("inherits", "");
        var adds = Child("adds", $"""<annotationProcessorPaths combine.children="append">{childPath}</annotationProcessorPaths>""");
        var replaces = Child("replaces", $"<annotationProcessorPaths>{childPath}</annotationProcessorPaths>");
        var namesNone = Write(@"alone\pom.xml", Pom("uni", "alone", "1.0"));

        var resolver = new MavenResolver(new LibraryStores([new MavenRepository(repository)]));
        string[] Jars(string pom) => resolver.ProcessorsOf(pom)!.Main.Select(Path.GetFullPath).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] Expected(params string[] jars) => jars.Select(Path.GetFullPath).Order(StringComparer.OrdinalIgnoreCase).ToArray();

        Assert.Equal(Expected(fromParent), Jars(inherits), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Expected(fromParent, fromChild, helper), Jars(adds), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Expected(fromChild, helper), Jars(replaces), StringComparer.OrdinalIgnoreCase);
        Assert.Null(resolver.ProcessorsOf(namesNone));
    }

    [Fact]
    public async Task AProcessorAGradleBuildGivesAnnotationProcessorIsRun()
    {
        if (ProcessorJars() is not { } jars) return;

        var cache = Folder("gradle-cache");
        foreach (var (artifact, jar) in new[] { ("greeting-annotations", jars.Annotations), ("greeting-processor", jars.Processor) })
        {
            var folder = Path.Combine(cache, "gen", artifact, "1.0", "0123abcd");
            Directory.CreateDirectory(folder);
            File.Copy(jar, Path.Combine(folder, $"{artifact}-1.0.jar"));
        }

        Write(@"gradle\build.gradle", "plugins { id 'application' }\n\ndependencies {\n    compileOnly 'gen:greeting-annotations:1.0'\n    annotationProcessor 'gen:greeting-processor:1.0'\n}\n");
        var app = Write(@"gradle\src\main\java\app\App.java", UsesAGeneratedGreeting);

        using var stores = JavaLibraries.UsingStores(new LibraryStores([new GradleCache(cache)]));

        var libraries = JavaLibraries.For(app);
        Assert.Equal(["greeting-processor-1.0.jar"], libraries.ProcessorPath.Select(Path.GetFileName).ToArray());
        Assert.DoesNotContain(libraries.ClassPath, jar => Path.GetFileName(jar) == "greeting-processor-1.0.jar");

        var report = await CheckAsync(app, "Hello from a processor");

        Assert.DoesNotContain(report.Findings, finding => finding.Severity == Severity.Error);
        Assert.Equal("It printed what you expected", report.LogicSummary);
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

    private async Task<CheckReport> CheckAsync(string file, string? expected = null, TimeSpan? timeLimit = null)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);
        if (timeLimit is { } limit) launch = launch with { Spec = launch.Spec!.WithTimeout(limit) };
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

    [Fact]
    public async Task AMistypedSystemIsAMistakeNotAMissingLibrary()
    {
        if (Toolchains.FindJavac() is null) return;

        // javac takes Sytem.out and system.out for a class "out" in packages named Sytem and system.
        var app = Write(@"typos\Typos.java", """
            public class Typos {
                public static void main(String[] args) {
                    Sytem.out.println("a");
                    system.out.println("b");
                }
            }
            """);

        var report = await CheckAsync(app);

        Assert.DoesNotContain(report.Notes, note => note.Contains("not part of Java or of this program", StringComparison.Ordinal));
        Assert.Equal(2, report.Findings.Count(finding => finding.Kind == FindingKind.Syntax && finding.Severity == Severity.Error));
    }

    [Theory]
    [InlineData("Class.forName(\"com.mysql.cj.jdbc.Driver\");", "a class it needed could not be found - com.mysql.cj.jdbc.Driver")]
    [InlineData("java.sql.DriverManager.getConnection(\"jdbc:mysql://localhost:3306/shop\");", "No suitable driver found for jdbc:mysql://localhost:3306/shop")]
    public async Task WhatARunCouldNotFindWhileTheBuildsLibrariesAreMissingIsOnlyPossiblyAMistake(string statement, string stoppedBecause)
    {
        if (Toolchains.FindJavac() is null) return;

        Write(@"undownloaded\pom.xml", Pom("uni", "coursework", "1.0", Dependency("com.mysql", "mysql-connector-j", "8.3.0", scope: "runtime")));
        var app = Write(@"undownloaded\src\main\java\app\App.java", $$"""
            package app;

            public class App {
                public static void main(String[] args) throws Exception {
                    {{statement}}
                    System.out.println("connected");
                }
            }
            """);

        using var stores = JavaLibraries.UsingStores(new LibraryStores([new MavenRepository(Folder("empty-repository"))]));
        var report = await CheckAsync(app);

        var finding = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(Confidence.Possible, finding.Confidence);
        Assert.StartsWith("com.mysql:mysql-connector-j:8.3.0 is named in pom.xml but not on this computer, so the program ran without it.", finding.Explanation, StringComparison.Ordinal);
        Assert.Contains(stoppedBecause, finding.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AClassFromALibraryTheBuildDoesNotNameIsOfferedAsAnAdditionToItsPom()
    {
        if (Toolchains.FindJavac() is null) return;

        Write(@"unnamed\pom.xml", Pom("uni", "coursework", "1.0"));
        var app = Write(@"unnamed\src\main\java\app\App.java", """
            package app;

            public class App {
                public static void main(String[] args) throws Exception {
                    Class.forName("org.apache.commons.lang3.StringUtils");
                }
            }
            """);

        using var stores = JavaLibraries.UsingStores(new LibraryStores([new MavenRepository(Folder("empty-repository"))]));
        using var http = new FixFinderHttpClient();
        var outcome = await new FixFinderSession(http, new FixSourceRegistry()) { SearchOnline = false }
            .RunAsync(TargetFactory.FromFile(app), new SearchBudget(Cache: CacheMode.CacheOnly));

        Assert.Equal("It needs a library its pom.xml does not name.", outcome.Headline);
        Assert.Contains("the block to add to pom.xml", outcome.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("terminal", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AProfileThisComputerSwitchesOnSetsWhatTheBuildUses()
    {
        if (!OperatingSystem.IsWindows()) return;

        // As JavaFX's pom.xml picks the jar for each computer: a classifier that a profile, switched on by the computer's
        // operating system and by a property not being set, fills in.
        var repository = Folder("repository");
        Publish(repository, "gui", "toolkit-parent", "1.0", withJar: false);
        File.WriteAllText(Path.Combine(repository, "gui", "toolkit-parent", "1.0", "toolkit-parent-1.0.pom"), """
            <project>
              <groupId>gui</groupId><artifactId>toolkit-parent</artifactId><version>1.0</version><packaging>pom</packaging>
              <profiles>
                <profile><id>windows</id>
                  <activation><os><family>windows</family></os><property><name>toolkit.monocle</name><value>!true</value></property></activation>
                  <properties><toolkit.platform>win</toolkit.platform></properties>
                </profile>
                <profile><id>windows-monocle</id>
                  <activation><os><family>windows</family></os><property><name>toolkit.monocle</name><value>true</value></property></activation>
                  <properties><toolkit.platform>win-monocle</toolkit.platform></properties>
                </profile>
                <profile><id>linux</id>
                  <activation><os><family>unix</family></os></activation>
                  <properties><toolkit.platform>linux</toolkit.platform></properties>
                </profile>
                <profile><id>custom</id>
                  <activation><property><name>toolkit.platform</name></property></activation>
                  <properties><toolkit.platform>custom</toolkit.platform></properties>
                </profile>
                <profile><id>fallback</id>
                  <activation><activeByDefault>true</activeByDefault></activation>
                  <properties><toolkit.platform>fallback</toolkit.platform></properties>
                </profile>
              </profiles>
            </project>
            """);

        var placeholder = Publish(repository, "gui", "toolkit", "1.0");
        File.WriteAllText(Path.Combine(repository, "gui", "toolkit", "1.0", "toolkit-1.0.pom"), """
            <project>
              <parent><groupId>gui</groupId><artifactId>toolkit-parent</artifactId><version>1.0</version></parent>
              <artifactId>toolkit</artifactId>
              <dependencies>
                <dependency><groupId>gui</groupId><artifactId>toolkit</artifactId><version>1.0</version><classifier>${toolkit.platform}</classifier></dependency>
              </dependencies>
            </project>
            """);
        foreach (var platform in new[] { "win", "win-monocle", "linux", "custom", "fallback" })
            File.WriteAllBytes(Path.Combine(repository, "gui", "toolkit", "1.0", $"toolkit-1.0-{platform}.jar"), []);

        // A profile that is on only when no other in its pom.xml is.
        var extra = Publish(repository, "gui", "extra", "2.0");
        var pom = Write(@"profiled\pom.xml", Pom("uni", "coursework", "1.0", Dependency("gui", "toolkit", "1.0") + Dependency("gui", "extra", "${extra.version}"), extra: """
            <profiles>
              <profile><id>by-default</id><activation><activeByDefault>true</activeByDefault></activation><properties><extra.version>2.0</extra.version></properties></profile>
              <profile><id>on-a-jdk</id><activation><jdk>[1.4,)</jdk></activation><properties><extra.version>9.9</extra.version></properties></profile>
            </profiles>
            """));

        var resolved = new MavenResolver(new LibraryStores([new MavenRepository(repository)])).Resolve(pom);

        var chosen = resolved.Main.Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        string[] expected = [Path.GetFileName(extra), Path.GetFileName(placeholder), "toolkit-1.0-win.jar"];
        Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), chosen);
        Assert.Empty(resolved.Missing);
    }

    /// <summary>
    /// A jar standing in for JavaFX's javafx.graphics, with what the java launcher looks for to start an application: the
    /// module itself, javafx.application.Application, and the launcher class it hands the application to - which here starts
    /// it straight away. Built as a module, as JavaFX's jars are, or as a plain jar.
    /// </summary>
    private string? JavaFxGraphicsStandIn(bool asAModule)
    {
        if (Toolchains.FindJavac() is not { } javac) return null;

        var jarTool = Path.Combine(Path.GetDirectoryName(javac.Program)!, "jar.exe");
        if (!File.Exists(jarTool)) return null;

        var kind = asAModule ? "module" : "plain";
        List<string> sources =
        [
            Write($@"javafx-{kind}\javafx\application\Application.java", """
                package javafx.application;

                public abstract class Application {
                    public abstract void start(Object stage) throws Exception;

                    public static void launch(String... args) {
                    }
                }
                """),
            Write($@"javafx-{kind}\com\sun\javafx\application\LauncherImpl.java", """
                package com.sun.javafx.application;

                public final class LauncherImpl {
                    private LauncherImpl() {
                    }

                    public static void launchApplication(String launchName, String launchMode, String[] args) throws Exception {
                        Class<?> application = Class.forName(launchName, true, ClassLoader.getSystemClassLoader());
                        ((javafx.application.Application) application.getDeclaredConstructor().newInstance()).start(null);
                    }
                }
                """),
        ];
        if (asAModule)
            sources.Add(Write($@"javafx-{kind}\module-info.java", "module javafx.graphics {\n    exports javafx.application;\n    exports com.sun.javafx.application;\n}\n"));

        var classes = Folder($"javafx-{kind}-classes");
        var jar = Path.Combine(_temp.Path, $"javafx-{kind}", "javafx.graphics.jar");
        Run(javac.Program, $"-d \"{classes}\" {string.Join(" ", sources.Select(source => $"\"{source}\""))}");
        Run(jarTool, $"cf \"{jar}\" -C \"{classes}\" .");

        return File.Exists(jar) ? jar : null;
    }

    private const string JavaFxApplication = """
        package app;

        import javafx.application.Application;

        public class HelloApp extends Application {
            @Override
            public void start(Object stage) {
                System.out.println("started");
            }

            public static void main(String[] args) {
                launch(args);
            }
        }
        """;

    [Fact]
    public async Task AJavaFxApplicationIsStartedWithJavaFxOnTheModulePath()
    {
        if (JavaFxGraphicsStandIn(asAModule: true) is not { } javaFx) return;

        Directory.CreateDirectory(Folder(@"fx-app\lib"));
        File.Copy(javaFx, Path.Combine(Folder(@"fx-app\lib"), "javafx.graphics.jar"));
        var app = Write(@"fx-app\src\app\HelloApp.java", JavaFxApplication);

        var report = await CheckAsync(app, "started");

        Assert.Empty(report.Findings);
        Assert.Equal("It printed what you expected", report.LogicSummary);
        Assert.Contains("with JavaFX's modules on the module path", TargetFactory.FromFile(app).Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AJavaFxApplicationWithNoJavaFxModulesToStartItIsANoteNotAMistake()
    {
        if (JavaFxGraphicsStandIn(asAModule: false) is not { } plainJar) return;

        Directory.CreateDirectory(Folder(@"fx-plain\lib"));
        File.Copy(plainJar, Path.Combine(Folder(@"fx-plain\lib"), "javafx.graphics.jar"));
        var app = Write(@"fx-plain\src\app\HelloApp.java", JavaFxApplication);

        var report = await CheckAsync(app);

        Assert.Empty(report.Findings);
        Assert.Contains(report.Notes, note => note.StartsWith("Java would not start HelloApp.java: its class extends javafx.application.Application", StringComparison.Ordinal));
        Assert.Equal("It builds; Java would not start it without JavaFX", report.SyntaxSummary);
    }

    /// <summary>
    /// A folder of exercises, as a student keeps them: a Swing window class, a program that opens it - which, like any
    /// program with a window, waits until the window is closed - and a console program beside them whose loop never ends.
    /// The window class holds a JFrame it never makes, so no window opens while the tests run.
    /// </summary>
    private (string WindowProgram, string EndlessProgram) Exercises()
    {
        // A Swing helper nothing uses yet: with no main, it could be anyone's, so it stays in every program's files.
        Write(@"exercises\Dialogs.java", """
            import javax.swing.JOptionPane;

            public class Dialogs {
                public static void tell(String message) {
                    JOptionPane.showMessageDialog(null, message);
                }
            }
            """);
        Write(@"exercises\Window.java", """
            import javax.swing.JFrame;

            public class Window {
                private JFrame frame;

                public void open() throws InterruptedException {
                    Thread.sleep(Long.MAX_VALUE);
                }
            }
            """);
        var windowProgram = Write(@"exercises\Main.java", """
            public class Main {
                public static void main(String[] args) throws InterruptedException {
                    new Window().open();
                }
            }
            """);
        var endlessProgram = Write(@"exercises\Counter.java", """
            public class Counter {
                public static void main(String[] args) {
                    int count = 0;
                    int total = 0;
                    while (count < 10) {
                        total += count;
                    }
                    System.out.println(total);
                }
            }
            """);

        return (windowProgram, endlessProgram);
    }

    [Fact]
    public async Task AWindowProgramStillRunningWhenItsTimeRunsOutIsANoteNotAMistake()
    {
        if (Toolchains.FindJavac() is null) return;

        var report = await CheckAsync(Exercises().WindowProgram, timeLimit: TimeSpan.FromSeconds(5));

        Assert.DoesNotContain(report.Findings, finding => finding.RuleId == "timed-out");
        Assert.Contains(report.Notes, note => note.StartsWith("Main.java is a program with a window - it uses Swing", StringComparison.Ordinal));
        Assert.Equal("It builds, and it was still running when its time ran out, as a program with a window does", report.SyntaxSummary);
    }

    [Fact]
    public async Task AServerStillWaitingForConnectionsWhenItsTimeRunsOutIsANoteNotAMistake()
    {
        if (Toolchains.FindJavac() is null) return;

        // On this computer's loopback address and a port the system picks, so nothing outside it can connect or be asked to allow it.
        var server = Write(@"server\EchoServer.java", """
            import java.io.IOException;
            import java.net.InetAddress;
            import java.net.ServerSocket;
            import java.net.Socket;

            public class EchoServer {
                public static void main(String[] args) throws IOException {
                    try (ServerSocket listening = new ServerSocket(0, 50, InetAddress.getLoopbackAddress())) {
                        System.out.println("listening");
                        while (true) {
                            try (Socket client = listening.accept()) {
                                client.getOutputStream().write(client.getInputStream().readAllBytes());
                            }
                        }
                    }
                }
            }
            """);

        var report = await CheckAsync(server, timeLimit: TimeSpan.FromSeconds(5));

        Assert.Empty(report.Findings);
        Assert.Contains(report.Notes, note => note.StartsWith("EchoServer.java is a server - it waits for connections with a ServerSocket", StringComparison.Ordinal));
        Assert.Equal("It builds, and it was still running when its time ran out, as a server does", report.SyntaxSummary);
    }

    [Fact]
    public async Task AConsoleProgramBesideAWindowProgramThatNeverFinishesIsStillWarnedAbout()
    {
        if (Toolchains.FindJavac() is null) return;

        var report = await CheckAsync(Exercises().EndlessProgram, timeLimit: TimeSpan.FromSeconds(5));

        Assert.Contains(report.Findings, finding => finding.RuleId == "timed-out");
        Assert.DoesNotContain(report.Notes, note => note.Contains("is a program with a window", StringComparison.Ordinal));

        // Found by the analysis and by the pattern that looks for it, and said once.
        Assert.Single(report.Findings, finding => finding.Title == "A loop that never ends");
    }

    [Fact]
    public async Task AnotherProgramInTheSameFolderIsNotReadAsPartOfTheOneChecked()
    {
        if (Toolchains.FindJavac() is null) return;

        var (windowProgram, _) = Exercises();
        var hello = Write(@"exercises\Hello.java", "public class Hello {\n    public static void main(String[] args) {\n        System.out.println(\"hello\");\n    }\n}\n");

        // Each program is its own file, what it uses, and what nothing's main uses: Main uses Window, Counter and Hello use
        // nothing, and Dialogs is used by nobody.
        Assert.Equal(["Dialogs.java", "Hello.java"], ProgramFiles.Of(hello).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["Dialogs.java", "Main.java", "Window.java"], ProgramFiles.Of(windowProgram).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());

        var report = await CheckAsync(hello, "hello");

        Assert.Empty(report.Findings);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    /// <summary>Lombok itself, which CI fetches from Maven Central; null elsewhere, where the test that needs it returns early.</summary>
    private static string? LombokJar() =>
        Environment.GetEnvironmentVariable("FIXFINDER_LOMBOK_JAR") is { Length: > 0 } jar && File.Exists(jar) ? jar : null;

    [Fact]
    public async Task AProgramUsingLombokIsBuiltWithItAndWhatItAddsIsNotTakenForMistakes()
    {
        if (Toolchains.FindJavac() is null || LombokJar() is not { } lombok) return;

        Directory.CreateDirectory(Folder(@"lombok-project\lib"));
        File.Copy(lombok, Path.Combine(Folder(@"lombok-project\lib"), Path.GetFileName(lombok)));

        Write(@"lombok-project\src\app\Person.java", """
            package app;

            import lombok.AllArgsConstructor;
            import lombok.Builder;
            import lombok.Data;

            @Data
            @Builder
            @AllArgsConstructor
            public class Person {
                private String name;
                private int age;
            }
            """);
        var app = Write(@"lombok-project\src\app\App.java", """
            package app;

            import java.util.ArrayList;
            import java.util.List;

            public class App {
                public static void main(String[] args) {
                    List<Person> people = new ArrayList<>();
                    people.add(new Person("Sam", 20));
                    people.add(Person.builder().name("Alex").age(31).build());

                    int total = 0;
                    for (Person person : people) {
                        person.setAge(person.getAge() + 1);
                        total += person.getAge();
                        System.out.println(person.getName() + " " + person.getAge());
                    }

                    System.out.println("total " + total);
                    System.out.println(people.get(0).equals(new Person("Sam", 21)));
                }
            }
            """);

        var report = await CheckAsync(app, "Sam 21\nAlex 32\ntotal 53\ntrue");

        Assert.Empty(report.Findings);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    [Fact]
    public async Task UsesOfWhatAMissingLombokWouldAddAreNotMistakesButMistakesBesideThemStillAre()
    {
        if (Toolchains.FindJavac() is null) return;

        Write(@"annotated\src\app\Person.java", """
            package app;

            import lombok.AllArgsConstructor;
            import lombok.Builder;
            import lombok.Data;

            @Data
            @Builder
            @AllArgsConstructor
            public class Person {
                private String name;
                private int age;
                private Strng nickname;
            }
            """);
        Write(@"annotated\src\app\Helper.java", """
            package app;

            public class Helper {
                public static int twice(int value) {
                    return value * 2;
                }
            }
            """);
        var app = Write(@"annotated\src\app\App.java", """
            package app;

            import lombok.extern.slf4j.Slf4j;

            @Slf4j
            public class App {
                public static void main(String[] args) {
                    Person person = new Person("Sam", 20);
                    System.out.println(person.getName() + " " + person.getAge());
                    Person.PersonBuilder builder = Person.builder();
                    log.info("done");
                    System.out.println(Helper.twise(2));
                    System.out.println(total);
                }
            }
            """);

        var report = await CheckAsync(app);

        var note = Assert.Single(report.Notes, note => note.Contains("not part of Java or of this program", StringComparison.Ordinal));
        Assert.Contains("they come from a library - Lombok", note, StringComparison.Ordinal);
        Assert.Contains("6 of them use what that library would add to Person and App", note, StringComparison.Ordinal);

        // A mistyped method on a class nothing marks, a variable no library adds, and a mistyped class inside a marked one
        // are still mistakes in the code.
        var mistakes = report.Findings.Where(finding => finding.Kind == FindingKind.Syntax && finding.Severity == Severity.Error).ToList();
        Assert.Equal(3, mistakes.Count);
        Assert.Contains(mistakes, finding => $"{finding.Title} {finding.Explanation}".Contains("twise", StringComparison.Ordinal));
        Assert.Contains(mistakes, finding => $"{finding.Title} {finding.Explanation}".Contains("total", StringComparison.Ordinal));
        Assert.Contains(mistakes, finding => $"{finding.Title} {finding.Explanation}".Contains("Strng", StringComparison.Ordinal));
    }
}
