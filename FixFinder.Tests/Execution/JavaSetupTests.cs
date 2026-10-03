using FixFinder.Core.Execution;
using FixFinder.Core.Execution.Libraries;

namespace FixFinder.Tests;

/// <summary>
/// Which JDK builds a Java program, and for which Java: what its project says - pom.xml, a Gradle build, IntelliJ's, Eclipse's
/// or VS Code's settings - and what its own code needs. The JDKs are made here, laid out as installers lay them out, and
/// nothing is compiled: these are the choices, which the live tests then build with.
/// </summary>
public class JavaSetupTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private readonly string _java21;

    public JavaSetupTests()
    {
        _java21 = JdksTests.FakeJdk(Path.Combine(_temp.Path, "Program Files", "Microsoft", "jdk-21.0.12.101-hotspot"), "21.0.12.1");
        JdksTests.FakeJdk(Path.Combine(_temp.Path, "Program Files", "Eclipse Adoptium", "jdk-25.0.4+7"), "25.0.4");
        JdksTests.FakeJdk(Path.Combine(_temp.Path, "Program Files", "Java", "jdk1.8.0_402"), "1.8.0_402");
    }

    public void Dispose() => _temp.Dispose();

    /// <summary>A computer with Java 21 on PATH, as this one has, and Java 25 and Java 8 installed beside it - looked in until disposed.</summary>
    private IDisposable ThisComputer() => Jdks.LookingIn(JdksTests.PlacesIn(_temp.Path, jdkOnPath: _java21));

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private const string PlainClass = "public class Main {\n    public static void main(String[] args) {\n        System.out.println(\"hi\");\n    }\n}\n";

    private string MavenProject(string folder, string properties, string build = "") =>
        Write($@"{folder}\pom.xml",
            $"<project><modelVersion>4.0.0</modelVersion><groupId>uni</groupId><artifactId>{folder}</artifactId><version>1</version>" +
            $"<properties>{properties}</properties>{build}</project>");

    private JavaSetup Setup(string javaFile) => JavaSetup.For(javaFile).Setup ?? throw new InvalidOperationException(JavaSetup.For(javaFile).Problem);

    [Fact]
    public void AProgramThatAsksForNothingIsBuiltWithTheDefaultJdkAsItIs()
    {
        using var computer = ThisComputer();
        var main = Write(@"plain\Main.java", PlainClass);

        var setup = Setup(main);

        Assert.Equal(21, setup.Jdk.Version);
        Assert.Empty(setup.CompilerOptions);
        Assert.Empty(setup.RunOptions);
        Assert.Null(setup.HowCompiled);
        Assert.Equal("Java 21.0.12.1 (on PATH)", setup.JdkExplained);
    }

    [Fact]
    public void APomsReleaseIsCompiledForWithTheDefaultJdkWhenItIsNewEnough()
    {
        using var computer = ThisComputer();
        MavenProject("release17", "<maven.compiler.release>17</maven.compiler.release>");
        var main = Write(@"release17\src\main\java\Main.java", PlainClass);

        var setup = Setup(main);

        Assert.Equal(21, setup.Jdk.Version);
        Assert.Equal(["--release", "17"], setup.CompilerOptions);
        Assert.Equal("compiling it for Java 17 as pom.xml's maven.compiler.release says", setup.HowCompiled);
    }

    /// <summary>Maven's source and target hold only the language to a release - not the library, as --release does.</summary>
    [Fact]
    public void APomsSourceAndTargetAreGivenAsSourceAndTarget()
    {
        using var computer = ThisComputer();
        MavenProject("source11", "<maven.compiler.source>11</maven.compiler.source><maven.compiler.target>11</maven.compiler.target>");
        var main = Write(@"source11\src\main\java\Main.java", PlainClass);

        Assert.Equal(["-source", "11", "-target", "11"], Setup(main).CompilerOptions);
    }

    /// <summary>javac from 21 to 27 compiles for nothing older than Java 8 - its Source.MIN - so an older one is compiled for 8, and says so.</summary>
    [Fact]
    public void AReleaseOlderThanTheJdkCompilesForIsCompiledForItsOldest()
    {
        using var computer = ThisComputer();
        MavenProject("source7", "", "<build><plugins><plugin><artifactId>maven-compiler-plugin</artifactId><configuration><source>1.7</source><target>1.7</target></configuration></plugin></plugins></build>");
        var main = Write(@"source7\src\main\java\Main.java", PlainClass);

        var setup = Setup(main);

        Assert.Equal(["-source", "8", "-target", "8"], setup.CompilerOptions);
        Assert.Equal("compiling it for Java 8, the oldest Java 21's javac compiles for - maven-compiler-plugin's source in pom.xml says Java 7", setup.HowCompiled);
    }

    [Fact]
    public void AReleaseNewerThanEveryJdkHereIsSaidRatherThanBuilt()
    {
        using var computer = ThisComputer();
        MavenProject("release27", "<maven.compiler.release>27</maven.compiler.release>");
        var main = Write(@"release27\src\main\java\Main.java", PlainClass);

        var (setup, problem) = JavaSetup.For(main);

        Assert.Null(setup);
        Assert.StartsWith("Main.java's project is written for Java 27 - pom.xml's maven.compiler.release says so - and the newest JDK on this computer is Java 25.0.4", problem, StringComparison.Ordinal);
        Assert.Contains("A JDK cannot compile for a later Java than its own.", problem, StringComparison.Ordinal);
        Assert.Contains("winget search Temurin", problem, StringComparison.Ordinal);
    }

    /// <summary>Spring Boot's parent passes java.version to the compiler, as its release from Spring Boot 3, so it is read even with the parent not downloaded.</summary>
    [Fact]
    public void SpringBootsJavaVersionIsTheReleaseEvenWithItsParentNotDownloaded()
    {
        using var computer = ThisComputer();
        Write(@"boot\pom.xml",
            "<project><modelVersion>4.0.0</modelVersion>" +
            "<parent><groupId>org.springframework.boot</groupId><artifactId>spring-boot-starter-parent</artifactId><version>3.5.0</version></parent>" +
            "<artifactId>shop</artifactId><properties><java.version>17</java.version></properties></project>");
        var main = Write(@"boot\src\main\java\Main.java", PlainClass);

        using var nothingDownloaded = JavaLibraries.UsingStores(new LibraryStores([]));
        var setup = Setup(main);

        Assert.Equal(["--release", "17"], setup.CompilerOptions);
        Assert.Contains("pom.xml's java.version, which Spring Boot's parent passes to the compiler", setup.HowCompiled, StringComparison.Ordinal);
    }

    /// <summary>A preview feature is its JDK's own, so a build that turns them on is built by a JDK of exactly its release, and run with them on.</summary>
    [Fact]
    public void ABuildWithPreviewFeaturesOnIsBuiltByAJdkOfExactlyItsRelease()
    {
        using var computer = ThisComputer();
        MavenProject("preview", "<maven.compiler.release>25</maven.compiler.release><maven.compiler.enablePreview>true</maven.compiler.enablePreview>");
        var main = Write(@"preview\src\main\java\Main.java", PlainClass);

        var setup = Setup(main);

        Assert.Equal(25, setup.Jdk.Version);
        Assert.Equal(["--enable-preview", "--release", "25"], setup.CompilerOptions);
        Assert.Equal(["--enable-preview"], setup.RunOptions);
        Assert.Contains("with Java 25's preview features on", setup.HowCompiled, StringComparison.Ordinal);
    }

    [Fact]
    public void EnablePreviewAmongMavensCompilerArgumentsTurnsPreviewFeaturesOn()
    {
        using var computer = ThisComputer();
        MavenProject("previewArgs", "<maven.compiler.release>25</maven.compiler.release>",
            "<build><plugins><plugin><artifactId>maven-compiler-plugin</artifactId><configuration><compilerArgs><arg>--enable-preview</arg></compilerArgs></configuration></plugin></plugins></build>");
        var main = Write(@"previewArgs\src\main\java\Main.java", PlainClass);

        Assert.True(Setup(main).Preview);
    }

    /// <summary>A Gradle toolchain is the JDK Gradle builds with, so a JDK of that Java is taken when one is here.</summary>
    [Fact]
    public void AGradleToolchainChoosesAJdkOfItsJava()
    {
        using var computer = ThisComputer();
        Write(@"toolchain\build.gradle", "plugins { id 'java' }\n\njava {\n    toolchain {\n        languageVersion = JavaLanguageVersion.of(25)\n    }\n}\n");
        var main = Write(@"toolchain\src\main\java\Main.java", PlainClass);

        var setup = Setup(main);

        Assert.Equal(25, setup.Jdk.Version);
        Assert.Equal(["--release", "25"], setup.CompilerOptions);
        Assert.Equal("Java 25.0.4 (installed in Program Files), as the Java toolchain in build.gradle asks", setup.JdkExplained);
    }

    [Theory]
    [InlineData("java {\n    sourceCompatibility = JavaVersion.VERSION_17\n}\n", "-source", "17")]
    [InlineData("sourceCompatibility = '1.8'\n", "-source", "8")]
    [InlineData("tasks.withType(JavaCompile) {\n    options.release = 17\n}\n", "--release", "17")]
    [InlineData("tasks.withType<JavaCompile> {\n    options.release.set(11)\n}\n", "--release", "11")]
    public void AGradleBuildsJavaIsReadInTheFormsItIsWrittenIn(string written, string option, string release)
    {
        using var computer = ThisComputer();
        var folder = $"gradle-{Guid.NewGuid():N}";
        Write($@"{folder}\build.gradle", "plugins { id 'java' }\n\n" + written);
        var main = Write($@"{folder}\src\main\java\Main.java", PlainClass);

        var options = Setup(main).CompilerOptions;

        Assert.Equal(option, options[0]);
        Assert.Equal(release, options[1]);
    }

    /// <summary>A build of several projects says it once, for them all, in subprojects { } - with the Java in gradle.properties.</summary>
    [Fact]
    public void AGradleBuildOfSeveralProjectsSaysItsJavaAboveThem()
    {
        using var computer = ThisComputer();
        Write(@"several\settings.gradle", "include 'app'\n");
        Write(@"several\gradle.properties", "javaVersion=17\n");
        Write(@"several\build.gradle", "subprojects {\n    apply plugin: 'java'\n    java {\n        toolchain {\n            languageVersion = JavaLanguageVersion.of(javaVersion)\n        }\n    }\n}\n");
        Write(@"several\app\build.gradle", "dependencies {\n}\n");
        var main = Write(@"several\app\src\main\java\Main.java", PlainClass);

        var declared = DeclaredJava.Of(main)!;

        Assert.Equal(17, declared.Release);
        Assert.Equal(17, declared.JdkVersion);
        Assert.Equal("the Java toolchain in the subprojects { } of build.gradle", declared.SaidBy);
    }

    [Fact]
    public void AVersionCatalogsJavaVersionIsRead()
    {
        using var computer = ThisComputer();
        Write(@"catalog\gradle\libs.versions.toml", "[versions]\njava = \"21\"\n");
        Write(@"catalog\build.gradle.kts", "plugins { java }\n\njava {\n    toolchain {\n        languageVersion.set(JavaLanguageVersion.of(libs.versions.java.get().toInt()))\n    }\n}\n");
        var main = Write(@"catalog\src\main\java\Main.java", PlainClass);

        Assert.Equal(21, DeclaredJava.Of(main)!.Release);
    }

    [Fact]
    public void IntelliJsLanguageLevelIsTheReleaseOfAProjectWithNoBuild()
    {
        using var computer = ThisComputer();
        Write(@"intellij\.idea\misc.xml",
            "<project version=\"4\"><component name=\"ProjectRootManager\" version=\"2\" languageLevel=\"JDK_17\" project-jdk-name=\"no-such-jdk-here\" project-jdk-type=\"JavaSDK\" /></project>");
        var main = Write(@"intellij\src\Main.java", PlainClass);

        var setup = Setup(main);

        Assert.Equal(["--release", "17"], setup.CompilerOptions);
        Assert.Equal("compiling it for Java 17 as IntelliJ's project language level says", setup.HowCompiled);
    }

    [Fact]
    public void IntelliJsPreviewLanguageLevelTurnsPreviewFeaturesOn()
    {
        using var computer = ThisComputer();
        Write(@"intellijPreview\.idea\misc.xml",
            "<project version=\"4\"><component name=\"ProjectRootManager\" version=\"2\" languageLevel=\"JDK_25_PREVIEW\" /></project>");
        var main = Write(@"intellijPreview\src\Main.java", PlainClass);

        var setup = Setup(main);

        Assert.Equal(25, setup.Jdk.Version);
        Assert.Equal(["--enable-preview", "--release", "25"], setup.CompilerOptions);
    }

    [Fact]
    public void EclipsesComplianceLevelIsTheRelease()
    {
        using var computer = ThisComputer();
        Write(@"eclipse\.classpath",
            "<classpath><classpathentry kind=\"src\" path=\"src\"/><classpathentry kind=\"con\" path=\"org.eclipse.jdt.launching.JRE_CONTAINER/org.eclipse.jdt.internal.debug.ui.launcher.StandardVMType/JavaSE-17\"/></classpath>");
        Write(@"eclipse\.settings\org.eclipse.jdt.core.prefs",
            "eclipse.preferences.version=1\norg.eclipse.jdt.core.compiler.compliance=11\norg.eclipse.jdt.core.compiler.release=enabled\norg.eclipse.jdt.core.compiler.source=11\n");
        var main = Write(@"eclipse\src\Main.java", PlainClass);

        Assert.Equal(["--release", "11"], Setup(main).CompilerOptions);
    }

    [Fact]
    public void VsCodesDefaultRuntimeIsTheJdkItBuildsWith()
    {
        using var computer = ThisComputer();
        var java25 = Jdks.Installed.Single(jdk => jdk.Version == 25).Home;
        Write(@"vscode\.vscode\settings.json",
            "{\n  // the JDKs VS Code knows\n  \"java.configuration.runtimes\": [\n    { \"name\": \"JavaSE-21\", \"path\": \"" + _java21.Replace("\\", "\\\\") + "\" },\n" +
            "    { \"name\": \"JavaSE-25\", \"path\": \"" + java25.Replace("\\", "\\\\") + "\", \"default\": true },\n  ],\n}\n");
        var main = Write(@"vscode\src\Main.java", PlainClass);

        var setup = Setup(main);

        Assert.Equal(25, setup.Jdk.Version);
        Assert.Equal("Java 25.0.4 (installed in Program Files), VS Code's default Java runtime, JavaSE-25", setup.JdkExplained);
    }

    /// <summary>A compact source file builds only on Java 25 or later, so a JDK of that is chosen when the default is older.</summary>
    [Fact]
    public void ACompactSourceFileIsBuiltWithAJdkOfJava25()
    {
        using var computer = ThisComputer();
        var hello = Write(@"compact\Hello.java", "void main() {\n    IO.println(\"Hello\");\n}\n");

        var setup = Setup(hello);

        Assert.Equal(25, setup.Jdk.Version);
        Assert.Equal("Java 25.0.4 (installed in Program Files), as Hello.java is a compact source file - methods with no class around them - which Java 25 made part of the language", setup.JdkExplained);
    }

    [Fact]
    public void AnUnnamedVariableIsBuiltWithTheOldestJdkThatHasIt()
    {
        using var computer = ThisComputer();
        var count = Write(@"unnamed\Count.java",
            "import java.util.List;\n\npublic class Count {\n    public static void main(String[] args) {\n        int total = 0;\n        for (String _ : List.of(\"a\")) total++;\n        System.out.println(total);\n    }\n}\n");

        Assert.Equal(25, Setup(count).Jdk.Version);
    }

    /// <summary>Java 26 took the Applet API out, so an applet is built with a JDK of 25 or earlier when the default is later.</summary>
    [Fact]
    public void AnAppletIsBuiltWithAJdkThatStillHasTheAppletApi()
    {
        using var temp = new TempFolder();
        var java26 = JdksTests.FakeJdk(Path.Combine(temp.Path, "Program Files", "Java", "jdk-26"), "26.0.1");
        JdksTests.FakeJdk(Path.Combine(temp.Path, "Program Files", "Java", "jdk-21"), "21.0.5");
        using var computer = Jdks.LookingIn(JdksTests.PlacesIn(temp.Path, jdkOnPath: java26));

        var applet = Write(@"applet\Clock.java", "import java.applet.Applet;\n\npublic class Clock extends Applet {\n}\n");

        var setup = Setup(applet);

        Assert.Equal(21, setup.Jdk.Version);
        Assert.EndsWith("as Clock.java is an applet, and Java 26 took the Applet API out of Java", setup.JdkExplained, StringComparison.Ordinal);
    }

    /// <summary>A jar holding an annotation processor as javac finds one: named in META-INF/services, as Lombok's jar names its own.</summary>
    private static void ProcessorJar(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var jar = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        using var services = new StreamWriter(jar.CreateEntry("META-INF/services/javax.annotation.processing.Processor").Open());
        services.Write("lombok.launch.AnnotationProcessorHider$AnnotationProcessor\n");
    }

    /// <summary>
    /// Lombok added support for each Java in a release of its own - Java 22 in 1.18.32, as its changelog records - so a
    /// project with an earlier Lombok is built with a JDK that Lombok works with, when one is here.
    /// </summary>
    [Fact]
    public void AProjectsLombokChoosesAJdkItWorksWith()
    {
        using var temp = new TempFolder();
        var java25 = JdksTests.FakeJdk(Path.Combine(temp.Path, "Program Files", "Java", "jdk-25"), "25.0.1");
        JdksTests.FakeJdk(Path.Combine(temp.Path, "Program Files", "Java", "jdk-21"), "21.0.5");
        using var computer = Jdks.LookingIn(JdksTests.PlacesIn(temp.Path, jdkOnPath: java25));

        ProcessorJar(Path.Combine(_temp.Path, "lombok-project", "lib", "lombok-1.18.30.jar"));
        var main = Write(@"lombok-project\src\Main.java", PlainClass);

        var setup = Setup(main);

        Assert.Equal(21, setup.Jdk.Version);
        Assert.EndsWith("as the project builds with Lombok 1.18.30, and Lombok added support for Java 22 only in 1.18.32", setup.JdkExplained, StringComparison.Ordinal);
    }

    [Fact]
    public void ALombokThatWorksWithEveryJavaUpTo27LeavesTheDefaultJdk()
    {
        using var temp = new TempFolder();
        var java25 = JdksTests.FakeJdk(Path.Combine(temp.Path, "Program Files", "Java", "jdk-25"), "25.0.1");
        JdksTests.FakeJdk(Path.Combine(temp.Path, "Program Files", "Java", "jdk-21"), "21.0.5");
        using var computer = Jdks.LookingIn(JdksTests.PlacesIn(temp.Path, jdkOnPath: java25));

        ProcessorJar(Path.Combine(_temp.Path, "new-lombok", "lib", "lombok-1.18.48.jar"));
        var main = Write(@"new-lombok\src\Main.java", PlainClass);

        Assert.Equal(25, Setup(main).Jdk.Version);
    }

    [Fact]
    public void ACompactSourceFileWithNoJdkNewEnoughSaysSoAndIsBuiltAnyway()
    {
        using var temp = new TempFolder();
        var java21 = JdksTests.FakeJdk(Path.Combine(temp.Path, "Program Files", "Java", "jdk-21"), "21.0.5");
        using var computer = Jdks.LookingIn(JdksTests.PlacesIn(temp.Path, jdkOnPath: java21));

        var hello = Write(@"compactOnly21\Hello.java", "void main() {\n    IO.println(\"Hello\");\n}\n");

        var setup = Setup(hello);

        Assert.Equal(21, setup.Jdk.Version);
        Assert.Equal("though Hello.java is a compact source file - methods with no class around them - which Java 25 made part of the language, " +
                     "and no JDK of Java 25 or later is on this computer", setup.HowCompiled);
    }

    [Fact]
    public void WithNoJdkAtAllTheProblemSaysWhereFixFinderLooked()
    {
        using var temp = new TempFolder();
        using var computer = Jdks.LookingIn(JdksTests.PlacesIn(temp.Path));
        var main = Write(@"nojdk\Main.java", PlainClass);

        var (setup, problem) = JavaSetup.For(main);

        Assert.Null(setup);
        Assert.Contains("no JDK was found", problem, StringComparison.Ordinal);
        Assert.Contains("winget install Microsoft.OpenJDK.25", problem, StringComparison.Ordinal);
    }
}
