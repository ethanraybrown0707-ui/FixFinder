using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Tests;

/// <summary>
/// What OCaml says when a program does not build, and when it stops - as OCaml 5.2's own test suite records it, in its
/// .reference files and expect tests. The compiler's messages are copied as recorded; an expect test records them under
/// the toplevel's "Line 2, characters 28-33:", which the compiler writes as File "x.ml", line 2, characters 28-33:.
/// </summary>
public class OCamlParserTests
{
    private static IReadOnlyList<CapturedLine> Lines(string output) =>
        output.Replace("\r\n", "\n").TrimEnd('\n').Split('\n')
            .Select((text, index) => new CapturedLine(index, StreamKind.StdErr, text, TimeSpan.Zero))
            .ToList();

    [Fact]
    public void ASyntaxErrorIsReadWithItsPlaceAndColumn()
    {
        // testsuite/tests/parse-errors/pr7847.compilers.reference
        var output = """
            File "pr7847.ml", line 10, characters 30-31:
            10 | external x : unit -> (int,int)`A.t = "x"
                                               ^
            Error: Syntax error
            """;

        var error = Assert.Single(new OCamlCompileParser().ParseAll(Lines(output)));

        Assert.Equal(("ocaml", "compile error", "Syntax error"), (error.LanguageId, error.ExceptionType, error.Message));
        Assert.Equal(("pr7847.ml", 10, 31), (error.Frames[0].File, error.Frames[0].Line!.Value, error.Frames[0].Column!.Value));
        Assert.Equal((30, 31), OCamlCompileParser.SpanOf(error));
    }

    [Fact]
    public void AnUnmatchedBracketIsSaidWithTheErrorItCaused()
    {
        // testsuite/tests/parse-errors/unclosed_simple_expr.compilers.reference, under the compiler's own heading.
        var output = """
            File "marks.ml", line 5, characters 5-7:
            5 | (3; 2;;
                     ^^
            Error: Syntax error: ")" expected
            File "marks.ml", line 5, characters 0-1:
            5 | (3; 2;;
                ^
              This "(" might be unmatched
            """;

        var error = Assert.Single(new OCamlCompileParser().ParseAll(Lines(output)));

        Assert.Equal("Syntax error: \")\" expected\nThis \"(\" might be unmatched", error.Message);
        Assert.Equal(5, error.Frames[0].Line);
        Assert.Equal((5, 7), OCamlCompileParser.SpanOf(error));
    }

    [Fact]
    public void AMessageOverSeveralLinesKeepsItsHints()
    {
        // testsuite/tests/typing-core-bugs/missing_rec_hint.ml and const_int_hint.ml, under the compiler's own heading.
        var output = """
            File "marks.ml", line 2, characters 28-33:
            2 |    if n = 0 then 1 else n * facto (n-1)
                                            ^^^^^
            Error: Unbound value "facto"
            Hint: If this is a recursive definition,
            you should add the "rec" keyword on line 1
            """;

        var floatExpected = """
            File "marks.ml", line 1, characters 16-19:
            1 | let _ : float = 123;;
                                ^^^
            Error: This expression has type "int" but an expression was expected of type
                     "float"
              Hint: Did you mean "123."?
            """;

        var unbound = Assert.Single(new OCamlCompileParser().ParseAll(Lines(output)));
        var mismatch = Assert.Single(new OCamlCompileParser().ParseAll(Lines(floatExpected)));

        Assert.Equal("Unbound value \"facto\"\nHint: If this is a recursive definition,\nyou should add the \"rec\" keyword on line 1", unbound.Message);
        Assert.Equal(29, unbound.Frames[0].Column);
        Assert.Equal("This expression has type \"int\" but an expression was expected of type\n\"float\"\nHint: Did you mean \"123.\"?", mismatch.Message);
    }

    [Fact]
    public void WarningsAreReadApartFromErrorsWithTheirNamesAsTheirCodes()
    {
        // testsuite/tests/warnings/w32.compilers.reference and w33.compilers.reference
        var output = """
            File "w32.ml", line 40, characters 24-25:
            40 | let[@warning "-32"] rec q x = x
                                         ^
            Warning 39 [unused-rec-flag]: unused rec flag.

            File "w32.ml", line 20, characters 4-5:
            20 | let h x = x
                     ^
            Warning 32 [unused-value-declaration]: unused value h.

            File "w33.ml", line 19, characters 6-11:
            19 | let f M.(x) = x (* useless open *)
                       ^^^^^
            Warning 33 [unused-open]: unused open M.
            """;

        var warnings = OCamlCompileParser.ParseWarnings(Lines(output));

        Assert.Empty(new OCamlCompileParser().ParseAll(Lines(output)));
        Assert.Equal(new[] { "unused-rec-flag", "unused-value-declaration", "unused-open" }, warnings.Select(warning => warning.ErrorCode));
        Assert.Equal(new[] { "unused rec flag.", "unused value h.", "unused open M." }, warnings.Select(warning => warning.Message));
        Assert.Equal(new[] { 40, 20, 19 }, warnings.Select(warning => warning.Frames[0].Line!.Value));
    }

    [Fact]
    public void AnExceptionIsPlacedAtTheProgramsOwnFrameNotOCamlsLibrary()
    {
        // The frames of testsuite/tests/backtrace/backtrace.reference, with a frame of OCaml's library above them, as the
        // program's own failwith would put one there.
        var output = """
            Fatal error: exception Failure("a mark cannot be negative")
            Raised at Stdlib.failwith in file "stdlib.ml", line 29, characters 17-33
            Called from Backtrace.g in file "backtrace.ml", line 15, characters 4-11
            Called from Backtrace in file "backtrace.ml", line 21, characters 9-25
            """;

        var crash = new ParserRegistry().Parse(Lines(output));

        Assert.NotNull(crash);
        Assert.Equal(("ocaml", "Failure", "a mark cannot be negative"), (crash.LanguageId, crash.ExceptionType, crash.Message));
        Assert.Equal(FrameOrigin.Runtime, crash.Frames[0].Origin);
        Assert.Equal(("backtrace.ml", 15, 5), (crash.CulpritFrame!.File, crash.CulpritFrame.Line!.Value, crash.CulpritFrame.Column!.Value));
    }

    [Fact]
    public void ABoundsErrorRaisedByAPrimitiveOperationIsReadWithItsMessage()
    {
        // testsuite/tests/backtrace/backtrace.reference, its last exception.
        var output = """
            Fatal error: exception Invalid_argument("index out of bounds")
            Raised by primitive operation at Backtrace in file "backtrace.ml", line 21, characters 12-24
            """;

        var crash = new OCamlExceptionParser().Parse(Lines(output));

        Assert.NotNull(crash);
        Assert.Equal(("Invalid_argument", "index out of bounds"), (crash.ExceptionType, crash.Message));
        Assert.Equal(("Backtrace", 21), (crash.Frames[0].Symbol, crash.Frames[0].Line!.Value));
    }

    [Fact]
    public void AnExceptionOfTheProgramsOwnKeepsItsModuleAndWhatItCarries()
    {
        var output = """
            Fatal error: exception Backtrace.Error("b")
            Raised at Backtrace.f in file "backtrace.ml", line 11, characters 16-32
            """;

        var crash = new OCamlExceptionParser().Parse(Lines(output));

        Assert.NotNull(crash);
        Assert.Equal(("Backtrace.Error", "Error", "b"), (crash.ExceptionType, crash.ShortExceptionType, crash.Message));
    }
}
