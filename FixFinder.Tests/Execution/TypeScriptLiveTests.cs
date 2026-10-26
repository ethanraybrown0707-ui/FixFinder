using System.Diagnostics;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// TypeScript programs checked whole, as the window checks them: checked by Node.js itself before they run - their types
/// stripped and what is left compiled, none of it run - and run by Node.js; what it would not read found before the program
/// runs, and what stopped it at its line, in a file it imports too, with changes checked in copies and run; and every example a
/// TypeScript guide shows checked and run. A computer whose Node.js does not run TypeScript itself passes over them.
/// </summary>
public class TypeScriptLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string? Node()
    {
        if (TargetFactory.FindOnPath("node") is { } node && TypeScriptCheck.NodeRunsTypeScript(node))
        {
            output.WriteLine($"Checked with Node.js {ScriptLanguages.VersionOf(ScriptLanguages.TypeScript)}: {node}");
            return node;
        }

        output.WriteLine("Not run on this computer: no Node.js here runs TypeScript itself.");
        return null;
    }

    private string Write(string relative, string code)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
        return path;
    }

    private async Task<CheckReport> CheckAsync(string file)
    {
        var launch = TargetFactory.FromFile(file, LiveAllowance.For(file, TimeSpan.FromMinutes(2)));
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.TypeScript }.CheckAsync(launch);

        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
        {
            output.WriteLine($"{Path.GetFileName(finding.File)}:{finding.Line}: {finding.Kind} {finding.Severity} {finding.RuleId}: {finding.Title} | {finding.Verified.Summary}");
            if (finding.Fix is { } fix) output.WriteLine($"  change line {fix.StartLine} ({fix.RemoveCount} removed) to: {string.Join(" / ", fix.NewLines)}");
            if (finding.Error is { } error) output.WriteLine("  as Node.js said it:\n    " + error.RawText.Replace("\n", "\n    "));
        }

        foreach (var line in report.Run?.Run?.Lines ?? []) output.WriteLine($"  | {line.Text}");
        return report;
    }

    /// <summary>Runs a TypeScript file with Node.js in its own folder and gives back what it printed, failing on anything Node said.</summary>
    private static async Task<string> RunAsync(string node, string file)
    {
        using var running = Process.Start(new ProcessStartInfo(node, $"\"{Path.GetFileName(file)}\"")
        {
            WorkingDirectory = Path.GetDirectoryName(file)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        var printed = await running.StandardOutput.ReadToEndAsync();
        var said = await running.StandardError.ReadToEndAsync();
        await running.WaitForExitAsync();

        Assert.True(running.ExitCode == 0 && said.Trim().Length == 0, $"{file}: Node.js said:\n{said}");
        return printed.Trim().ReplaceLineEndings("\n");
    }

    private static string Changed(string code, Finding found)
    {
        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(found.Fix!.StartLine - 1, found.Fix.RemoveCount);
        lines.InsertRange(found.Fix.StartLine - 1, found.Fix.NewLines);
        return string.Join("\n", lines) + "\n";
    }

    [Theory]
    [InlineData("const total: number = 10;\nconsole.log(total;\n", "js-missing-closing-paren", 2, 2, "console.log(total);", "10")]
    [InlineData("const name: string = \"Ada;\nconsole.log(name);\n", "js-unclosed-string", 1, 1, "const name: string = \"Ada\";", "Ada")]
    [InlineData("const marks: number[] = [70 80];\nconsole.log(marks.length);\n", "ts-missing-comma", 1, 1, "const marks: number[] = [70, 80];", "2")]
    [InlineData("const total: number = 10;\nif (total > 5 {\n  console.log(\"big\");\n}\n", "ts-condition-closing-bracket", 2, 2, "if (total > 5) {", "big")]
    [InlineData("let count: number = 0;\nfor (let i = 0; i < 3; i++ {\n  count++;\n}\nconsole.log(count);\n", "ts-condition-closing-bracket", 2, 2, "for (let i = 0; i < 3; i++) {", "3")]
    public async Task WhatNodeWillNotReadIsFoundBeforeItRunsAndItsChangeChecksAndRuns(string code, string rule, int line, int changedLine, string changedTo, string fixedPrints)
    {
        if (Node() is not { } node) return;

        var file = Write(Path.Combine($"refused-{rule}-{code.Length}", "main.ts"), code);
        var report = await CheckAsync(file);

        var refused = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Equal((rule, line), (refused.RuleId, refused.Line!.Value));
        Assert.Equal(changedLine, refused.Fix?.StartLine);
        Assert.Equal(changedTo, refused.Fix?.NewLines.Single());
        Assert.Null(report.Run?.Run);

        File.WriteAllText(file, Changed(code, refused));
        Assert.Equal(fixedPrints, await RunAsync(node, file));
    }

    [Fact]
    public async Task ABlockLeftOpenIsClosedWhereItsIndentationEnds()
    {
        if (Node() is not { } node) return;

        const string code = "function double(value: number): number {\n  return value * 2;\n\nconsole.log(double(4));\n";
        var file = Write(Path.Combine("brace", "main.ts"), code);
        var report = await CheckAsync(file);

        var refused = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Equal(("js-missing-closing-brace", 3, 0), (refused.RuleId, refused.Fix!.StartLine, refused.Fix.RemoveCount));
        Assert.Equal(["}"], refused.Fix.NewLines);

        File.WriteAllText(file, Changed(code, refused));
        Assert.Equal("8", await RunAsync(node, file));
    }

    [Theory]
    // Each: the program, the rule for what Node.js stops it with, the rule for what tsc says of it first, where, the change, and what the changed program prints.
    [InlineData("const total: number = 10;\nconsole.log(totl + 1);\n", "js-nearest-name", "ts-did-you-mean", 2, 2, "console.log(total + 1);", "11")]
    [InlineData("const total: number = 10;\ntotal = 20;\nconsole.log(total);\n", "js-const-reassigned", "ts-const-reassigned", 2, 1, "let total: number = 10;", "20")]
    public async Task WhatStopsTheProgramIsFoundAtItsLineAndItsChangeRuns(string code, string rule, string tscRule, int line, int changedLine, string changedTo, string fixedPrints)
    {
        if (Node() is not { } node) return;

        var file = Write(Path.Combine($"stopped-{rule}", "main.ts"), code);
        var report = await CheckAsync(file);

        // Where this computer has tsc, it finds the same mistake before the program runs and offers the same change, so the
        // two are one finding - kept under either rule.
        var stopped = Assert.Single(report.Findings, finding => finding.Line == line && finding.Fix is not null);
        Assert.Contains(stopped.RuleId, new[] { rule, tscRule });
        Assert.Equal(changedLine, stopped.Fix?.StartLine);
        Assert.Equal(changedTo, stopped.Fix?.NewLines.Single());

        File.WriteAllText(file, Changed(code, stopped));
        Assert.Equal(fixedPrints, await RunAsync(node, file));
    }

    [Theory]
    [InlineData("let total: number = 1;\nlet total: number = 2;\nconsole.log(total);\n", "total = 2;", "2")]
    // A function's type holds => of its own, which is not the = that gives it its value.
    [InlineData("let twice: (n: number) => number = (n) => n * 2;\nlet twice: (n: number) => number = (n) => n * 3;\nconsole.log(twice(5));\n", "twice = (n) => n * 3;", "15")]
    public async Task ANameDeclaredAgainWithItsTypeIsGivenItsNewValueInstead(string code, string changedTo, string fixedPrints)
    {
        if (Node() is not { } node) return;

        var file = Write(Path.Combine($"redeclared-{code.Length}", "main.ts"), code);
        var report = await CheckAsync(file);

        var refused = Assert.Single(report.Findings, finding => finding.RuleId == "js-redeclared");
        Assert.Equal((2, 2), (refused.Line!.Value, refused.Fix!.StartLine));
        Assert.Equal(changedTo, refused.Fix.NewLines.Single());

        File.WriteAllText(file, Changed(code, refused));
        Assert.Equal(fixedPrints, await RunAsync(node, file));
    }

    [Fact]
    public async Task AnImportWithoutItsExtensionIsFoundAtTheImportAndGetsIt()
    {
        if (Node() is not { } node) return;

        Write(Path.Combine("import", "helpers.ts"), "export function average(values: number[]): number {\n  return values.reduce((sum, value) => sum + value, 0) / values.length;\n}\n");
        const string code = "import { average } from './helpers';\nconsole.log(average([70, 80]));\n";
        var main = Write(Path.Combine("import", "main.ts"), code);

        var report = await CheckAsync(main);

        var stopped = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal(("js-import-extension", 1), (stopped.RuleId, stopped.Line!.Value));
        Assert.Equal("import { average } from './helpers.ts';", stopped.Fix?.NewLines.Single());

        File.WriteAllText(main, Changed(code, stopped));
        Assert.Equal("75", await RunAsync(node, main));
    }

    [Fact]
    public async Task ACrashInAFileTheProgramImportsIsPlacedThere()
    {
        if (Node() is null) return;

        var helpers = Write(Path.Combine("imported", "helpers.ts"), "export function first(values: number[]): number {\n  const found: number[] | undefined = undefined;\n  return found!.length + values.length;\n}\n");
        var main = Write(Path.Combine("imported", "main.ts"), "import { first } from './helpers.ts';\nconsole.log(first([70]));\n");

        var report = await CheckAsync(main);

        var stopped = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal((helpers, 3), (stopped.File, stopped.Line!.Value));
        Assert.Equal("TypeError", stopped.Error?.ExceptionType);
    }

    [Theory]
    [InlineData("enum Grade { Pass, Fail }\nconsole.log(Grade.Pass);\n", "ERR_UNSUPPORTED_TYPESCRIPT_SYNTAX", 1)]
    [InlineData("let total: number = 1;\nlet total: number = 2;\nconsole.log(total);\n", null, 2)]
    public async Task WhatNodeCannotRunIsFoundBeforeAnyOfItRuns(string code, string? errorCode, int line)
    {
        if (Node() is null) return;

        var report = await CheckAsync(Write(Path.Combine($"cannot-run-{line}", "main.ts"), code));

        var refused = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Equal((errorCode, line), (refused.Error?.ErrorCode, refused.Line!.Value));
        Assert.Null(report.Run?.Run);
    }

    /// <summary>
    /// A value of the wrong type: Node.js strips the types without checking them and runs it, printing ten1 - and where this
    /// computer has TypeScript's compiler, tsc finds it, at its line; where it has none, the note says the types were not checked.
    /// </summary>
    [Fact]
    public async Task AValueOfTheWrongTypeRunsAndTscFindsItWhereItIsHere()
    {
        if (Node() is null) return;

        var file = Write(Path.Combine("types", "main.ts"), "const total: number = \"ten\";\nconsole.log(total + 1);\n");
        var report = await CheckAsync(file);

        Assert.Equal("ten1", report.Run?.Run?.Lines.Single(line => line.Stream == StreamKind.StdOut).Text.Trim());
        if (TypeScriptCompiler.For(file) is null)
        {
            Assert.Empty(report.Findings);
            Assert.Contains(report.Notes, note => note.StartsWith("The types were not checked", StringComparison.Ordinal));
            return;
        }

        var typeError = Assert.Single(report.Findings);
        Assert.Equal(("TS2322", 1), (typeError.Error?.ErrorCode, typeError.Line!.Value));
    }

    /// <summary>Every whole program a TypeScript guide shows checks with nothing said, and runs with nothing printed to the error stream.</summary>
    [Fact]
    public async Task EveryExampleATypeScriptGuideShowsChecksAndRunsClean()
    {
        if (Node() is not { } node) return;

        var examples = TypeScriptGuides.All.Select(entry => entry.Guide.Example).Where(example => example.Length > 0).Distinct().ToList();

        foreach (var (example, number) in examples.Select((example, index) => (example, index + 1)))
        {
            var file = Write(Path.Combine("examples", $"example{number}", "main.ts"), example + "\n");
            var check = ScriptLanguages.CheckOf(ScriptLanguages.TypeScript, file, TimeSpan.FromMinutes(1))!;

            using var checking = Process.Start(new ProcessStartInfo(check.ExecutablePath, check.Arguments)
                { WorkingDirectory = check.WorkingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
            var said = await checking.StandardOutput.ReadToEndAsync() + await checking.StandardError.ReadToEndAsync();
            await checking.WaitForExitAsync();
            Assert.True(checking.ExitCode == 0 && said.Trim().Length == 0, $"example {number}:\n{example}\nthe check said:\n{said}");

            // Where this computer has TypeScript's compiler, it finds no type error in the example either.
            if (TypeScriptCompiler.For(file) is { ChecksAsNodeRuns: true } compiler)
            {
                var typeCheck = TypeScriptCompiler.CheckOf(compiler, node, file, TimeSpan.FromMinutes(1)).Spec;
                using var typing = Process.Start(new ProcessStartInfo(typeCheck.ExecutablePath, typeCheck.Arguments)
                    { WorkingDirectory = typeCheck.WorkingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
                var tscSaid = await typing.StandardOutput.ReadToEndAsync() + await typing.StandardError.ReadToEndAsync();
                await typing.WaitForExitAsync();
                Assert.True(typing.ExitCode == 0 && tscSaid.Trim().Length == 0, $"example {number}:\n{example}\n{compiler.Description} said:\n{tscSaid}");
            }

            await RunAsync(node, file);
        }

        output.WriteLine($"{examples.Count} examples checked and run.");
    }
}
