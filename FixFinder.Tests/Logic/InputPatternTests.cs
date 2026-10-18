using FixFinder.Core;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Parsing;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>What a program reads in, used as if it were something else: found where it is, with its change, and left alone where it is not.</summary>
public class InputPatternTests : IDisposable
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

    [Theory]
    [InlineData("main.rb", "answer = gets.chomp\nputs answer == \"yes\"\n")]
    [InlineData("main.rb", "answer = gets\nputs answer.length\n")]
    [InlineData("main.php", "<?php\n$answer = trim(fgets(STDIN));\necho $answer == \"yes\";\n")]
    [InlineData("main.rs", "fn main() {\n    let mut answer = String::new();\n    std::io::stdin().read_line(&mut answer).unwrap();\n    println!(\"{}\", answer.trim() == \"yes\");\n}\n")]
    [InlineData("Main.java", "class Main {\n    void read(java.util.Scanner scanner) {\n        int age = scanner.nextInt();\n        int height = scanner.nextInt();\n    }\n}\n")]
    [InlineData("main.ps1", "$age = Read-Host \"Age\"\nif ($age -eq 7) { Write-Output \"seven\" }\n")]
    [InlineData("main.c", "#include <stdio.h>\n\nint main(void) {\n    char line[20];\n    fgets(line, sizeof line, stdin);\n    printf(\"%s\", line);\n    return 0;\n}\n")]
    // The newline skipped already: by std::ws, by ignore, by a format ending in a space, or by getchar.
    [InlineData("main.cpp", "#include <iostream>\n#include <string>\nint main() {\n    int age;\n    std::string name;\n    std::cin >> age;\n    std::getline(std::cin >> std::ws, name);\n}\n")]
    [InlineData("main.cpp", "#include <iostream>\n#include <string>\nint main() {\n    int age;\n    std::string name;\n    std::cin >> age;\n    std::cin.ignore(1000, '\\n');\n    std::getline(std::cin, name);\n}\n")]
    [InlineData("main.c", "#include <stdio.h>\nint main(void) {\n    int age;\n    char name[50];\n    scanf(\"%d \", &age);\n    fgets(name, sizeof name, stdin);\n    return 0;\n}\n")]
    [InlineData("main.c", "#include <stdio.h>\nint main(void) {\n    int age;\n    char name[50];\n    scanf(\"%d\", &age);\n    getchar();\n    fgets(name, sizeof name, stdin);\n    return 0;\n}\n")]
    // The checks' own changes, made already: the newline cut off before strcmp, and the rest of a number's line read and thrown away.
    [InlineData("main.c", "#include <stdio.h>\n#include <string.h>\n\nint main(void) {\n    char answer[20];\n    fgets(answer, sizeof answer, stdin);\n    answer[strcspn(answer, \"\\n\")] = '\\0';\n    if (strcmp(answer, \"yes\") == 0) {\n        printf(\"matched\\n\");\n    }\n    return 0;\n}\n")]
    [InlineData("main.c", "#include <stdio.h>\n#include <string.h>\n\nint main(void) {\n    char answer[20];\n    fgets(answer, sizeof answer, stdin);\n    strtok(answer, \"\\n\");\n    if (strcmp(answer, \"yes\") == 0) {\n        printf(\"matched\\n\");\n    }\n    return 0;\n}\n")]
    [InlineData("Main.java", "import java.util.Scanner;\n\npublic class Main {\n    public static void main(String[] args) {\n        Scanner scanner = new Scanner(System.in);\n        int age = scanner.nextInt();\n        scanner.nextLine();\n        String name = scanner.nextLine();\n        System.out.println(\"[\" + name + \"] is \" + age);\n    }\n}\n")]
    public void InputReadAndUsedAsItIsIsLeftAlone(string fileName, string code) =>
        Assert.DoesNotContain(Scan(fileName, code), finding => finding.PatternId.Contains("input", StringComparison.Ordinal) ||
                                                               finding.PatternId is "logic-c-fgets-newline" or "logic-java-nextline-after-nextint" or "logic-java-scanner-skips-line"
                                                                   or "logic-powershell-read-host-compared-as-text" or "logic-cpp-getline-after-cin" or "logic-c-fgets-after-scanf");

    [Fact]
    public void CppGetlineAfterCinIsFoundAndSkipsTheNewlineFirst()
    {
        var found = Assert.Single(Scan("main.cpp",
            "#include <iostream>\n#include <string>\nusing namespace std;\nint main() {\n    int age;\n    string name;\n    cin >> age;\n    getline(cin, name);\n}\n"),
            finding => finding.PatternId == "logic-cpp-getline-after-cin");

        Assert.Equal(8, found.Line);
        Assert.Equal("    getline(cin >> ws, name);", found.Fix!.NewLines.Single());
    }

    [Fact]
    public void CFgetsAfterScanfIsFoundAndTheRestOfTheLineIsReadFirst()
    {
        var found = Assert.Single(Scan("main.c",
            "#include <stdio.h>\nint main(void) {\n    int age;\n    char name[50];\n    scanf(\"%d\", &age);\n    fgets(name, sizeof name, stdin);\n    return 0;\n}\n"),
            finding => finding.PatternId == "logic-c-fgets-after-scanf");

        Assert.Equal(6, found.Line);
        Assert.Equal((6, 0), (found.Fix!.StartLine, found.Fix.RemoveCount));
        Assert.Equal("    for (int rest = getchar(); rest != '\\n' && rest != EOF; rest = getchar()) { }", found.Fix.NewLines.Single());
    }

    [Fact]
    public void AGoLineComparedWithoutStringsImportedIsFoundWithNoChange()
    {
        var found = Assert.Single(Scan("main.go",
            "package main\n\nimport (\n\t\"bufio\"\n\t\"fmt\"\n\t\"os\"\n)\n\nfunc main() {\n\treader := bufio.NewReader(os.Stdin)\n\tanswer, _ := reader.ReadString('\\n')\n\tfmt.Println(answer == \"yes\")\n}\n"),
            finding => finding.PatternId == "logic-go-input-not-trimmed");

        Assert.Equal(11, found.Line);
        Assert.Null(found.Fix);
    }
}

