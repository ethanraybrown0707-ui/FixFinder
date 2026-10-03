using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// The note beside what javac says when code is newer than the Java it is built for. The messages are javac's own, as JDK
/// 21 and 25 print them, and the JDKs are made here, laid out as installers lay them out.
/// </summary>
public class JavaVersionErrorsTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>A computer with Java 21 on PATH, and - when asked - Java 25 installed beside it.</summary>
    private IDisposable Computer(bool withJava25)
    {
        var java21 = JdksTests.FakeJdk(Path.Combine(_temp.Path, "Program Files", "Microsoft", "jdk-21.0.5"), "21.0.5");
        if (withJava25) JdksTests.FakeJdk(Path.Combine(_temp.Path, "Program Files", "Eclipse Adoptium", "jdk-25.0.4+7"), "25.0.4");
        return Jdks.LookingIn(JdksTests.PlacesIn(_temp.Path, jdkOnPath: java21));
    }

    private static ParsedError CompileError(string file, int line, string message, params string[] following) => new()
    {
        LanguageId = "java",
        Confidence = 90,
        RawText = string.Join("\n", new[] { $"{file}:{line}: error: {message}" }.Concat(following)),
        FirstLineSequence = 1,
        ExceptionType = "compile error",
        Message = message,
        Frames = [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = $"{file}:{line}: error: {message}" }],
    };

    /// <summary>A program javac reads as the one this note is for: a plain class, so FixFinder itself would build it with the default JDK.</summary>
    private string Program() => Write(@"program\Main.java", "public class Main {\n    public static void main(String[] args) {}\n}\n");

    [Fact]
    public void APreviewFeatureWithNoJdkThatHasItSaysWhichJavaToInstall()
    {
        using var computer = Computer(withJava25: false);
        var main = Program();

        var note = JavaVersionErrors.NoteFor([CompileError(main, 3, "unnamed classes are a preview feature and are disabled by default.", "  (use --enable-preview to enable unnamed classes)")], main);

        Assert.Equal("Unnamed classes, which Main.java:3 uses, became part of Java in Java 25, and the program was built with Java 21.0.5 (on PATH), " +
                     "where it is only a preview. No JDK of Java 25 or later is on this computer. Install one - for example:\n  winget install Microsoft.OpenJDK.25", note);
    }

    [Fact]
    public void APreviewFeatureWithAJdkThatHasItSaysHowToBuildWithThatOne()
    {
        using var computer = Computer(withJava25: true);
        var main = Program();

        var note = JavaVersionErrors.NoteFor([CompileError(main, 7, "flexible constructors is a preview feature and is disabled by default.")], main);

        Assert.EndsWith("Java 25.0.4 (installed in Program Files) is on this computer and can build it: choosing Java 25 in Settings builds the program with it.", note);
        Assert.StartsWith("Flexible constructors, which Main.java:7 uses, became part of Java in Java 25", note, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimitivePatternsAreSaidToBeAPreviewInEveryJavaUpTo27()
    {
        using var computer = Computer(withJava25: true);
        var main = Program();

        var note = JavaVersionErrors.NoteFor([CompileError(main, 4, "primitive patterns are a preview feature and are disabled by default.")], main);

        Assert.StartsWith("Primitive patterns, which Main.java:4 uses, are a preview feature in every Java up to 27", note, StringComparison.Ordinal);
    }

    [Fact]
    public void StringTemplatesAreSaidToHaveBeenTakenOut()
    {
        using var computer = Computer(withJava25: false);
        var main = Program();

        var note = JavaVersionErrors.NoteFor([CompileError(main, 5, "string templates are a preview feature and are disabled by default.")], main);

        Assert.StartsWith("String templates, which Main.java:5 uses, were a preview in Java 21 and 22 only, and were then taken out of Java", note, StringComparison.Ordinal);
    }

    [Fact]
    public void AFeatureNewerThanTheReleaseCompiledForSaysWhichJavaItCameIn()
    {
        using var computer = Computer(withJava25: true);
        var main = Program();

        var note = JavaVersionErrors.NoteFor(
            [CompileError(main, 1, "implicitly declared classes are not supported in -source 21", "void main() {", "^", "  (use -source 25 or higher to enable implicitly declared classes)")], main);

        Assert.StartsWith("Implicitly declared classes, which Main.java:1 uses, came in Java 25 - and the program is compiled for Java 21.", note, StringComparison.Ordinal);
    }

    /// <summary>A class file's version is its Java plus 44, as the Java Virtual Machine Specification's table has it: 69 is Java 25's.</summary>
    [Fact]
    public void ALibraryBuiltForALaterJavaSaysWhichJavaItWasBuiltFor()
    {
        using var computer = Computer(withJava25: false);
        var main = Program();

        var note = JavaVersionErrors.NoteFor(
            [CompileError(main, 1, "cannot access fancy.Widget",
                "  bad class file: C:\\libs\\fancy-2.0.jar(/fancy/Widget.class)",
                "    class file has wrong version 69.0, should be 65.0",
                "    Please remove or make sure it appears in the correct subdirectory of the classpath.")], main);

        Assert.StartsWith("C:\\libs\\fancy-2.0.jar(/fancy/Widget.class) was built for Java 25 - its class file's version is 69 - and Java 21.0.5 (on PATH) " +
                          "reads class files only up to Java 21's.", note, StringComparison.Ordinal);
        Assert.Contains("winget install Microsoft.OpenJDK.25", note, StringComparison.Ordinal);
    }

    [Fact]
    public void AnythingElseJavacSaysHasNoNote()
    {
        using var computer = Computer(withJava25: true);
        var main = Program();

        Assert.Null(JavaVersionErrors.NoteFor([CompileError(main, 2, "';' expected")], main));
    }
}

/// <summary>A program built for an older Java than its code is written in, as Settings chooses one, checked as the window checks it.</summary>
[Collection(SharedLanguageStandards.Name)]
public class JavaVersionErrorsLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ACompactSourceFileCompiledForJava21SaysItCameInJava25()
    {
        if (Jdks.AtLeast(25) is null) return;

        using var temp = new TempFolder();
        var hello = Path.Combine(temp.Path, "Hello.java");
        await File.WriteAllTextAsync(hello, "void main() {\n    IO.println(\"Hello\");\n}\n");

        var before = LanguageStandards.Current;
        try
        {
            LanguageStandards.Current = new LanguageStandards { Java = "21" };

            var launch = TargetFactory.FromFile(hello);
            Assert.True(launch.Ok, launch.Problem);
            output.WriteLine($"how: {launch.Explanation}");

            using var http = new FixFinderHttpClient();
            var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Java }.CheckAsync(launch);
            foreach (var note in report.Notes) output.WriteLine($"note: {note}");

            Assert.Contains(report.Notes, note => note.StartsWith(
                "Implicitly declared classes, which Hello.java:1 uses, came in Java 25 - and the program is compiled for Java 21 (compiling it for Java 21 as chosen in Settings).",
                StringComparison.Ordinal));
        }
        finally
        {
            LanguageStandards.Current = before;
        }
    }
}
