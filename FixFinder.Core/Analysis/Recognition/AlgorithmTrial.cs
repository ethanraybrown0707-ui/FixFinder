using System.Globalization;
using System.Text;
using System.Text.Json;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Analysis.Recognition;

/// <summary>
/// What an algorithm recognised in the code must do, tried on lists of FixFinder's own: a function that looks like a sort
/// is called with lists empty, of one item, in order, in reverse order, with repeats and below zero, and must give back
/// the same items in order each time; one that looks like a search is called with lists in order and each value in them,
/// and values not in them, and must find every one that is there and none that is not. What it does is said beside the
/// recognition, and the first list it got wrong becomes a finding, with what it was called with and what it gave.
/// </summary>
/// <remarks>
/// For a Python function at the top of its file whose parameters are the list, what a search looks for, and a length
/// - n, count, size, length - or have defaults; it is called from a copy of the file's definitions alone, as the
/// confirmations call one, so the program's own top-level code never runs. A sort that gives back nothing is read from
/// the list it was given, which it sorted in place. A sort that gives back every list largest first is a sort too. A
/// binary search that misses on lists in order is tried on the same lists in reverse order, which it may be written for.
/// A call still going after 2 seconds ends the trial, and nothing is said of it.
/// </remarks>
public static class AlgorithmTrial
{
    private const string TrialFile = "fixfinder_trial.py";
    private const string PlanFile = "fixfinder_trial.json";
    private const string Said = "FIXFINDER-TRIAL ";
    private const int MostTried = 6;

    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Lists to sort, shortest first, so the first one a sort gets wrong is the shortest it gets wrong: empty, of one, of
    /// two either way, mixed, in reverse order, with repeats, in order, below zero.
    /// </summary>
    private static readonly int[][] ToSort = [[], [1], [2, 1], [1, 2], [3, 1, 2], [4, 3, 2, 1], [2, 2, 1, 1], [1, 2, 3, 4, 5], [7, -3, 0, 12, -3], [5, 2, 9, 1, 5, 6]];

    /// <summary>Lists to search, in order: empty, of one, of two, of four and six spread out, and one with a repeat.</summary>
    private static readonly int[][] ToSearch = [[], [5], [1, 3], [1, 3, 5, 7], [1, 3, 5, 7, 9, 11], [2, 4, 4, 8]];

    /// <summary>A list out of order, which a linear search needs no order to search.</summary>
    private static readonly int[] OutOfOrder = [5, 2, 9, 1];

    /// <summary>The names a parameter that is given the list's length goes by.</summary>
    private static readonly HashSet<string> LengthNames = new(["n", "count", "size", "length", "len", "num"], StringComparer.Ordinal);

    public sealed record Outcome(IReadOnlyList<AlgorithmSeen> Seen, IReadOnlyList<Finding> Findings);

    private sealed record Trial(int[] List, int? Sought);

    private sealed record Result(Trial Trial, JsonElement? Returned, JsonElement? After, string? Error);

    public static async Task<Outcome> TryAsync(IReadOnlyList<AlgorithmSeen> recognised, IrProgram program, string python, CancellationToken cancellationToken)
    {
        var seen = recognised.ToList();
        var findings = new List<Finding>();
        var tried = 0;

        for (var index = 0; index < seen.Count && tried < MostTried; index++)
        {
            var recognition = seen[index];
            if (Roles(recognition, program) is not { } roles) continue;
            tried++;

            var (said, broken) = await JudgedAsync(recognition, roles, python, ascending: true, cancellationToken);
            if (broken is not null && recognition.Algorithm == "a binary search" &&
                await JudgedAsync(recognition, roles, python, ascending: false, cancellationToken) is { Broken: null, Said: { } reversed })
                (said, broken) = (reversed, null);

            if (said is null) continue;
            seen[index] = recognition with { Tried = said };
            if (broken is not null) findings.Add(FindingFor(recognition, broken));
        }

        return new Outcome(seen, findings);
    }

