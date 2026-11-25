using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// A largest or smallest kept over a loop and started at 0 instead of at the list's first item, in Python and Java: found with
/// the change that starts it at the first item, and left alone where 0 is not a starting best - a total, a count, a depth -
/// or where the best already starts at an item.
/// </summary>
public class RunningBestTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string fileName, string code)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId.EndsWith("best-starts-at-zero", StringComparison.Ordinal)).ToList();
    }

    private static string Java(string body) => "public class Main {\n    static void show(int[] temperatures) {\n" + body + "    }\n}\n";

    [Theory]
    [InlineData("highest.py", "temperatures = [-5, -2, -8]\nhighest = 0\nfor t in temperatures:\n    if t > highest:\n        highest = t\nprint(highest)\n",
        "logic-python-best-starts-at-zero", 2, "highest = temperatures[0]", "largest")]
    [InlineData("lowest.py", "marks = [60, 70, 80]\nlowest = 0\nfor mark in marks:\n    if mark < lowest:\n        lowest = mark\nprint(lowest)\n",
        "logic-python-best-starts-at-zero", 2, "lowest = marks[0]", "smallest")]
    [InlineData("reversed.py", "def warmest(temperatures):\n    best = 0.0\n    for t in temperatures:\n        if best < t:\n            best = t\n    return best\n",
        "logic-python-best-starts-at-zero", 2, "    best = temperatures[0]", "largest")]
    [InlineData("positions.py", "marks = [3, 9, 4]\nbest = 0\nfor i in range(len(marks)):\n    if marks[i] >= best:\n        best = marks[i]\nprint(best)\n",
        "logic-python-best-starts-at-zero", 2, "best = marks[0]", "largest")]
    [InlineData("Main.java", "        int highest = 0;\n        for (int t : temperatures) {\n            if (t > highest) {\n                highest = t;\n            }\n        }\n        System.out.println(highest);\n",
        "logic-java-best-starts-at-zero", 3, "        int highest = temperatures[0];", "largest")]
    [InlineData("Main.java", "        int lowest = 0;\n        for (int i = 0; i < temperatures.length; i++) {\n            if (temperatures[i] < lowest) lowest = temperatures[i];\n        }\n        System.out.println(lowest);\n",
        "logic-java-best-starts-at-zero", 3, "        int lowest = temperatures[0];", "smallest")]
    public void ABestStartedAtZeroIsStartedAtTheFirstItem(string fileName, string code, string check, int line, string startedAtFirstItem, string best)
    {
        var found = Assert.Single(Scan(fileName, fileName.EndsWith(".java", StringComparison.Ordinal) ? Java(code) : code));

        Assert.Equal(check, found.PatternId);
        Assert.Equal(line, found.Line);
        Assert.Contains($"keeps the {best} item", found.Message, StringComparison.Ordinal);
        Assert.Equal([startedAtFirstItem], found.Fix?.NewLines);
    }

    [Theory]
    // A total and a count start at 0 because they add up from nothing.
    [InlineData("total.py", "marks = [60, 70]\ntotal = 0\nfor mark in marks:\n    total += mark\nprint(total)\n")]
    [InlineData("count.py", "marks = [60, 30]\npassed = 0\nfor mark in marks:\n    if mark > 40:\n        passed = passed + 1\nprint(passed)\n")]
    // A depth started at 0 and compared with 0 is a counter, not a best.
    [InlineData("depth.py", "def balanced(text):\n    depth = 0\n    for c in text:\n        if c == '(':\n            depth += 1\n        if depth < 0:\n            return False\n    return depth == 0\n")]
    // Already started at an item.
    [InlineData("first.py", "temperatures = [-5, -2]\nhighest = temperatures[0]\nfor t in temperatures:\n    if t > highest:\n        highest = t\n")]
    // Given another start before the loop.
    [InlineData("given.py", "temperatures = [-5, -2]\nhighest = 0\nhighest = temperatures[0]\nfor t in temperatures:\n    if t > highest:\n        highest = t\n")]
    // Compared with the best but not kept: a count of items above it.
    [InlineData("above.py", "marks = [60, 70]\nbest = 0\nfor mark in marks:\n    if mark > best:\n        print(mark)\n")]
    // A length is never below 0, and len(word) is not the loop's item.
    [InlineData("longest.py", "words = ['a', 'abc']\nlongest = 0\nfor word in words:\n    if len(word) > longest:\n        longest = len(word)\nprint(longest)\n")]
    [InlineData("Main.java", "        int total = 0;\n        for (int t : temperatures) {\n            total += t;\n        }\n        System.out.println(total);\n")]
    [InlineData("Main.java", "        int highest = temperatures[0];\n        for (int t : temperatures) {\n            if (t > highest) {\n                highest = t;\n            }\n        }\n")]
    public void ZeroThatIsNotAStartingBestIsLeftAlone(string fileName, string code)
    {
        Assert.Empty(Scan(fileName, fileName.EndsWith(".java", StringComparison.Ordinal) ? Java(code) : code));
    }
}
