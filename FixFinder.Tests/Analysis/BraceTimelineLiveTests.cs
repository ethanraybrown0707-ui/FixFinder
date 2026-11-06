using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// What each variable of a Java, JavaScript, TypeScript, C# or Go program held where it stopped on a missing value, line by
/// line - printed by the same copy of the program that follows the missing value back. Each row is what FixFinder gave for
/// the program on this computer when these were written: every variable, every value it was given and the line - or the
/// call - that gave it. A computer without the language passes them over.
/// </summary>
/// <remarks>Each row builds its program up to three times - as it is, the copy that prints, and the copy with the
/// check - so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class BraceTimelineLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The file, the program, the line it stopped on, and each variable's values: "name: line 2 = null", split by |.</summary>
    public static TheoryData<string, string, int, string> Programs => new()
    {
        { "Best.java", "public class Best {\n    public static void main(String[] args) {\n        int[] scores = {40, 55};\n        String best = null;\n        for (int score : scores) {\n            if (score > 60) {\n                best = \"score \" + score;\n            }\n        }\n        System.out.println(best.length());\n    }\n}\n", 10,
            "args: the call = []|scores: line 3 = [40, 55]|best: line 4 = null" },
        { "Report.java", "public class Report {\n    static String label(int mark) {\n        if (mark >= 70) {\n            return \"first\";\n        }\n        if (mark >= 40) {\n            return \"pass\";\n        }\n        return null;\n    }\n\n    static int labelLength(int mark) {\n        String said = label(mark);\n        return said.length();\n    }\n\n    public static void main(String[] args) {\n        System.out.println(labelLength(20));\n    }\n}\n", 14,
            "mark: the call on line 18 = 20|said: line 13 = null" },
        { "never_set.js", "const scores = [40, 55];\nlet best;\nfor (const score of scores) {\n    if (score > 60) {\n        best = \"score \" + score;\n    }\n}\nconsole.log(best.length);\n", 8,
            "scores: line 1 = [40,55]|best: line 2 = undefined" },
        { "parameter.js", "function show(name) {\n    console.log(name.toUpperCase());\n}\n\nfunction find(names, start) {\n    for (const name of names) {\n        if (name.startsWith(start)) {\n            return name;\n        }\n    }\n    return null;\n}\n\nshow(find([\"ann\", \"bob\"], \"c\"));\n", 2,
            "name: the call on line 14 = null" },
        { "Program.cs", "int[] scores = { 40, 55 };\nstring? best = null;\nforeach (var score in scores)\n{\n    if (score > 60)\n    {\n        best = \"score \" + score;\n    }\n}\nConsole.WriteLine(best.Length);\n", 10,
            "scores: line 1 = [40, 55]|best: line 2 = null" },
        { "Program.cs", "var members = new List<Member> { new Member(1, 30) };\nvar member = FindMember(members, 2);\nConsole.WriteLine(\"Age: \" + member.Age);\n\nstatic Member? FindMember(List<Member> members, int wanted)\n{\n    foreach (var member in members)\n    {\n        if (member.Id == wanted)\n        {\n            return member;\n        }\n    }\n    return null;\n}\n\nrecord Member(int Id, int Age);\n", 3,
            "members: line 1 = [Member { Id = 1, Age = 30 }]|member: line 2 = null" },
        { "map_items.js", "const prices = new Map([[\"tea\", 2], [\"cake\", 3]]);\nconst basket = new Set([\"tea\", \"milk\"]);\nlet total = 0;\nfor (const item of basket) {\n    const price = prices.get(item);\n    total = total + price.valueOf();\n}\nconsole.log(total);\n", 6,
            "prices: line 1 = {\"tea\":2,\"cake\":3}|basket: line 2 = [\"tea\",\"milk\"]|total: line 3 = 0; line 6 = 2|price: line 5 = 2; line 5 = undefined" },
        { "class_method.ts", "class Library {\n    private shelves: Map<string, string[]> = new Map();\n\n    add(shelf: string, title: string): void {\n        if (!this.shelves.has(shelf)) {\n            this.shelves.set(shelf, []);\n        }\n        this.shelves.get(shelf)!.push(title);\n    }\n\n    firstOn(shelf: string): string | undefined {\n        const titles = this.shelves.get(shelf);\n        if (titles && titles.length > 0) {\n            return titles[0];\n        }\n        return undefined;\n    }\n}\n\nconst library = new Library();\nlibrary.add(\"science\", \"Cosmos\");\nconst first = library.firstOn(\"history\");\nconsole.log(first!.length);\n", 23,
            "library: line 20 = {\"shelves\":{}}|first: line 22 = undefined" },
        { "main.go", "package main\n\nimport \"fmt\"\n\ntype Item struct {\n\tCount int\n}\n\nfunc main() {\n\tstock := map[string]*Item{\"apples\": {Count: 5}, \"pears\": {Count: 0}}\n\titem := stock[\"plums\"]\n\tfmt.Println(\"Left:\", item.Count+1)\n}\n", 12,
            "stock: line 10 = map[\"apples\":&{Count:5} \"pears\":&{Count:0}]|item: line 11 = nil" },
        { "main.go", "package main\n\nimport \"fmt\"\n\ntype Score struct {\n\tPoints int\n}\n\nfunc main() {\n\tscores := []Score{{Points: 40}, {Points: 55}}\n\tvar best *Score\n\tfor i := range scores {\n\t\tif scores[i].Points > 90 {\n\t\t\tbest = &scores[i]\n\t\t}\n\t}\n\tfmt.Println(\"Best:\", best.Points)\n}\n", 17,
            "scores: line 10 = [{Points:40} {Points:55}]|best: line 11 = nil" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task EachVariablesValuesAreThoseTheCopyPrinted(string fileName, string code, int crashLine, string timeline)
    {
        var program = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(program, code);

        var launch = TargetFactory.FromFile(program, LiveAllowance.For(program, TimeSpan.FromMinutes(3)));
        if (!launch.Ok)
        {
            output.WriteLine($"Not run on this computer: {launch.Problem}");
            return;
        }

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()).CheckAsync(launch);

        var stopped = report.Findings.SingleOrDefault(finding => finding.Kind == FindingKind.Runtime && finding.Timeline.Count > 0);
        Assert.True(stopped is not null, $"no timeline among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");
        Assert.Equal(crashLine, stopped!.Line);

        var said = stopped.Timeline.Select(variable => $"{variable.Name}: " + string.Join("; ", variable.Changes.Select(change => $"{change.GivenBy} = {change.Value}")));
        Assert.Equal(timeline.Split('|'), said);
    }
}
