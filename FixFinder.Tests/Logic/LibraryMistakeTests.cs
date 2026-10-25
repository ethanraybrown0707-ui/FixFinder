using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// A language's own library used in a way that runs, says nothing and does something else - in Java, C#, Python, JavaScript,
/// C, C++ and Go: found where it is, with the line each check's change writes, and left alone where the code means what it says.
/// </summary>
public class LibraryMistakeTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string fileName, string code, string check)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == check).ToList();
    }

    [Theory]
    // Java.
    [InlineData("Main.java", "logic-java-thread-run", "class Main {\n    void go() {\n        Thread worker = new Thread(() -> {});\n        worker.run();\n    }\n}\n", 4, "        worker.start();")]
    [InlineData("Main.java", "logic-java-thread-run", "class Main {\n    void go() {\n        new Thread(this::work).run();\n    }\n    void work() {}\n}\n", 3, "        new Thread(this::work).start();")]
    [InlineData("Main.java", "logic-java-bigdecimal-from-double", "class Main {\n    java.math.BigDecimal price = new java.math.BigDecimal(19.99);\n}\n", 2, "    java.math.BigDecimal price = new java.math.BigDecimal(\"19.99\");")]
    [InlineData("Main.java", "logic-java-bigdecimal-from-double", "class Main {\n    java.math.BigDecimal make(double price) {\n        return new java.math.BigDecimal(price);\n    }\n}\n", 3, "        return java.math.BigDecimal.valueOf(price);")]
    [InlineData("Main.java", "logic-java-aslist-of-primitive-array", "import java.util.Arrays;\nclass Main {\n    boolean has(int[] marks) {\n        return Arrays.asList(marks).contains(80);\n    }\n}\n", 4, "        return Arrays.stream(marks).boxed().collect(java.util.stream.Collectors.toList()).contains(80);")]
    [InlineData("Main.java", "logic-java-abs-of-hash", "class Main {\n    int bucket(Object key) { return Math.abs(key.hashCode()) % 16; }\n}\n", 2, "    int bucket(Object key) { return Math.floorMod(key.hashCode(), 16); }")]
    // C#.
    [InlineData("Program.cs", "logic-csharp-task-printed", "var total = SumAsync();\nConsole.WriteLine(total);\nstatic async Task<int> SumAsync() { await Task.Delay(1); return 1; }\n", 1, "var total = await SumAsync();")]
    // Python.
    [InlineData("app.py", "logic-python-thread-run", "import threading\nworker = threading.Thread(target=print)\nworker.run()\n", 3, "worker.start()")]
    [InlineData("app.py", "logic-python-decimal-from-float", "from decimal import Decimal\nprice = Decimal(19.99)\n", 2, "price = Decimal(\"19.99\")")]
    [InlineData("app.py", "logic-python-get-default-changed", "counts = {}\ncounts.get('a', []).append(1)\n", 2, "counts.setdefault('a', []).append(1)")]
    [InlineData("app.py", "logic-python-iterator-used-twice", "words = ['3', '4']\nnumbers = map(int, words)\nfor n in numbers:\n    print(n)\nprint(max(numbers))\n", 2, "numbers = list(map(int, words))")]
    [InlineData("app.py", "logic-python-iterator-used-twice", "squares = (n * n for n in range(4))\nprint(sum(squares))\nprint(sorted(squares))\n", 1, "squares = list(n * n for n in range(4))")]
    [InlineData("app.py", "logic-python-strip-word", "name = \"photo.jpg\"\nprint(name.rstrip(\".jpg\"))\n", 2, "print(name.removesuffix(\".jpg\"))")]
    [InlineData("app.py", "logic-python-strip-word", "url = \"www.example.com\"\nprint(url.lstrip(\"www.\"))\n", 2, "print(url.removeprefix(\"www.\"))")]
    [InlineData("app.py", "logic-python-lambda-in-loop", "adders = [lambda x: x + step for step in range(3)]\n", 1, "adders = [lambda x, step=step: x + step for step in range(3)]")]
    [InlineData("app.py", "logic-python-lambda-in-loop", "callbacks = []\nfor step in range(3):\n    callbacks.append(lambda: step)\n", 3, "    callbacks.append(lambda step=step: step)")]
    // JavaScript.
    [InlineData("app.js", "logic-js-array-holes-mapped", "const squares = new Array(3).map((_, i) => i * i);\n", 1, "const squares = Array.from({ length: 3 }).map((_, i) => i * i);")]
    [InlineData("app.js", "logic-js-replace-first-only", "const phone = '555-1234';\nconst digits = phone.replace(\"-\", \"\");\n", 2, "const digits = phone.replaceAll(\"-\", \"\");")]
    [InlineData("app.js", "logic-js-promise-used-as-value", "async function load() { return 3; }\nconst data = load();\nconsole.log(data);\n", 2, "const data = await load();")]
    [InlineData("app.js", "logic-js-promise-used-as-value", "const doubled = [1, 2].map(async (n) => n * 2);\nconsole.log(doubled);\n", 1, "const doubled = await Promise.all([1, 2].map(async (n) => n * 2));")]
    [InlineData("app.js", "logic-js-timer-given-a-call", "function tick() {\n    console.log('tick');\n}\nsetInterval(tick(), 1000);\n", 4, "setInterval(tick, 1000);")]
    [InlineData("app.js", "logic-js-timer-given-a-call", "function greet(name) {\n    console.log(name);\n}\nsetTimeout(greet('Ada'), 500);\n", 4, "setTimeout(() => greet('Ada'), 500);")]
    [InlineData("app.js", "logic-js-get-year", "const year = new Date().getYear();\n", 1, "const year = new Date().getFullYear();")]
    [InlineData("app.js", "logic-js-get-day-for-date", "const d = new Date();\nconsole.log(d.getDay() + '/' + (d.getMonth() + 1));\n", 2, "console.log(d.getDate() + '/' + (d.getMonth() + 1));")]
    // C and C++.
    [InlineData("main.c", "logic-c-text-without-end", "char code[3] = \"abc\";\n", 1, "char code[4] = \"abc\";")]
    [InlineData("main.c", "logic-c-getchar-into-char", "#include <stdio.h>\nint main(void) {\n    char c;\n    while ((c = getchar()) != EOF) {\n        putchar(c);\n    }\n    return 0;\n}\n", 4, "    int c;")]
    [InlineData("main.c", "logic-c-malloc-without-sizeof", "#include <stdlib.h>\nint main(void) {\n    int n = 10;\n    double *values = malloc(n);\n    free(values);\n    return 0;\n}\n", 4, "    double *values = malloc(n * sizeof *values);")]
    [InlineData("main.cpp", "logic-cpp-text-plus-number", "#include <string>\nint main() {\n    int count = 3;\n    std::string shown = \"Count: \" + count;\n}\n", 4, "    std::string shown = \"Count: \" + std::to_string(count);")]
    [InlineData("main.cpp", "logic-cpp-reserve-then-index", "#include <vector>\nint main() {\n    std::vector<int> marks;\n    marks.reserve(3);\n    marks[0] = 70;\n}\n", 5, "    marks.resize(3);")]
    // Go.
    [InlineData("main.go", "logic-go-sleep-nanoseconds", "package main\n\nimport \"time\"\n\nfunc main() {\n\ttime.Sleep(5)\n}\n", 6, "\ttime.Sleep(5 * time.Second)")]
    [InlineData("main.go", "logic-go-sleep-nanoseconds", "package main\n\nimport \"time\"\n\nfunc main() {\n\ttime.Sleep(500)\n}\n", 6, "\ttime.Sleep(500 * time.Millisecond)")]
    [InlineData("main.go", "logic-go-copy-into-empty", "package main\n\nfunc main() {\n\tmarks := []int{1}\n\tbackup := []int{}\n\tcopy(backup, marks)\n\t_ = backup\n}\n", 6, "\tbackup := make([]int, len(marks))")]
    [InlineData("main.go", "logic-go-copy-into-empty", "package main\n\nfunc main() {\n\tmarks := []int{1}\n\tvar backup []int\n\tcopy(backup, marks)\n\t_ = backup\n}\n", 6, "\tvar backup = make([]int, len(marks))")]
    [InlineData("main.go", "logic-go-byte-printed", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tword := \"go\"\n\tfmt.Println(word[1])\n}\n", 7, "\tfmt.Println(string(word[1]))")]
    public void AMistakeIsFoundWithItsChange(string fileName, string check, string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code, check));

        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Fact]
    public void ALambdaMadeInACSharpForLoopIsGivenACopyOfTheCounterOnTheLineBefore()
    {
        var found = Assert.Single(Scan("Program.cs", "var actions = new List<Action>();\nfor (int i = 0; i < 3; i++)\n{\n    actions.Add(() => Console.Write(i));\n}\n", "logic-csharp-for-closure"));

        Assert.Equal(4, found.Line);
        Assert.Equal(["    var currentI = i;", "    actions.Add(() => Console.Write(currentI));"], found.Fix!.NewLines);
    }

    [Theory]
    // Arrays.asList of a char[] has no stream to make a list of; a goroutine is waited for in ways too many to write in.
    [InlineData("Main.java", "logic-java-aslist-of-primitive-array", "import java.util.Arrays;\nclass Main {\n    int size(char[] letters) { return Arrays.asList(letters).size(); }\n}\n")]
    [InlineData("main.go", "logic-go-goroutine-not-waited", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tgo fmt.Println(\"hi\")\n}\n")]
    public void AMistakeWithNoSureChangeIsFoundWithoutOne(string fileName, string check, string code) =>
        Assert.Null(Assert.Single(Scan(fileName, code, check)).Fix);

    [Theory]
    // Java: start(), a Runnable's own run(), BigDecimal from text or from a decimal a double holds exactly, Integer[] in asList, floorMod.
    [InlineData("Main.java", "logic-java-thread-run", "class Main {\n    void go(Runnable task) {\n        Thread worker = new Thread(task);\n        worker.start();\n        task.run();\n    }\n}\n")]
    [InlineData("Main.java", "logic-java-bigdecimal-from-double", "import java.math.BigDecimal;\nclass Main {\n    BigDecimal a = new BigDecimal(\"0.1\"), b = new BigDecimal(0.5), c = BigDecimal.valueOf(0.1);\n    BigDecimal d = new BigDecimal(count);\n    static int count = 3;\n}\n")]
    [InlineData("Main.java", "logic-java-aslist-of-primitive-array", "import java.util.Arrays;\nclass Main {\n    boolean has(Integer[] marks) { return Arrays.asList(marks).contains(80) || Arrays.asList(1, 2).contains(1); }\n}\n")]
    [InlineData("Main.java", "logic-java-abs-of-hash", "class Main {\n    int bucket(Object key, int n) { return Math.floorMod(key.hashCode(), 16) + Math.abs(n) % 16; }\n}\n")]
    // C#: a foreach's own variable, the counter copied already, and a lambda that does not use it.
    [InlineData("Program.cs", "logic-csharp-for-closure", "var actions = new List<Action>();\nforeach (var mark in new[] { 1, 2 })\n{\n    actions.Add(() => Console.Write(mark));\n}\nfor (int i = 0; i < 3; i++)\n{\n    var current = i;\n    actions.Add(() => Console.Write(current));\n    actions.Add(() => Console.Write(\"tick\"));\n}\n")]
    [InlineData("Program.cs", "logic-csharp-task-printed", "var total = await SumAsync();\nConsole.WriteLine(total);\nstatic async Task<int> SumAsync() { await Task.Delay(1); return 1; }\n")]
    // Python: start(), Decimal from text or an exact decimal, get only read, a list read twice, strip given a set, a lambda given its value.
    [InlineData("app.py", "logic-python-thread-run", "import threading\nworker = threading.Thread(target=print)\nworker.start()\n")]
    [InlineData("app.py", "logic-python-decimal-from-float", "from decimal import Decimal\nprint(Decimal(\"0.1\"), Decimal(0.5), Decimal(3))\n")]
    [InlineData("app.py", "logic-python-get-default-changed", "counts = {}\nprint(counts.get('a', []))\ncounts.setdefault('a', []).append(1)\n")]
    [InlineData("app.py", "logic-python-iterator-used-twice", "numbers = list(map(int, ['3']))\nprint(sum(numbers), max(numbers))\nsquares = map(abs, [-1])\nprint(sum(squares))\n")]
    [InlineData("app.py", "logic-python-strip-word", "line = input()\nprint(line.rstrip(\" \\n\"), line.rstrip(\"/\"), line.lstrip(\"0\"))\n")]
    [InlineData("app.py", "logic-python-lambda-in-loop", "adders = [lambda x, step=step: x + step for step in range(3)]\nfor step in range(3):\n    print((lambda: step)())\n")]
    // JavaScript: holes filled first, a regular expression, await and then, the function itself, a factory, the year and the weekday.
    [InlineData("app.js", "logic-js-array-holes-mapped", "const zeros = new Array(3).fill(0).map((n) => n);\nconst named = new Array(\"a\").map((s) => s);\n")]
    [InlineData("app.js", "logic-js-replace-first-only", "const phone = '555-1234';\nconsole.log(phone.replace(/-/g, ''), phone.replace('555', ''));\n")]
    [InlineData("app.js", "logic-js-promise-used-as-value", "async function load() { return 3; }\nasync function main() {\n    const data = await load();\n    console.log(data);\n    const later = load();\n    later.then(console.log);\n}\nmain();\n")]
    [InlineData("app.js", "logic-js-timer-given-a-call", "function greet() { console.log('hi'); }\nfunction makeTick() {\n    return () => console.log('tick');\n}\nsetTimeout(greet, 1000);\nsetTimeout(() => greet(), 1000);\nsetInterval(makeTick(), 1000);\n")]
    [InlineData("app.js", "logic-js-get-day-for-date", "const d = new Date();\nconst weekday = d.getDay();\nconsole.log(d.getFullYear());\n")]
    // C and C++: room for the end, text shorter than its array, an int for getchar, a char buffer, sizeof given, a std::string, push_back.
    [InlineData("main.c", "logic-c-text-without-end", "char word[6] = \"hello\";\nchar other[] = \"hello\";\nchar line[3] = \"a\\n\";\n")]
    [InlineData("main.c", "logic-c-getchar-into-char", "#include <stdio.h>\nint main(void) {\n    int c;\n    while ((c = getchar()) != EOF) {\n        putchar(c);\n    }\n    return 0;\n}\n")]
    [InlineData("main.c", "logic-c-malloc-without-sizeof", "#include <stdlib.h>\nint main(void) {\n    int n = 10;\n    char *text = malloc(n);\n    int *marks = malloc(n * sizeof(int));\n    free(text);\n    free(marks);\n    return 0;\n}\n")]
    [InlineData("main.cpp", "logic-cpp-text-plus-number", "#include <string>\nint main() {\n    int count = 3;\n    std::string name = \"Ada\";\n    std::string a = \"Hi \" + name;\n    std::string b = std::string(\"Count: \") + std::to_string(count);\n}\n")]
    [InlineData("main.cpp", "logic-cpp-reserve-then-index", "#include <vector>\nint main() {\n    std::vector<int> marks;\n    marks.reserve(3);\n    marks.push_back(70);\n    marks[0] = 80;\n}\n")]
    // Go: a Duration, a slice as long as what is copied, a byte turned back into text, numbers indexed, and a goroutine waited for.
    [InlineData("main.go", "logic-go-sleep-nanoseconds", "package main\n\nimport \"time\"\n\nfunc main() {\n\ttime.Sleep(time.Second)\n\ttime.Sleep(0)\n}\n")]
    [InlineData("main.go", "logic-go-copy-into-empty", "package main\n\nfunc main() {\n\tmarks := []int{1}\n\tbackup := make([]int, len(marks))\n\tcopy(backup, marks)\n\t_ = backup\n}\n")]
    [InlineData("main.go", "logic-go-byte-printed", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tword := \"go\"\n\tnums := []int{1}\n\tfmt.Println(string(word[1]), nums[0])\n}\n")]
    [InlineData("main.go", "logic-go-goroutine-not-waited", "package main\n\nimport (\n\t\"fmt\"\n\t\"sync\"\n)\n\nfunc main() {\n\tvar wg sync.WaitGroup\n\twg.Add(1)\n\tgo func() {\n\t\tdefer wg.Done()\n\t\tfmt.Println(\"hi\")\n\t}()\n\twg.Wait()\n}\n")]
    public void CodeThatMeansWhatItSaysIsLeftAlone(string fileName, string check, string code) => Assert.Empty(Scan(fileName, code, check));

    [Theory]
    [InlineData("0.5", true)]
    [InlineData("2.25", true)]
    [InlineData("1.0", true)]
    [InlineData("0.1", false)]
    [InlineData("19.99", false)]
    [InlineData("-0.75", true)]
    public void ADecimalIsKnownToBeHeldExactlyByADoubleOrNot(string value, bool exact) => Assert.Equal(exact, ManagedLibraryPatterns.IsExactInBinary(value));
}
