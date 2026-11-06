using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// JavaScript programs that stop on undefined or null, each checked the way the window checks a file: the finding for where it
/// stopped says which variable held it, the line that gave it and why - all of it printed by a copy of the program - and what
/// to do. Each row is what FixFinder gave for the program on this computer when these were written. A computer without Node
/// passes them over.
/// </summary>
public class JavaScriptRootCauseLiveTests(ITestOutputHelper output) : IDisposable
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
        new object[] { "members.js", "function findMember(members, wanted) {\n    for (const member of members) {\n        if (member.id === wanted) {\n            return member;\n        }\n    }\n}\n\nconst members = [{ id: 1, age: 30 }];\nconst member = findMember(members, 2);\nconsole.log(\"Age: \" + member.age);\n", 11, 10, "member is undefined on line 11 - it was made undefined on line 10",
            "The program stopped on line 11: it reads age from `member`, but `member` is undefined there. `member` became undefined on line 10: `const member = findMember(members, 2);`. `findMember` reached its end without a return, and a function that does gives back undefined. It was called with members = [{\"id\":1,\"age\":30}] and wanted = 2. Line 4, `return member;`, never ran in that call: the if on line 3, `if (member.id === wanted) {`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the undefined could come from, stopped on line 11 with the same TypeError.|`member` was undefined when line 11 ran.|Line 10 was the last line to set `member` before line 11, and it set it to undefined.|`findMember`, called from line 10, reached its end without running a return.|Line 4 did not run in that call.",
            "Give `findMember` a return at its end, for when it gets there. Or check `member` is not undefined before line 11 uses it.", 11, "if (member != null) {|    console.log(\"Age: \" + member.age);|}", "" },
        new object[] { "no_semicolons.js", "function findMember(members, wanted) {\n    for (const member of members) {\n        if (member.id === wanted) {\n            return member\n        }\n    }\n}\n\nconst members = [{ id: 1, age: 30 }]\nconst member = findMember(members, 2)\nconsole.log(\"Age: \" + member.age)\n", 11, 10, "member is undefined on line 11 - it was made undefined on line 10",
            "The program stopped on line 11: it reads age from `member`, but `member` is undefined there. `member` became undefined on line 10: `const member = findMember(members, 2)`. `findMember` reached its end without a return, and a function that does gives back undefined. It was called with members = [{\"id\":1,\"age\":30}] and wanted = 2. Line 4, `return member`, never ran in that call: the if on line 3, `if (member.id === wanted) {`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the undefined could come from, stopped on line 11 with the same TypeError.|`member` was undefined when line 11 ran.|Line 10 was the last line to set `member` before line 11, and it set it to undefined.|`findMember`, called from line 10, reached its end without running a return.|Line 4 did not run in that call.",
            "Give `findMember` a return at its end, for when it gets there. Or check `member` is not undefined before line 11 uses it.", 11, "if (member != null) {|    console.log(\"Age: \" + member.age)|}", "" },
        new object[] { "never_set.js", "const scores = [40, 55];\nlet best;\nfor (const score of scores) {\n    if (score > 60) {\n        best = \"score \" + score;\n    }\n}\nconsole.log(best.length);\n", 8, 2, "best is undefined on line 8 - it was made undefined on line 2",
            "The program stopped on line 8: it reads length from `best`, but `best` is undefined there. `best` was set to undefined on line 2, `let best;`, and nothing changed it after that. Line 5, `best = \"score \" + score;`, never ran: the if on line 4, `if (score > 60) {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the undefined could come from, stopped on line 8 with the same TypeError.|`best` was undefined when line 8 ran.|Line 2 was the last line to set `best` before line 8, and it set it to undefined.|Line 5 did not run at all.",
            "Give `best` a value for when line 5 does not run, or check `best` is not undefined before line 8 uses it.", 8, "if (best != null) {|    console.log(best.length);|}", "" },
        new object[] { "return_null.js", "function label(mark) {\n    if (mark >= 70) {\n        return \"first\";\n    }\n    if (mark >= 40) {\n        return \"pass\";\n    }\n    return null;\n}\n\nconst said = label(20);\nconsole.log(said.toUpperCase());\n", 12, 11, "said is null on line 12 - it was made null on line 11",
            "The program stopped on line 12: it reads toUpperCase from `said`, but `said` is null there. `said` became null on line 11: `const said = label(20);`. `label` gave back null on line 8: `return null;`. It was called with mark = 20. Line 3, `return \"first\";`, never ran in that call: the if on line 2, `if (mark >= 70) {`, was checked once and was never true. Line 6, `return \"pass\";`, never ran in that call: the if on line 5, `if (mark >= 40) {`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 12 with the same TypeError.|`said` was null when line 12 ran.|Line 11 was the last line to set `said` before line 12, and it set it to null.|`label`, called from line 11, ended on line 8, and what it gave back was null.|Line 3 did not run in that call.|Line 6 did not run in that call.",
            "Decide what `label` should give back on line 8, and whether its callers expect null. Or check `said` is not null before line 12 uses it.", 12, "if (said != null) {|    console.log(said.toUpperCase());|}", "" },
        new object[] { "array_find.js", "const members = [{ id: 1, age: 30 }];\nconst member = members.find((each) => each.id === 2);\nconsole.log(\"Age: \" + member.age);\n", 3, 2, "member is undefined on line 3 - it was made undefined on line 2",
            "The program stopped on line 3: it reads age from `member`, but `member` is undefined there. `member` became undefined on line 2: `const member = members.find((each) => each.id === 2);`. `members` is an array, and an array's find gives back undefined when no item passes its test.",
            "A copy of the program, printing a line wherever the undefined could come from, stopped on line 3 with the same TypeError.|`member` was undefined when line 3 ran.|Line 2 was the last line to set `member` before line 3, and it set it to undefined.",
            "Decide what the program should do when no item passes the test, and check `member` is not undefined before line 3 uses it.", 3, "if (member != null) {|    console.log(\"Age: \" + member.age);|}", "" },
        new object[] { "map_get.js", "const ages = new Map([[\"ann\", 30]]);\nconst age = ages.get(\"bob\");\nconsole.log(age.toFixed(1));\n", 3, 2, "age is undefined on line 3 - it was made undefined on line 2",
            "The program stopped on line 3: it reads toFixed from `age`, but `age` is undefined there. `age` became undefined on line 2: `const age = ages.get(\"bob\");`. `ages` is a Map, and a Map's get gives back undefined for a key it does not have.",
            "A copy of the program, printing a line wherever the undefined could come from, stopped on line 3 with the same TypeError.|`age` was undefined when line 3 ran.|Line 2 was the last line to set `age` before line 3, and it set it to undefined.",
            "Ask has first, or give a value of your own with ?? - or check `age` is not undefined before line 3 uses it.", 3, "if (age != null) {|    console.log(age.toFixed(1));|}", "" },
        new object[] { "parameter.js", "function show(name) {\n    console.log(name.toUpperCase());\n}\n\nfunction find(names, start) {\n    for (const name of names) {\n        if (name.startsWith(start)) {\n            return name;\n        }\n    }\n    return null;\n}\n\nshow(find([\"ann\", \"bob\"], \"c\"));\n", 2, 14, "name is null on line 2 - it was made null on line 14",
            "The program stopped on line 2: it reads toUpperCase from `name`, but `name` is null there. `name` is a parameter of `show`, and the call on line 14, `show(find([\"ann\", \"bob\"], \"c\"));`, gave it null. `find` gave back null on line 11: `return null;`. It was called with names = [\"ann\",\"bob\"] and start = \"c\". Line 8, `return name;`, never ran in that call: the if on line 7, `if (name.startsWith(start)) {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 2 with the same TypeError.|`name` was null when line 2 ran.|No line of `show` set `name` before line 2: it held what the call on line 14 gave it.|`find`, called from line 14, ended on line 11, and what it gave back was null.|Line 8 did not run in that call.",
            "Decide what `find` should give back on line 11, and whether its callers expect null. Or check `name` is not null before line 2 uses it.", 2, "    if (name != null) {|        console.log(name.toUpperCase());|    }", "" },
        new object[] { "call_result.js", "function find(names, start) {\n    for (const name of names) {\n        if (name.startsWith(start)) {\n            return name;\n        }\n    }\n}\n\nconsole.log(find([\"ann\", \"bob\"], \"c\").length);\n", 9, 9, "find(...) is undefined on line 9",
            "The program stopped on line 9: it reads length from what `find` gave back, but `find` gave back undefined. `find` reached its end without a return, and a function that does gives back undefined. It was called with names = [\"ann\",\"bob\"] and start = \"c\". Line 4, `return name;`, never ran in that call: the if on line 3, `if (name.startsWith(start)) {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the undefined could come from, stopped on line 9 with the same TypeError.|`find`, called from line 9, reached its end without running a return.|Line 4 did not run in that call.",
            "Give `find` a return at its end, for when it gets there. Or keep what it gives back under a name, and check that is not undefined before line 9 uses it.", 0, "", "" },
        new object[] { "not_iterable.js", "function loadMarks(name) {\n    if (name === \"ann\") {\n        return [50, 60];\n    }\n}\n\nconst marks = loadMarks(\"bob\");\nfor (const mark of marks) {\n    console.log(mark);\n}\n", 8, 7, "marks is undefined on line 8 - it was made undefined on line 7",
            "The program stopped on line 8: it goes through `marks` one item at a time, but `marks` is undefined there. `marks` became undefined on line 7: `const marks = loadMarks(\"bob\");`. `loadMarks` reached its end without a return, and a function that does gives back undefined. It was called with name = \"bob\". Line 3, `return [50, 60];`, never ran in that call: the if on line 2, `if (name === \"ann\") {`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the undefined could come from, stopped on line 8 with the same TypeError.|`marks` was undefined when line 8 ran.|Line 7 was the last line to set `marks` before line 8, and it set it to undefined.|`loadMarks`, called from line 7, reached its end without running a return.|Line 3 did not run in that call.",
            "Give `loadMarks` a return at its end, for when it gets there. Or check `marks` is not undefined before line 8 uses it.", 0, "", "" },
        new object[] { "destructure.js", "const people = { ann: { age: 30 } };\nconst person = people[\"bob\"];\nconst { age } = person;\nconsole.log(age);\n", 3, 2, "person is undefined on line 3 - it was made undefined on line 2",
            "The program stopped on line 3: it takes age out of `person`, but `person` is undefined there. `person` became undefined on line 2: `const person = people[\"bob\"];`.",
            "A copy of the program, printing a line wherever the undefined could come from, stopped on line 3 with the same TypeError.|`person` was undefined when line 3 ran.|Line 2 was the last line to set `person` before line 3, and it set it to undefined.",
            "Decide what the program should do when `person` is undefined: check `person` is not undefined before line 3 uses it.", 0, "", "" },
        new object[] { "set_property.js", "function findMember(members, wanted) {\n    for (const member of members) {\n        if (member.id === wanted) {\n            return member;\n        }\n    }\n}\n\nconst member = findMember([{ id: 1, age: 30 }], 2);\nmember.age = 31;\n", 10, 9, "member is undefined on line 10 - it was made undefined on line 9",
            "The program stopped on line 10: it sets age on `member`, but `member` is undefined there. `member` became undefined on line 9: `const member = findMember([{ id: 1, age: 30 }], 2);`. `findMember` reached its end without a return, and a function that does gives back undefined. It was called with members = [{\"id\":1,\"age\":30}] and wanted = 2. Line 4, `return member;`, never ran in that call: the if on line 3, `if (member.id === wanted) {`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the undefined could come from, stopped on line 10 with the same TypeError.|`member` was undefined when line 10 ran.|Line 9 was the last line to set `member` before line 10, and it set it to undefined.|`findMember`, called from line 9, reached its end without running a return.|Line 4 did not run in that call.",
            "Give `findMember` a return at its end, for when it gets there. Or check `member` is not undefined before line 10 uses it.", 10, "if (member != null) {|    member.age = 31;|}", "" },
    ];

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheMissingValueIsFollowedBackToWhereItCameFrom(
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
            Assert.True(stopped.Fix is null || stopped.Fix.RuleId != "js-missing-value-guard");
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
