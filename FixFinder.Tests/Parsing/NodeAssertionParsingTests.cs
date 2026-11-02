using FixFinder.Core.Execution;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Tests;

/// <summary>
/// node:assert's AssertionError, as Node.js 24.19 printed it here: its message goes on over several lines before its stack -
/// the comparison strictEqual prints, or the diff deepStrictEqual does - and it is read as the error the program stopped
/// with, on the line of the program's own code that made the check.
/// </summary>
public class NodeAssertionParsingTests
{
    private static List<CapturedLine> Printed(params string[] lines) =>
        lines.Select((text, index) => new CapturedLine(index, StreamKind.StdErr, text, TimeSpan.Zero)).ToList();

    [Fact]
    public void AStrictEqualThatFailedIsReadWithTheLineThatMadeTheCheck()
    {
        var error = new NodeStackTraceParser().Parse(Printed(
            "node:assert:152",
            "  throw new AssertionError(obj);",
            "  ^",
            "",
            "AssertionError [ERR_ASSERTION]: the total of 60, 70 and 80",
            "",
            "150 !== 210",
            "",
            @"    at Object.<anonymous> (C:\work\total.js:11:8)",
            "    at Module._compile (node:internal/modules/cjs/loader:1872:14)",
            "    at node:internal/main/run_main_module:33:47 {",
            "  generatedMessage: false,",
            "  code: 'ERR_ASSERTION',",
            "  actual: 150,",
            "  expected: 210,",
            "  operator: 'strictEqual'",
            "}"));

        Assert.NotNull(error);
        Assert.Equal("AssertionError", error!.ExceptionType);
        Assert.Equal("ERR_ASSERTION", error.ErrorCode);
        Assert.Equal("the total of 60, 70 and 80", error.Message);
        var own = Assert.Single(error.Frames, frame => frame.File is { } file && file.EndsWith("total.js", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(11, own.Line);
    }

    [Fact]
    public void ADeepStrictEqualWhoseDiffRunsOverManyLinesIsReadToo()
    {
        var error = new NodeStackTraceParser().Parse(Printed(
            "node:assert:152",
            "  throw new AssertionError(obj);",
            "  ^",
            "",
            "AssertionError [ERR_ASSERTION]: Expected values to be strictly deep-equal:",
            "+ actual - expected",
            "",
            "  [",
            "    1,",
            "    2,",
            "+   3",
            "-   4",
            "  ]",
            "",
            @"    at Object.<anonymous> (C:\work\deep.js:2:8)",
            "    at Module._compile (node:internal/modules/cjs/loader:1872:14)",
            "    at node:internal/main/run_main_module:33:47 {",
            "  generatedMessage: true,",
            "  code: 'ERR_ASSERTION',",
            "}"));

        Assert.NotNull(error);
        Assert.Equal("AssertionError", error!.ExceptionType);
        Assert.Equal(2, Assert.Single(error.Frames, frame => frame.File is { } file && file.EndsWith("deep.js", StringComparison.OrdinalIgnoreCase)).Line);
    }

    /// <summary>Any other error is still read only when its stack follows it at once, as before.</summary>
    [Fact]
    public void AnotherErrorFollowedByOtherOutputIsNotTakenForOne()
    {
        var error = new NodeStackTraceParser().Parse(Printed(
            "TypeError: this is only a line the program printed",
            "and another",
            "and a third",
            @"    at Object.<anonymous> (C:\work\app.js:3:1)"));

        Assert.Null(error);
    }
}
