using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// The same three functions in each language whose missing value a copy of the program follows back: main sets member from
/// loadMember(3), which gives back what findMember(3) gave - nothing, as no member has id 3 - and main passes member to
/// printMember, which stops on it. In Rust and Perl main stops on member itself, as their crash names no caller line. The
/// missing value is followed through all three, to the line where member got it, and the way it went is shown step by step,
/// each as its depth, its function, its line and what happened there. Each row is what FixFinder gave for the program on
/// this computer when these were written; a computer without the language passes it over.
/// </summary>
public class BraceRootCausePathLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The file, the program, the line it stopped on, the line the missing value came from, and the way it went, a step to a line.</summary>
    public static IEnumerable<object[]> Programs =>
    [
        new object[] { "Members.java", "import java.util.List;\n\npublic class Members {\n    record Member(int id, String name) {}\n\n    static List<Member> members = List.of(new Member(1, \"Ann\"), new Member(2, \"Bob\"));\n\n    static Member findMember(int wanted) {\n        for (Member member : members) {\n            if (member.id() == wanted) {\n                return member;\n            }\n        }\n        return null;\n    }\n\n    static Member loadMember(int wanted) {\n        Member found = findMember(wanted);\n        return found;\n    }\n\n    static void printMember(Member member) {\n        System.out.println(member.name());\n    }\n\n    public static void main(String[] args) {\n        Member member = loadMember(3);\n        printMember(member);\n    }\n}\n", 23, 27,
            "0 main 27 calls `loadMember(3)`\n1 loadMember 18 calls `findMember(wanted)`\n2 findMember 14 gives back null: `return null;`\n1 loadMember 18 sets `found` to null\n1 loadMember 19 gives back null: `return found;`\n0 main 27 sets `member` to null\n0 main 28 calls `printMember(member)`, giving `member` null\n1 printMember 23 stops: it calls name on `member`" },
        new object[] { "Program.cs", "using System;\nusing System.Collections.Generic;\n\npublic record Member(int Id, string Name);\n\npublic static class Program\n{\n    static readonly List<Member> Members = new() { new Member(1, \"Ann\"), new Member(2, \"Bob\") };\n\n    static Member? FindMember(int wanted)\n    {\n        foreach (var member in Members)\n        {\n            if (member.Id == wanted)\n            {\n                return member;\n            }\n        }\n        return null;\n    }\n\n    static Member? LoadMember(int wanted)\n    {\n        var found = FindMember(wanted);\n        return found;\n    }\n\n    static void PrintMember(Member? member)\n    {\n        Console.WriteLine(member!.Name);\n    }\n\n    public static void Main()\n    {\n        var member = LoadMember(3);\n        PrintMember(member);\n    }\n}\n", 30, 35,
            "0 Main 35 calls `LoadMember(3)`\n1 LoadMember 24 calls `FindMember(wanted)`\n2 FindMember 19 gives back null: `return null;`\n1 LoadMember 24 sets `found` to null\n1 LoadMember 25 gives back null: `return found;`\n0 Main 35 sets `member` to null\n0 Main 36 calls `PrintMember(member)`, giving `member` null\n1 PrintMember 30 stops: it reads Name from `member`" },
        new object[] { "members.js", "const members = [{ id: 1, name: \"Ann\" }, { id: 2, name: \"Bob\" }];\n\nfunction findMember(wanted) {\n    for (const member of members) {\n        if (member.id === wanted) {\n            return member;\n        }\n    }\n    return null;\n}\n\nfunction loadMember(wanted) {\n    const found = findMember(wanted);\n    return found;\n}\n\nfunction printMember(member) {\n    console.log(member.name);\n}\n\nfunction main() {\n    const member = loadMember(3);\n    printMember(member);\n}\n\nmain();\n", 18, 22,
            "0 main 22 calls `loadMember(3)`\n1 loadMember 13 calls `findMember(wanted)`\n2 findMember 9 gives back null: `return null;`\n1 loadMember 13 sets `found` to null\n1 loadMember 14 gives back null: `return found;`\n0 main 22 sets `member` to null\n0 main 23 calls `printMember(member)`, giving `member` null\n1 printMember 18 stops: it reads name from `member`" },
        new object[] { "members.ts", "type Member = { id: number; name: string };\n\nconst members: Member[] = [{ id: 1, name: \"Ann\" }, { id: 2, name: \"Bob\" }];\n\nfunction findMember(wanted: number): Member | null {\n    for (const member of members) {\n        if (member.id === wanted) {\n            return member;\n        }\n    }\n    return null;\n}\n\nfunction loadMember(wanted: number): Member | null {\n    const found = findMember(wanted);\n    return found;\n}\n\nfunction printMember(member: Member | null): void {\n    console.log(member!.name);\n}\n\nfunction main(): void {\n    const member = loadMember(3);\n    printMember(member);\n}\n\nmain();\n", 20, 24,
            "0 main 24 calls `loadMember(3)`\n1 loadMember 15 calls `findMember(wanted)`\n2 findMember 11 gives back null: `return null;`\n1 loadMember 15 sets `found` to null\n1 loadMember 16 gives back null: `return found;`\n0 main 24 sets `member` to null\n0 main 25 calls `printMember(member)`, giving `member` null\n1 printMember 20 stops: it reads name from `member`" },
        new object[] { "main.go", "package main\n\nimport \"fmt\"\n\ntype Member struct {\n\tID   int\n\tName string\n}\n\nvar members = []*Member{{ID: 1, Name: \"Ann\"}, {ID: 2, Name: \"Bob\"}}\n\nfunc findMember(wanted int) *Member {\n\tfor _, member := range members {\n\t\tif member.ID == wanted {\n\t\t\treturn member\n\t\t}\n\t}\n\treturn nil\n}\n\nfunc loadMember(wanted int) *Member {\n\tfound := findMember(wanted)\n\treturn found\n}\n\nfunc printMember(member *Member) {\n\tfmt.Println(member.Name)\n}\n\nfunc main() {\n\tmember := loadMember(3)\n\tprintMember(member)\n}\n", 27, 31,
            "0 main 31 calls `loadMember(3)`\n1 loadMember 22 calls `findMember(wanted)`\n2 findMember 18 gives back nil: `return nil`\n1 loadMember 22 sets `found` to nil\n1 loadMember 23 gives back nil: `return found`\n0 main 31 sets `member` to nil\n0 main 32 calls `printMember(member)`, giving `member` nil\n1 printMember 27 stops: it reads Name from `member`" },
        new object[] { "members.php", "<?php\n\nclass Member\n{\n    public function __construct(public int $id, public string $name)\n    {\n    }\n\n    public function describe(): string\n    {\n        return $this->name;\n    }\n}\n\nfunction findMember(array $members, int $wanted): ?Member\n{\n    foreach ($members as $member) {\n        if ($member->id === $wanted) {\n            return $member;\n        }\n    }\n    return null;\n}\n\nfunction loadMember(array $members, int $wanted): ?Member\n{\n    $found = findMember($members, $wanted);\n    return $found;\n}\n\nfunction printMember(?Member $member): void\n{\n    echo $member->describe(), \"\\n\";\n}\n\n$members = [new Member(1, \"Ann\"), new Member(2, \"Bob\")];\n$member = loadMember($members, 3);\nprintMember($member);\n", 33, 37,
            "0 <module> 37 calls `loadMember($members, 3)`\n1 loadMember 27 calls `findMember($members, $wanted)`\n2 findMember 22 gives back null: `return null;`\n1 loadMember 27 sets `$found` to null\n1 loadMember 28 gives back null: `return $found;`\n0 <module> 37 sets `$member` to null\n0 <module> 38 calls `printMember($member)`, giving `$member` null\n1 printMember 33 stops: it calls describe on `$member`" },
        new object[] { "members.dart", "class Member {\n  final int id;\n  final String name;\n  Member(this.id, this.name);\n}\n\nfinal members = [Member(1, 'Ann'), Member(2, 'Bob')];\n\nMember? findMember(int wanted) {\n  for (final member in members) {\n    if (member.id == wanted) {\n      return member;\n    }\n  }\n  return null;\n}\n\nMember? loadMember(int wanted) {\n  final found = findMember(wanted);\n  return found;\n}\n\nvoid printMember(Member? member) {\n  print(member!.name);\n}\n\nvoid main() {\n  final member = loadMember(3);\n  printMember(member);\n}\n", 24, 28,
            "0 main 28 calls `loadMember(3)`\n1 loadMember 19 calls `findMember(wanted)`\n2 findMember 15 gives back null: `return null;`\n1 loadMember 19 sets `found` to null\n1 loadMember 20 gives back null: `return found;`\n0 main 28 sets `member` to null\n0 main 29 calls `printMember(member)`, giving `member` null\n1 printMember 24 stops: it reads name from `member`" },
        new object[] { "members.lua", "local members = { { id = 1, name = \"Ann\" }, { id = 2, name = \"Bob\" } }\n\nlocal function findMember(wanted)\n  for _, member in ipairs(members) do\n    if member.id == wanted then\n      return member\n    end\n  end\n  return nil\nend\n\nlocal function loadMember(wanted)\n  local found = findMember(wanted)\n  return found\nend\n\nlocal function printMember(member)\n  print(member.name)\nend\n\nlocal function main()\n  local member = loadMember(3)\n  printMember(member)\nend\n\nmain()\n", 18, 22,
            "0 main 22 calls `loadMember(3)`\n1 loadMember 13 calls `findMember(wanted)`\n2 findMember 9 gives back nil: `return nil`\n1 loadMember 13 sets `found` to nil\n1 loadMember 14 gives back nil: `return found`\n0 main 22 sets `member` to nil\n0 main 23 calls `printMember(member)`, giving `member` nil\n1 printMember 18 stops: it reads name from `member`" },
        new object[] { "members.rb", "Member = Struct.new(:id, :name)\n\nMEMBERS = [Member.new(1, \"Ann\"), Member.new(2, \"Bob\")]\n\ndef find_member(wanted)\n  MEMBERS.each do |member|\n    if member.id == wanted\n      return member\n    end\n  end\n  return nil\nend\n\ndef load_member(wanted)\n  found = find_member(wanted)\n  return found\nend\n\ndef print_member(member)\n  puts member.name\nend\n\ndef main\n  member = load_member(3)\n  print_member(member)\nend\n\nmain\n", 20, 24,
            "0 main 24 calls `load_member(3)`\n1 load_member 15 calls `find_member(wanted)`\n2 find_member 11 gives back nil: `return nil`\n1 load_member 15 sets `found` to nil\n1 load_member 16 gives back nil: `return found`\n0 main 24 sets `member` to nil\n0 main 25 calls `print_member(member)`, giving `member` nil\n1 print_member 20 stops: it calls name on `member`" },
        new object[] { "members.pl", "use strict;\nuse warnings;\n\npackage Member;\n\nsub new {\n    my ($class, $id, $name) = @_;\n    return bless { id => $id, name => $name }, $class;\n}\n\nsub describe {\n    my ($self) = @_;\n    return $self->{name};\n}\n\npackage main;\n\nmy @members = (Member->new(1, \"Ann\"), Member->new(2, \"Bob\"));\n\nsub find_member {\n    my ($wanted) = @_;\n    for my $member (@members) {\n        if ($member->{id} == $wanted) {\n            return $member;\n        }\n    }\n    return undef;\n}\n\nsub load_member {\n    my ($wanted) = @_;\n    my $found = find_member($wanted);\n    return $found;\n}\n\nsub main {\n    my $member = load_member(3);\n    print $member->describe(), \"\\n\";\n}\n\nmain();\n", 38, 37,
            "0 main 37 calls `load_member(3)`\n1 load_member 32 calls `find_member($wanted)`\n2 find_member 27 gives back undef: `return undef;`\n1 load_member 32 sets `$found` to undef\n1 load_member 33 gives back undef: `return $found;`\n0 main 37 sets `$member` to undef\n0 main 38 stops: it calls describe on `$member`" },
        new object[] { "main.rs", "struct Member {\n    id: u32,\n    name: String,\n}\n\nfn find_member(members: &[Member], wanted: u32) -> Option<&Member> {\n    for member in members {\n        if member.id == wanted {\n            return Some(member);\n        }\n    }\n    return None;\n}\n\nfn load_member(members: &[Member], wanted: u32) -> Option<&Member> {\n    let found = find_member(members, wanted);\n    return found;\n}\n\nfn main() {\n    let members = vec![Member { id: 1, name: String::from(\"Ann\") }, Member { id: 2, name: String::from(\"Bob\") }];\n    let member = load_member(&members, 3);\n    println!(\"{}\", member.unwrap().name);\n}\n", 23, 22,
            "0 main 22 calls `load_member(&members, 3)`\n1 load_member 16 calls `find_member(members, wanted)`\n2 find_member 12 gives back None: `return None;`\n1 load_member 16 sets `found` to None\n1 load_member 17 gives back None: `return found;`\n0 main 22 sets `member` to None\n0 main 23 stops: it calls unwrap on `member`" },
    ];

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheWayTheMissingValueWentIsShownCallByCall(string fileName, string code, int crashLine, int introducedLine, string steps)
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
        var stopped = report.Findings.SingleOrDefault(finding => finding.RootCause is not null);
        Assert.True(stopped is not null, $"no root cause among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");

        var cause = stopped!.RootCause!;
        foreach (var step in cause.Path) output.WriteLine($"{step.Depth} {step.Function} {step.Line} {step.Happened}");
        Assert.Equal(crashLine, cause.CrashLine);
        Assert.Equal(introducedLine, cause.IntroducedLine);
        Assert.Equal(steps.Split('\n'), cause.Path.Select(step => $"{step.Depth} {step.Function} {step.Line} {step.Happened}"));
        Assert.Equal(code, File.ReadAllText(program));
    }
}
