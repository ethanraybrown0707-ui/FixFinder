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
        Assert.Equal("Count.java names a variable _ at line 3, which Java 22 made part of the language", needs.AtLeastBecause);
    }

    /// <summary>
    /// Each part of the language or library a Java made final, found in the code that uses it, with the Java that made it
    /// final - JEPs 213, 269, 286, 323, 321, 361, 378, 394, 395, 409, 431, 440, 441, 444, 454, 484, 485, 506, 510, 512, 513
    /// and 517, and javac 21 and 25 compiling each.
    /// </summary>
    [Theory]
    [InlineData("class App { void m() { var items = new java.util.ArrayList<String>(); } }", 10, "declares a local variable with var")]
    [InlineData("class App { java.util.function.BiFunction<Integer, Integer, Integer> add = (var a, var b) -> a + b; }", 11, "uses var in a lambda's parameters")]
    [InlineData("class App { String m(String s) { return s.strip(); } }", 11, "uses String.strip, isBlank or repeat")]
    [InlineData("class App { int m(int day) { return switch (day) { case 1 -> 10; default -> 0; }; } }", 14, "uses a switch with case ... ->")]
    [InlineData("class App { String s = \"\"\"\n    hello\n    \"\"\"; }", 15, "uses a text block")]
    [InlineData("record Point(int x, int y) { }", 16, "declares a record")]
    [InlineData("class App { boolean m(Object o) { return o instanceof String s && s.isEmpty(); } }", 16, "uses a pattern in instanceof")]
    [InlineData("sealed interface Shape permits Circle { }\nfinal class Circle implements Shape { }", 17, "declares a sealed class or interface")]
    [InlineData("class App { String m(Object o) { return switch (o) { case Integer i -> \"int\"; default -> \"other\"; }; } }", 21, "uses a type pattern in a switch")]
    [InlineData("class App { int m(Object o) { if (o instanceof Point(int x, int y)) return x; return 0; } }\nrecord Point(int x, int y) { }", 21, "uses a record pattern")]
    [InlineData("class App { void m() { Thread.ofVirtual().start(() -> { }); } }", 21, "uses virtual threads")]
    [InlineData("class App { int m(int x) { return Math.clamp(x, 0, 10); } }", 21, "uses Math.clamp")]
    [InlineData("import java.lang.foreign.*;\nclass App { }", 22, "uses the foreign function and memory API")]
    [InlineData("import java.util.stream.*;\nclass App { Object m() { return Stream.of(1, 2).gather(Gatherers.windowFixed(2)).toList(); } }", 24, "uses stream gatherers")]
    [InlineData("class App {\n    public static void main(String[] args) {\n        IO.println(\"hi\");\n    }\n}", 25, "uses java.lang.IO")]
    [InlineData("class App { static final ScopedValue<String> NAME = ScopedValue.newInstance(); }", 25, "uses ScopedValue")]
    [InlineData("import java.net.http.*;\nclass App { HttpClient client = HttpClient.newBuilder().version(HttpClient.Version.HTTP_3).build(); }", 26, "uses HTTP/3 in the HTTP client")]
    public void APartOfJavaIsFoundWithTheJavaThatMadeItFinal(string code, int java, string uses)
    {
        var file = Write($@"final{java}-{Guid.NewGuid():N}\App.java", code);

        var needs = JavaFeaturesUsed.For(file);

        Assert.Equal(java, needs.AtLeast);
        Assert.StartsWith($"App.java {uses} at line ", needs.AtLeastBecause, StringComparison.Ordinal);
        Assert.Null(needs.Preview);
    }

    /// <summary>A void main() that is not static is a program's start from Java 25 - the java of 21 says it has no main - but not beside a static one.</summary>
    [Fact]
    public void AMainThatIsNotStaticNeedsJava25UnlessTheProgramHasAStaticOne()
    {
        var alone = Write(@"instance\Game.java", "class Game {\n    void main() {\n        System.out.println(1);\n    }\n}\n");
        var beside = Write(@"both\Game.java", "class Game {\n    void main() { }\n    public static void main(String[] args) { }\n}\n");

        Assert.Equal("Game.java has a main method that is not static at line 2, which Java 25 made a program's start", JavaFeaturesUsed.For(alone).AtLeastBecause);
        Assert.Null(JavaFeaturesUsed.For(beside).AtLeast);
    }

    /// <summary>
    /// What Java 25 only stopped forbidding says nothing of which Java the code is for: before 25 a main(String[] args)
    /// without static, and a statement before super(...), are mistakes, and are left to be checked as mistakes.
    /// </summary>
    [Theory]
    [InlineData("public class Welcome {\n    public void main(String[] args) {\n        System.out.println(\"Hello\");\n    }\n}\n")]
    [InlineData("class Animal { String name; Animal(String name) { this.name = name; } }\nclass Dog extends Animal { String breed; Dog(String name, String breed) { this.breed = breed; super(name); } }\n")]
    public void WhatJava25OnlyStoppedForbiddingIsNotCounted(string code)
    {
        var file = Write($@"relaxed{Guid.NewGuid():N}\App.java", code);

        Assert.Equal(JavaFeaturesUsed.None, JavaFeaturesUsed.For(file));
    }

    /// <summary>What is written in a comment, in quotes or in a text block is not code, and needs nothing - beyond the text block itself.</summary>
    [Fact]
    public void WhatIsInCommentsTextsAndTextBlocksIsNotCode()
    {
        var file = Write(@"quoted\Notes.java", """
            public class Notes {
                // IO.println("hi"); record Point(int x) {}
                /* case Integer i -> sealed interface Shape permits Circle */
                String help = "use IO.println(...) or StableValue";
                String more = '"' + "case null ->";
            }
            """);

        Assert.Equal(JavaFeaturesUsed.None, JavaFeaturesUsed.For(file));

        var block = Write(@"block\Notes.java", "public class Notes {\n    String help = \"\"\"\n        IO.println(\"hi\");\n        x instanceof int i\n        \"\"\";\n}\n");

        var needs = JavaFeaturesUsed.For(block);
        Assert.Equal(15, needs.AtLeast);
        Assert.Null(needs.Preview);
    }

    /// <summary>A class of the program's own called IO, or a List from another library, is not Java's.</summary>
    [Fact]
    public void ATypeOfTheProgramsOwnOrAnotherLibrarysIsNotJavas()
    {
        var own = Write(@"own\App.java", "class IO {\n    static void println(String s) { System.out.println(s); }\n}\nclass App {\n    public static void main(String[] args) { IO.println(\"hi\"); }\n}\n");
        var library = Write(@"library\App.java", "import io.vavr.collection.List;\n\nclass App {\n    Object items = List.of(1, 2);\n}\n");

        Assert.Null(JavaFeaturesUsed.For(own).AtLeast);
        Assert.Null(JavaFeaturesUsed.For(library).AtLeast);
    }

    /// <summary>
    /// A preview feature is found with the Javas that have it as a preview - JEPs 455, 488, 507, 530 and 532 for primitive
    /// patterns, 502 for stable values, 526 and 531 for lazy constants, 453 to 533 for structured concurrency, 430 and 459
    /// for string templates - checked with javac 21 and 25.
    /// </summary>
    [Theory]
    [InlineData("class App { boolean m(int x) { return x instanceof byte b; } }", 23, 27, "uses a primitive type in a pattern")]
    [InlineData("class App { String m(int x) { return switch (x) { case 0 -> \"zero\"; case int i -> \"other\"; }; } }", 23, 27, "uses a primitive type in a pattern")]
    [InlineData("class App { final StableValue<String> name = StableValue.of(); }", 25, 25, "uses StableValue")]
    [InlineData("class App { final LazyConstant<String> name = LazyConstant.of(() -> \"x\"); }", 26, 27, "uses lazy constants")]
    [InlineData("import java.util.*;\nclass App { Set<String> names = Set.ofLazy(Set.of(\"a\"), key -> key); }", 27, 27, "uses Set.ofLazy")]
    [InlineData("import java.util.concurrent.StructuredTaskScope;\nclass App { void m() throws Exception { try (var scope = StructuredTaskScope.open()) { scope.join(); } } }", 25, 27, "uses StructuredTaskScope.open")]
    [InlineData("import java.util.concurrent.StructuredTaskScope;\nclass App { void m() throws Exception { try (var scope = new StructuredTaskScope.ShutdownOnFailure()) { } } }", 21, 24, "uses StructuredTaskScope.ShutdownOnFailure or ShutdownOnSuccess")]
    [InlineData("class App { String m(String name) { return STR.\"Hello \\{name}\"; } }", 21, 22, "uses a string template")]
    public void APreviewIsFoundWithTheJavasThatHaveIt(string code, int from, int until, string uses)
    {
        var file = Write($@"preview{from}-{until}-{Guid.NewGuid():N}\App.java", code);

        var preview = JavaFeaturesUsed.For(file).Preview;

        Assert.NotNull(preview);
        Assert.Equal(from, preview!.From);
        Assert.Equal(until, preview.Until);
        Assert.StartsWith($"App.java {uses} at line ", preview.Because, StringComparison.Ordinal);
    }

    /// <summary>Two previews together need a Java that has them both: lazy constants and primitive patterns, Java 26 and 27.</summary>
    [Fact]
    public void TwoPreviewsNeedAJavaThatHasBoth()
    {
        var file = Write(@"previews\App.java", "class App {\n    final LazyConstant<String> name = LazyConstant.of(() -> \"x\");\n    boolean small(int x) { return x instanceof byte b; }\n}\n");

        var preview = JavaFeaturesUsed.For(file).Preview!;

        Assert.Equal(26, preview.From);
        Assert.Equal(27, preview.Until);
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
