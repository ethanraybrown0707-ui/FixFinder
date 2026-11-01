using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// JavaScript and TypeScript tests written for Node.js's own test runner, checked as a student would: node --test runs
/// them, each test that fails is said on its own line in node:assert's words, its ending with code 1 is not a finding of
/// its own, and the change to the code the tests bring in that makes every test pass is found - with the program's own
/// files never written to.
/// </summary>
public class NodeTestLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        return path;
    }

    private async Task<CheckReport> CheckAsync(string file)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);
        output.WriteLine($"how: {launch.Explanation}");

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Of(file) ?? CodeLanguage.Any };
        checker.Log += message => output.WriteLine($"log: {message}");
        var report = await checker.CheckAsync(launch);

        output.WriteLine($"syntax: {report.SyntaxSummary}");
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings) output.WriteLine($"[{finding.RuleId}] {finding.Line}: {finding.Title} :: {finding.Fix?.Title} :: {finding.Verified.Summary}");
        return report;
    }

    private const string Grades = """
        function grade(mark) {
          if (mark > 70) return "First";
          if (mark >= 60) return "2:1";
          return "Fail";
        }

        module.exports = { grade };
        """;

    [Fact]
    public async Task TheFailingTestIsSaidAndTheChangeIsFoundInTheCodeItBringsIn()
    {
        if (!LocalFixLiveTests.Available("node")) return;

        var program = Write(@"grades\grades.js", Grades);
        var tests = Write(@"grades\grades.test.js", """
            const test = require("node:test");
            const assert = require("node:assert");
            const { grade } = require("./grades");

            test("first at seventy", () => {
              assert.strictEqual(grade(70), "First");
            });

            test("upper second", () => {
              assert.strictEqual(grade(65), "2:1");
            });
            """);

        var report = await CheckAsync(tests);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors; 1 of 2 tests failed", report.SyntaxSummary);

        // node --test ended with code 1 because a test failed, which is the test's finding, not one of its own.
        var failed = Assert.Single(report.Findings);
        Assert.Equal("test-failed", failed.RuleId);
        Assert.Equal(6, failed.Line);
        Assert.Equal("Test first at seventy failed: Expected values to be strictly equal: '2:1' !== 'First'", failed.Title);
        Assert.StartsWith("node:test ran the test first at seventy, and the assertion on line 6 did not hold", failed.Explanation, StringComparison.Ordinal);

        Assert.NotNull(failed.Fix);
        Assert.Equal(program, failed.Fix!.File, ignoreCase: true);
        Assert.Equal(["  if (mark >= 70) return \"First\";"], failed.Fix.NewLines);
        Assert.True(failed.Verified.IsVerified, failed.Verified.Summary);
        Assert.Equal(Grades.ReplaceLineEndings("\n"), File.ReadAllText(program));
    }

    /// <summary>A stack that gives back its first item and reads one past its end, tested in a describe block: fixed a change at a time.</summary>
    [Fact]
    public async Task TwoMistakesTestedInADescribeBlockAreFixedAChangeAtATime()
    {
        if (!LocalFixLiveTests.Available("node")) return;

        const string Stack = """
            class Stack {
              constructor() {
                this.items = [];
              }

              push(item) {
                this.items.push(item);
              }

              pop() {
                return this.items.shift();
              }

              peek() {
                return this.items[this.items.length];
              }
            }

            module.exports = { Stack };
            """;
        var program = Write(@"stack\stack.js", Stack);
        var tests = Write(@"stack\stack.test.js", """
            const { describe, it } = require("node:test");
            const assert = require("node:assert");
            const { Stack } = require("./stack");

            describe("Stack", () => {
              it("pops the last pushed", () => {
                const s = new Stack();
                s.push(1);
                s.push(2);
                assert.strictEqual(s.pop(), 2);
              });

              it("peeks the last pushed", () => {
                const s = new Stack();
                s.push(1);
                s.push(2);
                assert.strictEqual(s.peek(), 2);
              });
            });
            """);

        var report = await CheckAsync(tests);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors; 2 of 2 tests failed", report.SyntaxSummary);

        var fixedFirst = Assert.Single(report.Findings, finding => finding is { RuleId: "test-failed", Fix: not null });
        Assert.Equal(11, fixedFirst.Fix!.StartLine);
        Assert.Equal(["    return this.items.pop();", "  }", "", "  peek() {", "    return this.items[this.items.length - 1];"], fixedFirst.Fix.NewLines);
        Assert.True(fixedFirst.Verified.IsVerified, fixedFirst.Verified.Summary);
        Assert.Equal(Stack.ReplaceLineEndings("\n"), File.ReadAllText(program));
    }

    /// <summary>TypeScript tests in a folder of their own, importing the program from ../src as an ES module, run by Node.js itself.</summary>
    [Fact]
    public async Task TypeScriptTestsInAFolderOfTheirOwnAreFixedInTheSrcTheyImport()
    {
        if (TargetFactory.FindOnPath("node") is not { } node || !TypeScriptCheck.NodeRunsTypeScript(node)) return;

        const string Grade = """
            export function grade(mark: number): string {
              if (mark > 70) {
                return "First";
              }
              return "Fail";
            }
            """;
        var program = Write(@"marks\src\grades.ts", Grade);
        var tests = Write(@"marks\tests\grades.test.ts", """
            import test from "node:test";
            import assert from "node:assert";
            import { grade } from "../src/grades.ts";

            test("first at seventy", () => {
              assert.strictEqual(grade(70), "First");
            });

            test("fail at thirty", () => {
              assert.strictEqual(grade(30), "Fail");
            });
            """);

        var report = await CheckAsync(tests);

        if (report.SyntaxSummary == "It could not be started") return;
        var failed = Assert.Single(report.Findings, finding => finding.RuleId == "test-failed");
        Assert.Equal(program, failed.Fix?.File, ignoreCase: true);
        Assert.Equal(["  if (mark >= 70) {"], failed.Fix!.NewLines);
        Assert.True(failed.Verified.IsVerified, failed.Verified.Summary);
        Assert.Equal(Grade.ReplaceLineEndings("\n"), File.ReadAllText(program));
    }
}
