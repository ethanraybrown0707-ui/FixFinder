using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Tests;

/// <summary>
/// Erlang's messages, as Erlang/OTP 29 printed them in the fixtures: erlc's errors and warnings, each at its file, line and
/// column, and what stopped a program, as FixFinder's starting module prints it - Erlang's own words, given the name of its
/// reason, and the functions it was in.
/// </summary>
public class ErlangParserTests
{
    private static readonly ParserRegistry Registry = new();

    private static ParsedError Parse(string fixture)
    {
        var parsed = Registry.Parse(Fixtures.LoadStackTrace(fixture));
        Assert.NotNull(parsed);
        return parsed;
    }

    [Theory]
    [InlineData("erlang/live-unbound.txt")]
    [InlineData("erlang/live-missing-period.txt")]
    [InlineData("erlang/live-badarith.txt")]
    [InlineData("erlang/live-function-clause.txt")]
    [InlineData("erlang/live-badarg.txt")]
    [InlineData("erlang/live-nth.txt")]
    [InlineData("erlang/live-undef.txt")]
    public void WhatErlangPrintedIsReadAsErlangs(string fixture) => Assert.Equal("erlang", Parse(fixture).LanguageId);

    [Fact]
    public void ACompileErrorIsReadAtItsFileLineAndColumnWithErlcsSuggestion()
    {
        var error = Parse("erlang/live-unbound.txt");

        Assert.Equal(ErlangParser.CompileError, error.ExceptionType);
        Assert.Equal("variable 'Totl' is unbound, did you mean 'Total'?", error.Message);
        Assert.Equal(("marks.erl", 7, 24), (error.Frames[0].File, error.Frames[0].Line, error.Frames[0].Column));
        Assert.Contains("%    7|", error.RawText);
    }

    [Fact]
    public void AWarningIsReadApartFromTheErrors()
    {
        var lines = Fixtures.LoadStackTrace("erlang/live-unbound.txt");

        var warning = Assert.Single(ErlangParser.ParseWarnings(lines));
        Assert.Equal((ErlangParser.CompileWarning, "variable 'Total' is unused", 6), (warning.ExceptionType, warning.Message, warning.Frames[0].Line!.Value));
        Assert.Single(new ErlangParser().ParseAll(lines));
    }

    [Fact]
    public void EveryErrorErlcGaveIsRead()
    {
        var all = new ErlangParser().ParseAll(Fixtures.LoadStackTrace("erlang/live-missing-period.txt"));

        Assert.Equal(["syntax error before: main", "function main/0 undefined"], all.Select(error => error.Message!));
        Assert.Equal([6, 2], all.Select(error => error.Frames[0].Line!.Value));
    }

    [Fact]
    public void WhatStoppedTheProgramIsGivenErlangsNameForItAndItsFunctions()
    {
        var error = Parse("erlang/live-badarith.txt");

        Assert.Equal(("badarith", "an error occurred when evaluating an arithmetic expression"), (error.ExceptionType, error.Message));
        Assert.Equal(["marks:average/1", "marks:main/0"], error.Frames.Select(frame => frame.Symbol!));
        Assert.Equal([4, 8], error.Frames.Select(frame => frame.Line!.Value));
    }

    [Fact]
    public void TheClauseNoClauseMatchedIsPlacedFromTheMessageItself()
    {
        var error = Parse("erlang/live-function-clause.txt");

        Assert.Equal(("function_clause", "no function clause matching marks:letter(20)"), (error.ExceptionType, error.Message));
        Assert.Equal(("marks.erl", 4), (error.Frames.Single().File, error.Frames.Single().Line));
    }

    [Fact]
    public void ABuiltInsArgumentIsKeptAndTheBuiltInIsErlangsOwn()
    {
        var error = Parse("erlang/live-badarg.txt");

        Assert.Equal("badarg", error.ExceptionType);
        Assert.Contains("called as list_to_integer(\"12\\n\")", error.RawText);
        Assert.Equal(FrameOrigin.Runtime, error.Frames[0].Origin);
        Assert.Equal(("marks.erl", 4), (error.Frames[1].File, error.Frames[1].Line));
    }

    [Fact]
    public void AnErlangModulesClauseIsErlangsOwnAndTheProgramsLineFollows()
    {
        var error = Parse("erlang/live-nth.txt");

        Assert.Equal("no function clause matching lists:nth(0,\"FP\")", error.Message);
        Assert.Equal(FrameOrigin.Runtime, error.Frames[0].Origin);
        Assert.Equal(("marks:show/1", 4), (error.Frames[1].Symbol, error.Frames[1].Line));
    }

    [Fact]
    public void AnUndefinedFunctionIsUndef()
    {
        var error = Parse("erlang/live-undef.txt");

        Assert.Equal(("undef", "undefined function lists:summ/1"), (error.ExceptionType, error.Message));
        Assert.Equal(5, error.Frames.Single().Line);
    }
}
