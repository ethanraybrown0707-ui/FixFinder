using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Perl programs that stop on undef - a method called on it, or it used as a list or a hash - each checked the way the
/// window checks a file: the finding for where it stopped says which variable held undef, the line that gave it undef and
/// why - all of it printed by a copy of the program - and what to do. Each row is what FixFinder gave for the program on this
/// computer when these were written. A computer without Perl passes them over.
/// </summary>
public class PerlRootCauseLiveTests(ITestOutputHelper output) : IDisposable
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
        new object[] { "return_undef.pl", "use strict;\nuse warnings;\n\npackage Member;\n\nsub new {\n    my ($class, $id, $age) = @_;\n    return bless { id => $id, age => $age }, $class;\n}\n\nsub describe {\n    my ($self) = @_;\n    return \"Member $self->{id}, aged $self->{age}\";\n}\n\npackage main;\n\nsub find_member {\n    my ($members, $id) = @_;\n    foreach my $member (@$members) {\n        if ($member->{id} == $id) {\n            return $member;\n        }\n    }\n    return undef;\n}\n\nmy @members = (Member->new(1, 30));\nmy $member = find_member(\\@members, 2);\nprint $member->describe() . \"\\n\";\n", 30, 29, "$member is undef on line 30 - it was made undef on line 29",
            "The program stopped on line 30: it calls describe on `$member`, but `$member` is undef there. `$member` became undef on line 29: `my $member = find_member(\\@members, 2);`. `find_member` gave back undef on line 25: `return undef;`. It was called with $members = [bless( {'age' => 30,'id' => 1}, 'Member' )] and $id = 2. Line 22, `return $member;`, never ran in that call: the if on line 21, `if ($member->{id} == $id) {`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the undef could come from, stopped on line 30 with the same error.|`$member` was undef when line 30 ran.|Line 29 was the last line to set `$member` before line 30, and it set it to undef.|`find_member`, called from line 29, ended on line 25, and what it gave back was undef.|Line 22 did not run in that call.",
            "Decide what `find_member` should give back on line 25, and whether its callers expect undef. Or check `$member` is not undef before line 30 uses it.", 30, "if (defined $member) {|    print $member->describe() . \"\\n\";|}", "" },
        new object[] { "never_set.pl", "use strict;\nuse warnings;\n\npackage Score;\n\nsub new {\n    my ($class, $points) = @_;\n    return bless { points => $points }, $class;\n}\n\nsub show {\n    my ($self) = @_;\n    return \"Best: $self->{points}\";\n}\n\npackage main;\n\nmy @scores = (Score->new(40), Score->new(55));\nmy $best;\nforeach my $score (@scores) {\n    if ($score->{points} > 90) {\n        $best = $score;\n    }\n}\nprint $best->show() . \"\\n\";\n", 25, 19, "$best is undef on line 25 - it was made undef on line 19",
            "The program stopped on line 25: it calls show on `$best`, but `$best` is undef there. `$best` was set to undef on line 19, `my $best;`, and nothing changed it after that. Line 22, `$best = $score;`, never ran: the if on line 21, `if ($score->{points} > 90) {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the undef could come from, stopped on line 25 with the same error.|`$best` was undef when line 25 ran.|Line 19 was the last line to set `$best` before line 25, and it set it to undef.|Line 22 did not run at all.",
            "Give `$best` a value for when line 22 does not run, or check `$best` is not undef before line 25 uses it.", 25, "if (defined $best) {|    print $best->show() . \"\\n\";|}", "" },
        new object[] { "returned_variable.pl", "use strict;\nuse warnings;\n\nsub longest {\n    my ($titles) = @_;\n    my $best; # none found yet\n    foreach my $title (@$titles) {\n        if (length($title) > 20) {\n            $best = [$title]; # keep the longest so far\n        }\n    }\n    return $best;\n}\n\nmy @titles = (\"Dune\", \"Emma\", \"Ulysses\");\nmy $chosen = longest(\\@titles);\nmy @first = @$chosen;\nprint \"First: $first[0]\\n\";\n", 17, 16, "$chosen is undef on line 17 - it was made undef on line 16",
            "The program stopped on line 17: it uses `$chosen` as a list, but `$chosen` is undef there. `$chosen` became undef on line 16: `my $chosen = longest(\\@titles);`. `longest` gave back undef on line 12: `return $best;`. It was called with $titles = ['Dune','Emma','Ulysses']. `$best` was set to undef on line 6, `my $best;`, and nothing changed it after that in that call. Line 9, `$best = [$title];`, never ran in that call: the if on line 8, `if (length($title) > 20) {`, was checked 3 times and was never true.",
            "A copy of the program, printing a line wherever the undef could come from, stopped on line 17 with the same error.|`$chosen` was undef when line 17 ran.|Line 16 was the last line to set `$chosen` before line 17, and it set it to undef.|`longest`, called from line 16, ended on line 12, and what it gave back was undef.|Line 6 was the last line of `longest` to set `$best` in that call, and it set it to undef.|Line 9 did not run in that call.",
            "Decide what `longest` should give back on line 12, and whether its callers expect undef. Or check `$chosen` is not undef before line 17 uses it.", 0, "", "" },
        new object[] { "hash_lookup.pl", "use strict;\nuse warnings;\n\nmy %stock = (apples => { count => 4 }, pears => { count => 0 });\nmy $item = $stock{plums};\nmy %counted = %$item;\nprint \"Left: $counted{count}\\n\";\n", 6, 5, "$item is undef on line 6 - it was made undef on line 5",
            "The program stopped on line 6: it uses `$item` as a hash, but `$item` is undef there. `$item` became undef on line 5: `my $item = $stock{plums};`. `%stock` is a hash, and a hash gives back undef for a key it does not have.",
            "A copy of the program, printing a line wherever the undef could come from, stopped on line 6 with the same error.|`$item` was undef when line 6 ran.|Line 5 was the last line to set `$item` before line 6, and it set it to undef.",
            "Ask exists first, or give a value of your own with // - or check `$item` is not undef before line 6 uses it.", 0, "", "" },
        new object[] { "call_result.pl", "use strict;\nuse warnings;\n\npackage Item;\n\nsub new {\n    my ($class, $name, $price) = @_;\n    return bless { name => $name, price => $price }, $class;\n}\n\nsub priced {\n    my ($self) = @_;\n    return sprintf(\"%s: %.2f\", $self->{name}, $self->{price});\n}\n\npackage main;\n\nsub look_up {\n    my ($items, $name) = @_;\n    foreach my $item (@$items) {\n        if ($item->{name} eq $name) {\n            return $item;\n        }\n    }\n    return undef;\n}\n\nmy @items = (Item->new(\"tea\", 2.5), Item->new(\"cake\", 3.0));\nprint look_up(\\@items, \"milk\")->priced() . \"\\n\";\n", 29, 29, "look_up(...) is undef on line 29",
            "The program stopped on line 29: it calls priced on what `look_up` gave back, but `look_up` gave back undef. `look_up` gave back undef on line 25: `return undef;`. It was called with $items = [bless( {'name' => 'tea','price' => '2.5'}, 'Item' ),bless( {... and $name = 'milk'. Line 22, `return $item;`, never ran in that call: the if on line 21, `if ($item->{name} eq $name) {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the undef could come from, stopped on line 29 with the same error.|`look_up`, called from line 29, ended on line 25, and what it gave back was undef.|Line 22 did not run in that call.",
            "Decide what `look_up` should give back on line 25, and whether its callers expect undef. Or keep what it gives back under a name, and check that is not undef before line 29 uses it.", 0, "", "" },
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
            Assert.True(stopped.Fix is null || stopped.Fix.RuleId != "perl-undef-guard");
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
