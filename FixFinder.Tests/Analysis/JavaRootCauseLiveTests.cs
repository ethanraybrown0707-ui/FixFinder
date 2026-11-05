using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Java programs that stop on null, each checked the way the window checks a file: the finding for where it stopped says
/// which variable was null, the line that gave it null and why - all of it printed by a copy of the program - and what to do.
/// Each row is what FixFinder gave for the program on this computer when these were written. A computer without a JDK passes
/// them over.
/// </summary>
public class JavaRootCauseLiveTests(ITestOutputHelper output) : IDisposable
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
        new object[] { "Members.java", "import java.util.List;\n\npublic class Members {\n    record Member(int id, int age) {}\n\n    static Member findMember(List<Member> members, int wanted) {\n        for (Member member : members) {\n            if (member.id() == wanted) {\n                return member;\n            }\n        }\n        return null;\n    }\n\n    public static void main(String[] args) {\n        List<Member> members = List.of(new Member(1, 30));\n        Member member = findMember(members, 2);\n        System.out.println(\"Age: \" + member.age());\n    }\n}\n", 18, 17, "member is null on line 18 - it was made null on line 17",
            "The program stopped on line 18: it calls age on `member`, but `member` is null there. `member` became null on line 17: `Member member = findMember(members, 2);`. `findMember` gave back null on line 12: `return null;`. It was called with members = [Member[id=1, age=30]] and wanted = 2. Line 9, `return member;`, never ran in that call: the if on line 8, `if (member.id() == wanted) {`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 18 with the same NullPointerException.|`member` was null when line 18 ran.|Line 17 was the last line to set `member` before line 18, and it set it to null.|`findMember`, called from line 17, ended on line 12, and what it gave back was null.|Line 9 did not run in that call.",
            "Decide what `findMember` should give back on line 12, and whether its callers expect null. Or check `member` is not null before line 18 uses it.", 18, "        if (member != null) {|            System.out.println(\"Age: \" + member.age());|        }", "" },
        new object[] { "Best.java", "public class Best {\n    public static void main(String[] args) {\n        int[] scores = {40, 55};\n        String best = null;\n        for (int score : scores) {\n            if (score > 60) {\n                best = \"score \" + score;\n            }\n        }\n        System.out.println(best.length());\n    }\n}\n", 10, 4, "best is null on line 10 - it was made null on line 4",
            "The program stopped on line 10: it calls length on `best`, but `best` is null there. `best` was set to null on line 4, `String best = null;`, and nothing changed it after that. Line 7, `best = \"score \" + score;`, never ran: the if on line 6, `if (score > 60) {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 10 with the same NullPointerException.|`best` was null when line 10 ran.|Line 4 was the last line to set `best` before line 10, and it set it to null.|Line 7 did not run at all.",
            "Give `best` an object for when line 7 does not run, or check `best` is not null before line 10 uses it.", 10, "        if (best != null) {|            System.out.println(best.length());|        }", "" },
        new object[] { "Ages.java", "import java.util.HashMap;\nimport java.util.Map;\n\npublic class Ages {\n    public static void main(String[] args) {\n        Map<String, Integer> ages = new HashMap<>();\n        ages.put(\"ann\", 30);\n        Integer age = ages.get(\"bob\");\n        System.out.println(age + 1);\n    }\n}\n", 9, 8, "age is null on line 9 - it was made null on line 8",
            "The program stopped on line 9: it uses `age` as a plain int, but `age` is null there. `age` became null on line 8: `Integer age = ages.get(\"bob\");`. `ages` is declared a Map, and a Map's get gives back null for a key it does not have.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 9 with the same NullPointerException.|`age` was null when line 9 ran.|Line 8 was the last line to set `age` before line 9, and it set it to null.",
            "Use getOrDefault, to give back a value of your own when the key is not there - or check `age` is not null before line 9 uses it.", 9, "        if (age != null) {|            System.out.println(age + 1);|        }", "" },
        new object[] { "Show.java", "import java.util.List;\n\npublic class Show {\n    static void show(String name) {\n        System.out.println(name.toUpperCase());\n    }\n\n    static String find(List<String> names, String start) {\n        for (String name : names) {\n            if (name.startsWith(start)) {\n                return name;\n            }\n        }\n        return null;\n    }\n\n    public static void main(String[] args) {\n        show(find(List.of(\"ann\", \"bob\"), \"c\"));\n    }\n}\n", 5, 18, "name is null on line 5 - it was made null on line 18",
            "The program stopped on line 5: it calls toUpperCase on `name`, but `name` is null there. `name` is a parameter of `show`, and the call on line 18, `show(find(List.of(\"ann\", \"bob\"), \"c\"));`, gave it null. `find` gave back null on line 14: `return null;`. It was called with names = [ann, bob] and start = \"c\". Line 11, `return name;`, never ran in that call: the if on line 10, `if (name.startsWith(start)) {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 5 with the same NullPointerException.|`name` was null when line 5 ran.|No line of `show` set `name` before line 5: it held what the call on line 18 gave it.|`find`, called from line 18, ended on line 14, and what it gave back was null.|Line 11 did not run in that call.",
            "Decide what `find` should give back on line 14, and whether its callers expect null. Or check `name` is not null before line 5 uses it.", 5, "        if (name != null) {|            System.out.println(name.toUpperCase());|        }", "" },
        new object[] { "Lookup.java", "import java.util.List;\n\npublic class Lookup {\n    static String find(List<String> names, String start) {\n        for (String name : names) {\n            if (name.startsWith(start)) {\n                return name;\n            }\n        }\n        return null;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(find(List.of(\"ann\", \"bob\"), \"c\").length());\n    }\n}\n", 14, 14, "find(...) is null on line 14",
            "The program stopped on line 14: it calls length on what `find` gave back, but `find` gave back null. `find` gave back null on line 10: `return null;`. It was called with names = [ann, bob] and start = \"c\". Line 7, `return name;`, never ran in that call: the if on line 6, `if (name.startsWith(start)) {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 14 with the same NullPointerException.|`find`, called from line 14, ended on line 10, and what it gave back was null.|Line 7 did not run in that call.",
            "Decide what `find` should give back on line 10, and whether its callers expect null. Or keep what it gives back under a name, and check that is not null before line 14 uses it.", 0, "", "" },
        new object[] { "Report.java", "public class Report {\n    static String label(int mark) {\n        if (mark >= 70) {\n            return \"first\";\n        }\n        if (mark >= 40) {\n            return \"pass\";\n        }\n        return null;\n    }\n\n    static int labelLength(int mark) {\n        String said = label(mark);\n        return said.length();\n    }\n\n    public static void main(String[] args) {\n        System.out.println(labelLength(20));\n    }\n}\n", 14, 13, "said is null on line 14 - it was made null on line 13",
            "The program stopped on line 14: it calls length on `said`, but `said` is null there. `said` became null on line 13: `String said = label(mark);`. `label` gave back null on line 9: `return null;`. It was called with mark = 20. Line 4, `return \"first\";`, never ran in that call: the if on line 3, `if (mark >= 70) {`, was checked once and was never true. Line 7, `return \"pass\";`, never ran in that call: the if on line 6, `if (mark >= 40) {`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 14 with the same NullPointerException.|`said` was null when line 14 ran.|Line 13 was the last line to set `said` before line 14, and it set it to null.|`label`, called from line 13, ended on line 9, and what it gave back was null.|Line 4 did not run in that call.|Line 7 did not run in that call.",
            "Decide what `label` should give back on line 9, and whether its callers expect null. Or check `said` is not null before line 14 uses it.", 0, "", "" },
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
            Assert.True(stopped.Fix is null || stopped.Fix.RuleId != "java-null-guard");
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
