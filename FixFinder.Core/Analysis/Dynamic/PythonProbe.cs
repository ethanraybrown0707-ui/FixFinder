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

    /// <summary>
    /// For each followed name, the last changes to what it held in the frame that stopped with the error, oldest first, up
    /// to the moment it stopped. Empty unless names were followed.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ValueChange>> Followed { get; init; } = new Dictionary<string, IReadOnlyList<ValueChange>>();

    /// <summary>How many times each line of the file ran, over the whole run, when names were followed.</summary>
    public IReadOnlyDictionary<int, int> TimesRun { get; init; } = new Dictionary<int, int>();

    /// <summary>The last calls to the file's own functions that gave back None, oldest first, when names were followed.</summary>
    public IReadOnlyList<NoneReturned> GaveNone { get; init; } = [];
}

/// <summary>One change to what a followed name held.</summary>
/// <param name="Line">The line that ran just before the change was seen - the one that made it - or null when the name already held it as its frame began: a parameter.</param>
/// <param name="Value">What it held after the change, as Python writes it, cut to 60 characters.</param>
/// <param name="CalledFrom">For a parameter, the line of the file the call was made from, when it was made from the file.</param>
public sealed record ValueChange(int? Line, string Value, int? CalledFrom);

/// <summary>One call to a function of the file that gave back None, other than by stopping with an error.</summary>
/// <param name="Function">The function's name.</param>
/// <param name="Line">The line the call ended on: its return, or the last line it ran when it reached its end.</param>
/// <param name="CalledFrom">The line of the file the call was made from, or null when it was called from elsewhere.</param>
/// <param name="Given">What each of its parameters held as the call began.</param>
/// <param name="TimesRun">How many times each line ran during that call.</param>
public sealed record NoneReturned(string Function, int Line, int? CalledFrom, IReadOnlyDictionary<string, string> Given, IReadOnlyDictionary<int, int> TimesRun);

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
        import ast, collections, json, os, runpy, sys, threading
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
        missing = object()
        follow = [name for name in os.environ.get("FIXFINDER_FOLLOW", "").split(",") if name]
        follow_scope = os.environ.get("FIXFINDER_FOLLOW_IN", "<module>")
        follow_line = int(os.environ.get("FIXFINDER_FOLLOW_LINE", "0"))
        times_run = collections.Counter()
        held, changes, previous_line, times_run_in_call, started_with, unwinding = {}, {}, {}, {}, {}, set()
        gave_none = collections.deque(maxlen=50)
        followed_at_error = {}
        def caller_line(frame):
            caller = frame.f_back
            return caller.f_lineno if caller is not None and os.path.normcase(caller.f_code.co_filename) == wanted else None
        def followed(frame, event, arg):
            key, scope = id(frame), frame.f_code.co_name
            if event == "line":
                times_run[frame.f_lineno] += 1
                unwinding.discard(key)
                if scope != "<module>":
                    times_run_in_call.setdefault(key, collections.Counter())[frame.f_lineno] += 1
                    if key not in previous_line:
                        code = frame.f_code
                        parameters = code.co_varnames[:code.co_argcount + code.co_kwonlyargcount]
                        started_with[key] = {name: repr(frame.f_locals.get(name))[:60] for name in parameters}
            if scope == follow_scope:
                for name in follow:
                    value = frame.f_locals.get(name, missing)
                    if value is not held.get((key, name), missing):
                        held[(key, name)] = value
                        if value is not missing:
                            made_by = previous_line.get(key)
                            changes.setdefault((key, name), collections.deque(maxlen=20)).append(
                                {"line": made_by, "value": repr(value)[:60], "from": caller_line(frame) if made_by is None else None})
                if event == "exception" and frame.f_lineno == follow_line:
                    followed_at_error.clear()
                    followed_at_error.update({name: list(changes.get((key, name), [])) for name in follow})
            if event == "line":
                previous_line[key] = frame.f_lineno
            elif event == "exception":
                unwinding.add(key)
            elif event == "return":
                ran, given = times_run_in_call.pop(key, None), started_with.pop(key, {})
                if arg is None and key not in unwinding and scope != "<module>":
                    gave_none.append({"function": scope, "line": frame.f_lineno, "from": caller_line(frame), "given": given,
                                      "ran": {str(line): times for line, times in (ran or {}).items()}})
                unwinding.discard(key)
                previous_line.pop(key, None)
                for name in follow:
                    held.pop((key, name), None)
                    changes.pop((key, name), None)
        def local(frame, event, arg):
            if event == "line":
                note(frame.f_lineno)
                if watch and frame.f_lineno == watch:
                    if len(watched) < moststates:
                        watched.append({k: repr(v)[:60] for k, v in list(frame.f_locals.items())[:12] if not k.startswith("__")})
                    else:
                        state["watchcut"] = True
            if follow_line:
                followed(frame, event, arg)
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
            if follow_line:
                result["followed"] = followed_at_error
                result["times_run"] = {str(line): times for line, times in times_run.items()}
                result["gave_none"] = list(gave_none)
            with open(out, "w", encoding="utf-8") as saved:
                json.dump(result, saved)
        """;

    /// <summary>Runs the whole program with <paramref name="input"/> typed in.</summary>
    public static Task<Trace?> RunAsync(string interpreter, string file, string input, TimeSpan timeout, CancellationToken cancellationToken,
        int watchLine = 0) =>
        TraceAsync(interpreter, file, ["program"], input, timeout, cancellationToken, watchLine);

    /// <summary>
    /// Runs the whole program with <paramref name="input"/> typed in, following what each of <paramref name="names"/> holds in
    /// the frames of <paramref name="scope"/> - a function's name, or &lt;module&gt; for the file's own top level - up to the
    /// moment it stops with an error on <paramref name="line"/>; how many times each line runs; and which calls to the file's
    /// functions give back None.
    /// </summary>
    public static Task<Trace?> FollowAsync(string interpreter, string file, string input, IReadOnlyList<string> names, string scope, int line,
        TimeSpan timeout, CancellationToken cancellationToken) =>
        TraceAsync(interpreter, file, ["program"], input, timeout, cancellationToken, following: new Dictionary<string, string>
        {
            ["FIXFINDER_FOLLOW"] = string.Join(",", names),
            ["FIXFINDER_FOLLOW_IN"] = scope,
            ["FIXFINDER_FOLLOW_LINE"] = line.ToString(),
        });

    /// <summary>Calls one top-level function of the file with keyword arguments written as a Python dict literal.</summary>
    /// <param name="watchLine">A line to record the variables at every time the run reaches it, or 0 for none.</param>
    public static Task<Trace?> CallAsync(string interpreter, string file, string function, string arguments, string input, TimeSpan timeout,
        CancellationToken cancellationToken, int watchLine = 0) =>
        TraceAsync(interpreter, file, ["call", function, arguments], input, timeout, cancellationToken, watchLine);

    private static async Task<Trace?> TraceAsync(string interpreter, string file, IReadOnlyList<string> how, string input, TimeSpan timeout,
        CancellationToken cancellationToken, int watchLine = 0, IReadOnlyDictionary<string, string>? following = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-probe", Guid.NewGuid().ToString("N")[..12]);

        try
        {
            Directory.CreateDirectory(folder);
            var script = Path.Combine(folder, "fixfinder_probe.py");
            var output = Path.Combine(folder, "trace.json");
            await File.WriteAllTextAsync(script, Script, new UTF8Encoding(false), cancellationToken);

            var arguments = string.Join(" ", new[] { "-X utf8", Quote(script), Quote(output), Quote(file) }.Concat(how.Select(Quote)));
            var environment = new Dictionary<string, string>
            {
                ["FIXFINDER_MOST_LINES"] = MostLines.ToString(),
                ["FIXFINDER_WATCH"] = watchLine.ToString(),
                ["FIXFINDER_MOST_STATES"] = MostStates.ToString(),
            };
            foreach (var (setting, value) in following ?? new Dictionary<string, string>()) environment[setting] = value;

            // A notebook's code runs from the notebook's folder, where its data and its own modules are, not the script's; a
            // file in a folder of its own imports the program's modules from the folders the program runs it with.
            var folderOfCode = NotebookScript.FolderOfCode(file);
            var importFolders = ProgramLayout.PythonImportFolders(file, interpreter).ToList();
            if (!string.Equals(folderOfCode, Path.GetDirectoryName(Path.GetFullPath(file)), StringComparison.OrdinalIgnoreCase)) importFolders.Insert(0, folderOfCode);
            if (importFolders.Count > 0) environment["PYTHONPATH"] = ProgramLayout.PythonPathWith(importFolders);

            await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = interpreter,
                Arguments = arguments,
                WorkingDirectory = folderOfCode,
                Timeout = timeout,
            }.WithInput(input).WithEnvironment(environment), cancellationToken);

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

        var followed = root.TryGetProperty("followed", out var names) && names.ValueKind == JsonValueKind.Object
            ? names.EnumerateObject().ToDictionary(
                name => name.Name,
                name => (IReadOnlyList<ValueChange>)name.Value.EnumerateArray()
                    .Select(change => new ValueChange(NumberOrNull(change, "line"), change.GetProperty("value").GetString() ?? "", NumberOrNull(change, "from")))
                    .ToList(),
                StringComparer.Ordinal)
            : new Dictionary<string, IReadOnlyList<ValueChange>>();

        var timesRun = root.TryGetProperty("times_run", out var counted) && counted.ValueKind == JsonValueKind.Object ? LineCounts(counted) : new Dictionary<int, int>();

        var gaveNone = root.TryGetProperty("gave_none", out var returns) && returns.ValueKind == JsonValueKind.Array
            ? returns.EnumerateArray()
                .Select(call => new NoneReturned(
                    call.GetProperty("function").GetString() ?? "",
                    call.GetProperty("line").GetInt32(),
                    NumberOrNull(call, "from"),
                    call.GetProperty("given").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal),
                    LineCounts(call.GetProperty("ran"))))
                .ToList()
            : [];

        if (root.GetProperty("error") is not { ValueKind: JsonValueKind.Object } error)
        {
            return new Trace(lines, cut, null, null, null, new Dictionary<string, string>())
            {
                Watched = watched,
                WatchedCut = watchedCut,
                Followed = followed,
                TimesRun = timesRun,
                GaveNone = gaveNone,
            };
        }

        var values = error.GetProperty("values").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);
        return new Trace(lines, cut, error.GetProperty("type").GetString(), error.GetProperty("message").GetString(),
            error.GetProperty("line") is { ValueKind: JsonValueKind.Number } line ? line.GetInt32() : null, values)
        {
            Watched = watched,
            WatchedCut = watchedCut,
            Followed = followed,
            TimesRun = timesRun,
            GaveNone = gaveNone,
        };
    }

    private static int? NumberOrNull(JsonElement holder, string property) =>
        holder.TryGetProperty(property, out var number) && number.ValueKind == JsonValueKind.Number ? number.GetInt32() : null;

    /// <summary>A line-to-count table the script wrote with each line number as a key in quotes.</summary>
    private static Dictionary<int, int> LineCounts(JsonElement counts) =>
        counts.EnumerateObject()
            .Where(entry => int.TryParse(entry.Name, out _))
            .ToDictionary(entry => int.Parse(entry.Name), entry => entry.Value.GetInt32());
}
