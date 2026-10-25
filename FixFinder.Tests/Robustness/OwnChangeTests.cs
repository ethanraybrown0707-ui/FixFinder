using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// Every change a logic check offers, in every program FixFinder holds, made and read again: the check finds one fewer mistake
/// of its kind in the file - a change that left its own mistake where it was would be offered for nothing - and no more of any
/// other kind. Counted in the whole file, as a check may name one line and change another: a loop that removes from its list
/// is named where it removes, and changed where it loops. The first of these found Go, Ruby, PHP, Rust and PowerShell input
/// checks naming a line that was trimmed - or made a number - on the line after it, as their own changes do.
/// </summary>
public class OwnChangeTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string FileNameFor(string extension) => (extension == ".java" ? "Main" : "main") + extension;

    private static IReadOnlyList<LogicFinding> ScanText(string folder, string extension, string code)
    {
        var path = Path.Combine(folder, FileNameFor(extension));
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!);
    }

    /// <summary>Each finding with a change, in each program that has one: the program, its kind of file, and which of its findings.</summary>
    public static TheoryData<string, string, string, int> FindingsWithChanges()
    {
        var cases = new TheoryData<string, string, string, int>();
        using var temp = new TempFolder();

        foreach (var (name, extension, code) in HarmlessChangeTests.Programs().DistinctBy(program => (program.Extension, program.Code)))
        {
            var text = code.ReplaceLineEndings("\n").TrimEnd('\n') + "\n";
            var findings = ScanText(temp.Path, extension, text);
            for (var index = 0; index < findings.Count; index++)
                if (findings[index].Fix is not null) cases.Add(name, extension, text, index);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(FindingsWithChanges))]
    public void AChangeLeavesOneFewerOfItsOwnMistakeInTheFile(string name, string extension, string code, int which)
    {
        var findings = ScanText(_temp.Path, extension, code);
        var finding = findings[which];
        var fix = finding.Fix!;
        var before = findings.Count(other => other.PatternId == finding.PatternId);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(fix.StartLine - 1, fix.RemoveCount);
        lines.InsertRange(fix.StartLine - 1, fix.NewLines);
        var after = ScanText(_temp.Path, extension, string.Join("\n", lines) + "\n").Count(other => other.PatternId == finding.PatternId);

        Assert.True(after < before,
            $"{name}: {finding.PatternId} on line {finding.Line} - \"{fix.Title}\" - left {after} of its own findings in the file, where there were {before}");
    }

    /// <summary>
    /// The change, made, leaves no more of any other check's mistakes in the file than there were. Style suggestions are not
    /// counted: int.Parse(Console.ReadLine()), the change for a Console.Read() used as a number, is the usual way to read one,
    /// and the suggestion to use TryParse in case it is not one is a suggestion, not a mistake the change made.
    /// </summary>
    [Theory]
    [MemberData(nameof(FindingsWithChanges))]
    public void AChangeBringsInNoOtherMistake(string name, string extension, string code, int which)
    {
        var findings = ScanText(_temp.Path, extension, code);
        var fix = findings[which].Fix!;
        var countsBefore = findings.GroupBy(finding => finding.PatternId).ToDictionary(group => group.Key, group => group.Count());

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(fix.StartLine - 1, fix.RemoveCount);
        lines.InsertRange(fix.StartLine - 1, fix.NewLines);
        var changedFindings = ScanText(_temp.Path, extension, string.Join("\n", lines) + "\n").Where(finding => finding.Kind != FindingKind.Style);

        var broughtIn = changedFindings.GroupBy(finding => finding.PatternId)
            .Where(group => group.Count() > countsBefore.GetValueOrDefault(group.Key))
            .Select(group => $"{group.Key} on line {group.First().Line}")
            .ToList();
        Assert.True(broughtIn.Count == 0,
            $"{name}: {findings[which].PatternId} - \"{fix.Title}\" - brought in {string.Join(", ", broughtIn)}");
    }
}
