using System.Diagnostics;
using System.Text.Json;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Covers checking a Python program as the project it belongs to is set up: run with the project's own virtual
/// environment, so what is installed only there is found as its IDE finds it.
/// </summary>
public class PythonProjectTests(ITestOutputHelper output) : IDisposable
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

    private string Folder(string relative) => Path.GetFullPath(Path.Combine(_temp.Path, relative));

    private async Task<CheckReport> CheckAsync(string file, string? expected = null, TimeSpan? timeLimit = null)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);
        if (timeLimit is { } limit) launch = launch with { Spec = launch.Spec!.WithTimeout(limit) };
        output.WriteLine($"how: {launch.Explanation}");

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry())
        {
            Language = CodeLanguage.Python,
            Expected = expected is null ? null : ExpectedBehaviour.From([new ExpectedRun(null, expected)], ""),
        };

        var report = await checker.CheckAsync(launch);
        output.WriteLine($"syntax: {report.SyntaxSummary} | logic: {report.LogicSummary}");
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings) output.WriteLine($"[{finding.Severity}/{finding.Confidence}] {finding.Location} {finding.RuleId}: {finding.Title}");
        return report;
    }

    /// <summary>
    /// A virtual environment made the way PyCharm and VS Code make one, with nothing downloaded: python -m venv without
    /// pip. False when there is no Python here to make it with.
    /// </summary>
    private bool MadeEnvironment(string folder)
    {
        if (!LocalFixLiveTests.Available("python") || TargetFactory.FindOnPath("python") is not { } python) return false;

        using var making = Process.Start(new ProcessStartInfo(python, $"-m venv --without-pip \"{folder}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        making.StandardOutput.ReadToEnd();
        making.StandardError.ReadToEnd();
        making.WaitForExit(120_000);

        return File.Exists(Path.Combine(folder, "Scripts", "python.exe")) || File.Exists(Path.Combine(folder, "bin", "python"));
    }

    /// <summary>A module installed into an environment only, as pip would put it there.</summary>
    private static void InstallInto(string environment, string module, string source)
    {
        var sitePackages = OperatingSystem.IsWindows()
            ? Path.Combine(environment, "Lib", "site-packages")
            : Directory.EnumerateDirectories(Path.Combine(environment, "lib")).Select(version => Path.Combine(version, "site-packages")).First();

        Directory.CreateDirectory(Path.Combine(sitePackages, module));
        File.WriteAllText(Path.Combine(sitePackages, module, "__init__.py"), source.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task AProgramRunsWithItsProjectsOwnEnvironmentAndWhatIsInstalledThere()
    {
        var environment = Folder(@"coursework\.venv");
        if (!MadeEnvironment(environment)) return;

        InstallInto(environment, "gradetools", "def classify(mark):\n    return \"pass\" if mark >= 40 else \"fail\"\n");
        var program = Write(@"coursework\grades.py", "from gradetools import classify\n\nfor mark in (72, 55, 38):\n    print(mark, classify(mark))\n");

        Assert.Contains("the Python in .venv, the project's own environment", TargetFactory.FromFile(program).Explanation, StringComparison.Ordinal);

        var report = await CheckAsync(program, "72 pass\n55 pass\n38 fail");

        Assert.Empty(report.Findings);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    [Fact]
    public async Task AChangeIsTriedOnACopyOfTheProgramWithTheProjectsOwnEnvironmentToo()
    {
        var environment = Folder(@"tried\.venv");
        if (!MadeEnvironment(environment)) return;

        InstallInto(environment, "gradetools", "def classify(mark):\n    return \"pass\" if mark >= 40 else \"fail\"\n");
        var program = Write(@"tried\grades.py", """
            from gradetools import classify


            def average(values):
                return sum(values) / len(values)


            for mark in (72, 55, 38):
                print(mark, classify(mark))
            print(average([]))
            """);

        var report = await CheckAsync(program);

        // The copy a fix is tried on is outside the project, where its environment is not, and it runs with it all the same:
        // with the Python on PATH, gradetools would not be found and the copy would stop before it reached the fixed line.
        var byZero = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.StartsWith("It crashed: ZeroDivisionError", byZero.Title, StringComparison.Ordinal);
        Assert.True(byZero.Verified.IsVerified, string.Join(" / ", byZero.Verified.Steps.Select(step => step.Detail)));
    }

    [Fact]
    public async Task AMisspeltBuiltInIsPutRightAsPythonSuggestsAndTheChangeIsTriedOnACopy()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var program = Write(@"typo\totals.py", "marks = [40, 2]\ntotal = sum(marks)\nprnt(total)\n");

        var report = await CheckAsync(program);

        var misspelt = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal("Change prnt to print. Python itself compared `prnt` with the names it knew at this point, and suggested `print`.", misspelt.SuggestedFix);
        Assert.Equal(["prnt(total)", "print(total)"], misspelt.Change!.Lines.Where(line => line.Kind != ChangeKind.Context).Select(line => line.Text));
        Assert.True(misspelt.CameFrom!.IsTheLanguagesOwn);
        Assert.True(misspelt.Verified.IsVerified, string.Join(" / ", misspelt.Verified.Steps.Select(step => step.Detail)));
    }

    /// <summary>
    /// A stand-in for a computer's own folders - its home, its two application data folders and its environment variables -
    /// laid out in the test's temp folder, so a conda, Poetry or PyCharm can be set up as each lays itself out.
    /// </summary>
    private PythonEnvironment.Places Computer(Dictionary<string, string>? variables = null)
    {
        var home = Folder("computer");
        return new PythonEnvironment.Places(home, Path.Combine(home, "AppData", "Roaming"), Path.Combine(home, "AppData", "Local"),
            name => variables?.GetValueOrDefault(name));
    }

    /// <summary>A stand-in interpreter where a tool keeps one - a file to be found, not one to run.</summary>
    private string Interpreter(string relative) => Write(relative, "");

    /// <summary>A virtual environment's own setting file, as a tool that makes one writes it, with no Python named to check for.</summary>
    private void VirtualEnvironment(string relative) => Write(Path.Combine(relative, "pyvenv.cfg"), "include-system-site-packages = false\n");

    [Fact]
    public void ACondaEnvironmentAProjectNamesIsFoundInTheListCondaKeeps()
    {
        var python = Interpreter(@"elsewhere\conda\envs\coursework\python.exe");
        Directory.CreateDirectory(Folder(@"elsewhere\conda\envs\coursework\conda-meta"));
        Write(@"computer\.conda\environments.txt", Folder(@"elsewhere\conda\envs\coursework") + "\n");
        Write(@"computer\work\analysis\environment.yml", "name: coursework\nchannels:\n  - conda-forge\ndependencies:\n  - numpy\n");
        var program = Write(@"computer\work\analysis\clean.py", "import numpy\n");

        using (PythonEnvironment.LookingIn(Computer()))
        {
            var found = PythonEnvironment.For(program);

            Assert.Equal(python, found?.Interpreter, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("the Python in the conda environment coursework, which environment.yml names", found!.Described);
        }
    }

    [Fact]
    public void ACondaEnvironmentIsFoundInAnInstallationsEnvironmentsAndByThePrefixAnExportWrites()
    {
        var inInstallation = Interpreter(@"computer\miniconda3\envs\stats\python.exe");
        Directory.CreateDirectory(Folder(@"computer\miniconda3\envs\stats\conda-meta"));
        Write(@"computer\work\stats\environment.yml", "name: stats\ndependencies:\n  - pandas\n");
        var byName = Write(@"computer\work\stats\summary.py", "import pandas\n");

        var exported = Interpreter(@"elsewhere\exported\python.exe");
        Directory.CreateDirectory(Folder(@"elsewhere\exported\conda-meta"));
        Write(@"computer\work\exported\environment.yml", $"name: somewhere-else\ndependencies:\n  - pandas\nprefix: {Folder(@"elsewhere\exported")}\n");
        var byPrefix = Write(@"computer\work\exported\report.py", "import pandas\n");

        var missing = Write(@"computer\work\missing\report.py", "import pandas\n");
        Write(@"computer\work\missing\environment.yml", "name: never-made\n");

        // What conda could not delete when it removed an environment - a python.exe still in use - without the record of
        // what was installed that makes the folder an environment.
        Interpreter(@"computer\miniconda3\envs\removed\python.exe");
        var removed = Write(@"computer\work\removed\report.py", "import pandas\n");
        Write(@"computer\work\removed\environment.yml", "name: removed\n");

        using (PythonEnvironment.LookingIn(Computer()))
        {
            Assert.Equal(inInstallation, PythonEnvironment.For(byName)?.Interpreter, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(exported, PythonEnvironment.For(byPrefix)?.Interpreter, StringComparer.OrdinalIgnoreCase);

            // An environment the project names that is not on this computer is not guessed at: the Python on PATH is used.
            Assert.Null(PythonEnvironment.For(missing));
            Assert.Null(PythonEnvironment.For(removed));
        }
    }

    /// <summary>
    /// The name Poetry gives a project's environment, worked out by Python running Poetry's own generate_env_name, as its
    /// source has it - so the test does not take FixFinder's reading of it on trust. Null when there is no Python here.
    /// </summary>
    private static string? PoetryNameByPython(string projectName, string projectFolder)
    {
        if (TargetFactory.FindOnPath("python") is not { } python) return null;

        const string generateEnvName = """
            import base64, hashlib, os, re, sys
            name = re.sub(r"[-_.]+", "-", sys.argv[1]).lower()
            name = name.lower()
            sanitized_name = re.sub(r'[ $`!*@"\\\r\n\t]', "_", name)[:42]
            normalized_cwd = os.path.normcase(os.path.realpath(sys.argv[2]))
            h_bytes = hashlib.sha256(normalized_cwd.encode()).digest()
            h_str = base64.urlsafe_b64encode(h_bytes).decode()[:8]
            print(f"{sanitized_name}-{h_str}")
            """;

        var script = Path.Combine(Path.GetTempPath(), $"poetry-name-{Guid.NewGuid():N}.py");
        File.WriteAllText(script, generateEnvName);

        try
        {
            using var naming = Process.Start(new ProcessStartInfo(python, $"\"{script}\" \"{projectName}\" \"{projectFolder}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var name = naming.StandardOutput.ReadToEnd().Trim();
            naming.StandardError.ReadToEnd();
            naming.WaitForExit(60_000);
            return name.Length > 0 ? name : null;
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public void ThePoetryEnvironmentIsFoundUnderTheNamePoetryGivesIt()
    {
        var project = Folder(@"computer\work\Grade Book");
        Write(@"computer\work\Grade Book\pyproject.toml", "[tool.poetry]\nname = \"Grade_Book\"\nversion = \"0.1.0\"\n");
        var program = Write(@"computer\work\Grade Book\grades.py", "import requests\n");
        if (PoetryNameByPython("Grade_Book", project) is not { } poetrysName) return;

        Assert.Equal(poetrysName, PoetryEnvironments.EnvironmentName("Grade_Book", project));

        // The name as Poetry works it out for any project name: runs of - _ . made one -, what a folder name cannot hold made
        // _, and cut at 42 characters.
        foreach (var projectName in new[] { "Stats..Tools", "odd name!", "Module-Marks-For-The-Second-Year-Of-Study-In-Full" })
            Assert.Equal(PoetryNameByPython(projectName, project), PoetryEnvironments.EnvironmentName(projectName, project));

        var environment = $@"computer\AppData\Local\pypoetry\Cache\virtualenvs\{poetrysName}-py3.12";
        VirtualEnvironment(environment);
        var python = Interpreter($@"{environment}\Scripts\python.exe");

        using (PythonEnvironment.LookingIn(Computer()))
        {
            var found = PythonEnvironment.For(program);

            Assert.Equal(python, found?.Interpreter, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("the Python in the environment Poetry made for this project", found!.Described);
        }
    }

    [Fact]
    public void PoetrysOwnRecordDecidesBetweenEnvironmentsForTwoPythonsAndItsSettingsDecideWhereTheyAre()
    {
        var project = Folder(@"computer\work\marks");
        Write(@"computer\work\marks\pyproject.toml", "[project]\nname = \"marks\"\n");
        Write(@"computer\work\marks\poetry.lock", "");
        var program = Write(@"computer\work\marks\marks.py", "print(1)\n");
        var name = PoetryEnvironments.EnvironmentName("marks", project);

        // Kept where Poetry's settings put its environments, one for each of two Pythons, envs.toml recording which is chosen.
        var kept = Folder(@"elsewhere\poetry-environments");
        Write(@"computer\AppData\Roaming\pypoetry\config.toml", $"[virtualenvs]\npath = '{kept}'\n");
        VirtualEnvironment($@"elsewhere\poetry-environments\{name}-py3.11");
        VirtualEnvironment($@"elsewhere\poetry-environments\{name}-py3.12");
        var chosen = Interpreter($@"elsewhere\poetry-environments\{name}-py3.11\Scripts\python.exe");
        Interpreter($@"elsewhere\poetry-environments\{name}-py3.12\Scripts\python.exe");

        using (PythonEnvironment.LookingIn(Computer()))
        {
            // Two, and no record of which: neither is taken, rather than one guessed at.
            Assert.Null(PythonEnvironment.For(program));

            Write(@"elsewhere\poetry-environments\envs.toml", $"[{name}]\nminor = \"3.11\"\npatch = \"3.11.9\"\n");

            Assert.Equal(chosen, PythonEnvironment.For(program)?.Interpreter, StringComparer.OrdinalIgnoreCase);
        }

        // Poetry reads a setting from its POETRY_ variable first, then from the project's own poetry.toml, then from its
        // settings file - and keeps environments only in the folder the setting names, not in its cache as well.
        VirtualEnvironment($@"computer\AppData\Local\pypoetry\Cache\virtualenvs\{name}-py3.13");
        Interpreter($@"computer\AppData\Local\pypoetry\Cache\virtualenvs\{name}-py3.13\Scripts\python.exe");

        var projectsFolder = Folder(@"elsewhere\project-environments");
        Write(@"computer\work\marks\poetry.toml", $"[virtualenvs]\npath = '{projectsFolder}'\n");
        VirtualEnvironment($@"elsewhere\project-environments\{name}-py3.13");
        var projectsOwn = Interpreter($@"elsewhere\project-environments\{name}-py3.13\Scripts\python.exe");

        var variablesFolder = Folder(@"elsewhere\variable-environments");
        VirtualEnvironment($@"elsewhere\variable-environments\{name}-py3.13");
        var variablesOwn = Interpreter($@"elsewhere\variable-environments\{name}-py3.13\Scripts\python.exe");

        using (PythonEnvironment.LookingIn(Computer()))
        {
            Assert.Equal(projectsOwn, PythonEnvironment.For(program)?.Interpreter, StringComparer.OrdinalIgnoreCase);
        }

        using (PythonEnvironment.LookingIn(Computer(new() { ["POETRY_VIRTUALENVS_PATH"] = variablesFolder })))
        {
            Assert.Equal(variablesOwn, PythonEnvironment.For(program)?.Interpreter, StringComparer.OrdinalIgnoreCase);
        }

        // Named, but empty of this project's environment: the one in the cache is not what Poetry would run.
        using (PythonEnvironment.LookingIn(Computer(new() { ["POETRY_VIRTUALENVS_PATH"] = Folder(@"elsewhere\empty") })))
        {
            Assert.Null(PythonEnvironment.For(program));
        }
    }

    [Fact]
    public void ThePipenvEnvironmentIsTheOneWhoseProjectFileNamesThisProject()
    {
        var project = Folder(@"computer\work\shop");
        Write(@"computer\work\shop\Pipfile", "[packages]\nrequests = \"*\"\n");
        var program = Write(@"computer\work\shop\shop.py", "import requests\n");

        // Another project's of the same name, listed first.
        VirtualEnvironment(@"computer\.virtualenvs\shop-Ab12Cd34");
        Write(@"computer\.virtualenvs\shop-Ab12Cd34\.project", Folder(@"computer\work\other-shop"));
        Interpreter(@"computer\.virtualenvs\shop-Ab12Cd34\Scripts\python.exe");

        VirtualEnvironment(@"computer\.virtualenvs\shop-Xy56Ef78");
        Write(@"computer\.virtualenvs\shop-Xy56Ef78\.project", project);
        var python = Interpreter(@"computer\.virtualenvs\shop-Xy56Ef78\Scripts\python.exe");

        using (PythonEnvironment.LookingIn(Computer()))
        {
            Assert.Equal(python, PythonEnvironment.For(program)?.Interpreter, StringComparer.OrdinalIgnoreCase);
        }

        // Kept where WORKON_HOME says instead, ~ standing for the home folder as it does for Pipenv.
        VirtualEnvironment(@"computer\envs\shop-Gh90Ij12");
        Write(@"computer\envs\shop-Gh90Ij12\.project", project);
        var named = Interpreter(@"computer\envs\shop-Gh90Ij12\Scripts\python.exe");

        using (PythonEnvironment.LookingIn(Computer(new() { ["WORKON_HOME"] = @"~\envs" })))
        {
            Assert.Equal(named, PythonEnvironment.For(program)?.Interpreter, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheInterpreterAPyCharmProjectIsSetToUseIsFoundInPyCharmsList()
    {
        Write(@"computer\work\library\.idea\misc.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <project version="4">
              <component name="Black"><option name="sdkName" value="Python 3.12 (library)" /></component>
              <component name="ProjectRootManager" version="2" project-jdk-name="Python 3.12 (library)" project-jdk-type="Python SDK" />
            </project>
            """);
        var program = Write(@"computer\work\library\books.py", "print('books')\n");
        var python = Interpreter(@"computer\PycharmProjects\venvs\library\Scripts\python.exe");
        Write(@"computer\AppData\Roaming\JetBrains\PyCharmCE2024.3\options\jdk.table.xml", """
            <application>
              <component name="ProjectJdkTable">
                <jdk version="2">
                  <name value="Python 3.11" />
                  <type value="Python SDK" />
                  <homePath value="C:\Python311\python.exe" />
                </jdk>
                <jdk version="2">
                  <name value="Python 3.12 (library)" />
                  <type value="Python SDK" />
                  <homePath value="$USER_HOME$/PycharmProjects/venvs/library/Scripts/python.exe" />
                </jdk>
              </component>
            </application>
            """);

        using (PythonEnvironment.LookingIn(Computer()))
        {
            var found = PythonEnvironment.For(program);

            Assert.Equal(python, found?.Interpreter, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("the Python this project's PyCharm settings name, Python 3.12 (library)", found!.Described);
        }
    }

    /// <summary>A notebook of one cell, saved with the kernel of that name - as Jupyter records the kernel it was last run with.</summary>
    private string NotebookWithKernel(string relative, string kernelName) => Write(relative, JsonSerializer.Serialize(new
    {
        cells = new[] { new { cell_type = "code", metadata = new { }, source = new[] { "print(1)" }, outputs = Array.Empty<object>(), execution_count = (int?)null } },
        metadata = new { kernelspec = new { name = kernelName, display_name = "Python", language = "python" } },
        nbformat = 4,
        nbformat_minor = 5,
    }));

    /// <summary>A kernel's kernel.json in that kernels folder, as ipykernel install writes one: its Python first in argv.</summary>
    private void Kernel(string kernelsFolder, string kernelName, string python, string displayName) =>
        Write(Path.Combine(kernelsFolder, kernelName, "kernel.json"), JsonSerializer.Serialize(new
        {
            argv = new[] { python, "-m", "ipykernel_launcher", "-f", "{connection_file}" },
            display_name = displayName,
            language = "python",
        }));

    [Fact]
    public void ANotebookSavedWithAKernelOfItsOwnRunsWithThatKernelsPython()
    {
        var python = Interpreter(@"elsewhere\envs\ml\python.exe");
        Kernel(@"computer\AppData\Roaming\jupyter\kernels", "ml", python, "Python (ml)");

        var ownKernel = NotebookWithKernel(@"computer\work\ml\train.ipynb", "ml");
        var anyPython = NotebookWithKernel(@"computer\work\plain\explore.ipynb", "python3");
        Write(@"computer\AppData\Roaming\jupyter\kernels\python3\kernel.json", JsonSerializer.Serialize(new { argv = new[] { python, "-m", "ipykernel_launcher" } }));

        using (PythonEnvironment.LookingIn(Computer()))
        {
            var launch = TargetFactory.FromFile(ownKernel);
            Assert.Equal(python, launch.Spec?.ExecutablePath, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("with the Python of the notebook's Jupyter kernel, Python (ml)", launch.Explanation, StringComparison.Ordinal);

            // python3 is what every Python's own kernel is called, so it says nothing about which one this notebook needs.
            Assert.NotEqual(python, TargetFactory.FromFile(anyPython).Spec?.ExecutablePath, StringComparer.OrdinalIgnoreCase);

            // A copy of the notebook's code, made to try a change in, runs with the kernel of the notebook it was copied from.
            var copies = Folder(@"copies\ml");
            var copy = Write(@"copies\ml\train.ipynb.py", "print(1)\n");
            ProgramCopy.Remember(copies, Path.GetDirectoryName(ownKernel)!);

            try
            {
                Assert.Equal(python, TargetFactory.FromFile(copy).Spec?.ExecutablePath, StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                ProgramCopy.Forget(copies);
            }
        }
    }

    [Fact]
    public void AKernelIsLookedForWhereJupyterItselfLooksForOne()
    {
        var python = Interpreter(@"elsewhere\envs\stats\python.exe");
        var everyones = Folder("programdata");
        var ownData = Folder(@"elsewhere\jupyter-data");

        // In ProgramData, which anyone can write to: Jupyter takes a kernel from there only when told to trust it.
        Kernel(@"programdata\jupyter\kernels", "shared", python, "Shared");
        Assert.Null(JupyterKernels.Named("shared", Computer(new() { ["ProgramData"] = everyones })));
        Assert.Null(JupyterKernels.Named("shared", Computer(new() { ["ProgramData"] = everyones, ["JUPYTER_USE_PROGRAMDATA"] = "0" })));
        Assert.Equal(python, JupyterKernels.Named("shared", Computer(new() { ["ProgramData"] = everyones, ["JUPYTER_USE_PROGRAMDATA"] = "1" }))?.Interpreter);

        // JUPYTER_DATA_DIR is looked in in place of this user's Jupyter folder, not as well as it.
        Kernel(@"elsewhere\jupyter-data\kernels", "moved", python, "Moved");
        Kernel(@"computer\AppData\Roaming\jupyter\kernels", "left-behind", python, "Left behind");
        var moved = Computer(new() { ["JUPYTER_DATA_DIR"] = ownData });

        Assert.Equal(python, JupyterKernels.Named("moved", moved)?.Interpreter);
        Assert.Null(JupyterKernels.Named("left-behind", moved));
        Assert.Equal(python, JupyterKernels.Named("left-behind", Computer())?.Interpreter);
    }

    [Fact]
    public void WhatAToolRunByTheStoresPythonKeptInThatPythonsCopyOfAppDataIsFoundThere()
    {
        // The Microsoft Store's Python gives the tools it runs a copy of the application data folders of its own, so what
        // Poetry and Jupyter write to them lands in that copy.
        const string storesCopy = @"computer\AppData\Local\Packages\PythonSoftwareFoundation.Python.3.13_qbz5n2kfra8p0\LocalCache";

        var project = Folder(@"computer\work\poems");
        Write(@"computer\work\poems\pyproject.toml", "[tool.poetry]\nname = \"poems\"\n");
        var program = Write(@"computer\work\poems\poems.py", "print('poems')\n");
        var environment = $@"{storesCopy}\Local\pypoetry\Cache\virtualenvs\{PoetryEnvironments.EnvironmentName("poems", project)}-py3.13";
        VirtualEnvironment(environment);
        var poetrysPython = Interpreter($@"{environment}\Scripts\python.exe");

        var kernelsPython = Interpreter(@"elsewhere\envs\nlp\python.exe");
        Kernel($@"{storesCopy}\Roaming\jupyter\kernels", "nlp", kernelsPython, "Python (nlp)");

        using (PythonEnvironment.LookingIn(Computer()))
        {
            Assert.Equal(poetrysPython, PythonEnvironment.For(program)?.Interpreter, StringComparer.OrdinalIgnoreCase);
        }

        Assert.Equal(kernelsPython, JupyterKernels.Named("nlp", Computer())?.Interpreter, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AProgramRunsWithThePoetryEnvironmentPoetryMadeForItAndWhatIsInstalledThere()
    {
        var project = Folder(@"computer\work\gradebook");
        Write(@"computer\work\gradebook\pyproject.toml", "[tool.poetry]\nname = \"gradebook\"\nversion = \"0.1.0\"\n");
        var program = Write(@"computer\work\gradebook\grades.py", "from gradetools import classify\n\nfor mark in (72, 38):\n    print(mark, classify(mark))\n");

        var environment = Folder($@"computer\AppData\Local\pypoetry\Cache\virtualenvs\{PoetryEnvironments.EnvironmentName("gradebook", project)}-py3.13");
        if (!MadeEnvironment(environment)) return;
        InstallInto(environment, "gradetools", "def classify(mark):\n    return \"pass\" if mark >= 40 else \"fail\"\n");

        using (PythonEnvironment.LookingIn(Computer()))
        {
            var report = await CheckAsync(program, "72 pass\n38 fail");

            Assert.Empty(report.Findings);
            Assert.Equal("It printed what you expected", report.LogicSummary);
        }
    }

    [Fact]
    public void AProgramInACondaProjectIsRunWithTheCondaEnvironmentsOwnPython()
    {
        // The run itself, with what only a found environment has installed, is shown above with Poetry's: every environment
        // found is run the same way. Here, that a conda project's is the one its run is planned with.
        var python = Interpreter(@"computer\miniconda3\envs\analysis\python.exe");
        Directory.CreateDirectory(Folder(@"computer\miniconda3\envs\analysis\conda-meta"));
        Write(@"computer\work\analysis\environment.yml", "name: analysis\ndependencies:\n  - python=3.13\n");
        var program = Write(@"computer\work\analysis\report.py", "from stattools import mean\n\nprint(mean([4, 8]))\n");

        using (PythonEnvironment.LookingIn(Computer()))
        {
            var launch = TargetFactory.FromFile(program);

            Assert.Equal(python, launch.Spec?.ExecutablePath, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("Running it with the Python in the conda environment analysis, which environment.yml names.", launch.Explanation);
        }
    }

    [Fact]
    public void AnEnvironmentBeyondTheProjectsOwnFolderIsNotTaken()
    {
        // An environment of some other project further up: the folder with .git is where this project begins.
        Write(@"outer\.venv\pyvenv.cfg", "include-system-site-packages = false\n");
        Write(@"outer\.venv\Scripts\python.exe", "");
        Directory.CreateDirectory(Folder(@"outer\project\.git"));
        var program = Write(@"outer\project\app\main.py", "print('hello')\n");

        Assert.Null(PythonEnvironment.For(program));
    }

    [Fact]
    public void AnEnvironmentWhosePythonIsNoLongerInstalledIsPassedOver()
    {
        Write(@"stale\.venv\pyvenv.cfg", $"home = {Folder("uninstalled-python")}\ninclude-system-site-packages = false\n");
        Write(@"stale\.venv\Scripts\python.exe", "");
        var program = Write(@"stale\main.py", "print('hello')\n");

        Assert.Null(PythonEnvironment.For(program));
    }

    [Fact]
    public async Task AWindowProgramStillRunningWhenItsTimeRunsOutIsANoteNotAMistake()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        // Waits as tkinter's mainloop does, without opening a window while the tests run.
        var program = Write(@"window\converter.py", """
            import threading
            import tkinter as tk


            def build(root):
                tk.Label(root, text="Temperature converter").pack()


            threading.Event().wait()
            """);

        var report = await CheckAsync(program, timeLimit: TimeSpan.FromSeconds(5));

        Assert.DoesNotContain(report.Findings, finding => finding.RuleId == "timed-out");
        Assert.Contains(report.Notes, note => note.StartsWith("converter.py is a program with a window - it uses tkinter", StringComparison.Ordinal));
        Assert.Equal("No syntax errors, and it was still running when its time ran out, as a program with a window does", report.SyntaxSummary);
    }

    [Fact]
    public async Task AServerStillWaitingForConnectionsIsANoteAndAnEndlessLoopBesideItIsStillWarnedAbout()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        // On this computer's loopback address and a port the system picks, so nothing outside can connect or be asked to allow it.
        var server = Write(@"web\server.py", """
            from http.server import BaseHTTPRequestHandler, HTTPServer


            class Hello(BaseHTTPRequestHandler):
                def do_GET(self):
                    self.send_response(200)
                    self.end_headers()
                    self.wfile.write(b"hello")


            HTTPServer(("127.0.0.1", 0), Hello).serve_forever()
            """);
        var endless = Write(@"web\counter.py", "count = 0\ntotal = 0\nwhile count < 10:\n    total += count\nprint(total)\n");

        var served = await CheckAsync(server, timeLimit: TimeSpan.FromSeconds(5));

        Assert.DoesNotContain(served.Findings, finding => finding.RuleId == "timed-out");
        Assert.Contains(served.Notes, note => note.StartsWith("server.py is a server - it waits for connections with Python's http.server", StringComparison.Ordinal));

        var counted = await CheckAsync(endless, timeLimit: TimeSpan.FromSeconds(5));

        Assert.Contains(counted.Findings, finding => finding.RuleId == "timed-out");
        Assert.DoesNotContain(counted.Notes, note => note.Contains("keeps running until", StringComparison.Ordinal));
    }

    private const string Bank = """
        class InsufficientFunds(Exception):
            pass


        class BankAccount:
            def __init__(self, owner, balance=0):
                self.owner = owner
                self.balance = balance

            def withdraw(self, amount):
                if amount > self.balance + 1:
                    raise InsufficientFunds(f"{self.owner} has only {self.balance}")
                self.balance -= amount

            def share_of(self, people):
                return self.balance / people
        """;

    private const string BankTests = """
        import unittest

        from bank import BankAccount, InsufficientFunds


        class BankAccountTests(unittest.TestCase):
            def setUp(self):
                self.account = BankAccount("Ada", 100)

            def test_withdraw_takes_away(self):
                self.account.withdraw(30)
                self.assertEqual(self.account.balance, 70)

            def test_withdraw_more_than_balance(self):
                with self.assertRaises(InsufficientFunds):
                    self.account.withdraw(101)

            def test_share_between_nobody(self):
                self.assertEqual(self.account.share_of(0), 0)

            def test_shares(self):
                for people, share in [(1, 100), (2, 50), (4, 20)]:
                    with self.subTest(people=people):
                        self.assertEqual(self.account.share_of(people), share)
        """;

    [Fact]
    public async Task EachUnittestTestIsRunAndEachThatFailsIsReportedOnItsOwnLine()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        // No unittest.main(): the file's tests are run all the same.
        Write(@"tests\bank.py", Bank);
        var tests = Write(@"tests\test_bank.py", BankTests);
        int LineOf(string text) => Array.FindIndex(BankTests.ReplaceLineEndings("\n").Split('\n'), line => line.Contains(text, StringComparison.Ordinal)) + 1;

        Assert.StartsWith("Running its tests with unittest, test by test, using", TargetFactory.FromFile(tests).Explanation, StringComparison.Ordinal);

        var report = await CheckAsync(tests);
        var failed = report.Findings.Where(finding => finding.RuleId == "test-failed").ToList();

        var notRaised = Assert.Single(failed, finding => finding.Title == "Test test_withdraw_more_than_balance failed: InsufficientFunds not raised");
        Assert.Equal(LineOf("with self.assertRaises(InsufficientFunds)"), notRaised.Line);
        Assert.Equal(Severity.Error, notRaised.Severity);

        var byZero = Assert.Single(failed, finding => finding.Title.StartsWith("Test test_share_between_nobody stopped with ZeroDivisionError", StringComparison.Ordinal));
        Assert.Equal(LineOf("self.assertEqual(self.account.share_of(0), 0)"), byZero.Line);
        Assert.Contains("raised in bank.share_of on line 16 of bank.py", byZero.Explanation, StringComparison.Ordinal);

        // A share of 100 among 4 is 25, not 20: the one subtest that fails is said, with what it was run with.
        var share = Assert.Single(failed, finding => finding.Title.StartsWith("Test test_shares (people=4) failed: 25.0 != 20", StringComparison.Ordinal));
        Assert.Equal(LineOf("self.assertEqual(self.account.share_of(people), share)"), share.Line);

        // Four tests: one passes, and test_shares is one test however many of its subtests fail.
        Assert.Equal(3, failed.Count);
        Assert.Equal("No syntax errors; 3 of 4 tests failed", report.SyntaxSummary);
    }

    [Fact]
    public async Task ATestFileThatCallsUnittestMainWithoutTheNameTestStillHasEachTestRun()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        // unittest.main() at the file's top, not under if __name__ == "__main__": - which runs the tests while the file is
        // being imported, and then ends the program.
        Write(@"unguarded\bank.py", Bank);
        var tests = Write(@"unguarded\test_bank.py", BankTests + "\n\n\nunittest.main()\n");

        var report = await CheckAsync(tests);

        Assert.Equal(3, report.Findings.Count(finding => finding.RuleId == "test-failed"));
        Assert.Equal("No syntax errors; 3 of 4 tests failed", report.SyntaxSummary);
    }

    [Fact]
    public async Task ATestFileInAFolderWhoseNameHasAHashInItIsStillPlacedOnItsLines()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        // A folder's name can hold what the launcher writes between the parts of a place, a # or a ;.
        Write(@"C# course\week #3; banking\bank.py", Bank);
        var tests = Write(@"C# course\week #3; banking\test_bank.py", BankTests);
        int LineOf(string text) => Array.FindIndex(BankTests.ReplaceLineEndings("\n").Split('\n'), line => line.Contains(text, StringComparison.Ordinal)) + 1;

        var report = await CheckAsync(tests);

        var byZero = Assert.Single(report.Findings, finding => finding.Title.StartsWith("Test test_share_between_nobody stopped with ZeroDivisionError", StringComparison.Ordinal));
        Assert.Equal(LineOf("self.assertEqual(self.account.share_of(0), 0)"), byZero.Line);
        Assert.Contains("raised in bank.share_of on line 16 of bank.py", byZero.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailureInsideUnittestItselfIsPlacedOnTheTestsLineAsUnittestPlacesIt()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        // assertAlmostEqual refuses places and delta together, and says so from inside unittest's own code.
        var tests = Write(@"misused\test_rounding.py", """
            import unittest


            class RoundingTests(unittest.TestCase):
                def test_close_enough(self):
                    self.assertAlmostEqual(0.1 + 0.2, 0.3, places=2, delta=0.1)
            """);

        var report = await CheckAsync(tests);

        var misused = Assert.Single(report.Findings, finding => finding.RuleId == "test-failed");
        Assert.Equal(6, misused.Line);
        Assert.Equal("unittest ran the test test_close_enough, and it stopped with TypeError (specify delta or places not both) on line 6.", misused.Explanation);
    }

    [Fact]
    public async Task ATestClassWhoseSettingUpFailsIsSaidToHaveRunNoneOfItsTests()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        Write(@"setup\bank.py", Bank);
        var tests = Write(@"setup\test_bank.py", """
            import unittest

            from bank import BankAccount


            class SharedAccountTests(unittest.TestCase):
                @classmethod
                def setUpClass(cls):
                    cls.account = BankAccount("Ada", 100)
                    cls.share = cls.account.share_of(0)

                def test_share(self):
                    self.assertEqual(self.share, 0)

                def test_balance(self):
                    self.assertEqual(self.account.balance, 100)
            """);

        var report = await CheckAsync(tests);

        // Setting the class up fails, so neither of its tests runs: that is one failure, and not a test that failed.
        var settingUp = Assert.Single(report.Findings, finding => finding.RuleId == "test-failed");
        Assert.Equal(10, settingUp.Line);
        Assert.Equal("setUpClass (test_bank.SharedAccountTests) stopped with ZeroDivisionError: division by zero", settingUp.Title);
        Assert.EndsWith("raised in bank.share_of on line 16 of bank.py, so the tests it sets up for did not run.", settingUp.Explanation, StringComparison.Ordinal);
        Assert.Equal("No syntax errors, but setting up for its tests failed, so unittest ran none of them", report.SyntaxSummary);
    }

    [Fact]
    public async Task ATestFileThatEndsTheProgramAsItIsImportedIsSaidNotToHaveHadItsTestsRun()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var tests = Write(@"ended\test_totals.py", """
            import sys
            import unittest


            class TotalTests(unittest.TestCase):
                def test_total(self):
                    self.assertEqual(sum([1, 2]), 3)


            sys.exit(1)
            """);

        var report = await CheckAsync(tests);

        // Nothing is reported as a mistake in FixFinder's own launcher; a note says why the tests were not run.
        Assert.DoesNotContain(report.Findings, finding => Path.GetFileName(finding.File).StartsWith(PythonTests.LauncherPrefix, StringComparison.Ordinal));
        Assert.Contains(report.Notes, note => note.Contains(
            "could not finish, so whether the tests pass is not known: RuntimeError: test_totals.py ended the program with sys.exit(1) while it was " +
            "being imported, so its tests could not be run", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AUnittestFileWhoseTestsAllPassSaysSo()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        Write(@"passing\bank.py", Bank);
        var tests = Write(@"passing\test_bank.py", """
            import unittest

            from bank import BankAccount


            class BankAccountTests(unittest.TestCase):
                def test_withdraw_takes_away(self):
                    account = BankAccount("Ada", 100)
                    account.withdraw(30)
                    self.assertEqual(account.balance, 70)

                def test_share(self):
                    self.assertEqual(BankAccount("Ada", 100).share_of(4), 25)


            if __name__ == "__main__":
                unittest.main()
            """);

        var report = await CheckAsync(tests);

        Assert.Empty(report.Findings);
        Assert.Equal("No syntax errors, and all 2 of its tests pass", report.SyntaxSummary);
    }

    /// <summary>
    /// A project whose VS Code settings name a Python with pytest installed - the one the tests were given, or the one on
    /// PATH - so its tests run with pytest itself. Null when there is no Python here with pytest.
    /// </summary>
    private string? WithPytest(string project)
    {
        if (NotebookTests.PythonWith("pytest") is not { } python) return null;

        Write(Path.Combine(project, ".vscode", "settings.json"),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["python.defaultInterpreterPath"] = python }));
        return python;
    }

    private const string BankPytests = """
        import pytest

        from bank import BankAccount, InsufficientFunds


        @pytest.fixture
        def account():
            return BankAccount("Ada", 100)


        @pytest.fixture
        def closed_account():
            raise ValueError("the account is closed")


        def test_balance(account):
            assert account.balance == 100


        @pytest.mark.parametrize("people, share", [(1, 100), (2, 50), (4, 20)])
        def test_shares(account, people, share):
            assert account.share_of(people) == share


        def test_share_between_nobody(account):
            assert account.share_of(0) == 0


        def test_withdraw_from_closed(closed_account):
            closed_account.withdraw(10)


        def test_withdraw_more_than_balance(account):
            with pytest.raises(InsufficientFunds):
                account.withdraw(101)


        @pytest.mark.skip(reason="interest is not written yet")
        def test_interest(account):
            pass


        @pytest.mark.xfail(reason="an overdraft of 1 is allowed")
        def test_overdraft(account):
            with pytest.raises(Exception):
                account.withdraw(101)
        """;

    [Fact]
    public async Task EachPytestTestIsRunWithPytestItselfAndEachThatFailsIsReportedOnItsOwnLine()
    {
        if (WithPytest("pytests") is null) return;

        Write(@"pytests\bank.py", Bank);
        // The project's own settings are pytest's to read: -v changes how pytest writes what it does, not what is found.
        Write(@"pytests\pytest.ini", "[pytest]\naddopts = -v\n");
        var tests = Write(@"pytests\test_bank.py", BankPytests);
        int LineOf(string text) => Array.FindIndex(BankPytests.ReplaceLineEndings("\n").Split('\n'), line => line.Contains(text, StringComparison.Ordinal)) + 1;

        Assert.Equal("Running its tests with pytest, test by test, using the Python this project's VS Code settings name.", TargetFactory.FromFile(tests).Explanation);

        var report = await CheckAsync(tests);
        var failed = report.Findings.Where(finding => finding.RuleId == "test-failed").ToList();

        // A share of 100 among 4 is 25, not 20: the one set of parameters that fails is said, in pytest's own words.
        var share = Assert.Single(failed, finding => finding.Title == "Test test_shares[4-20] failed: assert 25.0 == 20");
        Assert.Equal(LineOf("assert account.share_of(people) == share"), share.Line);
        Assert.Contains("assert 25.0 == 20 + where 25.0 = share_of(4)", share.Explanation, StringComparison.Ordinal);

        // pytest fails a test itself when pytest.raises sees nothing raised - in words that name the exception differently from
        // one version of pytest to the next.
        var notRaised = Assert.Single(failed, finding =>
            finding.Title.StartsWith("Test test_withdraw_more_than_balance failed: DID NOT RAISE", StringComparison.Ordinal) &&
            finding.Title.Contains("InsufficientFunds", StringComparison.Ordinal));
        Assert.Equal(LineOf("with pytest.raises(InsufficientFunds):"), notRaised.Line);
        Assert.StartsWith("pytest ran the test test_withdraw_more_than_balance, and failed it on line", notRaised.Explanation, StringComparison.Ordinal);

        var byZero = Assert.Single(failed, finding => finding.Title == "Test test_share_between_nobody stopped with ZeroDivisionError: division by zero");
        Assert.Equal(LineOf("assert account.share_of(0) == 0"), byZero.Line);
        Assert.Contains("raised in bank.share_of on line 16 of bank.py", byZero.Explanation, StringComparison.Ordinal);

        // The fixture it asks for fails, so the test never runs: placed on the fixture's own line.
        var settingUp = Assert.Single(failed, finding => finding.Title == "Setting up test_withdraw_from_closed stopped with ValueError: the account is closed");
        Assert.Equal(LineOf("raise ValueError(\"the account is closed\")"), settingUp.Line);
        Assert.EndsWith("so the test did not run.", settingUp.Explanation, StringComparison.Ordinal);

        Assert.Equal(4, failed.Count);
        Assert.Contains("1 test was skipped, so pytest did not say whether it passes.", report.Notes);

        // Each set of parameters is a test of its own, as pytest counts them; the xfail test failing as marked is no failure.
        Assert.Equal("No syntax errors; 3 of 7 tests failed, and setting up for others failed, so those did not run", report.SyntaxSummary);

        // pytest's cache is kept out of the project, and so are the compiled files Python keeps beside the code it imports.
        Assert.False(Directory.Exists(Folder(@"pytests\.pytest_cache")));
        Assert.False(Directory.Exists(Folder(@"pytests\__pycache__")));
    }

    [Fact]
    public async Task AFailureInsidePytestItselfIsPlacedOnTheTestsLineAsPytestPlacesIt()
    {
        if (WithPytest("inside") is null) return;

        // pytest.approx is given a set, which it cannot compare in order: its own code raises the error.
        var tests = Write(@"inside\test_close.py", "import pytest\n\n\ndef test_close_enough():\n    assert [0.1 + 0.2] == pytest.approx({0.3})\n");

        var report = await CheckAsync(tests);

        var stopped = Assert.Single(report.Findings, finding => finding.RuleId == "test-failed");
        Assert.StartsWith("Test test_close_enough stopped with TypeError: pytest.approx() only supports ordered sequences", stopped.Title, StringComparison.Ordinal);
        Assert.Equal(5, stopped.Line);
        Assert.DoesNotContain("_pytest", stopped.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATestCaseClassInAFileThatImportsPytestIsRunWithPytestAlongsideItsOtherTests()
    {
        if (WithPytest("mixed") is null) return;

        var tests = Write(@"mixed\test_marks.py", """
            import unittest

            import pytest


            class MarksTests(unittest.TestCase):
                def test_total(self):
                    self.assertEqual(sum([40, 2]), 42)


            @pytest.mark.parametrize("mark", [40, 2])
            def test_positive(mark):
                assert mark > 0
            """);

        Assert.StartsWith("Running its tests with pytest", TargetFactory.FromFile(tests).Explanation, StringComparison.Ordinal);

        var report = await CheckAsync(tests);

        Assert.Empty(report.Findings.Where(finding => finding.RuleId == "test-failed"));
        Assert.Equal("No syntax errors, and all 3 of its tests pass", report.SyntaxSummary);
    }

    [Fact]
    public async Task APytestFileThatCannotBeImportedIsReportedAsTheErrorThatStopsIt()
    {
        if (WithPytest("unimportable") is null) return;

        var tests = Write(@"unimportable\test_tidy.py", "from text_helpers import tidy\n\n\ndef test_tidy():\n    assert tidy(\" a \") == \"a\"\n");

        var report = await CheckAsync(tests);

        var stopped = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.StartsWith("It crashed: ModuleNotFoundError: No module named 'text_helpers'", stopped.Title, StringComparison.Ordinal);
        Assert.Equal(1, stopped.Line);
        Assert.DoesNotContain(report.Notes, note => note.Contains("found no tests", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATestFileThatPytestSkipsWholeIsSaidToHaveHadNoneOfItsTestsRun()
    {
        if (WithPytest("skipped") is null) return;

        var tests = Write(@"skipped\test_stats.py", "import pytest\n\nstats = pytest.importorskip(\"no_such_stats_library\")\n\n\ndef test_mean():\n    assert stats.mean([1, 2]) == 1.5\n");

        var report = await CheckAsync(tests);

        Assert.Empty(report.Findings.Where(finding => finding.Kind == FindingKind.Runtime));
        Assert.Contains(report.Notes, note => note.StartsWith(
            "pytest skipped the whole of test_stats.py - could not import 'no_such_stats_library': No module named 'no_such_stats_library' - so none of its tests were run.",
            StringComparison.Ordinal));
        Assert.Equal("No syntax errors, but pytest ran none of its tests", report.SyntaxSummary);
    }

    [Fact]
    public async Task CleaningUpAfterAPytestTestThatFailsIsReportedApartFromTheTest()
    {
        if (WithPytest("cleanup") is null) return;

        var tests = Write(@"cleanup\test_files.py", """
            import pytest


            @pytest.fixture
            def report_file():
                yield "report.txt"
                raise RuntimeError("the report is still open")


            def test_name(report_file):
                assert report_file.endswith(".txt")
            """);

        var report = await CheckAsync(tests);

        var cleaningUp = Assert.Single(report.Findings, finding => finding.RuleId == "test-failed");
        Assert.Equal("Cleaning up after test_name stopped with RuntimeError: the report is still open", cleaningUp.Title);
        Assert.Equal(7, cleaningUp.Line);
        Assert.Equal("No syntax errors, and its test passes; cleaning up after it failed", report.SyntaxSummary);
    }

    [Fact]
    public async Task WhatPytestSaysWhenItWillNotRunTheTestsIsQuoted()
    {
        if (WithPytest("options") is null) return;

        // An option of a plugin that is not installed, as a course's settings may name for coverage.
        Write(@"options\pytest.ini", "[pytest]\naddopts = --cov=src\n");
        var tests = Write(@"options\test_one.py", "def test_one():\n    assert 1 + 1 == 2\n");

        var report = await CheckAsync(tests);

        // The settings file is named as pytest names it, which may spell the temp folder out in full.
        Assert.Contains(report.Notes, note =>
            note.StartsWith("pytest would not run test_one.py's tests, so whether they pass is not known. It said: \"unrecognized arguments: --cov=src\" - " +
                            "and it takes options from ", StringComparison.Ordinal) &&
            note.EndsWith(@"\options\pytest.ini as well as from how it is run.", StringComparison.Ordinal));
        Assert.Empty(report.Findings.Where(finding => finding.Kind == FindingKind.Runtime));
        Assert.Equal("No syntax errors, but pytest would not run its tests", report.SyntaxSummary);
    }

    [Fact]
    public async Task ATestThatStopsPytestLeavesTheTestsAfterItSaidNotToHaveRun()
    {
        if (WithPytest("stopping") is null) return;

        var tests = Write(@"stopping\test_marks.py", """
            import pytest


            def test_first():
                assert True


            def test_stops():
                pytest.exit("the marks file is missing")


            def test_never_reached():
                assert True
            """);

        var report = await CheckAsync(tests);

        Assert.Contains("pytest stopped before it had run all of test_marks.py's tests - it said \"_pytest.outcomes.Exit: the marks file is missing\" - " +
                        "so those it had not reached were not run.", report.Notes);
        Assert.Equal("No syntax errors; pytest stopped after 1 of its 3 tests", report.SyntaxSummary);
    }

    [Fact]
    public async Task WithoutPytestInstalledTheTestsAreSaidNotToHaveBeenRunAndTheFileIsRunAsAProgram()
    {
        // An environment of the project's own, which has nothing installed in it - pytest included, whatever the Python it was
        // made from has.
        if (!MadeEnvironment(Folder(@"no-pytest\.venv"))) return;

        // Run as a program, the file finds what is beside it, as it would.
        Write(@"no-pytest\totals.py", "def total(values):\n    return sum(values)\n");
        var plain = Write(@"no-pytest\test_totals.py", "from totals import total\n\n\ndef test_total():\n    assert total([1, 2, 3]) == 6\n");
        var importing = Write(@"no-pytest\test_marks.py", "import pytest\n\n\ndef test_marks():\n    with pytest.raises(ValueError):\n        int(\"x\")\n");

        var plainReport = await CheckAsync(plain);

        Assert.Contains(plainReport.Notes, note => note.StartsWith(
            "test_totals.py's tests are written for pytest, which is not part of Python and is not installed for the Python it was run with, so they were not run",
            StringComparison.Ordinal));
        Assert.Equal("No syntax errors; its tests are written for pytest, which is not installed for the Python it ran with", plainReport.SyntaxSummary);
        Assert.Empty(plainReport.Findings.Where(finding => finding.Kind == FindingKind.Runtime));

        // Run as a program, a file that imports pytest stops there, as it would.
        var importingReport = await CheckAsync(importing);

        var stopped = Assert.Single(importingReport.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.StartsWith("It crashed: ModuleNotFoundError: No module named 'pytest'", stopped.Title, StringComparison.Ordinal);
        Assert.Equal(1, stopped.Line);
    }

    [Fact]
    public async Task ATestClassWhoseCleaningUpFailsHasItsTestsCountedAndTheCleaningUpSaid()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var tests = Write(@"teardown\test_ledger.py", """
            import unittest


            class LedgerTests(unittest.TestCase):
                @classmethod
                def tearDownClass(cls):
                    raise RuntimeError("the ledger is still open")

                def test_opening(self):
                    self.assertEqual(0, 0)

                def test_closing(self):
                    self.assertTrue(True)
            """);

        var report = await CheckAsync(tests);

        // Both tests ran and passed; cleaning up after them is not setting up for them.
        var cleaningUp = Assert.Single(report.Findings, finding => finding.RuleId == "test-failed");
        Assert.Equal("tearDownClass (test_ledger.LedgerTests) stopped with RuntimeError: the ledger is still open", cleaningUp.Title);
        Assert.Equal(7, cleaningUp.Line);
        Assert.StartsWith("unittest ran the tests, and then cleaning up after them stopped", cleaningUp.Explanation, StringComparison.Ordinal);
        Assert.Equal("No syntax errors, and all 2 of its tests pass; cleaning up after them failed", report.SyntaxSummary);
    }

    [Fact]
    public void TheInterpreterAProjectsVsCodeSettingsNameIsTaken()
    {
        var interpreter = Write(@"vscode\tools\python\python.exe", "");
        Write(@"vscode\.vscode\settings.json", """
            {
                // chosen for this project
                "python.defaultInterpreterPath": "${workspaceFolder}/tools/python/python.exe",
            }
            """);
        var program = Write(@"vscode\src\main.py", "print('hello')\n");

        var found = PythonEnvironment.For(program);

        Assert.NotNull(found);
        Assert.Equal(Path.GetFullPath(interpreter), found!.Interpreter, StringComparer.OrdinalIgnoreCase);
    }
}
