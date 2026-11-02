using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// Which JavaScript and TypeScript files are tests for Node.js's own test runner, node:test, and which of the program's
/// files they bring in by paths of their own - found as Node.js finds them.
/// </summary>
public class NodeTestsTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string relative, string content = "")
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Theory]
    [InlineData("grades.test.js", "const test = require(\"node:test\");\n")]
    [InlineData("grades.test.mjs", "import { describe, it } from 'node:test';\n")]
    [InlineData("grades.test.ts", "import test from \"node:test\";\n")]
    [InlineData("grades.test.cjs", "const { test } = require('node:test');\n")]
    public void AFileThatBringsInNodeTestIsOneOfItsTests(string name, string source) =>
        Assert.True(NodeTests.AreItsTests(Write(name, source)));

    [Theory]
    // A program, Jest's tests - which need Jest, never downloaded - node:test in a comment, and a Python file.
    [InlineData("grades.js", "function grade(mark) { return mark; }\n")]
    [InlineData("grades.test.js", "test('first', () => { expect(grade(70)).toBe('First'); });\n")]
    [InlineData("grades.test.js", "// const test = require(\"node:test\");\nconsole.log(1);\n")]
    [InlineData("test_grades.py", "import node_test\n")]
    public void AFileThatDoesNotIsNot(string name, string source) =>
        Assert.False(NodeTests.AreItsTests(Write(name, source)));

    [Theory]
    [InlineData("average.js", "console.assert(average([1]) === 1);\n", true)]
    [InlineData("average.ts", "function check(): void {\n  console.assert(1 + 1 === 2, \"sums\");\n}\n", true)]
    // In a comment, or in a file of node:test's tests, whose own runner says what each test did.
    [InlineData("average.js", "// console.assert(average([1]) === 1);\nconsole.log(1);\n", false)]
    [InlineData("average.test.js", "const test = require(\"node:test\");\ntest(\"one\", () => { console.assert(true); });\n", false)]
    public void AProgramThatChecksItselfWithConsoleAssertIsKnown(string name, string source, bool checksItself) =>
        Assert.Equal(checksItself, NodeTests.ChecksWithConsoleAssert(Write(name, source)));

    [Fact]
    public void TheFilesATestBringsInByAPathOfItsOwnAreFoundAsNodeFindsThem()
    {
        var grades = Write(@"project\src\grades.ts");
        var stack = Write(@"project\tests\stack.js");
        var helpers = Write(@"project\tests\helpers\index.js");
        Write(@"project\tests\old.js");
        var tests = Write(@"project\tests\all.test.js", """
            const test = require("node:test");
            const { Stack } = require("./stack");
            const helpers = require('./helpers');
            import { grade } from "../src/grades.ts";
            const assert = require("node:assert");
            const lodash = require("lodash");
            // const old = require("./old");
            """);

        Assert.Equal(new[] { stack, helpers, grades }.Select(Path.GetFullPath), ProgramLayout.JavaScriptImports(tests));
        Assert.Equal(Path.GetFullPath(Path.Combine(_temp.Path, "project")), ProgramCopy.RootOf(tests), ignoreCase: true);
    }
}
