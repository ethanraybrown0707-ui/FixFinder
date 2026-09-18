using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Logic;

/// <summary>
/// Which lines of a file one run of the program executed - what the suspiciousness formulas count.
/// </summary>
/// <remarks>
/// Only with what each language already ships: Python's own tracing hook, the coverage V8 records for Node when asked, gcc's
/// <c>--coverage</c> read back with gcov, and Go's <c>-cover</c> read back with <c>go tool covdata</c>. Java and C# have no
/// coverage without installing something, so for them this answers nothing and lines are ordered without it. Every collector
/// works in a folder of its own under the temp directory and leaves the program's folder untouched.
/// </remarks>
public static partial class LineCoverage
{
    /// <summary>Whether coverage can be collected for a file at all.</summary>
    public static bool Supports(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".py" => true,
        ".js" or ".mjs" or ".cjs" => TargetFactory.FindOnPath("node") is not null,
        ".c" => Toolchains.FindGnu(cpp: false) is not null && TargetFactory.FindOnPath("gcov") is not null,
        ".cpp" or ".cc" or ".cxx" or ".c++" => Toolchains.FindGnu(cpp: true) is not null && TargetFactory.FindOnPath("gcov") is not null,
        ".go" => TargetFactory.FindOnPath("go") is not null,
        _ => false,
    };

