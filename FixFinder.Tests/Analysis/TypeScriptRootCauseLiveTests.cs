using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// TypeScript programs that stop on undefined or null - run by Node, which takes the types away first - each checked the way the window checks a file: the finding for where it
/// stopped says which variable held it, the line that gave it and why - all of it printed by a copy of the program - and what
/// to do. Each row is what FixFinder gave for the program on this computer when these were written. A computer without Node
/// passes them over.
/// </summary>
public class TypeScriptRootCauseLiveTests(ITestOutputHelper output) : IDisposable
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
        new object[] { "never_set.ts", "const scores: number[] = [40, 55];\nlet best: { score: number } | undefined;\nfor (const score of scores) {\n    if (score > 90) {\n        best = { score: score };\n    }\n}\nconsole.log(best!.score);\n", 8, 2, "best is undefined on line 8 - it was made undefined on line 2",
            "The program stopped on line 8: it reads score from `best`, but `best` is undefined there. `best` was set to undefined on line 2, `let best: { score: number } | undefined;`, and nothing changed it after that. Line 5, `best = { score: score };`, never ran: the if on line 4, `if (score > 90) {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the undefined could come from, stopped on line 8 with the same TypeError.|`best` was undefined when line 8 ran.|Line 2 was the last line to set `best` before line 8, and it set it to undefined.|Line 5 did not run at all.",
            "Give `best` a value for when line 5 does not run, or check `best` is not undefined before line 8 uses it.", 8, "if (best != null) {|    console.log(best!.score);|}", "" },
        new object[] { "return_null.ts", "interface Member {\n    id: number;\n    name: string;\n}\n\nfunction findMember(members: Member[], id: number): Member | null {\n    for (const member of members) {\n        if (member.id === id) {\n            return member;\n        }\n    }\n    return null;\n}\n\nconst members: Member[] = [{ id: 1, name: \"Ada\" }, { id: 2, name: \"Grace\" }];\nconst found = findMember(members, 3);\nconsole.log(found!.name.toUpperCase());\n", 17, 16, "found is null on line 17 - it was made null on line 16",
            "The program stopped on line 17: it reads name from `found`, but `found` is null there. `found` became null on line 16: `const found = findMember(members, 3);`. `findMember` gave back null on line 12: `return null;`. It was called with members = [{\"id\":1,\"name\":\"Ada\"},{\"id\":2,\"name\":\"Grace\"}] and id = 3. Line 9, `return member;`, never ran in that call: the if on line 8, `if (member.id === id) {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 17 with the same TypeError.|`found` was null when line 17 ran.|Line 16 was the last line to set `found` before line 17, and it set it to null.|`findMember`, called from line 16, ended on line 12, and what it gave back was null.|Line 9 did not run in that call.",
            "Decide what `findMember` should give back on line 12, and whether its callers expect null. Or check `found` is not null before line 17 uses it.", 17, "if (found != null) {|    console.log(found!.name.toUpperCase());|}", "" },
        new object[] { "array_find.ts", "type Book = { title: string; year: number };\n\nconst books: Book[] = [{ title: \"Dune\", year: 1965 }, { title: \"Emma\", year: 1815 }];\nconst wanted = books.find((book) => book.year > 2000);\nconsole.log(wanted!.title);\n", 5, 4, "wanted is undefined on line 5 - it was made undefined on line 4",
            "The program stopped on line 5: it reads title from `wanted`, but `wanted` is undefined there. `wanted` became undefined on line 4: `const wanted = books.find((book) => book.year > 2000);`. `books` is an array, and an array's find gives back undefined when no item passes its test.",
            "A copy of the program, printing a line wherever the undefined could come from, stopped on line 5 with the same TypeError.|`wanted` was undefined when line 5 ran.|Line 4 was the last line to set `wanted` before line 5, and it set it to undefined.",
            "Decide what the program should do when no item passes the test, and check `wanted` is not undefined before line 5 uses it.", 5, "if (wanted != null) {|    console.log(wanted!.title);|}", "" },
        new object[] { "map_get.ts", "const stock = new Map<string, { count: number }>();\nstock.set(\"apples\", { count: 4 });\nstock.set(\"pears\", { count: 0 });\n\nconst item = stock.get(\"plums\");\nconsole.log(item!.count + 1);\n", 6, 5, "item is undefined on line 6 - it was made undefined on line 5",
            "The program stopped on line 6: it reads count from `item`, but `item` is undefined there. `item` became undefined on line 5: `const item = stock.get(\"plums\");`. `stock` is a Map, and a Map's get gives back undefined for a key it does not have.",
            "A copy of the program, printing a line wherever the undefined could come from, stopped on line 6 with the same TypeError.|`item` was undefined when line 6 ran.|Line 5 was the last line to set `item` before line 6, and it set it to undefined.",
            "Ask has first, or give a value of your own with ?? - or check `item` is not undefined before line 6 uses it.", 6, "if (item != null) {|    console.log(item!.count + 1);|}", "" },
        new object[] { "parameter.ts", "function greeting(name: string | null): string {\n    const shout: string = name!.toUpperCase();\n    return \"Hello, \" + shout;\n}\n\nfunction chooseName(names: string[], index: number): string | null {\n    if (index < names.length) {\n        return names[index];\n    }\n    return null;\n}\n\nconst names: string[] = [\"ada\", \"grace\"];\nconst chosen = chooseName(names, 5);\nconsole.log(greeting(chosen));\n", 2, 15, "name is null on line 2 - it was made null on line 15",
            "The program stopped on line 2: it reads toUpperCase from `name`, but `name` is null there. `name` is a parameter of `greeting`, and the call on line 15, `console.log(greeting(chosen));`, gave it null.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 2 with the same TypeError.|`name` was null when line 2 ran.|No line of `greeting` set `name` before line 2: it held what the call on line 15 gave it.",
            "Make the call on line 15 give `greeting` a value for `name`, or check `name` is not null before line 2 uses it.", 0, "", "" },
        new object[] { "class_method.ts", "class Library {\n    private shelves: Map<string, string[]> = new Map();\n\n    add(shelf: string, title: string): void {\n        if (!this.shelves.has(shelf)) {\n            this.shelves.set(shelf, []);\n        }\n        this.shelves.get(shelf)!.push(title);\n    }\n\n    firstOn(shelf: string): string | undefined {\n        const titles = this.shelves.get(shelf);\n        if (titles && titles.length > 0) {\n            return titles[0];\n        }\n        return undefined;\n    }\n}\n\nconst library = new Library();\nlibrary.add(\"science\", \"Cosmos\");\nconst first = library.firstOn(\"history\");\nconsole.log(first!.length);\n", 23, 22, "first is undefined on line 23 - it was made undefined on line 22",
            "The program stopped on line 23: it reads length from `first`, but `first` is undefined there. `first` became undefined on line 22: `const first = library.firstOn(\"history\");`. `firstOn` gave back undefined on line 16: `return undefined;`. It was called with shelf = \"history\". Line 14, `return titles[0];`, never ran in that call: the if on line 13, `if (titles && titles.length > 0) {`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the undefined could come from, stopped on line 23 with the same TypeError.|`first` was undefined when line 23 ran.|Line 22 was the last line to set `first` before line 23, and it set it to undefined.|`firstOn`, called from line 22, ended on line 16, and what it gave back was undefined.|Line 14 did not run in that call.",
            "Decide what `firstOn` should give back on line 16, and whether its callers expect undefined. Or check `first` is not undefined before line 23 uses it.", 23, "if (first != null) {|    console.log(first!.length);|}", "" },
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
