using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Go programs that stop with a nil pointer dereference, each checked the way the window checks a file: the finding for where
/// it stopped says which variable was nil - the one the line reads a field through - the line that gave it nil and why, all
/// of it printed by a copy of the program, and what to do. Each row is what FixFinder gave for the program on this computer
/// when these were written. A computer without Go passes them over.
/// </summary>
/// <remarks>Each row builds its program up to three times - as it is, the copy that prints, and the copy with the
/// check - so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class GoRootCauseLiveTests(ITestOutputHelper output) : IDisposable
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
        new object[] { "main.go", "package main\n\nimport \"fmt\"\n\ntype Member struct {\n\tID  int\n\tAge int\n}\n\nfunc findMember(members []Member, id int) *Member {\n\tfor i := range members {\n\t\tif members[i].ID == id {\n\t\t\treturn &members[i]\n\t\t}\n\t}\n\treturn nil\n}\n\nfunc main() {\n\tmembers := []Member{{ID: 1, Age: 33}}\n\tmember := findMember(members, 2)\n\tfmt.Println(\"Age:\", member.Age)\n}\n", 22, 21, "member is nil on line 22 - it was made nil on line 21",
            "The program stopped on line 22: it reads Age from `member`, but `member` is nil there. `member` became nil on line 21: `member := findMember(members, 2)`. `findMember` gave back nil on line 16: `return nil`. It was called with members = [{ID:1 Age:33}] and id = 2. Line 13, `return &members[i]`, never ran in that call: the if on line 12, `if members[i].ID == id {`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 22 with the same nil pointer dereference.|`member` was nil when line 22 ran.|Line 21 was the last line to set `member` before line 22, and it set it to nil.|`findMember`, called from line 21, ended on line 16, and what it gave back was nil.|Line 13 did not run in that call.",
            "Decide what `findMember` should give back on line 16, and whether its callers expect nil. Or check `member` is not nil before line 22 uses it.", 22, "\tif member != nil {|\t\tfmt.Println(\"Age:\", member.Age)|\t}", "" },
        new object[] { "main.go", "package main\n\nimport \"fmt\"\n\ntype Score struct {\n\tPoints int\n}\n\nfunc main() {\n\tscores := []Score{{Points: 40}, {Points: 55}}\n\tvar best *Score\n\tfor i := range scores {\n\t\tif scores[i].Points > 90 {\n\t\t\tbest = &scores[i]\n\t\t}\n\t}\n\tfmt.Println(\"Best:\", best.Points)\n}\n", 17, 11, "best is nil on line 17 - it was made nil on line 11",
            "The program stopped on line 17: it reads Points from `best`, but `best` is nil there. `best` was set to nil on line 11, `var best *Score`, and nothing changed it after that. Line 14, `best = &scores[i]`, never ran: the if on line 13, `if scores[i].Points > 90 {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 17 with the same nil pointer dereference.|`best` was nil when line 17 ran.|Line 11 was the last line to set `best` before line 17, and it set it to nil.|Line 14 did not run at all.",
            "Give `best` a value for when line 14 does not run, or check `best` is not nil before line 17 uses it.", 17, "\tif best != nil {|\t\tfmt.Println(\"Best:\", best.Points)|\t}", "" },
        new object[] { "main.go", "package main\n\nimport \"fmt\"\n\ntype Item struct {\n\tCount int\n}\n\nfunc main() {\n\tstock := map[string]*Item{\"apples\": {Count: 5}, \"pears\": {Count: 0}}\n\titem := stock[\"plums\"]\n\tfmt.Println(\"Left:\", item.Count+1)\n}\n", 12, 11, "item is nil on line 12 - it was made nil on line 11",
            "The program stopped on line 12: it reads Count from `item`, but `item` is nil there. `item` became nil on line 11: `item := stock[\"plums\"]`. `stock` is a map, and indexing a map with a key it does not have gives the zero value of its values - nil, for pointers.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 12 with the same nil pointer dereference.|`item` was nil when line 12 ran.|Line 11 was the last line to set `item` before line 12, and it set it to nil.",
            "Ask with two values - value, ok := stock[key] - and decide what to do when ok is false, or check `item` is not nil before line 12 uses it.", 12, "\tif item != nil {|\t\tfmt.Println(\"Left:\", item.Count+1)|\t}", "" },
        new object[] { "main.go", "package main\n\nimport (\n\t\"fmt\"\n\t\"strings\"\n)\n\ntype Person struct {\n\tName string\n}\n\nfunc greeting(person *Person) string {\n\tshout := strings.ToUpper(person.Name)\n\treturn \"Hello, \" + shout\n}\n\nfunc choose(people []Person, index int) *Person {\n\tif index < len(people) {\n\t\treturn &people[index]\n\t}\n\treturn nil\n}\n\nfunc main() {\n\tpeople := []Person{{Name: \"ada\"}, {Name: \"grace\"}}\n\tchosen := choose(people, 5)\n\tfmt.Println(greeting(chosen))\n}\n", 13, 27, "person is nil on line 13 - it was made nil on line 27",
            "The program stopped on line 13: it reads Name from `person`, but `person` is nil there. `person` is a parameter of `greeting`, and the call on line 27, `fmt.Println(greeting(chosen))`, gave it nil.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 13 with the same nil pointer dereference.|`person` was nil when line 13 ran.|No line of `greeting` set `person` before line 13: it held what the call on line 27 gave it.",
            "Make the call on line 27 give `greeting` a value for `person`, or check `person` is not nil before line 13 uses it.", 0, "", "" },
        new object[] { "main.go", "package main // the whole program is in just this file\n\nimport \"fmt\"\n\ntype Book struct {\n\tTitle string\n\tPages int\n}\n\nfunc longest(books []Book) *Book {\n\tvar best *Book // none found yet\n\tfor i := range books {\n\t\tif books[i].Pages > 1000 { // only very long books count\n\t\t\tbest = &books[i] // keep the longest so far\n\t\t}\n\t}\n\treturn best\n}\n\nfunc main() {\n\tbooks := []Book{{Title: \"Dune\", Pages: 412}, {Title: \"Emma\", Pages: 474}}\n\tchosen := longest(books) // the longest book\n\tfmt.Println(chosen.Title, chosen.Pages)\n}\n", 23, 22, "chosen is nil on line 23 - it was made nil on line 22",
            "The program stopped on line 23: it reads Title from `chosen`, but `chosen` is nil there. `chosen` became nil on line 22: `chosen := longest(books)`. `longest` gave back nil on line 17: `return best`. It was called with books = [{Title:\"Dune\" Pages:412} {Title:\"Emma\" Pages:474}]. `best` was set to nil on line 11, `var best *Book`, and nothing changed it after that in that call. Line 14, `best = &books[i]`, never ran in that call: the if on line 13, `if books[i].Pages > 1000 {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 23 with the same nil pointer dereference.|`chosen` was nil when line 23 ran.|Line 22 was the last line to set `chosen` before line 23, and it set it to nil.|`longest`, called from line 22, ended on line 17, and what it gave back was nil.|Line 11 was the last line of `longest` to set `best` in that call, and it set it to nil.|Line 14 did not run in that call.",
            "Decide what `longest` should give back on line 17, and whether its callers expect nil. Or check `chosen` is not nil before line 23 uses it.", 23, "\tif chosen != nil {|\t\tfmt.Println(chosen.Title, chosen.Pages)|\t}", "" },
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
            Assert.True(stopped.Fix is null || stopped.Fix.RuleId != "go-nil-guard");
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
