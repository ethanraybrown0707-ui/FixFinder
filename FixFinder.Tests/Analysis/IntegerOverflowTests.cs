using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;

namespace FixFinder.Tests;

/// <summary>
/// int arithmetic whose known values pass what an int holds, in Java and C#: days * 24 * 60 * 60 * 1000 with days 365 is
/// 31,536,000,000, and Java 21 and .NET 10 both gave 1471228928 - the wrapped value the finding names - even stored in a
/// long. The change makes the arithmetic a long from its first number. A long, an L, a cast to long, a value the code does
/// not show and a result that fits are left alone.
/// </summary>
public class IntegerOverflowTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Analyse(string language, string code)
    {
        if (language == "Java")
        {
            if (JavaFrontend.FindTools() is not { } tools) return null;
            var source = Path.Combine(_temp.Path, "App.java");
            await File.WriteAllTextAsync(source, code);
            return AbstractChecks.Run(await JavaFrontend.ReadAsync([source], tools.Javac, tools.Java), new SourceText());
        }

        var program = Path.Combine(_temp.Path, "Sample.cs");
        await File.WriteAllTextAsync(program, code);
        return AbstractChecks.Run(await CSharpFrontend.ReadAsync([program]), new SourceText());
    }

    private const string JavaYear = "public class App {\n    static long millis() {\n        int days = 365;\n        return days * 24 * 60 * 60 * 1000;\n    }\n}\n";

    private const string CSharpYear = "class Program\n{\n    static long Millis()\n    {\n        int days = 365;\n        long millis = days * 24 * 60 * 60 * 1000;\n        return millis;\n    }\n}\n";

    [Theory]
    [InlineData("Java", JavaYear, 4, "        return (long) days * 24 * 60 * 60 * 1000;")]
    [InlineData("C#", CSharpYear, 6, "        long millis = (long) days * 24 * 60 * 60 * 1000;")]
    public async Task IntArithmeticPastWhatAnIntHoldsIsFoundWithTheValueItWrapsTo(string language, string code, int line, string changed)
    {
        if (await Analyse(language, code) is not { } findings) return;

        var overflow = Assert.Single(findings, f => f.CheckId == "analysis-integer-overflow");
        Assert.Equal(line, overflow.Span.Line);
        Assert.Equal("`days * 24 * 60 * 60 * 1000` is worked out as an int, and 31,536,000 * 1,000 is 31,536,000,000 - past the largest an int holds, " +
                     "2,147,483,647 - so it wraps round to 1,471,228,928, with no error", overflow.Message);
        Assert.Equal(changed, overflow.Fix?.NewLines.Single());
    }

    [Fact]
    public async Task ASumBelowTheSmallestIntIsFoundToo()
    {
        if (await Analyse("C#", "class Program\n{\n    static int Low()\n    {\n        int floor = -2000000000;\n        return floor - 1000000000;\n    }\n}\n") is not { } findings) return;

        var overflow = Assert.Single(findings, f => f.CheckId == "analysis-integer-overflow");
        Assert.Contains("is -3,000,000,000 - past the smallest an int holds, -2,147,483,648 - so it wraps round to 1,294,967,296", overflow.Message);
    }

    [Theory]
    [InlineData("Java", "public class App {\n    static long millis() {\n        long days = 365;\n        return days * 24 * 60 * 60 * 1000;\n    }\n}\n")]
    [InlineData("Java", "public class App {\n    static long millis() {\n        int days = 365;\n        return days * 24L * 60 * 60 * 1000;\n    }\n}\n")]
    [InlineData("Java", "public class App {\n    static long millis(int days) {\n        return days * 24 * 60 * 60 * 1000;\n    }\n}\n")]
    [InlineData("Java", "public class App {\n    static int seconds() {\n        int days = 365;\n        return days * 24 * 60 * 60;\n    }\n}\n")]
    [InlineData("C#", "class Program\n{\n    static long Millis()\n    {\n        int days = 365;\n        return (long)days * 24 * 60 * 60 * 1000;\n    }\n}\n")]
    public async Task ArithmeticThatFitsOrIsALongIsLeftAlone(string language, string code)
    {
        if (await Analyse(language, code) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-integer-overflow");
    }
}
