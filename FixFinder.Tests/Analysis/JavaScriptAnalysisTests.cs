using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Reading JavaScript with FixFinder's own reader, and the checks that follow JavaScript's rules: dividing by zero gives
/// Infinity rather than failing, a position past the end gives undefined, and every array is true however empty.
/// </summary>
public class JavaScriptAnalysisTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<(IrProgram Program, IReadOnlyList<AnalysisFinding> Findings)> CheckAsync(string code, string name = "app.js")
    {
        var path = Path.Combine(_temp.Path, name);
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));

        var program = await JavaScriptFrontend.ReadAsync([path]);
        foreach (var problem in program.Problems) output.WriteLine($"problem: {problem}");
        foreach (var function in program.AllFunctions)
            output.WriteLine($"== {function.FullName}" + Environment.NewLine + IrText.Of(FixFinder.Core.Analysis.Flow.CfgBuilder.Build(function)));

        var findings = AbstractChecks.Run(program, new SourceText());
        foreach (var finding in findings) output.WriteLine($"{finding.Span.Line} {finding.CheckId} {finding.Confidence}: {finding.Message}");

        return (program, findings);
    }

    [Fact]
    public async Task AModuleIsReadIntoTheIr()
    {
        const string code = """
            const PI = 3.14;

            function area(radius) {
                return PI * radius * radius;
            }

            class Shape {
                constructor(name) {
                    this.name = name;
                }

                describe() {
                    return `${this.name} has area ${area(2)}`;
                }
            }

            const shapes = [new Shape('circle')];
            shapes.forEach((s) => console.log(s.describe()));
            """;

        var (program, _) = await CheckAsync(code);

        Assert.Empty(program.Problems);
        Assert.Contains(program.Functions, f => f.Name == "area" && f.Parameters.Count == 1);
        var shape = Assert.Single(program.Classes);
        Assert.Equal("Shape", shape.Name);
        Assert.Contains(shape.Methods, m => m is { Name: "constructor", IsConstructor: true });
        Assert.Contains(shape.Methods, m => m.Name == "describe");
        Assert.Contains(program.Functions, f => f.Name.StartsWith("arrow at line", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadingSomethingOfNothingIsFound()
    {
        const string code = """
            let person = null;
            console.log(person.name);
            """;

        var (_, findings) = await CheckAsync(code);

        var found = Assert.Single(findings, f => f.CheckId == "analysis-null-used");
        Assert.Equal(Confidence.Certain, found.Confidence);
        Assert.Contains("a TypeError", found.Message);
    }

    [Fact]
    public async Task AVariableDeclaredWithoutAValueIsNothing()
    {
        const string code = """
            function shout(word) {
                let result;
                return result.toUpperCase();
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.Contains(findings, f => f.CheckId == "analysis-null-used" && f.Span.Line == 3);
    }

    [Fact]
    public async Task DividingByZeroIsNotAMistakeInJavaScript()
    {
        const string code = """
            function average(values) {
                let total = 0;
                for (const v of values) {
                    total += v;
                }
                return total / values.length;
            }

            console.log(average([]));
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-division-by-zero");
    }

    [Fact]
    public async Task APositionPastTheEndIsNotAMistakeInJavaScript()
    {
        const string code = """
            const points = [1, 2, 3];
            console.log(points[5]);
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-index-out-of-range");
    }

    [Fact]
    public async Task AnEmptyArrayIsStillTrue()
    {
        const string code = """
            const items = [];
            if (items) {
                console.log('there is a list');
            }
            if (items.length === 0) {
                console.log('it is empty');
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.DoesNotContain(findings, f => f.CheckId is "analysis-never-true" or "analysis-always-true");
    }

    [Fact]
    public async Task ModernWritingIsRead()
    {
        const string code = """
            import fs from 'node:fs';

            export async function load(path, { encoding = 'utf8', retries = 3 } = {}) {
                const [first, ...rest] = path.split(',');
                const text = await fs.promises.readFile(first, encoding);
                const lines = text?.split('\n') ?? [];
                const counts = lines.map((line) => ({ line, length: line.length }));
                let total = 0;
                for (let i = 0; i < counts.length; i++) {
                    total += counts[i].length;
                }
                switch (rest.length) {
                    case 0:
                        break;
                    default:
                        total *= 2;
                }
                try {
                    return { total, average: total / (counts.length || 1) };
                } catch (error) {
                    console.error(`failed: ${error.message}`);
                    return null;
                } finally {
                    fs.close?.();
                }
            }
            """;

        var (program, _) = await CheckAsync(code);

        Assert.Empty(program.Problems);
        Assert.Contains(program.Functions, f => f.Name == "load" && f.IsAsync);
    }

    [Fact]
    public async Task ARegularExpressionIsNotADivision()
    {
        const string code = """
            function clean(text) {
                const tidy = text.replace(/\s+/g, ' ');
                const half = text.length / 2;
                return tidy.slice(0, half);
            }
            """;

        var (program, _) = await CheckAsync(code);

        Assert.Empty(program.Problems);
        Assert.Contains(program.Functions, f => f.Name == "clean");
    }

    /// <summary>
    /// A function written inside another that assigns the outer function's variable changes it. After the first loop i is 4
    /// and rows 3; what comes between the loops decides whether the second runs. Run with node, each program printed i, rows
    /// and more. The second loop runs, and is left alone, when rows is changed by a function declared there (11 10 0), by a
    /// function expression held in a constant (11 10 0), by an arrow function (11 10 0), and by a function expression handed
    /// to forEach (9 8 0). But for the arrow function, each was said never to run while the assignment was read as Python
    /// reads one: as the inner function declaring a rows of its own. It never runs, and is reported, when the inner function
    /// does declare a rows of its own and changes that (4 3 17), or changes only a count of its own (4 3 7).
    /// </summary>
    [Theory]
    [InlineData("function bump() { rows += 7; } bump();", false)]
    [InlineData("const bump = function () { rows = rows + 7; }; bump();", false)]
    [InlineData("const bump = () => { rows += 7; }; bump();", false)]
    [InlineData("extra.forEach(function (x) { rows += x; });", false)]
    [InlineData("function bump() { let rows = 10; rows += 7; return rows; } more = bump();", true)]
    [InlineData("function tally() { let count = 0; count += 7; return count; } more = tally();", true)]
    public async Task WhatAnInnerFunctionAssignsToAnOuterVariableIsSeenByTheLoopAfterIt(string between, bool neverRuns)
    {
        var (_, findings) = await CheckAsync($$"""
            function main() {
                const extra = [2, 3];
                let rows = 3;
                let more = 0;
                let i = 0;
                while (i <= rows) i++;
                {{between}}
                while (i <= rows) i++;
                console.log(i, rows, more);
            }

            main();
            """);

        int[] expected = neverRuns ? [8] : [];
        Assert.Equal(expected, findings.Where(f => f.CheckId == "analysis-loop-never-runs").Select(f => f.Span.Line));
    }

    /// <summary>
    /// Code at the top of a script whose variable a function declared beside it changes: run with node, the script printed
    /// 11, so the second loop runs. It was said never to, bump's rows += 7 having been read as bump declaring a rows of its own.
    /// </summary>
    [Fact]
    public async Task AFunctionThatAssignsAVariableOfTheScriptChangesIt()
    {
        var (_, findings) = await CheckAsync("""
            let rows = 3;
            let i = 0;
            while (i <= rows) i++;
            function bump() { rows += 7; }
            bump();
            while (i <= rows) i++;
            console.log(i);
            """);

        Assert.Empty(findings.Where(f => f.CheckId == "analysis-loop-never-runs"));
    }
}
