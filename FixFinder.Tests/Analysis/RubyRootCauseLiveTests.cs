using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Ruby programs that stop because a method was called on nil, each checked the way the window checks a file: the finding
/// for where it stopped says which variable held nil, the line that gave it nil and why - all of it printed by a copy of
/// the program - and what to do. Each row is what FixFinder gave for the program on this computer when these were written.
/// A computer without Ruby passes them over.
/// </summary>
public class RubyRootCauseLiveTests(ITestOutputHelper output) : IDisposable
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
        new object[] { "return_nil.rb", "Member = Struct.new(:id, :age)\n\ndef find_member(members, id)\n  members.each do |member|\n    if member.id == id\n      return member\n    end\n  end\n  return nil\nend\n\nmembers = [Member.new(1, 30)]\nmember = find_member(members, 2)\nputs \"Age: #{member.age}\"\n", 14, 13, "member is nil on line 14 - it was made nil on line 13",
            "The program stopped on line 14: it calls age on `member`, but `member` is nil there. `member` became nil on line 13: `member = find_member(members, 2)`. `find_member` gave back nil on line 9: `return nil`. It was called with members = [#<struct Member id=1, age=30>] and id = 2. Line 6, `return member`, never ran in that call: the if on line 5, `if member.id == id`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 14 with the same NoMethodError.|`member` was nil when line 14 ran.|Line 13 was the last line to set `member` before line 14, and it set it to nil.|`find_member`, called from line 13, ended on line 9, and what it gave back was nil.|Line 6 did not run in that call.",
            "Decide what `find_member` should give back on line 9, and whether its callers expect nil. Or check `member` is not nil before line 14 uses it.", 14, "unless member.nil?|  puts \"Age: #{member.age}\"|end", "" },
        new object[] { "never_set.rb", "Score = Struct.new(:points)\n\nscores = [Score.new(40), Score.new(55)]\nbest = nil\nscores.each do |score|\n  if score.points > 90\n    best = score\n  end\nend\nputs \"Best: #{best.points}\"\n", 10, 4, "best is nil on line 10 - it was made nil on line 4",
            "The program stopped on line 10: it calls points on `best`, but `best` is nil there. `best` was set to nil on line 4, `best = nil`, and nothing changed it after that. Line 7, `best = score`, never ran: the if on line 6, `if score.points > 90`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 10 with the same NoMethodError.|`best` was nil when line 10 ran.|Line 4 was the last line to set `best` before line 10, and it set it to nil.|Line 7 did not run at all.",
            "Give `best` a value for when line 7 does not run, or check `best` is not nil before line 10 uses it.", 10, "unless best.nil?|  puts \"Best: #{best.points}\"|end", "" },
        new object[] { "parameter.rb", "def greeting(name)\n  shout = name.upcase\n  \"Hello, #{shout}\"\nend\n\ndef choose_name(names, index)\n  return names[index] if index < names.length\n  return nil\nend\n\nnames = [\"ada\", \"grace\"]\nchosen = choose_name(names, 5)\nputs greeting(chosen)\n", 2, 13, "name is nil on line 2 - it was made nil on line 13",
            "The program stopped on line 2: it calls upcase on `name`, but `name` is nil there. `name` is a parameter of `greeting`, and the call on line 13, `puts greeting(chosen)`, gave it nil.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 2 with the same NoMethodError.|`name` was nil when line 2 ran.|No line of `greeting` set `name` before line 2: it held what the call on line 13 gave it.",
            "Make the call on line 13 give `greeting` a value for `name`, or check `name` is not nil before line 2 uses it.", 0, "", "" },
        new object[] { "returned_variable.rb", "def longest(titles)\n  best = nil # none found yet\n  titles.each do |title|\n    if title.length > 20\n      best = title # keep the longest so far\n    end\n  end\n  return best\nend\n\ntitles = [\"Dune\", \"Emma\", \"Ulysses\"]\nchosen = longest(titles)\nputs chosen.upcase\n", 13, 12, "chosen is nil on line 13 - it was made nil on line 12",
            "The program stopped on line 13: it calls upcase on `chosen`, but `chosen` is nil there. `chosen` became nil on line 12: `chosen = longest(titles)`. `longest` gave back nil on line 8: `return best`. It was called with titles = [\"Dune\", \"Emma\", \"Ulysses\"]. `best` was set to nil on line 2, `best = nil`, and nothing changed it after that in that call. Line 5, `best = title`, never ran in that call: the if on line 4, `if title.length > 20`, was checked 3 times and was never true.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 13 with the same NoMethodError.|`chosen` was nil when line 13 ran.|Line 12 was the last line to set `chosen` before line 13, and it set it to nil.|`longest`, called from line 12, ended on line 8, and what it gave back was nil.|Line 2 was the last line of `longest` to set `best` in that call, and it set it to nil.|Line 5 did not run in that call.",
            "Decide what `longest` should give back on line 8, and whether its callers expect nil. Or check `chosen` is not nil before line 13 uses it.", 13, "unless chosen.nil?|  puts chosen.upcase|end", "" },
        new object[] { "find.rb", "Room = Struct.new(:name, :seats)\n\nrooms = [Room.new(\"A1\", 20), Room.new(\"B2\", 35)]\nroom = rooms.find { |each| each.seats >= 50 }\nputs \"Booked #{room.name}\"\n", 5, 4, "room is nil on line 5 - it was made nil on line 4",
            "The program stopped on line 5: it calls name on `room`, but `room` is nil there. `room` became nil on line 4: `room = rooms.find { |each| each.seats >= 50 }`. find gives back nil when no item passes its test.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 5 with the same NoMethodError.|`room` was nil when line 5 ran.|Line 4 was the last line to set `room` before line 5, and it set it to nil.",
            "Decide what the program should do when no item passes the test, and check `room` is not nil before line 5 uses it.", 5, "unless room.nil?|    puts \"Booked #{room.name}\"|end", "" },
        new object[] { "hash_lookup.rb", "prices = { \"tea\" => 2.5, \"cake\" => 3.0 }\nprice = prices[\"milk\"]\nputs \"Milk costs #{price.round(2)}\"\n", 3, 2, "price is nil on line 3 - it was made nil on line 2",
            "The program stopped on line 3: it calls round on `price`, but `price` is nil there. `price` became nil on line 2: `price = prices[\"milk\"]`. `prices` is a Hash, and a Hash gives back nil for a key it does not have.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 3 with the same NoMethodError.|`price` was nil when line 3 ran.|Line 2 was the last line to set `price` before line 3, and it set it to nil.",
            "Ask key? first, or use fetch with a value of its own - or check `price` is not nil before line 3 uses it.", 3, "unless price.nil?|    puts \"Milk costs #{price.round(2)}\"|end", "" },
        new object[] { "last_line.rb", "Account = Struct.new(:owner, :balance)\n\ndef open_account(open, owner)\n  if open.include?(owner)\n    Account.new(owner, 0)\n  end\nend\n\nopen = [\"ada\", \"grace\"]\naccount = open_account(open, \"alan\")\nputs account.balance\n", 11, 10, "account is nil on line 11 - it was made nil on line 10",
            "The program stopped on line 11: it calls balance on `account`, but `account` is nil there. `account` became nil on line 10: `account = open_account(open, \"alan\")`. `open_account` gave back nil.",
            "A copy of the program, printing a line wherever the nil could come from, stopped on line 11 with the same NoMethodError.|`account` was nil when line 11 ran.|Line 10 was the last line to set `account` before line 11, and it set it to nil.",
            "Decide what `open_account` should give back. Or check `account` is not nil before line 11 uses it.", 11, "unless account.nil?|  puts account.balance|end", "" },
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
            Assert.True(stopped.Fix is null || stopped.Fix.RuleId != "ruby-nil-guard");
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
