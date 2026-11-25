using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// The loops over the wrong list, run in each language with the output they should print: the change the check offers is the
/// one that makes the program print it, confirmed by running a copy with it. A language this computer cannot run is passed over,
/// and a copy Windows would not let start is not asked to be confirmed.
/// </summary>
public class WrongListLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The file, the program, what it should print, the line the loop starts on, and what the change makes of it.</summary>
    public static TheoryData<string, string, string, int, string> Programs => new()
    {
        { "areas.cpp", "#include <iostream>\n#include <vector>\n\nint main() {\n    std::vector<int> widths{3, 4};\n    std::vector<int> heights{5, 6};\n    int total_width = 0;\n    for (int width : widths) {\n        total_width += width;\n    }\n    int total_height = 0;\n    for (int height : widths) {\n        total_height += height;\n    }\n    std::cout << total_width << \" \" << total_height << std::endl;\n    return 0;\n}\n", "7 11\n", 12, "    for (int height : heights) {" },
        { "areas.exs", "widths = [3, 4]\nheights = [5, 6]\ntotal_width = Enum.reduce(widths, 0, fn width, sum -> sum + width end)\nEnum.each(widths, fn width -> IO.puts(width) end)\ntotal_height = Enum.reduce(widths, 0, fn height, sum -> sum + height end)\nIO.puts(\"#{total_width} #{total_height} #{length(heights)}\")\n", "3\n4\n7 11 2\n", 5, "total_height = Enum.reduce(heights, 0, fn height, sum -> sum + height end)" },
        { "areas.fsx", "let widths = [3; 4]\nlet heights = [5; 6]\nlet mutable totalWidth = 0\nfor width in widths do\n    totalWidth <- totalWidth + width\nlet mutable totalHeight = 0\nfor height in widths do\n    totalHeight <- totalHeight + height\nprintfn \"%d %d\" totalWidth totalHeight\n", "7 11\n", 7, "for height in heights do" },
        { "Areas.java", "import java.util.List;\n\npublic class Areas {\n    public static void main(String[] args) {\n        List<Integer> widths = List.of(3, 4);\n        List<Integer> heights = List.of(5, 6);\n        int totalWidth = 0;\n        for (int width : widths) {\n            totalWidth += width;\n        }\n        int totalHeight = 0;\n        for (int height : widths) {\n            totalHeight += height;\n        }\n        System.out.println(totalWidth + \" \" + totalHeight);\n    }\n}\n", "7 11\n", 12, "        for (int height : heights) {" },
        { "areas.js", "const widths = [3, 4];\nconst heights = [5, 6];\nlet totalWidth = 0;\nfor (const width of widths) {\n    totalWidth += width;\n}\nlet totalHeight = 0;\nfor (const height of widths) {\n    totalHeight += height;\n}\nconsole.log(totalWidth + \" \" + totalHeight);\n", "7 11\n", 8, "for (const height of heights) {" },
        { "areas.lua", "local widths = {3, 4}\nlocal heights = {5, 6}\nlocal total_width = 0\nfor _, width in ipairs(widths) do\n  total_width = total_width + width\nend\nlocal total_height = 0\nfor _, height in ipairs(widths) do\n  total_height = total_height + height\nend\nprint(total_width .. \" \" .. total_height)\n", "7 11\n", 8, "for _, height in ipairs(heights) do" },
        { "areas.php", "<?php\n$widths = [3, 4];\n$heights = [5, 6];\n$totalWidth = 0;\nforeach ($widths as $width) {\n    $totalWidth += $width;\n}\n$totalHeight = 0;\nforeach ($widths as $height) {\n    $totalHeight += $height;\n}\necho \"$totalWidth $totalHeight\\n\";\n", "7 11\n", 9, "foreach ($heights as $height) {" },
        { "areas.pl", "use strict;\nuse warnings;\n\nmy @widths = (3, 4);\nmy @heights = (5, 6);\nmy $total_width = 0;\nforeach my $width (@widths) {\n    $total_width += $width;\n}\nmy $total_height = 0;\nforeach my $height (@widths) {\n    $total_height += $height;\n}\nprint \"$total_width $total_height\\n\";\n", "7 11\n", 11, "foreach my $height (@heights) {" },
        { "areas.ps1", "$widths = @(3, 4)\n$heights = @(5, 6)\n$totalWidth = 0\nforeach ($width in $widths) {\n    $totalWidth += $width\n}\n$totalHeight = 0\nforeach ($height in $widths) {\n    $totalHeight += $height\n}\nWrite-Output \"$totalWidth $totalHeight\"\n", "7 11\n", 8, "foreach ($height in $heights) {" },
        { "areas.py", "widths = [3, 4]\nheights = [5, 6]\ntotal_width = 0\nfor width in widths:\n    total_width += width\ntotal_height = 0\nfor height in widths:\n    total_height += height\nprint(total_width, total_height)\n", "7 11\n", 7, "for height in heights:" },
        { "areas.rb", "widths = [3, 4]\nheights = [5, 6]\ntotal_width = 0\nwidths.each do |width|\n  total_width += width\nend\ntotal_height = 0\nwidths.each do |height|\n  total_height += height\nend\nputs \"#{total_width} #{total_height}\"\n", "7 11\n", 8, "heights.each do |height|" },
        { "bucketsort.py", "def bucketsort(arr, k):\n    counts = [0] * k\n    for x in arr:\n        counts[x] += 1\n\n    sorted_arr = []\n    for i, count in enumerate(arr):\n        sorted_arr.extend([i] * count)\n\n    return sorted_arr\n\n\nprint(bucketsort([2, 0, 1], 3))\n", "[0, 1, 2]\n", 7, "    for i, count in enumerate(counts):" },
        { "main.dart", "void main() {\n  final widths = [3, 4];\n  final heights = [5, 6];\n  var totalWidth = 0;\n  for (final width in widths) {\n    totalWidth += width;\n  }\n  var totalHeight = 0;\n  for (final height in widths) {\n    totalHeight += height;\n  }\n  print('$totalWidth $totalHeight ${heights.length}');\n}\n", "7 11 2\n", 9, "  for (final height in heights) {" },
        { "main.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\twidths := []int{3, 4}\n\theights := []int{5, 6}\n\ttotalWidth := 0\n\tfor _, width := range widths {\n\t\ttotalWidth += width\n\t}\n\ttotalHeight := 0\n\tfor _, height := range widths {\n\t\ttotalHeight += height\n\t}\n\tfmt.Println(totalWidth, totalHeight)\n}\n", "7 11\n", 13, "\tfor _, height := range heights {" },
        { "main.rs", "fn main() {\n    let widths = vec![3, 4];\n    let heights = vec![5, 6];\n    let mut total_width = 0;\n    for width in &widths {\n        total_width += width;\n    }\n    let mut total_height = 0;\n    for height in &widths {\n        total_height += height;\n    }\n    println!(\"{} {} {}\", total_width, total_height, heights.len());\n}\n", "7 11 2\n", 9, "    for height in &heights {" },
        { "Main.scala", "object Main extends App {\n  val widths = List(3, 4)\n  val heights = List(5, 6)\n  var totalWidth = 0\n  for (width <- widths) {\n    totalWidth += width\n  }\n  var totalHeight = 0\n  for (height <- widths) {\n    totalHeight += height\n  }\n  println(s\"$totalWidth $totalHeight\")\n}\n", "7 11\n", 9, "  for (height <- heights) {" },
        { "Program.cs", "int[] widths = { 3, 4 };\nint[] heights = { 5, 6 };\nvar totalWidth = 0;\nforeach (var width in widths)\n{\n    totalWidth += width;\n}\nvar totalHeight = 0;\nforeach (var height in widths)\n{\n    totalHeight += height;\n}\nConsole.WriteLine($\"{totalWidth} {totalHeight}\");\n", "7 11\n", 9, "foreach (var height in heights)" },
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
