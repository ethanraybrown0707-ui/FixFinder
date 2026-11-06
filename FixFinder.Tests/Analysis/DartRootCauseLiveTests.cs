using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Dart programs that stop because a ! met null, each checked the way the window checks a file: the finding for where it
/// stopped says which variable the ! said was not null, the line that gave it null and why - all of it printed by a copy of
/// the program - and what to do. Each row is what FixFinder gave for the program on this computer when these were written.
/// A computer without Dart passes them over.
/// </summary>
/// <remarks>Each row builds its program up to three times - as it is, the copy that prints, and the copy with the
/// check - so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class DartRootCauseLiveTests(ITestOutputHelper output) : IDisposable
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
        new object[] { "return_null.dart", "class Member {\n  final int id;\n  final int age;\n  Member(this.id, this.age);\n}\n\nMember? findMember(List<Member> members, int id) {\n  for (final member in members) {\n    if (member.id == id) {\n      return member;\n    }\n  }\n  return null;\n}\n\nvoid main() {\n  final members = [Member(1, 30)];\n  final member = findMember(members, 2);\n  print('Age: ${member!.age}');\n}\n", 19, 18, "member is null on line 19 - it was made null on line 18",
            "The program stopped on line 19: it reads age from `member`, but `member` is null there. `member` became null on line 18: `final member = findMember(members, 2);`. `findMember` gave back null on line 13: `return null;`. It was called with members = [Instance of 'Member'] and id = 2. Line 10, `return member;`, never ran in that call: the if on line 9, `if (member.id == id) {`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 19 with the same null check error.|`member` was null when line 19 ran.|Line 18 was the last line to set `member` before line 19, and it set it to null.|`findMember`, called from line 18, ended on line 13, and what it gave back was null.|Line 10 did not run in that call.",
            "Decide what `findMember` should give back on line 13, and whether its callers expect null. Or check `member` is not null before line 19 uses it.", 19, "  if (member != null) {|    print('Age: ${member!.age}');|  }", "" },
        new object[] { "never_set.dart", "class Score {\n  final int points;\n  Score(this.points);\n}\n\nvoid main() {\n  final scores = [Score(40), Score(55)];\n  Score? best;\n  for (final score in scores) {\n    if (score.points > 90) {\n      best = score;\n    }\n  }\n  print('Best: ${best!.points}');\n}\n", 14, 8, "best is null on line 14 - it was made null on line 8",
            "The program stopped on line 14: it reads points from `best`, but `best` is null there. `best` was set to null on line 8, `Score? best;`, and nothing changed it after that. Line 11, `best = score;`, never ran: the if on line 10, `if (score.points > 90) {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 14 with the same null check error.|`best` was null when line 14 ran.|Line 8 was the last line to set `best` before line 14, and it set it to null.|Line 11 did not run at all.",
            "Give `best` a value for when line 11 does not run, or check `best` is not null before line 14 uses it.", 14, "  if (best != null) {|    print('Best: ${best!.points}');|  }", "" },
        new object[] { "parameter.dart", "String greeting(String? name) {\n  final shout = name!.toUpperCase();\n  return 'Hello, $shout';\n}\n\nString? chooseName(List<String> names, int index) {\n  if (index < names.length) {\n    return names[index];\n  }\n  return null;\n}\n\nvoid main() {\n  final names = ['ada', 'grace'];\n  final chosen = chooseName(names, 5);\n  print(greeting(chosen));\n}\n", 2, 16, "name is null on line 2 - it was made null on line 16",
            "The program stopped on line 2: it calls toUpperCase on `name`, but `name` is null there. `name` is a parameter of `greeting`, and the call on line 16, `print(greeting(chosen));`, gave it null.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 2 with the same null check error.|`name` was null when line 2 ran.|No line of `greeting` set `name` before line 2: it held what the call on line 16 gave it.",
            "Make the call on line 16 give `greeting` a value for `name`, or check `name` is not null before line 2 uses it.", 0, "", "" },
        new object[] { "returned_variable.dart", "String? longest(List<String> titles) {\n  String? best; // none found yet\n  for (final title in titles) {\n    if (title.length > 20) {\n      best = title; // keep the longest so far\n    }\n  }\n  return best;\n}\n\nvoid main() {\n  final titles = ['Dune', 'Emma', 'Ulysses'];\n  final chosen = longest(titles);\n  print(chosen!.toUpperCase());\n}\n", 14, 13, "chosen is null on line 14 - it was made null on line 13",
            "The program stopped on line 14: it calls toUpperCase on `chosen`, but `chosen` is null there. `chosen` became null on line 13: `final chosen = longest(titles);`. `longest` gave back null on line 8: `return best;`. It was called with titles = [Dune, Emma, Ulysses]. `best` was set to null on line 2, `String? best;`, and nothing changed it after that in that call. Line 5, `best = title;`, never ran in that call: the if on line 4, `if (title.length > 20) {`, was checked 3 times and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 14 with the same null check error.|`chosen` was null when line 14 ran.|Line 13 was the last line to set `chosen` before line 14, and it set it to null.|`longest`, called from line 13, ended on line 8, and what it gave back was null.|Line 2 was the last line of `longest` to set `best` in that call, and it set it to null.|Line 5 did not run in that call.",
            "Decide what `longest` should give back on line 8, and whether its callers expect null. Or check `chosen` is not null before line 14 uses it.", 14, "  if (chosen != null) {|    print(chosen!.toUpperCase());|  }", "" },
        new object[] { "map_lookup.dart", "void main() {\n  final stock = <String, int>{'apples': 4, 'pears': 0};\n  final count = stock['plums'];\n  print('Left: ${count! + 1}');\n}\n", 4, 3, "count is null on line 4 - it was made null on line 3",
            "The program stopped on line 4: it uses ! to say `count` is not null, but `count` is null there. `count` became null on line 3: `final count = stock['plums'];`. `stock` is a Map, and a Map's [] gives back null for a key it does not have.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 4 with the same null check error.|`count` was null when line 4 ran.|Line 3 was the last line to set `count` before line 4, and it set it to null.",
            "Ask containsKey first, or give a value of your own with ?? - or check `count` is not null before line 4 uses it.", 4, "  if (count != null) {|    print('Left: ${count! + 1}');|  }", "" },
        new object[] { "try_parse.dart", "void main() {\n  final typed = 'twelve';\n  final age = int.tryParse(typed);\n  print('Next year: ${age! + 1}');\n}\n", 4, 3, "age is null on line 4 - it was made null on line 3",
            "The program stopped on line 4: it uses ! to say `age` is not null, but `age` is null there. `age` became null on line 3: `final age = int.tryParse(typed);`. tryParse gives back null for text that is not a number.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 4 with the same null check error.|`age` was null when line 4 ran.|Line 3 was the last line to set `age` before line 4, and it set it to null.",
            "Decide what the program should do when the text is not a number, and check `age` is not null before line 4 uses it.", 4, "  if (age != null) {|    print('Next year: ${age! + 1}');|  }", "" },
        new object[] { "call_result.dart", "class Item {\n  final String name;\n  final double price;\n  Item(this.name, this.price);\n}\n\nItem? lookUp(List<Item> items, String name) {\n  for (final item in items) {\n    if (item.name == name) {\n      return item;\n    }\n  }\n  return null;\n}\n\nvoid main() {\n  final items = [Item('tea', 2.5), Item('cake', 3.0)];\n  print(lookUp(items, 'milk')!.price.toStringAsFixed(2));\n}\n", 18, 18, "lookUp(...) is null on line 18",
            "The program stopped on line 18: it reads price from what `lookUp` gave back, but `lookUp` gave back null. `lookUp` gave back null on line 13: `return null;`. It was called with items = [Instance of 'Item', Instance of 'Item'] and name = \"milk\". Line 10, `return item;`, never ran in that call: the if on line 9, `if (item.name == name) {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 18 with the same null check error.|`lookUp`, called from line 18, ended on line 13, and what it gave back was null.|Line 10 did not run in that call.",
            "Decide what `lookUp` should give back on line 13, and whether its callers expect null. Or keep what it gives back under a name, and check that is not null before line 18 uses it.", 0, "", "" },
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
            Assert.True(stopped.Fix is null || stopped.Fix.RuleId != "dart-null-guard");
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
