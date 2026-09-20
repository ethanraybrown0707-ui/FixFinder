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
}
