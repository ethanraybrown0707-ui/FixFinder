using System.Text;
using System.Text.Json;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>What one instrumented run did: the lines of the file it ran, in order, and the error it stopped with.</summary>
public sealed record Trace(IReadOnlyList<int> Lines, bool Cut, string? Error, string? Message, int? ErrorLine, IReadOnlyDictionary<string, string> Values)
{
    /// <summary>
    /// What the variables held each time one watched line was reached, in the order the run reached it.
    /// </summary>
    /// <remarks>
    /// Recorded during the run rather than worked out afterwards, so every value here is one the program actually
    /// held. Empty unless a line was asked about.
    /// </remarks>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Watched { get; init; } = [];

    /// <summary>Whether the watched line was reached more times than were kept.</summary>
    public bool WatchedCut { get; init; }
}

/// <summary>
/// Dynamic instrumentation for Python: runs the program, or one of its functions, under sys.settrace and records every
/// line of the file it runs, and - if it stops with an error - the error, the line and the values of that frame's
/// variables. Calling one function loads only the file's definitions, imports and constants, so its top-level code
/// does not run.
/// </summary>
public static class PythonProbe
{
    private const int MostLines = 5000;

    /// <summary>How many visits to a watched line are kept. A loop over a large list is common and a table of it is not readable.</summary>
    private const int MostStates = 200;

    private const string Script = """
        import ast, json, os, runpy, sys, threading
        out, target, mode = sys.argv[1], os.path.abspath(sys.argv[2]), sys.argv[3]
        wanted = os.path.normcase(target)
        lines, state = [], {"cut": False, "watchcut": False}
        most = int(os.environ.get("FIXFINDER_MOST_LINES", "5000"))
        watch = int(os.environ.get("FIXFINDER_WATCH", "0"))
        moststates = int(os.environ.get("FIXFINDER_MOST_STATES", "200"))
        watched = []
        def note(line):
            if len(lines) < most:
                lines.append(line)
            else:
                state["cut"] = True
        def local(frame, event, arg):
            if event == "line":
                note(frame.f_lineno)
                if watch and frame.f_lineno == watch:
                    if len(watched) < moststates:
                        watched.append({k: repr(v)[:60] for k, v in list(frame.f_locals.items())[:12] if not k.startswith("__")})
                    else:
                        state["watchcut"] = True
            return local
        def calls(frame, event, arg):
            if os.path.normcase(frame.f_code.co_filename) == wanted:
                if frame.f_code.co_name != "<module>":
                    note(frame.f_code.co_firstlineno)
                return local
            return None
        def kept(node):
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef, ast.Import, ast.ImportFrom)):
                return True
            if isinstance(node, ast.Assign):
                try:
                    ast.literal_eval(node.value)
                    return True
                except Exception:
                    return False
            return False
        result = {"error": None}
        sys.path.insert(0, os.path.dirname(target))
        try:
            if mode == "call":
                name, arguments = sys.argv[4], ast.literal_eval(sys.argv[5])
                with open(target, encoding="utf-8") as source:
                    tree = ast.parse(source.read(), target)
                tree.body = [node for node in tree.body if kept(node)]
                space = {"__name__": "fixfinder_probe", "__file__": target}
                exec(compile(tree, target, "exec"), space)
                function = space[name]
                sys.settrace(calls)
                try:
                    function(**arguments)
                finally:
                    sys.settrace(None)
            else:
                sys.argv = [target]
                sys.settrace(calls)
                threading.settrace(calls)
                try:
                    runpy.run_path(target, run_name="__main__")
                finally:
                    sys.settrace(None)
        except SystemExit:
            pass
        except BaseException as error:
            trace, line, values = error.__traceback__, None, {}
            while trace is not None:
                if os.path.normcase(trace.tb_frame.f_code.co_filename) == wanted:
                    line = trace.tb_lineno
                    values = {k: repr(v)[:60] for k, v in list(trace.tb_frame.f_locals.items())[:30] if not k.startswith("__")}
                trace = trace.tb_next
            result["error"] = {"type": type(error).__name__, "message": str(error)[:300], "line": line, "values": values}
        finally:
            result["lines"] = lines
            result["cut"] = state["cut"]
            result["watched"] = watched
            result["watchcut"] = state["watchcut"]
            with open(out, "w", encoding="utf-8") as saved:
                json.dump(result, saved)
        """;

