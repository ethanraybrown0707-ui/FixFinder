using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Tests;

/// <summary>
/// What a program's own code needs of the JDK it is built with, read from how it is written - and the java launcher's own
/// complaints about a main that is not static, which came with the same Java.
/// </summary>
public class JavaFeaturesUsedTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static bool Compact(string code) => JavaFeaturesUsed.IsCompactSource(string.Join("\n", CodeText.MaskAll(code.Split('\n'), Syntax.CLike)));

    [Theory]
    [InlineData("void main() {\n    IO.println(\"Hello\");\n}\n")]
    [InlineData("import java.util.List;\n\nint total = 0;\n\nvoid main() {\n}\n")]
    [InlineData("String greet(String name) {\n    return \"Hi \" + name;\n}\n\nvoid main() {\n    IO.println(greet(\"Ada\"));\n}\n")]
    public void MethodsOrFieldsWithNoClassAroundThemAreACompactSourceFile(string code)
    {
        Assert.True(Compact(code));
    }

    [Theory]
    [InlineData("public class Main {\n    public static void main(String[] args) {}\n}\n")]
    [InlineData("package shop;\n\nimport java.util.*;\n\n@SuppressWarnings(\"unused\")\npublic final class Basket {\n}\n")]
    [InlineData("record Point(int x, int y) {}\n\ninterface Shape {}\n\nenum Suit { HEARTS, SPADES }\n")]
    [InlineData("sealed interface Shape permits Circle {}\nfinal class Circle implements Shape {}\n")]
    [InlineData("@interface Marker {}\n")]
    [InlineData("// void main() { } in a comment\n/* int x = 1; */\npublic class Quiet {\n    String text = \"{ int y; }\";\n}\n")]
    [InlineData("@Deprecated\npackage shop.old;\n")]
    [InlineData("module shop {\n    requires java.sql;\n}\n")]
    public void ClassesPackagesAndModulesAreNot(string code)
    {
        Assert.False(Compact(code));
    }

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void AnUnnamedVariableNeedsJava22()
    {
        var file = Write(@"unnamed\Count.java", "public class Count {\n    void count() {\n        try { run(); } catch (Exception _) { }\n    }\n}\n");

        var needs = JavaFeaturesUsed.For(file);

        Assert.Equal(22, needs.AtLeast);
        Assert.Equal("Count.java names a variable _, which Java 22 made part of the language", needs.AtLeastBecause);
    }

    /// <summary>An underscore inside a number, a name, a text or a comment is not the unnamed variable.</summary>
    [Fact]
    public void AnUnderscoreThatIsNotAVariableOfItsOwnNeedsNothing()
    {
        var file = Write(@"underscores\Big.java",
            "public class Big {\n    static final int MAX_SIZE = 1_000_000; // _ is fine here\n    String label = \"_\";\n    int __count;\n}\n");

        Assert.Equal(JavaFeaturesUsed.None, JavaFeaturesUsed.For(file));
    }

    [Fact]
    public void ImportModuleNeedsJava25()
    {
        var file = Write(@"modules\Lists.java", "import module java.base;\n\npublic class Lists {\n}\n");

        Assert.Equal(25, JavaFeaturesUsed.For(file).AtLeast);
    }

    /// <summary>Any file of the program counts, not only the one chosen: javac builds them together.</summary>
    [Fact]
    public void WhatAnotherFileOfTheProgramNeedsCounts()
    {
        var main = Write(@"together\Main.java", "public class Main {\n    public static void main(String[] args) {}\n}\n");
        Write(@"together\Helper.java", "void main() {\n}\n");

        Assert.Equal(25, JavaFeaturesUsed.For(main).AtLeast);
    }

    [Theory]
    [InlineData("import java.applet.Applet;\n\npublic class Clock extends Applet {\n}\n")]
    [InlineData("import javax.swing.JApplet;\n\npublic class Clock extends JApplet {\n}\n")]
    public void AnAppletNeedsJava25OrEarlier(string code)
    {
        var file = Write($@"applet{Guid.NewGuid():N}\Clock.java", code);

        var needs = JavaFeaturesUsed.For(file);

        Assert.Equal(25, needs.AtMost);
        Assert.Equal("Clock.java is an applet, and Java 26 took the Applet API out of Java", needs.AtMostBecause);
    }

    /// <summary>Each of the launcher's messages for a main it cannot call is read as the launcher's, from its launcher.properties of 25 and 27.</summary>
    [Theory]
    [InlineData("Error: no non-private zero argument constructor found in class Game\nremove private from existing constructor or define as:\n   public Game()",
        "no non-private zero argument constructor found in class Game")]
    [InlineData("Error: abstract class Shape can not be instantiated\nplease use a concrete class", "abstract class Shape can not be instantiated")]
    [InlineData("Error: non-static inner class Outer$Program constructor can not be invoked \nmake inner class static or move inner class out to separate source file",
        "non-static inner class Outer$Program constructor can not be invoked")]
    public void TheLaunchersMessagesForAMainThatIsNotStaticAreRead(string printed, string message)
    {
        var lines = printed.Split('\n').Select((text, index) => new CapturedLine(index + 1, StreamKind.StdErr, text, TimeSpan.Zero)).ToList();

        var error = new JavaStackTraceParser().Parse(lines);

        Assert.NotNull(error);
        Assert.Equal(JavaStackTraceParser.LauncherError, error!.ExceptionType);
        Assert.StartsWith(message, error.Message, StringComparison.Ordinal);
    }
}
