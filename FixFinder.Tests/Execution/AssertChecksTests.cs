using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// Which Python programs check themselves with assert, and nothing else: asserts at their top, or in their __main__ block,
/// with only definitions, names given values and prints beside them - and nothing read from what is typed in.
/// </summary>
public class AssertChecksTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Double = "def double(x):\n    return x * 2\n\n\n";

    [Theory]
    [InlineData(Double + "assert double(2) == 4\nassert double(0) == 0\nprint(\"all good\")\n")]
    [InlineData(Double + "if __name__ == \"__main__\":\n    assert double(2) == 4, \"two doubled\"\n    print(\"done\")\n")]
    [InlineData(Double + "EXPECTED = 4\nassert (\n    double(2)\n    == EXPECTED\n)\n")]
    public void AProgramWhoseAssertsAreWhatItDoesChecksItself(string source) =>
        Assert.Equal(PythonTests.Framework.Asserts, FrameworkOf("double.py", source));

    [Theory]
    // It reads what is typed in, it calls something of its own at its top, its asserts are a function's own, or it has none.
    [InlineData(Double + "number = int(input())\nassert double(number) == number * 2\n")]
    [InlineData(Double + "main()\nassert double(2) == 4\n")]
    [InlineData("def double(x):\n    assert x >= 0\n    return x * 2\n")]
    [InlineData(Double + "print(double(2))\n")]
    public void AProgramThatDoesMoreOrHasNoAssertsAtItsTopDoesNot(string source) =>
        Assert.Equal(PythonTests.Framework.None, FrameworkOf("double.py", source));

    /// <summary>A file of doctests is one of doctests, and a file of pytest's tests is pytest's, whatever asserts they hold.</summary>
    [Fact]
    public void DoctestsAndPytestTestsAreRunByTheirOwnFrameworks()
    {
        Assert.Equal(PythonTests.Framework.Doctest, FrameworkOf("double.py", "def double(x):\n    \"\"\"\n    >>> double(2)\n    4\n    \"\"\"\n    return x * 2\n"));
        Assert.Equal(PythonTests.Framework.Pytest, FrameworkOf("test_double.py", Double + "def test_double():\n    assert double(2) == 4\n"));
    }

    private PythonTests.Framework FrameworkOf(string name, string source)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, source);
        return PythonTests.FrameworkOf(path);
    }
}
