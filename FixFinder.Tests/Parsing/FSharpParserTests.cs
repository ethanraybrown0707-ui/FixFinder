using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Tests;

/// <summary>
/// F#'s messages, as F# Interactive 15.2 (F# 10) printed them in the fixtures - captured from real runs, with the folder each
/// ran in written as C:\marks: what it refused as it checked a script, with the lines that run on under each message; the
/// warnings and notes it carried on past; and the .NET exceptions that stopped a running script, as F#'s.
/// </summary>
public class FSharpParserTests
{
    private static readonly ParserRegistry Registry = new();

    private static ParsedError Parse(string fixture)
    {
        var parsed = Registry.Parse(Fixtures.LoadStackTrace(fixture));
        Assert.NotNull(parsed);
        return parsed;
    }

    [Theory]
    [InlineData("fsharp/live-undefined-value.txt")]
    [InlineData("fsharp/live-type-mismatch.txt")]
    [InlineData("fsharp/live-list-commas.txt")]
    [InlineData("fsharp/live-warning-then-match-failure.txt")]
    [InlineData("fsharp/live-divide-in-loaded-file.txt")]
    [InlineData("fsharp/live-list-item.txt")]
    [InlineData("fsharp/live-stack-overflow.txt")]
    public void WhatFSharpPrintedIsReadAsFSharpsNotAsCSharps(string fixture) => Assert.Equal("fsharp", Parse(fixture).LanguageId);

    [Fact]
    public void WhatItRefusedIsTheErrorAtTheLineAndColumnItNamesWithTheNameItSuggests()
    {
        var error = Parse("fsharp/live-undefined-value.txt");

        Assert.Equal((FSharpParser.CompileError, "FS0039"), (error.ExceptionType, error.ErrorCode));
        Assert.Equal("The value or constructor 'totl' is not defined. Maybe you want one of the following: total", error.Message);
        Assert.Equal(("main.fsx", 3, 15), (error.Frames[0].File, error.Frames[0].Line, error.Frames[0].Column));
    }

    [Fact]
    public void TwoTypesThatDoNotMatchAreReadAsOneMessageOverItsLines()
    {
        var error = Parse("fsharp/live-type-mismatch.txt");

        Assert.Equal("This expression was expected to have type 'int' but here has type 'string'", error.Message);
        Assert.Equal(2, error.Frames[0].Line);
    }

    [Fact]
    public void ANoteBeforeAnErrorIsAWarningOfItsOwnAndTheErrorIsWhatStoppedTheCheck()
    {
        var lines = Fixtures.LoadStackTrace("fsharp/live-list-commas.txt");

        var error = Parse("fsharp/live-list-commas.txt");
        Assert.Equal(("FS0001", 2), (error.ErrorCode, error.Frames[0].Line));

        var note = Assert.Single(FSharpParser.Warnings(lines));
        Assert.Equal(("FS3886", 1, 13), (note.ErrorCode, note.Frames[0].Line!.Value, note.Frames[0].Column!.Value));
        Assert.Equal("a warning", CarriedOnPast.WhatItWas(note));
    }

    [Fact]
    public void WhatTheCheckSaysOfAScriptThatIsOneExpressionIsNotAnError()
    {
        var lines = Fixtures.LoadStackTrace("fsharp/live-one-expression.txt");

        Assert.Contains(lines, line => line.Text.EndsWith("error FS0971: Undefined value 'it'", StringComparison.Ordinal));
        Assert.Empty(new FSharpParser().ParseAll(lines));
    }

    [Fact]
    public void ACrashAfterAWarningIsTheCrashAtTheScriptsOwnLineAndTheWarningIsKept()
    {
        var lines = Fixtures.LoadStackTrace("fsharp/live-warning-then-match-failure.txt");

        var error = Parse("fsharp/live-warning-then-match-failure.txt");
        Assert.Equal("Microsoft.FSharp.Core.MatchFailureException", error.ExceptionType);
        Assert.Equal([4, 5], error.Frames.Select(frame => frame.Line!.Value));

        var warning = Assert.Single(FSharpParser.Warnings(lines));
        Assert.Equal(("FS0025", 2), (warning.ErrorCode, warning.Frames[0].Line!.Value));
    }

    [Fact]
    public void ACrashInALoadedFileIsPlacedThereAndFSharpInteractivesOwnCallsAreLeftOut()
    {
        var error = Parse("fsharp/live-divide-in-loaded-file.txt");

        Assert.Equal(("System.DivideByZeroException", "Attempted to divide by zero."), (error.ExceptionType, error.Message));
        Assert.Equal([(@"C:\marks\helpers.fsx", 2), (@"C:\marks\main.fsx", 3)], error.Frames.Select(frame => (frame.File!, frame.Line!.Value)));
        Assert.Equal(@"C:\marks\helpers.fsx", error.CulpritFrame?.File);
    }

    [Fact]
    public void FSharpsOwnLibraryIsNotTheScriptsCode()
    {
        var error = Parse("fsharp/live-list-item.txt");

        Assert.Equal("The index was outside the range of elements in the list. (Parameter 'index')", error.Message);
        Assert.True(FrameClassifier.IsVendored(error.Frames[0].File));
        Assert.Equal((@"C:\marks\main.fsx", 2), (error.CulpritFrame?.File, error.CulpritFrame?.Line));
    }

    [Fact]
    public void AStackOverflowNamesTheCallItRepeated()
    {
        var error = Parse("fsharp/live-stack-overflow.txt");

        Assert.Equal(FSharpParser.StackOverflow, error.ExceptionType);
        Assert.StartsWith("Stack overflow. Repeated ", error.Message, StringComparison.Ordinal);
        Assert.EndsWith(" times: FSI_0001.count(Int32)", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(error.Frames, frame => frame.Symbol!.StartsWith("FSharp.Compiler", StringComparison.Ordinal));
    }

    /// <summary>
    /// The same script on a GitHub runner, F# Interactive 15.2.403 for F# 10.0: .NET ran out of room in one of its own
    /// helpers, GetGCStaticBase, and listed each call - 24,006 of FSI_0001.count, cut down to 40 here - with no "Repeated"
    /// line. The call listed most often is the one it repeated.
    /// </summary>
    [Fact]
    public void AStackOverflowListedCallByCallNamesTheCallListedMostOften()
    {
        var error = Parse("fsharp/runner-stack-overflow-unrepeated.txt");

        Assert.Equal(FSharpParser.StackOverflow, error.ExceptionType);
        Assert.Equal("Stack overflow. The call it repeated: FSI_0001.count(Int32)", error.Message);
        Assert.DoesNotContain(error.Frames, frame => frame.Symbol!.StartsWith("FSharp.Compiler", StringComparison.Ordinal));
    }

    [Fact]
    public void ACSharpCrashIsStillReadAsCSharps()
    {
        var parsed = Registry.Parse(Fixtures.LoadStackTrace("csharp/inner-exception.txt"));
        Assert.Equal("csharp", parsed?.LanguageId);
    }
}
