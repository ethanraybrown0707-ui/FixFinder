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

    /// <summary>
    /// Checking that an allocation worked is not a leak. Down the branch the check takes there is nothing allocated to
    /// free, and reporting it there would flag the idiom in every careful C program - the one written precisely to stop
    /// the mistake being reported.
    /// </summary>
    [Fact]
    public async Task CheckingThatAllocationWorkedIsNotALeak()
    {
        const string code = """
            #include <stdlib.h>

            int total(int n)
            {
                int *numbers = malloc(n * sizeof(int));

                if (numbers == NULL) {
                    return 0;
                }

                numbers[0] = 1;
                int first = numbers[0];

                free(numbers);
                return first;
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-memory-leak");
    }

    /// <summary>The other side of it: following the branch must not lose a leak down the path where the memory is real.</summary>
    [Fact]
    public async Task MemoryKeptPastTheAllocationCheckIsStillALeak()
    {
        const string code = """
            #include <stdlib.h>

            int total(int n)
            {
                int *numbers = malloc(n * sizeof(int));

                if (numbers == NULL) {
                    return 0;
                }

                numbers[0] = 1;
                return 1;
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.Contains(findings, f => f.CheckId == "analysis-memory-leak");
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
    public async Task AnElementOfALocalArrayIsNotHandedBack()
    {
        const string code = """
            int *first_score(void)
            {
                int scores[3] = {90, 75, 60};
                return &scores[0];
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.Contains(findings, f => f.CheckId == "analysis-dangling-pointer" && f.Span.Line == 4);
    }

    [Fact]
    public async Task TheAddressOfSomethingThatOutlivesTheFunctionIsSafeToHandBack()
    {
        const string code = """
            int total = 0;

            int *running_total(void)
            {
                return &total;
            }

            int *call_count(void)
            {
                static int calls;
                calls++;
                return &calls;
            }

            int *second_slot(void)
            {
                int *slots = malloc(4 * sizeof(int));
                return &slots[1];
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-dangling-pointer");
        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-uninitialised-read");
    }

    [Fact]
    public async Task AStaticLocalIsSetOnceAndKeepsItsValueBetweenCalls()
    {
        const string code = """
            int share_of_calls(void)
            {
                static int calls = 0;
                int share = 0;
                if (calls > 0)
                {
                    share = 100 / calls;
                }
                calls++;
                return share;
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.DoesNotContain(findings, f => f.CheckId is "analysis-never-true" or "analysis-always-true");
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

    /// <summary>
    /// A number takes the type of the variable it is kept in: 0 in a double divides into infinity, which is no crash,
    /// while 0.5 in an int is 0, which is.
    /// </summary>
    [Fact]
    public async Task ANumberTakesTheTypeOfTheVariableItIsKeptIn()
    {
        const string code = """
            #include <stdio.h>

            int main(void)
            {
                double share = 0;
                printf("%f\n", 10 / share);
                int whole = 0.5;
                printf("%d\n", 10 / whole);
                return 0;
            }
            """;

        var (_, findings) = await CheckAsync(code);

        var division = Assert.Single(findings, f => f.CheckId == "analysis-division-by-zero");
        Assert.Equal(8, division.Span.Line);
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

    /// <summary>
    /// A variable whose address a loop's condition hands to a call - while (next(&amp;value)) - is filled in by that call,
    /// as it would be by the same call written as a statement.
    /// </summary>
    [Fact]
    public async Task AVariableFilledInThroughALoopConditionIsNotUnset()
    {
        const string code = """
            #include <stdio.h>

            int next(int *value);

            int main(void)
            {
                int value;
                while (next(&value)) {
                    printf("%d\n", value);
                }
                if (next(&value) && value > 0) {
                    printf("%d\n", value);
                }
                return 0;
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-uninitialised-read");
    }

    /// <summary>
    /// Freeing a field frees what the field points at, not the struct holding it: after free(vector->items) the vector is
    /// still there to clear, and freeing an entry's key and then the entry frees each of them once.
    /// </summary>
    [Fact]
    public async Task FreeingAFieldLeavesItsHolderAlone()
    {
        const string code = """
            #include <stdlib.h>

            typedef struct {
                int *items;
                int length;
            } Vector;

            typedef struct Entry {
                char *key;
                struct Entry *next;
            } Entry;

            void vector_free(Vector *vector)
            {
                free(vector->items);
                vector->items = NULL;
                vector->length = 0;
            }

            void entry_free(Entry *entry)
            {
                free(entry->key);
                free(entry);
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.DoesNotContain(findings, f => f.CheckId is "analysis-use-after-free" or "analysis-double-free");
    }

    /// <summary>What a field pointed at is still gone when it is reached through that field again.</summary>
    [Fact]
    public async Task UsingWhatAFieldPointedAtAfterFreeingItIsFound()
    {
        const string code = """
            #include <stdlib.h>

            typedef struct {
                int *items;
                int length;
            } Vector;

            int first(Vector *vector)
            {
                free(vector->items);
                return vector->items[0];
            }
            """;

        var (_, findings) = await CheckAsync(code);

        var used = Assert.Single(findings, f => f.CheckId == "analysis-use-after-free");
        Assert.Equal(11, used.Span.Line);
        Assert.StartsWith("`vector->items` was freed on line 10", used.Message);
    }

    /// <summary>C stores with an expression too - heads[0] = entry; - and the memory stored is the array's to free from then on.</summary>
    [Fact]
    public async Task MemoryStoredInSomethingElseIsNotLost()
    {
        const string code = """
            #include <stdlib.h>

            typedef struct Entry {
                int key;
                struct Entry *next;
            } Entry;

            static Entry *heads[4];

            int add(int key)
            {
                Entry *entry = malloc(sizeof(Entry));
                if (entry == NULL) {
                    return -1;
                }
                entry->key = key;
                entry->next = heads[0];
                heads[0] = entry;
                return 0;
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-memory-leak");
    }

    /// <summary>
    /// A name declared again - in a second loop, or after a loop that declared it - is a variable of its own. The second
    /// loop's i starts from 0 whatever the first left in its i, and the entry made after the loop is not the loop's own
    /// entry, which is NULL by then.
    /// </summary>
    [Fact]
    public async Task ANameDeclaredAgainIsAVariableOfItsOwn()
    {
        const string code = """
            #include <stdlib.h>

            typedef struct Entry {
                int key;
                struct Entry *next;
            } Entry;

            int total(void)
            {
                int big[5] = {1, 2, 3, 4, 5};
                int small[3] = {1, 2, 3};
                int sum = 0;
                for (int i = 0; i < 5; i++) {
                    sum += big[i];
                }
                for (int i = 0; i < 3; i++) {
                    sum += small[i];
                }
                return sum;
            }

            Entry *push(Entry *head, int key)
            {
                for (Entry *entry = head; entry != NULL; entry = entry->next) {
                    if (entry->key == key) {
                        return head;
                    }
                }
                Entry *entry = malloc(sizeof(Entry));
                if (entry == NULL) {
                    return head;
                }
                entry->key = key;
                entry->next = head;
                return entry;
            }
            """;

        var (_, findings) = await CheckAsync(code);

        Assert.Empty(findings);
    }

    /// <summary>C++ written the modern way is read whole: structured bindings, templates closed with >>, and catch clauses.</summary>
    [Fact]
    public async Task ModernCppIsReadWhole()
    {
        const string code = """
            #include <map>
            #include <memory>
            #include <stdexcept>
            #include <string>
            #include <utility>

            int count(const std::map<std::string, std::unique_ptr<int>> &values)
            {
                int total = 0;
                for (const auto &[name, value] : values) {
                    total += *value;
                }
                auto [smallest, largest] = std::make_pair(1, 2);
                try {
                    total += largest - smallest;
                } catch (const std::out_of_range &problem) {
                    total = -1;
                } catch (...) {
                    total = -2;
                }
                return total;
            }
            """;

        var (program, _) = await CheckAsync(code, "main.cpp");

        Assert.Empty(program.Problems);
        var body = IrWalk.Statements(Assert.Single(program.Functions, f => f.Name == "count").Body).ToList();
        Assert.Contains(body, statement => statement is Try { Handlers.Count: 2 });
        Assert.Contains(body, statement => statement is ForEach { Target: CollectionLiteral { Items.Count: 2 } });
    }

    /// <summary>
    /// Declarations C++ writes with a namespace, a template, a reference, braces or inside an if are declarations - not
    /// comparisons like std::vector &lt; Item &gt; items - and a lambda is a function of its own inside the one around it.
    /// </summary>
    [Fact]
    public async Task ModernCppDeclarationsAndLambdasAreRead()
    {
        const string code = """
            #include <algorithm>
            #include <map>
            #include <optional>
            #include <string>
            #include <vector>

            struct Item {
                std::string name;
                int price;
            };

            std::optional<int> price_of(const std::vector<Item> &items, const std::string &name)
            {
                auto found = std::find_if(items.begin(), items.end(), [&name](const Item &item) { return item.name == name; });
                if (found == items.end()) {
                    return std::nullopt;
                }
                return found->price;
            }

            int total()
            {
                std::vector<Item> items{{"pen", 3}, {"pad", 5}};
                std::map<std::string, int>::size_type kinds = 2;
                Item &first = items[0];
                int sum = first.price;
                if (auto price = price_of(items, "pad")) {
                    sum += *price;
                }
                if (auto price = price_of(items, "pen"); price.has_value()) {
                    sum += *price;
                }
                std::sort(items.begin(), items.end(), [](const Item &left, const Item &right) { return left.price < right.price; });
                return sum + static_cast<int>(kinds);
            }
            """;

        var (program, _) = await CheckAsync(code, "main.cpp");

        Assert.Empty(program.Problems);
        var declared = IrWalk.Statements(Assert.Single(program.Functions, f => f.Name == "total").Body).OfType<Declare>().Select(d => d.Variable).ToList();
        Assert.Equal(["items", "kinds", "first", "sum", "price", "price'"], declared);

        var lambdas = program.Functions.Where(f => f.Name.StartsWith("lambda at line", StringComparison.Ordinal)).ToList();
        Assert.Equal(["price_of", "total"], lambdas.Select(lambda => lambda.EnclosedBy));
        Assert.Equal(["item"], lambdas[0].Parameters.Select(p => p.Name));
    }
}
