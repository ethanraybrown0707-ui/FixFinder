using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Tests;

/// <summary>
/// Elixir's messages, as Elixir 1.20.4 printed them in the fixtures: what it refused to compile, written in boxes; code it
/// could not read; what stopped a running program, with what it said on the lines under it; and the warnings it carried on past.
/// </summary>
public class ElixirParserTests
{
    private static readonly ParserRegistry Registry = new();

    private static ParsedError Parse(string fixture)
    {
        var parsed = Registry.Parse(Fixtures.LoadStackTrace(fixture));
        Assert.NotNull(parsed);
        return parsed;
    }

    [Theory]
    [InlineData("elixir/live-undefined-variable.txt")]
    [InlineData("elixir/live-two-undefined-functions.txt")]
    [InlineData("elixir/live-key-error.txt")]
    [InlineData("elixir/live-missing-do.txt")]
    [InlineData("elixir/live-access-list.txt")]
    [InlineData("elixir/live-function-clause.txt")]
    public void WhatElixirPrintedIsReadAsElixirs(string fixture) => Assert.Equal("elixir", Parse(fixture).LanguageId);

    [Fact]
    public void WhatItRefusedToCompileIsTheBoxedErrorAtTheFileLineAndColumnItNames()
    {
        var error = Parse("elixir/live-undefined-variable.txt");

        Assert.Equal(ElixirParser.CompileError, error.ExceptionType);
        Assert.Equal("undefined variable \"totl\"", error.Message);
        Assert.Equal(("main.exs", 3, 9), (error.Frames[0].File, error.Frames[0].Line, error.Frames[0].Column));
        Assert.StartsWith("    error: undefined variable", error.RawText, StringComparison.Ordinal);
        Assert.EndsWith("└─ main.exs:3:9", error.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryErrorItWroteOutIsReadWithTheFunctionItIsIn()
    {
        var all = new ElixirParser().ParseAll(Fixtures.LoadStackTrace("elixir/live-two-undefined-functions.txt"));

        Assert.Equal([2, 3], all.Select(error => error.Frames[0].Line!.Value));
        Assert.Equal(["Marks.a/1", "Marks.b/1"], all.Select(error => error.Frames[0].Symbol!));
        Assert.All(all, error => Assert.StartsWith("undefined function ", error.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void CodeItCouldNotReadKeepsItsKindItsHintAndItsPlace()
    {
        var error = Parse("elixir/live-missing-do.txt");

        Assert.Equal("SyntaxError", error.ExceptionType);
        Assert.Equal("unexpected reserved word: end", error.Message);
        Assert.Contains("hint: the \"end\" on line 4 may not have a matching \"do\"", error.RawText);
        Assert.Equal(5, error.Frames.Single().Line);
    }

    [Fact]
    public void WhatAnExceptionSaidOnTheLinesUnderItIsKeptWithItsCalls()
    {
        var error = Parse("elixir/live-key-error.txt");

        Assert.Equal(("KeyError", "key :agee not found in:"), (error.ExceptionType, error.Message));
        Assert.Contains("* :age", error.RawText);
        Assert.Equal(("main.exs", 2), (error.Frames[0].File, error.Frames[0].Line));
        Assert.Equal(FrameOrigin.Runtime, error.Frames[1].Origin);
    }

    [Fact]
    public void AParagraphOfItsOwnBeforeTheCallsDoesNotEndTheMessage()
    {
        var error = Parse("elixir/live-access-list.txt");

        Assert.StartsWith("the Access module does not support accessing lists by index", error.Message, StringComparison.Ordinal);
        Assert.Equal(2, error.Frames.Count);
        Assert.Equal(FrameOrigin.Runtime, error.Frames[0].Origin);
        Assert.Equal(("main.exs", 2), (error.Frames[1].File, error.Frames[1].Line));
    }

    [Fact]
    public void ACallWithNoFileIsACallOfItsOwn()
    {
        var error = Parse("elixir/live-function-clause.txt");

        Assert.Equal("FunctionClauseError", error.ExceptionType);
        Assert.Equal(["Grade.letter", "(file)"], error.Frames.Select(frame => frame.Symbol!));
        Assert.Equal(6, error.Frames[1].Line);
    }

    [Fact]
    public void WarningsAreEachReadOnceAndARunThatOnlyWarnedStoppedWithNothing()
    {
        var lines = Fixtures.LoadStackTrace("elixir/live-warnings.txt");

        var warnings = ElixirParser.Warnings(lines);

        Assert.Equal([3, 1], warnings.Select(warning => warning.Frames[0].Line!.Value));
        Assert.All(warnings, warning => Assert.Equal(ElixirParser.Warning, warning.ExceptionType));
        Assert.StartsWith("variable \"total\" is unused (there is a variable with the same name", warnings[0].Message, StringComparison.Ordinal);
        Assert.Null(new ElixirParser().Parse(lines));
    }
}