    /// <summary>Runs the whole program with <paramref name="input"/> typed in.</summary>
    public static Task<Trace?> RunAsync(string interpreter, string file, string input, TimeSpan timeout, CancellationToken cancellationToken,
        int watchLine = 0) =>
        TraceAsync(interpreter, file, ["program"], input, timeout, cancellationToken, watchLine);

    /// <summary>Calls one top-level function of the file with keyword arguments written as a Python dict literal.</summary>
    /// <param name="watchLine">A line to record the variables at every time the run reaches it, or 0 for none.</param>
    public static Task<Trace?> CallAsync(string interpreter, string file, string function, string arguments, string input, TimeSpan timeout,
        CancellationToken cancellationToken, int watchLine = 0) =>
        TraceAsync(interpreter, file, ["call", function, arguments], input, timeout, cancellationToken, watchLine);

    private static async Task<Trace?> TraceAsync(string interpreter, string file, IReadOnlyList<string> how, string input, TimeSpan timeout,
        CancellationToken cancellationToken, int watchLine = 0)
    {
        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-probe", Guid.NewGuid().ToString("N")[..12]);

        try
        {
            Directory.CreateDirectory(folder);
            var script = Path.Combine(folder, "fixfinder_probe.py");
            var output = Path.Combine(folder, "trace.json");
            await File.WriteAllTextAsync(script, Script, new UTF8Encoding(false), cancellationToken);

            var arguments = string.Join(" ", new[] { "-X utf8", Quote(script), Quote(output), Quote(file) }.Concat(how.Select(Quote)));
            await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = interpreter,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(file)!,
                Timeout = timeout,
            }.WithInput(input).WithEnvironment(new Dictionary<string, string>
            {
                ["FIXFINDER_MOST_LINES"] = MostLines.ToString(),
                ["FIXFINDER_WATCH"] = watchLine.ToString(),
                ["FIXFINDER_MOST_STATES"] = MostStates.ToString(),
            }), cancellationToken);

            return File.Exists(output) ? Read(await File.ReadAllTextAsync(output, cancellationToken)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
        finally
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Quotes one command-line argument the way the Windows C runtime splits them, backslashes and all.</summary>
    private static string Quote(string argument)
    {
        var quoted = new StringBuilder("\"");
        var backslashes = 0;

        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes).Append(c);
            backslashes = 0;
        }

        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }

    private static Trace Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var lines = root.GetProperty("lines").EnumerateArray().Select(l => l.GetInt32()).ToList();
        var cut = root.GetProperty("cut").GetBoolean();

        var watched = root.TryGetProperty("watched", out var visits) && visits.ValueKind == JsonValueKind.Array
            ? visits.EnumerateArray()
                .Select(visit => (IReadOnlyDictionary<string, string>)visit.EnumerateObject()
                    .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal))
                .ToList()
            : [];

        var watchedCut = root.TryGetProperty("watchcut", out var more) && more.ValueKind == JsonValueKind.True;

        if (root.GetProperty("error") is not { ValueKind: JsonValueKind.Object } error)
            return new Trace(lines, cut, null, null, null, new Dictionary<string, string>()) { Watched = watched, WatchedCut = watchedCut };

        var values = error.GetProperty("values").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);
        return new Trace(lines, cut, error.GetProperty("type").GetString(), error.GetProperty("message").GetString(),
            error.GetProperty("line") is { ValueKind: JsonValueKind.Number } line ? line.GetInt32() : null, values)
        {
            Watched = watched,
            WatchedCut = watchedCut,
        };
    }
}
