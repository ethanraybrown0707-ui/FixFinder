using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using FixFinder.Core.Execution;

namespace FixFinder.Core.LocalFixes;

/// <summary>javac, kept running between checks.</summary>
internal sealed class JavaCompileServer
{
    private static readonly ConcurrentDictionary<string, JavaCompileServer> Servers = new(StringComparer.OrdinalIgnoreCase);

    private const string Program = """
        import java.io.*;
        import java.nio.charset.StandardCharsets;
        import java.util.Base64;
        import java.util.concurrent.*;
        import java.util.concurrent.atomic.AtomicLong;

        public class FixFinderJavac {
            // What javac's own error writer uses: the error stream's character set from JDK 18, the default before it.
            static java.nio.charset.Charset errorCharset() {
                try {
                    return (java.nio.charset.Charset) PrintStream.class.getMethod("charset").invoke(System.err);
                } catch (ReflectiveOperationException olderJdk) {
                    return java.nio.charset.Charset.defaultCharset();
                }
            }

            public static void main(String[] args) throws Exception {
                var replies = new PrintStream(new FileOutputStream(FileDescriptor.out), true, StandardCharsets.UTF_8);
                System.setOut(new PrintStream(OutputStream.nullOutputStream()));

                var input = new BufferedReader(new InputStreamReader(System.in, StandardCharsets.UTF_8));
                var pool = Executors.newFixedThreadPool(Integer.parseInt(args[0]));
                var lastUsed = new AtomicLong(System.nanoTime());

                var idle = new Thread(() -> {
                    while (true) {
                        try { Thread.sleep(30_000); } catch (InterruptedException e) { return; }
                        if (System.nanoTime() - lastUsed.get() > TimeUnit.MINUTES.toNanos(15)) System.exit(0);
                    }
                });
                idle.setDaemon(true);
                idle.start();

                String line;
                while ((line = input.readLine()) != null) {
                    lastUsed.set(System.nanoTime());
                    var parts = line.split("\t", -1);
                    var id = parts[0];
                    var arguments = new String[parts.length - 1];
                    for (int i = 1; i < parts.length; i++)
                        arguments[i - 1] = new String(Base64.getDecoder().decode(parts[i]), StandardCharsets.UTF_8);

                    pool.submit(() -> {
                        var bytes = new ByteArrayOutputStream();
                        String reply;
                        try {
                            var writer = new PrintWriter(new OutputStreamWriter(bytes, errorCharset()), true);
                            int code = com.sun.tools.javac.Main.compile(arguments, writer);
                            writer.flush();
                            reply = id + "\t" + code + "\t" + Base64.getEncoder().encodeToString(bytes.toByteArray());
                        } catch (Throwable failure) {
                            reply = id + "\tfailed\t";
                        }
                        lastUsed.set(System.nanoTime());
                        synchronized (replies) { replies.println(reply); }
                    });
                }

                System.exit(0);
            }
        }
        """;

    private readonly string _java;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<Reply?>> _waiting = new();
    private Process? _process;
    private long _nextId;
    private bool _answered;
    private bool _broken;

    private JavaCompileServer(string java) => _java = java;

    public FasterCheck.Trust Trust { get; } = new();

    public sealed record Reply(int ExitCode, byte[] Output);

    public static JavaCompileServer? For(string javac)
    {
        var java = Path.Combine(Path.GetDirectoryName(javac) ?? "", "java.exe");

        return File.Exists(java) ? Servers.GetOrAdd(java, path => new JavaCompileServer(path)) : null;
    }

    public async Task<Reply?> CompileAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var reply = new TaskCompletionSource<Reply?>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            if (_broken || Started() is not { } process) return null;

            _waiting[id] = reply;

            try
            {
                process.StandardInput.WriteLine(
                    $"{id}\t{string.Join("\t", arguments.Select(a => Convert.ToBase64String(Encoding.UTF8.GetBytes(a))))}");
                process.StandardInput.Flush();
            }
            catch (IOException)
            {
                _waiting.TryRemove(id, out _);
                return null;
            }
        }

        try
        {
            return await reply.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            _waiting.TryRemove(id, out _);
        }
    }

    public static IReadOnlyList<CapturedLine> Lines(byte[] output, Encoding encoding)
    {
        var lines = new List<CapturedLine>();
        using var reader = new StreamReader(new MemoryStream(output), encoding, detectEncodingFromByteOrderMarks: true);

        while (reader.ReadLine() is { } text)
            lines.Add(new CapturedLine(lines.Count, StreamKind.StdErr, text, TimeSpan.Zero));

        return lines;
    }

    private Process? Started()
    {
        if (_process is { HasExited: false }) return _process;

        if (_process is not null && !_answered)
        {
            _broken = true;
            return null;
        }

        try
        {
            var version = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Program)))[..12];
            var folder = Path.Combine(CompileCheck.Root, "javac-" + version);
            var source = Path.Combine(folder, "FixFinderJavac.java");

            if (!File.Exists(source) || File.ReadAllText(source) != Program)
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(source, Program, new UTF8Encoding(false));
            }

            var startInfo = new ProcessStartInfo(_java)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = folder,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
            };

            startInfo.ArgumentList.Add(source);
            startInfo.ArgumentList.Add(Math.Clamp(Environment.ProcessorCount / 2, 1, 4).ToString());

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;

                var parts = e.Data.Split('\t');
                if (parts.Length < 3 || !long.TryParse(parts[0], out var id) || !_waiting.TryGetValue(id, out var waiting)) return;

                _answered = true;
                waiting.TrySetResult(int.TryParse(parts[1], out var code) ? new Reply(code, Convert.FromBase64String(parts[2])) : null);
            };

            process.ErrorDataReceived += (_, _) => { };

            process.Exited += (_, _) =>
            {
                foreach (var waiting in _waiting.Values) waiting.TrySetResult(null);
            };

            if (!process.Start()) return null;

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _process = process;
            return process;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _broken = true;
            return null;
        }
    }
}
