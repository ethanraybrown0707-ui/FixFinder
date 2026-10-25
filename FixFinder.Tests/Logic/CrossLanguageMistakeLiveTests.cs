using FixFinder.Core;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Mistakes made the same way in many languages, each run with the language's own toolchain the way FixFinder Learn runs a
/// lesson, and the check's own change run the same way: the mistake prints what it was seen to, and the change what was meant.
/// A language this computer cannot run - or a program Windows refused to start - is passed over, and said to be.
/// </summary>
public class CrossLanguageMistakeLiveTests(ITestOutputHelper output)
{
    /// <summary>Each: the language, the check, the mistake, what it prints, and what its change prints.</summary>
    public static TheoryData<string, string, string, string, string> Mistakes => new()
    {
        // =+ for +=: 5 and 10 added, 10 printed for 15.
        { "Python", "logic-python-plus-equals-reversed", "total = 0\nfor mark in [5, 10]:\n    total =+ mark\nprint(total)\n", "10", "15" },
        {
            "Java", "logic-plus-equals-reversed",
            "public class Main {\n    public static void main(String[] args) {\n        int total = 0;\n        for (int mark : new int[] {5, 10}) {\n            total =+ mark;\n        }\n        System.out.println(total);\n    }\n}\n",
            "10", "15"
        },
        { "C", "logic-plus-equals-reversed", "#include <stdio.h>\nint main(void) {\n    int total = 0;\n    int marks[] = {5, 10};\n    for (int i = 0; i < 2; i++) {\n        total =+ marks[i];\n    }\n    printf(\"%d\\n\", total);\n    return 0;\n}\n", "10", "15" },
        { "C++", "logic-plus-equals-reversed", "#include <iostream>\nint main() {\n    int total = 0;\n    for (int mark : {5, 10}) {\n        total =+ mark;\n    }\n    std::cout << total << \"\\n\";\n}\n", "10", "15" },
        { "C#", "logic-plus-equals-reversed", "int total = 0;\nforeach (int mark in new[] { 5, 10 })\n{\n    total =+ mark;\n}\nConsole.WriteLine(total);\n", "10", "15" },
        { "JavaScript", "logic-plus-equals-reversed", "let total = 0;\nfor (const mark of [5, 10]) {\n    total =+ mark;\n}\nconsole.log(total);\n", "10", "15" },
        { "TypeScript", "logic-plus-equals-reversed", "let total: number = 0;\nfor (const mark of [5, 10]) {\n    total =+ mark;\n}\nconsole.log(total);\n", "10", "15" },
        { "Go", "logic-go-plus-equals-reversed", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\ttotal := 0\n\tfor _, mark := range []int{5, 10} {\n\t\ttotal =+ mark\n\t}\n\tfmt.Println(total)\n}\n", "10", "15" },
        { "Ruby", "logic-ruby-plus-equals-reversed", "total = 0\n[5, 10].each do |mark|\n  total =+ mark\nend\nputs total\n", "10", "15" },
        { "PHP", "logic-php-plus-equals-reversed", "<?php\n$total = 0;\nforeach ([5, 10] as $mark) {\n    $total =+ $mark;\n}\necho $total, PHP_EOL;\n", "10", "15" },
        { "PowerShell", "logic-powershell-plus-equals-reversed", "$total = 0\nforeach ($mark in 5, 10) {\n    $total =+ $mark\n}\nWrite-Output $total\n", "10", "15" },

        // % 2 == 1 for an odd number: -3 taken for even.
        {
            "Java", "logic-odd-test-negative",
            "public class Main {\n    public static void main(String[] args) {\n        int n = -3;\n        System.out.println(n % 2 == 1 ? \"odd\" : \"even\");\n    }\n}\n", "even", "odd"
        },
        { "C", "logic-odd-test-negative", "#include <stdio.h>\nint main(void) {\n    int n = -3;\n    puts(n % 2 == 1 ? \"odd\" : \"even\");\n    return 0;\n}\n", "even", "odd" },
        { "C++", "logic-odd-test-negative", "#include <iostream>\nint main() {\n    int n = -3;\n    std::cout << (n % 2 == 1 ? \"odd\" : \"even\") << \"\\n\";\n}\n", "even", "odd" },
        { "C#", "logic-odd-test-negative", "int n = -3;\nConsole.WriteLine(n % 2 == 1 ? \"odd\" : \"even\");\n", "even", "odd" },
        { "JavaScript", "logic-odd-test-negative", "const n = -3;\nconsole.log(n % 2 === 1 ? \"odd\" : \"even\");\n", "even", "odd" },
        { "Go", "logic-go-odd-test-negative", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tn := -3\n\tif n%2 == 1 {\n\t\tfmt.Println(\"odd\")\n\t} else {\n\t\tfmt.Println(\"even\")\n\t}\n}\n", "even", "odd" },
        { "Rust", "logic-rust-odd-test-negative", "fn main() {\n    let n: i32 = -3;\n    if n % 2 == 1 {\n        println!(\"odd\");\n    } else {\n        println!(\"even\");\n    }\n}\n", "even", "odd" },
        { "Scala", "logic-scala-odd-test-negative", "@main def run(): Unit =\n  val n = -3\n  if n % 2 == 1 then println(\"odd\") else println(\"even\")\n", "even", "odd" },
        { "PHP", "logic-php-odd-test-negative", "<?php\n$n = -3;\necho $n % 2 == 1 ? \"odd\" : \"even\", PHP_EOL;\n", "even", "odd" },
        {
            "Fortran", "logic-fortran-odd-test-negative",
            "program parity\n  implicit none\n  integer :: n\n  n = -3\n  if (mod(n, 2) == 1) then\n    print *, 'odd'\n  else\n    print *, 'even'\n  end if\nend program parity\n", " even", " odd"
        },
        {
            "Erlang", "logic-erlang-odd-test-negative",
            "-module(parity).\n-export([main/0]).\n\ndescribe(N) ->\n    if\n        N rem 2 == 1 -> \"odd\";\n        true -> \"even\"\n    end.\n\nmain() ->\n    io:format(\"~s~n\", [describe(-3)]).\n", "even", "odd"
        },
        { "Elixir", "logic-elixir-odd-test-negative", "n = -3\nif rem(n, 2) == 1 do\n  IO.puts(\"odd\")\nelse\n  IO.puts(\"even\")\nend\n", "even", "odd" },
        { "F#", "logic-fsharp-odd-test-negative", "let n = -3\nif n % 2 = 1 then printfn \"odd\" else printfn \"even\"\n", "even", "odd" },
        { "PowerShell", "logic-powershell-odd-test-negative", "$n = -3\nif ($n % 2 -eq 1) {\n    Write-Output \"odd\"\n} else {\n    Write-Output \"even\"\n}\n", "even", "odd" },

        // A date written with the wrong letters, for 9:30 on 5 February 2024 - and 30 December 2024 for Java's YYYY.
        { "Python", "logic-python-date-format-minutes", "from datetime import datetime\nwhen = datetime(2024, 2, 5, 9, 30)\nprint(when.strftime(\"%Y-%M-%d\"))\n", "2024-30-05", "2024-02-05" },
        { "Ruby", "logic-ruby-date-format-minutes", "whenever = Time.new(2024, 2, 5, 9, 30)\nputs whenever.strftime(\"%Y-%M-%d\")\n", "2024-30-05", "2024-02-05" },
        { "Perl", "logic-perl-date-format-minutes", "use strict;\nuse warnings;\nuse POSIX qw(strftime);\nprint strftime(\"%Y-%M-%d\", 0, 30, 9, 5, 1, 124), \"\\n\";\n", "2024-30-05", "2024-02-05" },
        { "Elixir", "logic-elixir-date-format-minutes", "whenever = ~N[2024-02-05 09:30:00]\nIO.puts(Calendar.strftime(whenever, \"%Y-%M-%d\"))\n", "2024-30-05", "2024-02-05" },
        {
            "Java", "logic-java-date-pattern-letters",
            "import java.time.LocalDate;\nimport java.time.format.DateTimeFormatter;\n\npublic class Main {\n    public static void main(String[] args) {\n        System.out.println(LocalDate.of(2024, 12, 30).format(DateTimeFormatter.ofPattern(\"YYYY-MM-dd\")));\n    }\n}\n",
            "2025-12-30", "2024-12-30"
        },
        {
            "Java", "logic-java-date-pattern-letters",
            "import java.time.LocalDate;\nimport java.time.format.DateTimeFormatter;\n\npublic class Main {\n    public static void main(String[] args) {\n        System.out.println(LocalDate.of(2024, 2, 5).format(DateTimeFormatter.ofPattern(\"yyyy-MM-DD\")));\n    }\n}\n",
            "2024-02-36", "2024-02-05"
        },
        {
            "Java", "logic-java-date-pattern-letters",
            "import java.time.LocalDateTime;\nimport java.time.format.DateTimeFormatter;\n\npublic class Main {\n    public static void main(String[] args) {\n        System.out.println(LocalDateTime.of(2024, 2, 5, 9, 30).format(DateTimeFormatter.ofPattern(\"yyyy-mm-dd\")));\n    }\n}\n",
            "2024-30-05", "2024-02-05"
        },
        { "C#", "logic-csharp-date-format-letters", "var when = new DateTime(2024, 2, 5, 9, 30, 0);\nConsole.WriteLine(when.ToString(\"yyyy-mm-dd\"));\n", "2024-30-05", "2024-02-05" },
        { "C#", "logic-csharp-date-format-letters", "var when = new DateTime(2024, 2, 5, 9, 30, 0);\nConsole.WriteLine($\"{when:YYYY-MM-DD}\");\n", "YYYY-02-DD", "2024-02-05" },
        { "F#", "logic-fsharp-date-format-letters", "let whenever = System.DateTime(2024, 2, 5, 9, 30, 0)\nprintfn \"%s\" (whenever.ToString(\"yyyy-mm-dd\"))\n", "2024-30-05", "2024-02-05" },
        {
            "PowerShell", "logic-powershell-date-format-letters",
            "$whenever = Get-Date -Year 2024 -Month 2 -Day 5 -Hour 9 -Minute 30\nWrite-Output (Get-Date $whenever -Format \"yyyy-mm-dd\")\n", "2024-30-05", "2024-02-05"
        },
        {
            "Go", "logic-go-date-layout-letters",
            "package main\n\nimport (\n\t\"fmt\"\n\t\"time\"\n)\n\nfunc main() {\n\twhen := time.Date(2024, 2, 5, 9, 30, 0, 0, time.UTC)\n\tfmt.Println(when.Format(\"YYYY-MM-DD\"))\n}\n",
            "YYYY-MM-DD", "2024-02-05"
        },
        {
            "Go", "logic-go-date-layout-letters",
            "package main\n\nimport (\n\t\"fmt\"\n\t\"time\"\n)\n\nfunc main() {\n\twhen := time.Date(2024, 2, 5, 9, 30, 0, 0, time.UTC)\n\tfmt.Println(when.Format(\"dd/mm/yyyy\"))\n}\n",
            "dd/mm/yyyy", "05/02/2024"
        },
        { "PHP", "logic-php-date-minutes-as-month", "<?php\n$whenever = mktime(9, 30, 0, 2, 5, 2024);\necho date(\"H:m\", $whenever), PHP_EOL;\n", "09:02", "09:30" },

        // A number asked as a condition: 4 called odd.
        { "Ruby", "logic-ruby-zero-is-true", "n = 4\nif n % 2\n  puts \"odd\"\nelse\n  puts \"even\"\nend\n", "odd", "even" },
        { "Lua", "logic-lua-zero-is-true", "local n = 4\nif n % 2 then\n  print(\"odd\")\nelse\n  print(\"even\")\nend\n", "odd", "even" },
        { "Elixir", "logic-elixir-zero-is-true", "n = 4\nif rem(n, 2) do\n  IO.puts(\"odd\")\nelse\n  IO.puts(\"even\")\nend\n", "odd", "even" },
        { "Lua", "logic-lua-zero-is-true", "local items = {}\nif #items then\n  print(\"has items\")\nelse\n  print(\"empty\")\nend\n", "has items", "empty" },

        // A misspelt name given a value: 5, 10 and 20 added, 0 printed for 35.
        { "Python", "logic-python-misspelt-name", "total = 0\nfor mark in [5, 10, 20]:\n    totl = total + mark\nprint(total)\n", "0", "35" },
        { "Lua", "logic-lua-misspelt-name", "local total = 0\nfor _, mark in ipairs({5, 10, 20}) do\n  totl = total + mark\nend\nprint(total)\n", "0", "35" },
        { "JavaScript", "logic-js-misspelt-name", "let total = 0;\nfor (const mark of [5, 10, 20]) {\n    totl = total + mark;\n}\nconsole.log(total);\n", "0", "35" },
        { "PHP", "logic-php-misspelt-name", "<?php\n$total = 0;\nforeach ([5, 10, 20] as $mark) {\n    $totl = $total + $mark;\n}\necho $total, PHP_EOL;\n", "0", "35" },
        { "PowerShell", "logic-powershell-misspelt-name", "$total = 0\nforeach ($mark in 5, 10, 20) {\n    $totl = $total + $mark\n}\nWrite-Output $total\n", "0", "35" },
    };

    /// <summary>Whether Windows' application control refused to start what was built - which says nothing about the code.</summary>
    private static bool RefusedByWindows(Observation observed) =>
        ApplicationControl.Refused(string.Join("\n", observed.Output) + "\n" + observed.WhyNotRun);

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakePrintsWhatItWasSeenToAndTheChangeWhatWasMeant(string language, string check, string mistake, string mistakePrints, string changePrints)
    {
        var codeLanguage = CodeLanguage.All.Single(each => each.Name == language);
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, Path.GetFileName(PastedCode.RelativePathFor(mistake, codeLanguage)));
        File.WriteAllText(path, mistake);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == check);

        var lines = mistake.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(found.Fix!.StartLine - 1, found.Fix.RemoveCount);
        lines.InsertRange(found.Fix.StartLine - 1, found.Fix.NewLines);
        var changed = string.Join("\n", lines) + "\n";

        var wrong = await SnippetRunner.RunAsync(codeLanguage, mistake);
        if (!wrong.Ran || RefusedByWindows(wrong))
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun ?? "Windows refused to start it"}");
            return;
        }

        var right = await SnippetRunner.RunAsync(codeLanguage, changed);
        output.WriteLine($"the mistake: {wrong.Said(language)}\nthe change:  {right.Said(language)}");
        if (RefusedByWindows(right))
        {
            output.WriteLine("Windows refused to start the change's program on this computer.");
            return;
        }

        Assert.True(wrong.Shows(Behaviour.Printing(mistakePrints)), wrong.Said(language));
        Assert.True(right.Shows(Behaviour.Printing(changePrints)), right.Said(language));
    }
}
