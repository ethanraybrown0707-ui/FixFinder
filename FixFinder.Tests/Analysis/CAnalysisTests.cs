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

    /// <summary>
    /// Freeing a struct before the fields it points to: reaching student->name then goes through memory already gone,
    /// and the finding names the field that is reached, not only the struct.
    /// </summary>
    [Fact]
    public async Task FreeingTheFieldsOfAFreedStructIsFound()
    {
        const string code = """
            #include <stdlib.h>

            typedef struct {
                char *name;
                int *marks;
            } Student;

            void free_student(Student *student)
            {
                free(student);
                free(student->name);
                free(student->marks);
            }
            """;

        var (_, findings) = await CheckAsync(code);

        var freed = findings.Where(f => f.CheckId == "analysis-use-after-free").ToList();
        Assert.Equal([11, 12], freed.Select(f => f.Span.Line));
        Assert.Equal("`student` was freed on line 10, so `student->name` goes through memory that is no longer there - undefined behaviour", freed[0].Message);
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

    /// <summary>
    /// strtoll(text, &amp;end, 10) sets end through the address it is given, so end is no longer the NULL it started as -
    /// which is how every C program reads a number and checks what follows it.
    /// </summary>
    [Fact]
    public async Task AVariableWhoseAddressIsHandedToACallIsNoLongerKnown()
    {
        var (program, findings) = await CheckAsync("""
            #include <stdlib.h>

            int read_whole(const char *text, long long *value) {
                char *end = NULL;
                *value = strtoll(text, &end, 10);
                return end != text && *end == '\0';
            }
            """);

        Assert.Equal(["end"], Assert.Single(program.Functions).AddressTaken);
        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-null-used");
    }

    /// <summary>
    /// An address handed to a function that only reads and writes through it is not kept, so only that call changes the
    /// variable: after count = 0 and an unrelated call, count is still 0 where it divides, which is reported.
    /// </summary>
    [Fact]
    public async Task AnAddressKeptByNothingIsChangedOnlyByTheCallGivenIt()
    {
        var (program, findings) = await CheckAsync("""
            #include <stdio.h>

            static void bump(int *count) {
                (*count)++;
            }

            int divide_after_counting(void) {
                int count = 0;
                bump(&count);
                count = 0;
                printf("counted again from zero\n");
                return 100 / count;
            }
            """);

        var dividing = program.AllFunctions.Single(f => f.Name == "divide_after_counting");
        Assert.Empty(FixFinder.Core.Analysis.Abstract.HandedAddresses.Of(dividing, program).Kept);
        Assert.Contains(findings, f => f.CheckId == "analysis-division-by-zero" && f.Span.Line == 12);
    }

    /// <summary>An address that may be kept - here in a global another function writes through - can be changed by any later call.</summary>
    [Fact]
    public async Task AnAddressThatMayBeKeptIsChangedByAnyLaterCall()
    {
        var (program, findings) = await CheckAsync("""
            static int *remembered;

            static void remember(int *count) {
                remembered = count;
            }

            static void bump_remembered(void) {
                (*remembered)++;
            }

            int divide_after_remembering(void) {
                int count = 0;
                remember(&count);
                count = 0;
                bump_remembered();
                return 100 / count;
            }
            """);

        var dividing = program.AllFunctions.Single(f => f.Name == "divide_after_remembering");
        Assert.Equal(["count"], FixFinder.Core.Analysis.Abstract.HandedAddresses.Of(dividing, program).Kept);
        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-division-by-zero");
    }

    /// <summary>
    /// What changes a variable through its address - sscanf in a condition, *where = 5, (*where)++ - is followed, and a
    /// division made before anything could change it is still reported.
    /// </summary>
    [Theory]
    [InlineData("read by sscanf in the condition", false, "int f(const char *text) {\n    int x = 0;\n    if (sscanf(text, \"%d\", &x) == 1) {\n        return 100 / x;\n    }\n    return 0;\n}")]
    [InlineData("written through a pointer", false, "int f(void) {\n    int count = 0;\n    int *where = &count;\n    *where = 5;\n    return 100 / count;\n}")]
    [InlineData("incremented through a pointer", false, "int f(void) {\n    int count = 0;\n    int *where = &count;\n    (*where)++;\n    return 100 / count;\n}")]
    [InlineData("divided before the call that changes it", true, "int f(void) {\n    int count = 0;\n    int result = 100 / count;\n    scanf(\"%d\", &count);\n    return result;\n}")]
    public async Task WhatChangesAVariableThroughItsAddressIsFollowed(string shape, bool reported, string function)
    {
        var (_, findings) = await CheckAsync("#include <stdio.h>\n\n" + function + "\n");

        Assert.True(findings.Any(f => f.CheckId == "analysis-division-by-zero") == reported, $"{shape}: {string.Join("; ", findings.Select(f => f.Message))}");
    }

    /// <summary>
    /// An overloaded operator is read as a function of its own - operator* and operator() in a class, operator&lt;&lt;
    /// outside one - so what is inside it is checked like any other function, and what follows it is read as written.
    /// </summary>
    [Fact]
    public async Task OverloadedOperatorsAreReadAsFunctions()
    {
        var (program, findings) = await CheckAsync("""
            #include <ostream>

            class Ratio {
            public:
                Ratio(int top, int bottom) : top_(top), bottom_(bottom) {}

                Ratio operator*(const Ratio& other) const {
                    Ratio product(top_ * other.top_, bottom_ * other.bottom_);
                    return product;
                }

                int operator()(int scale) const {
                    int nothing = 0;
                    return scale * top_ / nothing;
                }

                int top_;
                int bottom_;
            };

            std::ostream& operator<<(std::ostream& out, const Ratio& ratio) {
                return out << ratio.top_ << '/' << ratio.bottom_;
            }

            int after_the_operators(void) {
                int zero = 0;
                return 10 / zero;
            }
            """, "main.cpp");

        Assert.Empty(program.Problems);
        Assert.Contains(program.AllFunctions, f => f.Name == "operator*" && f.Owner == "Ratio");
        Assert.Contains(program.AllFunctions, f => f.Name == "operator()" && f.Owner == "Ratio");
        Assert.Contains(program.AllFunctions, f => f.Name == "operator<<");
        Assert.Contains(findings, f => f.CheckId == "analysis-division-by-zero" && f.Span.Line == 14);
        Assert.Contains(findings, f => f.CheckId == "analysis-division-by-zero" && f.Span.Line == 27);
    }

    /// <summary>
    /// In C++, &lt;&lt; and &gt;&gt; on a stream are the stream's own operators: std::cin &gt;&gt; count gives count the
    /// number typed, and a loop reading words until the stream runs out ends when it does. A shift of a whole number is
    /// still a shift.
    /// </summary>
    [Fact]
    public async Task CppStreamOperatorsReadAndWriteRatherThanShift()
    {
        var (_, findings) = await CheckAsync("""
            #include <iostream>
            #include <sstream>
            #include <string>

            int average_of_typed(int total) {
                int count = 0;
                std::cin >> count;
                return total / count;
            }

            int words_in(const std::string &text) {
                std::istringstream words(text);
                std::string word;
                int seen = 0;
                while (words >> word) {
                    ++seen;
                }
                std::cout << "words: " << seen << '\n';
                return seen;
            }

            int shifted(int value) {
                int nothing = 0;
                return (value << 2) / nothing;
            }
            """, "main.cpp");

        var typed = Assert.Single(findings, f => f.CheckId == "analysis-division-by-zero" && f.Span.Line == 8);
        Assert.Contains("the number typed at line 7", typed.Message);
        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-loop-never-ends");
        Assert.Contains(findings, f => f.CheckId == "analysis-division-by-zero" && f.Span.Line == 24);
    }

    /// <summary>exit never returns, so the division after a guard that exits on 0 cannot be reached with 0.</summary>
    [Fact]
    public async Task ExitEndsTheWayThroughTheFunction()
    {
        var (_, findings) = await CheckAsync("""
            #include <stdio.h>
            #include <stdlib.h>

            int ratio(int total, int parts) {
                if (parts == 0) {
                    fprintf(stderr, "no parts\n");
                    exit(1);
                }
                return total / parts;
            }
            """);

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-division-by-zero");
    }

    /// <summary>With no address handed out, end is still NULL where it is read, and the finding quotes the read as C writes it.</summary>
    [Fact]
    public async Task ReadingThroughANullPointerIsQuotedAsTheCodeWritesIt()
    {
        var (_, findings) = await CheckAsync("""
            int first_letter(void) {
                char *end = NULL;
                return *end == 'x';
            }
            """);

        var finding = Assert.Single(findings, f => f.CheckId == "analysis-null-used");
        Assert.StartsWith("`end` is NULL here, so reading `*end` fails", finding.Message);
    }

    /// <summary>
    /// lower[lowerCount++] = items[i] stores and counts at once, so the loops after it that go up to lowerCount and
    /// higherCount do run - a correct quicksort, which prints 1 1 2 3 3. Stored without being counted, both counts stay 0
    /// and those two loops never run.
    /// </summary>
    [Theory]
    [InlineData("lowerCount++", "higherCount++", false)]
    [InlineData("lowerCount", "higherCount", true)]
    public async Task ACountRaisedInsideAnIndexIsSeenByTheLoopsAfterIt(string lowerAt, string higherAt, bool neverRun)
    {
        var (_, findings) = await CheckAsync($$"""
            #include <stdio.h>

            static void quicksort(int *items, int count) {
                if (count <= 1) return;
                int pivot = items[0];
                int lower[16], higher[16];
                int lowerCount = 0, higherCount = 0;
                for (int i = 1; i < count; i++) {
                    if (items[i] < pivot) {
                        lower[{{lowerAt}}] = items[i];
                    } else if (items[i] >= pivot) {
                        higher[{{higherAt}}] = items[i];
                    }
                }
                quicksort(lower, lowerCount);
                quicksort(higher, higherCount);
                int at = 0;
                for (int i = 0; i < lowerCount; i++) items[at++] = lower[i];
                items[at++] = pivot;
                for (int i = 0; i < higherCount; i++) items[at++] = higher[i];
            }

            int main(void) {
                int numbers[] = {3, 1, 2, 3, 1};
                quicksort(numbers, 5);
                for (int i = 0; i < 5; i++) {
                    printf(i > 0 ? " %d" : "%d", numbers[i]);
                }
                printf("\n");
                return 0;
            }
            """);

        int[] expected = neverRun ? [18, 20] : [];
        Assert.Equal(expected, findings.Where(f => f.CheckId == "analysis-loop-never-runs").Select(f => f.Span.Line));
    }

    /// <summary>
    /// An array has the size its brackets give, however few values it is set up with: int done[N] = {0} has N places, the
    /// rest zero - it was once read as one place, so done[i] in a loop up to N was said to fail. Only without a size do
    /// the values give it, a string's with the zero that ends it. A place past the size is still found.
    /// </summary>
    [Theory]
    [InlineData("main.c", "int done[N] = {0};", "done[4]", false)]
    [InlineData("main.c", "int done[N] = {0};", "done[5]", true)]
    [InlineData("main.c", "double weights[3] = {1.5};", "weights[2]", false)]
    [InlineData("main.c", "double weights[3] = {1.5};", "weights[3]", true)]
    [InlineData("main.c", "int big[1000] = {0};", "big[999]", false)]
    [InlineData("main.c", "int big[1000] = {0};", "big[1000]", true)]
    [InlineData("main.c", "int grid[3][3] = {{0}};", "grid[2][0]", false)]
    [InlineData("main.c", "int marks[5] = {[4] = 1};", "marks[4]", false)]
    [InlineData("main.c", "char name[20] = \"abc\";", "name[19]", false)]
    [InlineData("main.c", "char name[20] = \"abc\";", "name[20]", true)]
    [InlineData("main.c", "char word[] = \"abc\";", "word[3]", false)]
    [InlineData("main.c", "char word[] = \"abc\";", "word[4]", true)]
    [InlineData("main.c", "int points[] = {3, 5, 8};", "points[3]", true)]
    [InlineData("main.cpp", "int slots[4]{};", "slots[3]", false)]
    [InlineData("main.cpp", "int slots[4]{};", "slots[4]", true)]
    public async Task AnArrayHasTheSizeItsBracketsGiveHoweverFewValuesItIsSetUpWith(string name, string declaration, string read, bool outside)
    {
        var (_, findings) = await CheckAsync($$"""
            #define N 5

            int main(void) {
                {{declaration}}
                return {{read}};
            }
            """, name);

        int[] expected = outside ? [5] : [];
        Assert.Equal(expected, findings.Where(f => f.CheckId == "analysis-index-out-of-range").Select(f => f.Span.Line));
    }

    /// <summary>
    /// A shortest-path search that marks each place done in an array set up as {0}: every round finds the nearest place not
    /// yet done, and the program prints 0 3 1 4. Nothing in it can fail.
    /// </summary>
    [Fact]
    public async Task AShortestPathSearchOverAnArraySetUpWithOneZeroIsLeftAlone()
    {
        var (_, findings) = await CheckAsync("""
            #include <stdio.h>

            #define N 4
            #define INF 1000000000

            int main(void) {
                int graph[N][N] = {{0, 4, 1, 0}, {0, 0, 0, 1}, {0, 2, 0, 5}, {0, 0, 0, 0}};
                int dist[N] = {0, INF, INF, INF};
                int done[N] = {0};
                for (int round = 0; round < N; round++) {
                    int node = -1;
                    for (int i = 0; i < N; i++) {
                        if (!done[i] && (node < 0 || dist[i] < dist[node])) node = i;
                    }
                    done[node] = 1;
                    for (int next = 0; next < N; next++) {
                        if (graph[node][next] > 0 && dist[node] + graph[node][next] < dist[next]) dist[next] = dist[node] + graph[node][next];
                    }
                }
                for (int i = 0; i < N; i++) printf(i > 0 ? " %d" : "%d", dist[i]);
                printf("\n");
                return 0;
            }
            """);

        Assert.Empty(findings);
    }

    /// <summary>
    /// A C++ reference is another name for its variable: int&amp; n takes the caller's variable, int&amp; alias = rows is rows.
    /// The reader keeps which parameters can be written through - not a const one, but a const int*&amp; whose pointer
    /// can - and writes each argument a call may change through one as a value the call sets, as it does C#'s ref.
    /// </summary>
    [Fact]
    public async Task TheCppReaderKeepsWhatAReferenceIsAnotherNameFor()
    {
        var (program, _) = await CheckAsync("""
            #include <string>
            #include <vector>

            void take(int& a, const int& b, int c, std::vector<int>& v, const std::string& s, const int*& p) {
                a = b + c + v.size() + s.size() + *p;
            }

            int main() {
                int x = 1, y = 2, z = 3;
                std::vector<int> items;
                std::string name;
                const int* cursor = &x;
                int& alias = x;
                auto&& again = y;
                take(x, y, z, items, name, cursor);
                return alias + again;
            }
            """, "main.cpp");

        var take = program.Functions.Single(function => function.Name == "take");
        Assert.Equal([true, false, false, true, false, true], take.Parameters.Select(parameter => parameter.ByReference));

        var main = program.Functions.Single(function => function.Name == "main");
        var call = IrWalk.Statements(main.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Within).OfType<Call>().Single(c => c.CalleeName == "take");
        Assert.Equal([0, 3, 5], call.Arguments.Select((argument, index) => (argument, index)).Where(pair => pair.argument.Value is AssignValue).Select(pair => pair.index));

        var borrowed = IrWalk.Statements(main.Body).OfType<Declare>().Where(declare => declare.Lifetime == Lifetime.Borrowed).Select(declare => declare.Variable);
        Assert.Equal(["alias", "again"], borrowed);
    }

    /// <summary>
    /// A variable changed through a reference: rows is 3 when the first loop ends with i at 4, then alias = 10 sets it,
    /// and grow, which takes int&amp; n, adds 5. Built with g++ the program prints 16, so the second loop runs - it was once
    /// said never to, the reader having written int&amp; alias = rows as a copy and grow(rows) as changing nothing.
    /// </summary>
    [Fact]
    public async Task AVariableChangedThroughAReferenceIsNotTakenToHoldWhatItHeldBefore()
    {
        var (_, findings) = await CheckAsync("""
            #include <cstdio>

            void grow(int& n) { n = n + 5; }

            int main() {
                int rows = 3;
                int& alias = rows;
                int i = 0;
                while (i <= rows) i++;
                alias = 10;
                grow(rows);
                while (i <= rows) i++;
                std::printf("%d\n", i);
                return 0;
            }
            """, "main.cpp");

        Assert.Empty(findings);
    }

    /// <summary>
    /// After the first loop i is 4 and rows 3; what comes between the loops decides whether the second runs. Built with g++,
    /// each program printed i, alias and more. The second loop runs, and is left alone, when rows is changed through
    /// grow's int&amp; n (9 8 20), through alias (11 10 20), through a reference to alias (11 10 20), through one bound to
    /// rows or more, whichever is smaller (11 10 20), by std::swap (21 20 3), by std::tie (11 10 30), by sscanf given
    /// alias's address (11 10 20), by a lambda held in a variable that takes int&amp; (9 8 20), or by a grow defined
    /// after main (9 8 20). It never runs, and is reported, when grow takes a copy (4 3 20), defined before main or after
    /// it; when a function takes rows as const int&amp; (4 3 8); and when rows is raised and then lowered through alias
    /// (4 2 20), which was missed while alias was taken for a copy.
    /// </summary>
    [Theory]
    [InlineData("void grow(int& n) { n = n + 5; }", "grow(rows);", "", false)]
    [InlineData("void grow(int n) { n = n + 5; }", "grow(rows);", "", true)]
    [InlineData("int grown(const int& n) { return n + 5; }", "more = grown(rows);", "", true)]
    [InlineData("// nothing else", "alias = 10;", "", false)]
    [InlineData("// nothing else", "rows = 10; alias = 2;", "", true)]
    [InlineData("// nothing else", "int& again = alias; again = 10;", "", false)]
    [InlineData("// nothing else", "int& smaller = rows < more ? rows : more; smaller = 10;", "", false)]
    [InlineData("// nothing else", "std::swap(rows, more);", "", false)]
    [InlineData("// nothing else", "std::tie(rows, more) = std::make_pair(10, 30);", "", false)]
    [InlineData("// nothing else", "std::sscanf(\"10\", \"%d\", &alias);", "", false)]
    [InlineData("// nothing else", "auto add = [](int& n) { n += 5; }; add(rows);", "", false)]
    [InlineData("void grow(int& n);", "grow(rows);", "void grow(int& n) { n = n + 5; }", false)]
    [InlineData("void grow(int n);", "grow(rows);", "void grow(int n) { n = n + 5; }", true)]
    public async Task WhatIsWrittenThroughACppReferenceIsSeenByTheLoopAfterIt(string before, string between, string after, bool neverRuns)
    {
        var (_, findings) = await CheckAsync($$"""
            #include <cstdio>
            #include <tuple>
            #include <utility>

            {{before}}

            int main() {
                int rows = 3;
                int more = 20;
                int& alias = rows;
                int i = 0;
                while (i <= rows) i++;
                {{between}}
                while (i <= rows) i++;
                std::printf("%d %d %d\n", i, alias, more);
                return 0;
            }
            {{after}}
            """, "main.cpp");

        int[] expected = neverRuns ? [14] : [];
        Assert.Equal(expected, findings.Where(f => f.CheckId == "analysis-loop-never-runs").Select(f => f.Span.Line));
    }

    /// <summary>
    /// A const reference to a number of another type is bound to a copy made from it: wide keeps 3 when rows becomes 10, so
    /// the second loop never runs - built with g++ the program printed 4 3 10 - and is reported as such.
    /// </summary>
    [Fact]
    public async Task AConstReferenceToANumberOfAnotherTypeIsACopy()
    {
        var (_, findings) = await CheckAsync("""
            #include <cstdio>

            int main() {
                int rows = 3;
                const long& wide = rows;
                int i = 0;
                while (i <= wide) i++;
                rows = 10;
                while (i <= wide) i++;
                std::printf("%d %ld %d\n", i, wide, rows);
                return 0;
            }
            """, "main.cpp");

        Assert.Equal([9], findings.Where(f => f.CheckId == "analysis-loop-never-runs").Select(f => f.Span.Line));
    }

    /// <summary>
    /// x is handed unset to a function taking int&amp;, which sets it: both programs printed 5 built with g++. Handing x on is
    /// still taken as reading it - grow does read it, init does not, and neither is looked into - but once the call has
    /// set x, printing it is not reading a variable never given a value, as it was said to be while the call was taken to
    /// change nothing.
    /// </summary>
    [Theory]
    [InlineData("void init(int& v) { v = 5; }", "init(x);")]
    [InlineData("void grow(int& n) { n = n + 5; }", "grow(x);")]
    public async Task AVariableSetThroughAnIntRefParameterIsSetOnceTheCallReturns(string function, string call)
    {
        var (_, findings) = await CheckAsync($$"""
            #include <cstdio>

            {{function}}

            int main() {
                int x;
                {{call}}
                std::printf("%d\n", x);
                return 0;
            }
            """, "main.cpp");

        Assert.Equal([7], findings.Where(f => f.CheckId == "analysis-uninitialised-read").Select(f => f.Span.Line));
    }

    /// <summary>
    /// A pointer handed to a lambda held in a variable is still handed on: freed first, it is still memory used after it
    /// was freed (built with g++, one run printed 455704432, not the 4 it held) - though the call is now taken to be able to
    /// give p another value.
    /// </summary>
    [Fact]
    public async Task AFreedPointerHandedToALambdaInAVariableIsStillUsedAfterItIsFreed()
    {
        var (_, findings) = await CheckAsync("""
            #include <cstdio>

            int main() {
                int* p = new int(4);
                auto show = [](int* q) { std::printf("%d\n", *q); };
                delete p;
                show(p);
                return 0;
            }
            """, "main.cpp");

        Assert.Equal([7], findings.Where(f => f.CheckId == "analysis-use-after-free").Select(f => f.Span.Line));
    }

    /// <summary>
    /// p holds x's address, and bump, a lambda held in a variable, writes 10 through it: built with g++ the program prints
    /// 10, so the second loop runs. Handing p on still makes x unknown, however the call is now written.
    /// </summary>
    [Fact]
    public async Task APointerHandedToALambdaInAVariableStillLeavesWhatItPointsAtUnknown()
    {
        var (_, findings) = await CheckAsync("""
            #include <cstdio>

            int main() {
                int x = 3;
                int* p = &x;
                auto bump = [](int* q) { *q = 10; };
                int i = 0;
                while (i < x) i++;
                bump(p);
                while (i < x) i++;
                std::printf("%d\n", i);
                return 0;
            }
            """, "main.cpp");

        Assert.Empty(findings);
    }
}
