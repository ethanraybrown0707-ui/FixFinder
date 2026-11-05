using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Python programs that stop on None, each checked the way the window checks a file: the finding for where it stopped says
/// which name held None, the line that gave it None and why - all of it recorded by a second run - and what to do. Each row
/// is what FixFinder gave for the program on this computer when these were written. A computer without Python passes them over.
/// </summary>
public class PythonRootCauseLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The file, the program, the line it stopped on, the line the None came from, the finding's title, what it found, why
    /// (split by |), what to do, where the change starts and the lines it writes (0 and empty for none), and the rule of the
    /// finding it follows from (empty for none).
    /// </summary>
    public static IEnumerable<object[]> Programs =>
    [
        new object[] { "members.py", "def find_member(members, wanted):\n    for member in members:\n        if member[\"id\"] == wanted:\n            return member\n    return None\n\n\nmembers = [{\"id\": 1, \"age\": 30}]\nmember = find_member(members, 2)\nprint(\"Age:\", member[\"age\"])\n", 10, 9, "member is None on line 10 - it was made None on line 9",
            "The program stopped on line 10: it picks an item out of `member` with [ ], but `member` is None there. `member` became None on line 9: `member = find_member(members, 2)`. `find_member` gave back None on line 5: `return None`. It was called with members = [{'id': 1, 'age': 30}] and wanted = 2. Line 4, `return member`, never ran in that call: the if on line 3, `if member[\"id\"] == wanted:`, was checked once and was never true.",
            "A second run, recording each line as it ran, stopped on line 10 with the same TypeError.|`member` held None when line 10 ran.|Line 9 was the last line to change `member` before line 10.|`find_member`, called from line 9, ended on line 5 and gave back None.|Line 4 did not run in that call.",
            "Decide what `find_member` should give back on line 5, and whether its callers expect None. Or check `member` is not None before line 10 uses it.", 10, "if member is not None:|    print(\"Age:\", member[\"age\"])", "" },
        new object[] { "fall_through.py", "def find_member(members, wanted):\n    for member in members:\n        if member[\"id\"] == wanted:\n            return member\n\n\nmembers = [{\"id\": 1, \"age\": 30}]\nmember = find_member(members, 2)\nprint(\"Age:\", member[\"age\"])\n", 9, 8, "member is None on line 9 - it was made None on line 8",
            "The program stopped on line 9: it picks an item out of `member` with [ ], but `member` is None there. `member` became None on line 8: `member = find_member(members, 2)`. `find_member` reached its end without a return - the last line it ran was line 2, `for member in members:` - and a function that reaches its end gives back None. It was called with members = [{'id': 1, 'age': 30}] and wanted = 2. Line 4, `return member`, never ran in that call: the if on line 3, `if member[\"id\"] == wanted:`, was checked once and was never true.",
            "A second run, recording each line as it ran, stopped on line 9 with the same TypeError.|`member` held None when line 9 ran.|Line 8 was the last line to change `member` before line 9.|`find_member`, called from line 8, ended on line 2 and gave back None.|Line 4 did not run in that call.",
            "Give `find_member` a return at its end, for when it gets there. Or check `member` is not None before line 9 uses it.", 9, "if member is not None:|    print(\"Age:\", member[\"age\"])", "logic-python-returns-nothing-sometimes" },
        new object[] { "never_set.py", "scores = [40, 55]\nbest = None\nfor score in scores:\n    if score > 60:\n        best = score\nprint(best + 1)\n", 6, 2, "best is None on line 6 - it was made None on line 2",
            "The program stopped on line 6: it uses `best` with +, but `best` is None there. `best` was set to None on line 2, `best = None`, and nothing changed it after that. Line 5, `best = score`, never ran: the if on line 4, `if score > 60:`, was checked twice and was never true.",
            "A second run, recording each line as it ran, stopped on line 6 with the same TypeError.|`best` held None when line 6 ran.|Line 2 was the last line to change `best` before line 6.|Line 5 did not run at all.",
            "Give `best` a value for when line 5 does not run, or check `best` is not None before line 6 uses it.", 6, "if best is not None:|    print(best + 1)", "" },
        new object[] { "dict_get.py", "ages = {\"ann\": 30}\nage = ages.get(\"bob\")\nprint(age + 1)\n", 3, 2, "age is None on line 3 - it was made None on line 2",
            "The program stopped on line 3: it uses `age` with +, but `age` is None there. `age` became None on line 2: `age = ages.get(\"bob\")`. A dictionary's get gives back None for a key it does not have.",
            "A second run, recording each line as it ran, stopped on line 3 with the same TypeError.|`age` held None when line 3 ran.|Line 2 was the last line to change `age` before line 3.",
            "Give get a second value, to give back when the key is not there - or check `age` is not None before line 3 uses it.", 3, "if age is not None:|    print(age + 1)", "" },
        new object[] { "re_match.py", "import re\nmatch = re.match(\"[0-9]+\", \"abc\")\nprint(match.group())\n", 3, 2, "match is None on line 3 - it was made None on line 2",
            "The program stopped on line 3: it looks up group on `match`, but `match` is None there. `match` became None on line 2: `match = re.match(\"[0-9]+\", \"abc\")`. re.match gives back None when the start of the text does not match the pattern.",
            "A second run, recording each line as it ran, stopped on line 3 with the same AttributeError.|`match` held None when line 3 ran.|Line 2 was the last line to change `match` before line 3.",
            "Decide what the program should do when nothing matches, and check `match` is not None before line 3 uses it.", 3, "if match is not None:|    print(match.group())", "" },
        new object[] { "in_function.py", "def lookup(table, key):\n    for k, v in table:\n        if k == key:\n            return v\n\n\ndef report(table):\n    value = lookup(table, \"b\")\n    return value.upper()\n\n\nprint(report([(\"a\", \"x\")]))\n", 9, 8, "value is None on line 9 - it was made None on line 8",
            "The program stopped on line 9: it looks up upper on `value`, but `value` is None there. `value` became None on line 8: `value = lookup(table, \"b\")`. `lookup` reached its end without a return - the last line it ran was line 2, `for k, v in table:` - and a function that reaches its end gives back None. It was called with table = [('a', 'x')] and key = 'b'. Line 4, `return v`, never ran in that call: the if on line 3, `if k == key:`, was checked once and was never true.",
            "A second run, recording each line as it ran, stopped on line 9 with the same AttributeError.|`value` held None when line 9 ran.|Line 8 was the last line to change `value` before line 9.|`lookup`, called from line 8, ended on line 2 and gave back None.|Line 4 did not run in that call.",
            "Give `lookup` a return at its end, for when it gets there. Or check `value` is not None before line 9 uses it.", 0, "", "logic-python-returns-nothing-sometimes" },
        new object[] { "parameter.py", "def show(member):\n    print(member[\"age\"])\n\n\ndef find(members, wanted):\n    for m in members:\n        if m[\"id\"] == wanted:\n            return m\n\n\nshow(find([{\"id\": 1, \"age\": 30}], 2))\n", 2, 11, "member is None on line 2 - it was made None on line 11",
            "The program stopped on line 2: it picks an item out of `member` with [ ], but `member` is None there. `member` is a parameter of `show`, and the call on line 11, `show(find([{\"id\": 1, \"age\": 30}], 2))`, gave it None. `find` reached its end without a return - the last line it ran was line 6, `for m in members:` - and a function that reaches its end gives back None. It was called with members = [{'id': 1, 'age': 30}] and wanted = 2. Line 8, `return m`, never ran in that call: the if on line 7, `if m[\"id\"] == wanted:`, was checked once and was never true.",
            "A second run, recording each line as it ran, stopped on line 2 with the same TypeError.|`member` held None when line 2 ran.|`member` already held None as `show` began, called from line 11.|`find`, called from line 11, ended on line 6 and gave back None.|Line 8 did not run in that call.",
            "Give `find` a return at its end, for when it gets there. Or check `member` is not None before line 2 uses it.", 2, "    if member is not None:|        print(member[\"age\"])", "logic-python-returns-nothing-sometimes" },
        new object[] { "no_return.py", "def average(marks):\n    total = sum(marks)\n    result = total / len(marks)\n\n\nmean = average([50, 70])\nprint(\"Mean: \" + str(mean + 1))\n", 7, 6, "mean is None on line 7 - it was made None on line 6",
            "The program stopped on line 7: it uses `mean` with +, but `mean` is None there. `mean` became None on line 6: `mean = average([50, 70])`. `average` has no return, so every call to it gives back None. It was called with marks = [50, 70].",
            "A second run, recording each line as it ran, stopped on line 7 with the same TypeError.|`mean` held None when line 7 ran.|Line 6 was the last line to change `mean` before line 7.|`average`, called from line 6, ended on line 3 and gave back None.",
            "Give `average` a return for the value it should give back. Or check `mean` is not None before line 7 uses it.", 4, "    return result", "" },
        new object[] { "sorted_result.py", "items = [3, 1, 2]\nitems = items.sort()\nprint(items[0])\n", 3, 2, "items is None on line 3 - it was made None on line 2",
            "The program stopped on line 3: it picks an item out of `items` with [ ], but `items` is None there. `items` became None on line 2: `items = items.sort()`. Before that it held [3, 1, 2], and a list's sort changes the list itself and gives back None.",
            "A second run, recording each line as it ran, stopped on line 3 with the same TypeError.|`items` held None when line 3 ran.|Line 2 was the last line to change `items` before line 3.",
            "Call sort on a line of its own, without keeping what it gives back: `items.sort()`.", 0, "", "logic-python-none-returned-assigned" },
        new object[] { "iterate.py", "def load_marks(name):\n    if name == \"ann\":\n        return [50, 60]\n\n\nmarks = load_marks(\"bob\")\nfor mark in marks:\n    print(mark)\n", 7, 6, "marks is None on line 7 - it was made None on line 6",
            "The program stopped on line 7: it goes through `marks` one item at a time, but `marks` is None there. `marks` became None on line 6: `marks = load_marks(\"bob\")`. `load_marks` reached its end without a return - the last line it ran was line 2, `if name == \"ann\":` - and a function that reaches its end gives back None. It was called with name = 'bob'. Line 3, `return [50, 60]`, never ran in that call: the if on line 2, `if name == \"ann\":`, was checked once and was never true.",
            "A second run, recording each line as it ran, stopped on line 7 with the same TypeError.|`marks` held None when line 7 ran.|Line 6 was the last line to change `marks` before line 7.|`load_marks`, called from line 6, ended on line 2 and gave back None.|Line 3 did not run in that call.",
            "Give `load_marks` a return at its end, for when it gets there. Or check `marks` is not None before line 7 uses it.", 0, "", "logic-python-returns-nothing-sometimes" },
        new object[] { "length.py", "def names_in(group):\n    if group:\n        return group.split(\",\")\n    return None\n\n\nnames = names_in(\"\")\nprint(len(names))\n", 8, 7, "names is None on line 8 - it was made None on line 7",
            "The program stopped on line 8: it asks for len(`names`), but `names` is None there. `names` became None on line 7: `names = names_in(\"\")`. `names_in` gave back None on line 4: `return None`. It was called with group = ''. Line 3, `return group.split(\",\")`, never ran in that call: the if on line 2, `if group:`, was checked once and was never true.",
            "A second run, recording each line as it ran, stopped on line 8 with the same TypeError.|`names` held None when line 8 ran.|Line 7 was the last line to change `names` before line 8.|`names_in`, called from line 7, ended on line 4 and gave back None.|Line 3 did not run in that call.",
            "Decide what `names_in` should give back on line 4, and whether its callers expect None. Or check `names` is not None before line 8 uses it.", 8, "if names is not None:|    print(len(names))", "" },
        new object[] { "concat.py", "name = None\nfor word in [\"\", \" \"]:\n    if word.strip():\n        name = word\nprint(\"Hello \" + name)\n", 5, 1, "name is None on line 5 - it was made None on line 1",
            "The program stopped on line 5: it adds `name` on with +, but `name` is None there. `name` was set to None on line 1, `name = None`, and nothing changed it after that. Line 4, `name = word`, never ran: the if on line 3, `if word.strip():`, was checked twice and was never true.",
            "A second run, recording each line as it ran, stopped on line 5 with the same TypeError.|`name` held None when line 5 ran.|Line 1 was the last line to change `name` before line 5.|Line 4 did not run at all.",
            "Give `name` a value for when line 4 does not run, or check `name` is not None before line 5 uses it.", 5, "if name is not None:|    print(\"Hello \" + name)", "" },
        new object[] { "compare.py", "scores = {\"ann\": 70}\nscore = scores.get(\"bob\")\nif score > 50:\n    print(\"pass\")\n", 3, 2, "score is None on line 3 - it was made None on line 2",
            "The program stopped on line 3: it compares `score` using >, but `score` is None there. `score` became None on line 2: `score = scores.get(\"bob\")`. A dictionary's get gives back None for a key it does not have.",
            "A second run, recording each line as it ran, stopped on line 3 with the same TypeError.|`score` held None when line 3 ran.|Line 2 was the last line to change `score` before line 3.",
            "Give get a second value, to give back when the key is not there - or check `score` is not None before line 3 uses it.", 0, "", "" },
        new object[] { "unpack.py", "def find_bounds(values):\n    if values:\n        return min(values), max(values)\n\n\nbounds = find_bounds([])\nlow, high = bounds\nprint(low, high)\n", 7, 6, "bounds is None on line 7 - it was made None on line 6",
            "The program stopped on line 7: it unpacks `bounds` into separate names, but `bounds` is None there. `bounds` became None on line 6: `bounds = find_bounds([])`. `find_bounds` reached its end without a return - the last line it ran was line 2, `if values:` - and a function that reaches its end gives back None. It was called with values = []. Line 3, `return min(values), max(values)`, never ran in that call: the if on line 2, `if values:`, was checked once and was never true.",
            "A second run, recording each line as it ran, stopped on line 7 with the same TypeError.|`bounds` held None when line 7 ran.|Line 6 was the last line to change `bounds` before line 7.|`find_bounds`, called from line 6, ended on line 2 and gave back None.|Line 3 did not run in that call.",
            "Give `find_bounds` a return at its end, for when it gets there. Or check `bounds` is not None before line 7 uses it.", 0, "", "logic-python-returns-nothing-sometimes" },
        new object[] { "call.py", "handlers = {\"load\": print}\nhandler = handlers.get(\"save\")\nhandler()\n", 3, 2, "handler is None on line 3 - it was made None on line 2",
            "The program stopped on line 3: it calls `handler`, but `handler` is None there. `handler` became None on line 2: `handler = handlers.get(\"save\")`. A dictionary's get gives back None for a key it does not have.",
            "A second run, recording each line as it ran, stopped on line 3 with the same TypeError.|`handler` held None when line 3 ran.|Line 2 was the last line to change `handler` before line 3.",
            "Give get a second value, to give back when the key is not there - or check `handler` is not None before line 3 uses it.", 3, "if handler is not None:|    handler()", "" },
        new object[] { "convert.py", "data = {\"name\": \"ann\"}\nage = data.get(\"age\")\nprint(int(age))\n", 3, 2, "age is None on line 3 - it was made None on line 2",
            "The program stopped on line 3: it turns `age` into a whole number with int(), but `age` is None there. `age` became None on line 2: `age = data.get(\"age\")`. A dictionary's get gives back None for a key it does not have.",
            "A second run, recording each line as it ran, stopped on line 3 with the same TypeError.|`age` held None when line 3 ran.|Line 2 was the last line to change `age` before line 3.",
            "Give get a second value, to give back when the key is not there - or check `age` is not None before line 3 uses it.", 3, "if age is not None:|    print(int(age))", "" },
    ];

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheNoneIsFollowedBackToWhereItCameFrom(
        string fileName, string code, int crashLine, int introducedLine, string title, string found, string evidence, string whatToDo,
        int fixStart, string fixLines, string causedByRule)
    {
        var program = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(program, code);

        var launch = TargetFactory.FromFile(program, LiveAllowance.For(program, TimeSpan.FromMinutes(2)));
        if (!launch.Ok)
        {
            output.WriteLine($"Not run on this computer: {launch.Problem}");
            return;
        }

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()).CheckAsync(launch);
        foreach (var each in report.Findings)
            output.WriteLine($"[{each.RuleId}] {each.Line}: {each.Title} :: {each.Found} :: {each.Fix?.StartLine} {string.Join("|", each.Fix?.NewLines ?? [])}");

        var stopped = report.Findings.SingleOrDefault(finding => finding.RootCause is not null);
        Assert.True(stopped is not null, $"no root cause among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");

        var cause = stopped!.RootCause!;
        Assert.Equal(crashLine, stopped.Line);
        Assert.Equal(crashLine, cause.CrashLine);
        Assert.Equal(introducedLine, cause.IntroducedLine);
        Assert.Equal(title, stopped.Title);
        Assert.Equal(found, stopped.Found);
        Assert.StartsWith(found, stopped.Explanation);
        Assert.Equal(evidence.Split('|'), cause.Evidence);
        Assert.Equal(whatToDo, cause.WhatToDo);

        if (fixStart == 0)
        {
            Assert.True(stopped.Fix is null || stopped.Fix.RuleId != "python-none-guard");
        }
        else
        {
            Assert.NotNull(stopped.Fix);
            Assert.Equal(fixStart, stopped.Fix!.StartLine);
            Assert.Equal(fixLines.Split('|'), stopped.Fix.NewLines);
            Assert.True(stopped.Verified.IsVerified, stopped.Verified.Summary);
        }

        if (causedByRule.Length == 0)
        {
            Assert.Null(stopped.CausedBy);
        }
        else
        {
            var root = report.Findings.Single(finding => finding.Id == stopped.CausedBy?.RootId);
            Assert.Equal(causedByRule, root.RuleId);
            Assert.Equal(RelationKind.ValueMadeThere, stopped.CausedBy!.Kind);
        }

        Assert.Equal(code, File.ReadAllText(program));
    }
}
