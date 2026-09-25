using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Resource ownership: a file a function opens is its own until it is closed or handed to other code. Left open on a way
/// out, it is never closed - which, for a writer, can mean what was written never reaches the file. Checked beside the
/// ways ownership legitimately moves on: returned, wrapped in another stream that is closed, or closed automatically.
/// </summary>
public class ResourceLifetimeTests(ITestOutputHelper output) : IDisposable
{
    private const string Rule = "analysis-resource-not-closed";

    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Java(string code)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var path = Path.Combine(_temp.Path, code.Split("public class ")[1].Split(' ', '{')[0] + ".java");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Leaks(AbstractChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Python(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, "files.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Leaks(AbstractChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>> CSharp(string code)
    {
        var path = Path.Combine(_temp.Path, "Program.cs");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Leaks(AbstractChecks.Run(await CSharpFrontend.ReadAsync([path]), new SourceText()));
    }

    private List<AnalysisFinding> Leaks(IEnumerable<AnalysisFinding> findings)
    {
        var leaks = findings.Where(f => f.CheckId == Rule).ToList();
        foreach (var leak in leaks) output.WriteLine($"line {leak.Span.Line} [{leak.Severity}]: {leak.Message}");
        return leaks;
    }

    private static string Summary(IEnumerable<AnalysisFinding> findings) => string.Join("; ", findings.Select(f => $"line {f.Span.Line}: {f.Message}"));

    private static void Says(string expected, AnalysisFinding finding) =>
        Assert.True(finding.Message.Contains(expected, StringComparison.Ordinal), $"expected \"{expected}\" in: {finding.Message}");

    private const string Report = """
        import java.io.FileWriter;
        import java.io.IOException;

        public class Report {
            public static void main(String[] args) throws IOException {
                FileWriter writer = new FileWriter("report.txt");
                writer.write("total: 42");
            }
        }
        """;

    /// <summary>The classic empty output file: the writer's buffer is never flushed, because the writer is never closed.</summary>
    [Fact]
    public async Task AWriterNeverClosedMayNeverWriteAnything()
    {
        if (await Java(Report) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(6, found.Span.Line);
        Assert.Equal(Severity.Warning, found.Severity);
        Says("`writer`, opened here, is still open when the function returns on line", found);
        Says("so what was written to it may never reach the file", found);
        Says("try-with-resources", found);
    }

    [Theory]
    [InlineData("writer.write(\"total: 42\");\n        writer.close();")]
    public async Task AWriterClosedBeforeTheMethodEndsIsFine(string closing)
    {
        if (await Java(Report.ReplaceLineEndings("\n").Replace("writer.write(\"total: 42\");", closing, StringComparison.Ordinal)) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task TryWithResourcesClosesItOnEveryWayOut()
    {
        const string code = """
            import java.io.FileWriter;
            import java.io.IOException;

            public class Report {
                public static void main(String[] args) throws IOException {
                    try (FileWriter writer = new FileWriter("report.txt")) {
                        writer.write("total: 42");
                    }
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>Closed at the end - but the early return leaves before it gets there.</summary>
    [Fact]
    public async Task AnEarlyReturnBeforeTheCloseLeavesItOpen()
    {
        const string code = """
            import java.io.BufferedReader;
            import java.io.FileReader;
            import java.io.IOException;

            public class FirstLine {
                static String first(String path) throws IOException {
                    BufferedReader reader = new BufferedReader(new FileReader(path));
                    String line = reader.readLine();
                    if (line == null) {
                        return "";
                    }
                    reader.close();
                    return line;
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(7, found.Span.Line);
        Says("`reader`, opened here, is still open when the function returns on line 10", found);
        Says("so the file stays open until the program ends", found);
    }

    /// <summary>Returning the reader hands it to the caller, who owns it from then on.</summary>
    [Fact]
    public async Task AReaderReturnedBelongsToTheCaller()
    {
        const string code = """
            import java.io.BufferedReader;
            import java.io.FileReader;
            import java.io.IOException;

            public class Opener {
                static BufferedReader open(String path) throws IOException {
                    BufferedReader reader = new BufferedReader(new FileReader(path));
                    return reader;
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>The BufferedReader wrapped around the FileReader owns it, and closing the wrapper closes both.</summary>
    [Fact]
    public async Task AStreamWrappedInAnotherIsClosedWithIt()
    {
        const string code = """
            import java.io.BufferedReader;
            import java.io.FileReader;
            import java.io.IOException;

            public class Wrapped {
                static String first(String path) throws IOException {
                    FileReader file = new FileReader(path);
                    BufferedReader reader = new BufferedReader(file);
                    String line = reader.readLine();
                    reader.close();
                    return line;
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task APythonFileOpenedInAFunctionAndNeverClosedIsReported()
    {
        const string code = """
            def read_marks(path):
                handle = open(path)
                return handle.read()
            """;
        if (await Python(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(2, found.Span.Line);
        Assert.Equal(Severity.Suggestion, found.Severity);
        Says("with open(...) as f:", found);
    }

    [Fact]
    public async Task APythonFileOpenedWithWithIsClosedForIt()
    {
        const string code = """
            def read_marks(path):
                with open(path) as handle:
                    return handle.read()
            """;
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task ACSharpWriterNeverDisposedIsReported()
    {
        const string code = """
            using System.IO;

            class Program
            {
                static void Main()
                {
                    var writer = new StreamWriter("out.txt");
                    writer.Write("total: 42");
                }
            }
            """;

        var found = Assert.Single(await CSharp(code));
        Assert.Equal(7, found.Span.Line);
        Says("Declare it with `using`", found);

        var disposed = await CSharp(code.Replace("var writer = new StreamWriter", "using var writer = new StreamWriter", StringComparison.Ordinal));
        Assert.True(disposed.Count == 0, Summary(disposed));
    }
}
