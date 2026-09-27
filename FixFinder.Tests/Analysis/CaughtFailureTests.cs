using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// A failure the code catches on purpose - trying first and handling what goes wrong, or a test checking that bad input
/// is refused - is how the program works, so it is not reported. The same failure without a handler in the same
/// function that certainly catches it still is: one for another exception, one that raises it again, one with a when
/// test, a try the failing line is not inside, and a caller's try in another function.
/// </summary>
public class CaughtFailureTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<string> WriteAsync(string name, string code)
    {
        var path = Path.Combine(_temp.Path, name);
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return path;
    }

    /// <summary>The mistakes found in a Python file, or nothing at all when Python is not installed.</summary>
    private async Task<IReadOnlyList<AnalysisFinding>?> PythonAsync(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;
        return Mistakes(AbstractChecks.Run(await PythonFrontend.ReadAsync([await WriteAsync("caught.py", code)], python), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> JavaAsync(string body)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var code = $"import java.util.*;\n\npublic class Caught {{\n    static int percent(int value) {{\n        if (value > 100) {{\n            throw new IllegalArgumentException(\"too big\");\n        }}\n        return value;\n    }}\n\n{body}\n}}\n";
        return Mistakes(AbstractChecks.Run(await JavaFrontend.ReadAsync([await WriteAsync("Caught.java", code)], tools.Javac, tools.Java), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>> CSharpAsync(string body)
    {
        var code = $"using System;\nusing System.Collections.Generic;\n\npublic static class Caught\n{{\n    static int Percent(int value)\n    {{\n        if (value > 100) throw new ArgumentOutOfRangeException(nameof(value));\n        return value;\n    }}\n\n{body}\n}}\n";
        return Mistakes(AbstractChecks.Run(await CSharpFrontend.ReadAsync([await WriteAsync("Caught.cs", code)]), new SourceText()));
    }

    private static IReadOnlyList<AnalysisFinding> Mistakes(IEnumerable<AnalysisFinding> findings) => findings.Where(f => f.Severity != Severity.Suggestion).ToList();

    private static string Shown(IEnumerable<AnalysisFinding> findings) => string.Join("; ", findings.Select(f => $"{f.CheckId} line {f.Span.Line}: {f.Message}"));

    private const string PythonPercent = "def percent(value):\n    if value > 100:\n        raise ValueError(\"too big\")\n    return value\n\n\n";

    [Theory]
    [InlineData("trying first and handling ZeroDivisionError", "def average(values):\n    try:\n        return sum(values) / len(values)\n    except ZeroDivisionError:\n        return 0.0\n")]
    [InlineData("text that is not a number, caught as ValueError", "def number():\n    try:\n        return int(\"twelve\")\n    except ValueError:\n        return 0\n")]
    [InlineData("an IndexError caught as the LookupError above it", "def first():\n    items = []\n    try:\n        return items[0]\n    except LookupError:\n        return None\n")]
    [InlineData("a bare except", "def ratio():\n    try:\n        return 10 / 0\n    except:\n        return None\n")]
    [InlineData("a refused argument caught as Exception", PythonPercent + "def demo():\n    try:\n        percent(120)\n    except Exception:\n        print(\"refused\")\n")]
    [InlineData("the program's own exception caught as the builtin it extends", "class NegativeError(ValueError):\n    pass\n\n\ndef root(value):\n    if value < 0:\n        raise NegativeError(\"negative\")\n    return value ** 0.5\n\n\ndef demo():\n    try:\n        root(-4)\n    except ValueError:\n        print(\"refused\")\n")]
    [InlineData("a handler that raises something else in its place", "def number():\n    try:\n        return int(\"twelve\")\n    except ValueError:\n        raise SystemExit(\"not a number\") from None\n")]
    [InlineData("a unittest assertRaises block", "import unittest\n\n\n" + PythonPercent + "class PercentTests(unittest.TestCase):\n    def test_refuses_more_than_a_hundred(self):\n        with self.assertRaises(ValueError):\n            percent(120)\n")]
    [InlineData("a pytest.raises block", "import pytest\n\n\n" + PythonPercent + "def test_refuses_more_than_a_hundred():\n    with pytest.raises((ValueError, TypeError), match=\"too big\"):\n        percent(120)\n")]
    public async Task PythonFailuresCaughtOnPurposeAreNotReported(string shape, string code)
    {
        if (await PythonAsync(code) is not { } found) return;

        Assert.True(found.Count == 0, $"{shape}: {Shown(found)}");
    }

    [Theory]
    [InlineData("the handler catches another exception", "analysis-not-a-number", "def number():\n    try:\n        return int(\"twelve\")\n    except KeyError:\n        return 0\n")]
    [InlineData("the handler raises it again", "analysis-division-by-zero", "def ratio():\n    try:\n        return 10 / 0\n    except ZeroDivisionError:\n        print(\"dividing by zero\")\n        raise\n")]
    [InlineData("the failure is in the handler itself", "analysis-not-a-number", "def number():\n    try:\n        return int(\"12\")\n    except ValueError:\n        return int(\"twelve\")\n")]
    [InlineData("the failure comes after the try", "analysis-division-by-zero", "def ratio():\n    try:\n        value = 1\n    except ValueError:\n        value = 0\n    return value / 0\n")]
    [InlineData("only a caller's try, in another function", "analysis-division-by-zero", "def average(values):\n    return sum(values) / len(values)\n\n\ndef caller():\n    try:\n        return average([])\n    except ZeroDivisionError:\n        return 0\n")]
    [InlineData("a test expecting another exception", "analysis-contract-broken", "import unittest\n\n\n" + PythonPercent + "class PercentTests(unittest.TestCase):\n    def test_refuses(self):\n        with self.assertRaises(TypeError):\n            percent(120)\n")]
    public async Task PythonFailuresNotCertainlyCaughtAreStillReported(string shape, string check, string code)
    {
        if (await PythonAsync(code) is not { } found) return;

        Assert.True(found.Any(f => f.CheckId == check), $"{shape}: expected {check}, found {Shown(found)}");
    }

    [Theory]
    [InlineData("ArithmeticException", "    static int ratio(int total) {\n        try {\n            return total / 0;\n        } catch (ArithmeticException problem) {\n            return 0;\n        }\n    }")]
    [InlineData("NumberFormatException caught as IllegalArgumentException", "    static int number() {\n        try {\n            return Integer.parseInt(\"twelve\");\n        } catch (IllegalArgumentException problem) {\n            return -1;\n        }\n    }")]
    [InlineData("a list index caught as IndexOutOfBoundsException", "    static String first() {\n        List<String> names = new ArrayList<>();\n        try {\n            return names.get(0);\n        } catch (IndexOutOfBoundsException problem) {\n            return \"none\";\n        }\n    }")]
    [InlineData("a refused argument caught as RuntimeException", "    static void demo() {\n        try {\n            percent(120);\n        } catch (RuntimeException problem) {\n            System.out.println(\"refused\");\n        }\n    }")]
    public async Task JavaFailuresCaughtOnPurposeAreNotReported(string shape, string body)
    {
        if (await JavaAsync(body) is not { } found) return;

        Assert.True(found.Count == 0, $"{shape}: {Shown(found)}");
    }

    [Theory]
    [InlineData("the handler catches another exception", "analysis-not-a-number", "    static int number() {\n        try {\n            return Integer.parseInt(\"twelve\");\n        } catch (NullPointerException problem) {\n            return -1;\n        }\n    }")]
    [InlineData("the handler throws what it caught", "analysis-division-by-zero", "    static int ratio(int total) {\n        try {\n            return total / 0;\n        } catch (ArithmeticException problem) {\n            System.out.println(\"dividing by zero\");\n            throw problem;\n        }\n    }")]
    public async Task JavaFailuresNotCertainlyCaughtAreStillReported(string shape, string check, string body)
    {
        if (await JavaAsync(body) is not { } found) return;

        Assert.True(found.Any(f => f.CheckId == check), $"{shape}: expected {check}, found {Shown(found)}");
    }

    [Theory]
    [InlineData("DivideByZeroException caught as ArithmeticException", "    static int Ratio(int total)\n    {\n        int nothing = 0;\n        try\n        {\n            return total / nothing;\n        }\n        catch (ArithmeticException)\n        {\n            return 0;\n        }\n    }")]
    [InlineData("a bare catch", "    static int Number()\n    {\n        try\n        {\n            return int.Parse(\"twelve\");\n        }\n        catch\n        {\n            return -1;\n        }\n    }")]
    [InlineData("a List index caught as ArgumentOutOfRangeException", "    static string First()\n    {\n        var names = new List<string>();\n        try\n        {\n            return names[0];\n        }\n        catch (ArgumentOutOfRangeException)\n        {\n            return \"none\";\n        }\n    }")]
    [InlineData("a refused argument caught as the ArgumentException above it", "    static void Demo()\n    {\n        try\n        {\n            Percent(120);\n        }\n        catch (ArgumentException)\n        {\n            Console.WriteLine(\"refused\");\n        }\n    }")]
    public async Task CSharpFailuresCaughtOnPurposeAreNotReported(string shape, string body)
    {
        var found = await CSharpAsync(body);

        Assert.True(found.Count == 0, $"{shape}: {Shown(found)}");
    }

    [Theory]
    [InlineData("a catch with a when test", "analysis-division-by-zero", "    static bool Worth(Exception problem) => problem.Message.Length > 100;\n\n    static int Ratio(int total)\n    {\n        int nothing = 0;\n        try\n        {\n            return total / nothing;\n        }\n        catch (DivideByZeroException problem) when (Worth(problem))\n        {\n            return 0;\n        }\n    }")]
    [InlineData("an array index, whose exception is not the List's", "analysis-index-out-of-range", "    static int First()\n    {\n        var empty = new int[0];\n        try\n        {\n            return empty[0];\n        }\n        catch (ArgumentOutOfRangeException)\n        {\n            return -1;\n        }\n    }")]
    [InlineData("a catch that throws it again", "analysis-not-a-number", "    static int Number()\n    {\n        try\n        {\n            return int.Parse(\"twelve\");\n        }\n        catch (FormatException)\n        {\n            Console.WriteLine(\"not a number\");\n            throw;\n        }\n    }")]
    public async Task CSharpFailuresNotCertainlyCaughtAreStillReported(string shape, string check, string body)
    {
        var found = await CSharpAsync(body);

        Assert.True(found.Any(f => f.CheckId == check), $"{shape}: expected {check}, found {Shown(found)}");
    }

    /// <summary>
    /// A test hands its framework's assertion a lambda it expects to raise. Only the calls the assertion does not expect
    /// are reported: one expecting another exception, and one expecting exactly a type above the one raised.
    /// </summary>
    [Fact]
    public async Task JUnitAssertThrowsExpectsItsLambdaToRaise()
    {
        if (JavaFrontend.FindTools() is not { } tools) return;

        var path = await WriteAsync("PercentTest.java", """
            import static org.junit.jupiter.api.Assertions.assertThrows;
            import static org.junit.jupiter.api.Assertions.assertThrowsExactly;

            class PercentTest {
                static int percent(int value) {
                    if (value < 0 || value > 100) {
                        throw new IllegalArgumentException(value + " is not a percentage");
                    }
                    return value;
                }

                void refusesMoreThanAHundred() {
                    assertThrows(IllegalArgumentException.class, () -> percent(120));
                }

                void refusesNegativesAsRuntimeExceptions() {
                    assertThrows(RuntimeException.class, () -> {
                        percent(-1);
                    });
                }

                void expectsAnotherException() {
                    assertThrows(NullPointerException.class, () -> percent(150));
                }

                void expectsExactlyTheTypeAbove() {
                    assertThrowsExactly(RuntimeException.class, () -> percent(160));
                }
            }
            """);
        var found = Mistakes(AbstractChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText()));

        Assert.Equal([23, 27], found.Select(f => f.Span.Line).Order());
    }

    /// <summary>JUnit 4's @Test(expected = ...) expects the whole test method to raise, and the test passes when it does.</summary>
    [Fact]
    public async Task JUnit4ExpectedExceptionIsTheTestPassing()
    {
        if (JavaFrontend.FindTools() is not { } tools) return;

        var path = await WriteAsync("ListTest.java", """
            import java.util.ArrayList;
            import java.util.List;
            import org.junit.Test;

            public class ListTest {
                static int percent(int value) {
                    if (value > 100) {
                        throw new IllegalArgumentException("too big");
                    }
                    return value;
                }

                @Test(expected = IndexOutOfBoundsException.class)
                public void outOfBounds() {
                    List<Object> items = new ArrayList<>();
                    items.get(1);
                }

                @Test(expected = RuntimeException.class)
                public void refusesMoreThanAHundred() {
                    percent(120);
                }

                @Test(expected = NullPointerException.class)
                public void expectsAnotherException() {
                    percent(150);
                }

                @Test
                public void expectsNothing() {
                    percent(160);
                }
            }
            """);
        var program = await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java);
        Assert.Equal(["IndexOutOfBoundsException"], program.AllFunctions.Single(f => f.Name == "outOfBounds").ExpectedToRaise);

        var found = Mistakes(AbstractChecks.Run(program, new SourceText()));
        Assert.Equal([26, 31], found.Select(f => f.Span.Line).Order());
    }

    [Fact]
    public async Task CSharpAssertThrowsExpectsItsLambdaToRaise()
    {
        var path = await WriteAsync("PercentTests.cs", """
            using System;
            using Xunit;

            public static class Percents
            {
                public static int Percent(int value)
                {
                    if (value < 0 || value > 100) throw new ArgumentOutOfRangeException(nameof(value));
                    return value;
                }
            }

            public class PercentTests
            {
                public void RefusesMoreThanAHundred() => Assert.Throws<ArgumentOutOfRangeException>(() => Percents.Percent(120));

                public void RefusesNegativesAsArgumentExceptions() => Assert.ThrowsAny<ArgumentException>(() => Percents.Percent(-1));

                public void RefusesTextThatIsNotANumber() => Assert.Throws<FormatException>(() => int.Parse("twelve"));

                public void ExpectsAnotherException() => Assert.Throws<NullReferenceException>(() => Percents.Percent(150));

                public void ExpectsExactlyTheTypeAbove() => Assert.ThrowsException<ArgumentException>(() => Percents.Percent(160));
            }
            """);
        var found = Mistakes(AbstractChecks.Run(await CSharpFrontend.ReadAsync([path]), new SourceText()));

        Assert.Equal([21, 23], found.Select(f => f.Span.Line).Order());
    }

    /// <summary>Jest's toThrow expects any error, one with the message it is given, or an instance of the class; .not.toThrow expects none.</summary>
    [Fact]
    public async Task JestToThrowExpectsItsArrowToRaise()
    {
        var path = await WriteAsync("percent.test.js", """
            function percent(value) {
              if (value < 0 || value > 100) {
                throw new RangeError(`${value} is not a percentage`);
              }
              return value;
            }

            test('refuses more than a hundred', () => {
              expect(() => percent(120)).toThrow(RangeError);
            });

            test('refuses negatives', () => {
              expect(() => percent(-1)).toThrow('not a percentage');
            });

            test('refuses with an Error', () => {
              expect(() => percent(150)).toThrow(Error);
            });

            test('expects another error', () => {
              expect(() => percent(200)).toThrow(TypeError);
            });

            test('expects no error', () => {
              expect(() => percent(300)).not.toThrow();
            });
            """);
        var found = Mistakes(AbstractChecks.Run(await JavaScriptFrontend.ReadAsync([path]), new SourceText()));

        Assert.Equal([21, 25], found.Select(f => f.Span.Line).Order());
    }
}
