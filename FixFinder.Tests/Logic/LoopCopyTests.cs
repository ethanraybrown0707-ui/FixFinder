using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>What a loop seems to change, or give back, and does not: found where it is, and left alone where the loop does what it says.</summary>
public class LoopCopyPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string fileName, string code)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!);
    }

    private static readonly string[] Checks =
    [
        "logic-python-loop-variable-changed", "logic-js-loop-copy-changed", "logic-php-foreach-copy-changed", "logic-ruby-each-for-map",
        "logic-elixir-each-for-map", "logic-js-foreach-for-map", "logic-powershell-foreach-copy-changed", "logic-go-range-index-as-value", "logic-powershell-function-output-returned", "logic-js-for-in-over-array",
    ];

    [Theory]
    [InlineData("main.js", "const marks = [70, 80];\nlet total = 0;\nfor (let mark in marks) {\n  total += mark;\n}\n", "for (let mark of marks) {")]
    [InlineData("main.ts", "const marks: number[] = [70, 80];\nlet total = 0;\nfor (const mark in marks) {\n  total += mark;\n}\n", "for (const mark of marks) {")]
    [InlineData("main.js", "const text = \"Ada Grace\";\nconst names = text.split(\" \");\nfor (const name in names) {\n  console.log(name);\n}\n", "for (const name of names) {")]
    [InlineData("main.js", "const student = { name: \"Ada\", mark: 70 };\nconst fields = Object.keys(student);\nfor (const field in fields) {\n  console.log(field);\n}\n", "for (const field of fields) {")]
    public void ForInOverAnArrayWhoseVariableIsUsedAsAnItemIsFoundAndBecomesForOf(string fileName, string code, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code), finding => finding.PatternId == "logic-js-for-in-over-array");
        Assert.Equal(3, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // The loop's variable read, not given a new value.
    [InlineData("main.py", "marks = [70, 80]\ntotal = 0\nfor mark in marks:\n    total += mark\nprint(total, marks)\n")]
    [InlineData("main.js", "const marks = [70, 80];\nfor (const mark of marks) {\n  console.log(mark);\n}\nconsole.log(marks);\n")]
    [InlineData("main.php", "<?php\n$marks = [70, 80];\n$total = 0;\nforeach ($marks as $mark) {\n    $total += $mark;\n}\necho $total, count($marks);\n")]
    [InlineData("main.ps1", "$marks = @(70, 80)\nforeach ($mark in $marks) {\n    Write-Output $mark\n}\nWrite-Output $marks.Count\n")]
    // An array the file does not make - what Get-Content gives is one line of text when the file has one line.
    [InlineData("main.ps1", "$lines = Get-Content \"marks.txt\"\nforeach ($line in $lines) {\n    $line = $line.Trim()\n}\nWrite-Output $lines\n")]
    // each run for what its block does, its result not kept.
    [InlineData("main.rb", "marks = [70, 80]\nmarks.each { |mark| puts mark }\n")]
    [InlineData("main.exs", "marks = [70, 80]\nEnum.each(marks, fn mark -> IO.puts(mark) end)\n")]
    // What Enum.each gives back given to a name Elixir keeps for values thrown away, or to one never read.
    [InlineData("main.exs", "marks = [70, 80]\n_shown = Enum.each(marks, &IO.puts/1)\n")]
    [InlineData("main.exs", "marks = [70, 80]\nshown = Enum.each(marks, &IO.puts/1)\n")]
    // forEach run for what its function does, and what it gave back never read.
    [InlineData("main.js", "const marks = [70, 80];\nmarks.forEach((mark) => console.log(mark));\n")]
    [InlineData("main.js", "const marks = [70, 80];\nconst shown = marks.forEach((mark) => console.log(mark));\n")]
    // range's one variable used as a position.
    [InlineData("main.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tmarks := []int{70, 80}\n\ttotal := 0\n\tfor i := range marks {\n\t\ttotal += marks[i]\n\t}\n\tfmt.Println(total)\n}\n")]
    // range's one variable printed beside the item at it - a position used as both.
    [InlineData("main.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tmarks := []int{70, 80, 90}\n\tfor i := range marks {\n\t\tfmt.Println(i, marks[i])\n\t}\n}\n")]
    // range over a map gives its keys.
    [InlineData("main.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tages := map[string]int{\"Ada\": 36}\n\tfor name := range ages {\n\t\tfmt.Println(name)\n\t}\n}\n")]
    // for...in's variable used as a position, to read the item at it.
    [InlineData("main.js", "const marks = [70, 80];\nlet total = 0;\nfor (const i in marks) {\n  total += marks[i];\n}\nconsole.log(total);\n")]
    // for...in over an object gives its keys, as meant.
    [InlineData("main.js", "const student = { name: \"Ada\", mark: 70 };\nfor (const key in student) {\n  console.log(key);\n}\n")]
    // for...in over a parameter, which may be an object: nothing says it is an array.
    [InlineData("main.js", "function show(items) {\n  for (const item in items) {\n    console.log(item);\n  }\n}\nshow({ a: 1 });\n")]
    // A function that writes and gives back nothing with return.
    [InlineData("main.ps1", "function Show-Total($marks) {\n    Write-Output \"Total\"\n}\n\n$shown = Show-Total @(70)\n")]
    public void ALoopThatDoesWhatItSaysIsLeftAlone(string fileName, string code) =>
        Assert.DoesNotContain(Scan(fileName, code), finding => Checks.Contains(finding.PatternId));

    [Fact]
    public void PowerShellsLoopVariableReadInItsNewValueBecomesTheItemAtItsPosition()
    {
        var found = Assert.Single(Scan("main.ps1", "$marks = 70, 80, 90\nForEach ($mark in $marks) {\n    $mark = $mark * 2\n}\nWrite-Output $marks\n"),
            finding => finding.PatternId == "logic-powershell-foreach-copy-changed");

        Assert.Equal(3, found.Line);
        Assert.Equal(["for ($index = 0; $index -lt $marks.Count; $index++) {", "    $marks[$index] = $marks[$index] * 2"], found.Fix!.NewLines);
    }
}

