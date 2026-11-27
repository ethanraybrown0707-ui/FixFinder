using System.Globalization;
using System.Text;
using System.Text.Json;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Analysis.Recognition;

/// <summary>
/// A function that works out the same answers again and again: a Python function at the top of its file, of one
/// parameter, that calls itself twice or more and nothing else - return fib(n - 1) + fib(n - 2) - called with 5, 10, 15,
/// 20 and 25, each call counted, and called again kept with functools.cache, which works each answer out once. When the
/// cache gives back the same answer for every number and, at the largest, makes a tenth of the calls or fewer, the
/// counts are a finding on the Efficiency tab.
/// </summary>
/// <remarks>
/// Only a function that calls nothing but itself, sets nothing but names of its own, and holds nothing FixFinder's reader
/// could not read is tried: only such a function gives back the same answer for the same number however often it is
/// called, which is what makes keeping its answers right. It is called from a copy of its file's definitions alone, as the
/// algorithm trials call one, so the program's own code at the top of its file never runs; a call still going after 2
/// seconds, or past 5,000,000 calls, ends the trial at the number before it.
/// </remarks>
public static partial class AlgorithmTrial
{
    public const string RepeatedCallsRule = "efficiency-repeated-calls";

    /// <summary>The numbers a function that calls itself is called with, smallest first.</summary>
    private static readonly int[] RepeatedCallSizes = [5, 10, 15, 20, 25];

    /// <summary>How many times fewer calls the cache has to make, at the largest number both finished, for it to be said.</summary>
    private const int FewerCallsWithTheCache = 10;

    /// <summary>The most calls counted in one trial before it is stopped.</summary>
    private const int MostCallsCounted = 5_000_000;

    private const string RepeatedCallsFile = "fixfinder_repeated_calls.py";

    /// <summary>
    /// The Python functions in <paramref name="chosenFile"/> that may work out the same answers again and again: at the top
    /// of the file, of one plain parameter, calling themselves twice or more and nothing else, setting nothing but names of
    /// their own, and holding no statement or expression the reader could not read.
    /// </summary>
    public static IReadOnlyList<IrFunction> RepeatedCallCandidates(IrProgram program, string? chosenFile) =>
        program.Language != SourceLanguage.Python ? [] :
        [.. program.Functions.Where(function =>
            // At the top of its file: written inside nothing but the file's own code, which is how the reader says it.
            function.Owner is null && function.EnclosedBy is null or IrFunction.ModuleBody && function.Name != IrFunction.ModuleBody &&
            function.OuterNames.Count == 0 &&
            function.Parameters is [{ Kind: ParameterKind.Normal }] &&
            (chosenFile is null || SameFile(function.Span.File, chosenFile)) &&
            CallsOnlyItself(function))];

    /// <summary>
    /// Whether a function calls itself twice or more and nothing else, and sets nothing but names of its own: every call in
    /// it is of its own name, every assignment is to a name, and nothing in it is a statement or an expression its
    /// reader kept without reading.
    /// </summary>
    private static bool CallsOnlyItself(IrFunction function)
    {
        var selfCalls = 0;
        foreach (var statement in IrWalk.Statements(function.Body))
        {
            switch (statement)
            {
                case OpaqueStmt or Using or Throw:
                    return false;
                case Assign { Target: not Name }:
                    return false;
            }

            foreach (var expression in IrWalk.Expressions(statement).SelectMany(IrWalk.Within))
            {
                switch (expression)
                {
                    case Opaque or AssignValue:
                        return false;
                    case Call { Callee: Name callee } when callee.Identifier == function.Name:
                        selfCalls++;
                        break;
                    case Call:
                        return false;
                }
            }
        }

        return selfCalls >= 2;
    }

