using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Lua's local and Perl's my inside a block, declaring a new variable from the outer one: found where the outer one was meant to
/// change, and left alone where the new one is used in the block or declared in the same scope.
/// </summary>
public class HiddenOuterVariablePatternTests : IDisposable
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

    [Theory]
    [InlineData("main.lua", "logic-lua-local-hides-outer", "local marks = {70, 80, 90}\nlocal total = 0\nfor _, mark in ipairs(marks) do\n  local total = total + mark\nend\nprint(\"Total: \" .. total)\n", 4, "  total = total + mark")]
    [InlineData("main.lua", "logic-lua-local-hides-outer", "local count = 0\nif #arg >= 0 then\n  local count = count + 1\nend\nprint(count)\n", 3, "  count = count + 1")]
    [InlineData("main.pl", "logic-perl-my-hides-outer", "my @marks = (70, 80, 90);\nmy $total = 0;\nforeach my $mark (@marks) {\n    my $total = $total + $mark;\n}\nprint \"Total: $total\\n\";\n", 4, "    $total = $total + $mark;")]
    public void ADeclarationInsideABlockFromTheOuterVariableIsFoundAndBecomesAnAssignment(string fileName, string patternId, string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code, patternId));
        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // The new variable used inside its block: a value of the block's own, on purpose.
    [InlineData("main.lua", "logic-lua-local-hides-outer", "local count = 10\nfor i = 1, 3 do\n  local count = count + i\n  print(count)\nend\nprint(count)\n")]
    // Declared again in the same scope, which changes what the name means from there on.
    [InlineData("main.lua", "logic-lua-local-hides-outer", "local total = 0\nlocal total = total + 70\nprint(total)\n")]
    // A comment.
    [InlineData("main.lua", "logic-lua-local-hides-outer", "local total = 0\nfor _, mark in ipairs({70}) do\n  -- local total = total + mark\n  total = total + mark\nend\nprint(total)\n")]
    [InlineData("main.pl", "logic-perl-my-hides-outer", "my $count = 10;\nfor my $i (1 .. 3) {\n    my $count = $count + $i;\n    print \"$count\\n\";\n}\nprint \"$count\\n\";\n")]
    // No $total outside the block.
    [InlineData("main.pl", "logic-perl-my-hides-outer", "sub add {\n    my ($total) = @_;\n    if (1) {\n        my $total = $total + 1;\n    }\n    return $total;\n}\n")]
    public void ADeclarationMeantForItsBlockIsLeftAlone(string fileName, string patternId, string code) =>
        Assert.Empty(Scan(fileName, code, patternId));
}

/// <summary>
/// The mistake and its change run with each language's own toolchain, as FixFinder Learn runs a lesson: the mistake prints the
/// total it started with, and the change the sum. A language this computer cannot run is passed over, and said to be.
/// </summary>
public class HiddenOuterVariableLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string, string, string, int> Mistakes => new()
    {
        {
            "Lua", "main.lua", "logic-lua-local-hides-outer",
            "local marks = {70, 80, 90}\nlocal total = 0\nfor _, mark in ipairs(marks) do\n  local total = total + mark\nend\nprint(\"Total: \" .. total)\n", 4
        },
        {
            "Perl", "main.pl", "logic-perl-my-hides-outer",
            "use strict;\nuse warnings;\nmy @marks = (70, 80, 90);\nmy $total = 0;\nforeach my $mark (@marks) {\n    my $total = $total + $mark;\n}\nprint \"Total: $total\\n\";\n", 6
        },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakePrintsTheTotalItStartedWithAndTheChangeTheSum(string languageName, string fileName, string patternId, string code, int line)
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

        Assert.True(wrong.Shows(Behaviour.Printing("Total: 0")), wrong.Said(languageName));
        Assert.True(right.Shows(Behaviour.Printing("Total: 240")), right.Said(languageName));
    }
}
