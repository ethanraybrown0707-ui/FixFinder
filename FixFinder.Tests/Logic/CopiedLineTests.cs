using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// A line copied from the one above with its name changed and what it reads left as it was - height = box.width under
/// width = box.width - in each language, with the change that reads the copy's own part; and pairs written to match, which
/// are left alone. Each row is what the check gave for the program when these were written.
/// </summary>
public class CopiedLineTests : IDisposable
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

    /// <summary>The file, the program, the check, the copied line, and what the change makes of it.</summary>
    public static TheoryData<string, string, string, int, string> Copied => new()
    {
        { "area.f90", "program area\n  implicit none\n  type :: box\n    integer :: width, height\n  end type box\n  type(box) :: b\n  integer :: width, height\n  b = box(3, 4)\n  width = b%width\n  height = b%width\n  print *, width * height\nend program area\n", "logic-fortran-copied-line", 10, "  height = b%height" },
        { "box.c", "#include <stdio.h>\n\nstruct box { int width; int height; };\n\nint main(void) {\n    struct box b = { 3, 4 };\n    int width = b.width;\n    int height = b.width;\n    printf(\"%d\\n\", width * height);\n    return 0;\n}\n", "logic-copied-line", 8, "    int height = b.height;" },
        { "box.cpp", "#include <iostream>\n\nstruct Box { int width; int height; };\n\nint main() {\n    Box b{3, 4};\n    int width = b.width;\n    int height = b.width;\n    std::cout << width * height << std::endl;\n    return 0;\n}\n", "logic-copied-line", 8, "    int height = b.height;" },
        { "box.exs", "box = %{width: 3, height: 4}\nwidth = box.width\nheight = box.width\nIO.puts(width * height)\n", "logic-elixir-copied-line", 3, "height = box.height" },
        { "box.fsx", "type Box = { Width: int; Height: int }\nlet box = { Width = 3; Height = 4 }\nlet width = box.Width\nlet height = box.Width\nprintfn \"%d\" (width * height)\n", "logic-fsharp-copied-line", 4, "let height = box.Height" },
        { "Box.java", "public class Box {\n    private final int width;\n    private final int height;\n\n    Box(int width, int height) {\n        this.width = width;\n        this.height = width;\n    }\n\n    public static void main(String[] args) {\n        Box box = new Box(3, 4);\n        System.out.println(box.width * box.height);\n    }\n}\n", "logic-java-copied-line", 7, "        this.height = height;" },
        { "box.js", "const box = { width: 3, height: 4 };\nconst width = box.width;\nconst height = box.width;\nconsole.log(width * height);\n", "logic-copied-line", 3, "const height = box.height;" },
        { "box.lua", "local box = { width = 3, height = 4 }\nlocal width = box.width\nlocal height = box.width\nprint(width * height)\n", "logic-lua-copied-line", 3, "local height = box.height" },
        { "box.php", "<?php\n$box = ['width' => 3, 'height' => 4];\n$width = $box['width'];\n$height = $box['width'];\necho $width * $height, \"\\n\";\n", "logic-php-copied-line", 4, "$height = $box['height'];" },
        { "box.pl", "use strict;\nuse warnings;\n\nmy %box = (width => 3, height => 4);\nmy $width = $box{width};\nmy $height = $box{width};\nprint $width * $height, \"\\n\";\n", "logic-perl-copied-line", 6, "my $height = $box{height};" },
        { "box.ps1", "$box = @{ Width = 3; Height = 4 }\n$width = $box.Width\n$height = $box.Width\nWrite-Output ($width * $height)\n", "logic-powershell-copied-line", 3, "$height = $box.Height" },
        { "box.py", "class Box:\n    def __init__(self, width, height):\n        self.width = width\n        self.height = width\n\n\nbox = Box(3, 4)\nprint(box.width * box.height)\n", "logic-python-copied-line", 4, "        self.height = height" },
        { "box.rb", "box = { width: 3, height: 4 }\nwidth = box[:width]\nheight = box[:width]\nputs width * height\n", "logic-ruby-copied-line", 3, "height = box[:height]" },
        { "main.dart", "void main() {\n  final box = {'width': 3, 'height': 4};\n  final width = box['width']!;\n  final height = box['width']!;\n  print(width * height);\n}\n", "logic-dart-copied-line", 4, "  final height = box['height']!;" },
        { "main.erl", "-module(main).\n-export([main/0]).\n\nmain() ->\n    Box = #{width => 3, height => 4},\n    Width = maps:get(width, Box),\n    Height = maps:get(width, Box),\n    io:format(\"~p~n\", [Width * Height]).\n", "logic-erlang-copied-line", 7, "    Height = maps:get(height, Box)," },
        { "main.go", "package main\n\nimport \"fmt\"\n\ntype box struct{ width, height int }\n\nfunc main() {\n\tb := box{width: 3, height: 4}\n\twidth := b.width\n\theight := b.width\n\tfmt.Println(width * height)\n}\n", "logic-go-copied-line", 10, "\theight := b.height" },
        { "main.rs", "struct Rect {\n    width: i32,\n    height: i32,\n}\n\nfn main() {\n    let r = Rect { width: 3, height: 4 };\n    let width = r.width;\n    let height = r.width;\n    println!(\"{} {}\", width * height, r.height);\n}\n", "logic-rust-copied-line", 9, "    let height = r.height;" },
        { "Main.scala", "case class Box(width: Int, height: Int)\n\nobject Main extends App {\n  val box = Box(3, 4)\n  val width = box.width\n  val height = box.width\n  println(width * height)\n}\n", "logic-scala-copied-line", 6, "  val height = box.height" },
        { "Program.cs", "var box = (Width: 3, Height: 4);\nvar width = box.Width;\nvar height = box.Width;\nConsole.WriteLine(width * height);\n", "logic-copied-line", 3, "var height = box.Height;" },
        { "ranges.py", "xs = [1, 5, 3]\nys = [2, 9, 4]\nmax_x = max(xs)\nmax_y = max(xs)\nprint(max_x, max_y)\n", "logic-python-copied-line", 4, "max_y = max(ys)" },
    };

    [Theory]
    [MemberData(nameof(Copied))]
    public void TheCopiedLineReadsItsOwnPart(string fileName, string code, string check, int line, string changedTo)
    {
        var found = LogicPatterns.Scan(Program(fileName, code)).Where(finding => finding.PatternId.EndsWith("copied-line", StringComparison.Ordinal)).ToList();

        var copied = Assert.Single(found);
        Assert.Equal(check, copied.PatternId);
        Assert.Equal(line, copied.Line);
        Assert.Equal([changedTo], copied.Fix!.NewLines);
        Assert.Equal(line, copied.Fix.StartLine);
    }

    [Theory]
    [InlineData("Pairs.java", "public class Pairs {\n    private int width;\n    private int height;\n\n    Pairs(int width, int height) {\n        this.width = width;\n        this.height = height;\n    }\n\n    public static void main(String[] args) {\n        int left = 0;\n        int right = 0;\n        System.out.println(new Pairs(3, 4).width + left + right);\n    }\n}\n")]
    [InlineData("pairs.js", "const box = { width: 3, height: 4 };\nconst width = box.width;\nconst height = box.height;\nlet count = 0;\nlet total = 0;\nconsole.log(width, height, count, total);\n")]
    [InlineData("pairs.py", "values = [4, 1, 9]\nlow = min(values)\nhigh = max(values)\nx_min = min(values)\nx_max = max(values)\nrow = 0\ncol = 0\nfirst_name = \"Ada\"\nlast_name = \"Ada\"\nwidth = 3\nheight = 4\ntotal_width = width + 1\ntotal_height = height + 1\nprint(low, high, x_min, x_max, row, col, first_name, last_name, total_width, total_height)\n")]
    [InlineData("points.py", "def distance(p, q):\n    dx = p[0] - q[0]\n    dy = p[0] - q[0]\n    return (dx * dx + dy * dy) ** 0.5\n\n\nprint(distance((0, 0), (3, 4)))\n")]
    [InlineData("Cause.cs", "var cause = new RootCause\n{\n    CrashLine = crashLine,\n    IntroducedLine = crashLine,\n};\n")]
    public void PairsWrittenToMatchAreLeftAlone(string fileName, string code)
    {
        Assert.DoesNotContain(LogicPatterns.Scan(Program(fileName, code)), finding => finding.PatternId.EndsWith("copied-line", StringComparison.Ordinal));
    }
}