    private static bool SameFile(string written, string chosen) =>
        written.Length > 0 && string.Equals(Path.GetFullPath(written), Path.GetFullPath(chosen), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Calls each function that may work out the same answers again and again with 5 up to 25, each call counted, as it
    /// is and kept with functools.cache, and gives back a finding for each whose cache gives back the same answers with a
    /// tenth of the calls or fewer. Nothing when there is none to try, or the run gave nothing.
    /// </summary>
    public static async Task<IReadOnlyList<Finding>> TryRepeatedCallsAsync(IrProgram program, string? chosenFile, string python, CancellationToken cancellationToken)
    {
        var candidates = RepeatedCallCandidates(program, chosenFile);
        if (candidates.Count == 0) return [];

        var file = candidates[0].Span.File;
        var plan = JsonSerializer.Serialize(new
        {
            functions = candidates.Select(function => function.Name).ToList(),
            sizes = RepeatedCallSizes,
            most_calls = MostCallsCounted,
        });

        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-trial", Guid.NewGuid().ToString("N")[..12]);
        TargetRunResult ran;
        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, PlanFile), plan, new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, RepeatedCallsFile), RepeatedCallsScript, new UTF8Encoding(false), cancellationToken);

            ran = await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = python,
                Arguments = $"-X utf8 \"{Path.Combine(folder, RepeatedCallsFile)}\" \"{file}\" \"{Path.Combine(folder, PlanFile)}\"",
                WorkingDirectory = Path.GetDirectoryName(file)!,
                Timeout = RunTimeout,
                StandardInput = "",
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        var counted = CountedCalls(ran);
        return [.. candidates.Select(function => RepeatedCallsFinding(function, counted.Where(sized => sized.Function == function.Name).ToList()))
            .OfType<Finding>()];
    }

    /// <summary>What one number did: the calls made, and the answer given back, as the function is and kept with the cache.</summary>
    private sealed record CountedSize(string Function, int Size, long Calls, string Answer, long CachedCalls, string CachedAnswer);

    /// <summary>Each number that finished both ways, as the run said it; a number that did not finish both ways ends its function's trial.</summary>
    private static List<CountedSize> CountedCalls(TargetRunResult ran)
    {
        var counted = new List<CountedSize>();
        foreach (var line in ran.Lines.Where(line => line.Stream == StreamKind.StdOut && line.Text.StartsWith(Said, StringComparison.Ordinal)))
        {
            try
            {
                using var said = JsonDocument.Parse(line.Text[Said.Length..]);
                var root = said.RootElement;
                if (root.TryGetProperty("unfinished", out _) ||
                    !root.TryGetProperty("plain", out var asItIs) || !root.TryGetProperty("cached", out var cached) ||
                    !asItIs.TryGetProperty("returned", out var answer) || !cached.TryGetProperty("returned", out var cachedAnswer))
                {
                    continue;
                }

                counted.Add(new CountedSize(root.GetProperty("function").GetString() ?? "", root.GetProperty("size").GetInt32(),
                    asItIs.GetProperty("calls").GetInt64(), answer.GetRawText(), cached.GetProperty("calls").GetInt64(), cachedAnswer.GetRawText()));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
            }
        }

        return counted;
    }

    /// <summary>
    /// The finding for a function whose cache gave back the same answer for every number that finished, with a tenth of the
    /// calls or fewer at the largest; null for one that did not.
    /// </summary>
    private static Finding? RepeatedCallsFinding(IrFunction function, List<CountedSize> sizes)
    {
        if (sizes.Count == 0 || sizes.Any(size => size.Answer != size.CachedAnswer)) return null;

        var largest = sizes[^1];
        if (largest.Calls < FewerCallsWithTheCache * largest.CachedCalls) return null;

        var name = function.Name;
        var found = $"Called with {Listed(sizes.Select(size => size.Size.ToString(CultureInfo.InvariantCulture)))}, `{name}` was called " +
                    $"{Listed(sizes.Select(size => size.Calls.ToString("N0", CultureInfo.InvariantCulture)))} times, counting the first call; " +
                    $"kept with `@functools.cache`, {Listed(sizes.Select(size => size.CachedCalls.ToString("N0", CultureInfo.InvariantCulture)))} times, " +
                    $"and it gave back the same {(sizes.Count == 1 ? "answer" : "answers")} both ways: " +
                    $"{Listed(sizes.Select(size => size.Answer))}";

        var guide = Guidebook.For(function.Span.File, FindingKind.Performance, RepeatedCallsRule);
        return new Finding
        {
            Kind = FindingKind.Performance,
            Severity = Severity.Suggestion,
            Confidence = Confidence.Certain,
            File = function.Span.File,
            Line = function.Span.Line,
            Title = $"`{name}` works out the same answers again and again",
            Explanation = $"`{name}` calls itself more than once, and those calls call it again with the same numbers. {guide.Explanation}",
            Found = found + ".",
            WhyItMatters = guide.WhyItMatters,
            SuggestedFix = guide.SuggestedFix,
            CorrectedExample = guide.Example,
            RuleId = RepeatedCallsRule,
            Family = RepeatedCallsRule,
        };
    }

    /// <summary>"5, 10 and 15".</summary>
    private static string Listed(IEnumerable<string> items)
    {
        var all = items.ToList();
        return all.Count == 1 ? all[0] : $"{string.Join(", ", all[..^1])} and {all[^1]}";
    }

    /// <summary>
    /// Calls each function with each number, as it is and kept with functools.cache, counting every call - the first and
    /// each the function makes of itself, which goes through the same name - and prints, for each number, the calls and the
    /// answer both ways. A call still going after 2 seconds is said, and the trials end there; one past the most calls
    /// counted is stopped, and its function's trial ends there.
    /// </summary>
    private const string RepeatedCallsScript = PythonTrialHelpers + "\n\n" + """
        import functools

        target, plan_file = sys.argv[1], sys.argv[2]
        with open(plan_file, encoding="utf-8") as given:
            plan = json.load(given)

        space = definitions(target)

        class TooManyCalls(Exception):
            pass

        for name in plan["functions"]:
            original = space.get(name)
            if original is None:
                continue
            counted = {"calls": 0}

            def counting(*arguments, original=original, counted=counted):
                counted["calls"] += 1
                if counted["calls"] > plan["most_calls"]:
                    raise TooManyCalls()
                return original(*arguments)

            for size in plan["sizes"]:
                record = {"function": name, "size": size}
                for kind in ("plain", "cached"):
                    counted["calls"] = 0
                    called = counting if kind == "plain" else functools.cache(counting)
                    space[name] = called
                    outcome = {}

                    def call(called=called, outcome=outcome, size=size):
                        try:
                            outcome["returned"] = plain(called(size))
                        except TooManyCalls:
                            outcome["stopped"] = True
                        except BaseException as error:
                            outcome["error"] = type(error).__name__

                    worker = threading.Thread(target=call, daemon=True)
                    worker.start()
                    worker.join(2)
                    if worker.is_alive():
                        say({"function": name, "size": size, "unfinished": True})
                        os._exit(0)
                    outcome["calls"] = counted["calls"]
                    record[kind] = outcome

                space[name] = original
                say(record)
                if "returned" not in record["plain"] or "returned" not in record["cached"]:
                    break

        os._exit(0)
        """;
}
