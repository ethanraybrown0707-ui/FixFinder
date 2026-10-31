using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;

namespace FixFinder.Tests;

/// <summary>
/// int arithmetic whose known values pass what an int holds, in Java, C#, C, C++ and Go: days * 24 * 60 * 60 * 1000 with
/// days 365 is 31,536,000,000, and Java 21, .NET 10, gcc 13.2 and Go 1.27 with days an int32 all gave 1471228928 - the
/// wrapped value the finding names - even stored in a long. The change makes the arithmetic 64 bits from its first number.
/// A long, an L, a cast to long, a value the code does not show and a result that fits are left alone - and so are Go's
/// own int, C's long, which is 32 bits on Windows and 64 elsewhere, and C's unsigned types.
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

        if (language is "C" or "C++")
        {
            var native = Path.Combine(_temp.Path, language == "C" ? "year.c" : "year.cpp");
            await File.WriteAllTextAsync(native, code);
            return AbstractChecks.Run(await CFrontend.ReadAsync([native]), new SourceText());
        }

        if (language == "Go")
        {
            var main = Path.Combine(_temp.Path, "main.go");
            await File.WriteAllTextAsync(main, code);
            return GoFrontend.FindGo(main) is { } go ? AbstractChecks.Run(await GoFrontend.ReadAsync([main], go), new SourceText()) : null;
        }

        var program = Path.Combine(_temp.Path, "Sample.cs");
        await File.WriteAllTextAsync(program, code);
        return AbstractChecks.Run(await CSharpFrontend.ReadAsync([program]), new SourceText());
    }

    private const string CYear = "#include <stdio.h>\n\nint main(void) {\n    int days = 365;\n    long long millis = days * 24 * 60 * 60 * 1000;\n    printf(\"%lld\\n\", millis);\n    return 0;\n}\n";

    private const string GoYear = "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tvar days int32 = 365\n\tmillis := days * 24 * 60 * 60 * 1000\n\tfmt.Println(millis)\n}\n";

    /// <summary>C and C++ leave it undefined, and say so; Go wraps an int32, as it says it does.</summary>
    [Theory]
    [InlineData("C", CYear, 5, "    long long millis = (long long) days * 24 * 60 * 60 * 1000;",
        "- C leaves what happens then undefined, and gcc, building it as FixFinder does, wraps it round to 1,471,228,928, with no error or warning")]
    [InlineData("C++", CYear, 5, "    long long millis = (long long) days * 24 * 60 * 60 * 1000;",
        "- C++ leaves what happens then undefined, and g++, building it as FixFinder does, wraps it round to 1,471,228,928, with no error or warning")]
    [InlineData("Go", GoYear, 7, "\tmillis := int64(days) * 24 * 60 * 60 * 1000",
        "- past the largest an int32 holds, 2,147,483,647 - so it wraps round to 1,471,228,928, with no error")]
    public async Task IntArithmeticPastWhatAnIntHoldsIsFoundInCCppAndGo(string language, string code, int line, string changed, string said)
    {
        if (await Analyse(language, code) is not { } findings) return;

        var overflow = Assert.Single(findings, f => f.CheckId == "analysis-integer-overflow");
        Assert.Equal(line, overflow.Span.Line);
        Assert.EndsWith(said, overflow.Message, StringComparison.Ordinal);
        Assert.Equal(changed, overflow.Fix?.NewLines.Single());
    }

    /// <summary>
    /// Go's own int is 64 bits; a long long, C's long, an unsigned int, a number with a suffix and a result that fits are
    /// left alone - and in Go a number first is not changed, as int64(24) beside an int32 is an error Go refuses to build.
    /// </summary>
    [Theory]
    [InlineData("Go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tdays := 365\n\tfmt.Println(days * 24 * 60 * 60 * 1000)\n}\n")]
    [InlineData("C", "int main(void) {\n    long long days = 365;\n    long long millis = days * 24 * 60 * 60 * 1000;\n    return millis > 0;\n}\n")]
    [InlineData("C", "int main(void) {\n    long days = 365;\n    long long millis = days * 24 * 60 * 60 * 1000;\n    return millis > 0;\n}\n")]
    [InlineData("C", "int main(void) {\n    unsigned int days = 365;\n    unsigned int millis = days * 24 * 60 * 60 * 1000;\n    return millis > 0;\n}\n")]
    [InlineData("C", "int main(void) {\n    int days = 365;\n    long long millis = days * 24LL * 60 * 60 * 1000;\n    return millis > 0;\n}\n")]
    [InlineData("C++", "int main() {\n    int days = 365;\n    int seconds = days * 24 * 60 * 60;\n    return seconds > 0;\n}\n")]
    public async Task ArithmeticThatFitsOrIsWiderIsLeftAloneInCCppAndGo(string language, string code)
    {
        if (await Analyse(language, code) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-integer-overflow");
    }

    [Fact]
    public async Task InGoANumberFirstIsFoundWithNoChange()
    {
        if (await Analyse("Go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tvar days int32 = 365\n\tfmt.Println(1000 * days * 24 * 60 * 60)\n}\n") is not { } findings) return;

        Assert.Null(Assert.Single(findings, f => f.CheckId == "analysis-integer-overflow").Fix);
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
