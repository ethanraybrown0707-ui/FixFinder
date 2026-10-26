using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// TypeScript programs checked whole, as the window checks them, with TypeScript's own compiler, tsc, checking their types: each
/// type error found at its line, with tsc's code for it, while the program still runs as Node.js runs it; Node's own names left
/// out when Node's type definitions are not here, and named to tsc when they are; and a project checked as its tsconfig.json
/// says. A computer without Node.js that runs TypeScript, or without tsc, passes over them and says so.
/// </summary>
public class TypeScriptTypeCheckLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string relative, string code)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
        return path;
    }

    /// <summary>Whether this computer can check the file's types: a Node.js that runs TypeScript, and a tsc of 5.0 or later.</summary>
    private bool Available(string file)
    {
        if (TargetFactory.FindOnPath("node") is not { } node || !TypeScriptCheck.NodeRunsTypeScript(node))
        {
            output.WriteLine("Not run on this computer: no Node.js here runs TypeScript itself.");
            return false;
        }

        if (TypeScriptCompiler.For(file) is not { ChecksAsNodeRuns: true } compiler)
        {
            output.WriteLine("Not run on this computer: no TypeScript compiler, tsc, of 5.0 or later is here.");
            return false;
        }

        output.WriteLine($"Checked with {compiler.Description}: {compiler.Package}");
        return true;
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
        }

        foreach (var line in report.Run?.Run?.Lines ?? []) output.WriteLine($"  | {line.Text}");
        return report;
    }

    private const string Mistakes =
        "import * as readline from \"node:readline\";\n" +
        "\n" +
        "let total: number = \"5\";\n" +
        "const marks = [70, 80, 90];\n" +
        "console.log(marks.lenght);\n" +
        "console.log(process.argv.length > 0);\n" +
        "const found = marks.find((mark) => mark > 85);\n" +
        "console.log(found.toFixed(1));\n" +
        "function average(values) {\n" +
        "  return values.length;\n" +
        "}\n" +
        "console.log(average(marks), total);\n" +
        "readline.createInterface({ input: process.stdin }).close();\n";

    [Fact]
    public async Task EachTypeErrorIsFoundAtItsLineAndTheProgramStillRunsAsNodeRunsIt()
    {
        var file = Write(Path.Combine("mistakes", "main.ts"), Mistakes);
        if (!Available(file)) return;

        var report = await CheckAsync(file);

        Assert.Equal([(3, "TS2322"), (5, "TS2551"), (8, "TS18048"), (9, "TS7006")],
            report.Findings.Where(finding => finding.Error?.LanguageId == "typescript").Select(finding => (finding.Line!.Value, finding.Error!.ErrorCode!)).Order());
        var misspelt = Assert.Single(report.Findings, finding => finding.Line == 5);
        Assert.Equal(("ts-did-you-mean", "console.log(marks.length);"), (misspelt.RuleId, misspelt.Fix?.NewLines.Single()));

        // Node's own names - node:readline and process - are not mistakes: they are named in the note, as not type-checked.
        var note = Assert.Single(report.Notes, each => each.StartsWith("The types were checked by TypeScript's compiler", StringComparison.Ordinal));
        Assert.Contains("it found 4 type errors.", note);
        Assert.Contains("@types/node, are not here, so what the program takes from Node itself - node:readline, process - was not type-checked.", note);

        // Node.js ran it all the same, to its end: found is 90, and lenght is undefined.
        Assert.Equal(["undefined", "true", "90.0", "3 5"], report.Run?.Run?.Lines.Where(line => line.Stream == StreamKind.StdOut).Select(line => line.Text.Trim()));
    }

    [Fact]
    public async Task NodesOwnTypeDefinitionsBesideTheProgramAreNamedToTscAndUsed()
    {
        // A stand-in for @types/node, with one declaration of Node's own: what tsc is given, it uses.
        Write(Path.Combine("typed", "node_modules", "@types", "node", "package.json"), "{\"name\":\"@types/node\",\"version\":\"0.0.0\",\"types\":\"index.d.ts\"}\n");
        Write(Path.Combine("typed", "node_modules", "@types", "node", "index.d.ts"), "declare var process: { argv: string[] };\n");
        var file = Write(Path.Combine("typed", "main.ts"), "const count: number = process.argv;\nconsole.log(count);\n");
        if (!Available(file)) return;

        var report = await CheckAsync(file);

        var typeError = Assert.Single(report.Findings);
        Assert.Equal(("TS2322", 1, "Type 'string[]' is not assignable to type 'number'."), (typeError.Error?.ErrorCode, typeError.Line!.Value, typeError.Error?.Message));
        Assert.DoesNotContain(report.Notes, note => note.Contains("@types/node", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AProjectIsCheckedAsItsTsconfigSaysNotStrictly()
    {
        Write(Path.Combine("project", "tsconfig.json"),
            "{\n  \"compilerOptions\": {\n    \"strict\": false,\n    \"target\": \"esnext\",\n    \"module\": \"esnext\",\n    \"moduleResolution\": \"bundler\",\n" +
            "    \"noEmit\": true\n  },\n  \"include\": [\"*.ts\"]\n}\n");
        var file = Write(Path.Combine("project", "main.ts"), "function double(value) {\n  return value * 2;\n}\nconst total: number = \"ten\";\nconsole.log(double(total));\n");
        if (!Available(file)) return;

        var report = await CheckAsync(file);

        // Not strict, so the parameter with no type is no error; the text given to a number still is.
        var typeError = Assert.Single(report.Findings, finding => finding.Error?.LanguageId == "typescript");
        Assert.Equal(("TS2322", 4), (typeError.Error?.ErrorCode, typeError.Line!.Value));
        Assert.Contains(report.Notes, note => note.Contains("as the project's tsconfig.json sets it up: it found 1 type error.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhatTscSaysOfATsconfigItCannotReadIsInTheNoteNotTheFindings()
    {
        Write(Path.Combine("badly-set-up", "tsconfig.json"), "{\n  \"compilerOptions\": {\n    \"strictt\": true\n  }\n}\n");
        var file = Write(Path.Combine("badly-set-up", "main.ts"), "const total: number = 1;\nconsole.log(total);\n");
        if (!Available(file)) return;

        var report = await CheckAsync(file);

        Assert.Empty(report.Findings);
        Assert.Contains(report.Notes, note => note.EndsWith("tsc also said: tsconfig.json(3,5): error TS5023: Unknown compiler option 'strictt'.", StringComparison.Ordinal));
    }
}
