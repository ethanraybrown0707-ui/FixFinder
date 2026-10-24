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
/// Dart programs checked whole, as the window checks them: read by dart analyze before they run, run by dart, what the
/// analyzer says and the exceptions that stop them found at their lines - in a file the program imports too - with changes
/// checked in copies, a program in a package run without its packages ever being fetched, and every example a Dart guide
/// shows analyzed and run. A computer without Dart passes over them.
/// </summary>
public class DartLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string? Dart()
    {
        if (TargetFactory.FindOnPath("dart") is { } dart)
        {
            output.WriteLine($"Checked with {ScriptLanguages.VersionOf(ScriptLanguages.Dart)}: {dart}");
            return dart;
        }

        output.WriteLine("Not run on this computer: no Dart is here.");
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
        var launch = TargetFactory.FromFile(file, LiveAllowance.For(file, TimeSpan.FromMinutes(3)));
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Dart }.CheckAsync(launch);

        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
        {
            output.WriteLine($"{Path.GetFileName(finding.File)}:{finding.Line}: {finding.Kind} {finding.Severity} {finding.RuleId}: {finding.Title} | {finding.Verified.Summary}");
            if (finding.Fix is { } fix) output.WriteLine($"  change line {fix.StartLine} to: {string.Join(" / ", fix.NewLines)}");
            if (finding.Error is { } error) output.WriteLine("  as Dart said it:\n    " + error.RawText.Replace("\n", "\n    "));
        }

        foreach (var line in report.Run?.Run?.Lines ?? []) output.WriteLine($"  | {line.Text}");
        return report;
    }

    /// <summary>Runs Dart code here, as dart main.dart, and gives back what it printed, failing on anything Dart said.</summary>
    private async Task<string> RunAsync(string dart, string code, string name)
    {
        var file = Write(Path.Combine("changed", name, "main.dart"), code);
        using var running = Process.Start(new ProcessStartInfo(dart, "main.dart")
            { WorkingDirectory = Path.GetDirectoryName(file)!, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
        var printed = await running.StandardOutput.ReadToEndAsync();
        var said = await running.StandardError.ReadToEndAsync();
        await running.WaitForExitAsync();

        Assert.True(running.ExitCode == 0 && said.Length == 0, $"{name}: dart said:\n{said}\n{printed}");
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
    [InlineData("void main() {\n  var total = 10\n  print(total);\n}\n", "dart-missing-semicolon", 2, 2, "  var total = 10;", "10")]
    [InlineData("void main() {\n  var total = 10;\n  print(totl + 1);\n}\n", "dart-nearest-name", 3, 3, "  print(total + 1);", "11")]
    [InlineData("int average(List<int> marks) => marks.isEmpty ? 0 : marks.reduce((a, b) => a + b) ~/ marks.length;\n\nvoid main() {\n  print(averge([70, 80]));\n}\n", "dart-nearest-name", 4, 4, "  print(average([70, 80]));", "75")]
    [InlineData("void main() {\n  var name = \"Ada\";\n  print(name.toUppercase());\n}\n", "dart-nearest-member", 3, 3, "  print(name.toUpperCase());", "ADA")]
    [InlineData("void main() {\n  var name = \"Ada\";\n  print(name.lenght);\n}\n", "dart-nearest-member", 3, 3, "  print(name.length);", "3")]
    [InlineData("void main() {\n  int total = 7;\n  int count = 2;\n  int average = total / count;\n  print(average);\n}\n", "dart-whole-number-division", 4, 4, "  int average = total ~/ count;", "3")]
    [InlineData("void main() {\n  Map<String, int> ages = {\"Ada\": 36};\n  int age = ages[\"Bob\"];\n  print(age + 1);\n}\n", "dart-null-default", 3, 3, "  int age = ages[\"Bob\"] ?? 0;", "1")]
    [InlineData("void main() {\n  var total = 12;\n  print(\"Total: \" + total);\n}\n", "dart-number-to-text", 3, 3, "  print(\"Total: \" + total.toString());", "Total: 12")]
    [InlineData("void main() {\n  var mark = 70;\n  if (mark = 40) {\n    print(\"forty\");\n  }\n  print(mark);\n}\n", "dart-compare-with-double-equals", 3, 3, "  if (mark == 40) {", "70")]
    [InlineData("void main() {\n  final passMark = 40;\n  passMark = 50;\n  print(passMark);\n}\n", "dart-final-changed", 3, 2, "  var passMark = 40;", "50")]
    [InlineData("void greet({String name}) {\n  print('Hello, $name');\n}\n\nvoid main() {\n  greet(name: 'Ada');\n}\n", "dart-required-parameter", 1, 1, "void greet({required String name}) {", "Hello, Ada")]
    [InlineData("void main() {\n  String name = null;\n  print(name);\n}\n", "dart-nullable-type", 2, 2, "  String? name = null;", "null")]
    [InlineData("void main() {\n  var total = 150;\n  print(\"Total: %d\", total);\n}\n", "dart-format-into-text", 3, 3, "  print(\"Total: $total\");", "Total: 150")]
    [InlineData("void main() {\n  var name = \"Ada\";\n  var total = 150;\n  print(\"%s scored %.1f\", name, total / 7);\n}\n", "dart-format-into-text", 4, 4,
        "  print(\"$name scored ${(total / 7).toStringAsFixed(1)}\");", "Ada scored 21.4")]
    public async Task WhatTheAnalyzerFindsIsFoundBeforeItRunsAndItsChangeAnalyzesAndRuns(string code, string rule, int line, int changedLine, string changedTo, string fixedPrints)
    {
        if (Dart() is not { } dart) return;

        var report = await CheckAsync(Write(Path.Combine($"analyzed{code.Length}", "main.dart"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == rule);
        Assert.NotEqual(FindingKind.Runtime, found.Kind);
        Assert.Equal(line, found.Line);
        Assert.Equal(changedLine, found.Fix?.StartLine);
        Assert.Equal(changedTo, found.Fix?.NewLines.Single());
        Assert.Contains("checked it with dart analyze", found.FixCheckedBy);
        Assert.Equal(fixedPrints, await RunAsync(dart, Changed(code, found), $"analyzed{code.Length}"));
    }

    /// <summary>The analyzer's line, as the report shows it, has each part the description of Dart's messages names.</summary>
    [Fact]
    public async Task TheAnalyzersMessageHasThePartsItsDescriptionNames()
    {
        if (Dart() is null) return;

        var report = await CheckAsync(Write(Path.Combine("described", "main.dart"), "void main() {\n  var total = 10;\n  print(totl + 1);\n}\n"));

        var found = Assert.Single(report.Findings, finding => finding.Error?.ErrorCode == "undefined_identifier");
        Assert.All(["error - main.dart:3:9 - Undefined name 'totl'.", " Try ", " - undefined_identifier"], part => Assert.Contains(part, found.Error!.RawText));
    }

    [Fact]
    public async Task AWholeNumberDividedByZeroStopsTheProgramAndGetsItsGuard()
    {
        if (Dart() is not { } dart) return;

        const string code = "void main() {\n  var marks = <int>[];\n  print(70 ~/ marks.length);\n}\n";
        var report = await CheckAsync(Write(Path.Combine("divided", "main.dart"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == "dart-division-guard");
        Assert.Equal((FindingKind.Runtime, 3), (found.Kind, found.Line!.Value));
        Assert.Equal("  print((marks.length == 0 ? 0 : 70 ~/ marks.length));", found.Fix?.NewLines.Single());
        Assert.True(found.Verified.IsVerified, found.Verified.Summary);
        Assert.Equal("0", await RunAsync(dart, Changed(code, found), "divided"));
    }

    /// <summary>The loop to &lt;= length is found by the logic check and by the RangeError its run stops with, with the same change: one finding.</summary>
    [Fact]
    public async Task ALoopPastTheEndIsOneFindingThoughItsRunStoppedWithRangeError()
    {
        if (Dart() is not { } dart) return;

        const string code = "void main() {\n  var marks = [70, 80, 90];\n  for (var i = 0; i <= marks.length; i++) {\n    print(marks[i]);\n  }\n}\n";
        var report = await CheckAsync(Write(Path.Combine("loop", "main.dart"), code));

        var loop = Assert.Single(report.Findings);
        Assert.Equal(3, loop.Fix?.StartLine);
        Assert.Equal("  for (var i = 0; i < marks.length; i++) {", loop.Fix?.NewLines.Single());
        Assert.Equal("70\n80\n90", await RunAsync(dart, Changed(code, loop), "loop"));
    }

    [Theory]
    [InlineData("void main() {\n  var first = [70, 80];\n  var second = [70, 80];\n  print(first == second);\n}\n", "logic-dart-lists-compared-with-equals", 4, "true")]
    [InlineData("void main() {\n  var total = 0.1 + 0.2;\n  print(total == 0.3);\n}\n", "logic-dart-float-equality", 3, "true")]
    // Dart puts in $marks alone - the mistake printed "Marks counted: [70, 80, 90].length" and "First: [70, 80, 90][0]".
    [InlineData("void main() {\n  var marks = [70, 80, 90];\n  print('Marks counted: $marks.length');\n}\n", "logic-dart-interpolated-property", 3, "Marks counted: 3")]
    [InlineData("void main() {\n  var marks = [70, 80, 90];\n  print('First: $marks[0]');\n}\n", "logic-dart-interpolated-property", 3, "First: 70")]
    public async Task ALogicMistakeTheAnalyzerSaysNothingOfIsFoundAndItsChangePrintsTheRightAnswer(string code, string check, int line, string fixedPrints)
    {
        if (Dart() is not { } dart) return;

        var report = await CheckAsync(Write(Path.Combine($"logic{code.Length}", "main.dart"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == check);
        Assert.Equal(line, found.Line);
        Assert.Equal(fixedPrints, await RunAsync(dart, Changed(code, found), $"logic{code.Length}"));
    }

    [Fact]
    public async Task AnExceptionInAnImportedFileIsPlacedThere()
    {
        if (Dart() is null) return;

        var helpers = Write(Path.Combine("program", "helpers.dart"),
            "int average(List<int> marks) {\n  var total = 0;\n  for (var mark in marks) {\n    total += mark;\n  }\n  return total ~/ marks.length;\n}\n");
        var main = Write(Path.Combine("program", "main.dart"), "import 'helpers.dart';\n\nvoid main() {\n  print(average([]));\n}\n");

        var report = await CheckAsync(main);

        var crash = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal((helpers, 6), (crash.File, crash.Line!.Value));
        Assert.Equal("dart-division-guard", crash.RuleId);
    }

    [Fact]
    public async Task WhatTheAnalyzerFindsInAnImportedFileIsFoundThere()
    {
        if (Dart() is null) return;

        var helpers = Write(Path.Combine("analyzedprogram", "helpers.dart"), "int doubled(int n) {\n  var result = n * 2\n  return result;\n}\n");
        var main = Write(Path.Combine("analyzedprogram", "main.dart"), "import 'helpers.dart';\n\nvoid main() {\n  print(doubled(4));\n}\n");

        var report = await CheckAsync(main);

        var found = Assert.Single(report.Findings, finding => finding.RuleId == "dart-missing-semicolon");
        Assert.Equal((helpers, 2), (found.File, found.Line!.Value));
    }

    /// <summary>
    /// A program in a package - a folder with pubspec.yaml - is run as dart main.dart, which fetches nothing and writes nothing
    /// into the folder: dart run would have resolved the package's dependencies, writing pubspec.lock and .dart_tool.
    /// </summary>
    [Fact]
    public async Task AProgramInAPackageIsRunWithoutItsPackagesBeingResolved()
    {
        if (Dart() is null) return;

        Write(Path.Combine("package", "pubspec.yaml"), "name: marks\nenvironment:\n  sdk: ^3.0.0\n");
        var main = Write(Path.Combine("package", "main.dart"), "void main() {\n  print('hi');\n}\n");

        var report = await CheckAsync(main);

        Assert.Equal("hi", report.Run?.Run?.Lines.Single(line => line.Stream == StreamKind.StdOut).Text);
        Assert.False(File.Exists(Path.Combine(_temp.Path, "package", "pubspec.lock")));
        Assert.False(Directory.Exists(Path.Combine(_temp.Path, "package", ".dart_tool")));
    }

    /// <summary>Every whole program a Dart guide shows analyzes with no issues and runs with nothing from Dart but its output.</summary>
    [Fact]
    public async Task EveryExampleADartGuideShowsAnalyzesAndRunsClean()
    {
        if (Dart() is not { } dart) return;

        var examples = DartGuides.All.Concat(DartPatternGuides.All).Select(entry => entry.Guide.Example).Where(example => example.Length > 0).Distinct().ToList();

        foreach (var (example, number) in examples.Select((example, index) => (example, index + 1)))
        {
            var file = Write(Path.Combine("examples", $"example{number}.dart"), example + "\n");

            using var analyzing = Process.Start(new ProcessStartInfo(dart, $"analyze \"{Path.GetFileName(file)}\"")
                { WorkingDirectory = Path.GetDirectoryName(file)!, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
            var said = await analyzing.StandardOutput.ReadToEndAsync() + await analyzing.StandardError.ReadToEndAsync();
            await analyzing.WaitForExitAsync();
            Assert.True(analyzing.ExitCode == 0 && said.Contains("No issues found!", StringComparison.Ordinal), $"example {number}:\n{example}\ndart analyze said:\n{said}");

            await RunAsync(dart, example + "\n", $"example{number}");
        }

        output.WriteLine($"{examples.Count} examples analyzed and run.");
    }
}
