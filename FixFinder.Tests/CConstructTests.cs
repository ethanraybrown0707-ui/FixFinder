using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// The C mistakes neither compiler fixes by itself: each rule's fix and refusals from MSVC's, gcc's and
/// AddressSanitizer's own words, then the same programs built for real with gcc and with MSVC.
/// </summary>
/// <remarks>
/// Live cases skip when their toolchain is missing, and a skip looks like a pass - an MSVC case that
/// really ran takes seconds. MSVC cases hide gcc for their own flow, since the build prefers gcc.
/// </remarks>
public class CConstructTests : IDisposable
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

    private static ParsedError Diagnostic(string language, string? code, string message, string file, int line, string type) => new()
    {
        LanguageId = language,
        Confidence = 90,
        RawText = message,
        FirstLineSequence = 0,
        ExceptionType = type,
        ErrorCode = code,
        Message = message,
        Frames = [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = "" }],
    };

    private LocalFix? Fix(string rule, ParsedError error) =>
        LocalFixEngine.Rules.Single(r => r.Id == rule).Propose(new LocalFixContext { Error = error, SourceRoot = _temp.Path });

    // ------------------------------------------------------------------ every rule, and what it refuses

    [Theory]
    [InlineData("c-elif", "app.c", "msvc", "C2143", "syntax error: missing ';' before '{'", "    } elif (x == 1) {\n", 1, "compile error", "    } else if (x == 1) {")]
    [InlineData("c-word-operators", "app.c", "msvc", "C2146", "syntax error: missing ')' before identifier 'and'", "    if (x > 1 and x < 10 or not done) {\n", 1, "compile error", "    if (x > 1 && x < 10 || !done) {")]
    [InlineData("c-word-operators", "app.cpp", "gcc", null, "expected ')' before 'and'", "    if (x > 1 and x < 10) {\n", 1, "compile error", null)]
    [InlineData("c-condition-parentheses", "app.c", "msvc", "C2061", "syntax error: identifier 'x'", "    if x > 1 {\n", 1, "compile error", "    if (x > 1) {")]
    [InlineData("c-condition-parentheses", "app.c", "gcc", null, "expected '(' before 'x'", "    while x < 3 {\n", 1, "compile error", "    while (x < 3) {")]
    [InlineData("c-unterminated-string", "app.c", "msvc", "C2001", "newline in string literal", "    printf(\"hello);\n", 1, "compile error", "    printf(\"hello\");")]
    [InlineData("c-unterminated-string", "app.c", "gcc", null, "missing terminating \" character", "    char *s = \"hi;\n", 1, "compile error", "    char *s = \"hi\";")]
    [InlineData("c-unterminated-string", "app.c", "msvc", "C2001", "newline in string literal", "    puts(\"hi\n", 1, "compile error", null)]
    [InlineData("c-missing-closing-parenthesis", "app.c", "msvc", "C2143", "syntax error: missing ')' before ';'", "    printf(\"hi\";\n", 1, "compile error", "    printf(\"hi\");")]
    [InlineData("c-extra-closing-brace", "app.c", "msvc", "C2059", "syntax error: '}'", "int main(void) {\n    return 0;\n}\n}\n", 4, "compile error", "")]
    [InlineData("c-extra-closing-brace", "app.c", "gcc", null, "expected identifier or '(' before '}' token", "int main(void) {\n    return 0; }}\n", 2, "compile error", null)]
    [InlineData("c-string-type", "app.c", "msvc", "C2065", "'string': undeclared identifier", "    string name = \"Ethan\";\n", 1, "compile error", "    char name[] = \"Ethan\";")]
    [InlineData("c-string-type", "app.c", "gcc", null, "unknown type name 'string'; did you mean 'stdin'?", "    string label = get_label();\n", 1, "compile error", "    const char *label = get_label();")]
    [InlineData("c-string-type", "app.cpp", "msvc", "C2065", "'string': undeclared identifier", "    string name = \"Ethan\";\n", 1, "compile error", null)]
    [InlineData("c-struct-keyword", "app.c", "msvc", "C2065", "'parcel': undeclared identifier", "struct parcel { int weight; };\nint main(void) {\n    parcel p = { 4 };\n", 3, "compile error", "    struct parcel p = { 4 };")]
    [InlineData("c-struct-keyword", "app.c", "msvc", "C2065", "'parcel': undeclared identifier", "typedef struct parcel { int weight; } parcel;\nint main(void) {\n    parcel p = { 4 };\n", 3, "compile error", null)]
    [InlineData("c-member-operator", "app.c", "msvc", "C2232", "'->weight': left operand has 'struct' type, use '.'", "    return p->weight;\n", 1, "compile error", "    return p.weight;")]
    [InlineData("c-member-operator", "app.c", "msvc", "C2231", "'.weight': left operand points to 'struct', use '->'", "    return p.weight;\n", 1, "compile error", "    return p->weight;")]
    [InlineData("c-member-operator", "app.c", "gcc", null, "invalid type argument of '->' (have 'struct parcel')", "    return p->weight;\n", 1, "compile error", "    return p.weight;")]
    [InlineData("c-member-operator", "app.c", "gcc", null, "invalid type argument of '->' (have 'struct parcel')", "    return a->x + b->y;\n", 1, "compile error", null)]
    [InlineData("c-for-counter", "app.c", "msvc", "C2065", "'i': undeclared identifier", "int main(void) {\n    for (i = 0; i < 3; i++) {\n        printf(\"%d\", i);\n    }\n    return 0;\n}\n", 2, "compile error", "    for (int i = 0; i < 3; i++) {")]
    [InlineData("c-for-counter", "app.c", "gcc", null, "'i' undeclared (first use in this function)", "int main(void) {\n    for (i = 0; i < 3; i++) {\n    }\n    return i;\n}\n", 2, "compile error", null)]
    [InlineData("c-function-prototype", "app.c", "msvc", "C2371", "'half': redefinition; different basic types", "#include <stdio.h>\nint main(void) {\n    printf(\"%f\", half(3.0));\n    return 0;\n}\ndouble half(double x) {\n    return x / 2;\n}\n", 6, "compile error", "double half(double x);")]
    [InlineData("c-function-prototype", "app.c", "gcc", null, "implicit declaration of function 'half' [-Wimplicit-function-declaration]", "#include <stdio.h>\nint main(void) {\n    printf(\"%f\", half(3.0));\n    return 0;\n}\ndouble half(double x) {\n    return x / 2;\n}\n", 3, "compile error", "double half(double x);")]
    [InlineData("c-array-assign-string", "app.c", "msvc", "C2106", "'=': left operand must be l-value", "#include <string.h>\nint main(void) {\n    char name[10];\n    name = \"Ethan\";\n", 4, "compile error", "    strcpy(name, \"Ethan\");")]
    [InlineData("c-array-assign-string", "app.c", "gcc", null, "assignment to expression with array type", "#include <string.h>\nint main(void) {\n    char name[4];\n    name = \"Ethan\";\n", 4, "compile error", null)]
    [InlineData("c-array-assign-string", "app.c", "gcc", null, "assignment to expression with array type", "int main(void) {\n    char name[10];\n    name = \"Ethan\";\n", 3, "compile error", null)]
    [InlineData("c-redefinition", "app.c", "msvc", "C2374", "'x': redefinition; multiple initialization", "int main(void) {\n    int x = 1;\n    int x = 2;\n", 3, "compile error", "    x = 2;")]
    [InlineData("c-redefinition", "app.c", "gcc", null, "redefinition of 'x'", "int main(void) {\n    int x = 1;\n    int x = 2;\n", 3, "compile error", "    x = 2;")]
    [InlineData("c-iostream-in-c", "app.c", "gcc", null, "iostream: No such file or directory", "#include <iostream>\nint main(void) { return 0; }\n", 1, "compile error", "#include <stdio.h>")]
    [InlineData("c-iostream-in-c", "app.c", "gcc", null, "iostream: No such file or directory", "#include <stdio.h>\n#include <iostream>\n", 2, "compile error", "")]
    [InlineData("c-cout-in-c", "app.c", "msvc", "C2065", "'cout': undeclared identifier", "    cout << \"Total: \" << endl;\n", 1, "compile error", "    printf(\"Total: \\n\");")]
    [InlineData("c-cout-in-c", "app.c", "msvc", "C2065", "'cout': undeclared identifier", "    cout << total;\n", 1, "compile error", null)]
    [InlineData("c-double-free", "app.c", "gcc", null, "attempting double-free on 0x1273b0ba0130 in thread T0:", "int main(void) {\n    int *p = malloc(4);\n    free(p);\n    free(p);\n    return 0;\n}\n", 4, "attempting double-free", "")]
    [InlineData("c-double-free", "app.c", "gcc", null, "attempting double-free on 0x1273b0ba0130 in thread T0:", "int main(void) {\n    int *p = malloc(4);\n    free(p);\n    p = malloc(4);\n    free(p);\n    return 0;\n}\n", 5, "attempting double-free", null)]
    [InlineData("c-array-bound-loop", "app.c", "gcc", null, "stack-buffer-overflow on address 0x00ad0acff76c", "int main(void) {\n    int values[3];\n    for (int i = 0; i <= 3; i++) values[i] = i;\n    return 0;\n}\n", 3, "stack-buffer-overflow", "    for (int i = 0; i < 3; i++) values[i] = i;")]
    [InlineData("c-array-bound-loop", "app.c", "gcc", null, "stack-buffer-overflow on address 0x00ad0acff76c", "int main(void) {\n    int values[3];\n    for (int i = 0; i < 3; i++) values[i] = i;\n    return 0;\n}\n", 3, "stack-buffer-overflow", null)]
    [InlineData("c-format-specifier", "app.c", "msvc", "C4477", "'printf' : format string '%s' requires an argument of type 'char *', but variadic argument 1 has type 'int'", "    printf(\"%s\\n\", 5);\n", 1, "compile warning", "    printf(\"%d\\n\", 5);")]
    [InlineData("c-format-specifier", "app.c", "msvc", "C4477", "'printf' : format string '%d' requires an argument of type 'int', but variadic argument 2 has type 'char *'", "    printf(\"%s is %d\", name, name);\n", 1, "compile warning", "    printf(\"%s is %s\", name, name);")]
    [InlineData("c-format-specifier", "app.c", "msvc", "C4477", "'printf' : format string '%s' requires an argument of type 'char *', but variadic argument 2 has type 'int'", "    printf(\"%*s\", 5, 7);\n", 1, "compile warning", null)]
    [InlineData("c-struct-semicolon", "app.c", "msvc", "C2628", "'parcel' followed by 'int' is illegal (did you forget a ';'?)", "struct parcel { int weight; }\nint main(void) {\n", 2, "compile error", "struct parcel { int weight; };")]
    [InlineData("c-struct-semicolon", "app.c", "gcc", null, "expected ';', identifier or '(' before 'int'", "struct parcel { int weight; }\nint main(void) {\n", 2, "compile error", "struct parcel { int weight; };")]
    [InlineData("c-struct-semicolon", "app.c", "gcc", null, "expected ';', identifier or '(' before 'int'", "int f(void) { return 1; }\nint main(void) {\n", 2, "compile error", null)]
    [InlineData("c-nearest-name", "app.c", "msvc", "C2039", "'wieght': is not a member of 'Parcel'", "typedef struct { int weight; } Parcel;\nint main(void) {\n    Parcel p = { 4 };\n    return p.wieght;\n}\n", 4, "compile error", "    return p.weight;")]
    public void EachConstructGetsItsFixOrARefusal(
        string rule, string fileName, string language, string? code, string message, string source, int line, string type, string? expected)
    {
        var file = Write(fileName, source);
        var fix = Fix(rule, Diagnostic(language, code, message, file, line, type));

        Assert.Equal(expected, fix is null ? null : string.Join("|", fix.NewLines));
    }

    /// <summary>MSVC reports &lt;iostream&gt; in C from inside its own header, so the C file is found in the project instead.</summary>
    [Fact]
    public void IostreamInCIsFoundInTheProjectWhenMsvcReportsItFromItsOwnHeader()
    {
        var app = Write("app.c", "#include <iostream>\nint main(void) { return 0; }\n");
        const string header = @"C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.51.36231\include\yvals_core.h";

        var fix = Fix("c-iostream-in-c", Diagnostic("msvc", "C1189", "#error:  error STL1003: Unexpected compiler, expected C++ compiler.", header, 23, "compile error"));

        Assert.NotNull(fix);
        Assert.Equal(Path.GetFullPath(app), Path.GetFullPath(fix!.File), ignoreCase: true);
        Assert.Equal(["#include <stdio.h>"], fix.NewLines);
    }

    /// <summary>A header inside the compiler's install is never the project - or the folder a fix may be written to.</summary>
    [Theory]
    [InlineData(@"C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.51.36231\include\yvals_core.h")]
    [InlineData(@"C:\Program Files (x86)\Windows Kits\10\include\10.0.26100.0\ucrt\stdio.h")]
    public void ToolchainHeadersAreNotTheUsersCode(string header)
    {
        Assert.True(FrameClassifier.IsVendored(header));
    }

    /// <summary>gcc's warnings are read the way its errors are, for the ones that explain a crash.</summary>
    [Fact]
    public void GccWarningsAreParsedWithTheirPlace()
    {
        var lines = new[]
        {
            new CapturedLine(0, StreamKind.StdErr, "app.c: In function 'main':", TimeSpan.Zero),
            new CapturedLine(1, StreamKind.StdErr, "app.c:3:14: warning: format '%s' expects argument of type 'char *', but argument 2 has type 'int' [-Wformat=]", TimeSpan.Zero),
            new CapturedLine(2, StreamKind.StdErr, "app.c:5:1: error: expected ';' before '}' token", TimeSpan.Zero),
        };

        var warning = Assert.Single(GccClangParser.ParseWarnings(lines));

        Assert.Equal("compile warning", warning.ExceptionType);
        Assert.StartsWith("format '%s' expects", warning.Message, StringComparison.Ordinal);
        Assert.Equal(1, warning.FirstLineSequence);
        Assert.Equal(3, warning.Frames[0].Line);
        Assert.Equal(14, warning.Frames[0].Column);
    }

    // ------------------------------------------------------------------ live, through gcc and MSVC

    /// <summary>The toolchain, the construct, the program, the rule, text the copied fix must contain, and text it must not.</summary>
    public static TheoryData<string, string, string, string, string, string?> Constructs
    {
        get
        {
            var programs = new (string Construct, string Source, string Rule, string Expected, string? Absent)[]
            {
                ("elif", "#include <stdio.h>\nint main(void) {\n    int x = 1;\n    if (x > 1) {\n        printf(\"big\\n\");\n    } elif (x == 1) {\n        printf(\"one\\n\");\n    }\n    return 0;\n}\n", "c-elif", "} else if (x == 1) {", null),
                ("and and or", "#include <stdio.h>\nint main(void) {\n    int x = 5;\n    if (x > 1 and x < 10) {\n        printf(\"%d\\n\", x);\n    }\n    return 0;\n}\n", "c-word-operators", "if (x > 1 && x < 10) {", null),
                ("if without brackets", "#include <stdio.h>\nint main(void) {\n    int x = 3;\n    if x > 1 {\n        printf(\"big\\n\");\n    }\n    return 0;\n}\n", "c-condition-parentheses", "if (x > 1) {", null),
                ("unclosed string", "#include <stdio.h>\nint main(void) {\n    printf(\"hello);\n    return 0;\n}\n", "c-unterminated-string", "printf(\"hello\");", null),
                ("missing closing bracket", "#include <stdio.h>\nint main(void) {\n    printf(\"hi\\n\";\n    return 0;\n}\n", "c-missing-closing-parenthesis", "printf(\"hi\\n\");", null),
                ("extra closing brace", "#include <stdio.h>\nint main(void) {\n    printf(\"hi\\n\");\n    return 0;\n}\n}\n", "c-extra-closing-brace", "}", "}\n}"),
                ("string type", "#include <stdio.h>\nint main(void) {\n    string name = \"Ethan\";\n    printf(\"%s\\n\", name);\n    return 0;\n}\n", "c-string-type", "char name[] = \"Ethan\";", null),
                ("struct without its keyword", "struct parcel { int weight; };\nint main(void) {\n    parcel p = { 4 };\n    return p.weight;\n}\n", "c-struct-keyword", "struct parcel p = { 4 };", null),
                ("arrow on a struct", "struct parcel { int weight; };\nint main(void) {\n    struct parcel p = { 4 };\n    return p->weight;\n}\n", "c-member-operator", "return p.weight;", null),
                ("undeclared loop counter", "#include <stdio.h>\nint main(void) {\n    for (i = 0; i < 3; i++) {\n        printf(\"%d\\n\", i);\n    }\n    return 0;\n}\n", "c-for-counter", "for (int i = 0; i < 3; i++) {", null),
                ("function used before it is defined", "#include <stdio.h>\nint main(void) {\n    printf(\"%f\\n\", half(3.0));\n    return 0;\n}\ndouble half(double x) {\n    return x / 2;\n}\n", "c-function-prototype", "double half(double x);", null),
                ("assigning a string to an array", "#include <stdio.h>\n#include <string.h>\nint main(void) {\n    char name[10];\n    name = \"Ethan\";\n    printf(\"%s\\n\", name);\n    return 0;\n}\n", "c-array-assign-string", "strcpy(name, \"Ethan\");", null),
                ("declared twice", "int main(void) {\n    int x = 1;\n    int x = 2;\n    return x;\n}\n", "c-redefinition", "    x = 2;", "int x = 2;"),
                ("iostream in C", "#include <iostream>\nint main(void) {\n    printf(\"hi\\n\");\n    return 0;\n}\n", "c-iostream-in-c", "#include <stdio.h>", "iostream"),
                ("cout in C", "#include <stdio.h>\nint main(void) {\n    cout << \"hi\" << endl;\n    return 0;\n}\n", "c-cout-in-c", "printf(\"hi\\n\");", null),
                ("struct without its semicolon", "struct parcel { int weight; }\nint main(void) {\n    struct parcel p = { 4 };\n    return p.weight;\n}\n", "c-struct-semicolon", "struct parcel { int weight; };", null),
                ("typedef member typo", "#include <stdio.h>\ntypedef struct { int weight; } Parcel;\nint main(void) {\n    Parcel p = { 4 };\n    printf(\"%d\\n\", p.wieght);\n    return 0;\n}\n", "c-nearest-name", "p.weight", null),
                ("%s given an int", "#include <stdio.h>\nint main(void) {\n    printf(\"%s\\n\", 5);\n    return 0;\n}\n", "c-format-specifier", "printf(\"%d\\n\", 5);", null),
            };

            var data = new TheoryData<string, string, string, string, string, string?>();

            foreach (var toolchain in new[] { "gcc", "msvc" })
            foreach (var (construct, source, rule, expected, absent) in programs)
                data.Add(toolchain, construct, source, rule, expected, absent);

            // Found by AddressSanitizer, which is always MSVC's, whichever compiler built the program.
            data.Add("asan", "double free", "#include <stdlib.h>\nint main(void) {\n    int *p = malloc(sizeof(int));\n    free(p);\n    free(p);\n    return 0;\n}\n", "c-double-free", "free(p);\n    return 0;", "free(p);\n    free(p);");
            data.Add("asan", "loop past the end of an array", "#include <stdio.h>\nint main(void) {\n    int values[3];\n    for (int i = 0; i <= 100000; i++) values[i] = i;\n    printf(\"%d\\n\", values[0]);\n    return 0;\n}\n", "c-array-bound-loop", "for (int i = 0; i < 3; i++)", null);

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Constructs))]
    public async Task TheConstructIsFixedByARealBuild(string toolchain, string construct, string source, string rule, string expected, string? absent)
    {
        var ready = toolchain switch
        {
            "gcc" => Toolchains.FindGnu(cpp: false) is not null,
            "msvc" => Toolchains.FindMsvc() is not null,
            "asan" => Toolchains.FindMsvc() is not null && LocalFixLiveTests.HasAddressSanitizer(),
            _ => false,
        };

        if (!ready) return;

        using var msvcOnly = toolchain == "msvc" ? Toolchains.WithoutGnu() : null;
        using var temp = new TempFolder();

        var path = Path.Combine(temp.Path, "app.c");
        File.WriteAllText(path, source);

        var plan = TargetFactory.FromFile(path, TimeSpan.FromMinutes(5));
        Assert.True(plan.Ok, plan.Problem);

        using var http = new FixFinderHttpClient();
        var outcome = await new FixFinderSession(http, new FixSourceRegistry()).RunAsync(plan, new SearchBudget(Cache: CacheMode.CacheOnly));

        Assert.True(outcome.Result == SessionResult.FoundFix, $"{construct} ({toolchain}): {outcome.Result} - {outcome.Headline}");

        // gcc often knows the answer itself - a did-you-mean, or a fix-it - and either answer is right.
        string[] acceptable = toolchain == "gcc"
            ? [$"local:{rule}", "local:c-compiler-fix-it", "gcc:did-you-mean"]
            : [$"local:{rule}"];

        Assert.Contains(outcome.Best!.Id, acceptable);

        var copied = PasteableFix.For(new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));
        Assert.NotNull(copied);

        var text = copied!.Text.ReplaceLineEndings("\n") + "\n";

        Assert.Contains(expected, text, StringComparison.Ordinal);
        if (absent is not null) Assert.DoesNotContain(absent, text, StringComparison.Ordinal);
    }
}
