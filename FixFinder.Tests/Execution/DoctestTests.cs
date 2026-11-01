using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// Which Python files are doctests: the examples in their docstrings are all they run - definitions at the top, maybe a
/// __main__ block that runs doctest.testmod() and nothing else. A program that also does work of its own stays a program.
/// </summary>
public class DoctestTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Examples = "def double(x):\n    \"\"\"\n    >>> double(2)\n    4\n    \"\"\"\n    return x + x\n";

    [Theory]
    // Doctests and nothing else, as a course's homework is written; then with a __main__ block that runs them.
    [InlineData(Examples)]
    [InlineData("\"\"\"Doubling.\"\"\"\nimport math\n\nLIMIT = 10\n\n\n" + Examples)]
    [InlineData(Examples + "\n\nif __name__ == \"__main__\":\n    import doctest\n    doctest.testmod()\n")]
    [InlineData(Examples + "\n\nif __name__ == '__main__':\n    from doctest import testmod\n    testmod(verbose=True)\n")]
    [InlineData("import doctest\n\n\n" + Examples + "\n\nif __name__ == \"__main__\":\n    doctest.testmod(optionflags=doctest.ELLIPSIS)\n")]
    // A name given a value over more than one line, and a decorated function.
    [InlineData("MARKS = {\n    'a': 70,\n    'b': 60,\n}\n\n\n@staticmethod\n" + Examples)]
    public void AFileWhoseDoctestsAreAllItRunsIsOneOfDoctests(string source) =>
        Assert.Equal(PythonTests.Framework.Doctest, FrameworkOf("marks.py", source));

    [Theory]
    // No examples at all, and >>> in a comment rather than a docstring.
    [InlineData("def double(x):\n    return x + x\n")]
    [InlineData("def double(x):\n    # >>> double(2)\n    return x + x\n")]
    // A program of its own: it calls main(), prints, or reads what is typed in at its top.
    [InlineData(Examples + "\n\nif __name__ == \"__main__\":\n    import doctest\n    doctest.testmod()\n    main()\n")]
    [InlineData(Examples + "\nprint(double(2))\n")]
    [InlineData(Examples + "\nnumber = int(input())\n")]
    public void AFileThatIsAProgramOrHasNoExamplesIsNotOneOfDoctests(string source) =>
        Assert.Equal(PythonTests.Framework.None, FrameworkOf("marks.py", source));

    /// <summary>A file of unittest or pytest tests with examples in its docstrings is run by its own framework.</summary>
    [Fact]
    public void AFileOfUnittestTestsWithExamplesIsRunByUnittest() =>
        Assert.Equal(PythonTests.Framework.Unittest,
            FrameworkOf("test_marks.py", "import unittest\n\n\nclass MarkTests(unittest.TestCase):\n    \"\"\"\n    >>> 1 + 1\n    2\n    \"\"\"\n    def test_one(self):\n        pass\n"));

    private PythonTests.Framework FrameworkOf(string name, string source)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, source);
        return PythonTests.FrameworkOf(path);
    }
}
