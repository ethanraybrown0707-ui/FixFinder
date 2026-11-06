using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// A loop that goes through one list while naming its items after another - for height in widths, under a loop over widths
/// named width, or QuixBugs' bucketsort going through arr for each count after filling counts from it - in each language,
/// with the change that goes through the list the items are named for; and loops over the lists they mean, which are left
/// alone. Each row is what the check gave for the program when these were written.
/// </summary>
public class WrongListTests : IDisposable
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

    /// <summary>The file, the program, the check, the line the loop starts on, and what the change makes of it.</summary>
    public static TheoryData<string, string, string, int, string> Loops => new()
    {
        { "areas.cpp", "#include <iostream>\n#include <vector>\n\nint main() {\n    std::vector<int> widths{3, 4};\n    std::vector<int> heights{5, 6};\n    int total_width = 0;\n    for (int width : widths) {\n        total_width += width;\n    }\n    int total_height = 0;\n    for (int height : widths) {\n        total_height += height;\n    }\n    std::cout << total_width << \" \" << total_height << std::endl;\n    return 0;\n}\n", "logic-wrong-list", 12, "    for (int height : heights) {" },
        { "areas.exs", "widths = [3, 4]\nheights = [5, 6]\ntotal_width = Enum.reduce(widths, 0, fn width, sum -> sum + width end)\nEnum.each(widths, fn width -> IO.puts(width) end)\ntotal_height = Enum.reduce(widths, 0, fn height, sum -> sum + height end)\nIO.puts(\"#{total_width} #{total_height} #{length(heights)}\")\n", "logic-elixir-wrong-list", 5, "total_height = Enum.reduce(heights, 0, fn height, sum -> sum + height end)" },
        { "areas.fsx", "let widths = [3; 4]\nlet heights = [5; 6]\nlet mutable totalWidth = 0\nfor width in widths do\n    totalWidth <- totalWidth + width\nlet mutable totalHeight = 0\nfor height in widths do\n    totalHeight <- totalHeight + height\nprintfn \"%d %d\" totalWidth totalHeight\n", "logic-fsharp-wrong-list", 7, "for height in heights do" },
        { "Areas.java", "import java.util.List;\n\npublic class Areas {\n    public static void main(String[] args) {\n        List<Integer> widths = List.of(3, 4);\n        List<Integer> heights = List.of(5, 6);\n        int totalWidth = 0;\n        for (int width : widths) {\n            totalWidth += width;\n        }\n        int totalHeight = 0;\n        for (int height : widths) {\n            totalHeight += height;\n        }\n        System.out.println(totalWidth + \" \" + totalHeight);\n    }\n}\n", "logic-java-wrong-list", 12, "        for (int height : heights) {" },
        { "areas.js", "const widths = [3, 4];\nconst heights = [5, 6];\nlet totalWidth = 0;\nfor (const width of widths) {\n    totalWidth += width;\n}\nlet totalHeight = 0;\nfor (const height of widths) {\n    totalHeight += height;\n}\nconsole.log(totalWidth + \" \" + totalHeight);\n", "logic-wrong-list", 8, "for (const height of heights) {" },
        { "areas.lua", "local widths = {3, 4}\nlocal heights = {5, 6}\nlocal total_width = 0\nfor _, width in ipairs(widths) do\n  total_width = total_width + width\nend\nlocal total_height = 0\nfor _, height in ipairs(widths) do\n  total_height = total_height + height\nend\nprint(total_width .. \" \" .. total_height)\n", "logic-lua-wrong-list", 8, "for _, height in ipairs(heights) do" },
        { "areas.php", "<?php\n$widths = [3, 4];\n$heights = [5, 6];\n$totalWidth = 0;\nforeach ($widths as $width) {\n    $totalWidth += $width;\n}\n$totalHeight = 0;\nforeach ($widths as $height) {\n    $totalHeight += $height;\n}\necho \"$totalWidth $totalHeight\\n\";\n", "logic-php-wrong-list", 9, "foreach ($heights as $height) {" },
        { "areas.pl", "use strict;\nuse warnings;\n\nmy @widths = (3, 4);\nmy @heights = (5, 6);\nmy $total_width = 0;\nforeach my $width (@widths) {\n    $total_width += $width;\n}\nmy $total_height = 0;\nforeach my $height (@widths) {\n    $total_height += $height;\n}\nprint \"$total_width $total_height\\n\";\n", "logic-perl-wrong-list", 11, "foreach my $height (@heights) {" },
        { "areas.ps1", "$widths = @(3, 4)\n$heights = @(5, 6)\n$totalWidth = 0\nforeach ($width in $widths) {\n    $totalWidth += $width\n}\n$totalHeight = 0\nforeach ($height in $widths) {\n    $totalHeight += $height\n}\nWrite-Output \"$totalWidth $totalHeight\"\n", "logic-powershell-wrong-list", 8, "foreach ($height in $heights) {" },
        { "areas.py", "widths = [3, 4]\nheights = [5, 6]\ntotal_width = 0\nfor width in widths:\n    total_width += width\ntotal_height = 0\nfor height in widths:\n    total_height += height\nprint(total_width, total_height)\n", "logic-python-wrong-list", 7, "for height in heights:" },
        { "areas.rb", "widths = [3, 4]\nheights = [5, 6]\ntotal_width = 0\nwidths.each do |width|\n  total_width += width\nend\ntotal_height = 0\nwidths.each do |height|\n  total_height += height\nend\nputs \"#{total_width} #{total_height}\"\n", "logic-ruby-wrong-list", 8, "heights.each do |height|" },
        { "bucketsort.py", "def bucketsort(arr, k):\n    counts = [0] * k\n    for x in arr:\n        counts[x] += 1\n\n    sorted_arr = []\n    for i, count in enumerate(arr):\n        sorted_arr.extend([i] * count)\n\n    return sorted_arr\n\n\nprint(bucketsort([2, 0, 1], 3))\n", "logic-python-wrong-list", 7, "    for i, count in enumerate(counts):" },
        { "main.dart", "void main() {\n  final widths = [3, 4];\n  final heights = [5, 6];\n  var totalWidth = 0;\n  for (final width in widths) {\n    totalWidth += width;\n  }\n  var totalHeight = 0;\n  for (final height in widths) {\n    totalHeight += height;\n  }\n  print('$totalWidth $totalHeight ${heights.length}');\n}\n", "logic-dart-wrong-list", 9, "  for (final height in heights) {" },
        { "main.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\twidths := []int{3, 4}\n\theights := []int{5, 6}\n\ttotalWidth := 0\n\tfor _, width := range widths {\n\t\ttotalWidth += width\n\t}\n\ttotalHeight := 0\n\tfor _, height := range widths {\n\t\ttotalHeight += height\n\t}\n\tfmt.Println(totalWidth, totalHeight)\n}\n", "logic-go-wrong-list", 13, "\tfor _, height := range heights {" },
        { "main.rs", "fn main() {\n    let widths = vec![3, 4];\n    let heights = vec![5, 6];\n    let mut total_width = 0;\n    for width in &widths {\n        total_width += width;\n    }\n    let mut total_height = 0;\n    for height in &widths {\n        total_height += height;\n    }\n    println!(\"{} {} {}\", total_width, total_height, heights.len());\n}\n", "logic-rust-wrong-list", 9, "    for height in &heights {" },
        { "Main.scala", "object Main extends App {\n  val widths = List(3, 4)\n  val heights = List(5, 6)\n  var totalWidth = 0\n  for (width <- widths) {\n    totalWidth += width\n  }\n  var totalHeight = 0\n  for (height <- widths) {\n    totalHeight += height\n  }\n  println(s\"$totalWidth $totalHeight\")\n}\n", "logic-scala-wrong-list", 9, "  for (height <- heights) {" },
        { "Program.cs", "int[] widths = { 3, 4 };\nint[] heights = { 5, 6 };\nvar totalWidth = 0;\nforeach (var width in widths)\n{\n    totalWidth += width;\n}\nvar totalHeight = 0;\nforeach (var height in widths)\n{\n    totalHeight += height;\n}\nConsole.WriteLine($\"{totalWidth} {totalHeight}\");\n", "logic-wrong-list", 9, "foreach (var height in heights)" },
    };

    [Theory]
    [MemberData(nameof(Loops))]
    public void TheLoopGoesThroughTheListItsItemsAreNamedFor(string fileName, string code, string check, int line, string changedTo)
    {
        var found = LogicPatterns.Scan(Program(fileName, code)).Where(finding => finding.PatternId.EndsWith("wrong-list", StringComparison.Ordinal)).ToList();

        var wrong = Assert.Single(found);
        Assert.Equal(check, wrong.PatternId);
        Assert.Equal(line, wrong.Line);
        Assert.Equal([changedTo], wrong.Fix!.NewLines);
        Assert.Equal(line, wrong.Fix.StartLine);
    }

    [Theory]
    [InlineData("Right.java", "import java.util.List;\n\npublic class Right {\n    public static void main(String[] args) {\n        List<String> files = List.of(\"a\", \"b\");\n        List<String> others = List.of(\"c\");\n        for (String file : files) {\n            System.out.println(file);\n        }\n        for (String file : others) {\n            System.out.println(file);\n        }\n    }\n}\n")]
    [InlineData("right.py", "widths = [3, 4]\nheights = [5, 6]\nfor width in widths:\n    print(width)\nfor height in heights:\n    print(height)\nlines = [\"a\", \"b\"]\nmasked = [line.strip() for line in lines]\nfor line in masked:\n    print(line)\n")]
    public void ALoopOverTheListItMeansIsLeftAlone(string fileName, string code)
    {
        Assert.DoesNotContain(LogicPatterns.Scan(Program(fileName, code)), finding => finding.PatternId.EndsWith("wrong-list", StringComparison.Ordinal));
    }
}
