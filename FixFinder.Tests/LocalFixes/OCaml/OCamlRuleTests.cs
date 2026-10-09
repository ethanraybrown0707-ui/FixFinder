using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// Each OCaml rule's change, worked out from what OCaml said - the messages as OCaml 5.2's own tests record them, at the
/// characters they point at - and the changes each rule refuses to make. That each change compiles is for ocamlc to say,
/// on a computer with OCaml: OCamlLiveTests.
/// </summary>
public class OCamlRuleTests : IDisposable
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

    /// <summary>A compile error as the parser reads one: its place, as OCaml's first line gives it, kept in what it was read from.</summary>
    private static ParsedError Compile(string message, string file, int line, int start, int end) => new()
    {
        LanguageId = "ocaml",
        Confidence = 85,
        RawText = $"File \"{Path.GetFileName(file)}\", line {line}, characters {start}-{end}:\n{message}",
        FirstLineSequence = 0,
        ExceptionType = "compile error",
        Message = message,
        Frames = [new ErrorFrame { Order = 0, File = file, Line = line, Column = start + 1, RawLine = "", Origin = FrameOrigin.FirstParty }],
    };

    private static ParsedError Raised(string exception, string? message, string file, int line) => new()
    {
        LanguageId = "ocaml",
        Confidence = 90,
        RawText = "",
        FirstLineSequence = 0,
        ExceptionType = exception,
        Message = message,
        Frames = [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = "", Origin = FrameOrigin.FirstParty }],
    };

    private LocalFix? Fix(string rule, ParsedError error) =>
        LocalFixEngine.Rules.Single(r => r.Id == rule).Propose(new LocalFixContext { Error = error, SourceRoot = _temp.Path });

    [Fact]
    public void ANameOCamlDoesNotKnowIsChangedToTheOneItsHintSuggests()
    {
        var file = Write("values.ml", "let value1 = 3\nlet value2 = value2 + 1\n");

        var fix = Fix("ocaml-hint", Compile("Unbound value \"value2\"\nHint: Did you mean \"value1\"?", file, 2, 13, 19));
        var older = Fix("ocaml-hint", Compile("Unbound value value2\nHint: Did you mean value1?", file, 2, 13, 19));

        Assert.Equal("let value2 = value1 + 1", fix?.NewLines.Single());
        Assert.Equal("let value2 = value1 + 1", older?.NewLines.Single());
    }

    [Fact]
    public void ANumberWrittenAsTheWrongKindIsWrittenAsOCamlsHintSays()
    {
        var file = Write("price.ml", "let price : float = 123\n");

        var fix = Fix("ocaml-hint", Compile("This expression has type \"int\" but an expression was expected of type\n\"float\"\nHint: Did you mean \"123.\"?", file, 1, 20, 23));

        Assert.Equal("let price : float = 123.", fix?.NewLines.Single());
    }

    [Fact]
    public void AFunctionThatCallsItselfGetsRecOnTheLineOCamlNames()
    {
        var file = Write("facto.ml", "let facto n =\n   if n = 0 then 1 else n * facto (n-1)\n");

        var fix = Fix("ocaml-add-rec", Compile("Unbound value \"facto\"\nHint: If this is a recursive definition,\nyou should add the \"rec\" keyword on line 1", file, 2, 28, 33));

        Assert.Equal(1, fix?.StartLine);
        Assert.Equal("let rec facto n =", fix?.NewLines.Single());
    }

    [Fact]
    public void AnIntOperatorBesideAFloatBecomesTheFloatOperator()
    {
        var file = Write("sum.ml", "let total = 2.5 + 1.5\n");

        var fix = Fix("ocaml-float-operator", Compile("This expression has type \"float\" but an expression was expected of type\n\"int\"", file, 1, 12, 15));

        Assert.Equal("let total = 2.5 +. 1.5", fix?.NewLines.Single());
    }

    [Theory]
    [InlineData("let () = print_int 3.5\n", "float", "int", 19, 22, "let () = print_float 3.5")]
    [InlineData("let () = print_endline 42\n", "int", "string", 23, 25, "let () = print_endline (string_of_int 42)")]
    public void AValueIsPrintedWithTheFunctionForItsType(string code, string found, string expected, int start, int end, string fixedLine)
    {
        var file = Write("print.ml", code);

        var fix = Fix("ocaml-print-function", Compile($"This expression has type \"{found}\" but an expression was expected of type\n\"{expected}\"", file, 1, start, end));

        Assert.Equal(fixedLine, fix?.NewLines.Single());
    }

    [Fact]
    public void AWholeNumberDividedByZeroIsGivenAnAnswerForWhenTheDivisorIsZero()
    {
        var file = Write("average.ml", "let average marks total = total / List.length marks\n");

        var fix = Fix("ocaml-division-guard", Raised("Division_by_zero", null, file, 1));

        Assert.Equal("let average marks total = (if List.length marks = 0 then 0 else total / List.length marks)", fix?.NewLines.Single());
    }

    [Fact]
    public void ALoopToALengthStopsOneBeforeIt()
    {
        var file = Write("marks.ml", "let marks = [| 70; 80; 90 |]\nlet () =\n  for i = 0 to Array.length marks do\n    print_int marks.(i)\n  done\n");

        var fix = Fix("ocaml-for-upper-bound", Raised("Invalid_argument", "index out of bounds", file, 4));
        var otherArgument = Fix("ocaml-for-upper-bound", Raised("Invalid_argument", "List.nth", file, 4));

        Assert.Equal(3, fix?.StartLine);
        Assert.Equal("  for i = 0 to Array.length marks - 1 do", fix?.NewLines.Single());
        Assert.Null(otherArgument);
    }
}