/// <summary>
/// Each input mistake run with the language's own toolchain, as FixFinder runs a program and typing its input, and the
/// check's own change run the same way. A language this computer cannot run - or a program Windows refused to start - is
/// passed over, and said to be.
/// </summary>
public class InputLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string, string, string, int, string, string> Mistakes => new()
    {
        {
            "Go", "package main\n\nimport (\n\t\"bufio\"\n\t\"fmt\"\n\t\"os\"\n\t\"strings\"\n)\n\nfunc main() {\n\treader := bufio.NewReader(os.Stdin)\n\tfmt.Print(\"Continue? \")\n" +
                  "\tanswer, _ := reader.ReadString('\\n')\n\tif answer == \"yes\" {\n\t\tfmt.Println(\"yes\")\n\t} else {\n\t\tfmt.Println(strings.ToUpper(\"no\"))\n\t}\n}\n",
            "yes\n", "logic-go-input-not-trimmed", 13, "Continue? NO", "Continue? yes"
        },
        {
            "Ruby", "print \"Continue? \"\nanswer = gets\nif answer == \"yes\"\n  puts \"yes\"\nelse\n  puts \"no\"\nend\n",
            "yes\n", "logic-ruby-input-not-chomped", 2, "Continue? no", "Continue? yes"
        },
        {
            "PHP", "<?php\necho \"Continue? \";\n$answer = fgets(STDIN);\nif ($answer == \"yes\") {\n    echo \"yes\\n\";\n} else {\n    echo \"no\\n\";\n}\n",
            "yes\n", "logic-php-input-not-trimmed", 3, "Continue? no", "Continue? yes"
        },
        {
            "C", "#include <stdio.h>\n#include <string.h>\n\nint main(void) {\n    char answer[20];\n    printf(\"Continue? \");\n    fgets(answer, sizeof answer, stdin);\n" +
                 "    if (strcmp(answer, \"yes\") == 0) {\n        printf(\"yes\\n\");\n    } else {\n        printf(\"no\\n\");\n    }\n    return 0;\n}\n",
            "yes\n", "logic-c-fgets-newline", 7, "Continue? no", "Continue? yes"
        },
        {
            "Rust", "use std::io;\n\nfn main() {\n    let mut answer = String::new();\n    io::stdin().read_line(&mut answer).unwrap();\n" +
                    "    if answer == \"yes\" {\n        println!(\"yes\");\n    } else {\n        println!(\"no\");\n    }\n}\n",
            "yes\n", "logic-rust-input-not-trimmed", 6, "no", "yes"
        },
        {
            "Java", "import java.util.Scanner;\n\npublic class Main {\n    public static void main(String[] args) {\n        Scanner scanner = new Scanner(System.in);\n" +
                    "        int age = scanner.nextInt();\n        String name = scanner.nextLine();\n        System.out.println(\"Name: [\" + name + \"] age \" + age);\n    }\n}\n",
            "36\nAda\n", "logic-java-nextline-after-nextint", 7, "Name: [] age 36", "Name: [Ada] age 36"
        },
        {
            "C++", "#include <iostream>\n#include <string>\n\nint main() {\n    int age;\n    std::string name;\n    std::cin >> age;\n    std::getline(std::cin, name);\n" +
                   "    std::cout << \"Name: [\" << name << \"] age \" << age << \"\\n\";\n}\n",
            "21\nAda\n", "logic-cpp-getline-after-cin", 8, "Name: [] age 21", "Name: [Ada] age 21"
        },
        {
            "C", "#include <stdio.h>\n#include <string.h>\n\nint main(void) {\n    int age;\n    char name[50];\n    scanf(\"%d\", &age);\n    fgets(name, sizeof name, stdin);\n" +
                 "    name[strcspn(name, \"\\n\")] = 0;\n    printf(\"Name: [%s] age %d\\n\", name, age);\n    return 0;\n}\n",
            "21\nAda\n", "logic-c-fgets-after-scanf", 8, "Name: [] age 21", "Name: [Ada] age 21"
        },
        {
            "PowerShell", "$age = Read-Host \"Age\"\nif ($age -gt 17) {\n    Write-Output \"adult\"\n} else {\n    Write-Output \"child\"\n}\n",
            "7\n", "logic-powershell-read-host-compared-as-text", 1, "adult", "child"
        },
    };

    /// <summary>Runs code as FixFinder runs a program - building it first when it is built - typing <paramref name="input"/>; null when it could not run here.</summary>
    private async Task<string?> RunAsync(CodeLanguage language, string code, string input, string name)
    {
        var file = PastedCode.Save(code, language, Path.Combine(_temp.Path, name));
        var launch = TargetFactory.FromFile(file);
        if (!launch.Ok || launch.Spec is not { } run)
        {
            output.WriteLine($"Not run on this computer: {launch.Problem}");
            return null;
        }

        var runner = new TargetRunner(language.Parsers());
        if (launch.Compile is { } compile)
        {
            var build = await runner.RunAsync(compile, CancellationToken.None);
            Assert.True(build.Outcome == RunOutcome.ExitedClean, $"{name} did not build:\n{string.Join("\n", build.Lines.Select(line => line.Text))}");
        }

        var result = await runner.RunAsync(run.WithInput(input), CancellationToken.None);
        if (result.Outcome == RunOutcome.LaunchFailed || ApplicationControl.Refused(result))
        {
            output.WriteLine($"Not run on this computer: {result.LaunchError ?? "Windows refused to start it"}");
            return null;
        }

        return string.Join("\n", result.Lines.Where(line => line.Stream == StreamKind.StdOut).Select(line => line.Text.TrimEnd())).Trim();
    }

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakeAnswersWronglyForWhatWasTypedAndTheChangeRightly(
        string languageName, string code, string input, string check, int line, string mistakePrints, string changePrints)
    {
        var language = CodeLanguage.All.Single(each => each.Name == languageName);
        var path = Path.Combine(_temp.Path, "scanned", Path.GetFileName(PastedCode.RelativePathFor(code, language)));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);

        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == check);
        Assert.Equal(line, found.Line);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(found.Fix!.StartLine - 1, found.Fix.RemoveCount);
        lines.InsertRange(found.Fix.StartLine - 1, found.Fix.NewLines);
        var changed = string.Join("\n", lines) + "\n";

        if (await RunAsync(language, code, input, "mistake") is not { } wrong) return;
        var right = await RunAsync(language, changed, input, "change");
        output.WriteLine($"the mistake printed: {wrong}\nthe change printed:  {right}");

        // PowerShell writes what was typed for Read-Host to the output too; what it decided is the last line.
        Assert.Equal(mistakePrints, languageName == "PowerShell" ? wrong.Split('\n')[^1] : wrong);
        Assert.Equal(changePrints, languageName == "PowerShell" ? right?.Split('\n')[^1] : right);
    }
}
