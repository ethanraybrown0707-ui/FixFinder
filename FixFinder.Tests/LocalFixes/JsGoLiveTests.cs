using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>JavaScript and Go programs broken the way learners break them, run for real through Node and <c>go run</c> - each
/// must end with its rule's fix, checked and ready to copy.</summary>
public class JsGoLiveTests
{
    private static string Go(string body, string imports = "import \"fmt\"\n\n", string extra = "") =>
        "package main\n\n" + imports + extra + "func main() {\n" + body + "}\n";

    public static TheoryData<string, string, string, string, string, string> Cases => new()
    {
        { "node", "missing )", "app.js", "const total = 5;\nconsole.log(\"Total:\", total;\n", "js-missing-closing-paren", "console.log(\"Total:\", total);" },
        { "node", "Python's print", "app.js", "print(\"hello\");\n", "js-foreign-print", "console.log(\"hello\");" },
        { "node", "misspelt name", "app.js", "const total = 5;\nconsole.log(totl);\n", "js-nearest-name", "console.log(total);" },
        { "node", "append on a list", "app.js", "const items = [];\nitems.append(1);\nconsole.log(items);\n", "js-member-not-function", "items.push(1);" },
        { "node", "length called", "app.js", "const text = \"hello\";\nconsole.log(text.length());\n", "js-member-not-function", "console.log(text.length);" },
        { "node", "const reassigned", "app.js", "const count = 0;\ncount = count + 1;\nconsole.log(count);\n", "js-const-reassigned", "let count = 0;" },
        { "node", "if without brackets", "app.js", "const x = 5;\nif x > 3 {\n  console.log(\"big\");\n}\n", "js-condition-parentheses", "if (x > 3) {" },
        {
            "node", "await outside async", "app.js",
            "function wait(ms) {\n  return new Promise((resolve) => setTimeout(resolve, ms));\n}\n\nfunction main() {\n  await wait(10);\n  console.log(\"done\");\n}\n\nmain();\n",
            "js-await-outside-async", "async function main() {"
        },
        { "node", "class without new", "app.js", "class Dog {\n  constructor(name) {\n    this.name = name;\n  }\n}\nconst d = Dog(\"Rex\");\nconsole.log(d.name);\n", "js-class-without-new", "const d = new Dog(\"Rex\");" },
        { "node", "require in a module", "app.mjs", "const fs = require(\"fs\");\nconsole.log(fs.existsSync(\".\"));\n", "js-require-in-module", "import fs from \"fs\";" },
        {
            "node", "field without this", "app.js",
            "class Counter {\n  constructor() {\n    this.count = 0;\n  }\n  increment() {\n    count++;\n  }\n}\nconst c = new Counter();\nc.increment();\nconsole.log(c.count);\n",
            "js-missing-this", "this.count++;"
        },
        {
            "node", "super missing", "app.js",
            "class Animal {\n  constructor(name) {\n    this.name = name;\n  }\n}\nclass Dog extends Animal {\n  constructor(name) {\n    this.sound = \"woof\";\n  }\n}\nconsole.log(new Dog(\"Rex\").sound);\n",
            "js-super-missing", "super(name);"
        },
        {
            "node", "promise not awaited", "app.js",
            "async function load() {\n  return [1, 2, 3];\n}\n\nasync function main() {\n  const items = load();\n  console.log(items.map((x) => x * 2));\n}\n\nmain();\n",
            "js-promise-not-awaited", "const items = await load();"
        },
        { "node", "push on a Set", "app.js", "const seen = new Set();\nseen.push(1);\nconsole.log(seen.size);\n", "js-member-not-function", "seen.add(1);" },
        {
            "node", "this in a function callback", "app.js",
            "class Timer {\n  constructor() {\n    this.ticks = 0;\n  }\n  start() {\n    [1, 2, 3].forEach(function () {\n      this.ticks++;\n    });\n  }\n}\nconst t = new Timer();\nt.start();\nconsole.log(t.ticks);\n",
            "js-this-in-callback", "forEach(() => {"
        },

        { "go", "fmt not imported", "app.go", Go("\tfmt.Println(\"hello\")\n", ""), "go-missing-import", "import \"fmt\"" },
        { "go", "misspelt name, reported as unused first", "app.go", Go("\ttotal := 5\n\tfmt.Println(totl)\n"), "go-nearest-name", "fmt.Println(total)" },
        { "go", "fmt.println", "app.go", Go("\tfmt.println(\"hello\")\n"), "go-package-member", "fmt.Println(\"hello\")" },
        { "go", "brace on the next line", "app.go", "package main\n\nimport \"fmt\"\n\nfunc main()\n{\n\tfmt.Println(\"hello\")\n}\n", "go-brace-on-next-line", "func main() {" },
        { "go", "string plus an int", "app.go", Go("\tage := 20\n\tfmt.Println(\"Age: \" + age)\n"), "go-string-plus-number", "fmt.Sprint(age)" },
        { "go", ".length on a slice", "app.go", Go("\titems := []int{1, 2, 3}\n\tfmt.Println(items.length)\n"), "go-selector", "len(items)" },
        { "go", "console.log", "app.go", Go("\tconsole.log(\"hello\")\n"), "go-foreign-print", "fmt.Println(\"hello\")" },
        { "go", "loop past the end", "app.go", Go("\titems := []int{1, 2, 3}\n\tfor i := 0; i <= len(items); i++ {\n\t\tfmt.Println(items[i])\n\t}\n"), "go-index-loop", "i < len(items)" },
        { "go", "nil map", "app.go", Go("\tvar ages map[string]int\n\tages[\"Ada\"] = 36\n\tfmt.Println(ages)\n"), "go-nil-map", "ages := make(map[string]int)" },
        {
            "go", "pointer receiver", "app.go",
            Go("\tvar s Shape = Square{side: 2}\n\tfmt.Println(s.Area())\n", extra: "type Shape interface {\n\tArea() float64\n}\n\ntype Square struct {\n\tside float64\n}\n\nfunc (s *Square) Area() float64 {\n\treturn s.side * s.side\n}\n\n"),
            "go-pointer-receiver", "&Square{side: 2}"
        },
        { "go", "unbuffered send deadlock", "app.go", Go("\tch := make(chan int)\n\tch <- 1\n\tfmt.Println(<-ch)\n"), "go-channel-deadlock", "make(chan int, 1)" },
        {
            "go", "return missing its value", "app.go",
            Go("\tn, err := divide(4, 2)\n\tfmt.Println(n, err)\n", "import (\n\t\"errors\"\n\t\"fmt\"\n)\n\n", "func divide(a, b int) (int, error) {\n\tif b == 0 {\n\t\treturn errors.New(\"divide by zero\")\n\t}\n\treturn a / b, nil\n}\n\n"),
            "go-not-enough-return-values", "return 0, errors.New(\"divide by zero\")"
        },
        { "go", "Main for main", "app.go", "package main\n\nimport \"fmt\"\n\nfunc Main() {\n\tfmt.Println(\"hello\")\n}\n", "go-main-name", "func main() {" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TheMistakeIsFixedByARealRun(string runtime, string construct, string fileName, string source, string rule, string expected)
    {
        if (TargetFactory.FindOnPath(runtime) is null) return;

        using var temp = new TempFolder();

        var path = Path.Combine(temp.Path, fileName);
        File.WriteAllText(path, source);

        var plan = TargetFactory.FromFile(path, TimeSpan.FromMinutes(3));
        Assert.True(plan.Ok, plan.Problem);

        using var http = new FixFinderHttpClient();
        var outcome = await new FixFinderSession(http, new FixSourceRegistry()).RunAsync(plan, new SearchBudget(Cache: CacheMode.CacheOnly));

        if (ApplicationControl.Refused(outcome)) return;
        Assert.True(outcome.Result == SessionResult.FoundFix, $"{construct} ({runtime}): {outcome.Result} - {outcome.Headline}");
        Assert.Equal($"local:{rule}", outcome.Best!.Id);

        var copied = PasteableFix.For(new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));
        Assert.NotNull(copied);
        Assert.Contains(expected, copied!.Text.ReplaceLineEndings("\n") + "\n", StringComparison.Ordinal);
    }
}
