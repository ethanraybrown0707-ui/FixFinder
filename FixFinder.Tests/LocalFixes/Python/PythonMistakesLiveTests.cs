using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Beginner Python programs with one syntax or logic mistake each, checked the way the window checks a file - Python 3.13
/// ran every one when these were written - and the change FixFinder offers: its rule, the line it is about, the lines it
/// replaces and writes, whether a run of the changed copy confirmed it, and the program itself left as it was.
/// </summary>
public class PythonMistakesLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<(CheckReport Report, string Program)> CheckAsync(string source)
    {
        var program = Path.Combine(_temp.Path, "main.py");
        File.WriteAllText(program, source);

        var launch = TargetFactory.FromFile(program, LiveAllowance.For(program, TimeSpan.FromMinutes(2)));
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Python }.CheckAsync(launch);

        output.WriteLine($"syntax: {report.SyntaxSummary}");
        foreach (var finding in report.Findings)
            output.WriteLine($"[{finding.RuleId}] {finding.Line}: {finding.Title} :: {finding.Fix?.StartLine} {string.Join("|", finding.Fix?.NewLines ?? [])} :: {finding.Verified.Summary}");
        return (report, program);
    }

    /// <summary>What was wrong, the program, the rule that fixes it, the line it is found on, where the change starts, how many lines it replaces, the lines it writes (split by |), and whether a run confirmed it.</summary>
    public static TheoryData<string, string, string, int, int, int, string, bool> Mistakes => new()
    {
        // Syntax: Python stops before the first line runs, and the change is confirmed by Python reading the copy.
        { "if without a colon", "mark = 72\nif mark >= 70\n    print('First')\n", "python-expected-colon", 2, 2, 1, "if mark >= 70:", true },
        { "def without a colon", "def double(x)\n    return x * 2\n\n\nprint(double(4))\n", "python-expected-colon", 1, 1, 1, "def double(x):", true },
        { "while without a colon", "count = 0\nwhile count < 3\n    count += 1\nprint(count)\n", "python-expected-colon", 2, 2, 1, "while count < 3:", true },
        { "= in an if", "mark = 72\nif mark = 70:\n    print('seventy')\n", "python-assignment-in-condition", 2, 2, 1, "if mark == 70:", true },
        { "Python 2 print", "name = 'Ada'\nprint 'Hello', name\n", "python-print-statement", 2, 2, 1, "print('Hello', name)", true },
        { "else if", "mark = 55\nif mark >= 70:\n    print('First')\nelse if mark >= 50:\n    print('Pass')\n", "python-else-if", 4, 4, 1, "elif mark >= 50:", true },
        { "bracket left open on its line", "marks = [60, 70]\nprint(sum(marks)\nprint('done')\n", "python-unclosed-bracket", 2, 2, 1, "print(sum(marks))", true },
        { "dictionary left open over lines", "grades = {\n    'Ada': 72,\n    'Alan': 65,\n\nprint(grades)\n", "python-unclosed-bracket", 1, 4, 0, "}", true },
        { "list left open over lines", "marks = [\n    60,\n    70,\n\nprint(marks)\n", "python-unclosed-bracket", 1, 4, 0, "]", true },
        { "items with no commas", "marks = [60 70 80]\nprint(marks)\n", "python-missing-comma", 1, 1, 1, "marks = [60, 70, 80]", true },
        { "arguments with no comma", "name = 'Ada'\nage = 36\nprint(name age)\n", "python-missing-comma", 3, 3, 1, "print(name, age)", true },
        { "string never closed", "print('Hello)\n", "python-unterminated-string", 1, 1, 1, "print('Hello')", true },
        { "body not indented", "def greet(name):\nprint('Hello', name)\n\n\ngreet('Ada')\n", "python-indented-block", 2, 2, 1, "    print('Hello', name)", true },
        { "line indented for no reason", "total = 0\n    total = total + 1\nprint(total)\n", "python-unexpected-indent", 2, 2, 1, "total = total + 1", true },

        // Crashes: the run stops with an error, and the change is confirmed by a run of the copy that does not.
        { "number added to text", "age = 20\nprint('You are ' + age + ' years old')\n", "python-str-concatenation", 2, 2, 1, "print('You are ' + str(age) + ' years old')", true },
        { "loop one past the end", "marks = [60, 70, 80]\nfor i in range(len(marks) + 1):\n    print(marks[i])\n", "python-off-by-one-range", 3, 2, 1, "for i in range(len(marks)):", true },
        { "value worked out but not returned", "def average(marks):\n    total = sum(marks)\n    total / len(marks)\n\n\nresult = average([60, 80])\nprint(result + 1)\n", "python-missing-return", 7, 3, 1, "    return total / len(marks)", true },
        { "key not in the dictionary", "ages = {'Ada': 36}\nprint(ages['Alan'])\n", "python-missing-key-get", 2, 2, 1, "print(ages.get('Alan'))", true },
        { "sort's None kept", "marks = [70, 60]\nmarks = marks.sort()\nprint(marks[0])\n", "logic-python-none-returned-assigned", 2, 2, 1, "marks.sort()", true },

        // Logic: the program runs, or stops somewhere else, and the change is offered from the code.
        { "mutable default", "def add_mark(mark, marks=[]):\n    marks.append(mark)\n    return marks\n\n\nprint(add_mark(60))\nprint(add_mark(70))\n", "logic-python-mutable-default", 1, 1, 1, "def add_mark(mark, marks=None):|    if marks is None:|        marks = []", false },
        { "removed from the list being looped over", "marks = [40, 45, 70, 80]\nfor mark in marks:\n    if mark < 50:\n        marks.remove(mark)\nprint(marks)\n", "logic-python-modified-while-looping", 4, 2, 1, "for mark in list(marks):", false },
        { "return on the first pass either way", "def has_fail(marks):\n    for mark in marks:\n        if mark < 40:\n            return True\n        else:\n            return False\n\n\nprint(has_fail([70, 30]))\n", "logic-python-return-in-loop", 5, 5, 2, "    return False", false },
        { "decimals compared exactly", "total = 0.1 + 0.2\nif total == 0.3:\n    print('equal')\nelse:\n    print('not equal')\n", "logic-python-float-equality", 2, 2, 1, "if abs(total - 0.3) < 1e-9:", false },
        { "list used as a name", "list = [3, 1, 2]\nsorted_marks = list(reversed(list))\nprint(sorted_marks)\n", "logic-python-shadowed-builtin", 1, 1, 2, "items = [3, 1, 2]|sorted_marks = list(reversed(items))", false },
        { "largest started at 0", "temperatures = [-5, -2, -8]\nhighest = 0\nfor t in temperatures:\n    if t > highest:\n        highest = t\nprint(highest)\n", "logic-python-best-starts-at-zero", 2, 2, 1, "highest = temperatures[0]", false },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakeIsFoundWithItsChange(string mistake, string source, string rule, int line, int fixStart, int fixRemoved, string fixLines, bool confirmedByARun)
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var (report, program) = await CheckAsync(source);

        var found = report.Findings.FirstOrDefault(finding => finding.RuleId == rule);
        Assert.True(found is not null, $"{mistake}: no {rule} among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");
        Assert.Equal(line, found!.Line);

        Assert.NotNull(found.Fix);
        Assert.Equal(fixStart, found.Fix!.StartLine);
        Assert.Equal(fixRemoved, found.Fix.RemoveCount);
        Assert.Equal(fixLines.Split('|'), found.Fix.NewLines);
        if (confirmedByARun) Assert.True(found.Verified.IsVerified, $"{mistake}: {found.Verified.Summary}");

        Assert.Equal(source, File.ReadAllText(program));
    }

    /// <summary>
    /// Python's own words, as 3.13 says them, for a mistake that looks like one a rule fixes and is not: a + between the
    /// items is not two items side by side, and the loop that ran past the end is over another list.
    /// </summary>
    [Theory]
    [InlineData("an operator between the items", "a = 1\nb = 2\nc = 3\nprint(a + b c)\n", "python-missing-comma")]
    [InlineData("a loop over another list", "marks = [60, 70, 80]\nnames = ['Ada']\nfor i in range(len(marks) + 1):\n    print(names[i])\n", "python-off-by-one-range")]
    public async Task ALookAlikeIsNotGivenTheChange(string lookAlike, string source, string rule)
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var (report, program) = await CheckAsync(source);

        Assert.True(report.Findings.Count > 0, $"{lookAlike}: nothing found at all");
        Assert.DoesNotContain(report.Findings, finding => finding.RuleId == rule);
        Assert.Equal(source, File.ReadAllText(program));
    }
}
