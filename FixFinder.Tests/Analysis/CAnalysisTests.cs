using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Reading C and C++ with FixFinder's own reader, and the memory checks that go with them: memory used after it is
/// freed, freed twice or never freed, the address of something that is about to go, and a value read before it is set.
/// </summary>
public class CAnalysisTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<(IrProgram Program, IReadOnlyList<AnalysisFinding> Findings)> CheckAsync(string code, string name = "main.c")
    {
        var path = Path.Combine(_temp.Path, name);
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));

        var program = await CFrontend.ReadAsync([path]);
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
            #include <stdio.h>

            #define SIZE 4

            struct Point {
                int x;
                int y;
            };

            static int total(const int values[], int count)
            {
                int sum = 0;
                for (int i = 0; i < count; i++) {
                    sum += values[i];
                }
                return sum;
            }

            int main(void)
            {
                int marks[SIZE] = {70, 45, 90, 62};
                struct Point p;
                p.x = 1;
                printf("%d\n", total(marks, SIZE));
                return 0;
            }
            """;

        var (program, _) = await CheckAsync(code);

        Assert.Empty(program.Problems);
        Assert.Contains(program.Functions, f => f.Name == "total" && f.Parameters.Count == 2);
        Assert.Contains(program.Functions, f => f.Name == "main");
        Assert.Contains(program.Classes, c => c.Name == "Point" && c.Fields.Count == 2);
    }

    [Fact]
    public async Task MemoryUsedAfterItIsFreedIsFound()
    {
        const string code = """
            #include <stdlib.h>
            #include <stdio.h>

            int main(void)
            {
                int *scores = malloc(4 * sizeof(int));
                if (scores == NULL) {
                    return 1;
                }
                scores[0] = 10;
                free(scores);
                printf("%d\n", scores[0]);
                return 0;
            }
            """;

        var (_, findings) = await CheckAsync(code);

        var found = Assert.Single(findings, f => f.CheckId == "analysis-use-after-free");
        Assert.Equal(12, found.Span.Line);
        Assert.Contains("freed on line 11", found.Message);
    }

    [Fact]
    public async Task FreeingTwiceIsFound()
    {
        const string code = """
            #include <stdlib.h>

            void tidy(int count)
            {
                char *buffer = malloc(count);
                free(buffer);
                free(buffer);
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.Contains(findings, f => f.CheckId == "analysis-double-free" && f.Span.Line == 7);
    }

    [Fact]
    public async Task MemoryThatIsNeverFreedIsFound()
    {
        const string code = """
            #include <stdlib.h>

            int count_up(int n)
            {
                int *numbers = malloc(n * sizeof(int));
                int total = 0;
                for (int i = 0; i < n; i++) {
                    total += i;
                }
                return total;
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.Contains(findings, f => f.CheckId == "analysis-memory-leak");
    }

    [Fact]
    public async Task MemoryHandedBackIsNotAWarning()
    {
        const string code = """
            #include <stdlib.h>

            int *make(int n)
            {
                int *numbers = malloc(n * sizeof(int));
                return numbers;
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-memory-leak");
    }

    [Fact]
    public async Task TheAddressOfSomethingLocalIsNotHandedBack()
    {
        const string code = """
            int *counter(void)
            {
                int count = 0;
                return &count;
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.Contains(findings, f => f.CheckId == "analysis-dangling-pointer" && f.Span.Line == 4);
    }

    [Fact]
    public async Task MemoryThatMayNotHaveBeenGivenIsCheckedBeforeItIsUsed()
    {
        const string code = """
            #include <stdlib.h>

            void fill(int n)
            {
                int *numbers = malloc(n * sizeof(int));
                numbers[0] = 1;
                free(numbers);
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.Contains(findings, f => f.CheckId == "analysis-null-used" && f.Span.Line == 6);
    }

    [Fact]
    public async Task APositionPastTheEndOfAnArrayIsFound()
    {
        const string code = """
            #include <stdio.h>

            int main(void)
            {
                int marks[3] = {1, 2, 3};
                printf("%d\n", marks[5]);
                return 0;
            }
            """;

        var (_, findings) = await CheckAsync(code);

        var found = Assert.Single(findings, f => f.CheckId == "analysis-index-out-of-range");
        Assert.Contains("undefined behaviour", found.Message);
    }

    [Fact]
    public async Task DividingByACountThatCanBeZeroIsFound()
    {
        const string code = """
            int average(const int values[], int size)
            {
                int total = 0;
                int count = 0;
                for (int i = 0; i < size; i++) {
                    total += values[i];
                    count++;
                }
                return total / count;
            }
            """;

        var (_, findings) = await CheckAsync(code);

        var division = Assert.Single(findings, f => f.CheckId == "analysis-division-by-zero");
        Assert.Contains("undefined behaviour", division.Message);
    }

    [Fact]
    public async Task CppClassesAndNewAreRead()
    {
        const string code = """
            #include <vector>
            #include <string>

            class Counter {
            public:
                Counter() : count_(0) {}

                void add(int n) {
                    count_ += n;
                }

                int total() const {
                    return count_;
                }

            private:
                int count_;
            };

            int main() {
                Counter *counter = new Counter();
                counter->add(2);
                std::vector<int> marks;
                for (auto mark : marks) {
                    counter->add(mark);
                }
                delete counter;
                counter->add(1);
                return 0;
            }
            """;

        var (program, findings) = await CheckAsync(code, "main.cpp");

        Assert.Empty(program.Problems);
        Assert.Contains(program.Classes, c => c.Name == "Counter" && c.Methods.Any(m => m.Name == "add"));
        Assert.Contains(findings, f => f.CheckId == "analysis-use-after-free");
    }

    [Fact]
    public async Task AValueReadBeforeItIsSetIsFound()
    {
        const string code = """
            #include <stdio.h>

            int main(void)
            {
                int total;
                printf("%d\n", total);
                return 0;
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.Contains(findings, f => f.CheckId == "analysis-uninitialised-read" && f.Span.Line == 6);
    }
}
