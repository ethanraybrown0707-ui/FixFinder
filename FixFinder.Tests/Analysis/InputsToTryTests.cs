using FixFinder.Core;
using FixFinder.Core.Analysis.Dynamic;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Inputs to try: one set of answers for each way through a Python program's top-level code that what is typed decides,
/// worked out from the code, and the program run with each - and none where the answers could go to the wrong question,
/// or every input does the same.
/// </summary>
public class InputsToTryTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Grade = "score = int(input(\"Score: \"))\nif score >= 70:\n    print(\"A\")\nelif score >= 50:\n    print(\"B\")\nelse:\n    print(\"Fail\")\n";

    private const string Share = "total = int(input(\"Total: \"))\npeople = int(input(\"People: \"))\nprint(\"Each gets\", total // people)\n";

    private string Write(string name, string code)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, code);
        return path;
    }

    /// <summary>The answers for each way, worked out from the code, or null when this computer has no Python to read it with.</summary>
    private static async Task<IReadOnlyList<IReadOnlyList<string>>?> AnswersAsync(string file) =>
        PythonFrontend.FindInterpreter() is { } python ? InputsToTry.AnswersFor(await PythonFrontend.ReadAsync([file], python), file) : null;

    /// <summary>score &gt;= 70, score &gt;= 50, and neither: the answers are where each way starts, and each run prints its grade.</summary>
    [Fact]
    public async Task EachGradeGetsAnInputAndEachRunPrintsItsGrade()
    {
        var file = Write("grade.py", Grade);
        if (await AnswersAsync(file) is not { } answers) return;

        Assert.Equal([["70"], ["50"], ["0"]], answers.Select(set => set.ToArray()).ToArray());

        var tried = await InputsToTry.TryAsync(TargetFactory.FromFile(file).Spec!, answers, CancellationToken.None);
        Assert.Equal(["Score: A", "Score: B", "Score: Fail"], tried.Select(each => string.Join("\n", each.Printed)));
        Assert.All(tried, each => Assert.Null(each.StoppedWith));
    }

    /// <summary>A line that fails for some answers and not others is a way too: dividing by people, when people is 0 and when it is not.</summary>
    [Fact]
    public async Task ALineThatFailsForSomeAnswersGetsAnInputThatFailsAndOneThatDoesNot()
    {
        var file = Write("share.py", Share);
        if (await AnswersAsync(file) is not { } answers) return;

        var tried = await InputsToTry.TryAsync(TargetFactory.FromFile(file).Spec!, answers, CancellationToken.None);
        foreach (var each in tried) output.WriteLine($"typing {each.TypedSaid}: {string.Join(" / ", each.Printed)} {each.StoppedWith}");

        Assert.Equal(2, tried.Count);
        Assert.Single(tried, each => each.StoppedWith == "ZeroDivisionError: integer division or modulo by zero" && each.Typed[1] == "0");
        Assert.Single(tried, each => each.StoppedWith is null && each.Typed[1] != "0");
    }

    /// <summary>
    /// None where an answer could go to the wrong question - asked in a loop, a branch or a function - nor where nothing
    /// typed decides the way, or nothing is asked.
    /// </summary>
    [Theory]
    [InlineData("count = int(input())\nfor _ in range(count):\n    mark = int(input())\n    print(mark)\n")]
    [InlineData("first = int(input())\nif first > 0:\n    second = int(input())\n    print(first + second)\nelse:\n    print(first)\n")]
    [InlineData("def ask():\n    return int(input())\n\n\nmark = ask()\nif mark > 50:\n    print(\"pass\")\nelse:\n    print(\"fail\")\n")]
    [InlineData("name = input()\nprint(\"Hello\", name)\n")]
    [InlineData("marks = [70, 40]\nif sum(marks) > 100:\n    print(\"high\")\nelse:\n    print(\"low\")\n")]
    public async Task NoInputsWhereAnAnswerCouldGoAstrayOrNothingTypedDecides(string code)
    {
        if (await AnswersAsync(Write("program.py", code)) is not { } answers) return;

        Assert.Empty(answers);
    }

    /// <summary>A check of a program that reads what is typed carries the inputs tried; one of a program that reads nothing carries none.</summary>
    [Fact]
    public async Task ACheckCarriesTheInputsItTried()
    {
        if (PythonFrontend.FindInterpreter() is null) return;

        using var http = new FixFinderHttpClient();
        var graded = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Python }
            .CheckAsync(TargetFactory.FromFile(Write("grade.py", Grade)) is var launch ? launch with { Spec = launch.Spec!.WithInput("65\n") } : launch);
        var plain = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Python }
            .CheckAsync(TargetFactory.FromFile(Write("plain.py", "print(\"hello\")\n")));

        Assert.Equal(["70", "50", "0"], graded.Tried.Select(each => each.TypedSaid));
        Assert.Empty(plain.Tried);
    }
}
