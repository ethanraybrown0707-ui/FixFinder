using FixFinder.Core.Checking;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// A Python, Go or Node.js release chosen in Settings: it is the toolchain that runs the program, checks its fixes and
/// tries them - and with none of it on this computer, the program is not run with another, and how to install it is said.
/// The toolchains are made here, laid out as their installers lay them out.
/// </summary>
[Collection(SharedLanguageStandards.Name)]
public class ChosenVersionTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly LanguageStandards _before = LanguageStandards.Current;

    public void Dispose()
    {
        LanguageStandards.Current = _before;
        _temp.Dispose();
    }

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
        return path;
    }

    private string Folder(string relative) => Path.Combine(_temp.Path, relative);

    /// <summary>A Python as python.org's installer lays one out: python.exe with its python3XY.dll beside it.</summary>
    private string FakePython(string relative, int minor)
    {
        Write(Path.Combine(relative, $"python3{minor}.dll"), "");
        return Write(Path.Combine(relative, "python.exe"), "");
    }

    private IDisposable PythonComputer(string? onPath) => Pythons.LookingIn(new Pythons.Places(
        Folder("home"), Folder(@"home\AppData\Roaming"), Folder(@"home\AppData\Local"), [Folder("Program Files")],
        name => name == "python" ? onPath : null, ReadRegistry: false, Ask: _ => null));

    [Fact]
    public void OnlyAListedReleaseIsTakenFromTheSettings()
    {
        var standards = new LanguageStandards { Python = "3.11", Go = "1.22", Node = "20" };
        var edited = new LanguageStandards { Python = "3.7; del *", Go = "go1.22", Node = "19" };

        Assert.Equal("3.11", standards.PythonRelease.ToString());
        Assert.Equal("1.22", standards.GoRelease.ToString());
        Assert.Equal("20", standards.NodeRelease.ToString());
        Assert.Null(edited.PythonRelease);
        Assert.Null(edited.GoRelease);
        Assert.Null(edited.NodeRelease);
        Assert.Null(LanguageStandards.Default.PythonRelease);
    }

    [Fact]
    public void TheChoicesAreKeptBetweenSessions()
    {
        var file = Path.Combine(_temp.Path, "preferences.json");
        new Preferences { PythonVersion = "3.12", GoVersion = "1.24", NodeVersion = "22" }.Save(file);

        var standards = Preferences.Load(file).Standards;

        Assert.Equal(("3.12", "1.24", "22"), (standards.Python, standards.Go, standards.Node));
    }

    /// <summary>The Python chosen runs the program, though another is the one a terminal runs - and how it ran says the code needs a later one.</summary>
    [Fact]
    public void TheChosenPythonRunsTheProgram()
    {
        var usual = FakePython(@"Program Files\Python313", 13);
        var chosen = FakePython(@"home\AppData\Local\Programs\Python\Python311", 11);
        using var computer = PythonComputer(onPath: usual);
        LanguageStandards.Current = new LanguageStandards { Python = "3.11" };
        var app = Write(@"chosenPython\app.py", "type Point = tuple[float, float]\n");

        var setup = PythonSetup.For(app, environment: null)!;

        Assert.Equal(chosen, setup.Interpreter);
        Assert.Equal("Python 3.11 (installed for this user), as Python 3.11 is chosen in Settings", setup.Explained);
        Assert.Equal("Its code needs Python 3.12 or later: app.py uses a type statement at line 1, which Python 3.12 added - and it ran with Python 3.11.", setup.CodeNeeds);
        Assert.Equal("app.py uses a type statement at line 1, which Python 3.12 added - and it ran with Python 3.11 (installed for this user), as Python 3.11 is " +
                     "chosen in Settings, so that part of it cannot run, which is not a mistake in the code. Choosing Python 3.12 or later in Settings, or Detect " +
                     "automatically, runs it with Python 3.13 (on PATH), which is on this computer.", ToolchainVersionErrors.NoteFor(app));
    }

    /// <summary>The project's own environment is used when it is the Python chosen - and when it is not, the chosen one runs, and what that leaves out is said.</summary>
    [Fact]
    public void TheProjectsOwnEnvironmentIsUsedOnlyWhenItIsThePythonChosen()
    {
        var installed = FakePython(@"Program Files\Python312", 12);
        using var computer = PythonComputer(onPath: installed);
        var environmentPython = Write(@"envProject\.venv\Scripts\python.exe", "");
        Write(@"envProject\.venv\pyvenv.cfg", "version = 3.11.9\n");
        var app = Write(@"envProject\app.py", "print('hi')\n");
        var environment = new PythonEnvironment.Found(environmentPython, "the Python in .venv, the project's own environment");

        LanguageStandards.Current = new LanguageStandards { Python = "3.11" };
        Assert.Equal(environmentPython, PythonSetup.For(app, environment)!.Interpreter);

        LanguageStandards.Current = new LanguageStandards { Python = "3.12" };
        var other = PythonSetup.For(app, environment)!;

        Assert.Equal(installed, other.Interpreter);
        Assert.Equal("Python 3.12 (on PATH), as Python 3.12 is chosen in Settings - not the Python in .venv, the project's own environment, which is " +
                     "Python 3.11.9, so what is installed only there is not found", other.Explained);
    }

    [Fact]
    public void TheChosenGoBuildsTheProgram()
    {
        var usual = Write(@"Program Files\Go\bin\go.exe", "");
        Write(@"Program Files\Go\VERSION", "go1.27.0\n");
        var chosen = Write(@"home\sdk\go1.22.5\bin\go.exe", "");
        Write(@"home\sdk\go1.22.5\VERSION", "go1.22.5\n");
        using var computer = GoToolchains.LookingIn(new GoToolchains.Places(
            Folder("home"), [Folder("Program Files")], GoRoot: null, ModuleCache: null, FindOnPath: name => name == "go" ? usual : null, Ask: _ => null));
        LanguageStandards.Current = new LanguageStandards { Go = "1.22" };
        var main = Write(@"chosenGo\main.go", "package main\n\nfunc main() {}\n");

        var setup = GoSetup.For(main)!;

        Assert.Equal(chosen, setup.Go);
        Assert.Equal("Go 1.22.5 (golang.org/dl's), as Go 1.22 is chosen in Settings", setup.Explained);
    }

    [Fact]
    public void TheChosenNodeRunsTheProgram()
    {
        var usual = Write(@"Program Files\nodejs\node.exe", "24.19.0");
        var chosen = Write(@"nvm\versions\v20.11.0\node.exe", "20.11.0");
        Write(@"nvm\settings.txt", $"root: {Folder(@"nvm\versions")}\n");
        using var computer = Nodes.LookingIn(new Nodes.Places([Folder("Program Files")], Folder("nvm"), name => name == "node" ? usual : null, File.ReadAllText));
        LanguageStandards.Current = new LanguageStandards { Node = "20" };
        var app = Write(@"chosenNode\app.js", "console.log('hi');\n");

        var setup = NodeSetup.For(app)!;

        Assert.Equal(chosen, setup.Node);
        Assert.Equal("Node.js 20.11.0 (nvm's), as Node.js 20 is chosen in Settings", setup.Explained);
    }

    /// <summary>A release chosen that is not on this computer: the program is not run with another, and how to install it is said.</summary>
    [Fact]
    public void AChosenReleaseThatIsNotHereIsSaidAndNoOtherRunsTheProgram()
    {
        var usual = FakePython(@"Program Files\Python313", 13);
        using var pythons = PythonComputer(onPath: usual);
        using var gos = GoToolchains.LookingIn(new GoToolchains.Places(Folder("home"), [Folder("Program Files")], null, null, _ => null, _ => null));
        using var nodes = Nodes.LookingIn(new Nodes.Places([Folder("Program Files")], null, _ => null, File.ReadAllText));
        LanguageStandards.Current = new LanguageStandards { Python = "3.9", Go = "1.22", Node = "20" };

        var python = TargetFactory.FromFile(Write(@"notHere\app.py", "print('hi')\n"));
        var go = TargetFactory.FromFile(Write(@"notHere\main.go", "package main\n\nfunc main() {}\n"));
        var node = TargetFactory.FromFile(Write(@"notHere\app.js", "console.log('hi');\n"));

        Assert.Equal("app.py is Python, and Python 3.9 is chosen in Settings, which is not on this computer, so it was not run with another Python.\n\n" +
                     "Install it - for example:\n\n  winget install Python.Python.3.9\n\nOr choose Detect automatically in Settings, to run it with a Python that is here.",
                     python.Problem);
        Assert.Equal("main.go is Go, and Go 1.22 is chosen in Settings, which is not on this computer, so it was not built with another Go.\n\n" +
                     "go.dev/dl has every Go release. Or choose Detect automatically in Settings, to build it with a Go that is here.", go.Problem);
        Assert.Equal("app.js is JavaScript, and Node.js 20 is chosen in Settings, which is not on this computer, so it was not run with another Node.js.\n\n" +
                     "Install it - for example:\n\n  winget install OpenJS.NodeJS.20\n\nOr choose Detect automatically in Settings, to run it with a Node.js that is here.",
                     node.Problem);
    }
}
