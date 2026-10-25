using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>A whole number written with a 0 in front: found where octal changes its value, written without the 0, and left alone for file modes.</summary>
public class OctalLiteralPatternTests : IDisposable
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
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId.EndsWith("octal-literal", StringComparison.Ordinal)).ToList();
    }

    [Theory]
    [InlineData("Main.java", "class Main {\n    int code = 010;\n}\n", "    int code = 10;")]
    [InlineData("main.c", "int main(void) {\n    int times[] = {9, 015, 30};\n}\n", "    int times[] = {9, 15, 30};")]
    [InlineData("main.cpp", "int area() {\n    return 0123;\n}\n", "    return 123;")]
    [InlineData("main.go", "package main\nfunc main() { code := 010; println(code) }\n", "func main() { code := 10; println(code) }")]
    public void ANumberWithAZeroInFrontIsFoundAndWrittenWithoutIt(string fileName, string code, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code));

        Assert.Equal(2, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // The same in octal as in tens, a decimal, and 0 itself.
    [InlineData("Main.java", "class Main {\n    int day = 07;\n    double rate = 0.5;\n    int none = 0;\n}\n")]
    // A file mode, where octal is meant.
    [InlineData("main.c", "int main(void) {\n    chmod(\"marks.txt\", 0644);\n}\n")]
    [InlineData("main.go", "package main\nimport \"os\"\nfunc main() { os.WriteFile(\"marks.txt\", nil, 0644) }\n")]
    // Inside text, and a hexadecimal number.
    [InlineData("Main.java", "class Main {\n    String code = \"010\";\n    int mask = 0x10;\n}\n")]
    public void ANumberThatMeansWhatItSaysIsLeftAlone(string fileName, string code) => Assert.Empty(Scan(fileName, code));
}

/// <summary>A number written with a 0 in front, run with each language's own toolchain, and the check's own change run the same way.</summary>
public class OctalLiteralLiveTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Java", "public class Main {\n    public static void main(String[] args) {\n        int code = 010;\n        System.out.println(code);\n    }\n}\n")]
    [InlineData("C", "#include <stdio.h>\n\nint main(void) {\n    int code = 010;\n    printf(\"%d\\n\", code);\n    return 0;\n}\n")]
    [InlineData("C++", "#include <iostream>\n\nint main() {\n    int code = 010;\n    std::cout << code << '\\n';\n    return 0;\n}\n")]
    [InlineData("Go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tcode := 010\n\tfmt.Println(code)\n}\n")]
    public async Task TheMistakePrintsEightAndTheChangeTen(string language, string mistake)
    {
        var codeLanguage = CodeLanguage.All.Single(each => each.Name == language);
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, (language == "Java" ? "Main" : "main") + codeLanguage.Extensions[0]);
        File.WriteAllText(path, mistake);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId.EndsWith("octal-literal", StringComparison.Ordinal));

        var lines = mistake.TrimEnd('\n').Split('\n').ToList();
        lines[found.Fix!.StartLine - 1] = found.Fix.NewLines.Single();
        var changed = string.Join("\n", lines) + "\n";

        var wrong = await SnippetRunner.RunAsync(codeLanguage, mistake);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(codeLanguage, changed);
        Assert.True(wrong.Shows(Behaviour.Printing("8")), wrong.Said(language));
        Assert.True(right.Shows(Behaviour.Printing("10")), right.Said(language));
    }
}