/// <summary>
/// Each loop mistake run with the language's own toolchain, as FixFinder Learn runs a lesson, and the check's own change run
/// the same way. A language this computer cannot run is passed over, and said to be.
/// </summary>
public class LoopCopyLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string, string, string, int, string, string> Mistakes => new()
    {
        { "Python", "main.py", "marks = [70, 80, 90]\nfor mark in marks:\n    mark += 5\nprint(marks)\n", "logic-python-loop-variable-changed", 3, "[70, 80, 90]", "[75, 85, 95]" },
        { "JavaScript", "main.js", "const marks = [70, 80, 90];\nfor (let mark of marks) {\n  mark += 5;\n}\nconsole.log(marks);\n", "logic-js-loop-copy-changed", 3, "[ 70, 80, 90 ]", "[ 75, 85, 95 ]" },
        {
            "PHP", "main.php", "<?php\n$marks = [70, 80, 90];\nforeach ($marks as $mark) {\n    $mark += 5;\n}\necho implode(\" \", $marks), \"\\n\";\n",
            "logic-php-foreach-copy-changed", 4, "70 80 90", "75 85 95"
        },
        {
            "PowerShell", "main.ps1", "$marks = @(70, 80, 90)\nforeach ($mark in $marks) {\n    $mark += 5\n}\nWrite-Output ($marks -join \" \")\n",
            "logic-powershell-foreach-copy-changed", 3, "70 80 90", "75 85 95"
        },
        { "Ruby", "main.rb", "marks = [70, 80]\ndoubled = marks.each { |mark| mark * 2 }\np doubled\n", "logic-ruby-each-for-map", 2, "[70, 80]", "[140, 160]" },
        { "Elixir", "main.exs", "marks = [70, 80]\ndoubled = Enum.each(marks, fn mark -> mark * 2 end)\nIO.inspect(doubled)\n", "logic-elixir-each-for-map", 2, ":ok", "[140, 160]" },
        { "Elixir", "main.exs", "marks = [70, 80]\ndoubled = marks |> Enum.each(fn mark -> mark * 2 end)\nIO.inspect(doubled)\n", "logic-elixir-each-for-map", 2, ":ok", "[140, 160]" },
        { "JavaScript", "main.js", "const marks = [70, 80];\nconst doubled = marks.forEach((mark) => mark * 2);\nconsole.log(doubled);\n", "logic-js-foreach-for-map", 2, "undefined", "[ 140, 160 ]" },
        { "TypeScript", "main.ts", "const marks: number[] = [70, 80];\nconst doubled = marks.forEach((mark: number) => mark * 2);\nconsole.log(doubled);\n", "logic-js-foreach-for-map", 2, "undefined", "[ 140, 160 ]" },
        {
            "Go", "main.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tmarks := []int{70, 80, 90}\n\ttotal := 0\n\tfor mark := range marks {\n\t\ttotal += mark\n\t}\n\tfmt.Println(total)\n}\n",
            "logic-go-range-index-as-value", 8, "3", "240"
        },
        {
            "JavaScript", "main.js", "const marks = [70, 80, 90];\nlet total = 0;\nfor (const mark in marks) {\n  total += mark;\n}\nconsole.log(\"Total: \" + total);\n",
            "logic-js-for-in-over-array", 3, "Total: 0012", "Total: 240"
        },
        {
            "TypeScript", "main.ts", "const marks: number[] = [70, 80, 90];\nlet total = 0;\nfor (const mark in marks) {\n  total += mark;\n}\nconsole.log(\"Total: \" + total);\n",
            "logic-js-for-in-over-array", 3, "Total: 0012", "Total: 240"
        },
        {
            "PowerShell", "main.ps1",
            "function Get-Total($marks) {\n    Write-Output \"Adding up the marks\"\n    $sum = 0\n    foreach ($mark in $marks) { $sum += $mark }\n    return $sum\n}\n\n$total = Get-Total @(70, 80)\nWrite-Output ($total + 1)\n",
            "logic-powershell-function-output-returned", 2, "Adding up the marks\n150\n1", "Adding up the marks\n151"
        },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakePrintsTheOldValuesAndTheChangeTheNewOnes(string languageName, string fileName, string code, string check, int line, string mistakePrints, string changePrints)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);

        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == check);
        Assert.Equal(line, found.Line);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(found.Fix!.StartLine - 1, found.Fix.RemoveCount);
        lines.InsertRange(found.Fix.StartLine - 1, found.Fix.NewLines);
        var changed = string.Join("\n", lines) + "\n";

        var language = CodeLanguage.All.Single(each => each.Name == languageName);
        var wrong = await SnippetRunner.RunAsync(language, code);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(language, changed);
        output.WriteLine($"the mistake: {wrong.Said(languageName)}\nthe change:  {right.Said(languageName)}");

        Assert.True(wrong.Shows(Behaviour.Printing(mistakePrints)), wrong.Said(languageName));
        Assert.True(right.Shows(Behaviour.Printing(changePrints)), right.Said(languageName));
    }
}
