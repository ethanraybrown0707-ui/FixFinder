using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// C# programs that stop with a NullReferenceException, each checked the way the window checks a file: the finding for where
/// it stopped says which variable was null - the one the line reads from - the line that gave it null and why, all of it
/// printed by a copy of the program, and what to do. Each row is what FixFinder gave for the program on this computer when
/// these were written. A computer without the .NET SDK passes them over.
/// </summary>
public class CSharpRootCauseLiveTests(ITestOutputHelper output) : IDisposable
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
        new object[] { "Program.cs", "var members = new List<Member> { new Member(1, 30) };\nvar member = FindMember(members, 2);\nConsole.WriteLine(\"Age: \" + member.Age);\n\nstatic Member? FindMember(List<Member> members, int wanted)\n{\n    foreach (var member in members)\n    {\n        if (member.Id == wanted)\n        {\n            return member;\n        }\n    }\n    return null;\n}\n\nrecord Member(int Id, int Age);\n", 3, 2, "member is null on line 3 - it was made null on line 2",
            "The program stopped on line 3: it reads Age from `member`, but `member` is null there. `member` became null on line 2: `var member = FindMember(members, 2);`. `FindMember` gave back null on line 14: `return null;`. It was called with members = [Member { Id = 1, Age = 30 }] and wanted = 2. Line 11, `return member;`, never ran in that call: the if on line 9, `if (member.Id == wanted)`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 3 with the same NullReferenceException.|`member` was null when line 3 ran.|Line 2 was the last line to set `member` before line 3, and it set it to null.|`FindMember`, called from line 2, ended on line 14, and what it gave back was null.|Line 11 did not run in that call.",
            "Decide what `FindMember` should give back on line 14, and whether its callers expect null. Or check `member` is not null before line 3 uses it.", 3, "if (member != null) {|    Console.WriteLine(\"Age: \" + member.Age);|}", "" },
        new object[] { "Program.cs", "int[] scores = { 40, 55 };\nstring? best = null;\nforeach (var score in scores)\n{\n    if (score > 60)\n    {\n        best = \"score \" + score;\n    }\n}\nConsole.WriteLine(best.Length);\n", 10, 2, "best is null on line 10 - it was made null on line 2",
            "The program stopped on line 10: it reads Length from `best`, but `best` is null there. `best` was set to null on line 2, `string? best = null;`, and nothing changed it after that. Line 7, `best = \"score \" + score;`, never ran: the if on line 5, `if (score > 60)`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 10 with the same NullReferenceException.|`best` was null when line 10 ran.|Line 2 was the last line to set `best` before line 10, and it set it to null.|Line 7 did not run at all.",
            "Give `best` an object for when line 7 does not run, or check `best` is not null before line 10 uses it.", 10, "if (best != null) {|    Console.WriteLine(best.Length);|}", "" },
        new object[] { "Program.cs", "Show(Find(new List<string> { \"ann\", \"bob\" }, \"c\"));\n\nstatic void Show(string name)\n{\n    Console.WriteLine(name.ToUpper());\n}\n\nstatic string? Find(List<string> names, string start)\n{\n    foreach (var name in names)\n    {\n        if (name.StartsWith(start))\n        {\n            return name;\n        }\n    }\n    return null;\n}\n", 5, 1, "name is null on line 5 - it was made null on line 1",
            "The program stopped on line 5: it calls ToUpper on `name`, but `name` is null there. `name` is a parameter of `Show`, and the call on line 1, `Show(Find(new List<string> { \"ann\", \"bob\" }, \"c\"));`, gave it null. `Find` gave back null on line 17: `return null;`. It was called with names = [\"ann\", \"bob\"] and start = \"c\". Line 14, `return name;`, never ran in that call: the if on line 12, `if (name.StartsWith(start))`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 5 with the same NullReferenceException.|`name` was null when line 5 ran.|No line of `Show` set `name` before line 5: it held what the call on line 1 gave it.|`Find`, called from line 1, ended on line 17, and what it gave back was null.|Line 14 did not run in that call.",
            "Decide what `Find` should give back on line 17, and whether its callers expect null. Or check `name` is not null before line 5 uses it.", 5, "    if (name != null) {|        Console.WriteLine(name.ToUpper());|    }", "" },
        new object[] { "Program.cs", "var members = new List<Member> { new Member(1, 30) };\nvar member = members.FirstOrDefault(each => each.Id == 2);\nConsole.WriteLine(\"Age: \" + member.Age);\n\nrecord Member(int Id, int Age);\n", 3, 2, "member is null on line 3 - it was made null on line 2",
            "The program stopped on line 3: it reads Age from `member`, but `member` is null there. `member` became null on line 2: `var member = members.FirstOrDefault(each => each.Id == 2);`. FirstOrDefault gives back the default when nothing is found - null, for a list or dictionary of objects.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 3 with the same NullReferenceException.|`member` was null when line 3 ran.|Line 2 was the last line to set `member` before line 3, and it set it to null.",
            "Decide what the program should do when nothing is found, and check `member` is not null before line 3 uses it.", 3, "if (member != null) {|    Console.WriteLine(\"Age: \" + member.Age);|}", "" },
        new object[] { "Program.cs", "var shelf = new Shelf();\nshelf.Add(\"Cosmos\");\nshelf.Add(\"Dune\");\nvar chosen = shelf.Longest();\nConsole.WriteLine(chosen.ToUpper());\n\nclass Shelf\n{\n    private readonly List<string> titles = new();\n\n    public void Add(string title)\n    {\n        titles.Add(title);\n    }\n\n    public string? Longest()\n    {\n        foreach (var title in titles)\n        {\n            if (title.Length > 20)\n            {\n                return title;\n            }\n        }\n        return null;\n    }\n}\n", 5, 4, "chosen is null on line 5 - it was made null on line 4",
            "The program stopped on line 5: it calls ToUpper on `chosen`, but `chosen` is null there. `chosen` became null on line 4: `var chosen = shelf.Longest();`. `Longest` gave back null on line 25: `return null;`. Line 22, `return title;`, never ran in that call: the if on line 20, `if (title.Length > 20)`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 5 with the same NullReferenceException.|`chosen` was null when line 5 ran.|Line 4 was the last line to set `chosen` before line 5, and it set it to null.|`Longest`, called from line 4, ended on line 25, and what it gave back was null.|Line 22 did not run in that call.",
            "Decide what `Longest` should give back on line 25, and whether its callers expect null. Or check `chosen` is not null before line 5 uses it.", 5, "if (chosen != null) {|    Console.WriteLine(chosen.ToUpper());|}", "" },
    ];

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheNullIsFollowedBackToWhereItCameFrom(
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
            Assert.True(stopped.Fix is null || stopped.Fix.RuleId != "csharp-null-guard");
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
