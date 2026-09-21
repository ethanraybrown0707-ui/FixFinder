using System.Text;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Engine;

/// <summary>Writes one plain-text file per run, recording everything FixFinder did.</summary>
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
