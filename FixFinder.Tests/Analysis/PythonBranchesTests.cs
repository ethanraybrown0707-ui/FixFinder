using FixFinder.Core.Analysis.Dynamic;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Tests;

/// <summary>
/// Which ifs, elifs, elses and whiles around a line of Python decided that it ran, read from the indentation as Python reads
/// it - and which are left out, because what was decided there is not read here.
/// </summary>
public class PythonBranchesTests
{
    private static IReadOnlyList<PythonBranches.Around> Around(string code, int line)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n');
        return PythonBranches.Of(lines, CodeText.MaskAll(lines, Syntax.Python), line);
    }

    private static string[] Said(IReadOnlyList<PythonBranches.Around> around) =>
        around.Select(decision => $"{decision.Line} {decision.Kind} {decision.WasTrue} {decision.Code}").ToArray();

    [Fact]
    public void TheIfALineIsInWasTrue()
    {
        const string code = """
            def grade(score, bands):
                if score >= 50:
                    return bands[score // 10]
                return "fail"
            """;

        Assert.Equal(["2 If True if score >= 50:"], Said(Around(code, 3)));
        Assert.Empty(Around(code, 4));
    }

    [Fact]
    public void AnElseRanBecauseEveryConditionAboveItWasFalse()
    {
        const string code = """
            def describe(temperature, labels):
                if temperature < 0:
                    return labels["freezing"]
                elif temperature < 15:
                    return labels["cold"]
                else:
                    return labels["warm"]
            """;

        Assert.Equal(["2 If False if temperature < 0:", "4 ElseIf False elif temperature < 15:", "6 Else True else:"], Said(Around(code, 7)));
        Assert.Equal(["2 If False if temperature < 0:", "4 ElseIf True elif temperature < 15:"], Said(Around(code, 5)));
    }

    [Fact]
    public void DecisionsAreOutermostFirstAndStopAtTheFunction()
    {
        const string code = """
            if __name__ == "__main__":
                def total_of(prices):
                    total = 0
                    index = 0
                    while index <= len(prices):
                        if prices[index] > 0:
                            total += prices[index]
                        index += 1
                    return total
            """;

        // The if around the def is not one this call decided.
        Assert.Equal(["5 While True while index <= len(prices):", "6 If True if prices[index] > 0:"], Said(Around(code, 7)));
    }

    [Fact]
    public void AStatementWrittenOverSeveralLinesIsReadFromItsFirst()
    {
        const string code = """
            def label(count, names):
                if count > 1:
                    text = ", ".join(
                        names[index] for index in range(count))
                    return text
            """;

        Assert.Equal(["2 If True if count > 1:"], Said(Around(code, 4)));
    }

    [Fact]
    public void WhatIsNotReadHereIsLeftOut()
    {
        // A header written over two lines, and the else of a for loop, which runs when the loop did not break.
        const string code = """
            def first_even(values, limit):
                if (len(values) > 0 and
                        limit > 0):
                    return values[limit]
                for value in values:
                    if value % 2 == 0:
                        break
                else:
                    return values[limit]
            """;

        Assert.Empty(Around(code, 4));
        Assert.Empty(Around(code, 9));
    }

    [Fact]
    public void TheValuesShownAreTheNamesTheConditionReads()
    {
        const string code = """
            def total_of(prices):
                index = 0
                while index <= len(prices):
                    print(prices[index])
            """;
        var lines = code.Replace("\r\n", "\n").Split('\n');
        var masked = CodeText.MaskAll(lines, Syntax.Python);
        var around = PythonBranches.Of(lines, masked, 4);

        // What the call held as it reached line 3, as a run recorded it: len is a function the condition calls, not a name it
        // reads, and nothing is said of a name the run did not record.
        var held = new Dictionary<int, IReadOnlyDictionary<string, string>>
        {
            [3] = new Dictionary<string, string> { ["prices"] = "[3, 4, 5]", ["index"] = "3" },
        };

        var taken = PythonBranches.With(around, held, masked)!;
        Assert.Equal([new HeldValue("index", "3"), new HeldValue("prices", "[3, 4, 5]")], taken.Single().Values);

        // A run that did not record the call reaching the condition says nothing of it.
        Assert.Null(PythonBranches.With(around, new Dictionary<int, IReadOnlyDictionary<string, string>>(), masked));
    }
}
