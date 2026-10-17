using System.Diagnostics;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// JavaScript's logic checks reading TypeScript, as the JavaScript Node.js runs once it has taken the types out: each mistake
/// written with its types is found at its line, the change keeps the types it was written with, and - where what it prints
/// shows it - the mistake and the change are both run with Node.js. Code that does what it says is left alone. A computer
/// whose Node.js does not run TypeScript itself passes over them.
/// </summary>
public class TypeScriptLogicTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string? Node()
    {
        if (TargetFactory.FindOnPath("node") is { } node && TypeScriptCheck.NodeRunsTypeScript(node)) return node;

        output.WriteLine("Not run on this computer: no Node.js here runs TypeScript itself.");
        return null;
    }

    private string Write(string name, string code)
    {
        var path = Path.Combine(_temp.Path, name, "main.ts");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
        return path;
    }

    /// <summary>
    /// Runs a TypeScript file with Node.js and gives back what it printed, failing on anything Node said. The minute allowed is
    /// only against a run that never ends: CI's busy runner once took over ten seconds to run a two-line program with Node.
    /// </summary>
    private static async Task<string> RunAsync(string node, string file)
    {
        using var running = Process.Start(new ProcessStartInfo(node, $"\"{Path.GetFileName(file)}\"")
        {
            WorkingDirectory = Path.GetDirectoryName(file)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        var printed = running.StandardOutput.ReadToEndAsync();
        var said = running.StandardError.ReadToEndAsync();

        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        await running.WaitForExitAsync(limit.Token);

        Assert.True(running.ExitCode == 0 && (await said).Trim().Length == 0, $"{file}: Node.js said:\n{await said}");
        return (await printed).Trim().ReplaceLineEndings("\n");
    }

    private static string Changed(string code, LocalFix fix)
    {
        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(fix.StartLine - 1, fix.RemoveCount);
        lines.InsertRange(fix.StartLine - 1, fix.NewLines);
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>A mistake written in TypeScript: the check that finds it, at which line, the change it makes, and - when it shows - what each prints.</summary>
    public static TheoryData<string, string, string, int, string?, string?, string?> Mistakes => new()
    {
        { "empty loop body", "const marks: number[] = [70, 80];\nlet total: number = 0;\nfor (let i: number = 0; i < marks.length; i++);\n{\n  total += 1;\n}\nconsole.log(total);\n",
            "logic-empty-loop-body", 3, "for (let i: number = 0; i < marks.length; i++)", "1", "2" },
        { "empty if body", "const mark: number = 30;\nif (mark > 50);\n{\n  console.log(\"pass\");\n}\n", "logic-empty-if-body", 2, "if (mark > 50)", "pass", "" },
        { "result discarded", "let name: string = \" ada \";\nname.trim();\nconsole.log(`[${name}]`);\n", "logic-result-discarded", 2, "name = name.trim();", "[ ada ]", "[ada]" },
        { "assignment in condition", "let items: number = 3;\nif (items = 0) {\n  console.log(\"none\");\n}\n", "logic-assignment-in-condition", 2, "if (items === 0) {", null, null },
        { "bitwise precedence", "const flags: number = 6;\nif (flags & 1 == 0) {\n  console.log(\"even\");\n}\n", "logic-bitwise-precedence", 2, "if ((flags & 1) == 0) {", "", "even" },
        { "switch fallthrough", "const grade: string = \"A\";\nlet said: string = \"\";\nswitch (grade) {\n  case \"A\":\n    said = \"top\";\n  case \"B\":\n    said = \"good\";\n    break;\n}\nconsole.log(said);\n",
            "logic-switch-fallthrough", 5, "    break;", "good", "top" },
        { "var in closure", "for (var i: number = 0; i < 3; i++) {\n  setTimeout(() => console.log(i), 10);\n}\n", "logic-js-var-in-closure", 1, "for (let i: number = 0; i < 3; i++) {", "3\n3\n3", "0\n1\n2" },
        { "numeric sort", "const scores: number[] = [9, 10, 100];\nscores.sort();\nconsole.log(scores);\n", "logic-js-numeric-sort", 2, "scores.sort((a, b) => a - b);", "[ 10, 100, 9 ]", "[ 9, 10, 100 ]" },
        { "map parseInt", "const numbers: number[] = [\"1\", \"2\", \"3\"].map(parseInt);\nconsole.log(numbers);\n", "logic-js-map-parseint", 1,
            "const numbers: number[] = [\"1\", \"2\", \"3\"].map(Number);", "[ 1, NaN, NaN ]", "[ 1, 2, 3 ]" },
        { "reset in loop", "const prices: number[] = [3, 4];\nlet total: number = 0;\nfor (const price of prices) {\n  total = 0;\n  total += price;\n}\nconsole.log(total);\n",
            "logic-reset-in-loop", 4, "total = 0;|for (const price of prices) {", "4", "7" },
        { "self assignment", "class Person {\n  private name: string;\n  constructor(name: string) {\n    name = name;\n  }\n}\nconsole.log(new Person(\"Ada\"));\n",
            "logic-self-assignment", 4, "    this.name = name;", "Person { name: undefined }", "Person { name: 'Ada' }" },
        { "lost increment", "let count: number = 0;\ncount = count++;\nconsole.log(count);\n", "logic-lost-increment", 2, "count++;", "0", "1" },
        { "off by one", "const names: string[] = [\"Ada\", \"Bob\"];\nfor (let i: number = 0; i <= names.length; i++) {\n  console.log(names[i]);\n}\n",
            "logic-off-by-one-length", 2, "for (let i: number = 0; i < names.length; i++) {", "Ada\nBob\nundefined", "Ada\nBob" },
        { "or constant", "const answer: string = \"no\";\nif (answer === \"yes\" || \"y\") {\n  console.log(\"going\");\n}\n", "logic-or-constant", 2,
            "if (answer === \"yes\" || answer === \"y\") {", "going", "" },
        { "empty catch", "try {\n  JSON.parse(\"{\");\n} catch (err: unknown) {}\n", "logic-empty-catch", 3,
            "} catch (err: unknown) {|  console.error(\"Something went wrong:\", err);|}", null, null },
        { "template in quotes", "const name: string = \"Ada\";\nconsole.log(\"Hello ${name}\");\n", "logic-js-template-in-quotes", 2, "console.log(`Hello ${name}`);", "Hello ${name}", "Hello Ada" },
        { "compare with new array", "const items: number[] = [];\nif (items === []) {\n  console.log(\"empty\");\n}\n", "logic-js-compare-with-new-array", 2, "if (items.length === 0) {", "", "empty" },
        { "NaN comparison", "const value: number = Number(\"x\");\nif (value === NaN) {\n  console.log(\"not a number\");\n}\n", "logic-nan-comparison", 2, "if (Number.isNaN(value)) {", "", "not a number" },
        { "case never matches", "const answer: string = \"y\";\nif (answer.toLowerCase() === \"Y\") {\n  console.log(\"yes\");\n}\n", "logic-case-never-matches", 2,
            "if (answer.toLowerCase() === \"y\") {", "", "yes" },
        { "loop copy changed", "const marks: number[] = [70, 80, 90];\nfor (let mark of marks) {\n  mark += 5;\n}\nconsole.log(marks);\n", "logic-js-loop-copy-changed", 3,
            "for (let index = 0; index < marks.length; index++) {|  marks[index] += 5;", "[ 70, 80, 90 ]", "[ 75, 85, 95 ]" },
        { "loop copy given a value that reads it", "const items: string[] = [\" a \"];\nfor (let item of items) {\n  item = item.trim();\n}\nconsole.log(items);\n",
            "logic-js-loop-copy-changed", 3, "for (let index = 0; index < items.length; index++) {|  items[index] = items[index].trim();", "[ ' a ' ]", "[ 'a' ]" },
        { "float equality", "const total: number = 0.1 + 0.2;\nif (total === 0.3) {\n  console.log(\"equal\");\n}\n", "logic-js-float-equality", 2,
            "if (Math.abs(total - 0.3) < 1e-9) {", "", "equal" },
        { "indexOf above zero", "const names: string[] = [\"Ada\", \"Bob\"];\nif (names.indexOf(\"Ada\") > 0) {\n  console.log(\"found\");\n}\n", "logic-index-of-above-zero", 2,
            "if (names.indexOf(\"Ada\") >= 0) {", "", "found" },
        { "caret as power", "const side: number = 5;\nconst area: number = side ^ 2;\nconsole.log(area);\n", "logic-caret-as-power", 2, "const area: number = side * side;", "7", "25" },
        { "loop steps away", "const n: number = 3;\nfor (let i: number = 0; i <= n; i -= 1) {\n  console.log(i);\n}\n", "logic-loop-steps-away", 2,
            "for (let i: number = 0; i <= n; i += 1) {", null, "0\n1\n2\n3" },
        { "loop never advances", "let i: number = 0;\nwhile (i < 3) {\n  console.log(i);\n}\n", "logic-loop-never-advances", 2, "  i++;", null, "0\n1\n2" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task EachMistakeWrittenInTypeScriptIsFoundAndItsChangeKeepsItsTypes(
        string mistake, string code, string check, int line, string? changedTo, string? mistakePrints, string? changePrints)
    {
        if (Node() is not { } node) return;

        var file = Write(mistake.Replace(' ', '-'), code);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(file)!), finding => finding.PatternId == check);
        Assert.Equal(line, found.Line);

        if (changedTo is null) return;
        Assert.Equal(changedTo, string.Join("|", found.Fix!.NewLines));

        if (mistakePrints is not null) Assert.Equal(mistakePrints, await RunAsync(node, file));
        if (changePrints is null) return;

        var changed = Write(mistake.Replace(' ', '-') + "-changed", Changed(code, found.Fix));
        Assert.Equal(changePrints, await RunAsync(node, changed));
    }

    [Fact]
    public void TheSameConditionTwiceIsFoundInTypeScriptToo()
    {
        if (Node() is null) return;

        var found = LogicPatterns.Scan(SourceFile.Read(Write("duplicate", "const n: number = 4;\nif (n > 5) {\n  console.log(\"a\");\n} else if (n > 5) {\n  console.log(\"b\");\n}\n"))!);
        Assert.Equal(4, Assert.Single(found, finding => finding.PatternId == "logic-duplicate-condition").Line);
    }

    [Fact]
    public void TypeScriptThatDoesWhatItSaysIsLeftAlone()
    {
        if (Node() is null) return;

        const string code = """
            interface Student {
              name: string;
              marks: number[];
            }

            type Grade = "pass" | "fail";

            function average(values: number[]): number {
              if (values.length === 0) return 0;
              return values.reduce((sum: number, value: number) => sum + value, 0) / values.length;
            }

            function grade(student: Student): Grade {
              return average(student.marks) >= 50 ? "pass" : "fail";
            }

            const students: Array<Student> = [
              { name: "Ada", marks: [70, 80] },
              { name: "Bob", marks: [30, 40] },
            ];

            const byName = new Map<string, Student>();
            for (const student of students) {
              byName.set(student.name, student);
            }

            for (let i: number = 0; i < students.length; i++) {
              const found: Student | undefined = byName.get(students[i].name);
              console.log(`${found!.name}: ${grade(found!)}`);
            }

            const sorted: number[] = [...students[0].marks].sort((a: number, b: number) => a - b);
            console.log(sorted.length > 0 ? sorted[0] as number : -1);
            """;

        Assert.Empty(LogicPatterns.Scan(SourceFile.Read(Write("left-alone", code + "\n"))!));
    }

    [Fact]
    public void TypeScriptNodeCannotReadIsLeftToItsSyntaxError()
    {
        if (Node() is null) return;

        // An enum is TypeScript Node.js will not run with its types taken out: the program is refused before it runs, so the
        // logic checks have nothing to read.
        Assert.Empty(LogicPatterns.Scan(SourceFile.Read(Write("enum", "enum Grade { Pass, Fail }\nlet total: number = 0;\nif (total = 1) {\n}\n"))!));
    }
}
