using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Lua programs that stop on nil - indexing it, adding to it, joining it to text, taking its length - each checked the way
/// the window checks a file: the finding for where it stopped says which variable held nil, the line that gave it nil and
/// why - all of it printed by a copy of the program - and what to do. Each row is what FixFinder gave for the program on this
/// computer when these were written. A computer without Lua passes them over.
/// </summary>
public class LuaRootCauseLiveTests(ITestOutputHelper output) : IDisposable
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
        new object[] { "return_nil.lua", "local function findMember(members, id)\n  for _, member in ipairs(members) do\n    if member.id == id then\n      return member\n    end\n  end\n  return nil\nend\n\nlocal members = { { id = 1, age = 30 } }\nlocal member = findMember(members, 2)\nprint(\"Age: \" .. member.age)\n", 12, 11, "member is nil on line 12 - it was made nil on line 11",
            "The program stopped on line 12: it reads age from `member`, but `member` is nil there. `member` became nil on line 11: `local member = findMember(members, 2)`. `findMember` gave back nil on line 7: `return nil`. It was called with members = {{age = 30, id = 1}} and id = 2. Line 4, `return member`, never ran in that call: the if on line 3, `if member.id == id then`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 12 with the same error.|`member` was nil when line 12 ran.|Line 11 was the last line to set `member` before line 12, and it set it to nil.|`findMember`, called from line 11, ended on line 7, and what it gave back was nil.|Line 4 did not run in that call.",
            "Decide what `findMember` should give back on line 7, and whether its callers expect nil. Or check `member` is not nil before line 12 uses it.", 12, "if member ~= nil then|  print(\"Age: \" .. member.age)|end", "" },
        new object[] { "never_set.lua", "local scores = { 40, 55 }\nlocal best\nfor _, score in ipairs(scores) do\n  if score > 90 then\n    best = score\n  end\nend\nprint(\"Best plus one: \" .. best + 1)\n", 8, 2, "best is nil on line 8 - it was made nil on line 2",
            "The program stopped on line 8: it does arithmetic with `best`, but `best` is nil there. `best` was set to nil on line 2, `local best`, and nothing changed it after that. Line 5, `best = score`, never ran: the if on line 4, `if score > 90 then`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 8 with the same error.|`best` was nil when line 8 ran.|Line 2 was the last line to set `best` before line 8, and it set it to nil.|Line 5 did not run at all.",
            "Give `best` a value for when line 5 does not run, or check `best` is not nil before line 8 uses it.", 8, "if best ~= nil then|  print(\"Best plus one: \" .. best + 1)|end", "" },
        new object[] { "parameter.lua", "local function greeting(name)\n  return \"Hello, \" .. name\nend\n\nlocal function chooseName(names, index)\n  if index <= #names then\n    return names[index]\n  end\nend\n\nlocal names = { \"ada\", \"grace\" }\nlocal chosen = chooseName(names, 5)\nprint(greeting(chosen))\n", 2, 13, "name is nil on line 2 - it was made nil on line 13",
            "The program stopped on line 2: it joins `name` to text with .., but `name` is nil there. `name` is a parameter of `greeting`, and the call on line 13, `print(greeting(chosen))`, gave it nil.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 2 with the same error.|`name` was nil when line 2 ran.|No line of `greeting` set `name` before line 2: it held what the call on line 13 gave it.",
            "Make the call on line 13 give `greeting` a value for `name`, or check `name` is not nil before line 2 uses it.", 0, "", "" },
        new object[] { "returned_variable.lua", "local function longest(titles)\n  local best -- none found yet\n  for _, title in ipairs(titles) do\n    if #title > 20 then\n      best = title -- keep the longest so far\n    end\n  end\n  return best\nend\n\nlocal titles = { \"Dune\", \"Emma\", \"Ulysses\" }\nlocal chosen = longest(titles)\nprint(#chosen)\n", 13, 12, "chosen is nil on line 13 - it was made nil on line 12",
            "The program stopped on line 13: it takes the length of `chosen` with #, but `chosen` is nil there. `chosen` became nil on line 12: `local chosen = longest(titles)`. `longest` gave back nil on line 8: `return best`. It was called with titles = {\"Dune\", \"Emma\", \"Ulysses\"}. `best` was set to nil on line 2, `local best`, and nothing changed it after that in that call. Line 5, `best = title`, never ran in that call: the if on line 4, `if #title > 20 then`, was checked 3 times and was never true.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 13 with the same error.|`chosen` was nil when line 13 ran.|Line 12 was the last line to set `chosen` before line 13, and it set it to nil.|`longest`, called from line 12, ended on line 8, and what it gave back was nil.|Line 2 was the last line of `longest` to set `best` in that call, and it set it to nil.|Line 5 did not run in that call.",
            "Decide what `longest` should give back on line 8, and whether its callers expect nil. Or check `chosen` is not nil before line 13 uses it.", 13, "if chosen ~= nil then|  print(#chosen)|end", "" },
        new object[] { "table_lookup.lua", "local stock = { apples = { count = 4 }, pears = { count = 0 } }\nlocal item = stock[\"plums\"]\nprint(\"Left: \" .. item.count)\n", 3, 2, "item is nil on line 3 - it was made nil on line 2",
            "The program stopped on line 3: it reads count from `item`, but `item` is nil there. `item` became nil on line 2: `local item = stock[\"plums\"]`. `stock` is a table, and a table gives back nil for a key it does not have.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 3 with the same error.|`item` was nil when line 3 ran.|Line 2 was the last line to set `item` before line 3, and it set it to nil.",
            "Give a value of your own with or - or check `item` is not nil before line 3 uses it.", 3, "if item ~= nil then|    print(\"Left: \" .. item.count)|end", "" },
        new object[] { "to_number.lua", "local typed = \"twelve\"\nlocal age = tonumber(typed)\nprint(\"Next year: \" .. age + 1)\n", 3, 2, "age is nil on line 3 - it was made nil on line 2",
            "The program stopped on line 3: it does arithmetic with `age`, but `age` is nil there. `age` became nil on line 2: `local age = tonumber(typed)`. tonumber, string.match and string.find give back nil when there is no number or nothing matches.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 3 with the same error.|`age` was nil when line 3 ran.|Line 2 was the last line to set `age` before line 3, and it set it to nil.",
            "Decide what the program should do when there is nothing, and check `age` is not nil before line 3 uses it.", 3, "if age ~= nil then|    print(\"Next year: \" .. age + 1)|end", "" },
        new object[] { "method.lua", "local Account = {}\nAccount.__index = Account\n\nfunction Account.new(owner)\n  return setmetatable({ owner = owner, balance = 0 }, Account)\nend\n\nfunction Account:deposit(amount)\n  self.balance = self.balance + amount\nend\n\nlocal function openAccount(open, owner)\n  for _, name in ipairs(open) do\n    if name == owner then\n      return Account.new(owner)\n    end\n  end\nend\n\nlocal open = { \"ada\", \"grace\" }\nlocal account = openAccount(open, \"alan\")\naccount:deposit(10)\n", 22, 21, "account is nil on line 22 - it was made nil on line 21",
            "The program stopped on line 22: it calls deposit on `account`, but `account` is nil there. `account` became nil on line 21: `local account = openAccount(open, \"alan\")`. `openAccount` reached its end without a return, and a function that does gives back nil. It was called with open = {\"ada\", \"grace\"} and owner = \"alan\". Line 15, `return Account.new(owner)`, never ran in that call: the if on line 14, `if name == owner then`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 22 with the same error.|`account` was nil when line 22 ran.|Line 21 was the last line to set `account` before line 22, and it set it to nil.|`openAccount`, called from line 21, reached its end without running a return.|Line 15 did not run in that call.",
            "Give `openAccount` a return at its end, for when it gets there. Or check `account` is not nil before line 22 uses it.", 22, "if account ~= nil then|  account:deposit(10)|end", "" },
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
            Assert.True(stopped.Fix is null || stopped.Fix.RuleId != "lua-nil-guard");
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
