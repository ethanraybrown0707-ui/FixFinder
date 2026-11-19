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
/// Inputs to try: one set of answers for each way through a program's first code that what is typed decides - a Python or
/// C# file's top-level code, a Java main - worked out from the code, and the program run with each; and none where the
/// answers could go to the wrong question, or every input does the same. The words each run printed are the runs' own.
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

    private const string JavaGrade =
        "import java.util.Scanner;\n\npublic class Grade {\n    public static void main(String[] args) {\n        Scanner input = new Scanner(System.in);\n        System.out.print(\"Score: \");\n        int score = input.nextInt();\n        if (score >= 70) {\n            System.out.println(\"A\");\n        } else if (score >= 50) {\n            System.out.println(\"B\");\n        } else {\n            System.out.println(\"Fail\");\n        }\n    }\n}\n";

    private const string JavaShare =
        "import java.util.Scanner;\n\npublic class Share {\n    public static void main(String[] args) {\n        Scanner input = new Scanner(System.in);\n        System.out.print(\"Total: \");\n        int total = input.nextInt();\n        System.out.print(\"People: \");\n        int people = input.nextInt();\n        System.out.println(\"Each gets \" + total / people);\n    }\n}\n";

    private const string JavaMarksInALoop =
        "import java.util.Scanner;\n\npublic class Marks {\n    public static void main(String[] args) {\n        Scanner input = new Scanner(System.in);\n        int count = input.nextInt();\n        for (int i = 0; i < count; i++) {\n            int mark = input.nextInt();\n            System.out.println(mark);\n        }\n    }\n}\n";

    private const string CSharpGrade = "Console.Write(\"Score: \");\nint score = int.Parse(Console.ReadLine()!);\nif (score >= 70) Console.WriteLine(\"A\");\nelse if (score >= 50) Console.WriteLine(\"B\");\nelse Console.WriteLine(\"Fail\");\n";

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

    /// <summary>
    /// The answers for each way through a Java program's main, read with a Scanner, and the runs with them - the program
    /// built first, as a check builds it before trying them; null when this computer has no JDK.
    /// </summary>
    private async Task<(IReadOnlyList<IReadOnlyList<string>> Answers, IReadOnlyList<InputTried> Tried)?> JavaAsync(string name, string code)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var file = Write(name, code);
        var answers = InputsToTry.AnswersFor(await JavaFrontend.ReadAsync([file], tools.Javac, tools.Java), file);
        if (answers.Count == 0) return (answers, []);

        var launch = TargetFactory.FromFile(file);
        if (launch.Compile is { } compile) Assert.Equal(RunOutcome.ExitedClean, (await new TargetRunner().RunAsync(compile, CancellationToken.None)).Outcome);
        return (answers, await InputsToTry.TryAsync(launch.Spec!, answers, CancellationToken.None));
    }

    [Fact]
    public async Task AJavaMainReadingWithAScannerGetsAnInputForEachGrade()
    {
        if (await JavaAsync("Grade.java", JavaGrade) is not var (answers, tried)) return;

        Assert.Equal([["70"], ["50"], ["0"]], answers.Select(set => set.ToArray()).ToArray());
        Assert.Equal(["Score: A", "Score: B", "Score: Fail"], tried.Select(each => string.Join("\n", each.Printed)));
        Assert.All(tried, each => Assert.Null(each.StoppedWith));
    }

    [Fact]
    public async Task AJavaDivisionByWhatIsTypedGetsAnInputThatFailsAndOneThatDoesNot()
    {
        if (await JavaAsync("Share.java", JavaShare) is not var (_, tried)) return;

        foreach (var each in tried) output.WriteLine($"typing {each.TypedSaid}: {string.Join(" / ", each.Printed)} {each.StoppedWith}");
        Assert.Equal(2, tried.Count);
        Assert.Single(tried, each => each.StoppedWith == "ArithmeticException: / by zero" && each.Typed[1] == "0");
        Assert.Single(tried, each => each.StoppedWith is null && each.Typed[1] != "0");
    }

    [Fact]
    public async Task NoInputsForAJavaProgramAskingInALoop()
    {
        if (await JavaAsync("Marks.java", JavaMarksInALoop) is not var (answers, _)) return;

        Assert.Empty(answers);
    }

    /// <summary>C#'s top-level statements reading with Console.ReadLine and int.Parse: the answers, and each run - built first - printing its grade.</summary>
    [Fact]
    public async Task CSharpTopLevelCodeReadingALineGetsAnInputForEachGrade()
    {
        if (TargetFactory.FindOnPath("dotnet") is null) return;

        var file = Write("grade.cs", CSharpGrade);
        var answers = InputsToTry.AnswersFor(await CSharpFrontend.ReadAsync([file]), file);
        Assert.Equal([["70"], ["50"], ["0"]], answers.Select(set => set.ToArray()).ToArray());

        var launch = TargetFactory.FromFile(file);
        if (launch.Compile is { } compile) await new TargetRunner().RunAsync(compile, CancellationToken.None);
        var tried = await InputsToTry.TryAsync(launch.Spec!, answers, CancellationToken.None);
        Assert.Equal(["Score: A", "Score: B", "Score: Fail"], tried.Select(each => string.Join("\n", each.Printed)));
    }

    /// <summary>A check of a Java program tries its inputs once it has built - the build and the reading of the code go on side by side.</summary>
    [Fact]
    public async Task ACheckOfAJavaProgramTriesItsInputsOnceItHasBuilt()
    {
        if (JavaFrontend.FindTools() is null) return;

        using var http = new FixFinderHttpClient();
        var launch = TargetFactory.FromFile(Write("Grade.java", JavaGrade));
        var graded = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Java }
            .CheckAsync(launch with { Spec = launch.Spec!.WithInput("65\n") });

        Assert.Equal(["70", "50", "0"], graded.Tried.Select(each => each.TypedSaid));
        Assert.Equal(["Score: A", "Score: B", "Score: Fail"], graded.Tried.Select(each => string.Join("\n", each.Printed)));
    }
}
