using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// Which outputs genuinely hold more than one error, and which only look as if they might.
/// </summary>
/// <remarks>
/// The distinction is the whole feature. A compiler reports everything it found and exits, so the
/// second diagnostic is really there to be looked up while the first is still unfixed. A program
/// that crashed has exactly one error, because the first one ended it - offering to move past it
/// would be offering to move somewhere that does not exist.
/// </remarks>
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

    // ------------------------------------------------------------------ compilers

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

    /// <summary>A warning did not stop the build, so it is not something to move on to.</summary>
    [Fact]
    public void WarningsAreNotOfferedAsErrorsToSkipTo()
    {
        Assert.DoesNotContain(Others("msvc/two-errors.txt"), e => e.ErrorCode == "C4101");
    }

    /// <summary>The one already on screen must not come back as something new to look at.</summary>
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

    // ------------------------------------------------------------------ crashes

    /// <summary>
    /// A crash has one error however much output surrounds it.
    /// </summary>
    /// <remarks>
    /// The honest answer, and the reason Skip is greyed out rather than hidden: the program
    /// stopped, so whatever would have failed next has not happened yet and no parser could find
    /// it. Offering to step past would be offering something the runtime cannot provide.
    /// </remarks>
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

    /// <summary>
    /// A chain is one failure at several depths, not several failures.
    /// </summary>
    /// <remarks>
    /// Causes belong to the error they explain and are already carried on it; returning them here
    /// would offer to "move on" to the inner half of the thing already being shown.
    /// </remarks>
    [Fact]
    public void ACausedByChainIsStillOneError()
    {
        var reported = Parse("java/caused-by.txt");

        Assert.NotEmpty(reported.Causes);
        Assert.Empty(Others("java/caused-by.txt"));
    }

    /// <summary>
    /// Two unrelated tracebacks in one log are still not two things to act on.
    /// </summary>
    /// <remarks>
    /// Python has no multi-error parser precisely because the second traceback in a log was
    /// printed and survived - only the last one ended the program. Treating the other as pending
    /// work would send the user to fix something that already ran to completion.
    /// </remarks>
    [Fact]
    public void TwoTracebacksInOneLogDoNotBecomeTwoProblems()
    {
        Assert.Empty(Others("adversarial/two-python-tracebacks.txt"));
    }
}
