using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Checking;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>Reading Go with its own parser, and the checks that follow Go's rules: a panic, a nil pointer, a nil slice that is not null.</summary>
public class GoAnalysisTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<(IrProgram Program, IReadOnlyList<AnalysisFinding> Findings)?> CheckAsync(string code, string name = "main.go")
    {
        if (GoFrontend.FindGo() is not { } go) return null;

        var path = Path.Combine(_temp.Path, name);
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));

        var program = await GoFrontend.ReadAsync([path], go);
        foreach (var problem in program.Problems) output.WriteLine($"problem: {problem}");

        foreach (var function in program.AllFunctions)
            output.WriteLine($"== {function.FullName}" + Environment.NewLine + IrText.Of(FixFinder.Core.Analysis.Flow.CfgBuilder.Build(function)));

        var findings = AbstractChecks.Run(program, new SourceText());
        foreach (var finding in findings) output.WriteLine($"{finding.Span.Line} {finding.CheckId} {finding.Confidence}: {finding.Message}");

        return (program, findings);
    }

    [Fact]
    public async Task AProgramIsReadIntoTheIr()
    {
        const string code = """
            package main

            import "fmt"

            func average(values []float64) float64 {
            	total := 0.0
            	for _, v := range values {
            		total += v
            	}
            	return total / float64(len(values))
            }

            func main() {
            	fmt.Println(average([]float64{1, 2, 3}))
            }
            """;

        if (await CheckAsync(code) is not { } read) return;

        Assert.Empty(read.Program.Problems);
        var average = Assert.Single(read.Program.Functions, f => f.Name == "average");
        Assert.Equal("double", average.ReturnType.Name);
        Assert.Equal("values", Assert.Single(average.Parameters).Name);
        output.WriteLine(IrText.Of(FixFinder.Core.Analysis.Flow.CfgBuilder.Build(average)));
    }

    [Fact]
    public async Task DividingByACountThatCanBeZeroIsFound()
    {
        const string code = """
            package main

            import "fmt"

            func average(values []int) int {
            	total := 0
            	count := 0
            	for _, v := range values {
            		total += v
            		count++
            	}
            	return total / count
            }

            func main() {
            	fmt.Println(average([]int{}))
            }
            """;

        if (await CheckAsync(code) is not { } read) return;

        Assert.Contains(read.Findings, f => f.CheckId == "analysis-division-by-zero");
    }

    [Fact]
    public async Task ANilSliceIsNotNull()
    {
        const string code = """
            package main

            import "fmt"

            func main() {
            	var names []string
            	fmt.Println(len(names))
            	for _, name := range names {
            		fmt.Println(name)
            	}
            }
            """;

        if (await CheckAsync(code) is not { } read) return;

        Assert.DoesNotContain(read.Findings, f => f.CheckId == "analysis-null-used");
    }

    [Fact]
    public async Task APointerThatIsNilIsFoundWhereItIsRead()
    {
        const string code = """
            package main

            import "fmt"

            type Person struct {
            	Name string
            }

            func main() {
            	var p *Person
            	fmt.Println(p.Name)
            }
            """;

        if (await CheckAsync(code) is not { } read) return;

        Assert.Contains(read.Findings, f => f.CheckId == "analysis-null-used" && f.Confidence == Confidence.Certain);
    }
    [Fact]
    public async Task WhatSomeoneTypesIsFollowedIntoTheDivision()
    {
        const string code = """
            package main

            import "fmt"

            func main() {
            	var count int
            	fmt.Scan(&count)
            	fmt.Println(100 / count)
            }
            """;

        if (await CheckAsync(code) is not { } read) return;

        var division = Assert.Single(read.Findings, f => f.CheckId == "analysis-division-by-zero");
        Assert.Contains("the number typed at line 7 is 0", division.Message);
    }

    [Fact]
    public async Task APanicIsAGuardOnWhatTheFunctionTakes()
    {
        const string code = """
            package main

            import "fmt"

            func root(x float64) float64 {
            	if x < 0 {
            		panic("no root of a negative number")
            	}
            	return x / 2
            }

            func main() {
            	fmt.Println(root(-4))
            }
            """;

        if (await CheckAsync(code) is not { } read) return;

        Assert.Contains(read.Findings, f => f.CheckId == "analysis-contract-broken");
    }

    [Fact]
    public async Task TwoGoroutinesUpdatingACountIsALostUpdate()
    {
        const string code = """
            package main

            import (
            	"fmt"
            	"sync"
            )

            func main() {
            	var wg sync.WaitGroup
            	count := 0
            	for i := 0; i < 100; i++ {
            		wg.Add(1)
            		go func() {
            			defer wg.Done()
            			count++
            		}()
            	}
            	wg.Wait()
            	fmt.Println(count)
            }
            """;

        if (await CheckAsync(code) is not { } read) return;

        Assert.Contains(read.Findings, f => f.CheckId == "analysis-lost-update");
    }

    [Fact]
    public async Task ALockReleasedByDeferIsNotReportedAsLeftLocked()
    {
        const string code = """
            package main

            import "sync"

            type Counter struct {
            	mu    sync.Mutex
            	count int
            }

            func (c *Counter) Add(n int) int {
            	c.mu.Lock()
            	defer c.mu.Unlock()
            	if n < 0 {
            		return c.count
            	}
            	c.count += n
            	return c.count
            }
            """;

        if (await CheckAsync(code) is not { } read) return;

        Assert.DoesNotContain(read.Findings, f => f.CheckId == "analysis-lock-not-released");
    }

    [Fact]
    public async Task ALockLeftLockedOnOneWayOutIsFound()
    {
        const string code = """
            package main

            import "sync"

            type Counter struct {
            	mu    sync.Mutex
            	count int
            }

            func (c *Counter) Add(n int) int {
            	c.mu.Lock()
            	if n < 0 {
            		return c.count
            	}
            	c.count += n
            	c.mu.Unlock()
            	return c.count
            }
            """;

        if (await CheckAsync(code) is not { } read) return;

        Assert.Contains(read.Findings, f => f.CheckId == "analysis-lock-not-released");
    }

    [Fact]
    public async Task AMapReadWhenTheMapIsNilIsNotReported()
    {
        const string code = """
            package main

            import "fmt"

            func main() {
            	var counts map[string]int
            	fmt.Println(counts["a"])
            }
            """;

        if (await CheckAsync(code) is not { } read) return;

        Assert.Empty(read.Findings);
    }
}
