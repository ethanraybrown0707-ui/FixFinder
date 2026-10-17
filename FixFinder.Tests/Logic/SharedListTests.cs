using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A second name for a list or an array, taken as a copy: found where the list is changed through one name and the other read
/// after it, with a copy as the change, and left alone where nothing shows the two are one.
/// </summary>
public class SharedListPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string fileName, string code, string patternId)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == patternId).ToList();
    }

    private static string InsideJavaMain(string body) => "import java.util.*;\npublic class Main {\n    public static void main(String[] args) {\n" + body + "    }\n}\n";

    [Theory]
    [InlineData("main.py", "logic-python-list-shared", "marks = [70, 80, 90]\nbackup = marks\nmarks.append(100)\nprint(backup)\n", 2, "backup = marks.copy()")]
    [InlineData("main.py", "logic-python-list-shared", "marks = [70, 80]\nold = marks\nold[0] = 100\nprint(marks)\n", 2, "old = marks.copy()")]
    [InlineData("main.py", "logic-python-list-shared", "marks = input().split()\nbackup = marks\nmarks.sort()\nprint(f\"{backup}\")\n", 2, "backup = marks.copy()")]
    [InlineData("Main.java", "logic-java-array-shared", "        int[] marks = {70, 80, 90};\n        int[] backup = marks;\n        marks[0] = 100;\n        System.out.println(backup[0]);\n", 5, "        int[] backup = marks.clone();")]
    [InlineData("Main.java", "logic-java-array-shared", "        List<Integer> marks = new ArrayList<>(List.of(70));\n        List<Integer> backup = marks;\n        marks.add(100);\n        System.out.println(backup);\n", 5, "        List<Integer> backup = new java.util.ArrayList<>(marks);")]
    [InlineData("main.js", "logic-js-array-shared", "const marks = [70, 80, 90];\nconst backup = marks;\nmarks.push(100);\nconsole.log(backup);\n", 2, "const backup = [...marks];")]
    [InlineData("main.ts", "logic-js-array-shared", "const marks: number[] = [70, 80];\nconst backup = marks;\nmarks.push(100);\nconsole.log(`${backup}`);\n", 2, "const backup = [...marks];")]
    [InlineData("main.cs", "logic-csharp-array-shared", "int[] marks = { 70, 80, 90 };\nint[] backup = marks;\nmarks[0] = 100;\nConsole.WriteLine(backup[0]);\n", 2, "int[] backup = marks.ToArray();")]
    [InlineData("main.cs", "logic-csharp-array-shared", "var marks = new List<int> { 70 };\nvar backup = marks;\nmarks.Add(100);\nConsole.WriteLine(backup.Count);\n", 2, "var backup = marks.ToList();")]
    [InlineData("main.rb", "logic-ruby-array-shared", "marks = [70, 80, 90]\nbackup = marks\nmarks << 100\np backup\n", 2, "backup = marks.dup")]
    [InlineData("main.dart", "logic-dart-list-shared", "  var marks = [70, 80, 90];\n  var backup = marks;\n  marks.add(100);\n  print(backup);\n", 2, "  var backup = [...marks];")]
    [InlineData("main.go", "logic-go-slice-shared", "\tmarks := []int{70, 80, 90}\n\tbackup := marks\n\tmarks[0] = 100\n\tfmt.Println(backup)\n", 2, "\tbackup := append([]int{}, marks...)")]
    [InlineData("main.lua", "logic-lua-table-shared", "local marks = {70, 80, 90}\nlocal backup = marks\nmarks[1] = 100\nprint(backup[1])\n", 2, "local backup = {table.unpack(marks)}")]
    [InlineData("main.ps1", "logic-powershell-array-shared", "$marks = @(70, 80, 90)\n$backup = $marks\n$marks[0] = 100\nWrite-Output $backup[0]\n", 2, "$backup = $marks.Clone()")]
    public void ASecondNameForAListThatIsChangedIsFoundAndBecomesACopy(string fileName, string patternId, string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, fileName == "Main.java" ? InsideJavaMain(code) : code, patternId));
        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // Never changed after the second name is given.
    [InlineData("main.py", "logic-python-list-shared", "marks = [70, 80]\nbackup = marks\nprint(backup)\n")]
    // Changed, but the other name is not read after it.
    [InlineData("main.py", "logic-python-list-shared", "marks = [70, 80]\nbackup = marks\nmarks.append(1)\nprint(marks)\n")]
    // marks given a new list before the change: backup keeps the old one.
    [InlineData("main.py", "logic-python-list-shared", "marks = [70, 80]\nbackup = marks\nmarks = [1]\nmarks.append(2)\nprint(backup)\n")]
    // A number, not a list.
    [InlineData("main.py", "logic-python-list-shared", "count = 3\ntotal = count\ncount += 1\nprint(total)\n")]
    // A copy already.
    [InlineData("main.py", "logic-python-list-shared", "marks = [70, 80]\nbackup = marks.copy()\nmarks.append(1)\nprint(backup)\n")]
    [InlineData("main.js", "logic-js-array-shared", "const marks = [70, 80];\nconst backup = [...marks];\nmarks.push(1);\nconsole.log(backup);\n")]
    [InlineData("main.cs", "logic-csharp-array-shared", "int[] marks = { 70, 80 };\nint[] backup = marks.ToArray();\nmarks[0] = 1;\nConsole.WriteLine(backup[0]);\n")]
    // A Lua table of named fields, which table.unpack would not copy, is not taken for a list.
    [InlineData("main.lua", "logic-lua-table-shared", "local student = {name = \"Ada\"}\nlocal saved = student\nstudent[\"name\"] = \"Grace\"\nprint(saved.name)\n")]
    // PowerShell's += makes a new array, so $backup keeps the old one.
    [InlineData("main.ps1", "logic-powershell-array-shared", "$marks = @(70, 80)\n$backup = $marks\n$marks += 100\nWrite-Output $backup.Count\n")]
    public void ASecondNameNothingShowsToBeTheSameListIsLeftAlone(string fileName, string patternId, string code) =>
        Assert.Empty(Scan(fileName, code, patternId));
}