    /// <summary>The lines of <paramref name="file"/> one run executed, or null when they could not be found out.</summary>
    public static async Task<IReadOnlySet<int>?> CollectAsync(
        string file, TargetSpec run, string? input, string? arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-coverage", Guid.NewGuid().ToString("N")[..12]);

        try
        {
            Directory.CreateDirectory(folder);

            return Path.GetExtension(file).ToLowerInvariant() switch
            {
                ".py" => await PythonAsync(file, run, input, arguments, timeout, folder, cancellationToken),
                ".js" or ".mjs" or ".cjs" => await NodeAsync(file, run, input, arguments, timeout, folder, cancellationToken),
                ".c" or ".cpp" or ".cc" or ".cxx" or ".c++" => await GccAsync(file, input, arguments, timeout, folder, cancellationToken),
                ".go" => await GoAsync(file, input, arguments, timeout, folder, cancellationToken),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or FormatException)
        {
            return null;
        }
        finally
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task<TargetRunResult> RunAsync(TargetSpec spec, CancellationToken cancellationToken) =>
        await new TargetRunner().RunAsync(spec, cancellationToken);

    private static TargetSpec Spec(string program, string arguments, string folder, TimeSpan timeout) => new()
    {
        ExecutablePath = program, Arguments = arguments, WorkingDirectory = folder, Timeout = timeout,
    };

    // ------------------------------------------------------------------ Python

    /// <summary>Runs the script under a tracer that records every line executed in it, then runs the script exactly as before.</summary>
    private const string Tracer = """
        import json, os, runpy, sys, threading
        out, target = sys.argv[1], os.path.abspath(sys.argv[2])
        sys.argv = sys.argv[2:]
        sys.path.insert(0, os.path.dirname(target))
        wanted = os.path.normcase(target)
        hits = set()
        def local(frame, event, arg):
            if event == "line":
                hits.add(frame.f_lineno)
            return local
        def calls(frame, event, arg):
            if event == "call" and os.path.normcase(frame.f_code.co_filename) == wanted:
                hits.add(frame.f_lineno)
                return local
            return None
        def save():
            with open(out, "w") as f:
                json.dump(sorted(hits), f)
        sys.settrace(calls)
        threading.settrace(calls)
        try:
            runpy.run_path(target, run_name="__main__")
        finally:
            sys.settrace(None)
            save()
        """;

    private static async Task<IReadOnlySet<int>?> PythonAsync(
        string file, TargetSpec run, string? input, string? arguments, TimeSpan timeout, string folder, CancellationToken cancellationToken)
    {
        // A package module run with -m has no single script to trace from.
        if (run.Arguments.Contains("-m ", StringComparison.Ordinal)) return null;

        var tracer = Path.Combine(folder, "fixfinder_trace.py");
        var output = Path.Combine(folder, "lines.json");
        await File.WriteAllTextAsync(tracer, Tracer, new UTF8Encoding(false), cancellationToken);

        var spec = Spec(run.ExecutablePath, $"-X utf8 \"{tracer}\" \"{output}\" \"{file}\"", Path.GetDirectoryName(file)!, timeout)
            .WithArguments(arguments).WithInput(input);

        await RunAsync(spec, cancellationToken);

        if (!File.Exists(output)) return null;
        return JsonSerializer.Deserialize<int[]>(await File.ReadAllTextAsync(output, cancellationToken))?.ToHashSet();
    }

    // ------------------------------------------------------------------ Node

    private static async Task<IReadOnlySet<int>?> NodeAsync(
        string file, TargetSpec run, string? input, string? arguments, TimeSpan timeout, string folder, CancellationToken cancellationToken)
    {
        var spec = new TargetSpec
        {
            ExecutablePath = run.ExecutablePath, Arguments = run.Arguments, WorkingDirectory = run.WorkingDirectory, Timeout = timeout,
        }.WithArguments(arguments).WithInput(input).WithEnvironment(new Dictionary<string, string> { ["NODE_V8_COVERAGE"] = folder });

        await RunAsync(spec, cancellationToken);

        var text = await File.ReadAllTextAsync(file, cancellationToken);
        var url = new Uri(Path.GetFullPath(file)).AbsoluteUri;
        var ranges = new List<(int Start, int End, int Count)>();

        foreach (var report in Directory.EnumerateFiles(folder, "coverage-*.json"))
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(report, cancellationToken));

            foreach (var script in document.RootElement.GetProperty("result").EnumerateArray())
            {
                if (!string.Equals(script.GetProperty("url").GetString(), url, StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var function in script.GetProperty("functions").EnumerateArray())
                    foreach (var range in function.GetProperty("ranges").EnumerateArray())
                        ranges.Add((range.GetProperty("startOffset").GetInt32(), range.GetProperty("endOffset").GetInt32(), range.GetProperty("count").GetInt32()));
            }
        }

        if (ranges.Count == 0) return null;

        // A line ran when the innermost range around its first character ran - V8 reports blocks inside functions as
        // ranges of their own, with a count of 0 for the ones never entered.
        var lines = new HashSet<int>();
        var offset = 0;
        var number = 1;

        foreach (var line in text.Split('\n'))
        {
            var first = line.Length - line.TrimStart().Length;

            if (line.Trim().Length > 0)
            {
                var at = offset + first;
                var innermost = ranges.Where(r => r.Start <= at && at < r.End).OrderBy(r => r.End - r.Start).FirstOrDefault();
                if (innermost.End > 0 && innermost.Count > 0) lines.Add(number);
            }

            offset += line.Length + 1;
            number++;
        }

        return lines;
    }

    // ------------------------------------------------------------------ gcc

    private static async Task<IReadOnlySet<int>?> GccAsync(
        string file, string? input, string? arguments, TimeSpan timeout, string folder, CancellationToken cancellationToken)
    {
        var cpp = !Path.GetExtension(file).Equals(".c", StringComparison.OrdinalIgnoreCase);
        if (Toolchains.FindGnu(cpp) is not { } gnu || TargetFactory.FindOnPath("gcov") is not { } gcov) return null;

        var sources = ProgramLayout.NativeSources(file);
        var objects = new List<string>();
        var standard = cpp ? "-std=c++17 " : "";

        foreach (var (source, index) in sources.Select((s, i) => (s, i)))
        {
            var obj = Path.Combine(folder, $"{index}-{Path.GetFileNameWithoutExtension(source)}.o");
            var compile = await RunAsync(Spec(gnu.Program, $"-c -g -O0 --coverage {standard}-I \"{Path.GetDirectoryName(file)}\" -o \"{obj}\" \"{source}\"", folder, timeout), cancellationToken);
            if (compile.ExitCode != 0) return null;
            objects.Add(obj);
        }

        var exe = Path.Combine(folder, "covered.exe");
        var link = await RunAsync(Spec(gnu.Program, $"--coverage -o \"{exe}\" {string.Join(" ", objects.Select(o => $"\"{o}\""))}", folder, timeout), cancellationToken);
        if (link.ExitCode != 0) return null;

        var ran = await RunAsync(Spec(exe, "", folder, timeout).WithArguments(arguments).WithInput(input), cancellationToken);
        if (ran.Outcome == RunOutcome.LaunchFailed) return null;

        var report = await RunAsync(Spec(gcov, $"--json-format --stdout \"{objects[0]}\"", folder, timeout), cancellationToken);
        var json = string.Join("\n", report.Lines.Where(l => l.Stream == StreamKind.StdOut).Select(l => l.Text));

        var lines = new HashSet<int>();
        var name = Path.GetFileName(file);

        foreach (var document in json.Split('\n').Where(l => l.TrimStart().StartsWith('{')))
        {
            using var parsed = JsonDocument.Parse(document);

            foreach (var entry in parsed.RootElement.GetProperty("files").EnumerateArray())
            {
                if (!Path.GetFileName(entry.GetProperty("file").GetString() ?? "").Equals(name, StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var line in entry.GetProperty("lines").EnumerateArray())
                    if (line.GetProperty("count").GetInt64() > 0) lines.Add(line.GetProperty("line_number").GetInt32());
            }
        }

        return lines.Count > 0 ? lines : null;
    }

    // ------------------------------------------------------------------ Go

    [GeneratedRegex(@"^(?<file>.+?):(?<from>\d+)\.\d+,(?<to>\d+)\.\d+ \d+ (?<count>\d+)$")]
    private static partial Regex GoBlock();

    private static async Task<IReadOnlySet<int>?> GoAsync(
        string file, string? input, string? arguments, TimeSpan timeout, string folder, CancellationToken cancellationToken)
    {
        if (TargetFactory.FindOnPath("go") is not { } go) return null;

        var program = ProgramLayout.GoPackageOf(file);
        var exe = Path.Combine(folder, "covered.exe");
        var data = Path.Combine(folder, "data");
        Directory.CreateDirectory(data);

        var build = program.Module is { } module
            ? Spec(go, $"build -cover -o \"{exe}\" .", module, timeout)
            : Spec(go, $"build -cover -o \"{exe}\" {string.Join(" ", program.Files.Select(f => $"\"{f}\""))}", Path.GetDirectoryName(file)!, timeout);

