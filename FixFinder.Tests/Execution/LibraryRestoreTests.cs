using FixFinder.Core.Engine;
using FixFinder.Core.Execution.Libraries;

namespace FixFinder.Tests;

/// <summary>
/// Restoring the libraries a project names, without downloading anything: which names each file gives, how Go's module
/// cache writes a path, what is said before and after - and that restoring is never on unless chosen.
/// </summary>
public class LibraryRestoreTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>
    /// A requirement's name, before its version, its extras and its comment. Options, includes, links and paths are left to
    /// pip; so is a line with a marker, which may not be meant for this Python.
    /// </summary>
    [Fact]
    public void RequirementsTxtGivesTheNamesItRequires()
    {
        string[] lines =
        [
            "# for the marks report", "tabulate==0.9.0", "requests[socks] >= 2.31  # web", "Flask", "", "-r more.txt", "-e .",
            "--index-url https://example.org/simple", "git+https://github.com/example/tool.git", "./local_package",
            "colorama; sys_platform == 'win32'", "tabulate>=0.8",
        ];

        Assert.Equal(["tabulate", "requests", "Flask"], LibraryRestore.RequirementsIn(lines));
    }

    [Fact]
    public void PackageJsonGivesItsDependenciesAndDevDependencies() =>
        Assert.Equal(["ms", "@types/node", "typescript"],
            LibraryRestore.PackagesIn("""{ "name": "marks", "scripts": { "start": "node app.js" }, "dependencies": { "ms": "2.1.3" }, "devDependencies": { "@types/node": "^24", "typescript": "7.0.2" } }"""));

    /// <summary>Each required module with its version - in a block or on a line of its own - less those replaced, with a folder or another module.</summary>
    [Fact]
    public void GoModGivesTheModulesItRequiresLessThoseItReplaces()
    {
        string[] goMod =
        [
            "module example.com/marks", "", "go 1.22", "", "require (", "\tgithub.com/google/uuid v1.6.0", "\tgolang.org/x/text v0.14.0 // indirect",
            "\texample.com/shared v0.0.0", ")", "", "require github.com/google/go-cmp v0.6.0", "", "replace example.com/shared => ../shared",
        ];

        Assert.Equal([("github.com/google/uuid", "v1.6.0"), ("golang.org/x/text", "v0.14.0"), ("github.com/google/go-cmp", "v0.6.0")], LibraryRestore.RequiredIn(goMod));
    }

    /// <summary>Go's module cache writes each capital letter as ! and the small letter, so paths differing only in case do not meet on Windows.</summary>
    [Fact]
    public void AModulePathIsWrittenAsGosModuleCacheWritesIt() =>
        Assert.Equal("github.com/!burnt!sushi/toml", LibraryRestore.Escaped("github.com/BurntSushi/toml"));

    [Fact]
    public void PubspecGivesTheDependenciesAndDevDependenciesItNames()
    {
        string[] pubspec = ["name: marks", "environment:", "  sdk: ^3.0.0", "dependencies:", "  path: ^1.9.0", "  # a comment", "  http:", "    version: ^1.2.0", "dev_dependencies:", "  test: ^1.25.0"];

        Assert.Equal(["path", "http", "test"], LibraryRestore.DartPackagesIn(pubspec));
    }

    /// <summary>Packages already in node_modules are not restored again, and a file with no project around it has nothing to restore.</summary>
    [Fact]
    public async Task NothingIsPlannedWhenEveryLibraryIsHereOrNoneIsNamed()
    {
        var app = Write(@"here\app.js", "console.log(require('ms')(60000));\n");
        Write(@"here\package.json", """{ "dependencies": { "ms": "2.1.3" } }""");
        Write(@"here\node_modules\ms\package.json", """{ "name": "ms", "version": "2.1.3" }""");

        Assert.Null(await LibraryRestore.PlanForAsync(app));
        Assert.Null(await LibraryRestore.PlanForAsync(Write(@"alone\marks.py", "print(1)\n")));
        Assert.Null(await LibraryRestore.PlanForAsync(Write(@"replaced\main.go", "package main\n")));
    }

    /// <summary>What is asked names what is missing, the tool, where it downloads from, where it puts it, the commands - and the risk.</summary>
    [Fact]
    public void TheQuestionSaysWhatWouldBeDownloadedFromWhereAndTheRisk()
    {
        var plan = new RestorePlan(@"C:\work\app.py", @"C:\work\requirements.txt", ["tabulate", "requests"], "pip", "PyPI (pypi.org)",
            "the Python in .venv, the project's own environment", [new RestoreStep("python.exe", [], "python -m pip install -r requirements.txt")]);

        Assert.Equal("requirements.txt names 2 libraries that are not on this computer: tabulate, requests.\n\nFixFinder can restore them: pip downloads them " +
                     "from PyPI (pypi.org) and installs them into the Python in .venv, the project's own environment, by running:\n\npython -m pip install -r " +
                     $"requirements.txt\n\n{LibraryRestore.ScriptsWarning}\n\nRestore them now?", plan.Question);
        Assert.Equal("tabulate and requests", plan.MissingSaid);
        Assert.Equal("requirements.txt names 2 libraries that are not on this computer: tabulate, requests. FixFinder downloads nothing unless you let it: " +
                     "restoring libraries is off - Settings, Advanced, Libraries - so the program was checked without them.", plan.OffNote);
    }

    /// <summary>Restoring is off unless chosen, a choice is kept, and one this version has no name for is read as off.</summary>
    [Fact]
    public void RestoringIsNeverOnUnlessChosen()
    {
        var file = Path.Combine(_temp.Path, "preferences.json");

        Assert.Equal(RestoreChoice.Never, new Preferences().RestoreLibraries);
        Assert.True(new Preferences { RestoreLibraries = RestoreChoice.Ask }.Save(file));
        Assert.Equal(RestoreChoice.Ask, Preferences.Load(file).RestoreLibraries);

        File.WriteAllText(file, """{ "Appearance": 2, "RestoreLibraries": 7 }""");
        Assert.Equal(RestoreChoice.Never, Preferences.Load(file).RestoreLibraries);
    }

    /// <summary>When restoring is off, the plan is not asked about and nothing is run: the note says what is missing.</summary>
    [Fact]
    public async Task WhenRestoringIsOffNothingIsAskedOrRun()
    {
        var plan = new RestorePlan(@"C:\work\app.js", @"C:\work\package.json", ["ms"], "npm", "the npm registry (registry.npmjs.org)", "node_modules beside package.json",
            [new RestoreStep(@"C:\nowhere\node.exe", [], "npm install")]);
        var asked = false;

        var note = await LibraryRestore.DecideAsync(plan, RestoreChoice.Never, _ => { asked = true; return Task.FromResult(true); });

        Assert.False(asked);
        Assert.Equal(plan.OffNote, note);
        Assert.Equal(plan.DeclinedNote, await LibraryRestore.DecideAsync(plan, RestoreChoice.Ask, _ => Task.FromResult(false)));
    }
}