/// <summary>
/// The mistake and its change run with each language's own toolchain, as FixFinder Learn runs a lesson: the mistake's second
/// name shows the change made through the first, and the change's copy keeps the values it had. A language this computer
/// cannot run is passed over, and said to be.
/// </summary>
public class SharedListLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string, string, string, int, string, string> Mistakes => new()
    {
        { "Python", "main.py", "logic-python-list-shared", "marks = [70, 80, 90]\nbackup = marks\nmarks.append(100)\nprint(backup)\n", 2, "[70, 80, 90, 100]", "[70, 80, 90]" },
        {
            "Java", "Main.java", "logic-java-array-shared",
            "public class Main {\n    public static void main(String[] args) {\n        int[] marks = {70, 80, 90};\n        int[] backup = marks;\n        marks[0] = 100;\n        System.out.println(backup[0]);\n    }\n}\n",
            4, "100", "70"
        },
        {
            "Java", "Main.java", "logic-java-array-shared",
            "import java.util.List;\nimport java.util.ArrayList;\n\npublic class Main {\n    public static void main(String[] args) {\n        List<Integer> marks = new ArrayList<>(List.of(70, 80, 90));\n        List<Integer> backup = marks;\n        marks.add(100);\n        System.out.println(backup);\n    }\n}\n",
            7, "[70, 80, 90, 100]", "[70, 80, 90]"
        },
        { "JavaScript", "main.js", "logic-js-array-shared", "const marks = [70, 80, 90];\nconst backup = marks;\nmarks.push(100);\nconsole.log(backup);\n", 2, "[ 70, 80, 90, 100 ]", "[ 70, 80, 90 ]" },
        { "TypeScript", "main.ts", "logic-js-array-shared", "const marks: number[] = [70, 80, 90];\nconst backup = marks;\nmarks.push(100);\nconsole.log(backup);\n", 2, "[ 70, 80, 90, 100 ]", "[ 70, 80, 90 ]" },
        { "C#", "main.cs", "logic-csharp-array-shared", "int[] marks = { 70, 80, 90 };\nint[] backup = marks;\nmarks[0] = 100;\nConsole.WriteLine(backup[0]);\n", 2, "100", "70" },
        { "Ruby", "main.rb", "logic-ruby-array-shared", "marks = [70, 80, 90]\nbackup = marks\nmarks << 100\np backup\n", 2, "[70, 80, 90, 100]", "[70, 80, 90]" },
        {
            "Dart", "main.dart", "logic-dart-list-shared",
            "void main() {\n  var marks = [70, 80, 90];\n  var backup = marks;\n  marks.add(100);\n  print(backup);\n}\n", 3, "[70, 80, 90, 100]", "[70, 80, 90]"
        },
        {
            "Go", "main.go", "logic-go-slice-shared",
            "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tmarks := []int{70, 80, 90}\n\tbackup := marks\n\tmarks[0] = 100\n\tfmt.Println(backup)\n}\n", 7, "[100 80 90]", "[70 80 90]"
        },
        { "Lua", "main.lua", "logic-lua-table-shared", "local marks = {70, 80, 90}\nlocal backup = marks\nmarks[1] = 100\nprint(backup[1])\n", 2, "100", "70" },
        { "PowerShell", "main.ps1", "logic-powershell-array-shared", "$marks = @(70, 80, 90)\n$backup = $marks\n$marks[0] = 100\nWrite-Output $backup[0]\n", 2, "100", "70" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakesSecondNameShowsTheChangeAndTheCopyKeepsTheValues(string languageName, string fileName, string patternId, string code, int line, string mistakePrints, string changePrints)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);

        var language = CodeLanguage.All.Single(each => each.Name == languageName);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == patternId);
        Assert.Equal(line, found.Line);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines[found.Fix!.StartLine - 1] = found.Fix.NewLines.Single();
        var changed = string.Join("\n", lines) + "\n";

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
