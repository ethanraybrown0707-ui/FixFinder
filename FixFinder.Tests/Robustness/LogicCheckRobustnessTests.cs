using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// Every logic check, on every kind of file it reads, given code no one would write on purpose but anyone can paste: it finishes,
/// throws nothing, and whatever it finds points at a line the file has, with a change that replaces lines the file has.
/// </summary>
/// <remarks>
/// Before these tests, an F# check took over twenty seconds on a 5,000-line script - it read every line above each line again - and
/// the check on JavaScript grids took six seconds on a 20,000-character line, trying a name at every letter of it. The minute each
/// test allows is only against a check that never ends, as CI's busy runner times nothing reliably.
/// </remarks>
public class LogicCheckRobustnessTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Code no one writes on purpose - by its name, and its text.</summary>
    public static IReadOnlyDictionary<string, string> AwkwardCode { get; } = new Dictionary<string, string>
    {
        ["empty"] = "",
        ["blank lines"] = string.Concat(Enumerable.Repeat("\n   \n\t\n", 200)),
        ["one 100,000-character word"] = new string('a', 100_000) + "\n",
        ["2,000 open brackets"] = new string('(', 2_000) + "\n",
        ["2,000 open braces"] = string.Concat(Enumerable.Repeat("{\n", 2_000)),
        ["2,000 close braces"] = string.Concat(Enumerable.Repeat("}\n", 2_000)),
        ["text never closed"] = "x = \"never closed\n" + string.Concat(Enumerable.Repeat("y = 1\n", 300)),
        ["comment never closed"] = "/* never closed\n" + string.Concat(Enumerable.Repeat("y = 1;\n", 300)),
        ["5,000 ordinary lines"] = string.Concat(Enumerable.Range(0, 5_000).Select(number => $"total = total + {number}\n")),
        ["quotes only"] = string.Concat(Enumerable.Repeat("\"'\"'`\"'\n", 300)),
        ["names and text beyond ASCII"] = "π = 3.14159\n名前 = \"Ada 😀\"\nprint(π, 名前)\n",
        ["Windows line endings"] = "x = 1\r\nif x == 1:\r\n    print(x)\r\n",
        ["1,000 conditions never closed"] = string.Concat(Enumerable.Repeat("if (x == 1 && (y < 2 || (z\n", 1_000)),
        ["5,000 backslashes in text"] = "s = \"" + new string('\\', 5_000) + "\"\n",
        ["keywords of every language"] = string.Concat(Enumerable.Repeat(
            "if else for while def class return int string var let const fn func end do then elif elsif foreach in of switch case break continue try catch except finally import using\n", 300)),
        ["one 60,000-character line of code"] = string.Concat(Enumerable.Repeat("x = x + 1; ", 5_000)) + "\n",
        ["400 levels of indentation"] = string.Concat(Enumerable.Range(0, 400).Select(depth => new string(' ', depth * 2) + "if x:\n")),
    };

    /// <summary>Each logic check, each kind of file it reads, and each piece of awkward code.</summary>
    public static TheoryData<string, string, string> EveryCheckOnEveryAwkwardFile()
    {
        var cases = new TheoryData<string, string, string>();
        foreach (var check in LogicPatterns.All)
            foreach (var extension in check.Extensions.Order(StringComparer.Ordinal))
                foreach (var awkward in AwkwardCode.Keys)
                    cases.Add(check.Id, extension, awkward);
        return cases;
    }

    [Theory]
    [MemberData(nameof(EveryCheckOnEveryAwkwardFile))]
    public void ALogicCheckFinishesOnAwkwardCodeAndPointsOnlyAtLinesTheFileHas(string checkId, string extension, string awkwardName)
    {
        var path = Path.Combine(_temp.Path, "awkward" + extension);
        File.WriteAllText(path, AwkwardCode[awkwardName]);
        var source = SourceFile.Read(path);
        Assert.NotNull(source);

        // Every check with this name - one reads each language it is named for.
        foreach (var check in LogicPatterns.All.Where(pattern => pattern.Id == checkId && pattern.Extensions.Contains(extension)))
        {
            var reading = Task.Run(() => check.Find(source).ToList());
            Assert.True(reading.Wait(TimeSpan.FromMinutes(1)), $"{checkId} was still reading {awkwardName} after a minute");

            foreach (var finding in reading.Result)
            {
                Assert.InRange(finding.Line, 1, source.Lines.Count);
                if (finding.Fix is not { } fix) continue;

                Assert.InRange(fix.StartLine, 1, source.Lines.Count + 1);
                Assert.True(fix.StartLine - 1 + fix.RemoveCount <= source.Lines.Count, $"{checkId}'s change removes lines past the end of the file");
                Assert.DoesNotContain(fix.NewLines, line => line.Contains('\n') || line.Contains('\r'));
            }
        }
    }
}
