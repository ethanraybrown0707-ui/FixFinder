using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// What a try statement does to the code around it when something inside it fails: a finally block that returns throws
/// the error away, and a name given its value only inside the try has none after the try failed. Each is checked beside
/// the versions that are fine - a handler that leaves, or gives the name a value; a read behind a flag the try set.
/// </summary>
public class ExceptionFlowTests(ITestOutputHelper output) : IDisposable
{
    private static readonly string[] FlowChecks = [ExceptionFlow.FinallyOverridesRule, ExceptionFlow.UnassignedRule];

    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Python(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, "flow.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Flow(AbstractChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Java(string code)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var path = Path.Combine(_temp.Path, code.Split("public class ")[1].Split(' ', '{')[0] + ".java");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Flow(AbstractChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText()));
    }

    private List<AnalysisFinding> Flow(IEnumerable<AnalysisFinding> findings)
    {
        var found = findings.Where(f => FlowChecks.Contains(f.CheckId)).ToList();
        foreach (var finding in found) output.WriteLine($"line {finding.Span.Line} {finding.CheckId}: {finding.Message}");
        return found;
    }

    private static string Summary(IEnumerable<AnalysisFinding> findings) => string.Join("; ", findings.Select(f => $"line {f.Span.Line}: {f.Message}"));

    private static void Says(string expected, AnalysisFinding finding) =>
        Assert.True(finding.Message.Contains(expected, StringComparison.Ordinal), $"expected \"{expected}\" in: {finding.Message}");

    [Fact]
    public async Task AReturnInFinallyThrowsTheErrorAway()
    {
        const string code = """
            def load(path):
                try:
                    return open(path).read()
                finally:
                    return ""
            """;
        if (await Python(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(ExceptionFlow.FinallyOverridesRule, found.CheckId);
        Assert.Equal(5, found.Span.Line);
        Says("the error is thrown away without a trace", found);
    }

    [Fact]
    public async Task ABreakLeavingFinallyThrowsTheErrorAway()
    {
        const string code = """
            def process_all(paths):
                for path in paths:
                    try:
                        print(open(path).read())
                    finally:
                        break
            """;
        if (await Python(code) is not { } findings) return;

        Assert.Equal(6, Assert.Single(findings).Span.Line);
    }

    /// <summary>The break belongs to the loop inside the finally block, and leaves only that loop.</summary>
    [Fact]
    public async Task ABreakFromALoopInsideFinallyIsFine()
    {
        const string code = """
            def tidy(items):
                try:
                    print(items[0])
                finally:
                    for item in items:
                        if item:
                            break
            """;
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task InJavaAReturnInFinallyThrowsTheErrorAway()
    {
        const string code = """
            public class Load {
                static int parse(String text) {
                    try {
                        return Integer.parseInt(text);
                    } finally {
                        return 0;
                    }
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        Assert.Equal(6, Assert.Single(findings).Span.Line);
    }

    private const string Parse = """
        def parse(text):
            try:
                number = int(text)
            except ValueError:
                print("not a number")
            return number * 2
        """;

    /// <summary>int() failed, the except block carried on, and number was never given a value.</summary>
    [Fact]
    public async Task ANameOnlyTheTryGivesAValueHasNoneAfterTheTryFailed()
    {
        if (await Python(Parse) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(ExceptionFlow.UnassignedRule, found.CheckId);
        Assert.Equal(6, found.Span.Line);
        Says("`number` has no value here if the try block failed before line 3: the except block on line 4 carries on without giving it one", found);
        Says("UnboundLocalError", found);
    }

    [Theory]
    [InlineData("        print(\"not a number\")", "        return 0")]
    [InlineData("        print(\"not a number\")", "        number = 0")]
    public async Task AHandlerThatLeavesOrGivesAValueIsFine(string original, string replacement)
    {
        var repaired = Parse.Replace(original, replacement, StringComparison.Ordinal);
        Assert.NotEqual(Parse, repaired);

        if (await Python(repaired) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task ANameGivenAValueBeforeTheTryAlwaysHasOne()
    {
        const string code = """
            def parse(text):
                number = 0
                try:
                    number = int(text)
                except ValueError:
                    print("not a number")
                return number
            """;
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>Behind a flag only the successful try sets, the read cannot happen after a failure - so it is not reported.</summary>
    [Fact]
    public async Task AReadBehindAFlagTheTrySetIsNotReported()
    {
        const string code = """
            def parse(text):
                ok = True
                try:
                    number = int(text)
                except ValueError:
                    ok = False
                if ok:
                    print(number)
            """;
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>open() failed, so handle was never given a value - and the finally block's handle.close() hides the real error.</summary>
    [Fact]
    public async Task AFinallyBlockReadingANameTheTryNeverGaveHidesTheRealError()
    {
        const string code = """
            def first_line(path):
                try:
                    handle = open(path)
                    return handle.readline()
                finally:
                    handle.close()
            """;
        if (await Python(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(6, found.Span.Line);
        Says("hiding the error that stopped the try", found);
    }
}
