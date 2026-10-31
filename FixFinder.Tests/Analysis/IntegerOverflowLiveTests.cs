using FixFinder.Core;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Execution;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// int arithmetic too big for an int, built and run with each language's own tools the way FixFinder Learn runs a lesson:
/// the program prints the wrapped value the finding names, 1471228928, and with the finding's change, 31536000000. A
/// language this computer cannot build - or a program Windows refused to start - is passed over, and said to be.
/// </summary>
public class IntegerOverflowLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string, string> Programs => new()
    {
        { "C", "year.c", "#include <stdio.h>\n\nint main(void) {\n    int days = 365;\n    long long millis = days * 24 * 60 * 60 * 1000;\n    printf(\"%lld\\n\", millis);\n    return 0;\n}\n" },
        { "C++", "year.cpp", "#include <iostream>\n\nint main() {\n    int days = 365;\n    long long millis = days * 24 * 60 * 60 * 1000;\n    std::cout << millis << std::endl;\n}\n" },
        { "Go", "main.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tvar days int32 = 365\n\tmillis := days * 24 * 60 * 60 * 1000\n\tfmt.Println(millis)\n}\n" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheProgramPrintsTheWrappedValueAndTheChangeTheWholeOne(string language, string fileName, string code)
    {
        var path = Path.Combine(_temp.Path, fileName);
        await File.WriteAllTextAsync(path, code);

        var program = language == "Go"
            ? GoFrontend.FindGo(path) is { } go ? await GoFrontend.ReadAsync([path], go) : null
            : await CFrontend.ReadAsync([path]);
        if (program is null)
        {
            output.WriteLine("Not run on this computer: no go.");
            return;
        }

        var overflow = Assert.Single(AbstractChecks.Run(program, new SourceText()), finding => finding.CheckId == "analysis-integer-overflow");
        Assert.Contains("wraps it round to 1,471,228,928", overflow.Message.Replace("wraps round", "wraps it round"), StringComparison.Ordinal);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(overflow.Fix!.StartLine - 1, overflow.Fix.RemoveCount);
        lines.InsertRange(overflow.Fix.StartLine - 1, overflow.Fix.NewLines);
        var changed = string.Join("\n", lines) + "\n";

        var codeLanguage = CodeLanguage.All.Single(each => each.Name == language);
        var wrong = await SnippetRunner.RunAsync(codeLanguage, code);
        var right = wrong.Ran && !RefusedByWindows(wrong) ? await SnippetRunner.RunAsync(codeLanguage, changed) : null;
        if (right is null || RefusedByWindows(right))
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun ?? "Windows refused to start it"}");
            return;
        }

        output.WriteLine($"the mistake: {wrong.Said(language)}\nthe change:  {right.Said(language)}");
        Assert.True(wrong.Shows(Behaviour.Printing("1471228928")), wrong.Said(language));
        Assert.True(right.Shows(Behaviour.Printing("31536000000")), right.Said(language));
    }

    /// <summary>Whether Windows' application control refused to start what was built - which says nothing about the code.</summary>
    private static bool RefusedByWindows(Observation observed) =>
        ApplicationControl.Refused(string.Join("\n", observed.Output) + "\n" + observed.WhyNotRun);
}
