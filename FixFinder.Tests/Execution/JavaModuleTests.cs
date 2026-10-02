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
/// Covers a Java program written as a module - a module-info.java at the top of its source - built as its build builds it:
/// the modules it requires on the module path, the rest on the class path, and its tests patched into it.
/// </summary>
/// <remarks>The jars the tests use are built here, with javac and jar, so the modules in them are real ones.</remarks>
public class JavaModuleTests(ITestOutputHelper output) : IDisposable
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

    private static void Run(string program, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(program, arguments) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        var said = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(120_000);
        Assert.True(process.ExitCode == 0, said);
    }

    /// <summary>
    /// A jar of the greet package's Greeter, built here: a module of its own when a module-info.java is given, with a
    /// manifest's Automatic-Module-Name when one is, and otherwise a jar java names from its file.
    /// </summary>
    private string? GreetingJar(string jarName, string? moduleInfo = null, string? automaticName = null)
    {
        if (Toolchains.FindJavac() is not { } javac) return null;

        var source = Folder($"{jarName}-source");
        var greeter = Write(Path.Combine(source, "greet", "Greeter.java"),
            "package greet;\n\npublic class Greeter {\n    public static String hello(String name) {\n        return \"Hello, \" + name;\n    }\n}\n");
        var files = $"\"{greeter}\"";
        if (moduleInfo is not null) files += $" \"{Write(Path.Combine(source, "module-info.java"), moduleInfo)}\"";

        var classes = Folder($"{jarName}-classes");
        Run(javac.Program, $"-d \"{classes}\" {files}");

        var jar = Path.Combine(Folder("jars"), jarName);
        Directory.CreateDirectory(Path.GetDirectoryName(jar)!);
        var jarTool = Path.Combine(Path.GetDirectoryName(javac.Program)!, OperatingSystem.IsWindows() ? "jar.exe" : "jar");

        if (automaticName is not null)
        {
            var manifest = Write($"{jarName}-manifest.txt", $"Automatic-Module-Name: {automaticName}\n");
            Run(jarTool, $"cfm \"{jar}\" \"{manifest}\" -C \"{classes}\" .");
        }
        else
        {
            Run(jarTool, $"cf \"{jar}\" -C \"{classes}\" .");
        }

        return jar;
    }

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
        {
            output.WriteLine($"[{finding.Severity}/{finding.Kind}] {finding.Location}: {finding.Title}");
            output.WriteLine($"    checked by: {finding.FixCheckedBy} | {finding.Verified.Summary}: {string.Join(" / ", finding.Verified.Steps.Select(step => step.Detail))}");
        }

        return report;
    }

    [Fact]
    public void AJarsModuleIsNamedAsJavaNamesIt()
    {
        if (GreetingJar("greeting-modular-1.0.jar", moduleInfo: "module greeting.api {\n    exports greet;\n}\n") is not { } modular) return;

        var automatic = GreetingJar("greeting-automatic-1.0.jar", automaticName: "com.example.greeting")!;
        var plain = GreetingJar("greeting-tools_extra-2.1.3-SNAPSHOT.jar")!;

        Assert.Equal("greeting.api", JavaModules.NameOf(modular));
        Assert.Equal("com.example.greeting", JavaModules.NameOf(automatic));

        // Named from its file, as java does: up to the version, what is not a letter or digit a dot.
        Assert.Equal("greeting.tools.extra", JavaModules.NameOf(plain));
    }

    [Fact]
    public void WhatAModuleRequiresIsReadWithoutItsComments()
    {
        Write(@"shop\src\module-info.java", """
            /** The shop. */
            module com.example.shop {
                requires transitive java.sql;
                requires static lombok; // only while it is compiled
                /* requires greeting.old; */
                requires
                    greeting.api;
                exports com.example.shop;
            }
            """);

        var module = JavaModules.In(Folder(@"shop\src"));

        Assert.Equal("com.example.shop", module!.Name);
        Assert.Equal(["java.sql", "lombok", "greeting.api"], module.Requires.ToArray());
        Assert.Null(JavaModules.In(Folder("shop")));
    }

    [Fact]
    public void TheModulesOnTheModulePathAreThoseRequiredAndThoseTheyRequire()
    {
        // greeting.api requires greeting.core: a module the program requires brings the ones it requires with it.
        if (GreetingJar("greeting-core-1.0.jar", moduleInfo: "module greeting.core {\n    exports greet;\n}\n") is not { } core) return;

        var javac = Toolchains.FindJavac()!;
        var apiSource = Write(@"api-source\module-info.java", "module greeting.api {\n    requires transitive greeting.core;\n}\n");
        Run(javac.Program, $"--module-path \"{core}\" -d \"{Folder("api-classes")}\" \"{apiSource}\"");
        var api = Path.Combine(Folder("jars"), "greeting-api-1.0.jar");
        Run(Path.Combine(Path.GetDirectoryName(javac.Program)!, OperatingSystem.IsWindows() ? "jar.exe" : "jar"), $"cf \"{api}\" -C \"{Folder("api-classes")}\" .");
        var unrelated = GreetingJar("unrelated-1.0.jar")!;

        var module = new JavaModules.Declared("shop", Folder("src"), ["greeting.api", "java.sql"]);

        Assert.Equal([api, core], JavaModules.ModulePath(module, [unrelated, core, api]).ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AProgramWrittenAsAModuleIsBuiltWithTheModulesItRequiresOnTheModulePath()
    {
        if (GreetingJar("greeting-1.0.jar") is not { } jar) return;

        Directory.CreateDirectory(Folder(@"shop\lib"));
        File.Copy(jar, Folder(@"shop\lib\greeting-1.0.jar"));
        Write(@"shop\src\module-info.java", "module shop {\n    requires greeting;\n}\n");
        var main = Write(@"shop\src\shop\Main.java", "package shop;\n\npublic class Main {\n    public static void main(String[] args) {\n        System.out.println(greet.Greeter.hello(\"Ada\"));\n    }\n}\n");

        Assert.Contains(", as the module shop its module-info.java declares, then running it with java", TargetFactory.FromFile(main).Explanation, StringComparison.Ordinal);

        // With greeting on the class path, as before, javac said it could not find the module greeting.
        var report = await CheckAsync(main, "Hello, Ada");

        Assert.Empty(report.Findings);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    [Fact]
    public async Task AModuleThatUsesALibraryItDoesNotRequireIsStillToldSo()
    {
        if (GreetingJar("greeting-1.0.jar") is not { } jar) return;

        Directory.CreateDirectory(Folder(@"shop\lib"));
        File.Copy(jar, Folder(@"shop\lib\greeting-1.0.jar"));
        Write(@"shop\src\module-info.java", "module shop {\n}\n");
        var main = Write(@"shop\src\shop\Main.java", "package shop;\n\npublic class Main {\n    public static void main(String[] args) {\n        System.out.println(greet.Greeter.hello(\"Ada\"));\n    }\n}\n");

        var report = await CheckAsync(main);

        // javac's own words: the package is in the class path's unnamed module, which a module does not read unless told to.
        var notRead = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Contains("greet", notRead.Title, StringComparison.Ordinal);
        Assert.Equal(5, notRead.Line);
    }

    [Fact]
    public async Task AChangeToAModulesCodeIsCompiledPatchedIntoTheModule()
    {
        if (Toolchains.FindJavac() is null) return;

        Write(@"marks\src\module-info.java", "module marks {\n}\n");
        var main = Write(@"marks\src\marks\Main.java",
            "package marks;\n\npublic class Main {\n    public static void main(String[] args) {\n        int[] values = {1, 2, 3};\n        System.out.println(values.length());\n    }\n}\n");

        var report = await CheckAsync(main);

        // The copy the change is compiled in stands outside the module's source; were it not patched in, it would not compile.
        var wrongLength = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Contains("System.out.println(values.length);", wrongLength.Change!.Lines.Select(line => line.Text.Trim()));
        Assert.Equal("FixFinder made this change to a copy of Main.java, outside your project, and compiled it with javac: the error is gone.",
            wrongLength.FixCheckedBy);

        // And the program, copied whole with the change in it, builds as the module and runs.
        Assert.True(wrongLength.Verified.IsVerified, string.Join(" / ", wrongLength.Verified.Steps.Select(step => step.Detail)));
    }

    /// <summary>JUnit 5's jars: those the CI workflow fetched, or else the ones an Eclipse here has; null when neither is here.</summary>
    private static IReadOnlyList<string>? JUnit5Jars() =>
        Environment.GetEnvironmentVariable("FIXFINDER_JUNIT_JARS") is { Length: > 0 } folder && Directory.Exists(folder) &&
        Directory.EnumerateFiles(folder, "junit-platform-console-standalone*.jar").FirstOrDefault() is { } standalone
            ? [standalone]
            : EclipseJUnit.For("5")?.Jars;

    [Fact]
    public async Task TheTestsOfAModuleArePatchedIntoItAsABuildCompilesThem()
    {
        if (Toolchains.FindJavac() is null || JUnit5Jars() is not { } junit) return;

        Directory.CreateDirectory(Folder(@"shop\lib"));
        foreach (var jar in junit) File.Copy(jar, Path.Combine(Folder(@"shop\lib"), Path.GetFileName(jar)));

        Write(@"shop\.classpath", "<classpath>\n  <classpathentry kind=\"src\" path=\"src/main/java\"/>\n  <classpathentry kind=\"src\" path=\"src/test/java\"/>\n" +
            string.Concat(junit.Select(jar => $"  <classpathentry kind=\"lib\" path=\"lib/{Path.GetFileName(jar)}\"/>\n")) + "</classpath>\n");
        Write(@"shop\src\main\java\module-info.java", "module shop {\n    exports shop;\n}\n");
        Write(@"shop\src\main\java\shop\Calculator.java", "package shop;\n\npublic class Calculator {\n    public int divide(int dividend, int divisor) {\n        return dividend / divisor;\n    }\n}\n");
        var tests = Write(@"shop\src\test\java\shop\CalculatorTest.java", """
            package shop;

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

        var report = await CheckAsync(tests);

        // Before, the test was compiled into the module, which could not read JUnit on the class path, and did not build.
        Assert.Single(report.Findings, finding => finding.Title.StartsWith("Test dividesByZero stopped with ArithmeticException", StringComparison.Ordinal));
        Assert.Equal("It builds; 1 of 2 tests failed", report.SyntaxSummary);
    }

    [Fact]
    public async Task TestsInsideAModuleThatRequiresJUnitAreRunWithFixFindersLauncherPatchedIn()
    {
        // Only modular jars of JUnit 5 make org.junit.jupiter.api a module to require; the standalone jar does not.
        if (Toolchains.FindJavac() is null || JUnit5Jars() is not { } junit || !junit.Any(jar => JavaModules.NameOf(jar) == "org.junit.jupiter.api")) return;

        Directory.CreateDirectory(Folder(@"till\lib"));
        foreach (var jar in junit) File.Copy(jar, Path.Combine(Folder(@"till\lib"), Path.GetFileName(jar)));

        // An Eclipse project as its wizard makes one: a single src folder, with module-info.java in it, and the tests beside the code.
        Write(@"till\.classpath", "<classpath>\n  <classpathentry kind=\"src\" path=\"src\"/>\n" +
            string.Concat(junit.Select(jar => $"  <classpathentry kind=\"lib\" path=\"lib/{Path.GetFileName(jar)}\"/>\n")) + "</classpath>\n");
        Write(@"till\src\module-info.java", "module till {\n    requires org.junit.jupiter.api;\n}\n");
        Write(@"till\src\till\Calculator.java", "package till;\n\npublic class Calculator {\n    public int divide(int dividend, int divisor) {\n        return dividend / divisor;\n    }\n}\n");
        var tests = Write(@"till\src\till\CalculatorTest.java", """
            package till;

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

        var report = await CheckAsync(tests);

        // FixFinder's launcher is compiled with the tests and uses JUnit's launcher, which the module does not require: it is
        // patched into the module, which then reads the class path, or it would not build.
        Assert.Single(report.Findings, finding => finding.Title.StartsWith("Test dividesByZero stopped with ArithmeticException", StringComparison.Ordinal));
        Assert.Equal("It builds; 1 of 2 tests failed", report.SyntaxSummary);
    }
}
