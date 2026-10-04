using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Execution.Versions;

namespace FixFinder.Tests;

/// <summary>
/// Which Python a program needs and runs with: what its code uses - each part at the Python that added it, from the What's
/// New pages of docs.python.org - what its project declares, and which of the Pythons on the computer runs it. The
/// Pythons are made here, laid out as their installers lay them out, and nothing is run but the real one on PATH.
/// </summary>
public class PythonVersionTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

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
        var interpreter = Write(Path.Combine(relative, "python.exe"), "");
        Write(Path.Combine(relative, $"python3{minor}.dll"), "");
        return interpreter;
    }

    /// <summary>A computer with the given Python on PATH, the Pythons made in its folders, and no registry - looked in until disposed.</summary>
    private IDisposable Computer(string? onPath) => Pythons.LookingIn(new Pythons.Places(
        Folder("home"),
        Folder(@"home\AppData\Roaming"),
        Folder(@"home\AppData\Local"),
        [Folder("Program Files")],
        name => name == "python" ? onPath : null,
        ReadRegistry: false,
        Ask: _ => null));

    [Theory]
    [InlineData("if (n := len(items)) > 3:\n    print(n)\n", 8, "uses :=")]
    [InlineData("def clamp(value, /, low, high):\n    return max(low, min(value, high))\n", 8, "declares positional-only parameters with /")]
    [InlineData("def total(marks: list[int]) -> int:\n    return sum(marks)\n", 9, "uses a built-in collection as a generic in an annotation, such as list[int]")]
    [InlineData("name = 'Mr Smith'.removeprefix('Mr ')\n", 9, "uses str.removeprefix or removesuffix, zoneinfo, graphlib or math.lcm")]
    [InlineData("def show(command):\n    match command:\n        case 'quit':\n            return 0\n        case _:\n            return 1\n", 10, "uses a match statement")]
    [InlineData("def find(name: str) -> str | None:\n    return None\n", 10, "writes a union of types with | in an annotation")]
    [InlineData("from itertools import pairwise\nprint(list(pairwise([1, 2, 3])))\n", 10, "uses itertools.pairwise, zip(..., strict=...), int.bit_count or a dataclass with slots or kw_only")]
    [InlineData("try:\n    run()\nexcept* ValueError:\n    pass\n", 11, "uses except*")]
    [InlineData("import tomllib\n", 11, "uses tomllib, ExceptionGroup, typing.Self, asyncio.TaskGroup or datetime.UTC")]
    [InlineData("type Point = tuple[float, float]\n", 12, "uses a type statement")]
    [InlineData("def first[T](items: list[T]) -> T:\n    return items[0]\n", 12, "declares a type parameter in brackets")]
    [InlineData("from itertools import batched\n", 12, "uses itertools.batched, typing.override or calendar.Month")]
    [InlineData("import copy\nnew = copy.replace(old, x=1)\n", 13, "uses copy.replace, warnings.deprecated, math.fma, typing.ReadOnly or typing.TypeIs")]
    [InlineData("name = 'Ada'\ngreeting = t'Hello {name}'\n", 14, "uses a template string, t\"...\"")]
    public void APartOfPythonIsFoundWithThePythonThatAddedIt(string code, int minor, string uses)
    {
        var file = Write($@"features{minor}-{Guid.NewGuid():N}\app.py", code);

        var needs = PythonFeaturesUsed.Of([file]);

        Assert.NotNull(needs);
        Assert.Equal(0, needs!.Version.CompareTo(new LanguageVersion(3, minor)));
        Assert.StartsWith($"app.py {uses} at line ", needs.Because, StringComparison.Ordinal);
    }

    /// <summary>An f-string with the quote around it inside its braces - which Python 3.12 allowed - and not one with the other quote.</summary>
    [Fact]
    public void AnFStringReusingItsQuoteNeedsPython312()
    {
        var reused = Write(@"fstring\reused.py", "marks = {'ada': 90}\nprint(f\"Ada got {marks[\"ada\"]}\")\n");
        var other = Write(@"fstring\other.py", "marks = {'ada': 90}\nprint(f\"Ada got {marks['ada']}\")\nprint(f\"\"\"{marks[\"ada\"]}\"\"\")\n");

        Assert.Equal("reused.py uses the same quote inside an f-string's braces as around it at line 2, which Python 3.12 added", PythonFeaturesUsed.Of([reused])!.Because);
        Assert.Null(PythonFeaturesUsed.Of([other]));
    }

    /// <summary>What only looks like a newer part of Python is not counted: a variable called match, text, comments, and except A, B: - a mistake before 3.14.</summary>
    [Fact]
    public void WhatOnlyLooksLikeANewerPartOfPythonIsNotCounted()
    {
        var file = Write(@"quiet\app.py", """
            import re
            match = re.match(r"\d+", "42")
            type = "circle"
            # match command:  case 'quit' -> a comment, not code
            text = "x := 1 and t'hi' and except* E"
            try:
                pass
            except ValueError, TypeError:
                pass
            """);

        Assert.Null(PythonFeaturesUsed.Of([file]));
    }

    /// <summary>With from __future__ import annotations, annotations are never evaluated, so list[int] and int | None in them need no newer Python.</summary>
    [Fact]
    public void PostponedAnnotationsNeedNothingNewer()
    {
        var file = Write(@"future\app.py", "from __future__ import annotations\n\ndef find(names: list[str]) -> str | None:\n    return None\n");

        Assert.Null(PythonFeaturesUsed.Of([file]));
    }

    [Theory]
    [InlineData("pyproject.toml", "[project]\nname = \"marks\"\nrequires-python = \">=3.11\"\n", 11, null, "pyproject.toml's requires-python says >=3.11")]
    [InlineData("pyproject.toml", "[project]\nrequires-python = \">=3.10,<3.13\"\n", 10, 12, "pyproject.toml's requires-python says >=3.10,<3.13")]
    [InlineData("pyproject.toml", "[tool.poetry.dependencies]\npython = \"^3.12\"\nrequests = \"^2.31\"\n", 12, null, "pyproject.toml's Poetry dependencies say python ^3.12")]
    [InlineData(".python-version", "3.12.4\n", 12, 12, ".python-version names Python 3.12.4")]
    [InlineData("Pipfile", "[requires]\npython_version = \"3.11\"\n", 11, 11, "the Pipfile's python_version says 3.11")]
    [InlineData("setup.cfg", "[options]\npython_requires = >=3.9\n", 9, null, "setup.cfg's python_requires says >=3.9")]
    [InlineData("runtime.txt", "python-3.11.9\n", 11, 11, "runtime.txt names Python 3.11.9")]
    [InlineData("environment.yml", "name: analysis\ndependencies:\n  - python=3.10\n  - numpy\n", 10, 10, "environment.yml asks conda for python =3.10")]
    public void WhatTheProjectDeclaresIsRead(string fileName, string text, int atLeast, int? atMost, string saidBy)
    {
        var folder = $"declared{Guid.NewGuid():N}";
        Write($@"{folder}\{fileName}", text);
        var app = Write($@"{folder}\app.py", "print('hi')\n");

        var declared = DeclaredPython.Of(app);

        Assert.NotNull(declared);
        Assert.Equal(0, declared!.AtLeast!.Version.CompareTo(new LanguageVersion(3, atLeast)));
        Assert.Equal(saidBy, declared.AtLeast.Because);
        if (atMost is { } most) Assert.Equal(0, declared.AtMost!.Version.CompareTo(new LanguageVersion(3, most)));
        else Assert.Null(declared.AtMost);
    }

    /// <summary>A Python is known by its own files: python3XY.dll beside it, an environment's pyvenv.cfg, a Store alias's name.</summary>
    [Fact]
    public void APythonIsKnownByItsOwnFiles()
    {
        var installed = FakePython(@"home\AppData\Local\Programs\Python\Python312", 12);
        var environment = Write(@"project\.venv\Scripts\python.exe", "");
        Write(@"project\.venv\pyvenv.cfg", "home = C:\\Python311\nversion = 3.11.9\n");

        Assert.Equal("3.12", Pythons.VersionFromFiles(installed));
        Assert.Equal("3.11.9", Pythons.VersionFromFiles(environment));
        Assert.Equal("3.13", Pythons.VersionFromFiles(@"C:\Users\someone\AppData\Local\Microsoft\WindowsApps\python3.13.exe"));
    }

    [Fact]
    public void EveryPythonInTheUsualPlacesIsFoundNewestFirst()
    {
        var usual = FakePython(@"Program Files\Python311", 11);
        FakePython(@"home\AppData\Local\Programs\Python\Python313", 13);
        FakePython(@"home\.pyenv\pyenv-win\versions\3.12.4", 12);
        using var computer = Computer(onPath: usual);

        var found = Pythons.Installed;

        Assert.Equal(["3.13", "3.12", "3.11"], found.Select(python => python.VersionText));
        Assert.Equal(["installed for this user", "pyenv's", "on PATH"], found.Select(python => python.FoundIn));
    }

    /// <summary>The usual Python runs a program it is new enough for - and how it ran still says what the code needs.</summary>
    [Fact]
    public void TheUsualPythonRunsCodeItIsNewEnoughFor()
    {
        var usual = FakePython(@"Program Files\Python313", 13);
        using var computer = Computer(onPath: usual);
        var app = Write(@"usual\app.py", "def show(command):\n    match command:\n        case 'quit':\n            return 0\n");

        var setup = PythonSetup.For(app, environment: null)!;

        Assert.Equal(usual, setup.Interpreter);
        Assert.Equal("Python 3.13 (on PATH)", setup.Explained);
        Assert.Equal("Its code needs Python 3.10 or later: app.py uses a match statement at line 2, which Python 3.10 added.", setup.CodeNeeds);
    }

    /// <summary>A type statement is Python 3.12's: with the usual Python of 3.11, the 3.12 installed for this user runs it.</summary>
    [Fact]
    public void ANewerPythonRunsCodeTheUsualOneIsTooOldFor()
    {
        var usual = FakePython(@"Program Files\Python311", 11);
        var newer = FakePython(@"home\AppData\Local\Programs\Python\Python312", 12);
        FakePython(@"home\AppData\Local\Programs\Python\Python313", 13);
        using var computer = Computer(onPath: usual);
        var app = Write(@"newer\app.py", "type Point = tuple[float, float]\n");

        var setup = PythonSetup.For(app, environment: null)!;

        Assert.Equal(newer, setup.Interpreter);
        Assert.Equal("Python 3.12 (installed for this user), as app.py uses a type statement at line 1, which Python 3.12 added", setup.Explained);
        Assert.Null(setup.CodeNeeds);
    }

    /// <summary>A Python the project rules out is not chosen, even when it is the usual one: requires-python below 3.13 keeps 3.13 out.</summary>
    [Fact]
    public void WhatTheProjectDeclaresDecidesAmongThePythons()
    {
        var usual = FakePython(@"Program Files\Python313", 13);
        var allowed = FakePython(@"home\AppData\Local\Programs\Python\Python312", 12);
        using var computer = Computer(onPath: usual);
        Write(@"declaredChoice\pyproject.toml", "[project]\nrequires-python = \">=3.10,<3.13\"\n");
        var app = Write(@"declaredChoice\app.py", "print('hi')\n");

        var setup = PythonSetup.For(app, environment: null)!;

        Assert.Equal(allowed, setup.Interpreter);
        Assert.Equal("Python 3.12 (installed for this user), as pyproject.toml's requires-python says >=3.10,<3.13", setup.Explained);
    }

    [Fact]
    public void WithNoPythonNewEnoughItSaysSoAndTheNoteSaysWhatToInstall()
    {
        var usual = FakePython(@"Program Files\Python311", 11);
        using var computer = Computer(onPath: usual);
        var app = Write(@"tooOld\app.py", "type Point = tuple[float, float]\n");

        var setup = PythonSetup.For(app, environment: null)!;

        Assert.Equal(usual, setup.Interpreter);
        Assert.Equal("Python 3.11 (on PATH), though app.py uses a type statement at line 1, which Python 3.12 added, and no Python of 3.12 or later is on this computer", setup.Explained);
        Assert.Equal("app.py uses a type statement at line 1, which Python 3.12 added - and Python 3.11 (on PATH) is the newest Python on this computer, so that " +
                     "part of it cannot run, which is not a mistake in the code. Installing Python 3.12 or later runs it - for example:\n  winget install Python.Python.3.14",
                     ToolchainVersionErrors.NoteFor(app));
    }

    /// <summary>
    /// A need some Python here meets, but none together with the rest - the code needs 3.12 and the project rules out
    /// anything after 3.11 - is said as that, not as there being no such Python.
    /// </summary>
    [Fact]
    public void NeedsNoOnePythonMeetsTogetherAreSaidAsThat()
    {
        var usual = FakePython(@"Program Files\Python311", 11);
        FakePython(@"home\AppData\Local\Programs\Python\Python313", 13);
        using var computer = Computer(onPath: usual);
        Write(@"conflict\pyproject.toml", "[project]\nrequires-python = \"<3.12\"\n");
        var app = Write(@"conflict\app.py", "type Point = tuple[float, float]\n");

        Assert.Equal("Python 3.11 (on PATH), though app.py uses a type statement at line 1, which Python 3.12 added, and no Python on this computer meets " +
                     "that and the rest of what it needs", PythonSetup.For(app, environment: null)!.Explained);
    }

    /// <summary>The project's own environment is used as it is - it holds the project's packages - and how it ran says what the code needs of it.</summary>
    [Fact]
    public void TheProjectsOwnEnvironmentIsUsedAsItIs()
    {
        var environment = Write(@"ownEnv\.venv\Scripts\python.exe", "");
        Write(@"ownEnv\.venv\pyvenv.cfg", "version = 3.9.13\n");
        var app = Write(@"ownEnv\app.py", "import tomllib\n");

        var setup = PythonSetup.For(app, new PythonEnvironment.Found(environment, "the Python in .venv, the project's own environment"))!;

        Assert.Equal(environment, setup.Interpreter);
        Assert.Equal("the Python in .venv, the project's own environment", setup.Explained);
        Assert.Equal("Its code needs Python 3.11 or later: app.py uses tomllib, ExceptionGroup, typing.Self, asyncio.TaskGroup or datetime.UTC at line 1, " +
                     "which Python 3.11 added - and it ran with Python 3.9.13.", setup.CodeNeeds);
    }
}

/// <summary>Programs run with the Python on this computer, as the window runs them, saying the Python their code needs.</summary>
public class PythonVersionLiveTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void HowItRanSaysThePythonTheCodeNeeds()
    {
        if (Pythons.Usual is not { } usual || usual.Version < new LanguageVersion(3, 10)) return;

        var app = Path.Combine(_temp.Path, "app.py");
        File.WriteAllText(app, "def show(command):\n    match command:\n        case 'quit':\n            return 0\n        case _:\n            return 1\n\nprint(show('quit'))\n");

        var plan = TargetFactory.FromFile(app);

        Assert.True(plan.Ok, plan.Problem);
        Assert.Equal($"Running it with Python {usual.VersionText} (on PATH). Its code needs Python 3.10 or later: app.py uses a match statement at line 2, which Python 3.10 added.", plan.Explanation);
    }
}
