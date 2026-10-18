using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;

namespace FixFinder.Tests;

/// <summary>
/// Every program a lesson shows - the broken one, the one put right, and each change that looks like a fix and is not - read as
/// FixFinder reads code: never taken for another language's, the one put right found to hold no mistake, and each change a
/// check offers on the broken one not found again once it is made.
/// </summary>
/// <remarks>
/// A lesson's programs are run by LessonLiveTests to check what each does; these read them the way the window reads a program,
/// so a check that finds a mistake in a program the lesson says is right - or in its own change - fails here. A style
/// suggestion, such as looping over a list's items rather than its positions, is not a mistake, and a program put right with
/// the smallest change can still draw one.
/// </remarks>
public class LessonProgramTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Broken = "broken";
    private const string PutRight = "put right";
    private const string WrongFix = "wrong fix ";

    /// <summary>Each lesson's programs: the language it teaches, its idea, the example's number in it, and which of its programs.</summary>
    public static TheoryData<string, string, int, string> EveryLessonProgram()
    {
        var programs = new TheoryData<string, string, int, string>();
        foreach (var lesson in Lessons.All)
            for (var number = 1; number <= lesson.Examples.Count; number++)
            {
                programs.Add(lesson.Language.Name, lesson.Concept.Code, number, Broken);
                programs.Add(lesson.Language.Name, lesson.Concept.Code, number, PutRight);
                for (var wrong = 1; wrong <= lesson.Examples[number - 1].WrongFixes.Count; wrong++)
                    programs.Add(lesson.Language.Name, lesson.Concept.Code, number, WrongFix + wrong);
            }
        return programs;
    }

    /// <summary>Each example: the language its lesson teaches, its idea, and its number in the lesson.</summary>
    public static TheoryData<string, string, int> EveryExample()
    {
        var examples = new TheoryData<string, string, int>();
        foreach (var lesson in Lessons.All)
            for (var number = 1; number <= lesson.Examples.Count; number++)
                examples.Add(lesson.Language.Name, lesson.Concept.Code, number);
        return examples;
    }

    private static Lesson LessonOf(string language, string concept) =>
        Lessons.All.Single(lesson => lesson.Language.Name == language && lesson.Concept.Code == concept);

    private static string CodeOf(WorkedExample example, string which) => which switch
    {
        Broken => example.Broken,
        PutRight => example.Fixed,
        _ => example.WrongFixes[int.Parse(which[WrongFix.Length..]) - 1].Code,
    };

    /// <summary>The program saved as the file a person would run it from - Main.java for Java, whose file is named for its class.</summary>
    private SourceFile Saved(Lesson lesson, string code, string name = "main")
    {
        var path = Path.Combine(_temp.Path, (lesson.Language.Name == "Java" ? "Main" : name) + lesson.Language.Extensions[0]);
        File.WriteAllText(path, code.ReplaceLineEndings("\n"));
        return SourceFile.Read(path)!;
    }

    [Theory]
    [MemberData(nameof(EveryLessonProgram))]
    public void ALessonProgramIsNeverTakenForAnotherLanguagesCode(string language, string concept, int example, string which)
    {
        var code = CodeOf(LessonOf(language, concept).Examples[example - 1], which);

        var guessed = PastedCode.LanguageOf(code);

        // Unsure is the person's to settle; a wrong guess would run their code as another language's.
        Assert.True(guessed is null || guessed.Name == language, $"{language} {concept} example {example}, {which}, was taken for {guessed?.Name}");
    }

    [Theory]
    [MemberData(nameof(EveryLessonProgram))]
    public void EveryLogicCheckReadsALessonProgramAndPointsOnlyInsideIt(string language, string concept, int example, string which)
    {
        var lesson = LessonOf(language, concept);

        LogicCheckRobustnessTests.AssertEveryCheckReadsItAndPointsOnlyInsideIt(Saved(lesson, CodeOf(lesson.Examples[example - 1], which)));
    }

    [Theory]
    [MemberData(nameof(EveryExample))]
    public void ALessonsProgramPutRightHoldsNoMistakeAnyCheckFinds(string language, string concept, int example)
    {
        var lesson = LessonOf(language, concept);

        var mistakes = LogicPatterns.Scan(Saved(lesson, lesson.Examples[example - 1].Fixed)).Where(finding => finding.Kind != FindingKind.Style).ToList();

        Assert.True(mistakes.Count == 0, string.Join("; ", mistakes.Select(finding => $"line {finding.Line} {finding.PatternId}: {finding.Message}")));
    }

    [Theory]
    [MemberData(nameof(EveryExample))]
    public void EachChangeACheckOffersOnABrokenLessonProgramIsNotFoundAgainOnceMade(string language, string concept, int example)
    {
        var lesson = LessonOf(language, concept);
        var source = Saved(lesson, lesson.Examples[example - 1].Broken);

        foreach (var finding in LogicPatterns.Scan(source).Where(finding => finding.Fix is not null))
        {
            var changed = finding.Fix!.ApplyTo(source);
            Assert.NotNull(changed);

            var again = LogicPatterns.Scan(Saved(lesson, string.Join("\n", changed) + "\n", "changed"))
                .Where(each => each.PatternId == finding.PatternId && each.Kind != FindingKind.Style)
                .ToList();

            Assert.True(again.Count == 0,
                $"{finding.PatternId}'s change on line {finding.Line} was found again: {string.Join("; ", again.Select(each => $"line {each.Line}: {each.Message}"))}");
        }
    }
}
