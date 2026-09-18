using System.Diagnostics;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>The Python mistakes of later years - classes, closures, async, comprehensions, collections - each rule's fix and
/// refusals, then the same programs run for real.</summary>
public class PythonAdvancedTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string name, string body)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, body);
        return path;
    }

    private static ParsedError Error(string type, string message, string file, int line, string symbol = "<module>") => new()
    {
        LanguageId = "python",
        Confidence = 90,
        RawText = message,
        FirstLineSequence = 0,
        ExceptionType = type,
        Message = message,
        Frames = [new ErrorFrame { Order = 0, File = file, Line = line, Symbol = symbol, RawLine = "" }],
    };

    private string? Propose(string rule, ParsedError error)
    {
        var fix = LocalFixEngine.Rules.Single(r => r.Id == rule).Propose(new LocalFixContext { Error = error, SourceRoot = _temp.Path });
        return fix is null ? null : string.Join("|", fix.NewLines);
    }

    [Theory]
    [InlineData("python-except-as", "SyntaxError", "invalid syntax", "try:\n    pass\nexcept ValueError e:\n    pass\n", 3, "<module>", "except ValueError as e:")]
    [InlineData("python-except-as", "SyntaxError", "invalid syntax", "try:\n    pass\nexcept (KeyError, ValueError) err:\n    pass\n", 3, "<module>", "except (KeyError, ValueError) as err:")]
    [InlineData("python-except-as", "SyntaxError", "invalid syntax", "try:\n    pass\nexcept ValueError as e:\n    pass\n", 3, "<module>", null)]
    [InlineData("python-comprehension-condition", "SyntaxError", "invalid syntax", "clamped = [n for n in numbers if n > 0 else 0]\n", 1, "<module>", "clamped = [n if n > 0 else 0 for n in numbers]")]
    [InlineData("python-comprehension-condition", "SyntaxError", "invalid syntax", "evens = [n for n in numbers if n % 2 == 0]\n", 1, "<module>", null)]
    [InlineData("python-await-outside-async", "SyntaxError", "'await' outside async function", "import asyncio\n\ndef main():\n    await asyncio.sleep(1)\n", 4, "<module>", "async def main():")]
    [InlineData("python-await-outside-async", "SyntaxError", "'await' outside async function", "await asyncio.sleep(1)\n", 1, "<module>", null)]
    [InlineData("python-coroutine-not-called", "ValueError", "a coroutine was expected, got <function main at 0x000001>", "import asyncio\n\nasync def main():\n    pass\n\nasyncio.run(main)\n", 6, "<module>", "asyncio.run(main())")]
    [InlineData("python-coroutine-not-called", "ValueError", "a coroutine was expected, got <function main at 0x000001>", "import asyncio\n\ndef main():\n    pass\n\nasyncio.run(main)\n", 6, "<module>", null)]
    [InlineData("python-module-called", "TypeError", "'module' object is not callable. Did you mean: 'pprint.pprint(...)'?", "import pprint\npprint(data)\n", 2, "<module>", "pprint.pprint(data)")]
    [InlineData("python-missing-from-import", "NameError", "name 'defaultdict' is not defined", "import sys\ncounts = defaultdict(int)\n", 2, "<module>", "from collections import defaultdict")]
    [InlineData("python-missing-from-import", "NameError", "name 'Counter' is not defined", "from collections import deque\nprint(Counter([1]))\n", 2, "<module>", "from collections import deque, Counter")]
    [InlineData("python-missing-from-import", "NameError", "name 'banana' is not defined", "print(banana)\n", 1, "<module>", null)]
    [InlineData("python-missing-self-attribute", "AttributeError", "'Student' object has no attribute 'name'", "class Student:\n    def __init__(self, name):\n        name = name\n\n    def greet(self):\n        print(self.name)\n", 6, "greet", "        self.name = name")]
    [InlineData("python-missing-self-attribute", "AttributeError", "'Student' object has no attribute 'name'", "class Student:\n    def __init__(self, name):\n        self.nmae = name\n\n    def greet(self):\n        print(self.name)\n", 6, "greet", null)]
    [InlineData("python-super-arguments", "TypeError", "Animal.__init__() missing 1 required positional argument: 'name'", "class Dog(Animal):\n    def __init__(self, name, breed):\n        super().__init__()\n", 3, "__init__", "        super().__init__(name)")]
    [InlineData("python-super-arguments", "TypeError", "Animal.__init__() missing 2 required positional arguments: 'name' and 'age'", "class Dog(Animal):\n    def __init__(self, name, age):\n        super().__init__()\n", 3, "__init__", "        super().__init__(name, age)")]
    [InlineData("python-super-arguments", "TypeError", "Animal.__init__() missing 1 required positional argument: 'name'", "class Dog(Animal):\n    def __init__(self, breed):\n        super().__init__()\n", 3, "__init__", null)]
    [InlineData("python-nonlocal", "UnboundLocalError", "cannot access local variable 'count' where it is not associated with a value", "def counter():\n    count = 0\n\n    def increment():\n        count += 1\n        return count\n\n    return increment\n", 5, "counter.<locals>.increment", "        nonlocal count")]
    [InlineData("python-nonlocal", "UnboundLocalError", "cannot access local variable 'count' where it is not associated with a value", "def counter():\n    def increment():\n        count += 1\n\n    return increment\n", 3, "increment", null)]
    [InlineData("python-string-item-assignment", "TypeError", "'str' object does not support item assignment", "word[0] = \"H\"\n", 1, "<module>", "word = \"H\" + word[1:]")]
    [InlineData("python-string-item-assignment", "TypeError", "'str' object does not support item assignment", "    word[i] = c  # swap\n", 1, "<module>", "    word = word[:i] + c + word[i + 1:]  # swap")]
    [InlineData("python-loop-unpack", "ValueError", "too many values to unpack (expected 2)", "ages = {\"ada\": 36}\nfor name, age in ages:\n    pass\n", 2, "<module>", "for name, age in ages.items():")]
    [InlineData("python-loop-unpack", "ValueError", "too many values to unpack (expected 2)", "ages = load()\nfor name, age in ages:\n    pass\n", 2, "<module>", null)]
    [InlineData("python-loop-unpack", "TypeError", "cannot unpack non-iterable int object", "for i, item in items:\n    pass\n", 1, "<module>", "for i, item in enumerate(items):")]
    [InlineData("python-unhashable-list", "TypeError", "unhashable type: 'list'", "seen.add([1, 2])\n", 1, "<module>", "seen.add((1, 2))")]
    [InlineData("python-unhashable-list", "TypeError", "unhashable type: 'list'", "seen.add([x])\n", 1, "<module>", "seen.add((x,))")]
    public void EachConstructGetsItsFixOrARefusal(string rule, string type, string message, string source, int line, string symbol, string? expected)
    {
        var file = Write("app.py", source);

        var proposed = Propose(rule, Error(type, message, file, line, symbol));

        if (expected is null) Assert.Null(proposed);
        else Assert.Contains(expected, proposed ?? "(no fix)", StringComparison.Ordinal);
    }

    [Fact]
    public void TheStorePythonsStandardLibraryIsNotTheUsersCode()
    {
        Assert.True(FrameClassifier.IsVendored(@"C:\Program Files\WindowsApps\PythonSoftwareFoundation.Python.3.13_3.13.3824.0_x64__qbz5n2kfra8p0\Lib\asyncio\runners.py"));
    }

    [Fact]
    public void AnyPythonInstallsStandardLibraryIsNotTheUsersCode()
    {
        var home = Path.Combine(_temp.Path, "toolcache", "3.12.10", "x64");
        Directory.CreateDirectory(Path.Combine(home, "Lib", "asyncio"));
        File.WriteAllText(Path.Combine(home, "python.exe"), "");

        var runners = Path.Combine(home, "Lib", "asyncio", "runners.py");
        File.WriteAllText(runners, "");

        Assert.True(FrameClassifier.IsVendored(runners));
        Assert.False(FrameClassifier.IsVendored(Write("app.py", "print(1)\n")));
    }

    [Fact]
    public void TheMissingCallIsFoundInTheUsersFrameWhenAsyncioRaisesItself()
    {
        var app = Write("app.py", "import asyncio\n\nasync def main():\n    pass\n\nasyncio.run(main)\n");

        var error = new ParsedError
        {
            LanguageId = "python",
            Confidence = 90,
            RawText = "ValueError: a coroutine was expected, got <function main at 0x000001>",
            FirstLineSequence = 0,
            ExceptionType = "ValueError",
            Message = "a coroutine was expected, got <function main at 0x000001>",
            Frames =
            [
                new ErrorFrame { Order = 0, File = @"C:\hostedtoolcache\windows\Python\3.12.10\x64\Lib\asyncio\runners.py", Line = 89, RawLine = "" },
                new ErrorFrame { Order = 1, File = app, Line = 6, RawLine = "" },
            ],
        };

        Assert.Equal("asyncio.run(main())", Propose("python-coroutine-not-called", error));
    }

    private static readonly Lazy<bool> Python = new(() =>
    {
        if (TargetFactory.FindOnPath("python") is not { } python) return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo(python, "-c \"import sys; print(sys.version_info >= (3, 12))\"")
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            });

            var answer = process!.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(30_000);

            return answer == "True";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    });

    public static TheoryData<string, string, string, string> Constructs => new()
    {
        { "property called", "class Circle:\n    def __init__(self, r):\n        self.r = r\n\n    @property\n    def area(self):\n        return 3.14159 * self.r ** 2\n\n\nc = Circle(2)\nprint(c.area())\n", "local:python-property-called", "print(c.area)" },
        { "except without as", "try:\n    int(\"x\")\nexcept ValueError e:\n    print(e)\n", "local:python-except-as", "except ValueError as e:" },
        { "await outside async", "import asyncio\n\n\ndef main():\n    await asyncio.sleep(0.1)\n    print(\"done\")\n\n\nasyncio.run(main())\n", "local:python-await-outside-async", "async def main():" },
        { "asyncio.run without the call", "import asyncio\n\n\nasync def main():\n    await asyncio.sleep(0.1)\n    print(\"done\")\n\n\nasyncio.run(main)\n", "local:python-coroutine-not-called", "asyncio.run(main())" },
        { "module called", "import pprint\n\ndata = {\"a\": [1, 2]}\npprint(data)\n", "local:python-module-called", "pprint.pprint(data)" },
        { "index with the element", "names = [\"ada\", \"alan\"]\nfor name in names:\n    print(names[name])\n", "local:python-index-with-element", "print(name)" },
        { "name = name in __init__", "class Student:\n    def __init__(self, name):\n        name = name\n\n    def greet(self):\n        print(\"Hi \" + self.name)\n\n\nStudent(\"Ada\").greet()\n", "local:python-missing-self-attribute", "self.name = name" },
        { "if else after for", "numbers = [3, -1, 4]\nclamped = [n for n in numbers if n > 0 else 0]\nprint(clamped)\n", "local:python-comprehension-condition", "[n if n > 0 else 0 for n in numbers]" },
        { "tuple item assignment", "point = (1, 2)\npoint[0] = 5\nprint(point)\n", "local:python-tuple-to-list", "point = [1, 2]" },
        { "string item assignment", "word = \"hello\"\nword[0] = \"H\"\nprint(word)\n", "local:python-string-item-assignment", "word = \"H\" + word[1:]" },
        { "closure without nonlocal", "def counter():\n    count = 0\n\n    def increment():\n        count += 1\n        return count\n\n    return increment\n\n\ninc = counter()\nprint(inc())\n", "local:python-nonlocal", "nonlocal count" },
        { "dataclass not imported", "@dataclass\nclass Point:\n    x: int\n    y: int\n\n\nprint(Point(1, 2))\n", "local:python-missing-from-import", "from dataclasses import dataclass" },
        { "defaultdict not imported", "counts = defaultdict(int)\nfor word in [\"a\", \"b\", \"a\"]:\n    counts[word] += 1\nprint(counts)\n", "local:python-missing-from-import", "from collections import defaultdict" },
        { "randint not imported", "print(randint(1, 6))\n", "local:python-missing-from-import", "from random import randint" },
        { "number format on text", "price = \"4.5\"\nprint(f\"Total: {price:.2f}\")\n", "local:python-format-code-on-text", "{float(price):.2f}" },
        { "map indexed", "values = map(int, [\"1\", \"2\"])\nprint(values[0])\n", "local:python-not-subscriptable", "values = list(map(int, [\"1\", \"2\"]))" },
        { "generator indexed", "squares = (n * n for n in range(5))\nprint(squares[2])\n", "local:python-not-subscriptable", "squares = [n * n for n in range(5)]" },
        { "dict keys indexed", "ages = {\"ada\": 36, \"alan\": 41}\nfirst = ages.keys()[0]\nprint(first)\n", "local:python-not-subscriptable", "first = list(ages.keys())[0]" },
        { "dict loop without items", "ages = {\"ada\": 36, \"alan\": 41}\nfor name, age in ages:\n    print(name, age)\n", "local:python-loop-unpack", "for name, age in ages.items():" },
        { "enumerate missing", "items = [10, 20, 30]\nfor i, item in items:\n    print(i, item)\n", "local:python-loop-unpack", "for i, item in enumerate(items):" },
        { "string times a float", "width = 9\nprint(\"-\" * (width / 2))\n", "local:python-sequence-times-float", "print(\"-\" * (width // 2))" },
        { "super without arguments", "class Animal:\n    def __init__(self, name):\n        self.name = name\n\n\nclass Dog(Animal):\n    def __init__(self, name, breed):\n        super().__init__()\n        self.breed = breed\n\n\nprint(Dog(\"Rex\", \"lab\").name)\n", "local:python-super-arguments", "super().__init__(name)" },
        { "list in a set", "seen = set()\nseen.add([1, 2])\nprint(seen)\n", "local:python-unhashable-list", "seen.add((1, 2))" },
        { "missing return", "def add(a, b):\n    total = a + b\n\n\nprint(add(1, 2) + 1)\n", "local:python-missing-return", "    return total" },
    };

    [Theory]
    [MemberData(nameof(Constructs))]
    public async Task TheConstructIsFixedLive(string construct, string source, string id, string expected)
    {
        if (!Python.Value) return;

        var plan = TargetFactory.FromFile(Write("app.py", source), TimeSpan.FromMinutes(2));
        Assert.True(plan.Ok, plan.Problem);

        using var http = new FixFinderHttpClient();
        var outcome = await new FixFinderSession(http, new FixSourceRegistry()).RunAsync(plan, new SearchBudget(Cache: CacheMode.CacheOnly));

        if (ApplicationControl.Refused(outcome)) return;
        Assert.True(outcome.Result == SessionResult.FoundFix, $"{construct}: {outcome.Result} - {outcome.Headline}");
        Assert.Equal(id, outcome.Best!.Id);

        var copied = PasteableFix.For(new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));

        Assert.NotNull(copied);
        Assert.Contains(expected, copied!.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }
}
