using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// Tests kept in a folder of their own: the folders above them that hold the program's modules they import, the folders
/// pytest's settings name and the conftest.py files it uses, the folder a copy of them has to hold - and a module of the
/// program's own that Python cannot find, looked for in the project.
/// </summary>
public class PythonTestsFolderTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static readonly string? Python = TargetFactory.FindOnPath("python") ?? TargetFactory.FindOnPath("py");

    private const string UnittestOfGrades = "import unittest\n\nfrom grades import grade\n\n\nclass GradeTests(unittest.TestCase):\n    def test_first(self):\n        self.assertEqual(grade(70), \"First\")\n";

    private string Write(string relative, string content = "")
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string Folder(string relative = "") => Path.GetFullPath(Path.Combine(_temp.Path, relative)).TrimEnd(Path.DirectorySeparatorChar);

    [Fact]
    public void ATestFileImportsTheProgramFromTheFolderAboveIt()
    {
        Write(@"project\grades.py", "def grade(mark):\n    return mark\n");
        var tests = Write(@"project\tests\test_grades.py", UnittestOfGrades);

        Assert.Equal([Folder("project")], ProgramLayout.PythonImportFolders(tests));
        Assert.Equal(Folder("project"), ProgramCopy.RootOf(tests), ignoreCase: true);
    }

    [Fact]
    public void ATestFileImportsTheProgramFromTheSrcFolderBesideItsOwn()
    {
        Write(@"project\src\grades.py", "def grade(mark):\n    return mark\n");
        var tests = Write(@"project\tests\test_grades.py", UnittestOfGrades);

        Assert.Equal([Folder(@"project\src")], ProgramLayout.PythonImportFolders(tests));
        Assert.Equal(Folder("project"), ProgramCopy.RootOf(tests), ignoreCase: true);
    }

    [Fact]
    public void AModuleBesideTheTestsIsTheOnePythonFindsSoNothingIsLookedForAbove()
    {
        Write(@"project\grades.py", "def grade(mark):\n    return mark\n");
        Write(@"project\tests\grades.py", "def grade(mark):\n    return mark\n");
        var tests = Write(@"project\tests\test_grades.py", UnittestOfGrades);

        Assert.Empty(ProgramLayout.PythonImportFolders(tests));
    }

    /// <summary>python scripts\run.py looks only in scripts wherever it is started, so a program is given no folders above it.</summary>
    [Fact]
    public void AProgramThatIsNotOneOfTestsIsGivenNoFoldersAbove()
    {
        Write(@"project\helpers.py", "def double(x):\n    return x + x\n");
        var script = Write(@"project\scripts\run.py", "from helpers import double\n\nprint(double(4))\n");

        Assert.Empty(ProgramLayout.PythonImportFolders(script));
        Assert.Equal(Folder(@"project\scripts"), ProgramCopy.RootOf(script), ignoreCase: true);
    }

    [Fact]
    public void NothingIsLookedForAboveTheProjectsOwnFolder()
    {
        Write("grades.py", "def grade(mark):\n    return mark\n");
        Write(@"project\pyproject.toml", "[project]\nname = 'marks'\n");
        var tests = Write(@"project\tests\test_grades.py", UnittestOfGrades);

        Assert.Empty(ProgramLayout.PythonImportFolders(tests));
    }

    /// <summary>A random.py written for an exercise in the folder above is never put ahead of the random Python has itself.</summary>
    [Fact]
    public void AModulePythonHasItselfIsNeverTakenFromAFolderAbove()
    {
        if (Python is null) return;

        Write(@"project\random.py", "print('the wrong random')\n");
        var tests = Write(@"project\tests\test_dice.py", "import random\nimport unittest\n\n\nclass DiceTests(unittest.TestCase):\n    def test_one(self):\n        self.assertEqual(random.randint(1, 1), 1)\n");

        Assert.Equal([Folder("project")], ProgramLayout.PythonImportFolders(tests));
        Assert.Empty(ProgramLayout.PythonImportFolders(tests, Python));
    }

    [Fact]
    public void ThePythonpathOfPytestIniIsReadFromItsOwnFolder()
    {
        Directory.CreateDirectory(Folder(@"project\app"));
        Directory.CreateDirectory(Folder(@"project\lib"));
        Write(@"project\pytest.ini", "[pytest]\npythonpath = app\n    lib\naddopts = -q\n");
        var tests = Write(@"project\tests\test_grades.py", "from grades import grade\n\n\ndef test_first():\n    assert grade(70) == 'First'\n");

        Assert.Equal([Folder(@"project\app"), Folder(@"project\lib")], ProgramLayout.PytestPythonPath(tests));
        Assert.Null(ProgramLayout.PytestRootFolder(tests));
    }

    [Fact]
    public void ThePythonpathOfPyprojectTomlIsReadFromItsPytestSection()
    {
        Directory.CreateDirectory(Folder(@"project\lib"));
        Write(@"project\pyproject.toml", "[project]\nname = 'marks'\n\n[tool.pytest.ini_options]\npythonpath = [\n    \"lib\",\n]\n");
        var tests = Write(@"project\tests\test_grades.py", "from grades import grade\n\n\ndef test_first():\n    assert grade(70) == 'First'\n");

        Assert.Equal([Folder(@"project\lib")], ProgramLayout.PytestPythonPath(tests));
        Assert.Equal(Folder("project"), ProgramCopy.RootOf(tests), ignoreCase: true);
    }

    /// <summary>With no settings file to say where the project is, the folder of the conftest.py above the tests is the project's.</summary>
    [Fact]
    public void TheFolderOfTheConftestAboveTheTestsIsTheProjects()
    {
        Write(@"project\conftest.py", "import pytest\n\n\n@pytest.fixture\ndef seventy():\n    return 70\n");
        var tests = Write(@"project\tests\test_grades.py", "def test_first(seventy):\n    assert seventy == 70\n");

        Assert.Equal(Folder("project"), ProgramLayout.PytestRootFolder(tests), ignoreCase: true);
        Assert.Equal(Folder("project"), ProgramCopy.RootOf(tests), ignoreCase: true);
    }

    [Fact]
    public void AModuleOfTheProgramsOwnIsFoundInTheProjectButNotInItsEnvironment()
    {
        Write(@"project\app\grades.py", "def grade(mark):\n    return mark\n");
        Write(@"project\.venv\pyvenv.cfg", "home = C:\\Python313\n");
        Write(@"project\.venv\Lib\site-packages\marks.py", "");
        var tests = Write(@"project\tests\test_grades.py", UnittestOfGrades);

        Assert.Equal(Path.Combine(Folder(@"project\app"), "grades.py"), ProgramLayout.PythonModuleInProject(tests, "grades"), ignoreCase: true);
        Assert.Null(ProgramLayout.PythonModuleInProject(tests, "marks"));
    }
}
