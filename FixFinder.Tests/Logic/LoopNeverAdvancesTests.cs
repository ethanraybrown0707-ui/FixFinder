using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// A loop that runs while a counter is below a bound and moves nothing on - in each language beyond Python's and the C
/// family's, which have checks of their own - with the change that moves the counter on at the end of each pass, and none for
/// Rust, whose counter has to be made mut as well; and loops that move on, leave, or change their bound, which are left
/// alone. Each row is what the check gave for the program when these were written.
/// </summary>
public class LoopNeverAdvancesTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private SourceFile Program(string fileName, string code)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return SourceFile.Read(path)!;
    }

    /// <summary>The file, the program, the check, the loop's line, the line the change goes before (0 for none), and the line it adds.</summary>
    public static TheoryData<string, string, string, int, int, string> Loops => new()
    {
        { "Main.scala", "object Main extends App {\n  val marks = Array(50, 60, 70)\n  var i = 0\n  var total = 0\n  while (i < marks.length) {\n    total += marks(i)\n  }\n  println(total)\n}\n", "logic-scala-loop-never-advances", 5, 7, "    i += 1" },
        { "stuck.dart", "void main() {\n  final marks = [50, 60, 70];\n  var i = 0;\n  var total = 0;\n  while (i < marks.length) {\n    total += marks[i];\n  }\n  print(total);\n}\n", "logic-dart-loop-never-advances", 5, 7, "    i++;" },
        { "stuck.f90", "program stuck\n  implicit none\n  integer :: marks(3) = [50, 60, 70]\n  integer :: i, total\n  i = 1\n  total = 0\n  do while (i <= size(marks))\n    total = total + marks(i)\n  end do\n  print *, total\nend program stuck\n", "logic-fortran-loop-never-advances", 7, 9, "    i = i + 1" },
        { "stuck.fsx", "let marks = [| 50; 60; 70 |]\nlet mutable i = 0\nlet mutable total = 0\nwhile i < marks.Length do\n    total <- total + marks.[i]\nprintfn \"%d\" total\n", "logic-fsharp-loop-never-advances", 4, 6, "    i <- i + 1" },
        { "stuck.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tmarks := []int{50, 60, 70}\n\ti := 0\n\ttotal := 0\n\tfor i < len(marks) {\n\t\ttotal += marks[i]\n\t}\n\tfmt.Println(total)\n}\n", "logic-go-loop-never-advances", 9, 11, "\t\ti++" },
        { "stuck.lua", "local marks = {50, 60, 70}\nlocal i = 1\nlocal total = 0\nwhile i <= #marks do\n  total = total + marks[i]\nend\nprint(total)\n", "logic-lua-loop-never-advances", 4, 6, "  i = i + 1" },
        { "stuck.php", "<?php\n$marks = [50, 60, 70];\n$i = 0;\n$total = 0;\nwhile ($i < count($marks)) {\n    $total += $marks[$i];\n}\necho $total;\n", "logic-php-loop-never-advances", 5, 7, "    $i++;" },
        { "stuck.pl", "my @marks = (50, 60, 70);\nmy $i = 0;\nmy $total = 0;\nwhile ($i < @marks) {\n    $total += $marks[$i];\n}\nprint \"$total\n\";\n", "logic-perl-loop-never-advances", 4, 6, "    $i++;" },
        { "stuck.ps1", "$marks = @(50, 60, 70)\n$i = 0\n$total = 0\nwhile ($i -lt $marks.Count) {\n    $total += $marks[$i]\n}\nWrite-Output $total\n", "logic-powershell-loop-never-advances", 4, 6, "    $i++" },
        { "stuck.rb", "marks = [50, 60, 70]\ni = 0\ntotal = 0\nwhile i < marks.length\n  total += marks[i]\nend\nputs total\n", "logic-ruby-loop-never-advances", 4, 6, "  i += 1" },
        { "stuck.rs", "fn main() {\n    let marks = [50, 60, 70];\n    let i = 0;\n    let mut total = 0;\n    while i < marks.len() {\n        total += marks[i];\n    }\n    println!(\"{}\", total);\n}\n", "logic-rust-loop-never-advances", 5, 0, "" },
    };

    [Theory]
    [MemberData(nameof(Loops))]
    public void ALoopThatNeverMovesOnIsFound(string fileName, string code, string check, int line, int changeAt, string added)
    {
        var found = Assert.Single(LogicPatterns.Scan(Program(fileName, code)), finding => finding.PatternId == check);

        Assert.Equal(line, found.Line);
        if (changeAt == 0)
        {
            Assert.Null(found.Fix);
            return;
        }

        Assert.Equal(changeAt, found.Fix!.StartLine);
        Assert.Equal(0, found.Fix.RemoveCount);
        Assert.Equal(added.Split('|'), found.Fix.NewLines);
    }

    [Theory]
    [InlineData("right.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tmarks := []int{50, 60, 70}\n\ti := 0\n\ttotal := 0\n\tfor i < len(marks) {\n\t\ttotal += marks[i]\n\t\ti++\n\t}\n\tfmt.Println(total)\n}\n")]
    [InlineData("right.lua", "local marks = {50, 60, 70}\nlocal i = 1\nlocal total = 0\nwhile i <= #marks do\n  total = total + marks[i]\n  i = i + 1\nend\nlocal tries = 0\nwhile tries < 3 do\n  if total > 100 then break end\nend\nprint(total)\n")]
    [InlineData("right.php", "<?php\n$queue = [1, 2];\n$i = 0;\nwhile ($i < count($queue)) {\n    array_pop($queue);\n}\necho $i;\n")]
    [InlineData("right.rb", "marks = [50, 60, 70]\ni = 0\ntotal = 0\nwhile i < marks.length\n  total += marks[i]\n  i += 1\nend\nwhile total > 0\n  total -= 10\nend\nputs total\n")]
    public void ALoopThatMovesOnOrLeavesIsLeftAlone(string fileName, string code)
    {
        Assert.DoesNotContain(LogicPatterns.Scan(Program(fileName, code)), finding => finding.PatternId.EndsWith("loop-never-advances", StringComparison.Ordinal));
    }
}
