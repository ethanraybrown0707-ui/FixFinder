using FixFinder.Core.Checking.Guides;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Teaching;

namespace FixFinder.Tests;

/// <summary>
/// Masking - blanking text and comments, so that a check reads only code - on every program FixFinder holds and on lines made
/// at random from quotes, comment marks and backslashes, under every language's rules: it throws nothing, keeps every line,
/// and every line's length, and changes nothing but to a space.
/// </summary>
/// <remarks>
/// Every logic check and many fix rules find a place in the masked line and use it in the line as written, and count lines in
/// the one to point into the other; a mask that dropped or added a character, or a line, would send them to the wrong place.
/// Each program is masked under all nine rules, its own language's and the others' - the properties hold whatever the code.
/// The lines made at random found a line ending straight after '\ stopping OCaml's and F#'s masking with an exception - and
/// with it every check of theirs, on the whole file.
/// </remarks>
public class MaskingTests
{
    /// <summary>Every program FixFinder holds, by a name that says where it is from: lessons, guides and captured programs.</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, string>> EveryProgram = new(() =>
    {
        var programs = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var lesson in Lessons.All)
            for (var number = 1; number <= lesson.Examples.Count; number++)
            {
                var example = lesson.Examples[number - 1];
                var name = $"lesson {lesson.Language.Name} {lesson.Concept.Code} {number}";
                programs[$"{name} broken"] = example.Broken;
                programs[$"{name} put right"] = example.Fixed;
                for (var wrong = 1; wrong <= example.WrongFixes.Count; wrong++) programs[$"{name} wrong fix {wrong}"] = example.WrongFixes[wrong - 1].Code;
            }

        var guideNumber = 0;
        foreach (var guide in Guidebook.Every())
        {
            guideNumber++;
            if (guide.Example.Length > 0) programs[$"guide {guideNumber}"] = guide.Example;
        }

        var captured = Path.Combine(Fixtures.Root, "Captured");
        foreach (var file in Directory.GetFiles(captured, "*", SearchOption.AllDirectories).Where(file => Path.GetFileName(file) != "output.txt").Order(StringComparer.Ordinal))
            programs[$"captured {Path.GetRelativePath(captured, file).Replace('\\', '/')}"] = File.ReadAllText(file);

        foreach (var (name, code) in LogicCheckRobustnessTests.AwkwardCode) programs[$"awkward {name}"] = code;

        return programs;
    });

    public static TheoryData<string> EveryProgramsName()
    {
        var names = new TheoryData<string>();
        foreach (var name in EveryProgram.Value.Keys) names.Add(name);
        return names;
    }

    /// <summary>The seeds of the lines made at random - the same lines on every run, so a failure can be looked at again.</summary>
    public static TheoryData<int> RandomSeeds()
    {
        var seeds = new TheoryData<int>();
        for (var seed = 1; seed <= 500; seed++) seeds.Add(seed);
        return seeds;
    }

    /// <summary>Up to twelve lines of up to 80 characters, mostly the ones masking acts on: quotes, comment marks, brackets, backslashes.</summary>
    private static string RandomLines(int seed)
    {
        const string Characters = "\"'`\\/*#-[]=!%$@(){}<>;:.,ab1 \t\"'/*";
        var random = new Random(seed);
        var lines = Enumerable.Range(0, random.Next(1, 13))
            .Select(_ => new string(Enumerable.Range(0, random.Next(0, 81)).Select(_ => Characters[random.Next(Characters.Length)]).ToArray()));
        return string.Join("\n", lines);
    }

    private static void AssertMaskingKeepsTheCode(string code)
    {
        var lines = code.ReplaceLineEndings("\n").Split('\n');

        foreach (var syntax in Enum.GetValues<Syntax>())
        {
            var masked = CodeText.MaskAll(lines, syntax);

            Assert.Equal(lines.Length, masked.Count);
            for (var index = 0; index < lines.Length; index++)
            {
                var (line, maskedLine) = (lines[index], masked[index]);
                Assert.True(line.Length == maskedLine.Length, $"{syntax}: line {index + 1} is {line.Length} long, masked {maskedLine.Length}");

                var changedToOtherThanASpace = Enumerable.Range(0, line.Length).FirstOrDefault(at => maskedLine[at] != line[at] && maskedLine[at] != ' ', -1);
                if (changedToOtherThanASpace >= 0)
                    Assert.Fail($"{syntax}: line {index + 1} has {maskedLine[changedToOtherThanASpace]} where it had {line[changedToOtherThanASpace]}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryProgramsName))]
    public void MaskingAProgramKeepsItsLinesAndTheirLengthsAndChangesNothingButToSpaces(string name) =>
        AssertMaskingKeepsTheCode(EveryProgram.Value[name]);

    [Theory]
    [MemberData(nameof(RandomSeeds))]
    public void MaskingLinesMadeAtRandomKeepsTheirLinesAndLengthsAndChangesNothingButToSpaces(int seed) =>
        AssertMaskingKeepsTheCode(RandomLines(seed));
}
