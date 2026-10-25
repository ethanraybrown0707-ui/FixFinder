using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// Every finding in every program FixFinder holds that has one, read for its shape: it names a line the file has, and its
/// change - when it has one - is the check's own, for the file that was read, takes away only lines the file has, writes
/// lines with no line break inside them, and makes the file different from what it was.
/// </summary>
public class FindingShapeTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string FileNameFor(string extension) => (extension == ".java" ? "Main" : "main") + extension;

    /// <summary>Each program with a finding in it: its name, its kind of file, and its text.</summary>
    public static TheoryData<string, string, string> ProgramsWithFindings()
    {
        var cases = new TheoryData<string, string, string>();
        using var temp = new TempFolder();

        foreach (var (name, extension, code) in HarmlessChangeTests.Programs().DistinctBy(program => (program.Extension, program.Code)))
        {
            var path = Path.Combine(temp.Path, FileNameFor(extension));
            File.WriteAllText(path, code);
            if (LogicPatterns.Scan(SourceFile.Read(path)!).Count > 0) cases.Add(name, extension, code);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(ProgramsWithFindings))]
    public void EachFindingNamesALineOfTheFileAndEachChangeFitsIt(string name, string extension, string code)
    {
        var path = Path.Combine(_temp.Path, FileNameFor(extension));
        File.WriteAllText(path, code);
        var source = SourceFile.Read(path)!;
        var lineCount = source.Lines.Count;

        foreach (var finding in LogicPatterns.Scan(source))
        {
            var where = $"{name}: {finding.PatternId} on line {finding.Line}";
            Assert.True(finding.Line >= 1 && finding.Line <= lineCount, $"{where} - the file has {lineCount} lines");
            Assert.False(string.IsNullOrWhiteSpace(finding.Message), $"{where} - says nothing");

            if (finding.Fix is not { } fix) continue;
            Assert.Equal(finding.PatternId, fix.RuleId);
            Assert.Equal(Path.GetFullPath(path), Path.GetFullPath(fix.File), StringComparer.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(fix.Title) || string.IsNullOrWhiteSpace(fix.Explanation), $"{where} - a change with no title or explanation");
            Assert.True(fix.StartLine >= 1 && fix.RemoveCount >= 0 && fix.StartLine - 1 + fix.RemoveCount <= lineCount,
                $"{where} - takes away lines {fix.StartLine}..{fix.StartLine + fix.RemoveCount - 1} of a file of {lineCount}");
            Assert.True(fix.StartLine <= lineCount + 1, $"{where} - writes at line {fix.StartLine} of a file of {lineCount}");
            Assert.DoesNotContain(fix.NewLines, line => line.Contains('\n') || line.Contains('\r'));

            var removed = source.Lines.Skip(fix.StartLine - 1).Take(fix.RemoveCount);
            Assert.False(removed.SequenceEqual(fix.NewLines), $"{where} - \"{fix.Title}\" writes the lines it takes away");
        }
    }
}
