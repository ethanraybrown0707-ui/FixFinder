using System.Text;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Engine;

/// <summary>
/// Writes one plain-text file per run, recording everything FixFinder did.
/// </summary>
/// <remarks>
/// It subscribes to the same events the on-screen panels do, so the file and the window cannot
/// drift apart. That matters more here than in most tools: FixFinder eventually modifies source
/// files, and this log is the audit trail - what was launched, what it printed, what was
/// searched for, which candidate was chosen, which files were written and where their backups
/// went.
/// <para>
/// <see cref="StreamWriter.AutoFlush"/> is on, so the file is complete and readable even if the
/// tool is killed mid-run - which is precisely when you most want to read it.
/// </para>
/// </remarks>
public sealed class FixFinderLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    private bool _disposed;

    public string FilePath { get; }

    public FixFinderLogger(string toolName = "FixFinder", string? logDirectory = null)
    {
        var directory = logDirectory ?? Path.Combine(AppContext.BaseDirectory, "Logs");
        Directory.CreateDirectory(directory);

        FilePath = Path.Combine(directory, $"{toolName}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        // UTF-8 with a byte-order mark. The default StreamWriter encoding is UTF-8 without one,
        // which is correct on the wire and wrong for a file like this: the log is full of the
        // separators and arrows the window uses, and Windows tools that meet a BOM-less file -
        // Notepad, and Get-Content in Windows PowerShell - fall back to the ANSI code page and
        // render every one of them as mojibake. Three bytes buys a log that opens correctly in
        // whatever the reader happens to use, which is the entire point of writing it.
        _writer = new StreamWriter(FilePath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
        {
            AutoFlush = true,
        };

        Write($"{toolName} run log");
        Write($"Started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Write(new string('-', 72));
    }

    public void Attach(TargetRunner runner)
    {
        runner.Log += Write;
        runner.LineCaptured += WriteCapturedLine;
    }

    public void Detach(TargetRunner runner)
    {
        runner.Log -= Write;
        runner.LineCaptured -= WriteCapturedLine;
    }

    public void Write(string message)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
    }

    /// <summary>Blank-line-separated block, for anything worth finding by eye when scrolling.</summary>
    public void WriteSection(string title)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _writer.WriteLine();
            _writer.WriteLine($"=== {title} ===");
        }
    }

    public void WriteRunResult(TargetRunResult result)
    {
        WriteSection("Run result");
        Write($"Outcome:  {result.Outcome}");
        Write($"Exit code: {(result.ExitCode.HasValue ? result.ExitCode.Value.ToString() : "(killed before it exited)")}");
        Write($"Duration: {result.Duration.TotalSeconds:0.###}s");
        Write($"Lines captured: {result.Lines.Count} ({result.StdErrLines.Count()} on stderr)");
        if (!result.OutputStreamsClosedCleanly) Write("WARNING: output streams did not close cleanly - capture may be incomplete.");
        if (result.EncodingWarning is not null) Write($"WARNING: {result.EncodingWarning}");
        Write(result.Explanation);
    }

    private void WriteCapturedLine(CapturedLine line) =>
        Write($"{line.Elapsed.TotalSeconds,7:0.000}s {line.DisplayLine}");

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _writer.WriteLine(new string('-', 72));
            _writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] Finished.");
            _writer.Dispose();
        }
    }
}
