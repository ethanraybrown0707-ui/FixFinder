using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// PHP programs that stop because a method was called on null, or a property set on it, each checked the way the window
/// checks a file: the finding for where it stopped says which variable held null, the line that gave it null and why - all
/// of it printed by a copy of the program - and what to do. Each row is what FixFinder gave for the program on this computer
/// when these were written. A computer without PHP passes them over.
/// </summary>
public class PhpRootCauseLiveTests(ITestOutputHelper output) : IDisposable
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
        new object[] { "return_null.php", "<?php\n\nclass Member\n{\n    public function __construct(public int $id, public int $age)\n    {\n    }\n\n    public function describe(): string\n    {\n        return \"Member \" . $this->id . \", aged \" . $this->age;\n    }\n}\n\nfunction findMember(array $members, int $id): ?Member\n{\n    foreach ($members as $member) {\n        if ($member->id === $id) {\n            return $member;\n        }\n    }\n    return null;\n}\n\n$members = [new Member(1, 30)];\n$member = findMember($members, 2);\necho $member->describe() . \"\\n\";\n", 27, 26, "$member is null on line 27 - it was made null on line 26",
            "The program stopped on line 27: it calls describe on `$member`, but `$member` is null there. `$member` became null on line 26: `$member = findMember($members, 2);`. `findMember` gave back null on line 22: `return null;`. It was called with $members = [{\"id\":1,\"age\":30}] and $id = 2. Line 19, `return $member;`, never ran in that call: the if on line 18, `if ($member->id === $id) {`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 27 with the same Error.|`$member` was null when line 27 ran.|Line 26 was the last line to set `$member` before line 27, and it set it to null.|`findMember`, called from line 26, ended on line 22, and what it gave back was null.|Line 19 did not run in that call.",
            "Decide what `findMember` should give back on line 22, and whether its callers expect null. Or check `$member` is not null before line 27 uses it.", 27, "if ($member !== null) {|    echo $member->describe() . \"\\n\";|}", "" },
        new object[] { "never_set.php", "<?php\n\nclass Score\n{\n    public function __construct(public int $points)\n    {\n    }\n\n    public function show(): string\n    {\n        return \"Best: \" . $this->points;\n    }\n}\n\n$scores = [new Score(40), new Score(55)];\n$best = null;\nforeach ($scores as $score) {\n    if ($score->points > 90) {\n        $best = $score;\n    }\n}\necho $best->show() . \"\\n\";\n", 22, 16, "$best is null on line 22 - it was made null on line 16",
            "The program stopped on line 22: it calls show on `$best`, but `$best` is null there. `$best` was set to null on line 16, `$best = null;`, and nothing changed it after that. Line 19, `$best = $score;`, never ran: the if on line 18, `if ($score->points > 90) {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 22 with the same Error.|`$best` was null when line 22 ran.|Line 16 was the last line to set `$best` before line 22, and it set it to null.|Line 19 did not run at all.",
            "Give `$best` an object for when line 19 does not run, or check `$best` is not null before line 22 uses it.", 22, "if ($best !== null) {|    echo $best->show() . \"\\n\";|}", "" },
        new object[] { "parameter.php", "<?php\n\nclass Person\n{\n    public function __construct(public string $name)\n    {\n    }\n\n    public function shout(): string\n    {\n        return strtoupper($this->name);\n    }\n}\n\nfunction greeting(?Person $person): string\n{\n    return \"Hello, \" . $person->shout();\n}\n\nfunction choose(array $people, int $index): ?Person\n{\n    if ($index < count($people)) {\n        return $people[$index];\n    }\n    return null;\n}\n\n$people = [new Person(\"ada\"), new Person(\"grace\")];\n$chosen = choose($people, 5);\necho greeting($chosen) . \"\\n\";\n", 17, 30, "$person is null on line 17 - it was made null on line 30",
            "The program stopped on line 17: it calls shout on `$person`, but `$person` is null there. `$person` is a parameter of `greeting`, and the call on line 30, `echo greeting($chosen) . \"\\n\";`, gave it null.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 17 with the same Error.|`$person` was null when line 17 ran.|No line of `greeting` set `$person` before line 17: it held what the call on line 30 gave it.",
            "Make the call on line 30 give `greeting` an object for `$person`, or check `$person` is not null before line 17 uses it.", 0, "", "" },
        new object[] { "returned_variable.php", "<?php\n\nclass Book\n{\n    public function __construct(public string $title, public int $pages)\n    {\n    }\n\n    public function label(): string\n    {\n        return $this->title . \" (\" . $this->pages . \" pages)\";\n    }\n}\n\nfunction longest(array $books): ?Book\n{\n    $best = null; // none found yet\n    foreach ($books as $book) {\n        if ($book->pages > 1000) {\n            $best = $book; // keep the longest so far\n        }\n    }\n    return $best;\n}\n\n$books = [new Book(\"Dune\", 412), new Book(\"Emma\", 474)];\n$chosen = longest($books);\necho $chosen->label() . \"\\n\";\n", 28, 27, "$chosen is null on line 28 - it was made null on line 27",
            "The program stopped on line 28: it calls label on `$chosen`, but `$chosen` is null there. `$chosen` became null on line 27: `$chosen = longest($books);`. `longest` gave back null on line 23: `return $best;`. It was called with $books = [{\"title\":\"Dune\",\"pages\":412},{\"title\":\"Emma\",\"pages\":474}]. `$best` was set to null on line 17, `$best = null;`, and nothing changed it after that in that call. Line 20, `$best = $book;`, never ran in that call: the if on line 19, `if ($book->pages > 1000) {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 28 with the same Error.|`$chosen` was null when line 28 ran.|Line 27 was the last line to set `$chosen` before line 28, and it set it to null.|`longest`, called from line 27, ended on line 23, and what it gave back was null.|Line 17 was the last line of `longest` to set `$best` in that call, and it set it to null.|Line 20 did not run in that call.",
            "Decide what `longest` should give back on line 23, and whether its callers expect null. Or check `$chosen` is not null before line 28 uses it.", 28, "if ($chosen !== null) {|    echo $chosen->label() . \"\\n\";|}", "" },
        new object[] { "array_find.php", "<?php\n\nclass Room\n{\n    public function __construct(public string $name, public int $seats)\n    {\n    }\n\n    public function book(): string\n    {\n        return \"Booked \" . $this->name;\n    }\n}\n\n$rooms = [new Room(\"A1\", 20), new Room(\"B2\", 35)];\n$room = array_find($rooms, fn(Room $room) => $room->seats >= 50);\necho $room->book() . \"\\n\";\n", 17, 16, "$room is null on line 17 - it was made null on line 16",
            "The program stopped on line 17: it calls book on `$room`, but `$room` is null there. `$room` became null on line 16: `$room = array_find($rooms, fn(Room $room) => $room->seats >= 50);`. array_find gives back null when no item passes its test.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 17 with the same Error.|`$room` was null when line 17 ran.|Line 16 was the last line to set `$room` before line 17, and it set it to null.",
            "Decide what the program should do when no item passes the test, and check `$room` is not null before line 17 uses it.", 17, "if ($room !== null) {|    echo $room->book() . \"\\n\";|}", "" },
        new object[] { "call_result.php", "<?php\n\nclass Item\n{\n    public function __construct(public string $name, public float $price)\n    {\n    }\n\n    public function priced(): string\n    {\n        return $this->name . \": \" . number_format($this->price, 2);\n    }\n}\n\nfunction lookUp(array $items, string $name): ?Item\n{\n    foreach ($items as $item) {\n        if ($item->name === $name) {\n            return $item;\n        }\n    }\n    return null;\n}\n\n$items = [new Item(\"tea\", 2.5), new Item(\"cake\", 3.0)];\necho lookUp($items, \"milk\")->priced() . \"\\n\";\n", 26, 26, "lookUp(...) is null on line 26",
            "The program stopped on line 26: it calls priced on what `lookUp` gave back, but `lookUp` gave back null. `lookUp` gave back null on line 22: `return null;`. It was called with $items = [{\"name\":\"tea\",\"price\":2.5},{\"name\":\"cake\",\"price\":3}] and $name = \"milk\". Line 19, `return $item;`, never ran in that call: the if on line 18, `if ($item->name === $name) {`, was checked twice and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 26 with the same Error.|`lookUp`, called from line 26, ended on line 22, and what it gave back was null.|Line 19 did not run in that call.",
            "Decide what `lookUp` should give back on line 22, and whether its callers expect null. Or keep what it gives back under a name, and check that is not null before line 26 uses it.", 0, "", "" },
        new object[] { "set_property.php", "<?php\n\nclass Account\n{\n    public float $balance = 0.0;\n}\n\nfunction openAccount(array $open, string $owner): ?Account\n{\n    if (in_array($owner, $open, true)) {\n        return new Account();\n    }\n    return null;\n}\n\n$open = [\"ada\", \"grace\"];\n$account = openAccount($open, \"alan\");\n$account->balance = 10.0;\necho \"Balance: \" . $account->balance . \"\\n\";\n", 18, 17, "$account is null on line 18 - it was made null on line 17",
            "The program stopped on line 18: it sets balance on `$account`, but `$account` is null there. `$account` became null on line 17: `$account = openAccount($open, \"alan\");`. `openAccount` gave back null on line 13: `return null;`. It was called with $open = [\"ada\",\"grace\"] and $owner = \"alan\". Line 11, `return new Account();`, never ran in that call: the if on line 10, `if (in_array($owner, $open, true)) {`, was checked once and was never true.",
            "A copy of the program, printing a line wherever the null could come from, stopped on line 18 with the same Error.|`$account` was null when line 18 ran.|Line 17 was the last line to set `$account` before line 18, and it set it to null.|`openAccount`, called from line 17, ended on line 13, and what it gave back was null.|Line 11 did not run in that call.",
            "Decide what `openAccount` should give back on line 13, and whether its callers expect null. Or check `$account` is not null before line 18 uses it.", 0, "", "" },
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
            Assert.True(stopped.Fix is null || stopped.Fix.RuleId != "php-null-guard");
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
