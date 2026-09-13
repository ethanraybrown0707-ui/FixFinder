using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// The Java mistakes a beginner makes: each rule's fix and refusals from javac's own output - message,
/// echo, caret and notes - then the same programs compiled for real.
/// </summary>
/// <remarks>Live cases skip without Java 11 or later, and a skip looks like a pass.</remarks>
public class JavaConstructTests : IDisposable
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

    /// <summary>What javac prints for one error: the error, the source line, the caret, and any notes.</summary>
    private static (ParsedError Error, List<CapturedLine> Output) Javac(string file, int line, string message, string echo, int caret, string notes)
    {
        var headline = message.StartsWith("cannot find symbol", StringComparison.Ordinal) ? "cannot find symbol" : message;

        var output = new List<CapturedLine>
        {
            new(0, StreamKind.StdErr, $"{Path.GetFileName(file)}:{line}: error: {headline}", TimeSpan.Zero),
            new(1, StreamKind.StdErr, echo, TimeSpan.Zero),
            new(2, StreamKind.StdErr, caret >= 0 ? new string(' ', caret) + "^" : "", TimeSpan.Zero),
        };

        foreach (var note in notes.Split('|', StringSplitOptions.RemoveEmptyEntries))
            output.Add(new CapturedLine(output.Count, StreamKind.StdErr, "  " + note, TimeSpan.Zero));

        var error = new ParsedError
        {
            LanguageId = "java",
            Confidence = 90,
            RawText = output[0].Text,
            FirstLineSequence = 0,
            ExceptionType = "compile error",
            Message = message,
            Frames = [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = "" }],
        };

        return (error, output);
    }

    // ------------------------------------------------------------------ every rule, and what it refuses

    [Theory]
    [InlineData("java-elif", "        } elif (x == 1) {\n", 1, "';' expected", 21, "", "        } else if (x == 1) {")]
    [InlineData("java-for-each", "        foreach (String n : names) {\n", 1, "')' or ',' expected", 25, "", "        for (String n : names) {")]
    [InlineData("java-for-each", "        for (String n in names) {\n", 1, "';' expected", 21, "", "        for (String n : names) {")]
    [InlineData("java-for-each", "        foreach (var n in names) {\n", 1, "';' expected", 22, "", "        for (var n : names) {")]
    [InlineData("java-foreign-word", "        boolean ready = True;\n", 1, "cannot find symbol (symbol: variable True, location: class App)", 24, "", "        boolean ready = true;")]
    [InlineData("java-foreign-word", "        bool ready = true;\n", 1, "cannot find symbol (symbol: class bool, location: class App)", 8, "", "        boolean ready = true;")]
    [InlineData("java-foreign-word", "        Console.WriteLine(\"hi\");\n", 1, "cannot find symbol (symbol: variable Console, location: class App)", 8, "", "        System.out.println(\"hi\");")]
    [InlineData("java-foreign-word", "        print(\"hi\");\n", 1, "cannot find symbol (symbol: method print(String), location: class App)", 8, "", "        System.out.println(\"hi\");")]
    [InlineData("java-foreign-word", "    static void print(int x) { }\n        print(\"hi\");\n", 2, "cannot find symbol (symbol: method print(String), location: class App)", 8, "", null)]
    [InlineData("java-lowercase-class", "        system.out.println(\"hi\");\n", 1, "package system does not exist", 14, "", "        System.out.println(\"hi\");")]
    [InlineData("java-lowercase-class", "        foo.bar();\n", 1, "package foo does not exist", 11, "", null)]
    [InlineData("java-length-size", "        System.out.println(values.length());\n", 1, "cannot find symbol (symbol: method length(), location: variable values of type int[])", 33, "", "        System.out.println(values.length);")]
    [InlineData("java-length-size", "        System.out.println(values.size());\n", 1, "cannot find symbol (symbol: method size(), location: variable values of type int[])", 33, "", "        System.out.println(values.length);")]
    [InlineData("java-length-size", "        System.out.println(name.length);\n", 1, "cannot find symbol (symbol: variable length, location: variable name of type String)", 31, "", "        System.out.println(name.length());")]
    [InlineData("java-length-size", "        System.out.println(names.length);\n", 1, "cannot find symbol (symbol: variable length, location: variable names of type List<String>)", 32, "", "        System.out.println(names.size());")]
    [InlineData("java-indexing", "        System.out.println(names[0]);\n", 1, "array required, but List<String> found", 32, "", "        System.out.println(names.get(0));")]
    [InlineData("java-indexing", "        char first = name[0];\n", 1, "array required, but String found", 25, "", "        char first = name.charAt(0);")]
    [InlineData("java-indexing", "        names[0] = \"x\";\n", 1, "array required, but List<String> found", 13, "", null)]
    [InlineData("java-missing-new", "        ArrayList<String> names = ArrayList<>();\n", 1, "illegal start of expression", 45, "", "        ArrayList<String> names = new ArrayList<>();")]
    [InlineData("java-missing-new", "class Dog { }\npublic class App {\n    public static void main(String[] args) {\n        Dog d = Dog();\n    }\n}\n", 4, "cannot find symbol (symbol: method Dog(), location: class App)", 16, "", "        Dog d = new Dog();")]
    [InlineData("java-char-string", "        String greeting = 'hello';\n", 1, "unclosed character literal", 26, "", "        String greeting = \"hello\";")]
    [InlineData("java-char-string", "        char letter = \"a\";\n", 1, "incompatible types: String cannot be converted to char", 22, "", "        char letter = 'a';")]
    [InlineData("java-char-string", "        if (word.charAt(0) == \"a\") {\n", 1, "bad operand types for binary operator '=='", 27, "first type:  char|second type: String", "        if (word.charAt(0) == 'a') {")]
    [InlineData("java-char-string", "        if (word.charAt(0) == \"ab\") {\n", 1, "bad operand types for binary operator '=='", 27, "first type:  char|second type: String", null)]
    [InlineData("java-missing-closing-brace", "public class App {\n    public static void main(String[] args) {\n    }\n", 3, "reached end of file while parsing", 5, "", "}")]
    [InlineData("java-primitive-method", "        if (x.equals(5)) {\n", 1, "int cannot be dereferenced", 13, "", "        if (x == 5) {")]
    [InlineData("java-primitive-method", "        if (!x.equals(5)) {\n", 1, "int cannot be dereferenced", 14, "", "        if (x != 5) {")]
    [InlineData("java-primitive-method", "        int y = x.equals(5) + 1;\n", 1, "int cannot be dereferenced", 17, "", null)]
    [InlineData("java-assignment-in-condition", "        if (x = 5) {\n", 1, "incompatible types: int cannot be converted to boolean", 16, "", "        if (x == 5) {")]
    [InlineData("java-for-counter", "public class App {\n    public static void main(String[] args) {\n        for (i = 0; i < 3; i++) {\n            System.out.println(i);\n        }\n    }\n}\n", 3, "cannot find symbol (symbol: variable i, location: class App)", 13, "", "        for (int i = 0; i < 3; i++) {")]
    [InlineData("java-for-counter", "public class App {\n    public static void main(String[] args) {\n        for (i = 0; i < 3; i++) {\n        }\n        System.out.println(i);\n    }\n}\n", 3, "cannot find symbol (symbol: variable i, location: class App)", 13, "", null)]
    [InlineData("java-generic-primitive", "        ArrayList<int> numbers = new ArrayList<>();\n", 1, "unexpected type", 18, "required: reference|found:    int", "        ArrayList<Integer> numbers = new ArrayList<>();")]
    [InlineData("java-generic-primitive", "        Map<String, double> prices = new HashMap<>();\n", 1, "unexpected type", 20, "required: reference|found:    double", "        Map<String, Double> prices = new HashMap<>();")]
    [InlineData("java-cast", "        int x = 5.5;\n", 1, "incompatible types: possible lossy conversion from double to int", 16, "", "        int x = (int) 5.5;")]
    [InlineData("java-cast", "        String text = value;\n", 1, "incompatible types: Object cannot be converted to String", 22, "", "        String text = (String) value;")]
    [InlineData("java-string-arithmetic", "        int b = a - 1;\n", 1, "bad operand types for binary operator '-'", 18, "first type:  String|second type: int", "        int b = Integer.parseInt(a) - 1;")]
    [InlineData("java-string-arithmetic", "        int b = \"10\" - 1;\n", 1, "bad operand types for binary operator '-'", 21, "first type:  String|second type: int", "        int b = 10 - 1;")]
    [InlineData("java-uninitialised", "        int total;\n        System.out.println(total);\n", 2, "variable total might not have been initialized", 27, "", "        int total = 0;")]
    [InlineData("java-uninitialised", "        String name;\n        System.out.println(name);\n", 2, "variable name might not have been initialized", 27, "", null)]
    public void EachConstructGetsItsFixOrARefusal(string rule, string source, int line, string message, int caret, string notes, string? expected)
    {
        var file = Write("App.java", source);
        var echo = source.Split('\n')[line - 1];
        var (error, output) = Javac(file, line, message, echo, caret, notes);

        var fix = LocalFixEngine.Rules.Single(r => r.Id == rule)
            .Propose(new LocalFixContext { Error = error, Output = output, SourceRoot = _temp.Path });

        Assert.Equal(expected, fix is null ? null : string.Join("|", fix.NewLines));
    }

    /// <summary>A Java frame names a file, never a path, so the JDK's own frames are known by their class.</summary>
    [Fact]
    public void TheCulpritOfAJavaCrashIsTheUsersFrameNotTheJdks()
    {
        var error = new ParsedError
        {
            LanguageId = "java",
            Confidence = 90,
            RawText = "java.lang.IndexOutOfBoundsException: Index 3 out of bounds for length 3",
            FirstLineSequence = 0,
            ExceptionType = "java.lang.IndexOutOfBoundsException",
            Message = "Index 3 out of bounds for length 3",
            Frames =
            [
                new ErrorFrame { Order = 0, Symbol = "java.base/jdk.internal.util.Preconditions.outOfBounds", File = "Preconditions.java", Line = 100, RawLine = "" },
                new ErrorFrame { Order = 1, Symbol = "java.base/java.util.ArrayList.get", File = "ArrayList.java", Line = 427, RawLine = "" },
                new ErrorFrame { Order = 2, Symbol = "App.main", File = "App.java", Line = 7, RawLine = "" },
            ],
        };

        var culprit = CulpritFrameSelector.Select(error, []);

        Assert.Equal("App.java", culprit?.File);
        Assert.Equal(FrameOrigin.Runtime, error.Frames[0].Origin);
        Assert.Equal(FrameOrigin.Runtime, error.Frames[1].Origin);
    }

    // ------------------------------------------------------------------ live, through javac

    private static string App(string body, string imports = "") =>
        imports + "public class App {\n    public static void main(String[] args) {\n" + body + "    }\n}\n";

    /// <summary>The construct, the program, the answer's id, and text the copied fix must contain.</summary>
    public static TheoryData<string, string, string, string> Constructs => new()
    {
        { "bool", App("        bool ready = true;\n        System.out.println(ready);\n"), "local:java-foreign-word", "boolean ready = true;" },
        { "lowercase system", App("        system.out.println(\"hi\");\n"), "local:java-lowercase-class", "System.out.println(\"hi\");" },
        { "array length()", App("        int[] values = {1, 2, 3};\n        System.out.println(values.length());\n"), "local:java-length-size", "System.out.println(values.length);" },
        { "array size()", App("        int[] values = {1, 2, 3};\n        System.out.println(values.size());\n"), "local:java-length-size", "System.out.println(values.length);" },
        { "String length", App("        String name = \"Ethan\";\n        System.out.println(name.length);\n"), "local:java-length-size", "System.out.println(name.length());" },
        { "List length", App("        List<String> names = new ArrayList<>();\n        System.out.println(names.length);\n", "import java.util.ArrayList;\nimport java.util.List;\n"), "local:java-length-size", "System.out.println(names.size());" },
        { "method without brackets", App("        String name = \"ethan\";\n        System.out.println(name.toUpperCase);\n"), "local:java-length-size", "System.out.println(name.toUpperCase());" },
        { "List with brackets", App("        List<String> names = new ArrayList<>();\n        names.add(\"sam\");\n        System.out.println(names[0]);\n", "import java.util.ArrayList;\nimport java.util.List;\n"), "local:java-indexing", "System.out.println(names.get(0));" },
        { "String with brackets", App("        String name = \"Ethan\";\n        System.out.println(name[0]);\n"), "local:java-indexing", "System.out.println(name.charAt(0));" },
        { "missing new", App("        ArrayList<String> names = ArrayList<>();\n        System.out.println(names);\n", "import java.util.ArrayList;\n"), "local:java-missing-new", "ArrayList<String> names = new ArrayList<>();" },
        { "single-quoted text", App("        String greeting = 'hello';\n        System.out.println(greeting);\n"), "local:java-char-string", "String greeting = \"hello\";" },
        { "String to char", App("        char letter = \"a\";\n        System.out.println(letter);\n"), "local:java-char-string", "char letter = 'a';" },
        { "char compared with String", App("        String word = \"apple\";\n        if (word.charAt(0) == \"a\") {\n            System.out.println(\"a\");\n        }\n"), "local:java-char-string", "if (word.charAt(0) == 'a') {" },
        { "elif", App("        int x = 1;\n        if (x > 1) {\n            System.out.println(\"big\");\n        } elif (x == 1) {\n            System.out.println(\"one\");\n        }\n"), "local:java-elif", "} else if (x == 1) {" },
        { "python print", App("        print(\"hi\");\n"), "local:java-foreign-word", "System.out.println(\"hi\");" },
        { "C# Console", App("        Console.WriteLine(\"hi\");\n"), "local:java-foreign-word", "System.out.println(\"hi\");" },
        { "python True", App("        boolean ready = True;\n        System.out.println(ready);\n"), "local:java-foreign-word", "boolean ready = true;" },
        { "python None", App("        String name = None;\n        System.out.println(name);\n"), "local:java-foreign-word", "String name = null;" },
        { "equals on an int", App("        int x = 5;\n        if (x.equals(5)) {\n            System.out.println(\"five\");\n        }\n"), "local:java-primitive-method", "if (x == 5) {" },
        { "assignment in a condition", App("        int x = 3;\n        if (x = 5) {\n            System.out.println(x);\n        }\n"), "local:java-assignment-in-condition", "if (x == 5) {" },
        { "undeclared loop counter", App("        for (i = 0; i < 3; i++) {\n            System.out.println(i);\n        }\n"), "local:java-for-counter", "for (int i = 0; i < 3; i++) {" },
        { "C# foreach", App("        String[] names = {\"a\", \"b\"};\n        foreach (String n : names) {\n            System.out.println(n);\n        }\n"), "local:java-for-each", "for (String n : names) {" },
        { "for in", App("        String[] names = {\"a\", \"b\"};\n        for (String n in names) {\n            System.out.println(n);\n        }\n"), "local:java-for-each", "for (String n : names) {" },
        { "Object to String", App("        Object value = \"text\";\n        String text = value;\n        System.out.println(text);\n"), "local:java-cast", "String text = (String) value;" },
        { "double to int", App("        int x = 5.5;\n        System.out.println(x);\n"), "local:java-cast", "int x = (int) 5.5;" },
        { "primitive in a generic", App("        ArrayList<int> numbers = new ArrayList<>();\n        numbers.add(1);\n        System.out.println(numbers);\n", "import java.util.ArrayList;\n"), "local:java-generic-primitive", "ArrayList<Integer> numbers = new ArrayList<>();" },
        { "String minus a number", App("        String a = \"10\";\n        int b = a - 1;\n        System.out.println(b);\n"), "local:java-string-arithmetic", "int b = Integer.parseInt(a) - 1;" },
        { "uninitialised local", App("        int total;\n        System.out.println(total);\n"), "local:java-uninitialised", "int total = 0;" },
        { "missing closing brace", "public class App {\n    public static void main(String[] args) {\n        System.out.println(\"hi\");\n    }\n", "local:java-missing-closing-brace", "    }\n}" },
        { "List loop past the end", App("        List<Integer> items = new ArrayList<>(List.of(1, 2, 3));\n        for (int i = 0; i <= items.size(); i++) {\n            System.out.println(items.get(i));\n        }\n", "import java.util.ArrayList;\nimport java.util.List;\n"), "local:java-off-by-one-loop", "for (int i = 0; i < items.size(); i++) {" },
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

        Assert.True(outcome.Result == SessionResult.FoundFix, $"{construct}: {outcome.Result} - {outcome.Headline}");
        Assert.Equal(id, outcome.Best!.Id);

        var copied = PasteableFix.For(new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));

        Assert.NotNull(copied);
        Assert.Contains(expected, copied!.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }
}