        if ((await RunAsync(build, cancellationToken)).ExitCode != 0) return null;

        var ran = await RunAsync(
            Spec(exe, "", Path.GetDirectoryName(file)!, timeout).WithArguments(arguments).WithInput(input)
                .WithEnvironment(new Dictionary<string, string> { ["GOCOVERDIR"] = data }),
            cancellationToken);
        if (ran.Outcome == RunOutcome.LaunchFailed) return null;

        var text = Path.Combine(folder, "coverage.txt");
        if ((await RunAsync(Spec(go, $"tool covdata textfmt -i=\"{data}\" -o=\"{text}\"", folder, timeout), cancellationToken)).ExitCode != 0 || !File.Exists(text)) return null;

        var name = Path.GetFileName(file);
        var lines = new HashSet<int>();

        foreach (var line in await File.ReadAllLinesAsync(text, cancellationToken))
        {
            if (GoBlock().Match(line) is not { Success: true } block || block.Groups["count"].Value == "0") continue;
            if (!block.Groups["file"].Value.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase) && block.Groups["file"].Value != name) continue;

            for (var n = int.Parse(block.Groups["from"].Value); n <= int.Parse(block.Groups["to"].Value); n++) lines.Add(n);
        }

        return lines.Count > 0 ? lines : null;
    }
}
