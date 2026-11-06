using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Rust programs that panic because unwrap met None, each checked the way the window checks a file: the finding for where
/// it stopped says which variable unwrap was called on, the line that gave it None and why - all of it printed by a copy of
/// the program - and what to do. Each row is what FixFinder gave for the program on this computer when these were written.
/// A computer without Rust passes them over.
/// </summary>
/// <remarks>Each row builds its program up to three times - as it is, the copy that prints, and the copy with the
/// check - so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class RustRootCauseLiveTests(ITestOutputHelper output) : IDisposable
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
        new object[] { "main.rs", "struct Member {\n    id: u32,\n    age: u32,\n}\n\nfn find_member(members: &[Member], id: u32) -> Option<&Member> {\n    for member in members {\n        if member.id == id {\n            return Some(member);\n        }\n    }\n    return None;\n}\n\nfn main() {\n    let members = vec![Member { id: 1, age: 30 }];\n    let member = find_member(&members, 2);\n    println!(\"Age: {}\", member.unwrap().age);\n}\n", 18, 17, "member is None on line 18 - it was made None on line 17",
            "The program stopped on line 18: it calls unwrap on `member`, but `member` is None there. `member` became None on line 17: `let member = find_member(&members, 2);`. `find_member` gave back None on line 12: `return None;`. It was called with members = (its type has no Debug) and id = 2. Line 9, `return Some(member);`, never ran in that call: the if on line 8, `if member.id == id {`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the None could come from, stopped on line 18 with the same panic.|`member` was None when line 18 ran.|Line 17 was the last line to set `member` before line 18, and it set it to None.|`find_member`, called from line 17, ended on line 12, and what it gave back was None.|Line 9 did not run in that call.",
            "Decide what `find_member` should give back on line 12, and whether its callers expect None. Or check `member` is not None before line 18 uses it.", 18, "    if member.is_some() {|        println!(\"Age: {}\", member.unwrap().age);|    }", "" },
        new object[] { "main.rs", "fn main() {\n    let scores = vec![40, 55];\n    let mut best: Option<u32> = None;\n    for score in &scores {\n        if *score > 90 {\n            best = Some(*score);\n        }\n    }\n    println!(\"Best: {}\", best.unwrap());\n}\n", 9, 3, "best is None on line 9 - it was made None on line 3",
            "The program stopped on line 9: it calls unwrap on `best`, but `best` is None there. `best` was set to None on line 3, `let mut best: Option<u32> = None;`, and nothing changed it after that. Line 6, `best = Some(*score);`, never ran: the if on line 5, `if *score > 90 {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the None could come from, stopped on line 9 with the same panic.|`best` was None when line 9 ran.|Line 3 was the last line to set `best` before line 9, and it set it to None.|Line 6 did not run at all.",
            "Give `best` a value for when line 6 does not run, or check `best` is not None before line 9 uses it.", 9, "    if best.is_some() {|        println!(\"Best: {}\", best.unwrap());|    }", "" },
        new object[] { "main.rs", "fn longest(titles: &[&str]) -> Option<String> {\n    let mut best = None; // none found yet\n    for title in titles {\n        if title.len() > 20 {\n            best = Some(title.to_string()); // keep the longest so far\n        }\n    }\n    return best;\n}\n\nfn main() {\n    let titles = vec![\"Dune\", \"Emma\", \"Ulysses\"];\n    let chosen = longest(&titles);\n    println!(\"{}\", chosen.unwrap().to_uppercase());\n}\n", 14, 13, "chosen is None on line 14 - it was made None on line 13",
            "The program stopped on line 14: it calls unwrap on `chosen`, but `chosen` is None there. `chosen` became None on line 13: `let chosen = longest(&titles);`. `longest` gave back None on line 8: `return best;`. It was called with titles = [\"Dune\", \"Emma\", \"Ulysses\"]. `best` was set to None on line 2, `let mut best = None;`, and nothing changed it after that in that call. Line 5, `best = Some(title.to_string());`, never ran in that call: the if on line 4, `if title.len() > 20 {`, was checked 3 times and was never true.",
            "A copy of the program, printing a line wherever the None could come from, stopped on line 14 with the same panic.|`chosen` was None when line 14 ran.|Line 13 was the last line to set `chosen` before line 14, and it set it to None.|`longest`, called from line 13, ended on line 8, and what it gave back was None.|Line 2 was the last line of `longest` to set `best` in that call, and it set it to None.|Line 5 did not run in that call.",
            "Decide what `longest` should give back on line 8, and whether its callers expect None. Or check `chosen` is not None before line 14 uses it.", 14, "    if chosen.is_some() {|        println!(\"{}\", chosen.unwrap().to_uppercase());|    }", "" },
        new object[] { "main.rs", "#[derive(Debug)]\nstruct Room {\n    name: String,\n    seats: u32,\n}\n\nfn main() {\n    let rooms = vec![\n        Room { name: String::from(\"A1\"), seats: 20 },\n        Room { name: String::from(\"B2\"), seats: 35 },\n    ];\n    let room = rooms.iter().find(|room| room.seats >= 50);\n    println!(\"Booked {}\", room.unwrap().name);\n}\n", 13, 12, "room is None on line 13 - it was made None on line 12",
            "The program stopped on line 13: it calls unwrap on `room`, but `room` is None there. `room` became None on line 12: `let room = rooms.iter().find(|room| room.seats >= 50);`. find gives back None when no item passes its test.",
            "A copy of the program, printing a line wherever the None could come from, stopped on line 13 with the same panic.|`room` was None when line 13 ran.|Line 12 was the last line to set `room` before line 13, and it set it to None.",
            "Decide what the program should do when there is nothing, and check `room` is not None before line 13 uses it - or match on it, or use if let.", 13, "    if room.is_some() {|        println!(\"Booked {}\", room.unwrap().name);|    }", "" },
        new object[] { "main.rs", "use std::collections::HashMap;\n\nfn main() {\n    let mut stock: HashMap<&str, u32> = HashMap::new();\n    stock.insert(\"apples\", 4);\n    let count = stock.get(\"plums\");\n    println!(\"Left: {}\", count.unwrap() + 1);\n}\n", 7, 6, "count is None on line 7 - it was made None on line 6",
            "The program stopped on line 7: it calls unwrap on `count`, but `count` is None there. `count` became None on line 6: `let count = stock.get(\"plums\");`. get gives back None for an index or a key the collection does not have.",
            "A copy of the program, printing a line wherever the None could come from, stopped on line 7 with the same panic.|`count` was None when line 7 ran.|Line 6 was the last line to set `count` before line 7, and it set it to None.",
            "Decide what the program should do when there is nothing, and check `count` is not None before line 7 uses it - or match on it, or use if let.", 7, "    if count.is_some() {|        println!(\"Left: {}\", count.unwrap() + 1);|    }", "" },
        new object[] { "main.rs", "#[derive(Debug)]\nstruct Item {\n    name: String,\n    price: f64,\n}\n\nfn look_up<'a>(items: &'a [Item], name: &str) -> Option<&'a Item> {\n    for item in items {\n        if item.name == name {\n            return Some(item);\n        }\n    }\n    return None;\n}\n\nfn main() {\n    let items = vec![Item { name: String::from(\"tea\"), price: 2.5 }];\n    println!(\"{:.2}\", look_up(&items, \"milk\").unwrap().price);\n}\n", 18, 18, "look_up(...) is None on line 18",
            "The program stopped on line 18: it calls unwrap on what `look_up` gave back, but `look_up` gave back None. `look_up` gave back None on line 13: `return None;`. It was called with items = [Item { name: \"tea\", price: 2.5 }] and name = \"milk\". Line 10, `return Some(item);`, never ran in that call: the if on line 9, `if item.name == name {`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the None could come from, stopped on line 18 with the same panic.|`look_up`, called from line 18, ended on line 13, and what it gave back was None.|Line 10 did not run in that call.",
            "Decide what `look_up` should give back on line 13, and whether its callers expect None. Or keep what it gives back under a name, and check that is not None before line 18 uses it.", 0, "", "" },
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
            Assert.True(stopped.Fix is null || stopped.Fix.RuleId != "rust-none-guard");
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
