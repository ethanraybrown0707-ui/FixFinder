using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>A root written as a power of one over a number: found where it is, with its change, and left alone where the power is meant.</summary>
public class RootPowerPatternTests : IDisposable
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
    [InlineData("area.py", "print(side ** 1/2)", "logic-python-power-of-one-over", "print(side ** 0.5)")]
    [InlineData("area.py", "root = (a + b) ** 1 / 3", "logic-python-power-of-one-over", "root = (a + b) ** (1.0 / 3)")]
    [InlineData("area.py", "root = 16 ** 1/4", "logic-python-power-of-one-over", "root = 16 ** (1.0 / 4)")]
    [InlineData("area.rb", "puts marks[0] ** 1/2", "logic-ruby-power-of-one-over", "puts marks[0] ** 0.5")]
    [InlineData("area.js", "const root = rect.width ** 1/2;", "logic-js-power-of-one-over", "const root = rect.width ** 0.5;")]
    [InlineData("area.ts", "const root: number = side ** 1/2;", "logic-js-power-of-one-over", "const root: number = side ** 0.5;")]
    [InlineData("area.php", "$root = $side ** 1/3;", "logic-php-power-of-one-over", "$root = $side ** (1.0 / 3);")]
    [InlineData("area.pl", "my $root = $shape->{side} ** 1/2;", "logic-perl-power-of-one-over", "my $root = $shape->{side} ** 0.5;")]
    [InlineData("area.f90", "  root = side ** 1/2", "logic-fortran-power-of-one-over", "  root = side ** 0.5")]
    [InlineData("area.lua", "local root = side^1/2", "logic-lua-power-of-one-over", "local root = side ^ 0.5")]
    public void ARootWrittenAsAPowerOfOneOverIsFoundAndRaisedToTheFraction(string fileName, string line, string check, string fixedLine)
    {
        var rooted = Assert.Single(Scan(fileName, line + "\n"), finding => finding.PatternId == check);

        Assert.Equal(fixedLine, rooted.Fix?.NewLines.Single());
    }

    [Theory]
    // A power of 2, halved: what it says.
    [InlineData("check.py", "half = side ** 2 / 2")]
    // The fraction already in brackets, or a decimal.
    [InlineData("check.py", "root = side ** (1 / 2)")]
    [InlineData("check.rb", "root = side ** 0.5")]
    // Divided by 10, or by a decimal: no root is written that way.
    [InlineData("check.py", "tenth = side ** 1 / 10")]
    [InlineData("check.py", "part = side ** 1/2.5")]
    // Inside text and comments.
    [InlineData("check.py", "print(\"side ** 1/2\")")]
    [InlineData("check.js", "// side ** 1/2 is half of side")]
    // ^ is a power only in Lua.
    [InlineData("check.py", "flags = mask ^ 1/2")]
    public void APowerThatIsMeantIsLeftAlone(string fileName, string line)
    {
        Assert.DoesNotContain(Scan(fileName, line + "\n"), finding => finding.PatternId.EndsWith("power-of-one-over", StringComparison.Ordinal));
    }
}

/// <summary>
/// What a root written as a power of one over a number prints, run with each language's own toolchain the way FixFinder
/// Learn runs a lesson, and what the check's own change prints instead. A language this computer cannot run is passed over,
/// and said to be.
/// </summary>
public class RootPowerLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Each: the language, its file, a program printing a root of 16 (or 27) written that way, what that prints, and what its change prints.</summary>
    public static TheoryData<string, string, string, string, string> Mistakes => new()
    {
        { "Python", "main.py", "side = 16\nprint(side ** 1/2)\n", "8.0", "4.0" },
        { "Ruby", "main.rb", "side = 16\nputs side ** 1/2\n", "8", "4.0" },
        { "Ruby", "main.rb", "puts 27 ** 1/3\n", "9", "3.0" },
        { "JavaScript", "main.js", "const side = 16;\nconsole.log(side ** 1/2);\n", "8", "4" },
        { "TypeScript", "main.ts", "const side: number = 16;\nconsole.log(side ** 1/2);\n", "8", "4" },
        { "PHP", "main.php", "<?php\n$side = 16;\necho $side ** 1/2, \"\\n\";\n", "8", "4" },
        { "Perl", "main.pl", "use strict;\nuse warnings;\n\nmy $side = 16;\nprint $side ** 1/2, \"\\n\";\n", "8", "4" },
        { "Fortran", "main.f90", "program roots\n  implicit none\n  real :: side\n  side = 16.0\n  print *, side ** 1/2\nend program roots\n", "   8.00000000", "   4.00000000" },
        { "Fortran", "main.f90", "program roots\n  implicit none\n  print *, 27.0 ** 1/3\nend program roots\n", "   9.00000000", "   3.00000000" },
        { "Lua", "main.lua", "local side = 16\nprint(side^1/2)\n", "8.0", "4.0" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakePrintsAPartOfTheNumberAndTheChangeItsRoot(string language, string fileName, string mistake, string mistakePrints, string changePrints)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, mistake);
        var rooted = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId.EndsWith("power-of-one-over", StringComparison.Ordinal));

        var lines = mistake.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(rooted.Fix!.StartLine - 1, rooted.Fix.RemoveCount);
        lines.InsertRange(rooted.Fix.StartLine - 1, rooted.Fix.NewLines);
        var changed = string.Join("\n", lines) + "\n";

        var codeLanguage = CodeLanguage.All.Single(each => each.Name == language);
        var wrong = await SnippetRunner.RunAsync(codeLanguage, mistake);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(codeLanguage, changed);
        output.WriteLine($"the mistake: {wrong.Said(language)}\nthe change:  {right.Said(language)}");

        Assert.True(wrong.Shows(Behaviour.Printing(mistakePrints)), wrong.Said(language));
        Assert.True(right.Shows(Behaviour.Printing(changePrints)), right.Said(language));
    }
}
