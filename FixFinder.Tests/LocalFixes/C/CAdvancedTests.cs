using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>The C mistakes of later years - macros, pointers, multi-dimensional arrays, input - each rule's fix and refusals from
/// gcc's, MSVC's and AddressSanitizer's words, then the same programs built for real.</summary>
public class CAdvancedTests : IDisposable
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

    private static ParsedError Diagnostic(string language, string? code, string message, string? file, int line, string type) => new()
    {
        LanguageId = language,
        Confidence = 90,
        RawText = message,
        FirstLineSequence = 0,
        ExceptionType = type,
        ErrorCode = code,
        Message = message,
        Frames = file is null ? [] : [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = "" }],
    };

    private LocalFix? Fix(string rule, ParsedError error) =>
        LocalFixEngine.Rules.Single(r => r.Id == rule).Propose(new LocalFixContext { Error = error, SourceRoot = _temp.Path });

    [Theory]
    [InlineData("c-define-semicolon", "gcc", null, "expected ']' before ';' token", "#define SIZE 5;\nint values[SIZE];\n", 1, "compile error", "#define SIZE 5")]
    [InlineData("c-define-semicolon", "msvc", "C2143", "syntax error: missing ']' before ';'", "#define SIZE 5;\nint values[SIZE];\n", 2, "compile error", "#define SIZE 5")]
    [InlineData("c-define-semicolon", "msvc", "C2143", "syntax error: missing ']' before ';'", "#define SIZE 5\nint values[SIZE;\n", 2, "compile error", null)]
    [InlineData("c-free-non-heap", "gcc", null, "attempting free on address which was not malloc()-ed: 0x1 in thread T0", "int main(void) {\n    int values[10];\n    free(values);\n    return 0;\n}\n", 3, "attempting free", "")]
    [InlineData("c-free-non-heap", "gcc", null, "attempting free on address which was not malloc()-ed: 0x1 in thread T0", "int main(void) {\n    int *p = malloc(4);\n    free(p);\n    return 0;\n}\n", 3, "attempting free", null)]
    [InlineData("c-if-semicolon", "gcc", null, "'else' without a previous 'if'", "    if (x > 3); {\n        a();\n    } else {\n        b();\n    }\n", 3, "compile error", "    if (x > 3) {")]
    [InlineData("c-if-semicolon", "msvc", "C2181", "illegal else without matching if", "    if (x > 3); {\n        a();\n    } else {\n        b();\n    }\n", 3, "compile error", "    if (x > 3) {")]
    [InlineData("c-array-parameter-size", "gcc", null, "array type has incomplete element type 'int[]'", "void show(int grid[][], int rows) {\n}\nint main(void) {\n    int grid[2][3];\n    show(grid, 2);\n    return 0;\n}\n", 1, "compile error", "void show(int grid[][3], int rows) {")]
    [InlineData("c-array-parameter-size", "msvc", "C2087", "'grid': missing subscript", "void show(int grid[][], int rows) {\n}\nint main(void) {\n    int grid[2][3];\n    show(grid, 2);\n    return 0;\n}\n", 1, "compile error", "void show(int grid[][3], int rows) {")]
    [InlineData("c-array-parameter-size", "gcc", null, "array type has incomplete element type 'int[]'", "void show(int grid[][], int rows) {\n}\n", 1, "compile error", null)]
    [InlineData("c-array-parameter-size", "gcc", null, "array type has incomplete element type 'int[]'", "void show(int grid[][], int rows) {\n    printf(\"%d\", grid[0][0]);\n}\nint main(void) {\n    int grid[2][3];\n    show(grid, 2);\n    return 0;\n}\n", 1, "compile error", "void show(int grid[][3], int rows) {")]
    [InlineData("c-void-pointer-dereference", "gcc", null, "invalid use of void expression", "int main(void) {\n    int x = 5;\n    void *p = &x;\n    printf(\"%d\\n\", *p);\n", 4, "compile error", "    printf(\"%d\\n\", *(int *)p);")]
    [InlineData("c-void-pointer-dereference", "msvc", "C2100", "you cannot dereference an operand of type 'void'", "int main(void) {\n    double x = 5;\n    void *p = &x;\n    printf(\"%f\\n\", *p);\n", 4, "compile error", "    printf(\"%f\\n\", *(double *)p);")]
    [InlineData("c-void-pointer-dereference", "gcc", null, "invalid use of void expression", "void f(void *p) {\n    printf(\"%d\\n\", *p);\n", 2, "compile error", null)]
    [InlineData("c-format-argument", "msvc", "C4477", "'scanf' : format string '%d' requires an argument of type 'int *', but variadic argument 1 has type 'int'", "    scanf(\"%d\", age);\n", 1, "compile warning", "    scanf(\"%d\", &age);")]
    [InlineData("c-format-argument", "gcc", null, "format '%d' expects argument of type 'int *', but argument 2 has type 'int' [-Wformat=]", "    scanf(\"%d\", age);\n", 1, "compile warning", "    scanf(\"%d\", &age);")]
    [InlineData("c-format-argument", "gcc", null, "format '%s' expects argument of type 'char *', but argument 2 has type 'int' [-Wformat=]", "    char grade = 'A';\n    printf(\"Grade: %s\\n\", grade);\n", 2, "compile warning", "    printf(\"Grade: %c\\n\", grade);")]
    [InlineData("c-format-argument", "msvc", "C4477", "'printf' : format string '%s' requires an argument of type 'char *', but variadic argument 1 has type 'int'", "    char grade = 'A';\n    printf(\"Grade: %s\\n\", grade);\n", 2, "compile warning", "    printf(\"Grade: %c\\n\", grade);")]
    [InlineData("c-format-argument", "gcc", null, "format '%s' expects argument of type 'char *', but argument 2 has type 'int' [-Wformat=]", "    int count = 5;\n    printf(\"%s\\n\", count);\n", 2, "compile warning", null)]
    public void EachConstructGetsItsFixOrARefusal(
        string rule, string language, string? code, string message, string source, int line, string type, string? expected)
    {
        var file = Write("app.c", source);
        var fix = Fix(rule, Diagnostic(language, code, message, file, line, type));

        Assert.Equal(expected, fix is null ? null : string.Join("|", fix.NewLines));
    }

    [Theory]
    [InlineData("gcc", null, "undefined reference to 'WinMain'", "#include <stdio.h>\nint Main(void) {\n    return 0;\n}\n", "int main(void) {")]
    [InlineData("msvc", "LNK1561", "entry point must be defined", "#include <stdio.h>\nint mian(void) {\n    return 0;\n}\n", "int main(void) {")]
    [InlineData("gcc", null, "undefined reference to 'WinMain'", "#include <stdio.h>\nint main(void) {\n    return 0;\n}\n", null)]
    public void AMisspeltMainIsFoundInTheProject(string language, string? code, string message, string source, string? expected)
    {
        Write("app.c", source);
        var fix = Fix("c-main-name", Diagnostic(language, code, message, null, 0, "link error"));

        Assert.Equal(expected, fix is null ? null : string.Join("|", fix.NewLines));
    }

    public static TheoryData<string, string, string, string?, string, string> Constructs
    {
        get
        {
            var programs = new (string Construct, string Source, string? Input, string Rule, string Expected)[]
            {
                ("define with a semicolon", "#include <stdio.h>\n#define SIZE 5;\nint main(void) {\n    int values[SIZE];\n    values[0] = 1;\n    printf(\"%d\\n\", values[0]);\n    return 0;\n}\n", null, "c-define-semicolon", "#define SIZE 5\n"),
                ("main misspelt", "#include <stdio.h>\nint Main(void) {\n    printf(\"hi\\n\");\n    return 0;\n}\n", null, "c-main-name", "int main(void) {"),
                ("if semicolon else", "#include <stdio.h>\nint main(void) {\n    int x = 5;\n    if (x > 3); {\n        printf(\"big\\n\");\n    } else {\n        printf(\"small\\n\");\n    }\n    return 0;\n}\n", null, "c-if-semicolon", "if (x > 3) {"),
                ("2D array parameter", "#include <stdio.h>\nvoid show(int grid[][], int rows) {\n    printf(\"%d %d\\n\", grid[0][0], rows);\n}\nint main(void) {\n    int grid[2][3] = {{1, 2, 3}, {4, 5, 6}};\n    show(grid, 2);\n    return 0;\n}\n", null, "c-array-parameter-size", "void show(int grid[][3], int rows) {"),
                ("void pointer dereference", "#include <stdio.h>\nint main(void) {\n    int x = 5;\n    void *p = &x;\n    printf(\"%d\\n\", *p);\n    return 0;\n}\n", null, "c-void-pointer-dereference", "printf(\"%d\\n\", *(int *)p);"),
                ("char printed with %s", "#include <stdio.h>\nint main(void) {\n    char grade = 'A';\n    printf(\"Grade: %s\\n\", grade);\n    return 0;\n}\n", null, "c-format-argument", "printf(\"Grade: %c\\n\", grade);"),
            };

            var data = new TheoryData<string, string, string, string?, string, string>();

            foreach (var toolchain in new[] { "gcc", "msvc" })
            foreach (var (construct, source, input, rule, expected) in programs)
                data.Add(toolchain, construct, source, input, rule, expected);

            data.Add("msvc", "scanf without &", "#include <stdio.h>\nint main(void) {\n    int age = 0;\n    scanf(\"%d\", age);\n    printf(\"%d\\n\", age);\n    return 0;\n}\n", "42\n", "c-format-argument", "scanf(\"%d\", &age);");

            data.Add("asan", "free of a stack array", "#include <stdlib.h>\nint main(void) {\n    int values[10];\n    values[0] = 1;\n    free(values);\n    return 0;\n}\n", null, "c-free-non-heap", "values[0] = 1;\n    return 0;");

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Constructs))]
    public async Task TheConstructIsFixedByARealBuild(string toolchain, string construct, string source, string? input, string rule, string expected)
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
        if (input is not null) plan = plan with { Spec = plan.Spec!.WithInput(input) };

        using var http = new FixFinderHttpClient();
        var outcome = await new FixFinderSession(http, new FixSourceRegistry()).RunAsync(plan, new SearchBudget(Cache: CacheMode.CacheOnly));

        if (ApplicationControl.Refused(outcome)) return;
        Assert.True(outcome.Result == SessionResult.FoundFix, $"{construct} ({toolchain}): {outcome.Result} - {outcome.Headline}");

        string[] acceptable = toolchain == "gcc" ? [$"local:{rule}", "local:c-compiler-fix-it", "gcc:did-you-mean"] : [$"local:{rule}"];
        Assert.Contains(outcome.Best!.Id, acceptable);

        var copied = PasteableFix.For(new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));
        Assert.NotNull(copied);
        Assert.Contains(expected, copied!.Text.ReplaceLineEndings("\n") + "\n", StringComparison.Ordinal);
    }
}