    /// <summary>
    /// The part each of the function's parameters plays - the list, what a search looks for, the list's length - or null
    /// when the function cannot be called that way: not a Python function at the top of its file, or a parameter that
    /// is none of those and has no default.
    /// </summary>
    private static Dictionary<string, string>? Roles(AlgorithmSeen recognition, IrProgram program)
    {
        if (!recognition.File.EndsWith(".py", StringComparison.OrdinalIgnoreCase) || recognition.Function == AlgorithmRecognition.TopLevelCode ||
            recognition.Function.Contains('.') || recognition.List is null || !recognition.IsSort && (recognition.Sought is null || recognition.Gives is null))
            return null;

        var function = program.AllFunctions.FirstOrDefault(each => each.FullName == recognition.Function && each.Owner is null &&
                                                                   string.Equals(Path.GetFullPath(each.Span.File), Path.GetFullPath(recognition.File), StringComparison.OrdinalIgnoreCase));
        if (function is null || function.IsAsync || function.IsGenerator) return null;

        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in function.Parameters)
        {
            if (parameter.Kind is ParameterKind.Rest or ParameterKind.Keywords) return null;
            if (parameter.Name == recognition.List) roles[parameter.Name] = "list";
            else if (parameter.Name == recognition.Sought) roles[parameter.Name] = "sought";
            else if (LengthNames.Contains(parameter.Name)) roles[parameter.Name] = "length";
            else if (parameter.Default is null) return null;
        }

