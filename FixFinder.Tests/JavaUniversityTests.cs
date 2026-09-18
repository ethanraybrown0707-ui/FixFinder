using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// The Java mistakes of later years - inheritance, interfaces, generics, collections, exceptions - each
/// rule's fix and refusals from javac's own output, then the same programs compiled for real.
/// </summary>
/// <remarks>Live cases skip without Java 11 or later, and a skip looks like a pass.</remarks>
public class JavaUniversityTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string name, string body)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, body);
        return path;
    }

    private static List<CapturedLine> Javac(string file, int line, string message, string echo, string notes)
    {
        var headline = message.StartsWith("cannot find symbol", StringComparison.Ordinal) ? "cannot find symbol" : message;

        var output = new List<CapturedLine>
        {
            new(0, StreamKind.StdErr, $"{Path.GetFileName(file)}:{line}: error: {headline}", TimeSpan.Zero),
            new(1, StreamKind.StdErr, echo, TimeSpan.Zero),
            new(2, StreamKind.StdErr, "^", TimeSpan.Zero),
        };

        foreach (var note in notes.Split('|', StringSplitOptions.RemoveEmptyEntries))
            output.Add(new CapturedLine(output.Count, StreamKind.StdErr, "  " + note, TimeSpan.Zero));

        return output;
    }

    private string? Propose(string rule, ParsedError error, IReadOnlyList<CapturedLine>? output = null)
    {
        var fix = LocalFixEngine.Rules.Single(r => r.Id == rule)
            .Propose(new LocalFixContext { Error = error, Output = output ?? [], SourceRoot = _temp.Path });

        return fix is null ? null : string.Join("|", fix.NewLines);
    }

    private static ParsedError Error(string type, string message, string file, int line) => new()
    {
        LanguageId = "java",
        Confidence = 90,
        RawText = message,
        FirstLineSequence = 0,
        ExceptionType = type,
        Message = message,
        Frames = [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = "" }],
    };

    // ------------------------------------------------------------------ compile errors

    [Theory]
    [InlineData("java-weaker-access", "    double area() {\n", 1, "area() in Circle cannot implement area() in Shape", "attempting to assign weaker access privileges; was public", "    public double area() {")]
    [InlineData("java-weaker-access", "    private double area() {\n", 1, "area() in Circle cannot implement area() in Shape", "attempting to assign weaker access privileges; was public", "    public double area() {")]
    [InlineData("java-weaker-access", "    double area() {\n", 1, "area() in Circle cannot implement area() in Shape", "", null)]
    [InlineData("java-override-typo", "class Animal {\n    String speak() {\n        return \"\";\n    }\n}\n\nclass Dog extends Animal {\n    @Override\n    String speek() {\n        return \"Woof\";\n    }\n}\n", 8, "method does not override or implement a method from a supertype", "", "    String speak() {")]
    [InlineData("java-override-typo", "class Point {\n    @Override\n    public String tostring() {\n        return \"\";\n    }\n}\n", 2, "method does not override or implement a method from a supertype", "", "    public String toString() {")]
    [InlineData("java-override-typo", "class Point {\n    @Override\n    public String describe() {\n        return \"\";\n    }\n}\n", 2, "method does not override or implement a method from a supertype", "", null)]
    [InlineData("java-extends-implements", "class Animal {\n}\n\nclass Dog implements Animal {\n}\n", 4, "interface expected here", "", "class Dog extends Animal {")]
    [InlineData("java-extends-implements", "public class App extends Runnable {\n}\n", 1, "no interface expected here", "", "public class App implements Runnable {")]
    [InlineData("java-extends-implements", "class Dog implements Pet {\n}\n", 1, "interface expected here", "", null)]
    [InlineData("java-super-first", "class Dog extends Animal {\n    Dog(String name, String breed) {\n        this.breed = breed;\n        super(name);\n    }\n}\n", 4, "call to super must be first statement in constructor", "", "        super(name);|        this.breed = breed;")]
    [InlineData("java-constructor-return-type", "class Dog {\n    String name;\n\n    public void Dog(String name) {\n        this.name = name;\n    }\n}\n\npublic class App {\n    public static void main(String[] args) {\n        new Dog(\"Rex\");\n    }\n}\n", 11, "constructor Dog in class Dog cannot be applied to given types;", "", "    public Dog(String name) {")]
    [InlineData("java-illegal-modifier", "private class Helper {\n}\n", 1, "modifier private not allowed here", "", "class Helper {")]
    [InlineData("java-long-literal", "        long big = 3000000000;\n", 1, "integer number too large", "", "        long big = 3000000000L;")]
    [InlineData("java-long-literal", "        long small = 30;\n", 1, "integer number too large", "", null)]
    [InlineData("java-redefinition", "public class App {\n    public static void main(String[] args) {\n        int x = 1;\n        int x = 2;\n    }\n}\n", 4, "variable x is already defined in method main(String[])", "", "        x = 2;")]
    [InlineData("java-generic-array", "class Box<T> {\n    T[] items = new T[10];\n}\n", 2, "generic array creation", "", "    T[] items = (T[]) new Object[10];")]
    [InlineData("java-generic-array", "class Box {\n    Item[] items = new Item[10];\n}\n", 2, "generic array creation", "", null)]
    [InlineData("java-array-stream", "        System.out.println(values.stream().sum());\n", 1, "cannot find symbol (symbol: method stream(), location: variable values of type int[])", "", "        System.out.println(java.util.Arrays.stream(values).sum());")]
    [InlineData("java-array-stream", "import java.util.Arrays;\n        System.out.println(values.stream().sum());\n", 2, "cannot find symbol (symbol: method stream(), location: variable values of type int[])", "", "        System.out.println(Arrays.stream(values).sum());")]
    [InlineData("java-unclosed-string", "        System.out.println(\"hello);\n", 1, "unclosed string literal", "", "        System.out.println(\"hello\");")]
    [InlineData("java-if-semicolon", "        if (x > 3); {\n            a();\n        } else {\n            b();\n        }\n", 3, "'else' without 'if'", "", "        if (x > 3) {")]
    [InlineData("java-catch-order", "        try {\n            f();\n        } catch (Exception e) {\n            a();\n        } catch (NumberFormatException e) {\n            b();\n        }\n", 5, "exception NumberFormatException has already been caught", "", "        } catch (NumberFormatException e) {|            b();|        } catch (Exception e) {|            a();")]
    [InlineData("java-missing-import", "public class App {\n    public static void main(String[] args) {\n        Arrays.sort(values);\n    }\n}\n", 3, "cannot find symbol (symbol: variable Arrays, location: class App)", "", "import java.util.Arrays;")]
    public void EachCompileErrorGetsItsFixOrARefusal(string rule, string source, int line, string message, string notes, string? expected)
    {
        var file = Write("App.java", source);
        var output = Javac(file, line, message, source.Split('\n')[line - 1], notes);

        var proposed = Propose(rule, Error("compile error", message, file, line), output);

        if (expected is null) Assert.Null(proposed);
        else Assert.Contains(expected, proposed ?? "(no fix)", StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ crashes

    [Theory]
    [InlineData("java-remove-in-for-each", "java.util.ConcurrentModificationException", "", "        for (Integer n : numbers) {\n            if (n % 2 == 0) numbers.remove(n);\n        }\n", 1, "        numbers.removeIf(n -> n % 2 == 0);")]
    [InlineData("java-remove-in-for-each", "java.util.ConcurrentModificationException", "", "        for (Integer n : numbers) {\n            total += n;\n            numbers.remove(n);\n        }\n", 1, null)]
    [InlineData("java-split-regex", "java.lang.ArrayIndexOutOfBoundsException", "Index 0 out of bounds for length 0", "public class App {\n    public static void main(String[] args) {\n        String[] parts = version.split(\".\");\n        System.out.println(parts[0]);\n    }\n}\n", 4, "        String[] parts = version.split(\"\\\\.\");")]
    [InlineData("java-split-regex", "java.util.regex.PatternSyntaxException", "Dangling meta character '*' near index 0", "        String[] parts = text.split(\"*\");\n", 1, "        String[] parts = text.split(\"\\\\*\");")]
    public void EachCrashGetsItsFixOrARefusal(string rule, string type, string message, string source, int line, string? expected)
    {
        var file = Write("App.java", source);

        var proposed = Propose(rule, Error(type, message, file, line));

        if (expected is null) Assert.Null(proposed);
        else Assert.Equal(expected, proposed);
    }

    // ------------------------------------------------------------------ live, through javac

    private static string App(string body, string imports = "") =>
        imports + "public class App {\n    public static void main(String[] args) {\n" + body + "    }\n}\n";

    /// <summary>The construct, the program, the answer's id, and text the copied fix must contain.</summary>
    public static TheoryData<string, string, string, string> Constructs => new()
    {
        { "interface method not public", "interface Shape {\n    double area();\n}\n\nclass Circle implements Shape {\n    double area() {\n        return 3.0;\n    }\n}\n\npublic class App {\n    public static void main(String[] args) {\n        Shape s = new Circle();\n        System.out.println(s.area());\n    }\n}\n", "local:java-weaker-access", "public double area() {" },
        { "override typo", "class Animal {\n    String speak() {\n        return \"...\";\n    }\n}\n\nclass Dog extends Animal {\n    @Override\n    String speek() {\n        return \"Woof\";\n    }\n}\n\npublic class App {\n    public static void main(String[] args) {\n        System.out.println(new Dog().speak());\n    }\n}\n", "local:java-override-typo", "String speak() {" },
        { "tostring typo", "class Point {\n    int x = 1;\n\n    @Override\n    public String tostring() {\n        return \"Point \" + x;\n    }\n}\n\npublic class App {\n    public static void main(String[] args) {\n        System.out.println(new Point());\n    }\n}\n", "local:java-override-typo", "public String toString() {" },
        { "implements a class", "class Animal {\n}\n\nclass Dog implements Animal {\n}\n\npublic class App {\n    public static void main(String[] args) {\n        System.out.println(new Dog());\n    }\n}\n", "local:java-extends-implements", "class Dog extends Animal {" },
        { "extends an interface", "public class App extends Runnable {\n    public void run() {\n        System.out.println(\"running\");\n    }\n\n    public static void main(String[] args) {\n        new App().run();\n    }\n}\n", "local:java-extends-implements", "public class App implements Runnable {" },
        { "super not first", "class Animal {\n    String name;\n\n    Animal(String name) {\n        this.name = name;\n    }\n}\n\nclass Dog extends Animal {\n    String breed;\n\n    Dog(String name, String breed) {\n        this.breed = breed;\n        super(name);\n    }\n}\n\npublic class App {\n    public static void main(String[] args) {\n        System.out.println(new Dog(\"Rex\", \"lab\").name);\n    }\n}\n", "local:java-super-first", "        super(name);\n        this.breed = breed;" },
        { "private top-level class", "private class Helper {\n}\n\npublic class App {\n    public static void main(String[] args) {\n        System.out.println(\"hi\");\n    }\n}\n", "local:java-illegal-modifier", "class Helper {" },
        { "integer too large", App("        long big = 3000000000;\n        System.out.println(big);\n"), "local:java-long-literal", "long big = 3000000000L;" },
        { "declared twice", App("        int x = 1;\n        int x = 2;\n        System.out.println(x);\n"), "local:java-redefinition", "        x = 2;" },
        { "generic array", "class Box<T> {\n    T[] items = new T[10];\n}\n\npublic class App {\n    public static void main(String[] args) {\n        System.out.println(new Box<String>().items.length);\n    }\n}\n", "local:java-generic-array", "(T[]) new Object[10]" },
        { "remove in for-each", App("        List<Integer> numbers = new ArrayList<>(List.of(1, 2, 3, 4));\n        for (Integer n : numbers) {\n            if (n % 2 == 0) numbers.remove(n);\n        }\n        System.out.println(numbers);\n", "import java.util.ArrayList;\nimport java.util.List;\n"), "local:java-remove-in-for-each", "numbers.removeIf(n -> n % 2 == 0);" },
        { "stream on an array", App("        int[] values = {1, 2, 3};\n        System.out.println(values.stream().sum());\n"), "local:java-array-stream", "java.util.Arrays.stream(values).sum()" },
        { "unclosed string", App("        System.out.println(\"hello);\n"), "local:java-unclosed-string", "System.out.println(\"hello\");" },
        { "if semicolon else", App("        int x = 5;\n        if (x > 3); {\n            System.out.println(\"big\");\n        } else {\n            System.out.println(\"small\");\n        }\n"), "local:java-if-semicolon", "if (x > 3) {" },
        { "split on a dot", App("        String version = \"1.2.3\";\n        String[] parts = version.split(\".\");\n        System.out.println(parts[0]);\n"), "local:java-split-regex", "version.split(\"\\\\.\");" },
        { "catch order", App("        try {\n            Integer.parseInt(\"x\");\n        } catch (Exception e) {\n            System.out.println(\"something went wrong\");\n        } catch (NumberFormatException e) {\n            System.out.println(\"not a number\");\n        }\n"), "local:java-catch-order", "} catch (NumberFormatException e) {\n            System.out.println(\"not a number\");\n        } catch (Exception e) {" },
        { "void on a constructor", "class Dog {\n    String name;\n\n    public void Dog(String name) {\n        this.name = name;\n    }\n}\n\npublic class App {\n    public static void main(String[] args) {\n        System.out.println(new Dog(\"Rex\").name);\n    }\n}\n", "local:java-constructor-return-type", "public Dog(String name) {" },
        { "Arrays not imported", App("        int[] values = {3, 1, 2};\n        Arrays.sort(values);\n        System.out.println(values[0]);\n"), "local:java-missing-import", "import java.util.Arrays;" },
    };

    [Theory]
    [MemberData(nameof(Constructs))]
    public async Task TheConstructIsFixedLive(string construct, string source, string id, string expected)
    {
        if (!LocalFixLiveTests.Available("java")) return;

        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "App.java");
        File.WriteAllText(path, source);

        var plan = TargetFactory.FromFile(path, TimeSpan.FromMinutes(3));
        Assert.True(plan.Ok, plan.Problem);

        using var http = new FixFinderHttpClient();
        var outcome = await new FixFinderSession(http, new FixSourceRegistry()).RunAsync(plan, new SearchBudget(Cache: CacheMode.CacheOnly));

        if (ApplicationControl.Refused(outcome)) return;
        Assert.True(outcome.Result == SessionResult.FoundFix, $"{construct}: {outcome.Result} - {outcome.Headline}");
        Assert.Equal(id, outcome.Best!.Id);

        var copied = PasteableFix.For(new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));

        Assert.NotNull(copied);
        Assert.Contains(expected, copied!.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }
}
