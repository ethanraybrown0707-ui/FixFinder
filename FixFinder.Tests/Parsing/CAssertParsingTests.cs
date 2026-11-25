using FixFinder.Core.Execution;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Tests;

/// <summary>
/// What a C or C++ program prints when it stops at an assert of its own: Windows' C runtime's words, as a program gcc built
/// printed them here, and the GNU C library's - read as the program stopping at that assert, on its line.
/// </summary>
public class CAssertParsingTests
{
    private static List<CapturedLine> Printed(params string[] lines) =>
        lines.Select((text, index) => new CapturedLine(index, StreamKind.StdErr, text, TimeSpan.Zero)).ToList();

    [Theory]
    [InlineData(@"Assertion failed: total(marks, 3) == 210, file C:\work\marks.c, line 14", @"C:\work\marks.c", null)]
    [InlineData("marks: marks.c:14: main: Assertion `total(marks, 3) == 210' failed.", "marks.c", "main")]
    public void AnAssertAProgramStoppedAtIsReadWithItsLine(string printed, string file, string? function)
    {
        var parser = new CAssertParser();
        var error = parser.Parse(Printed("Totals so far:", printed));

        Assert.Equal(90, parser.Detect([printed]));
        Assert.NotNull(error);
        Assert.Equal(CAssertParser.AssertionFailed, error!.ExceptionType);
        Assert.Equal("total(marks, 3) == 210", error.Message);
        var stopped = Assert.Single(error.Frames);
        Assert.Equal((file, 14, function), (stopped.File, stopped.Line ?? 0, stopped.Symbol));
    }

    [Fact]
    public void OutputThatOnlyMentionsAnAssertIsNotOne()
    {
        var parser = new CAssertParser();
        string[] printed = ["Assertion failed: the marks did not add up", "Checking assert(total == 210) by hand"];

        Assert.Equal(0, parser.Detect(printed));
        Assert.Null(parser.Parse(Printed(printed)));
    }
}