        return roles.ContainsValue("list") && (recognition.IsSort || roles.ContainsValue("sought")) ? roles : null;
    }

    /// <summary>
    /// The function called on its lists, and what that showed: a sentence saying every call was right, or saying the
    /// first that was not - and that one again, to be a finding. Both null when a call did not finish, or gave back
    /// something it cannot be judged by.
    /// </summary>
    private static async Task<(string? Said, string? Broken)> JudgedAsync(AlgorithmSeen recognition, Dictionary<string, string> roles, string python,
        bool ascending, CancellationToken cancellationToken)
    {
        var trials = recognition.IsSort ? ToSort.Select(list => new Trial(list, null)).ToList() : SearchTrials(recognition, ascending);
        if (await RunAsync(recognition, roles, trials, python, cancellationToken) is not { } results) return (null, null);

        return recognition.IsSort ? JudgeSort(recognition, results) : JudgeSearch(recognition, results, trials, ascending);
    }

    private static List<Trial> SearchTrials(AlgorithmSeen recognition, bool ascending)
    {
        var lists = ToSearch.Select(list => ascending ? list : list.Reverse().ToArray()).ToList();
        if (recognition.Algorithm == "a linear search") lists.Add(OutOfOrder);

        var trials = new List<Trial>();
        foreach (var list in lists)
        {
            var sought = list.Distinct().ToList();
            if (list.Length == 0) sought.Add(3);
            else
            {
                sought.Add(list.Min() - 1);
                sought.Add(list.Max() + 1);
                // A value between two of them, where a search that stops one short would miss nothing it should find.
                var ordered = list.Distinct().Order().ToList();
                if (ordered.Zip(ordered.Skip(1)).Where(pair => pair.Second - pair.First > 1).Select(pair => (int?)(pair.First + 1)).FirstOrDefault() is { } between)
                    sought.Add(between);
            }

            trials.AddRange(sought.Select(value => new Trial(list, value)));
        }

        return trials;
    }

    /// <summary>
    /// Whether each list came back as the same items in order. Which order - smallest or largest first - is taken from
    /// the first list given in neither, [3, 1, 2], as a list already in one order says nothing when it comes back as it
    /// went; then every list is judged by it, smallest first, so the one shown going wrong is the shortest that did.
    /// </summary>
    private static (string? Said, string? Broken) JudgeSort(AlgorithmSeen recognition, IReadOnlyList<Result> results)
    {
        var name = $"`{recognition.Function}`";
        static bool InOrder(IReadOnlyList<long> items, int direction) => items.Zip(items.Skip(1)).All(pair => direction * pair.First.CompareTo(pair.Second) <= 0);

        var given = new List<(Result Result, List<long> Sorted)>();
        foreach (var result in results)
        {
            if (result.Error is { } error) return Broken($"Called with {Show(result.Trial.List)}, {name} stopped with {error}");
            if ((Numbers(result.Returned) ?? Numbers(result.After)) is not { } sorted) return (null, null);
            given.Add((result, sorted));
        }

        var mixed = given.FirstOrDefault(each => !InOrder(Longs(each.Result.Trial.List), 1) && !InOrder(Longs(each.Result.Trial.List), -1));
        var direction = mixed.Sorted is { } shown && InOrder(shown, -1) && !InOrder(shown, 1) ? -1 : 1;

        foreach (var (result, sorted) in given)
        {
            var list = Show(result.Trial.List);
            var expected = Longs(result.Trial.List).Order().ToList();
            if (direction == -1) expected.Reverse();

            if (!sorted.Order().SequenceEqual(expected.Order()))
                return Broken($"Called with {list}, {name} gave back {Show(sorted)} - not the same items, which in order are {Show(expected)}");
            if (!InOrder(sorted, direction))
                return Broken($"Called with {list}, {name} gave back {Show(sorted)}, which is not in order - in order it is {Show(expected)}");
        }

        return ($"Called with {results.Count} lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - " +
                $"it gave each back {(direction == -1 ? "largest first" : "in order")}, with the same items.", null);

        static (string?, string?) Broken(string what) => (what + ".", what);
    }

    private static List<long> Longs(IEnumerable<int> items) => items.Select(item => (long)item).ToList();

    private static (string? Said, string? Broken) JudgeSearch(AlgorithmSeen recognition, IReadOnlyList<Result> results, IReadOnlyList<Trial> trials, bool ascending)
    {
        var name = $"`{recognition.Function}`";
        foreach (var result in results)
        {
            var (list, sought) = (result.Trial.List, result.Trial.Sought!.Value);
            var called = $"Called with {Show(list)} and {sought}, {name}";
            if (result.Error is { } error) return Broken($"{called} stopped with {error}");
            if (result.Returned is not { } returned) return (null, null);

            var present = Array.IndexOf(list, sought);
            bool found;
            switch (recognition.Gives)
            {
                case SearchGives.Position when returned.ValueKind == JsonValueKind.Number && returned.TryGetInt64(out var position):
                    if (position >= 0 && position < list.Length && list[position] != sought)
                        return Broken($"{called} gave {position}, the position of {list[position]}, not of {sought}");
                    found = position >= 0 && position < list.Length;
                    break;
                case SearchGives.Position when returned.ValueKind is JsonValueKind.Null or JsonValueKind.False:
                case SearchGives.Item when returned.ValueKind is JsonValueKind.Null or JsonValueKind.False:
                    found = false;
                    break;
                case SearchGives.Item when returned.ValueKind == JsonValueKind.Number && returned.TryGetInt64(out var item):
                    found = item == sought;
                    if (!found && present < 0 && item != -1) return (null, null);
                    break;
                case SearchGives.Truth when returned.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    found = returned.ValueKind == JsonValueKind.True;
                    break;
                default:
                    return (null, null);
            }

            if (found && present < 0) return Broken($"{called} gave {PythonText(returned)} - but {sought} is not in the list");
            if (!found && present >= 0) return Broken($"{called} gave {PythonText(returned)} - but {sought} is there, at position {present}");
        }

        var lists = trials.Select(trial => trial.List).Distinct().Count();
        return ($"Called with {lists} lists{(ascending ? "" : " in reverse order")} of FixFinder's own and {results.Count} values, each in them or not, " +
                "it found every one that was there and none that was not.", null);

        static (string?, string?) Broken(string what) => (what + ".", what);
    }

    /// <summary>A recognition's first wrong call as a finding: certain, as it was seen.</summary>
    private static Finding FindingFor(AlgorithmSeen recognition, string broken)
    {
        var rule = recognition.IsSort ? "algorithm-sort-wrong" : "algorithm-search-wrong";
        var guide = Guidebook.For(recognition.File, FindingKind.Logic, rule);
        return new Finding
        {
            Kind = FindingKind.Logic,
            Severity = Severity.Error,
            Confidence = Confidence.Certain,
            File = recognition.File,
            Line = recognition.Line,
            Title = recognition.IsSort ? $"`{recognition.Function}` does not sort every list" : $"`{recognition.Function}` does not find every value",
            Explanation = $"`{recognition.Function}` looks like {recognition.Algorithm}. {broken}. {guide.Explanation}",
            Found = broken + ".",
            WhyItMatters = guide.WhyItMatters,
            SuggestedFix = guide.SuggestedFix,
            CorrectedExample = guide.Example,
            RuleId = rule,
            Family = rule,
        };
    }

    /// <summary>
    /// The function called once on each trial, from a copy of the file's definitions: what it gave back, the list after
    /// the call, or what stopped it. Null when the run gave no answer for every trial - one still going, or Python not
    /// reading the file.
    /// </summary>
    private static async Task<IReadOnlyList<Result>?> RunAsync(AlgorithmSeen recognition, Dictionary<string, string> roles, IReadOnlyList<Trial> trials,
        string python, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-trial", Guid.NewGuid().ToString("N")[..12]);
        try
        {
            Directory.CreateDirectory(folder);
            var plan = JsonSerializer.Serialize(new { roles, trials = trials.Select(trial => new { list = trial.List, sought = trial.Sought }) });
            await File.WriteAllTextAsync(Path.Combine(folder, PlanFile), plan, new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, TrialFile), Script, new UTF8Encoding(false), cancellationToken);

            var ran = await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = python,
                Arguments = $"-X utf8 \"{Path.Combine(folder, TrialFile)}\" \"{recognition.File}\" {recognition.Function} \"{Path.Combine(folder, PlanFile)}\"",
                WorkingDirectory = Path.GetDirectoryName(recognition.File)!,
                Timeout = RunTimeout,
                StandardInput = "",
            }, cancellationToken);

            var results = new List<Result>();
            foreach (var line in ran.Lines.Where(line => line.Stream == StreamKind.StdOut && line.Text.StartsWith(Said, StringComparison.Ordinal)))
            {
                using var answer = JsonDocument.Parse(line.Text[Said.Length..]);
                var root = answer.RootElement;
                if (root.TryGetProperty("unfinished", out _)) return null;
                var trial = trials[root.GetProperty("trial").GetInt32()];
                results.Add(new Result(trial,
                    root.TryGetProperty("returned", out var returned) ? returned.Clone() : null,
                    root.TryGetProperty("after", out var after) ? after.Clone() : null,
                    root.TryGetProperty("error", out var error) ? error.GetString() : null));
            }

            return results.Count == trials.Count ? results : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static List<long>? Numbers(JsonElement? value)
    {
        if (value is not { ValueKind: JsonValueKind.Array } array) return null;
        var numbers = new List<long>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt64(out var number)) return null;
            numbers.Add(number);
        }

        return numbers;
    }

    private static string Show(IEnumerable<int> list) => "[" + string.Join(", ", list) + "]";

    private static string Show(IEnumerable<long> list) => "[" + string.Join(", ", list) + "]";

    /// <summary>A value as Python writes it: None, True, -1.</summary>
    private static string PythonText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => "None",
        JsonValueKind.True => "True",
        JsonValueKind.False => "False",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.String => "'" + value.GetString() + "'",
        JsonValueKind.Array => "[" + string.Join(", ", value.EnumerateArray().Select(PythonText)) + "]",
        _ => value.TryGetProperty("repr", out var shown) ? shown.GetString() ?? "" : value.GetRawText(),
    };

    /// <summary>
    /// Calls the function once for each trial, from the file's definitions alone - its functions, classes, imports and
    /// constants - and prints, for each, what it gave back, the list after the call, or what stopped it. A call still
    /// going after 2 seconds is said, and the trials end there.
    /// </summary>
    private const string Script = """
        import ast, json, os, sys, threading

        target, name, plan_file = sys.argv[1], sys.argv[2], sys.argv[3]
        with open(plan_file, encoding="utf-8") as given:
            plan = json.load(given)

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

        def plain(value, depth=0):
            if value is None or isinstance(value, (bool, int, float, str)):
                return value
            if isinstance(value, (list, tuple)) and depth < 3:
                return [plain(item, depth + 1) for item in value]
            return {"repr": repr(value)[:80]}

        def say(outcome):
            sys.stdout.write("FIXFINDER-TRIAL " + json.dumps(outcome) + "\n")
            sys.stdout.flush()

        sys.path.insert(0, os.path.dirname(os.path.abspath(target)))
        with open(target, encoding="utf-8") as source:
            tree = ast.parse(source.read(), target)
        tree.body = [node for node in tree.body if kept(node)]
        space = {"__name__": "fixfinder_trial", "__file__": target}
        exec(compile(tree, target, "exec"), space)
        function = space[name]
        list_parameter = next(parameter for parameter, role in plan["roles"].items() if role == "list")

        for index, trial in enumerate(plan["trials"]):
            arguments = {}
            for parameter, role in plan["roles"].items():
                arguments[parameter] = list(trial["list"]) if role == "list" else trial["sought"] if role == "sought" else len(trial["list"])
            outcome = {"trial": index}

            def call():
                try:
                    outcome["returned"] = plain(function(**arguments))
                except BaseException as error:
                    said = str(error)
                    outcome["error"] = type(error).__name__ + (": " + said[:200] if said else "")

            worker = threading.Thread(target=call, daemon=True)
            worker.start()
            worker.join(2)
            if worker.is_alive():
                say({"trial": index, "unfinished": True})
                os._exit(0)
            outcome["after"] = plain(arguments[list_parameter])
            say(outcome)

        os._exit(0)
        """;
}
