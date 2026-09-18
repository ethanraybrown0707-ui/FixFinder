using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>Which outputs genuinely hold more than one error, and which only look as if they might.</summary>
public class MultiErrorTests
{
    private static readonly ParserRegistry Registry = new();

    private static ParsedError Parse(string fixture)
    {
        var parsed = Registry.Parse(Fixtures.LoadStackTrace(fixture));
        Assert.NotNull(parsed);
        return parsed!;
    }

    private static IReadOnlyList<ParsedError> Others(string fixture)
    {
        var lines = Fixtures.LoadStackTrace(fixture);
        return Registry.Others(Parse(fixture), lines);
    }

    [Fact]
    public void ABuildWithTwoErrorsOffersTheSecondOne()
    {
        var reported = Parse("msvc/two-errors.txt");
        var others = Others("msvc/two-errors.txt");

        Assert.Equal("C2065", reported.ErrorCode);

        var next = Assert.Single(others);
        Assert.Equal("C2143", next.ErrorCode);
        Assert.Equal(41, next.Frames[0].Line);
    }

    [Fact]
    public void WarningsAreNotOfferedAsErrorsToSkipTo()
    {
        Assert.DoesNotContain(Others("msvc/two-errors.txt"), e => e.ErrorCode == "C4101");
    }

    [Fact]
    public void TheErrorAlreadyReportedIsExcluded()
    {
        var reported = Parse("msvc/two-errors.txt");

        Assert.DoesNotContain(Others("msvc/two-errors.txt"), e => e.ErrorCode == reported.ErrorCode);
    }

    [Fact]
    public void ASingleCompilerErrorLeavesNothingToSkipTo()
    {
        Assert.Empty(Others("msvc/cs0103.txt"));
    }

    [Theory]
    [InlineData("python/keyerror.txt")]
    [InlineData("python/chained.txt")]
    [InlineData("python/syntax-error.txt")]
    [InlineData("csharp/inner-exception.txt")]
    [InlineData("java/caused-by.txt")]
    [InlineData("go/panic.txt")]
    public void ACrashedProgramHasNothingToSkipTo(string fixture)
    {
        Assert.Empty(Others(fixture));
    }

    [Fact]
    public void ACausedByChainIsStillOneError()
    {
        var reported = Parse("java/caused-by.txt");

        Assert.NotEmpty(reported.Causes);
        Assert.Empty(Others("java/caused-by.txt"));
    }

    [Fact]
    public void TwoTracebacksInOneLogDoNotBecomeTwoProblems()
    {
        Assert.Empty(Others("adversarial/two-python-tracebacks.txt"));
    }
}
