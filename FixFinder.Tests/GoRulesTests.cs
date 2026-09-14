using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Tests;

/// <summary>
/// The Go mistakes of a beginner, of someone arriving from another language, and of later years - each rule's fix and refusals
/// from the Go compiler's and runtime's words - and the compiler parser that reads them.
/// </summary>
public class GoRulesTests : IDisposable
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

    private static ParsedError Error(string type, string message, string? file, int line, string? raw = null) => new()
    {
        LanguageId = "go",
        Confidence = 85,
        RawText = raw ?? message,
        FirstLineSequence = 0,
        ExceptionType = type,
        Message = message,
        Frames = file is null ? [] : [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = "" }],
    };

    private LocalFix? Fix(string rule, ParsedError error, IReadOnlyList<ParsedError>? others = null) =>
        LocalFixEngine.Rules.Single(r => r.Id == rule).Propose(new LocalFixContext { Error = error, Others = others ?? [], SourceRoot = _temp.Path });

    private const string Main = "package main\n\nimport \"fmt\"\n\nfunc main() {\n";
    private const string NilMapInStruct = "package main\n\nimport \"fmt\"\n\ntype Counter struct {\n\tcounts map[string]int\n}\n\nfunc main() {\n\tc := Counter{}\n\tc.counts[\"a\"]++\n\tfmt.Println(c.counts)\n}\n";

    [Theory]
    [InlineData("go-missing-import", "undefined: fmt", "package main\n\nfunc main() {\n\tfmt.Println(\"hello\")\n}\n", 4, "|import \"fmt\"")]
    [InlineData("go-missing-import", "undefined: strings", Main + "\tfmt.Println(strings.ToUpper(\"a\"))\n}\n", 6, "import (|\t\"fmt\"|\t\"strings\"|)")]
    [InlineData("go-unused-import", "\"os\" imported and not used", "package main\n\nimport (\n\t\"fmt\"\n\t\"os\"\n)\n", 5, "")]
    [InlineData("go-import-quotes", "missing import path", "package main\n\nimport fmt\n", 3, "import \"fmt\"")]
    [InlineData("go-foreign-print", "undefined: console", Main + "\tconsole.log(\"hello\")\n}\n", 6, "\tfmt.Println(\"hello\")")]
    [InlineData("go-foreign-word", "undefined: null", "\tvar p *int = null\n", 1, "\tvar p *int = nil")]
    [InlineData("go-foreign-word", "undefined: True", "\tready := True\n", 1, "\tready := true")]
    [InlineData("go-short-declare", "undefined: count", "package main\n\nfunc main() {\n\tcount = 5\n\tprintln(count)\n}\n", 4, "\tcount := 5")]
    [InlineData("go-nearest-name", "undefined: totl", Main + "\ttotal := 5\n\tfmt.Println(totl)\n}\n", 7, "\tfmt.Println(total)")]
    [InlineData("go-nearest-name", "undefined: sqr", "func (sq Square) Area() float64 {\n\treturn sqr.side * sqr.side\n}\n", 2, "\treturn sq.side * sq.side")]
    [InlineData("go-unused-variable", "declared and not used: i", "\tfor i, item := range items {\n", 1, "\tfor _, item := range items {")]
    [InlineData("go-unused-variable", "declared and not used: count", "\tcount := 5\n", 1, "")]
    [InlineData("go-unused-variable", "declared and not used: err", "\tn, err := strconv.Atoi(\"42\")\n", 1, "\tn, _ := strconv.Atoi(\"42\")")]
    [InlineData("go-unused-variable", "declared and not used: total", "\ttotal := compute()\n", 1, null)]
    [InlineData("go-package-member", "undefined: fmt.println (but have Println)", "\tfmt.println(\"hello\")\n", 1, "\tfmt.Println(\"hello\")")]
    [InlineData("go-selector", "d.name undefined (type Dog has no field or method name, but does have field Name)", "\tfmt.Println(d.name)\n", 1, "\tfmt.Println(d.Name)")]
    [InlineData("go-selector", "s.area undefined (type Square has no field or method area, but does have method Area)", "\tfmt.Println(s.area())\n", 1, "\tfmt.Println(s.Area())")]
    [InlineData("go-selector", "items.length undefined (type []int has no field or method length)", "\tfmt.Println(items.length)\n", 1, "\tfmt.Println(len(items))")]
    [InlineData("go-selector", "name.len undefined (type string has no field or method len)", "\tfmt.Println(name.len())\n", 1, "\tfmt.Println(len(name))")]
    [InlineData("go-selector", "items.append undefined (type []int has no field or method append)", "\titems.append(4)\n", 1, "\titems = append(items, 4)")]
    [InlineData("go-while", "syntax error: unexpected name x at end of statement", "\twhile x < 3 {\n", 1, "\tfor x < 3 {")]
    [InlineData("go-for-parentheses", "syntax error: unexpected :=, expected )", "\tfor (i := 0; i < 3; i++) {\n", 1, "\tfor i := 0; i < 3; i++ {")]
    [InlineData("go-redeclared", "no new variables on left side of :=", "\tcount := 2\n", 1, "\tcount = 2")]
    [InlineData("go-rune-literal", "more than one character in rune literal", "\tfmt.Println('hello')\n", 1, "\tfmt.Println(\"hello\")")]
    [InlineData("go-assignment-in-condition", "syntax error: cannot use assignment x = 5 as value", "\tif x = 5 {\n", 1, "\tif x == 5 {")]
    [InlineData("go-missing-closing-paren", "syntax error: unexpected newline in argument list; possibly missing comma or )", "\tfmt.Println(\"hello\"\n", 1, "\tfmt.Println(\"hello\")")]
    [InlineData("go-missing-closing-brace", "syntax error: unexpected EOF, expected }", "package main\n\nfunc main() {\n\tprintln(1)\n", 5, "}")]
    [InlineData("go-outside-function", "syntax error: non-declaration statement outside function body", "package main\n\nfunc main() {\n\tprintln(1)\n}\n}\n", 6, "")]
    [InlineData("go-outside-function", "syntax error: non-declaration statement outside function body", "package main\n\nFunc main() {\n\tprintln(1)\n}\n", 3, "func main() {")]
    [InlineData("go-string-plus-number", "invalid operation: \"Age: \" + age (mismatched types untyped string and int)", Main + "\tage := 20\n\tfmt.Println(\"Age: \" + age)\n}\n", 7, "\tfmt.Println(\"Age: \" + fmt.Sprint(age))")]
    [InlineData("go-string-plus-number", "invalid operation: \"Initial: \" + name[0] (mismatched types untyped string and byte)", "\tinitial := \"Initial: \" + name[0]\n", 1, "\tinitial := \"Initial: \" + string(name[0])")]
    [InlineData("go-byte-compared-with-string", "invalid operation: name[0] == \"A\" (mismatched types byte and untyped string)", "\tif name[0] == \"A\" {\n", 1, "\tif name[0] == 'A' {")]
    [InlineData("go-numeric-conversion", "cannot use count (variable of type int) as float64 value in variable declaration", "\tvar average float64 = count\n", 1, "\tvar average float64 = float64(count)")]
    [InlineData("go-two-values", "assignment mismatch: 1 variable but strconv.Atoi returns 2 values", "\tn := strconv.Atoi(\"42\")\n", 1, "\tn, _ := strconv.Atoi(\"42\")")]
    [InlineData("go-append-not-used", "append(items, 3) (value of type []int) is not used", "\tappend(items, 3)\n", 1, "\titems = append(items, 3)")]
    [InlineData("go-pointer-receiver", "cannot use Square{…} (value of struct type Square) as Shape value in variable declaration: Square does not implement Shape (method Area has pointer receiver)", "\tvar s Shape = Square{side: 2}\n", 1, "\tvar s Shape = &Square{side: 2}")]
    public void EachMistakeGetsItsFixOrARefusal(string rule, string message, string source, int line, string? expected)
    {
        var file = Write("app.go", source);
        var fix = Fix(rule, Error("compile error", message, file, line));

        Assert.Equal(expected, fix is null ? null : string.Join("|", fix.NewLines));
    }

    [Theory]
    [InlineData("go-index-loop", "runtime error", "runtime error: index out of range [3] with length 3", "package main\n\nfunc main() {\n\titems := []int{1, 2, 3}\n\tfor i := 0; i <= len(items); i++ {\n\t\tprintln(items[i])\n\t}\n}\n", 6, "\tfor i := 0; i < len(items); i++ {")]
    [InlineData("go-nil-map", "panic", "assignment to entry in nil map", "package main\n\nfunc main() {\n\tvar ages map[string]int\n\tages[\"Ada\"] = 36\n}\n", 5, "\tages := make(map[string]int)")]
    [InlineData("go-nil-map", "panic", "assignment to entry in nil map", NilMapInStruct, 11, "\tc := Counter{counts: make(map[string]int)}")]
    public void EachPanicGetsItsFix(string rule, string type, string message, string source, int line, string expected)
    {
        var file = Write("app.go", source);
        var fix = Fix(rule, Error(type, message, file, line));

        Assert.Equal(expected, fix is null ? null : string.Join("|", fix.NewLines));
    }

    // ------------------------------------------------------------------ the cause, not the symptom Go reported first

    /// <summary>"fmt" looks unused only because console.log was meant to use it - the import stays and the call is fixed.</summary>
    [Fact]
    public void AnUnusedFmtIsAnsweredByFixingThePrintThatShouldUseIt()
    {
        var file = Write("app.go", Main + "\tconsole.log(\"hello\")\n}\n");
        var unused = Error("compile error", "\"fmt\" imported and not used", file, 3);
        var console = Error("compile error", "undefined: console", file, 6);

        Assert.Null(Fix("go-unused-import", unused, [console]));

        var fix = Fix("go-foreign-print", unused, [console]);

        Assert.NotNull(fix);
        Assert.Equal(6, fix!.StartLine);
        Assert.Equal("\tfmt.Println(\"hello\")", Assert.Single(fix.NewLines));
    }

    /// <summary>total looks unused only because the line meant to use it spelt it totl - nothing is deleted.</summary>
    [Fact]
    public void AnUnusedVariableIsAnsweredByTheMisspellingThatExplainsIt()
    {
        var file = Write("app.go", Main + "\ttotal := 5\n\tfmt.Println(totl)\n}\n");
        var unused = Error("compile error", "declared and not used: total", file, 6);
        var misspelt = Error("compile error", "undefined: totl", file, 7);

        Assert.Null(Fix("go-unused-variable", unused, [misspelt]));

        var fix = Fix("go-nearest-name", unused, [misspelt]);

        Assert.NotNull(fix);
        Assert.Equal(7, fix!.StartLine);
        Assert.Equal("\tfmt.Println(total)", Assert.Single(fix.NewLines));
    }

    // ------------------------------------------------------------------ messages that go on over several lines

    [Fact]
    public void AReturnTypeComesFromWhatWasReturned()
    {
        var file = Write("app.go", "package main\n\nfunc add(a int, b int) {\n\treturn a + b\n}\n");
        var fix = Fix("go-missing-return-type", Error("compile error", "too many return values", file, 4, "app.go:4:9: too many return values\n\thave (int)\n\twant ()"));

        Assert.NotNull(fix);
        Assert.Equal(3, fix!.StartLine);
        Assert.Equal("func add(a int, b int) int {", Assert.Single(fix.NewLines));
    }

    [Fact]
    public void AMissingLeadingReturnValueIsItsZeroValue()
    {
        var file = Write("app.go", "\t\treturn errors.New(\"divide by zero\")\n");
        var fix = Fix("go-not-enough-return-values", Error("compile error", "not enough return values", file, 1, "app.go:1:10: not enough return values\n\thave (error)\n\twant (int, error)"));

        Assert.Equal("\t\treturn 0, errors.New(\"divide by zero\")", Assert.Single(fix!.NewLines));
    }

    [Fact]
    public void AnInterfaceMethodWrittenInTheWrongCaseIsRenamed()
    {
        var file = Write("app.go", "package main\n\ntype Shape interface {\n\tArea() float64\n}\n\ntype Square struct {\n\tside float64\n}\n\nfunc (s Square) area() float64 {\n\treturn s.side * s.side\n}\n\nfunc main() {\n\tvar s Shape = Square{side: 2}\n\t_ = s\n}\n");
        var message = "cannot use Square{…} (value of struct type Square) as Shape value in variable declaration: Square does not implement Shape (missing method Area)";
        var fix = Fix("go-method-case", Error("compile error", message, file, 16, message + "\n\t\thave area() float64\n\t\twant Area() float64"));

        Assert.NotNull(fix);
        Assert.Equal(11, fix!.StartLine);
        Assert.Equal("func (s Square) Area() float64 {", Assert.Single(fix.NewLines));
    }

    [Fact]
    public void TheBraceOnTheNextLineJoinsTheHeader()
    {
        var file = Write("app.go", "package main\n\nfunc main()\n{\n\tprintln(1)\n}\n");
        var fix = Fix("go-brace-on-next-line", Error("compile error", "syntax error: unexpected semicolon or newline before {", file, 4));

        Assert.NotNull(fix);
        Assert.Equal(3, fix!.StartLine);
        Assert.Equal(2, fix.RemoveCount);
        Assert.Equal("func main() {", Assert.Single(fix.NewLines));
    }

    [Fact]
    public void ElseOnTheNextLineJoinsTheClosingBrace()
    {
        var file = Write("app.go", "\tif x > 3 {\n\t\tprintln(1)\n\t}\n\telse {\n\t\tprintln(2)\n\t}\n");
        var fix = Fix("go-else-on-next-line", Error("compile error", "syntax error: unexpected keyword else, expected }", file, 4));

        Assert.NotNull(fix);
        Assert.Equal(3, fix!.StartLine);
        Assert.Equal(2, fix.RemoveCount);
        Assert.Equal("\t} else {", Assert.Single(fix.NewLines));
    }

    // ------------------------------------------------------------------ deadlocks

    [Theory]
    [InlineData("goroutine 1 [chan send]:", "package main\n\nfunc main() {\n\tch := make(chan int)\n\tch <- 1\n\tprintln(<-ch)\n}\n", 5, 4, "\tch := make(chan int, 1)")]
    [InlineData("goroutine 1 [chan receive (nil chan)]:", "package main\n\nfunc main() {\n\tvar ch chan int\n\tgo func() {\n\t\tch <- 1\n\t}()\n\tprintln(<-ch)\n}\n", 8, 4, "\tch := make(chan int)")]
    [InlineData("goroutine 1 [chan receive]:", "package main\n\nfunc main() {\n\tch := make(chan int, 3)\n\tch <- 1\n\tch <- 2\n\tfor v := range ch {\n\t\tprintln(v)\n\t}\n}\n", 7, 7, "\tclose(ch)")]
    public void ADeadlockOnAChannelIsTracedToTheChannel(string state, string source, int line, int fixLine, string expected)
    {
        var file = Write("app.go", source);
        var raw = $"fatal error: all goroutines are asleep - deadlock!\n\n{state}\nmain.main()";

        var fix = Fix("go-channel-deadlock", Error("fatal error", "all goroutines are asleep - deadlock!", file, line, raw));

        Assert.NotNull(fix);
        Assert.Equal(fixLine, fix!.StartLine);
        Assert.Equal(expected, Assert.Single(fix.NewLines));
    }

    // ------------------------------------------------------------------ the program as a whole

    [Fact]
    public void AMisspeltMainIsFoundInThePackage()
    {
        Write("app.go", "package main\n\nfunc Main() {\n}\n");

        var fix = Fix("go-main-name", Error("link error", "function main is undeclared in the main package", null, 0));

        Assert.Equal("func main() {", Assert.Single(fix!.NewLines));
    }

    [Fact]
    public void AProgramInAnotherPackageIsDeclaredMain()
    {
        Write("app.go", "package app\n\nfunc main() {\n}\n");

        var fix = Fix("go-package-main", Error("compile error", "package command-line-arguments is not a main package", null, 0));

        Assert.Equal("package main", Assert.Single(fix!.NewLines));
    }

    /// <summary>
    /// Go 1.24 says only "undefined: fmt.println". By edit distance Sprintln is as near as Println; the one that is the same word
    /// in another case is the answer. Caught by CI, whose Go was older than this machine's.
    /// </summary>
    [Fact]
    public void AnUnexportedSpellingWithoutGosHintIsTheSameWordCapitalised()
    {
        if (TargetFactory.FindOnPath("go") is null) return;

        var file = Write("app.go", "\tfmt.println(\"hello\")\n");
        var fix = Fix("go-package-member", Error("compile error", "undefined: fmt.println", file, 1));

        Assert.Equal("\tfmt.Println(\"hello\")", Assert.Single(fix!.NewLines));
    }

    [Fact]
    public void AMisspeltPackageMemberIsReadFromGoDoc()
    {
        if (TargetFactory.FindOnPath("go") is null) return;

        var file = Write("app.go", "\tfmt.Println(strings.Contians(\"hello\", \"ell\"))\n");
        var fix = Fix("go-package-member", Error("compile error", "undefined: strings.Contians", file, 1));

        Assert.Equal("\tfmt.Println(strings.Contains(\"hello\", \"ell\"))", Assert.Single(fix!.NewLines));
    }

    // ------------------------------------------------------------------ the parser

    private static IReadOnlyList<CapturedLine> Lines(params string[] text) =>
        text.Select((line, i) => new CapturedLine(i, StreamKind.StdErr, line, TimeSpan.Zero)).ToList();

    [Fact]
    public void EveryCompilerErrorIsReadWithTheLinesThatContinueIt()
    {
        var lines = Lines(
            "# command-line-arguments",
            @".\app.go:6:9: too many return values",
            "\thave (int)",
            "\twant ()",
            @".\app.go:10:14: add(2, 3) (no value) used as value");

        var parser = new GoCompileParser();
        Assert.True(parser.Detect(lines.Select(l => l.Text).ToList()) > 30);

        var errors = parser.ParseAll(lines);

        Assert.Equal(2, errors.Count);
        Assert.Equal("too many return values", errors[0].Message);
        Assert.Contains("want ()", errors[0].RawText, StringComparison.Ordinal);
        Assert.Equal(6, errors[0].Frames[0].Line);
        Assert.Equal(9, errors[0].Frames[0].Column);
        Assert.Equal("compile error", errors[1].ExceptionType);
    }

    [Fact]
    public void AMissingMainIsReadAsALinkError()
    {
        var error = new GoCompileParser().Parse(Lines("# command-line-arguments", "runtime.main_main·f: function main is undeclared in the main package"));

        Assert.NotNull(error);
        Assert.Equal("link error", error!.ExceptionType);
        Assert.Equal("function main is undeclared in the main package", error.Message);
    }

    /// <summary>Before the compiler parser, a Go program that would not build was reported as having failed without a word.</summary>
    [Fact]
    public void TheRegistryReadsAGoBuildFailure()
    {
        var error = new ParserRegistry().Parse(Lines("# command-line-arguments", @".\app.go:6:2: declared and not used: count"));

        Assert.NotNull(error);
        Assert.Equal("go", error!.LanguageId);
        Assert.Equal("declared and not used: count", error.Message);
    }
}
