using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// The copied-line programs run in each language with the output they should print: the change the check offers is the one
/// that makes the program print it, confirmed by running a copy with it. A language this computer cannot run is passed over,
/// and a copy Windows would not let start is not asked to be confirmed.
/// </summary>
public class CopiedLineLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The file, the program, what it should print, the copied line, and what the change makes of it.</summary>
    public static TheoryData<string, string, string, int, string> Programs => new()
    {
        { "area.f90", "program area\n  implicit none\n  type :: box\n    integer :: width, height\n  end type box\n  type(box) :: b\n  integer :: width, height\n  b = box(3, 4)\n  width = b%width\n  height = b%width\n  print *, width * height\nend program area\n", "12\n", 10, "  height = b%height" },
        { "box.c", "#include <stdio.h>\n\nstruct box { int width; int height; };\n\nint main(void) {\n    struct box b = { 3, 4 };\n    int width = b.width;\n    int height = b.width;\n    printf(\"%d\\n\", width * height);\n    return 0;\n}\n", "12\n", 8, "    int height = b.height;" },
        { "box.cpp", "#include <iostream>\n\nstruct Box { int width; int height; };\n\nint main() {\n    Box b{3, 4};\n    int width = b.width;\n    int height = b.width;\n    std::cout << width * height << std::endl;\n    return 0;\n}\n", "12\n", 8, "    int height = b.height;" },
        { "box.exs", "box = %{width: 3, height: 4}\nwidth = box.width\nheight = box.width\nIO.puts(width * height)\n", "12\n", 3, "height = box.height" },
        { "box.fsx", "type Box = { Width: int; Height: int }\nlet box = { Width = 3; Height = 4 }\nlet width = box.Width\nlet height = box.Width\nprintfn \"%d\" (width * height)\n", "12\n", 4, "let height = box.Height" },
        { "Box.java", "public class Box {\n    private final int width;\n    private final int height;\n\n    Box(int width, int height) {\n        this.width = width;\n        this.height = width;\n    }\n\n    public static void main(String[] args) {\n        Box box = new Box(3, 4);\n        System.out.println(box.width * box.height);\n    }\n}\n", "12\n", 7, "        this.height = height;" },
        { "box.js", "const box = { width: 3, height: 4 };\nconst width = box.width;\nconst height = box.width;\nconsole.log(width * height);\n", "12\n", 3, "const height = box.height;" },
        { "box.lua", "local box = { width = 3, height = 4 }\nlocal width = box.width\nlocal height = box.width\nprint(width * height)\n", "12\n", 3, "local height = box.height" },
        { "box.php", "<?php\n$box = ['width' => 3, 'height' => 4];\n$width = $box['width'];\n$height = $box['width'];\necho $width * $height, \"\\n\";\n", "12\n", 4, "$height = $box['height'];" },
        { "box.pl", "use strict;\nuse warnings;\n\nmy %box = (width => 3, height => 4);\nmy $width = $box{width};\nmy $height = $box{width};\nprint $width * $height, \"\\n\";\n", "12\n", 6, "my $height = $box{height};" },
        { "box.ps1", "$box = @{ Width = 3; Height = 4 }\n$width = $box.Width\n$height = $box.Width\nWrite-Output ($width * $height)\n", "12\n", 3, "$height = $box.Height" },
        { "box.py", "class Box:\n    def __init__(self, width, height):\n        self.width = width\n        self.height = width\n\n\nbox = Box(3, 4)\nprint(box.width * box.height)\n", "12\n", 4, "        self.height = height" },
        { "box.rb", "box = { width: 3, height: 4 }\nwidth = box[:width]\nheight = box[:width]\nputs width * height\n", "12\n", 3, "height = box[:height]" },
        { "main.dart", "void main() {\n  final box = {'width': 3, 'height': 4};\n  final width = box['width']!;\n  final height = box['width']!;\n  print(width * height);\n}\n", "12\n", 4, "  final height = box['height']!;" },
        { "main.erl", "-module(main).\n-export([main/0]).\n\nmain() ->\n    Box = #{width => 3, height => 4},\n    Width = maps:get(width, Box),\n    Height = maps:get(width, Box),\n    io:format(\"~p~n\", [Width * Height]).\n", "12\n", 7, "    Height = maps:get(height, Box)," },
        { "main.go", "package main\n\nimport \"fmt\"\n\ntype box struct{ width, height int }\n\nfunc main() {\n\tb := box{width: 3, height: 4}\n\twidth := b.width\n\theight := b.width\n\tfmt.Println(width * height)\n}\n", "12\n", 10, "\theight := b.height" },
        { "main.rs", "struct Rect {\n    width: i32,\n    height: i32,\n}\n\nfn main() {\n    let r = Rect { width: 3, height: 4 };\n    let width = r.width;\n    let height = r.width;\n    println!(\"{} {}\", width * height, r.height);\n}\n", "12\n", 9, "    let height = r.height;" },
        { "Main.scala", "case class Box(width: Int, height: Int)\n\nobject Main extends App {\n  val box = Box(3, 4)\n  val width = box.width\n  val height = box.width\n  println(width * height)\n}\n", "12\n", 6, "  val height = box.height" },
        { "Program.cs", "var box = (Width: 3, Height: 4);\nvar width = box.Width;\nvar height = box.Width;\nConsole.WriteLine(width * height);\n", "12\n", 3, "var height = box.Height;" },
        { "ranges.py", "xs = [1, 5, 3]\nys = [2, 9, 4]\nmax_x = max(xs)\nmax_y = max(xs)\nprint(max_x, max_y)\n", "5 9\n", 4, "max_y = max(ys)" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItPrintWhatItShould(string fileName, string code, string expected, int line, string changedTo)
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
        var report = await new ProgramChecker(http, new FixSourceRegistry())
        {
            Language = CodeLanguage.Of(program) ?? CodeLanguage.Any,
            Expected = ExpectedBehaviour.From([new ExpectedRun(null, expected)], ""),
        }.CheckAsync(launch);
        foreach (var each in report.Findings)
            output.WriteLine($"[{each.RuleId}] {each.Line}: {each.Title} :: {string.Join("|", each.Fix?.NewLines ?? [])} :: {each.Verified.Summary}");

        if (report.Notes.Any(ApplicationControl.Refused) || report.Findings.Any(finding => ApplicationControl.Refused(finding.Verified.Summary)))
        {
            output.WriteLine("Windows did not let the program start here, so the change could not be confirmed.");
            return;
        }

        var changed = report.Findings.FirstOrDefault(finding => finding.Fix is { } fix && fix.StartLine == line && fix.NewLines.SequenceEqual([changedTo]));
        Assert.True(changed is not null, $"no change to line {line} among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");
        if (changed!.RuleId == "wrong-output") Assert.True(changed.Verified.IsVerified, changed.Verified.Summary);

        Assert.Equal(code, File.ReadAllText(program));
    }
}
