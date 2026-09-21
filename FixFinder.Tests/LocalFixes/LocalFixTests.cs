using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;
using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>The rules that work a fix out from the code, read against real source files and real message shapes.</summary>
public class LocalFixTests : IDisposable
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
        File.WriteAllText(path, body.ReplaceLineEndings("\n"));
        return path;
    }

    private static ParsedError Error(
        string language, string? type, string message, string? file, int? line,
        string? code = null, string? raw = null, string? symbol = null) => new()
    {
        LanguageId = language,
        Confidence = 90,
        RawText = raw ?? message,
        FirstLineSequence = 0,
        ExceptionType = type,
        ErrorCode = code,
        Message = message,
        Frames = file is null ? [] : [new ErrorFrame { Order = 0, File = file, Line = line, Symbol = symbol, RawLine = "" }],
    };

    private LocalFixContext Context(
        ParsedError error, IReadOnlyList<CapturedLine>? output = null,
        IReadOnlyList<ParsedError>? others = null, bool fromBuild = false) => new()
    {
        Error = error,
        Output = output ?? [],
        Others = others ?? [],
        SourceRoot = _temp.Path,
        FromBuild = fromBuild,
    };

    private static IReadOnlyList<CapturedLine> Lines(params string[] text) =>
        text.Select((line, i) => new CapturedLine(i, StreamKind.StdErr, line, TimeSpan.Zero)).ToList();

    private static IReadOnlyList<string> Applied(LocalFix fix) => fix.ApplyTo(SourceFile.Read(fix.File)!)!;

    [Fact]
    public void StringsAndCommentsAreBlankedWithoutMovingAnything()
    {
        var line = "x = \"a # b\" # note";
        var masked = CodeText.Mask(line, Syntax.Python);

        Assert.Equal(line.Length, masked.Length);
        Assert.StartsWith("x = \"", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("note", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("#", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void AColonGoesBeforeTheCommentNotInsideIt()
    {
        var (code, tail) = CodeText.SplitComment("if x > 1   # big numbers only", Syntax.Python);

        Assert.Equal("if x > 1", code);
        Assert.Equal("   # big numbers only", tail);
    }

    [Theory]
    [InlineData("prinft", "printf")]
    [InlineData("wieght", "weight")]
    [InlineData("stdoi.h", "stdio.h")]
    [InlineData("system", "System")]
    public void ASwapOrACaseSlipIsOneEdit(string wrong, string right) =>
        Assert.Equal(1, CodeText.Distance(wrong, right));

    [Fact]
    public void ATieBetweenNearestNamesIsARefusal()
    {
        Assert.Null(CodeText.Nearest("printn", ["print", "println", "printf"]));
        Assert.Equal("average", CodeText.Nearest("avarage", ["average", "scores", "values"]));
    }

    [Fact]
    public void AShortNameIsNeverMatched() => Assert.Null(CodeText.Nearest("ab", ["ac", "xb"]));

    [Fact]
    public void TwoOccurrencesOnALineAreRefusedUnlessTheCaretPicksOne()
    {
        Assert.Null(CodeText.ReplaceWord("f(avg, avg)", "avg", "average", Syntax.CLike));
        Assert.Equal("f(avg, average)", CodeText.ReplaceWord("f(avg, avg)", "avg", "average", Syntax.CLike, nearColumn: 7));
    }

    [Fact]
    public void AWordInsideAStringIsNotAnOccurrence() =>
        Assert.Equal("print(\"avg\", average)", CodeText.ReplaceWord("print(\"avg\", avg)", "avg", "average", Syntax.Python));

    [Fact]
    public void AFileIsWrittenBackByteForByte()
    {
        var path = Path.Combine(_temp.Path, "crlf.c");
        byte[] original = [0xEF, 0xBB, 0xBF, .. "int a;\r\nint b;\r\n"u8.ToArray()];
        File.WriteAllBytes(path, original);

        var source = SourceFile.Read(path)!;

        Assert.Equal(["int a;", "int b;"], source.Lines);
        Assert.Equal(original, source.Render(source.Lines));
    }

    [Fact]
    public void AnInsertionAtTheTopIsAWellFormedHunk()
    {
        var path = Write("app.py", "print(math.sqrt(16))\n");
        var source = SourceFile.Read(path)!;

        var diff = LocalFixDiff.Render(source, LocalFix.Insert("t", "t", "t", path, 1, ["import math"]), "app.py");

        Assert.Equal("--- a/app.py\n+++ b/app.py\n@@ -1,1 +1,2 @@\n+import math\n print(math.sqrt(16))\n", diff);
    }

    [Fact]
    public void AChangeReachingAnUnterminatedLastLineIsRefused()
    {
        var path = Path.Combine(_temp.Path, "tail.c");
        File.WriteAllText(path, "int a;\nint b");

        var source = SourceFile.Read(path)!;

        Assert.Null(LocalFixDiff.Render(source, LocalFix.ReplaceLine("t", "t", "t", path, 2, "int b;"), "tail.c"));
    }

    [Fact]
    public void CopiesAreCheckedWherePackagedPythonCanSeeThem() =>
        Assert.StartsWith(Path.GetTempPath(), CompileCheck.Root, StringComparison.OrdinalIgnoreCase);

    private static CheckResult Check(int exit, params ParsedError[] errors) => new(true, exit, [], errors);

    [Fact]
    public void ACompilerThatFailsWithoutSayingWhyHasCheckedNothing()
    {
        var error = Error("python", "SyntaxError", "expected ':'", Write("app.py", "if x\n"), 1);
        var fix = LocalFix.ReplaceLine("t", "t", "t", error.Frames[0].File!, 1, "if x:");

        Assert.False(LocalFixEngine.Judge(Context(error), fix, Check(1)).Accepted);
    }

    [Fact]
    public void AFixForACrashOnlyHasToLeaveTheFileCompiling()
    {
        var error = Error("python", "TypeError", "can only concatenate str (not \"int\") to str", Write("app.py", "x\n"), 1);
        var fix = LocalFix.ReplaceLine("t", "t", "t", error.Frames[0].File!, 1, "y");
        var broken = Error("python", "SyntaxError", "invalid syntax", error.Frames[0].File, 1);

        Assert.True(LocalFixEngine.Judge(Context(error), fix, Check(0)).Accepted);
        Assert.False(LocalFixEngine.Judge(Context(error), fix, Check(1, broken)).Accepted);
    }

    [Fact]
    public void ACompileFixMayNotIntroduceADifferentError()
    {
        var file = Write("app.c", "a\nb\nc\nd\n");
        var error = Error("msvc", "compile error", "'avarage': undeclared identifier", file, 4, "C2065");
        var fix = LocalFix.ReplaceLine("t", "t", "t", file, 4, "e");
        var other = Error("msvc", "compile error", "'x': redefinition", file, 1, "C2086");

        Assert.False(LocalFixEngine.Judge(Context(error, fromBuild: true), fix, Check(2, other)).Accepted);
        Assert.True(LocalFixEngine.Judge(Context(error, fromBuild: true), fix, Check(0)).Accepted);
    }

    [Fact]
    public void ASyntaxFixMayUncoverAnErrorFurtherDownButNotAtTheChange()
    {
        var file = Write("app.py", string.Concat(Enumerable.Repeat("x\n", 12)));
        var error = Error("python", "SyntaxError", "expected ':'", file, 2);
        var fix = LocalFix.ReplaceLine("t", "t", "t", file, 2, "if x:");

        var later = Error("python", "SyntaxError", "invalid syntax", file, 10);
        var same = Error("python", "SyntaxError", "invalid syntax", file, 2);

        Assert.True(LocalFixEngine.Judge(Context(error), fix, Check(1, later)).Accepted);
        Assert.False(LocalFixEngine.Judge(Context(error), fix, Check(1, same)).Accepted);
    }

    [Fact]
    public void AForgottenImportGoesAfterTheDocstringFutureAndOtherImports()
    {
        var file = Write("app.py", "\"\"\"Tool.\"\"\"\nfrom __future__ import annotations\nimport os\n\nprint(math.sqrt(16))\n");
        var error = Error("python", "NameError", "name 'math' is not defined. Did you forget to import 'math'?", file, 5);

        var fix = new PythonForgottenImport().Propose(Context(error))!;

        Assert.Equal(4, fix.StartLine);
        Assert.Equal(["import math"], fix.NewLines);
    }

    [Fact]
    public void AMissingColonKeepsTheComment()
    {
        var file = Write("app.py", "if x > 1  # big\n    print(x)\n");
        var error = Error("python", "SyntaxError", "expected ':'", file, 1);

        Assert.Equal("if x > 1:  # big", new PythonExpectedColon().Propose(Context(error))!.NewLines[0]);
    }

    [Theory]
    [InlineData("print \"hello\", name", "print(\"hello\", name)")]
    [InlineData("print \"hello\",", null)]
    public void APrintStatementBecomesACall(string line, string? expected)
    {
        var file = Write("app.py", line + "\n");
        var error = Error("python", "SyntaxError", "Missing parentheses in call to 'print'. Did you mean print(...)?", file, 1);

        Assert.Equal(expected, new PythonPrintStatement().Propose(Context(error))?.NewLines[0]);
    }

    [Theory]
    [InlineData("if x = 3:", "if x == 3:")]
    [InlineData("if f(key=1) = 3:", "if f(key=1) == 3:")]
    [InlineData("if a = b = c:", null)]
    public void OnlyTheOneBareEqualsBecomesAComparison(string line, string? expected)
    {
        var file = Write("app.py", line + "\n    pass\n");
        var error = Error("python", "SyntaxError", "invalid syntax. Maybe you meant '==' or ':=' instead of '='?", file, 1);

        Assert.Equal(expected, new PythonAssignmentInCondition().Propose(Context(error))?.NewLines[0]);
    }

    [Theory]
    [InlineData("    ", "    return sum(values)")]
    [InlineData("\t", "\treturn sum(values)")]
    public void TheNamedLineIsIndentedTheWayTheFileIndents(string unit, string expected)
    {
        var file = Write("app.py", $"def other():\n{unit}pass\n\ndef total(values):\nreturn sum(values)\n");
        var error = Error("python", "IndentationError", "expected an indented block after function definition on line 4", file, 5);

        Assert.Equal(expected, new PythonIndentedBlock().Propose(Context(error))!.NewLines[0]);
    }

    [Fact]
    public void TheRightHandOperandIsConvertedWhereTheUnderlinePointsAtIt()
    {
        var file = Write("app.py", "total = 42\nprint(\"Total: \" + total)\n");
        var raw = string.Join("\n",
            "Traceback (most recent call last):",
            $"  File \"{file}\", line 2, in <module>",
            "    print(\"Total: \" + total)",
            "          ~~~~~~~~~~^~~~~~~",
            "TypeError: can only concatenate str (not \"int\") to str");

        var error = Error("python", "TypeError", "can only concatenate str (not \"int\") to str", file, 2, raw: raw);

        Assert.Equal("print(\"Total: \" + str(total))", new PythonStrConcatenation().Propose(Context(error))!.NewLines[0]);
    }

    [Theory]
    [InlineData("count = 0\ndef bump():\n    count += 1\nbump()\n", true)]
    [InlineData("def bump():\n    count += 1\nbump()\n", false)]
    [InlineData("count = 0\ndef bump():\n    count += 1\n    count = 5\nbump()\n", false)]
    public void GlobalIsDeclaredOnlyWhenTheModuleHasTheNameAndTheFunctionDoesNotSetItsOwn(string source, bool offered)
    {
        var file = Write("app.py", source);
        var line = source.Split('\n').ToList().FindIndex(l => l.Contains("+=")) + 1;
        var error = Error("python", "UnboundLocalError",
            "cannot access local variable 'count' where it is not associated with a value", file, line, symbol: "bump");

        var fix = new PythonUnboundGlobal().Propose(Context(error));

        if (!offered)
        {
            Assert.Null(fix);
            return;
        }

        Assert.Equal(["    global count"], fix!.NewLines);
        Assert.Equal(line, fix.StartLine);
    }

    [Fact]
    public void MissingImportsFromTheSameBuildAreAddedTogether()
    {
        var file = Write("App.java", "package app;\n\npublic class App {\n    public static void main(String[] args) {\n        List<String> names = new ArrayList<>();\n    }\n}\n");

        var list = Error("java", "compile error", "cannot find symbol (symbol: class List, location: class App)", file, 5);
        var arrayList = Error("java", "compile error", "cannot find symbol (symbol: class ArrayList, location: class App)", file, 5);

        var fix = new JavaMissingImport().Propose(Context(list, others: [arrayList], fromBuild: true))!;

        Assert.Equal(2, fix.StartLine);
        Assert.Equal(["", "import java.util.ArrayList;", "import java.util.List;"], fix.NewLines);
    }

    [Fact]
    public void AClassAlreadyCoveredByAWildcardImportIsNotImportedAgain()
    {
        var file = Write("App.java", "import java.util.*;\npublic class App { List<String> x; }\n");
        var error = Error("java", "compile error", "cannot find symbol (symbol: class List, location: class App)", file, 2);

        Assert.Null(new JavaMissingImport().Propose(Context(error, fromBuild: true)));
    }

    [Fact]
    public void AMisspeltVariableIsMatchedAgainstTheFilesOwnNames()
    {
        var file = Write("App.java", "public class App {\n    public static void main(String[] args) {\n        int average = 3;\n        System.out.println(avarage);\n    }\n}\n");
        var error = Error("java", "compile error", "cannot find symbol (symbol: variable avarage, location: class App)", file, 4);

        var output = Lines(
            $"{file}:4: error: cannot find symbol",
            "        System.out.println(avarage);",
            "                           ^");

        Assert.Equal("        System.out.println(average);", new JavaNearestName().Propose(Context(error, output))!.NewLines[0]);
    }

    [Fact]
    public void TheSemicolonGoesWhereJavacsCaretPoints()
    {
        var file = Write("App.java", "class App {\n    void f() {\n        int x = 3 // three\n    }\n}\n");
        var error = Error("java", "compile error", "';' expected", file, 3);
        var output = Lines($"{file}:3: error: ';' expected", "        int x = 3 // three", "                 ^");

        Assert.Equal("        int x = 3; // three", new JavaMissingSemicolon().Propose(Context(error, output))!.NewLines[0]);
    }

    [Theory]
    [InlineData("    public static void main(String[] args) {", "InterruptedException",
        "    public static void main(String[] args) throws InterruptedException {")]
    [InlineData("    void load() throws InterruptedException {", "IOException",
        "    void load() throws InterruptedException, java.io.IOException {")]
    public void ACheckedExceptionIsDeclaredOnTheEnclosingMethod(string header, string exception, string expected)
    {
        var file = Write("App.java", $"public class App {{\n{header}\n        if (true) {{\n            work();\n        }}\n    }}\n}}\n");
        var error = Error("java", "compile error", $"unreported exception {exception}; must be caught or declared to be thrown", file, 4);

        var fix = new JavaUnreportedException().Propose(Context(error, fromBuild: true))!;

        Assert.Equal(2, fix.StartLine);
        Assert.Equal(expected, fix.NewLines[0]);
    }

    [Fact]
    public void AnExceptionInsideALambdaIsNotDeclaredOnTheMethod()
    {
        var file = Write("App.java", "public class App {\n    public static void main(String[] args) {\n        Runnable r = () -> {\n            Thread.sleep(10);\n        };\n    }\n}\n");
        var error = Error("java", "compile error", "unreported exception InterruptedException; must be caught or declared to be thrown", file, 4);

        Assert.Null(new JavaUnreportedException().Propose(Context(error, fromBuild: true)));
    }

    [Fact]
    public void AnInstanceMethodCalledFromMainIsMadeStatic()
    {
        var file = Write("App.java", "public class App {\n    int total() { return 3; }\n    public static void main(String[] args) {\n        System.out.println(total());\n    }\n}\n");
        var error = Error("java", "compile error", "non-static method total() cannot be referenced from a static context", file, 4);

        var fix = new JavaNonStaticMember().Propose(Context(error, fromBuild: true))!;

        Assert.Equal(2, fix.StartLine);
        Assert.Equal("    static int total() { return 3; }", fix.NewLines[0]);
    }

    [Theory]
    [InlineData("public class Main {\n    public static void main(String[] args) { }\n}\n", "public class App {")]
    [InlineData("public class Main {\n    public static void main(String[] args) { new Main(); }\n}\n", null)]
    public void APublicClassIsRenamedToItsFileOnlyWhenNothingElseNamesIt(string source, string? expected)
    {
        var file = Write("App.java", source);
        var error = Error("java", "compile error", "class Main is public, should be declared in a file named Main.java", file, 1);

        Assert.Equal(expected, new JavaPublicClassName().Propose(Context(error, fromBuild: true))?.NewLines[0]);
    }

    [Theory]
    [InlineData("Index 3 out of bounds for length 3", "        for (int i = 0; i < values.length; i++) {")]
    [InlineData("Index 5 out of bounds for length 3", null)]
    public void ALoopThatReachesTheLengthStopsOneBefore(string message, string? expected)
    {
        Write("App.java", "public class App {\n    public static void main(String[] args) {\n        int[] values = {1, 2, 3};\n        for (int i = 0; i <= values.length; i++) {\n            System.out.println(values[i]);\n        }\n    }\n}\n");

        var error = Error("java", "java.lang.ArrayIndexOutOfBoundsException", message, "App.java", 5);

        Assert.Equal(expected, new JavaOffByOneLoop().Propose(Context(error))?.NewLines[0]);
    }

    [Fact]
    public void AStringAssignedToAnIntIsParsed()
    {
        var file = Write("App.java", "class App {\n    void f() {\n        int count = \"5\";\n    }\n}\n");
        var error = Error("java", "compile error", "incompatible types: String cannot be converted to int", file, 3);
        var output = Lines($"{file}:3: error: incompatible types: String cannot be converted to int", "        int count = \"5\";", "                    ^");

        Assert.Equal("        int count = Integer.parseInt(\"5\");", new JavaStringConversion().Propose(Context(error, output))!.NewLines[0]);
    }

    [Fact]
    public void AStandardHeaderGoesAfterTheLastIncludeAndOnlyIfMissing()
    {
        var file = Write("app.c", "#include <stdio.h>\nint main(void) {\n    bool ready = true;\n    return 0;\n}\n");
        var error = Error("msvc", "compile error", "'bool': undeclared identifier", file, 3, "C2065");

        var fix = new CMissingStandardHeader().Propose(Context(error, fromBuild: true))!;

        Assert.Equal(2, fix.StartLine);
        Assert.Equal(["#include <stdbool.h>"], fix.NewLines);

        var included = Write("included.c", "#include <stdbool.h>\nint main(void) { bool b; }\n");
        Assert.Null(new CMissingStandardHeader().Propose(Context(Error("msvc", "compile error", "'bool': undeclared identifier", included, 2, "C2065"), fromBuild: true)));
    }

    [Fact]
    public void AnUndeclaredMallocIsExplainedAsTheCrashAndMustLoseItsWarning()
    {
        var file = Write("app.c", "#include <stdio.h>\nint main(void) {\n    char *buffer = malloc(64);\n    return 0;\n}\n");
        var warning = Error("msvc", "compile warning", "'malloc' undefined; assuming extern returning int", file, 3, "C4013");

        var fix = new CMissingStandardHeader().Propose(Context(warning))!;

        Assert.Equal(["#include <stdlib.h>"], fix.NewLines);
        Assert.Equal("'malloc' undefined", fix.ResolvesWarning);
        Assert.Contains("cut in half", fix.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void AMisspeltMemberIsMatchedAgainstTheStructItBelongsTo()
    {
        var file = Write("app.c", "struct parcel { int weight; };\nint main(void) {\n    struct parcel p = { 4 };\n    return p.wieght;\n}\n");
        var error = Error("msvc", "compile error", "'wieght': is not a member of 'parcel'", file, 4, "C2039");

        Assert.Equal("    return p.weight;", new CNearestName().Propose(Context(error, fromBuild: true))!.NewLines[0]);
    }

    [Fact]
    public void AMisspeltFunctionIsFoundThroughTheLinkerAndPlacedByTheWarning()
    {
        var file = Write("app.c", "#include <stdio.h>\nint main(void) {\n    prinft(\"hello\");\n    return 0;\n}\n");

        var output = Lines(
            $"{file}(3): warning C4013: 'prinft' undefined; assuming extern returning int",
            "app.obj : error LNK2019: unresolved external symbol prinft referenced in function main",
            "app.exe : fatal error LNK1120: 1 unresolved externals");

        var errors = new MsvcParser().ParseAll(output);

        Assert.Single(errors);
        Assert.Equal("LNK2019", errors[0].ErrorCode);

        var fix = new CNearestName().Propose(Context(errors[0], output, fromBuild: true))!;

        Assert.Equal(3, fix.StartLine);
        Assert.Equal("    printf(\"hello\");", fix.NewLines[0]);
    }

    [Theory]
    [InlineData("#include <stdoi.h>", "#include <stdio.h>")]
    [InlineData("#include \"stdoi.h\"", null)]
    public void AHeaderTypoIsCorrectedOnlyForAStandardInclude(string include, string? expected)
    {
        var file = Write("app.c", include + "\nint main(void) { return 0; }\n");
        var error = Error("msvc", "compile error", "Cannot open include file: 'stdoi.h': No such file or directory", file, 1, "C1083");

        Assert.Equal(expected, new CHeaderTypo().Propose(Context(error, fromBuild: true))?.NewLines[0]);
    }

    [Fact]
    public void TheSemicolonMsvcReportsOnTheNextLineGoesOnThePreviousStatement()
    {
        var file = Write("app.c", "int main(void) {\n    int x = 3 // three\n\n    printf(\"%d\", x);\n}\n");
        var error = Error("msvc", "compile error", "syntax error: missing ';' before identifier 'printf'", file, 4, "C2146");

        var fix = new CMissingSemicolon().Propose(Context(error, fromBuild: true))!;

        Assert.Equal(2, fix.StartLine);
        Assert.Equal("    int x = 3; // three", fix.NewLines[0]);
    }

    [Theory]
    [InlineData("int main(void) {\n    return 0;\n", true)]
    [InlineData("int main(void) {\n    if (1) {\n    return 0;\n", false)]
    public void AClosingBraceIsAddedOnlyWhenExactlyOneIsMissing(string source, bool offered)
    {
        var file = Write("app.c", source);
        var error = Error("msvc", "compile error", "'{': no matching token found", file, 1, "C1075");

        var fix = new CMissingClosingBrace().Propose(Context(error, fromBuild: true));

        Assert.Equal(offered, fix is not null);
        if (fix is not null) Assert.Equal(["}"], fix.NewLines);
    }

    [Fact]
    public void WarningsAreReadSeparatelyFromErrors()
    {
        var output = Lines(@"C:\src\app.c(3): warning C4013: 'malloc' undefined; assuming extern returning int");

        Assert.Empty(new MsvcParser().ParseAll(output));
        Assert.Equal("C4013", Assert.Single(MsvcParser.ParseWarnings(output)).ErrorCode);
    }

    [Theory]
    [InlineData(unchecked((int)0xC0000094))]
    [InlineData(unchecked((int)0xC0000374))]
    public void AnIntegerDivideByZeroIsACrash(int exitCode) =>
        Assert.Equal(RunOutcome.Crashed, RunClassifier.Classify(exitCode, hasParsedError: false).Outcome);

    [Fact]
    public void JavacsMissingPackageGetsThePomBlock()
    {
        var error = Error("java", "compile error", "package com.google.gson does not exist", Write("App.java", "import com.google.gson.Gson;\n"), 1);
        var spec = new TargetSpec { ExecutablePath = "javac", Arguments = "App.java", WorkingDirectory = _temp.Path };

        Assert.Contains("<artifactId>gson</artifactId>", MissingDependency.For(error, spec)!.Command!, StringComparison.Ordinal);
    }
}
