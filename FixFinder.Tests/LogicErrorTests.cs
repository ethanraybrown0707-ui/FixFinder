using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// Logic errors - programs that run to the end and do the wrong thing: the comparison with expected output, the
/// suspiciousness formulas, the small edits tried, the mistakes recognised in code, and whole programs fixed for real.
/// </summary>
public class LogicErrorTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private SourceFile Source(string name, string text)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, text);
        return SourceFile.Read(path)!;
    }

    // ------------------------------------------------------------------ comparing output

    [Theory]
    [InlineData("1\n2\n3\n", "1\r\n2  \r\n3\r\n\r\n", null)]
    [InlineData("1\n2\n", "1\n3\n", "line 2 was \"2\" where \"3\" was expected")]
    [InlineData("1\n2\n3\n", "1\n2\n", "it printed an extra line 3, \"3\", after everything expected")]
    [InlineData("1\n", "1\n2\n", "it stopped after 1 line, where line 2 should have been \"2\"")]
    [InlineData("5.0\n", "5\n", "line 1 was \"5.0\" where \"5\" was expected")]
    public void OutputIsComparedLineByLineIgnoringOnlyTrailingSpace(string printed, string expected, string? difference)
    {
        var mismatch = OutputComparison.Compare(OutputComparison.Normalise(printed), expected);

        Assert.Equal(difference, mismatch?.Describe());
    }

    [Fact]
    public void AnExpectedRunWithNothingInItIsNotAClaim()
    {
        var expected = ExpectedBehaviour.From([new ExpectedRun("3", "  \n"), new ExpectedRun(null, "6")], "  ");

        Assert.Single(expected.Runs);
        Assert.Null(expected.Arguments);
    }

    // ------------------------------------------------------------------ the formulas

    [Fact]
    public void TheFormulasGiveTheirTextbookValues()
    {
        // ef = 2 of 2 failing runs, ep = 1 of 3 passing runs.
        Assert.Equal(2 / Math.Sqrt(2 * 3), Suspiciousness.Ochiai(2, 1, 2), 10);
        Assert.Equal(1.0 / (1.0 + 1.0 / 3), Suspiciousness.Tarantula(2, 1, 2, 3), 10);
        Assert.Equal(4.0 / 1, Suspiciousness.DStar(2, 1, 2), 10);

        Assert.Equal(0, Suspiciousness.Ochiai(0, 3, 2));
        Assert.Equal(double.PositiveInfinity, Suspiciousness.DStar(2, 0, 2));
    }

    [Fact]
    public void ALineOnlyTheWrongRunsExecuteIsMostSuspicious()
    {
        IReadOnlyList<(bool Passed, IReadOnlySet<int> Lines)> spectra =
        [
            (true, new HashSet<int> { 1, 2, 3, 5 }),
            (true, new HashSet<int> { 1, 2, 3, 5 }),
            (false, new HashSet<int> { 1, 2, 4, 5 }),
        ];

        var ranked = Suspiciousness.Rank(spectra);

        Assert.Equal(4, ranked[0].Line);
        Assert.Equal(1.0, ranked[0].Ochiai, 10);
        Assert.DoesNotContain(ranked, s => s.Line == 3);
    }

    // ------------------------------------------------------------------ the edits tried

    [Fact]
    public void AComparisonDecidingALoopIsTheFirstEditTried()
    {
        var source = Source("app.py", "total = 0\nwhile i < n:\n    total += i\n");

        var first = Mutations.For(source, 2)[0];

        Assert.Equal("<=", first.To);
        Assert.Equal("while i <= n:", first.NewText);
    }

    [Theory]
    [InlineData("app.py", "for i in range(len(word) - 1):", "for i in range(len(word)):")]
    [InlineData("App.cs", "        highest = Math.Min(highest, mark);", "        highest = Math.Max(highest, mark);")]
    [InlineData("app.py", "average = sum(scores) // len(scores)", "average = sum(scores) / len(scores)")]
    [InlineData("App.java", "        double average = sum / count;", "        double average = (double) sum / count;")]
    [InlineData("app.py", "    return year % 4 == 0 or year % 100 != 0", "    return year % 4 == 0 and year % 100 != 0")]
    public void TheEditThatCorrectsACommonMistakeIsAmongThoseTried(string file, string line, string corrected)
    {
        var source = Source(file, line + "\n");

        Assert.Contains(Mutations.For(source, 1), m => m.NewText == corrected);
    }

    [Fact]
    public void NothingInsideAStringOrACommentIsEdited()
    {
        var source = Source("app.py", "print(\"1 < 2\")  # 3 > 4\n");

        Assert.DoesNotContain(Mutations.For(source, 1), m => m.From is "<" or ">" || m.From is "1" or "2" or "3" or "4");
    }

    // ------------------------------------------------------------------ mistakes the code shows by itself

    [Theory]
    [InlineData("logic-python-return-in-loop", "app.py", "def has(items, x):\n    for item in items:\n        if item == x:\n            return True\n        else:\n            return False\n", "    return False")]
    [InlineData("logic-python-return-in-loop", "app.py", "def find(items, x):\n    for item in items:\n        if item == x:\n            return item\n        else:\n            return item\n", null)]
    [InlineData("logic-python-reset-in-loop", "app.py", "for p in prices:\n    total = 0\n    total += p\nprint(total)\n", "total = 0|for p in prices:")]
    [InlineData("logic-python-reset-in-loop", "app.py", "for p in prices:\n    total = 0\n    total += p\n    print(total)\n", null)]
    [InlineData("logic-python-mutable-default", "app.py", "def add(x, items=[]):\n    items.append(x)\n    return items\n", "def add(x, items=None):|    if items is None:|        items = []")]
    [InlineData("logic-python-mutable-default", "app.py", "def first(items=[]):\n    return items[0] if items else None\n", null)]
    [InlineData("logic-python-result-discarded", "app.py", "name = input()\nname.strip()\n", "name = name.strip()")]
    [InlineData("logic-python-result-discarded", "app.py", "name = get_name()\nname.strip()\n", null)]
    [InlineData("logic-python-is-literal", "app.py", "if count is 5:\n    pass\n", "if count == 5:")]
    [InlineData("logic-python-assert-tuple", "app.py", "assert (total == 7, \"total should be 7\")\n", "assert total == 7, \"total should be 7\"")]
    [InlineData("logic-python-comparison-statement", "app.py", "total == 0\n", "total = 0")]
    [InlineData("logic-python-loop-never-advances", "app.py", "i = 0\nwhile i < 3:\n    print(i)\n", "    i += 1")]
    [InlineData("logic-python-loop-never-advances", "app.py", "i = 0\nwhile i < 3:\n    i = step(i)\n", null)]
    [InlineData("logic-integer-division", "App.java", "        int sum = 7;\n        int count = 2;\n        double average = sum / count;\n", "        double average = (double) sum / count;")]
    [InlineData("logic-integer-division", "App.java", "        double sum = 7;\n        int count = 2;\n        double average = sum / count;\n", null)]
    [InlineData("logic-empty-loop-body", "app.c", "for (i = 0; i < 3; i++);\n{\n    total++;\n}\n", "for (i = 0; i < 3; i++)")]
    [InlineData("logic-empty-loop-body", "app.c", "do {\n    i++;\n}\nwhile (i < 3);\n", null)]
    [InlineData("logic-empty-if-body", "App.java", "        if (x > 3);\n        {\n            go();\n        }\n", "        if (x > 3)")]
    [InlineData("logic-java-string-equals", "App.java", "        String answer = in.nextLine();\n        if (answer == \"yes\") {\n", "        if (answer.equals(\"yes\")) {")]
    [InlineData("logic-java-string-equals", "App.java", "        int answer = 3;\n        if (answer == 4) {\n", null)]
    [InlineData("logic-result-discarded", "Program.cs", "        string name = \"ada\";\n        name.ToUpper();\n", "        name = name.ToUpper();")]
    [InlineData("logic-result-discarded", "app.js", "const name = \"ada\";\nname.toUpperCase();\n", null)]
    [InlineData("logic-assignment-in-condition", "app.c", "    if (items = 0) {\n", "    if (items == 0) {")]
    [InlineData("logic-assignment-in-condition", "app.js", "if (items = 0) {\n", "if (items === 0) {")]
    [InlineData("logic-bitwise-precedence", "app.c", "    if (flags & 1 == 0) {\n", "    if ((flags & 1) == 0) {")]
    [InlineData("logic-switch-fallthrough", "app.cpp", "switch (d) {\n    case 1:\n        name = \"Mon\";\n    case 2:\n        name = \"Tue\";\n        break;\n}\n", "        break;")]
    [InlineData("logic-switch-fallthrough", "app.cpp", "switch (d) {\n    case 1:\n        count++;\n        // falls through\n    case 2:\n        count++;\n        break;\n}\n", null)]
    [InlineData("logic-uninitialised-total", "app.c", "int main(void) {\n    int sum;\n    for (int i = 0; i < 3; i++) sum += i;\n}\n", "    int sum = 0;")]
    [InlineData("logic-uninitialised-total", "app.c", "int main(void) {\n    int sum;\n    scanf(\"%d\", &sum);\n}\n", null)]
    [InlineData("logic-string-literal-modified", "app.c", "int main(void) {\n    char *name = \"hello\";\n    name[0] = 'H';\n}\n", "    char name[] = \"hello\";")]
    [InlineData("logic-c-string-equals", "app.c", "#include <stdio.h>\n\nint main(void) {\n    char answer[] = \"yes\";\n    if (answer == \"yes\") {\n", "#include <string.h>|    if (strcmp(answer, \"yes\") == 0) {")]
    [InlineData("logic-c-string-equals", "app.cpp", "#include <string>\n\nint main() {\n    std::string answer = \"yes\";\n    if (answer == \"yes\") {\n", null)]
    [InlineData("logic-cpp-catch-by-value", "app.cpp", "    } catch (std::exception e) {\n", "    } catch (const std::exception& e) {")]
    [InlineData("logic-cpp-catch-by-value", "app.cpp", "    } catch (const std::exception& e) {\n", null)]
    [InlineData("logic-cpp-non-virtual-destructor", "app.cpp", "class Animal {\npublic:\n    ~Animal() {}\n    virtual void speak() = 0;\n};\n\nint main() {\n    Animal* pet = new Dog();\n    delete pet;\n}\n", "    virtual ~Animal() {}")]
    [InlineData("logic-cpp-non-virtual-destructor", "app.cpp", "class Animal {\npublic:\n    virtual ~Animal() {}\n    virtual void speak() = 0;\n};\n\nint main() {\n    Animal* pet = new Dog();\n    delete pet;\n}\n", null)]
    [InlineData("logic-js-var-in-closure", "app.js", "for (var i = 0; i < 3; i++) {\n  setTimeout(() => console.log(i), 10);\n}\n", "for (let i = 0; i < 3; i++) {")]
    [InlineData("logic-js-numeric-sort", "app.js", "const scores = [9, 10, 100];\nscores.sort();\n", "scores.sort((a, b) => a - b);")]
    [InlineData("logic-js-numeric-sort", "app.js", "const names = [\"b\", \"a\"];\nnames.sort();\n", null)]
    [InlineData("logic-js-map-parseint", "app.js", "const numbers = [\"1\", \"2\"].map(parseInt);\n", "const numbers = [\"1\", \"2\"].map(Number);")]
    [InlineData("logic-return-in-loop", "App.java", "    static boolean has(int[] a, int x) {\n        for (int v : a) {\n            if (v == x) {\n                return true;\n            } else {\n                return false;\n            }\n        }\n        return false;\n    }\n", "        return false;")]
    [InlineData("logic-reset-in-loop", "app.js", "for (const p of prices) {\n  total = 0;\n  total += p;\n}\nconsole.log(total);\n", "total = 0;|for (const p of prices) {")]
    [InlineData("logic-loop-never-advances", "App.java", "        int i = 0;\n        while (i < 3) {\n            System.out.println(i);\n        }\n", "            i++;")]
    public void EachLogicMistakeIsFoundAndFixedOrLeftAlone(string pattern, string file, string code, string? expected)
    {
        var source = Source(file, code);

        var finding = LogicPatterns.Scan(source).FirstOrDefault(f => f.PatternId == pattern);

        if (expected is null)
        {
            Assert.Null(finding);
            return;
        }

        Assert.NotNull(finding);
        var fixedLines = finding!.Fix.ApplyTo(source)!;

        foreach (var part in expected.Split('|'))
            Assert.Contains(part, fixedLines);
    }

    [Fact]
    public void ABranchAnEarlierConditionCoversCanBeMovedFirst()
    {
        var python = Source("app.py", "for n in range(1, 16):\n    if n % 3 == 0:\n        print(\"Fizz\")\n    elif n % 15 == 0:\n        print(\"FizzBuzz\")\n    else:\n        print(n)\n");

        var moved = BranchOrder.For(python, new HashSet<int> { 2 }).Single();

        Assert.Equal(
            ["    if n % 15 == 0:", "        print(\"FizzBuzz\")", "    elif n % 3 == 0:", "        print(\"Fizz\")", "    else:", "        print(n)"],
            moved.NewLines);

        var java = Source("App.java", "        if (n % 3 == 0) {\n            a();\n        } else if (n % 15 == 0) {\n            b();\n        } else {\n            c();\n        }\n");

        Assert.Equal(
            "        if (n % 15 == 0) {|            b();|        } else if (n % 3 == 0) {|            a();|        } else {|            c();|        }",
            string.Join("|", BranchOrder.For(java, new HashSet<int> { 1 }).Single().ApplyTo(java)!));
    }

    [Fact]
    public void AWidenedFixChangesNothingButItsContext()
    {
        var source = Source("main.go", "func a() {\n\tif x {\n\t}\n}\n\nfunc b() {\n\tif y {\n\t}\n}\n");
        var fix = LocalFix.Insert("test", "t", "e", source.Path, 4, ["\tclose(ch)"]);

        var widened = fix.Unambiguous(source);

        Assert.Equal(fix.ApplyTo(source), widened.ApplyTo(source));
        Assert.True(widened.RemoveCount > 0);
    }

    // ------------------------------------------------------------------ live

    /// <summary>The toolchain, the program, its files, the one run, the expected runs, the answer's id, and text the copied fix must contain.</summary>
    public static TheoryData<string, string, Dictionary<string, string>, string, (string? Input, string Output)[], string, string> Programs => new()
    {
        {
            "python", "binary search that misses the last item",
            new() { ["app.py"] = "def search(items, target):\n    low, high = 0, len(items) - 1\n    while low < high:\n        mid = (low + high) // 2\n        if items[mid] == target:\n            return mid\n        if items[mid] < target:\n            low = mid + 1\n        else:\n            high = mid - 1\n    return -1\n\n\nvalues = [2, 5, 8, 12, 16]\nfor target in [2, 8, 16, 7]:\n    print(search(values, target))\n" },
            "app.py", [(null, "0\n2\n4\n-1\n")], "local:logic-edit", "while low <= high:"
        },
        {
            "python", "vowels counted one short, two runs",
            new() { ["app.py"] = "word = input()\ncount = 0\nfor i in range(len(word) - 1):\n    if word[i] in \"aeiou\":\n        count += 1\nprint(count)\n" },
            "app.py", [("banana\n", "3\n"), ("tree\n", "2\n")], "local:logic-python-range-skips-last", "for i in range(len(word)):"
        },
        {
            "python", "leap year with or",
            new() { ["app.py"] = "def is_leap(year):\n    return year % 4 == 0 or year % 100 != 0 or year % 400 == 0\n\n\nfor year in [1900, 2000, 2023, 2024]:\n    print(year, is_leap(year))\n" },
            "app.py", [(null, "1900 False\n2000 True\n2023 False\n2024 True\n")], "local:logic-edit", "year % 4 == 0 and year % 100 != 0"
        },
        {
            "python", "FizzBuzz testing % 3 before % 15",
            new() { ["app.py"] = "for n in range(1, 16):\n    if n % 3 == 0:\n        print(\"Fizz\")\n    elif n % 5 == 0:\n        print(\"Buzz\")\n    elif n % 15 == 0:\n        print(\"FizzBuzz\")\n    else:\n        print(n)\n" },
            "app.py", [(null, "1\n2\nFizz\n4\nBuzz\nFizz\n7\n8\nFizz\nBuzz\n11\nFizz\n13\n14\nFizzBuzz\n")], "local:logic-edit", "    if n % 15 == 0:\n        print(\"FizzBuzz\")\n    elif n % 3 == 0:"
        },
        {
            "python", "search that stops at the first item",
            new() { ["app.py"] = "def contains(items, target):\n    for item in items:\n        if item == target:\n            return True\n        else:\n            return False\n\n\nprint(contains([3, 7, 9], 9))\n" },
            "app.py", [], "local:logic-python-return-in-loop", "    return False"
        },
        {
            "node", "loop one past the end",
            new() { ["app.js"] = "const names = [\"ada\", \"alan\", \"grace\"];\nfor (let i = 0; i <= names.length; i++) {\n  console.log(names[i]);\n}\n" },
            "app.js", [(null, "ada\nalan\ngrace\n")], "local:logic-off-by-one-length", "i < names.length"
        },
        {
            "java", "sum that skips the first value",
            new() { ["App.java"] = "public class App {\n    public static void main(String[] args) {\n        int[] values = {4, 8, 15, 16};\n        int total = 0;\n        for (int i = 1; i < values.length; i++) {\n            total += values[i];\n        }\n        System.out.println(total);\n    }\n}\n" },
            "App.java", [(null, "43\n")], "local:logic-edit", "for (int i = 0; i < values.length; i++)"
        },
        {
            "java", "strings compared with ==",
            new() { ["App.java"] = "import java.util.Scanner;\n\npublic class App {\n    public static void main(String[] args) {\n        Scanner in = new Scanner(System.in);\n        String answer = in.nextLine();\n        if (answer == \"yes\") {\n            System.out.println(\"agreed\");\n        }\n    }\n}\n" },
            "App.java", [], "local:logic-java-string-equals", "if (answer.equals(\"yes\"))"
        },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheLogicMistakeIsFixedLive(
        string toolchain, string program, Dictionary<string, string> files, string chosen, (string? Input, string Output)[] runs, string id, string expected)
    {
        if (!LocalFixLiveTests.Available(toolchain)) return;

        using var temp = new TempFolder();
        foreach (var (name, text) in files) File.WriteAllText(Path.Combine(temp.Path, name), text);

        var plan = TargetFactory.FromFile(Path.Combine(temp.Path, chosen), TimeSpan.FromMinutes(3));
        Assert.True(plan.Ok, plan.Problem);

        var typed = runs.Length > 0 ? runs[0].Input : "yes\n";
        if (typed is not null) plan = plan with { Spec = plan.Spec!.WithInput(typed) };

        using var http = new FixFinderHttpClient();
        var session = new FixFinderSession(http, new FixSourceRegistry())
        {
            Expected = runs.Length > 0 ? new ExpectedBehaviour(runs.Select(r => new ExpectedRun(r.Input, r.Output)).ToList()) : null,
        };

        var outcome = await session.RunAsync(plan, new SearchBudget(Cache: CacheMode.CacheOnly));

        if (ApplicationControl.Refused(outcome)) return;
        Assert.True(outcome.Result == SessionResult.FoundFix, $"{program}: {outcome.Result} - {outcome.Headline} - {outcome.Detail}");
        Assert.Equal(id, outcome.Best!.Id);

        var copied = PasteableFix.For(new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));

        Assert.NotNull(copied);
        Assert.Contains(expected, copied!.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProgramThatPrintsWhatWasExpectedIsLeftAlone()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var path = Path.Combine(_temp.Path, "app.py");
        File.WriteAllText(path, "print(sum([1, 2, 3]))\n");

        using var http = new FixFinderHttpClient();
        var session = new FixFinderSession(http, new FixSourceRegistry()) { Expected = new ExpectedBehaviour([new ExpectedRun(null, "6\n")]) };

        var outcome = await session.RunAsync(TargetFactory.FromFile(path), new SearchBudget(Cache: CacheMode.CacheOnly));

        if (ApplicationControl.Refused(outcome)) return;
        Assert.Equal(SessionResult.RanFine, outcome.Result);
        Assert.Equal("It ran, and printed what you expected.", outcome.Headline);
    }
}
