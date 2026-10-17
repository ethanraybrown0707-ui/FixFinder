using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A grid made by repeating one row: found where a row is then changed, with each row made its own, and left alone where the
/// rows are their own already or nothing changes them.
/// </summary>
public class SharedRowPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string fileName, string code, string patternId)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == patternId).ToList();
    }

    [Theory]
    [InlineData("main.py", "logic-python-rows-shared", "grid = [[0] * 3] * 3\ngrid[0][0] = 1\n", "grid = [[0] * 3 for _ in range(3)]")]
    [InlineData("main.py", "logic-python-rows-shared", "board = [[]] * size\nboard[1].append(\"X\")\n", "board = [[] for _ in range(size)]")]
    [InlineData("main.js", "logic-js-rows-shared", "const grid = new Array(3).fill(new Array(3).fill(0));\ngrid[0][0] = 1;\n", "const grid = Array.from({ length: 3 }, () => new Array(3).fill(0));")]
    [InlineData("main.ts", "logic-js-rows-shared", "const grid: number[][] = Array(2).fill([]);\ngrid[0].push(5);\n", "const grid: number[][] = Array.from({ length: 2 }, () => []);")]
    [InlineData("main.rb", "logic-ruby-rows-shared", "grid = Array.new(3, Array.new(3, 0))\ngrid[0][0] = 1\n", "grid = Array.new(3) { Array.new(3, 0) }")]
    [InlineData("main.dart", "logic-dart-rows-shared", "  var grid = List.filled(3, List.filled(3, 0));\n  grid[0][0] = 1;\n", "  var grid = List.generate(3, (_) => List.filled(3, 0));")]
    public void AGridOfOneRowWhoseRowIsChangedIsFoundAndEachRowMadeItsOwn(string fileName, string patternId, string code, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code, patternId));
        Assert.Equal(1, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // Each row its own already.
    [InlineData("main.py", "logic-python-rows-shared", "grid = [[0] * 3 for _ in range(3)]\ngrid[0][0] = 1\n")]
    // One row repeated, but only read.
    [InlineData("main.py", "logic-python-rows-shared", "grid = [[0] * 3] * 3\nprint(grid[0][0])\n")]
    // A row of numbers repeated, not a row of rows.
    [InlineData("main.py", "logic-python-rows-shared", "marks = [0] * 3\nmarks[0] = 1\n")]
    [InlineData("main.js", "logic-js-rows-shared", "const grid = Array.from({ length: 3 }, () => new Array(3).fill(0));\ngrid[0][0] = 1;\n")]
    [InlineData("main.rb", "logic-ruby-rows-shared", "grid = Array.new(3) { Array.new(3, 0) }\ngrid[0][0] = 1\n")]
    public void AGridWhoseRowsAreTheirOwnOrUnchangedIsLeftAlone(string fileName, string patternId, string code) =>
        Assert.Empty(Scan(fileName, code, patternId));
}

/// <summary>
/// The mistake and its change run with each language's own toolchain, as FixFinder Learn runs a lesson: the mistake shows the
/// change in every row, and the change in the first alone. A language this computer cannot run is passed over, and said to be.
/// </summary>
public class SharedRowLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string, string, string, int, string, string> Mistakes => new()
    {
        { "Python", "main.py", "logic-python-rows-shared", "grid = [[0] * 3] * 3\ngrid[0][0] = 1\nprint(grid)\n", 1, "[[1, 0, 0], [1, 0, 0], [1, 0, 0]]", "[[1, 0, 0], [0, 0, 0], [0, 0, 0]]" },
        {
            "JavaScript", "main.js", "logic-js-rows-shared", "const grid = new Array(3).fill(new Array(3).fill(0));\ngrid[0][0] = 1;\nconsole.log(JSON.stringify(grid));\n", 1,
            "[[1,0,0],[1,0,0],[1,0,0]]", "[[1,0,0],[0,0,0],[0,0,0]]"
        },
        { "Ruby", "main.rb", "logic-ruby-rows-shared", "grid = Array.new(3, Array.new(3, 0))\ngrid[0][0] = 1\np grid\n", 1, "[[1, 0, 0], [1, 0, 0], [1, 0, 0]]", "[[1, 0, 0], [0, 0, 0], [0, 0, 0]]" },
        {
            "Dart", "main.dart", "logic-dart-rows-shared", "void main() {\n  var grid = List.filled(3, List.filled(3, 0));\n  grid[0][0] = 1;\n  print(grid);\n}\n", 2,
            "[[1, 0, 0], [1, 0, 0], [1, 0, 0]]", "[[1, 0, 0], [0, 0, 0], [0, 0, 0]]"
        },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakeChangesEveryRowAndTheChangeTheFirstAlone(string languageName, string fileName, string patternId, string code, int line, string mistakePrints, string changePrints)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);

        var language = CodeLanguage.All.Single(each => each.Name == languageName);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == patternId);
        Assert.Equal(line, found.Line);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines[found.Fix!.StartLine - 1] = found.Fix.NewLines.Single();
        var changed = string.Join("\n", lines) + "\n";

        var wrong = await SnippetRunner.RunAsync(language, code);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(language, changed);
        output.WriteLine($"the mistake: {wrong.Said(languageName)}\nthe change:  {right.Said(languageName)}");

        Assert.True(wrong.Shows(Behaviour.Printing(mistakePrints)), wrong.Said(languageName));
        Assert.True(right.Shows(Behaviour.Printing(changePrints)), right.Said(languageName));
    }
}
