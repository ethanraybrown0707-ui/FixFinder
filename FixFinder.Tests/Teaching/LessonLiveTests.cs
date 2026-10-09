using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests.Teaching;

/// <summary>
/// Every program in every lesson, run with the language's own toolchain the way FixFinder runs a program, and held to what
/// the lesson says it does - what it prints, the error it stops with and the line, or the crash. A lesson that says one
/// thing of its code while the code does another fails here, so nothing FixFinder Learn says a program does is made up.
/// A language with nothing on this computer to run it is passed over and said to be, as every live test here does.
/// </summary>
public class LessonLiveTests(ITestOutputHelper output)
{
    /// <summary>One program of one lesson: the concept, the language, which example, and which version of it.</summary>
    public static TheoryData<string, string, int, string> Programs
    {
        get
        {
            var programs = new TheoryData<string, string, int, string>();

            foreach (var lesson in Lessons.All)
            {
                for (var index = 0; index < lesson.Examples.Count; index++)
                {
                    programs.Add(lesson.Concept.Code, lesson.Language.Name, index, "broken");
                    programs.Add(lesson.Concept.Code, lesson.Language.Name, index, "fixed");

                    for (var wrong = 0; wrong < lesson.Examples[index].WrongFixes.Count; wrong++)
                        programs.Add(lesson.Concept.Code, lesson.Language.Name, index, $"wrong fix {wrong + 1}");
                }
            }

            return programs;
        }
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task EachProgramDoesWhatItsLessonSays(string concept, string language, int index, string version)
    {
        var lesson = Lessons.All.Single(each => each.Concept.Code == concept && each.Language.Name == language);
        var example = lesson.Examples[index];

        var (code, expected) = version switch
        {
            "broken" => (example.Broken, example.BrokenDoes),
            "fixed" => (example.Fixed, example.FixedDoes),
            _ => (example.WrongFixes[int.Parse(version["wrong fix ".Length..]) - 1].Code, example.WrongFixes[int.Parse(version["wrong fix ".Length..]) - 1].Does),
        };

        var observed = await SnippetRunner.RunAsync(lesson.Language, code, SnippetRunner.TimeLimitFor(expected));

        if (!observed.Ran)
        {
            output.WriteLine($"Not run on this computer: {observed.WhyNotRun}");
            return;
        }

        output.WriteLine($"lesson says: {expected.Described(language)}");
        output.WriteLine($"running it:  {observed.Said(language)}");
        foreach (var line in observed.Output) output.WriteLine($"  | {line}");

        Assert.True(observed.Shows(expected), $"{concept} in {language}, example {index + 1} ({example.Title}), {version}: " +
            $"the lesson says \"{expected.Described(language)}\", and running it: {observed.Said(language)}");
    }
}
