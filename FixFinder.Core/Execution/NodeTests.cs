using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// Running a JavaScript or TypeScript file of tests written for Node.js's own test runner, node:test, test by test: node
/// --test runs them, as running them would, and a reporter of FixFinder's prints what each test did as one line, read back
/// by <see cref="TestReport"/>. node:test is part of Node.js, so nothing is installed to run them.
/// </summary>
public static partial class NodeTests
{
    /// <summary>What the test runner is called where a test is said to have been run by it.</summary>
    public const string RunnerName = "node:test";

    /// <summary>A file's use of the test runner: require("node:test"), or an import from it, outside its comments.</summary>
    [GeneratedRegex(@"\brequire\s*\(\s*(?:'node:test'|""node:test"")\s*\)|\bfrom\s+(?:'node:test'|""node:test"")|^\s*import\s+(?:'node:test'|""node:test"")", RegexOptions.Multiline)]
    private static partial Regex UsesNodeTest();

    /// <summary>Whether a JavaScript or TypeScript file's tests are written for node:test: it requires or imports it.</summary>
    public static bool AreItsTests(string file)
    {
        if (Path.GetExtension(file).ToLowerInvariant() is not (".js" or ".mjs" or ".cjs" or ".ts" or ".mts" or ".cts")) return false;

        try
        {
            var text = File.ReadAllText(file);
            var withoutComments = string.Join("\n", LocalFixes.CodeText.MaskAll(text.ReplaceLineEndings("\n").Split('\n'), LocalFixes.Syntax.CLike)
                .Zip(text.ReplaceLineEndings("\n").Split('\n'), (masked, raw) => masked.Trim().Length == 0 && raw.TrimStart().StartsWith("//", StringComparison.Ordinal) ? "" : raw));
            return UsesNodeTest().IsMatch(withoutComments);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>What a program's own checks are called where one is said to have failed.</summary>
    public const string ConsoleAssertName = "console.assert";

    [GeneratedRegex(@"\bconsole\s*\.\s*assert\s*\(")]
    private static partial Regex ConsoleAssertCall();

    /// <summary>
    /// Whether a JavaScript or TypeScript program checks itself with console.assert - outside its comments - and is not a
    /// file of node:test's tests. console.assert only prints "Assertion failed" and carries on, so a check that fails is
    /// easy to miss, and says nothing of where it is.
    /// </summary>
    public static bool ChecksWithConsoleAssert(string file)
    {
        if (Path.GetExtension(file).ToLowerInvariant() is not (".js" or ".mjs" or ".cjs" or ".ts" or ".mts" or ".cts") || AreItsTests(file)) return false;

        try
        {
            return LocalFixes.CodeText.MaskAll(File.ReadAllText(file).ReplaceLineEndings("\n").Split('\n'), LocalFixes.Syntax.CLike).Any(line => ConsoleAssertCall().IsMatch(line));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The name the console.assert watcher is written under.</summary>
    public const string ConsoleAssertWatcherPrefix = "fixfinder_console_assert_watcher";

    /// <summary>The console.assert watcher, written where Node.js can read it, for node --require to load before the program.</summary>
    public static string WriteConsoleAssertWatcher() => PythonTests.Written(ConsoleAssertWatcherPrefix, ConsoleAssertWatcherSource, ".cjs");

    /// <summary>
    /// The console.assert watcher, loaded before the program with node --require: each console.assert the program reaches
    /// is said as one line in the form TestReport reads - the console.assert on line 9, and whether what it checks held, with
    /// what it was given to say - and then does what console.assert does, so the program runs as it would.
    /// </summary>
    private const string ConsoleAssertWatcherSource = """
        // Written by FixFinder: says of each console.assert a program reaches whether what it checks held, one line each.
        "use strict";

        const path = require("node:path");
        const util = require("node:util");
        const { fileURLToPath } = require("node:url");

        const RESULT = "FIXFINDER-TEST";

        function clean(text) {
          return String(text).replace(/\\/g, "\\\\").replace(/\t/g, "\\t").replace(/\r/g, "\\r").replace(/\n/g, "\\n");
        }

        function part(text) {
          // The parts of a place are joined with # and ;, which a folder's name can hold, so those - and % - are written %23, %3B and %25.
          return String(text).replace(/%/g, "%25").replace(/#/g, "%23").replace(/;/g, "%3B");
        }

        function caller() {
          // The place console.assert was called from: the first in the stack that is not this file's nor Node.js's own.
          for (const line of String(new Error().stack ?? "").split("\n").slice(1)) {
            const place = /^\s*at (?:.*? \()?(.+?):(\d+):(\d+)\)?$/.exec(line);
            if (!place || place[1].startsWith("node:")) continue;
            const file = place[1].startsWith("file:") ? fileURLToPath(place[1]) : place[1];
            if (path.resolve(file) === path.resolve(__filename)) continue;
            return { file, line: Number(place[2]) };
          }
          return null;
        }

        const consoleAssert = console.assert;
        console.assert = function (condition, ...data) {
          const from = caller();
          if (from) {
            const name = `the console.assert on line ${from.line}`;
            const owner = path.basename(from.file).replace(/\.[^.]+$/, "");
            const fields = condition
              ? [RESULT, "SUCCESSFUL", name, owner, name, "", "", ""]
              : [RESULT, "FAILED", name, owner, name, "AssertionError", data.length > 0 ? util.format(...data) : "",
                 [part(owner), "<module>", part(from.file), from.line].join("#")];
            process.stdout.write(fields.map((field, index) => (index > 0 ? clean(field) : field)).join("\t") + "\n");
          }
          return consoleAssert.call(this, condition, ...data);
        };
        """;

    /// <summary>The name the reporter is written under, so a report can tell a failure of FixFinder's own reporter from the tests'.</summary>
    public const string ReporterPrefix = "fixfinder_node_test_reporter";

    /// <summary>
    /// The reporter, written where Node.js can read it, as the file: URL node --test-reporter takes - a Windows path is read
    /// as a URL whose scheme is the drive letter, and refused.
    /// </summary>
    public static string ReporterUrl() => new Uri(PythonTests.Written(ReporterPrefix, ReporterSource, ".mjs")).AbsoluteUri;

    /// <summary>
    /// The reporter: Node.js's test runner hands it each test as it starts and ends. It prints one line for each test - not
    /// for a describe block, which is a suite of them, nor for a test that failed only because a test inside it did - in the
    /// form TestReport reads, named with the suites it is in, with where a failure was thrown, innermost first and without
    /// Node.js's own places; and, once the run ends, how many tests it found.
    /// </summary>
    private const string ReporterSource = """
        // Written by FixFinder: Node.js's test runner reports each test to this, and it prints what each test did, one line each.

        import { fileURLToPath } from "node:url";
        import path from "node:path";

        const RESULT = "FIXFINDER-TEST";
        const FOUND = "FIXFINDER-TESTS-FOUND";

        function clean(text) {
          return String(text).replace(/\\/g, "\\\\").replace(/\t/g, "\\t").replace(/\r/g, "\\r").replace(/\n/g, "\\n");
        }

        function part(text) {
          // The parts of a place are joined with # and ;, which a folder's name can hold, so those - and % - are written %23, %3B and %25.
          return String(text).replace(/%/g, "%25").replace(/#/g, "%23").replace(/;/g, "%3B");
        }

        function nameOf(file) {
          return path.basename(file).replace(/\.[^.]+$/, "");
        }

        function framesOf(error) {
          // Where it was thrown, innermost first, from its stack - "at Stack.peek (C:\work\stack.js:5:49)" - without node:'s own,
          // each named with its file's name, as Python's are with their module's.
          const frames = [];
          for (const line of String(error?.stack ?? "").split("\n")) {
            const place = /^\s*at (?:(.*?) \()?(.+?):(\d+):(\d+)\)?$/.exec(line);
            if (!place || place[2].startsWith("node:")) continue;
            const file = place[2].startsWith("file:") ? fileURLToPath(place[2]) : place[2];
            frames.push([part(nameOf(file)), part(place[1] ?? "<anonymous>"), part(file), place[3]].join("#"));
          }
          return frames.join(";");
        }

        export default async function* reporter(source) {
          // The suites and tests running, by how deep each is: a test is named with the describe blocks it is in.
          const running = [];
          let reported = 0;
          for await (const event of source) {
            const data = event.data;
            if (event.type === "test:start") {
              running.length = data.nesting;
              running.push(data.name);
              continue;
            }
            if (event.type !== "test:pass" && event.type !== "test:fail") continue;
            if (data.details?.type === "suite") continue;

            const failure = data.details?.error;
            if (failure?.failureType === "subtestsFailed") continue;

            const file = data.file ?? "";
            const owner = [nameOf(file), ...running.slice(0, data.nesting)].join(" > ");
            const status = data.skip || data.todo ? "SKIPPED" : event.type === "test:pass" ? "SUCCESSFUL" : "FAILED";
            const thrown = failure?.cause ?? failure;
            const fields = status === "FAILED"
              ? [RESULT, status, data.name, owner, data.name, thrown?.name ?? "", thrown?.message ?? "", framesOf(thrown)]
              : [RESULT, status, data.name, owner, data.name, "", typeof data.skip === "string" ? data.skip : "", ""];
            reported++;
            yield fields.map((field, index) => (index > 0 ? clean(field) : field)).join("\t") + "\n";
          }
          // Every test is known once the run ends: those in a describe block are not found until it runs.
          yield `${FOUND}\t${reported}\n`;
        }
        """;
}
