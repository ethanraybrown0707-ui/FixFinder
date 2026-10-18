using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>Loads the captured crash-output fixtures as if a real run had produced them.</summary>
internal static class Fixtures
{
    public static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static IReadOnlyList<CapturedLine> LoadStackTrace(string relativePath) =>
        ToLines(ReadLines(relativePath).Select(text => (StreamKind.StdErr, text)));

    public static IReadOnlyList<CapturedLine> LoadSplit(string relativePath, Func<string, bool> stdOutPredicate) =>
        ToLines(ReadLines(relativePath)
            .Select(text => (stdOutPredicate(text) ? StreamKind.StdOut : StreamKind.StdErr, text)));

    public static IReadOnlyList<CapturedLine> Combine(IEnumerable<string> stdOutLines, string stdErrFixture) =>
        ToLines(stdOutLines.Select(text => (StreamKind.StdOut, text))
            .Concat(ReadLines(stdErrFixture).Select(text => (StreamKind.StdErr, text))));

    /// <summary>
    /// What a program printed when it was run, kept in Fixtures/Captured/(language)/(case)/output.txt - each line marked with
    /// the stream it came on, "out| " or "err| ".
    /// </summary>
    public static IReadOnlyList<CapturedLine> LoadCaptured(string language, string caseName)
    {
        var full = Path.Combine(Root, "Captured", language, caseName, "output.txt");
        if (!File.Exists(full)) throw new FileNotFoundException($"Fixture not found: {full}", full);

        return ToLines(File.ReadAllText(full).ReplaceLineEndings("\n").TrimEnd('\n').Split('\n')
            .Select(line => (line.StartsWith("out| ", StringComparison.Ordinal) ? StreamKind.StdOut : StreamKind.StdErr, line[5..])));
    }

    public static IReadOnlyList<CapturedLine> FromText(string text) =>
        ToLines(text.ReplaceLineEndings("\n").Split('\n').Select(line => (StreamKind.StdErr, line)));

    private static string[] ReadLines(string relativePath)
    {
        var full = Path.Combine(Root, "StackTraces", relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) throw new FileNotFoundException($"Fixture not found: {full}", full);

        return File.ReadAllText(full).ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
    }

    private static CapturedLine[] ToLines(IEnumerable<(StreamKind Stream, string Text)> source) =>
        source.Select((entry, index) =>
                   new CapturedLine(index + 1, entry.Stream, entry.Text, TimeSpan.FromMilliseconds(index)))
              .ToArray();
}
