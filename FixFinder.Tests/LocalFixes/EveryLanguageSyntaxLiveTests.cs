using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Text, brackets and blocks left open, and names misspelt, in the languages whose compilers name them in words of their own -
/// each program checked the way the window checks a file, with its language's own toolchain, and the change built or run
/// again. Each row is what FixFinder gave for the program on this computer when these were written: the rule, the line, the
/// change, and whether a build or run of the changed copy confirmed it. A language this computer cannot run is passed over,
/// and a change Windows would not let run here is not asked to be confirmed.
/// </summary>
public class EveryLanguageSyntaxLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The language, the file, the program, the rule that fixes it, its line, where the change starts, how many lines it replaces, the lines it writes (split by |), and whether a build or run confirmed it.</summary>
    public static TheoryData<string, string, string, string, int, int, int, string, bool> Mistakes => new()
    {
        { "C#", "pasted.cs", "Console.WriteLine(\"hello);\n", "csharp-unclosed-string", 1, 1, 1, "Console.WriteLine(\"hello\");", true },
        { "C#", "pasted.cs", "Console.WriteLine(\"total: \" + (2 + 3);\n", "csharp-unclosed-bracket", 1, 1, 1, "Console.WriteLine(\"total: \" + (2 + 3));", true },
        { "Rust", "pasted.rs", "fn main() {\n    println!(\"hello);\n}\n", "rust-unclosed-string", 2, 2, 1, "    println!(\"hello\");", true },
        { "Rust", "pasted.rs", "fn main() {\n    println!(\"{}\", (2 + 3);\n}\n", "rust-unclosed-bracket", 3, 2, 1, "    println!(\"{}\", (2 + 3));", true },
        { "Rust", "pasted.rs", "fn main() {\n    println!(\"hi\");\n", "rust-unclosed-block", 2, 3, 0, "}", true },
        { "PHP", "pasted.php", "<?php\necho \"hello, PHP_EOL;\n", "php-unclosed-string", 3, 2, 1, "echo \"hello\", PHP_EOL;", true },
        { "PHP", "pasted.php", "<?php\necho strlen(\"hello\", PHP_EOL;\n", "php-unclosed-bracket", 2, 2, 1, "echo strlen(\"hello\", PHP_EOL);", false },
        { "Elixir", "pasted.exs", "IO.puts(\"hello)\n", "elixir-unclosed-string", 1, 1, 1, "IO.puts(\"hello\")", true },
        { "Elixir", "pasted.exs", "IO.puts((2 + 3)\nIO.puts(\"done\")\n", "elixir-unclosed-bracket", 2, 1, 1, "IO.puts((2 + 3))", true },
        { "Erlang", "marks.erl", "-module(marks).\n-export([main/0]).\n\nmain() ->\n    io:format(\"hello~n).\n", "erlang-unclosed-string", 5, 5, 1, "    io:format(\"hello~n\").", true },
        { "Erlang", "marks.erl", "-module(marks).\n-export([main/0]).\n\nmain() ->\n    io:format(\"~p~n\", [(2 + 3]).\n", "erlang-unclosed-bracket", 5, 5, 1, "    io:format(\"~p~n\", [(2 + 3)]).", true },
        { "Dart", "pasted.dart", "void main() {\n  print('hello);\n}\n", "dart-unclosed-string", 2, 2, 1, "  print('hello');", true },
        { "Dart", "main.dart", "void main() {\n  print((2 + 3);\n}\n", "dart-unclosed-bracket", 2, 2, 1, "  print((2 + 3));", true },
        { "Dart", "pasted.dart", "void main() {\n  print('hi');\n", "dart-unclosed-block", 3, 3, 0, "}", true },
        { "F#", "pasted.fsx", "printfn \"hello\n", "fsharp-unclosed-string", 1, 1, 1, "printfn \"hello\"", true },
        { "Perl", "pasted.pl", "use strict;\nuse warnings;\n\nprint((2 + 3, \"\\n\";\n", "perl-unclosed-bracket", 4, 4, 1, "print((2 + 3, \"\\n\"));", true },
        { "C++", "pasted.cpp", "#include <iostream>\n\nint main() {\n    std::cot << \"hi\\n\";\n}\n", "cpp-std-member", 4, 4, 1, "    std::cout << \"hi\\n\";", false },
        { "JavaScript", "pasted.js", "console.lg(\"hi\");\n", "js-console-method", 1, 1, 1, "console.log(\"hi\");", true },
        { "TypeScript", "pasted.ts", "console.lg(\"hi\");\n", "js-console-method", 1, 1, 1, "console.log(\"hi\");", true },
        { "Erlang", "marks.erl", "-module(marks).\n-export([main/0]).\n\nmain() ->\n    io:fomat(\"hi~n\").\n", "erlang-nearest-function", 5, 5, 1, "    io:format(\"hi~n\").", true },
        { "Fortran", "pasted.f90", "program marks\n  implicit none\n  prnt *, 'hi'\nend program marks\n", "fortran-nearest-statement", 3, 3, 1, "  print *, 'hi'", false },
        { "PowerShell", "pasted.ps1", "Set-StrictMode -Version Latest\n$total = 5\nWrite-Output $totl\n", "powershell-nearest-variable", 3, 3, 1, "Write-Output $total", true },
        { "Lua", "pasted.lua", "prnt(\"hi\")\n", "lua-nearest-name", 1, 1, 1, "print(\"hi\")", true },
        { "Perl", "pasted.pl", "use strict;\nuse warnings;\n\nprnt(\"hi\\n\");\n", "perl-nearest-sub", 4, 4, 1, "print(\"hi\\n\");", true },
        { "Perl", "pasted.pl", "use strict;\nuse warnings;\n\nmy $total = 5;\nprint \"$totl\\n\";\n", "perl-undeclared-variable", 5, 5, 1, "print \"$total\\n\";", true },
        { "Dart", "pasted.dart", "void main() {\n  prnt('hi');\n}\n", "dart-nearest-name", 2, 2, 1, "  print('hi');", true },
        { "Elixir", "pasted.exs", "IO.put(\"hi\")\n", "elixir-did-you-mean-function", 1, 1, 1, "IO.puts(\"hi\")", true },
        { "Elixir", "pasted.exs", "Enum.each([60, 70], fn mark ->\n  IO.puts(mark)\nend\n", "elixir-call-left-open-around-fn", 3, 3, 1, "end)", true },
        { "Erlang", "marks.erl", "-module(marks).\n-export([main/0]).\n\nmain() ->\n    case 1 of\n        1 -> io:format(\"one~n\");\n        _ -> io:format(\"other~n\").\n", "erlang-missing-end", 7, 7, 1, "        _ -> io:format(\"other~n\")|    end.", true },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakeIsFoundWithItsChange(
        string language, string fileName, string code, string rule, int line, int fixStart, int fixRemoved, string fixLines, bool confirmedByARun)
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
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.All.Single(each => each.Name == language) }.CheckAsync(launch);
        foreach (var each in report.Findings)
            output.WriteLine($"[{each.RuleId}] {each.Line}: {each.Title} :: {each.Fix?.StartLine} {string.Join("|", each.Fix?.NewLines ?? [])} :: {each.Verified.Summary}");

        var found = report.Findings.FirstOrDefault(finding => finding.RuleId == rule);
        Assert.True(found is not null, $"{language}: no {rule} among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");
        Assert.Equal(line, found!.Line);

        Assert.NotNull(found.Fix);
        Assert.Equal(fixStart, found.Fix!.StartLine);
        Assert.Equal(fixRemoved, found.Fix.RemoveCount);
        Assert.Equal(fixLines.Split('|'), found.Fix.NewLines);

        var refusedByWindows = report.Notes.Any(ApplicationControl.Refused) || ApplicationControl.Refused(found.Verified.Summary);
        if (confirmedByARun && !refusedByWindows) Assert.True(found.Verified.IsVerified, $"{language}: {found.Verified.Summary}");

        Assert.Equal(code, File.ReadAllText(program));
    }
}
