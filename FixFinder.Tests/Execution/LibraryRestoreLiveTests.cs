using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Execution.Libraries;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Restoring a project's libraries for real - downloading them - and then checking the program as the window does: before
/// the restore it stops for want of the library, and after it runs, with the libraries where the run finds them. A
/// language whose tool is not on this computer is passed over, and said to be.
/// </summary>
public class LibraryRestoreLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Each: the language, the project's files, the program, what it prints once its libraries are here, and where they went.</summary>
    public static TheoryData<string, string[], string, string, string> Projects => new()
    {
        {
            "Python", ["requirements.txt", "tabulate==0.9.0\n"], "from tabulate import tabulate\nprint(tabulate([[\"Ada\", 70]], headers=[\"name\", \"mark\"]))\n",
            "Ada         70", @".venv\pyvenv.cfg"
        },
        { "JavaScript", ["package.json", "{ \"name\": \"marks\", \"dependencies\": { \"ms\": \"2.1.3\" } }\n"], "const ms = require(\"ms\");\nconsole.log(ms(60000));\n", "1m", @"node_modules\ms\package.json" },
        {
            "Go", ["go.mod", "module marks\n\ngo 1.22\n\nrequire github.com/google/go-cmp v0.6.0\n"],
            "package main\n\nimport (\n\t\"fmt\"\n\n\t\"github.com/google/go-cmp/cmp\"\n)\n\nfunc main() {\n\tfmt.Println(cmp.Equal([]int{70, 80}, []int{70, 80}))\n}\n",
            "true", "go.sum"
        },
        {
            "Dart", ["pubspec.yaml", "name: marks\nenvironment:\n  sdk: ^3.0.0\ndependencies:\n  path: ^1.9.0\n"],
            "import 'package:path/path.dart' as p;\n\nvoid main() {\n  print(p.basename('marks/ada.txt'));\n}\n", "ada.txt", @".dart_tool\package_config.json"
        },
    };

    private static readonly Dictionary<string, string> ProgramNames = new()
    {
        ["Python"] = "app.py", ["JavaScript"] = "app.js", ["Go"] = "main.go", ["Dart"] = "main.dart",
    };

    private static readonly Dictionary<string, string> Tools = new() { ["Python"] = "python", ["JavaScript"] = "npm", ["Go"] = "go", ["Dart"] = "dart" };

    [Theory]
    [MemberData(nameof(Projects))]
    public async Task ARestoredProjectRunsWithTheLibrariesItsFileNames(string language, string[] manifest, string code, string printed, string madeHere)
    {
        if (TargetFactory.FindOnPath(Tools[language]) is null)
        {
            output.WriteLine($"Not run on this computer: no {Tools[language]}.");
            return;
        }

        File.WriteAllText(Path.Combine(_temp.Path, manifest[0]), manifest[1]);
        var program = Path.Combine(_temp.Path, ProgramNames[language]);
        File.WriteAllText(program, code);

        var plan = await LibraryRestore.PlanForAsync(program);
        if (plan is null)
        {
            output.WriteLine("Already here on this computer, so there was nothing to restore.");
            return;
        }

        output.WriteLine(plan.Question);
        var note = await LibraryRestore.DecideAsync(plan, RestoreChoice.Automatically, _ => Task.FromResult(false));
        output.WriteLine(note);

        Assert.StartsWith("Restored ", note, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_temp.Path, madeHere)), $"{madeHere} was not made");
        Assert.Null(await LibraryRestore.PlanForAsync(program));

        var launch = TargetFactory.FromFile(program, TimeSpan.FromMinutes(3));
        Assert.True(launch.Ok, launch.Problem);
        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.All.Single(each => each.Name == language), NotesFirst = [note] }.CheckAsync(launch);

        foreach (var line in report.Run?.Run?.Lines ?? []) output.WriteLine($"| {line.Text}");
        Assert.Contains(note, report.Notes);
        Assert.DoesNotContain(report.Findings, finding => finding.Severity == Severity.Error);
        Assert.Contains(report.Run!.Run!.Lines, line => line.Text.Trim() == printed);
    }
}
