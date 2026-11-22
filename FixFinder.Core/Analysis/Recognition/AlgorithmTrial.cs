using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FixFinder.Core.Analysis.Dynamic;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Analysis.Recognition;

/// <summary>
/// What an algorithm recognised in the code must do, tried on lists of FixFinder's own: a function that looks like a
/// sort is called with lists empty, of one item, in order, in reverse order, with repeats and below zero, and must give
/// back the same items in order each time; one that looks like a search is called with lists in order and each value in
/// them, and values not in them, and must find every one that is there and none that is not. What it does is said
/// beside the recognition, and the first list it got wrong becomes a finding, with what it was called with and what it
/// gave.
/// </summary>
/// <remarks>
/// For a Python function at the top of its file, or a JavaScript one at the top of a CommonJS file, whose parameters
/// are the list, what a search looks for, and a length - n, count, size, length - or have defaults; it is called from a
/// copy of the file's definitions alone, as the confirmations call one, so the program's own top-level code never runs.
/// For a static Java method whose list is an int[], a List&lt;Integer&gt; or an ArrayList&lt;Integer&gt;, what a search
/// looks for an int or an Integer, and a length an int, called by reflection from a small class compiled beside a copy
/// of its file, so the program's own main never runs. A sort that gives back nothing is read from the list it was
/// given, which it sorted in place. A sort that gives back every list largest first is a sort too. A binary search that
/// misses on lists in order is tried on the same lists in reverse order, which it may be written for. A call still
/// going after 2 seconds ends a Python or Java trial, and a JavaScript one still going when its run's 10 seconds are up
/// ends there; nothing is said of any of them.
/// </remarks>
public static partial class AlgorithmTrial
{
    private const string TrialFile = "fixfinder_trial.py";
    private const string PlanFile = "fixfinder_trial.json";
    private const string LoaderFile = "fixfinder_trial_loader.js";
    private const string CopyFile = "program_copy.js";
    private const string JavaTrialClass = "FixFinderTrial";
    private const string Said = "FIXFINDER-TRIAL ";
    private const int MostTried = 6;

    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan JavaScriptTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CompileTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Lists to sort, shortest first, so the first one a sort gets wrong is the shortest it gets wrong: empty, of one,
    /// of two either way, mixed, in reverse order, with repeats, in order, below zero.
    /// </summary>
    private static readonly int[][] ToSort = [[], [1], [2, 1], [1, 2], [3, 1, 2], [4, 3, 2, 1], [2, 2, 1, 1], [1, 2, 3, 4, 5], [7, -3, 0, 12, -3], [5, 2, 9, 1, 5, 6]];

    /// <summary>
    /// Lists to search, in order: empty, of one, of two, of four and six spread out, and one with a repeat.
    /// </summary>
    private static readonly int[][] ToSearch = [[], [5], [1, 3], [1, 3, 5, 7], [1, 3, 5, 7, 9, 11], [2, 4, 4, 8]];

    /// <summary>
    /// Lists to find the largest or smallest of - never empty, which has neither: of one, of two either way, mixed, all
    /// below zero, all above zero, with repeats, and largest and smallest at either end.
    /// </summary>
    private static readonly int[][] ToFindTheExtremeOf = [[5], [1, 2], [2, 1], [3, 1, 2], [-3, -7, -1], [4, 9, 6], [4, 4], [7, -3, 0, 12, -3], [12, 0, 7, -3]];

    /// <summary>A list out of order, which a linear search needs no order to search.</summary>
    private static readonly int[] OutOfOrder = [5, 2, 9, 1];

    /// <summary>The names a parameter that is given the list's length goes by.</summary>
    private static readonly HashSet<string> LengthNames = new(["n", "count", "size", "length", "len", "num"], StringComparer.Ordinal);

    public sealed record Outcome(IReadOnlyList<AlgorithmSeen> Seen, IReadOnlyList<Finding> Findings);

    /// <param name="Number">What a prime test is given: a number on its own, with no list.</param>
    private sealed record Trial(int[] List, int? Sought, int? Number = null);

    /// <summary>
    /// The numbers a prime test is called with: every one from 0 to 30, so 0, 1, 2, squares and primes are all among
    /// them.
    /// </summary>
    private const int HighestNumberTried = 30;

    private sealed record Result(Trial Trial, JsonElement? Returned, JsonElement? After, string? Error);

    /// <param name="runtime">What runs the program: the Python of a Python program, the Node of a JavaScript one, the
    /// javac of a Java one.</param>
    /// <param name="java">The java of a Java program, beside its javac; nothing for any other.</param>
    public static async Task<Outcome> TryAsync(IReadOnlyList<AlgorithmSeen> recognised, IrProgram program, string runtime, CancellationToken cancellationToken,
        string? java = null)
    {
        var seen = recognised.ToList();
        var findings = new List<Finding>();
        var tried = 0;

        for (var index = 0; index < seen.Count && tried < MostTried; index++)
        {
            var recognition = seen[index];
            if (Roles(recognition, program) is not { } roles) continue;
            tried++;

            var (said, broken) = await JudgedAsync(recognition, roles, program, runtime, java, ascending: true, cancellationToken);
            if (broken is not null && recognition.Algorithm == "a binary search" &&
                await JudgedAsync(recognition, roles, program, runtime, java, ascending: false, cancellationToken) is { Broken: null, Said: { } reversed })
                (said, broken) = (reversed, null);

            if (said is null) continue;
            seen[index] = recognition with { Tried = said };
            if (broken is not null) findings.Add(FindingFor(recognition, broken));
        }

        return new Outcome(seen, findings);
    }

    /// <summary>
    /// The part each of the function's parameters plays, in their order - the list, what a search looks for, the list's
    /// length, or none for one left to its default - or null when the function cannot be called that way: not a Python
    /// function at the top of its file, a JavaScript one at the top of a CommonJS file nor a static Java method over an
    /// int[], or a parameter that is none of those and has no default.
    /// </summary>
    private static List<(string Name, string? Role)>? Roles(AlgorithmSeen recognition, IrProgram program)
    {
        var language = Path.GetExtension(recognition.File).ToLowerInvariant() switch
        {
            ".py" => SourceLanguage.Python,
            ".js" or ".cjs" => SourceLanguage.JavaScript,
            ".java" => SourceLanguage.Java,
            _ => (SourceLanguage?)null,
        };
        if (language is null || language != program.Language || recognition.Function == AlgorithmRecognition.TopLevelCode ||
            recognition.List is null && !recognition.IsPrimeTest)
            return null;

        // A sort is judged by its list; a search for the largest or smallest by the item it gives back; any other search by
        // what it looks for and what it gives back when it finds it.
        if (recognition.IsExtreme ? recognition.Gives != SearchGives.Item
            : recognition.IsPrimeTest ? recognition.Sought is null || recognition.Gives != SearchGives.Truth
            : !recognition.IsSort && (recognition.Sought is null || recognition.Gives is null))
            return null;

        var function = language == SourceLanguage.Java ? JavaMethod(recognition, program) : TopLevelFunction(recognition, program);
        if (function is null || function.IsAsync || function.IsGenerator) return null;

        var roles = new List<(string Name, string? Role)>();
        foreach (var parameter in function.Parameters)
        {
            if (parameter.Kind is ParameterKind.Rest or ParameterKind.Keywords || !Identifier().IsMatch(parameter.Name)) return null;
            if (parameter.Name == recognition.List) roles.Add((parameter.Name, "list"));
            else if (parameter.Name == recognition.Sought) roles.Add((parameter.Name, recognition.IsPrimeTest ? "number" : "sought"));
            else if (LengthNames.Contains(parameter.Name)) roles.Add((parameter.Name, "length"));
            else if (parameter.Default is not null) roles.Add((parameter.Name, null));
            else return null;

            // A Java method is found by its parameters' classes: an int[] for the list, an int for the rest.
            var role = roles[^1].Role;
            if (language == SourceLanguage.Java && JavaClassOf(parameter.Type, role) is null) return null;
        }

        if (recognition.IsPrimeTest) return roles.Any(role => role.Role == "number") ? roles : null;
        return roles.Any(role => role.Role == "list") && (recognition.IsSort || recognition.IsExtreme || roles.Any(role => role.Role == "sought")) ? roles : null;
    }

    /// <summary>
    /// The function called on its lists, and what that showed: a sentence saying every call was right, or saying the
    /// first that was not - and that one again, to be a finding. Both null when a call did not finish, or gave back
    /// something it cannot be judged by.
    /// </summary>
    private static async Task<(string? Said, string? Broken)> JudgedAsync(AlgorithmSeen recognition, List<(string Name, string? Role)> roles, IrProgram program,
        string runtime, string? java, bool ascending, CancellationToken cancellationToken)
    {
        var trials = recognition.IsSort ? ToSort.Select(list => new Trial(list, null)).ToList()
            : recognition.IsExtreme ? ToFindTheExtremeOf.Select(list => new Trial(list, null)).ToList()
            : recognition.IsPrimeTest ? Enumerable.Range(0, HighestNumberTried + 1).Select(number => new Trial([], null, number)).ToList()
            : SearchTrials(recognition, ascending);
        var results = program.Language switch
        {
            SourceLanguage.Python => await RunPythonAsync(recognition, roles, trials, runtime, cancellationToken),
            SourceLanguage.JavaScript => await RunJavaScriptAsync(recognition, roles, trials, program, runtime, cancellationToken),
            SourceLanguage.Java when java is not null && JavaMethod(recognition, program) is { } method =>
                await RunJavaAsync(recognition, method, roles, trials, runtime, java, cancellationToken),
            _ => null,
        };
        if (results is null) return (null, null);

        return recognition.IsSort ? JudgeSort(recognition, results)
            : recognition.IsExtreme ? JudgeExtreme(recognition, results)
            : recognition.IsPrimeTest ? JudgePrimeTest(recognition, results)
            : JudgeSearch(recognition, results, trials, ascending);
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

    /// <summary>
    /// Whether each list's largest, or smallest, item came back - and the first list that did not, with what did.
    /// </summary>
    private static (string? Said, string? Broken) JudgeExtreme(AlgorithmSeen recognition, IReadOnlyList<Result> results)
    {
        var name = $"`{recognition.Function}`";
        var which = recognition.IsLargest ? "largest" : "smallest";
        foreach (var result in results)
        {
            var list = Show(result.Trial.List);
            var wanted = recognition.IsLargest ? result.Trial.List.Max() : result.Trial.List.Min();
            if (result.Error is { } error) return Broken($"Called with {list}, {name} stopped with {error}");
            if (result.Returned is not { ValueKind: JsonValueKind.Number } returned || !returned.TryGetInt64(out var given)) return (null, null);
            if (given != wanted) return Broken($"Called with {list}, {name} gave {given} - but the {which} item is {wanted}");
        }

        return ($"Called with {results.Count} lists of FixFinder's own - of one item, in order, in reverse order, all below zero, all above zero " +
                $"and with repeats - it gave the {which} item of each.", null);

        static (string?, string?) Broken(string what) => (what + ".", what);
    }

    /// <summary>
    /// Whether each number's answer was whether it is prime - and the first that was not, with why: 1 is not prime, 9
    /// is 3 times 3, 7 is prime.
    /// </summary>
    private static (string? Said, string? Broken) JudgePrimeTest(AlgorithmSeen recognition, IReadOnlyList<Result> results)
    {
        var name = $"`{recognition.Function}`";
        foreach (var result in results)
        {
            var number = result.Trial.Number!.Value;
            if (result.Error is { } error) return Broken($"Called with {number}, {name} stopped with {error}");
            if (result.Returned is not { ValueKind: JsonValueKind.True or JsonValueKind.False } returned) return (null, null);

            var smallestFactor = Enumerable.Range(2, Math.Max(0, number - 2)).FirstOrDefault(divisor => number % divisor == 0);
            var prime = number >= 2 && smallestFactor == 0;
            var saidPrime = returned.ValueKind == JsonValueKind.True;
            if (saidPrime == prime) continue;

            var gave = AsWritten(returned, recognition.File);
            var why = prime ? $"{number} is prime" : number < 2 ? $"{number} is not prime" : $"{number} is {smallestFactor} times {number / smallestFactor}";
            return Broken($"Called with {number}, {name} gave {gave} - but {why}");
        }

        return ($"Called with every number from 0 to {HighestNumberTried}, it said which were prime and which were not, every one right.", null);

        static (string?, string?) Broken(string what) => (what + ".", what);
    }

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

            var gave = AsWritten(returned, recognition.File);
            if (found && present < 0) return Broken($"{called} gave {gave} - but {sought} is not in the list");
            if (!found && present >= 0) return Broken($"{called} gave {gave} - but {sought} is there, at position {present}");
        }

        var lists = trials.Select(trial => trial.List).Distinct().Count();
        return ($"Called with {lists} lists{(ascending ? "" : " in reverse order")} of FixFinder's own and {results.Count} values, each in them or not, " +
                "it found every one that was there and none that was not.", null);

        static (string?, string?) Broken(string what) => (what + ".", what);
    }

    /// <summary>A recognition's first wrong call as a finding: certain, as it was seen.</summary>
    private static Finding FindingFor(AlgorithmSeen recognition, string broken)
    {
        var rule = recognition.IsSort ? "algorithm-sort-wrong" : recognition.IsExtreme ? "algorithm-extreme-wrong"
            : recognition.IsPrimeTest ? "algorithm-prime-wrong" : "algorithm-search-wrong";
        var guide = Guidebook.For(recognition.File, FindingKind.Logic, rule);
        return new Finding
        {
            Kind = FindingKind.Logic,
            Severity = Severity.Error,
            Confidence = Confidence.Certain,
            File = recognition.File,
            Line = recognition.Line,
            Title = recognition.IsSort ? $"`{recognition.Function}` does not sort every list"
                : recognition.IsExtreme ? $"`{recognition.Function}` does not give back the {(recognition.IsLargest ? "largest" : "smallest")} item of every list"
                : recognition.IsPrimeTest ? $"`{recognition.Function}` does not say rightly which numbers are prime"
                : $"`{recognition.Function}` does not find every value",
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
    private static async Task<IReadOnlyList<Result>?> RunPythonAsync(AlgorithmSeen recognition, List<(string Name, string? Role)> roles, IReadOnlyList<Trial> trials,
        string python, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-trial", Guid.NewGuid().ToString("N")[..12]);
        try
        {
            Directory.CreateDirectory(folder);
            var plan = Plan(roles, trials);
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

            return Results(ran, trials);
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

    /// <summary>
    /// The function called once on each trial, from the end of a copy of its file that keeps only the file's
    /// definitions, compiled by Node under the program's own name: what it gave back, the list after the call, or what
    /// it threw. Null when the file cannot be copied so - an ES module - or the run gave no answer for every trial.
    /// </summary>
    private static async Task<IReadOnlyList<Result>?> RunJavaScriptAsync(AlgorithmSeen recognition, List<(string Name, string? Role)> roles, IReadOnlyList<Trial> trials,
        IrProgram program, string node, CancellationToken cancellationToken)
    {
        var file = Path.GetFullPath(recognition.File);
        if (await JavaScriptConfirmation.DefinitionsOfAsync(file, program, cancellationToken) is not { } kept) return null;

        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-trial", Guid.NewGuid().ToString("N")[..12]);
        try
        {
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, CopyFile);
            await File.WriteAllTextAsync(copy, string.Join("\n", kept) + "\n" + JavaScriptTrials(recognition.Function, Plan(roles, trials)), new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, LoaderFile), JavaScriptConfirmation.Loader, new UTF8Encoding(false), cancellationToken);

            var ran = await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = node,
                Arguments = $"\"{Path.Combine(folder, LoaderFile)}\" \"{file}\" \"{copy}\"",
                WorkingDirectory = Path.GetDirectoryName(file)!,
                Timeout = JavaScriptTimeout,
                StandardInput = "",
            }, cancellationToken);

            return Results(ran, trials);
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

    /// <summary>A Python or JavaScript function declared at the top of its file, by the name it is called.</summary>
    private static IrFunction? TopLevelFunction(AlgorithmSeen recognition, IrProgram program) =>
        Identifier().IsMatch(recognition.Function)
            ? program.AllFunctions.FirstOrDefault(each => each.FullName == recognition.Function && each.Owner is null && each.EnclosedBy is null or IrFunction.ModuleBody &&
                                                          string.Equals(Path.GetFullPath(each.Span.File), Path.GetFullPath(recognition.File), StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>A static method of a Java class, by its class and name: Search.find.</summary>
    private static IrFunction? JavaMethod(AlgorithmSeen recognition, IrProgram program) =>
        program.AllFunctions.FirstOrDefault(each => each.FullName == recognition.Function && each is { IsStatic: true, Owner: not null } &&
                                                    string.Equals(Path.GetFullPath(each.Span.File), Path.GetFullPath(recognition.File), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The class a Java parameter is found by, for its part: int[], List&lt;Integer&gt; or ArrayList&lt;Integer&gt; for
    /// the list, int or Integer for what is looked for, int for a length.
    /// </summary>
    private static string? JavaClassOf(IrType type, string? role) => (role, type.Name) switch
    {
        ("list", "array") when type.Arguments is [{ Name: "int" }] => "int[].class",
        ("list", "List" or "ArrayList") when type.Arguments is [{ Name: "Integer" }] => $"java.util.{type.Name}.class",
        ("sought" or "length" or "number", "int") => "int.class",
        ("sought", "Integer") => "Integer.class",
        _ => null,
    };

    /// <summary>
    /// The method called once on each trial, by reflection, from a small class compiled beside a copy of its file: what
    /// it gave back, the array after the call, or what it threw. Null when the copy does not compile on its own, or the
    /// run gave no answer for every trial.
    /// </summary>
    private static async Task<IReadOnlyList<Result>?> RunJavaAsync(AlgorithmSeen recognition, IrFunction method, List<(string Name, string? Role)> roles,
        IReadOnlyList<Trial> trials, string javac, string java, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-trial", Guid.NewGuid().ToString("N")[..12]);
        try
        {
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, Path.GetFileName(recognition.File));
            File.Copy(recognition.File, copy);
            var source = await File.ReadAllTextAsync(copy, cancellationToken);
            var owner = (JavaPackage().Match(source) is { Success: true } package ? package.Groups["name"].Value + "." : "") + method.Owner!.Replace('.', '$');
            var trialClass = Path.Combine(folder, JavaTrialClass + ".java");
            await File.WriteAllTextAsync(trialClass, JavaTrials(owner, method, roles, trials), new UTF8Encoding(false), cancellationToken);

            var classes = Path.Combine(folder, "classes");
            await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = javac,
                Arguments = $"-encoding UTF-8 -nowarn -d \"{classes}\" \"{copy}\" \"{trialClass}\"",
                WorkingDirectory = folder,
                Timeout = CompileTimeout,
            }, cancellationToken);
            if (!File.Exists(Path.Combine(classes, JavaTrialClass + ".class"))) return null;

            var ran = await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = java,
                Arguments = $"-cp \"{classes}\" {JavaTrialClass}",
                WorkingDirectory = Path.GetDirectoryName(recognition.File) ?? folder,
                Timeout = RunTimeout,
                StandardInput = "",
            }, cancellationToken);

            return Results(ran, trials);
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

    /// <summary>
    /// The small class that calls a Java method on each trial: found by reflection by its parameters' classes and made
    /// callable when private; each call made in a thread given 2 seconds; what it gave back, the array after it, or
    /// what it threw written out a line each, as the Python and JavaScript runs write them.
    /// </summary>
    private static string JavaTrials(string owner, IrFunction method, List<(string Name, string? Role)> roles, IReadOnlyList<Trial> trials)
    {
        var classes = string.Join("", method.Parameters.Zip(roles, (parameter, role) => ", " + JavaClassOf(parameter.Type, role.Role)));
        var lists = string.Join(", ", trials.Select(trial => "{" + string.Join(", ", trial.List) + "}"));
        var sought = string.Join(", ", trials.Select(trial => trial.Sought ?? 0));
        var given = string.Join(", ", roles.Select(role => role.Role switch
        {
            "list" => "list",
            "sought" => "sought[index]",
            "number" => "numbers[index]",
            _ => "lists[index].length",
        }));
        var numbers = string.Join(", ", trials.Select(trial => trial.Number ?? 0));

        // An array is given a copy of the trial's; a list, a fresh ArrayList of the same numbers; a prime test, no list.
        var listType = method.Parameters.Zip(roles).FirstOrDefault(pair => pair.Second.Role == "list").First?.Type;
        var madeList = listType is null ? "null" : listType.Name == "array" ? "lists[index].clone()" : "boxed(lists[index])";
        return $$"""
            public class {{JavaTrialClass}} {
                static String quote(String text) {
                    StringBuilder written = new StringBuilder("\"");
                    for (char each : text.toCharArray()) {
                        if (each == '"' || each == '\\') written.append('\\');
                        if (each >= ' ') written.append(each);
                    }
                    return written.append('"').toString();
                }

                static java.util.ArrayList<Integer> boxed(int[] items) {
                    java.util.ArrayList<Integer> list = new java.util.ArrayList<Integer>();
                    for (int item : items) list.add(item);
                    return list;
                }

                static String plain(Object value) {
                    if (value == null) return "null";
                    if (value instanceof java.util.Collection) {
                        StringBuilder written = new StringBuilder("[");
                        boolean first = true;
                        for (Object item : (java.util.Collection<?>) value) {
                            written.append(first ? "" : ",").append(plain(item));
                            first = false;
                        }
                        return written.append("]").toString();
                    }
                    if (value instanceof Boolean || value instanceof Integer || value instanceof Long || value instanceof Short || value instanceof Byte) return value.toString();
                    if (value instanceof int[]) {
                        StringBuilder written = new StringBuilder("[");
                        int[] items = (int[]) value;
                        for (int index = 0; index < items.length; index++) written.append(index == 0 ? "" : ",").append(items[index]);
                        return written.append("]").toString();
                    }
                    if (value instanceof String) return quote((String) value);
                    return "{\"repr\":" + quote(String.valueOf(value)) + "}";
                }

                public static void main(String[] args) throws Exception {
                    Class<?> owner = Class.forName("{{owner}}");
                    final java.lang.reflect.Method method = owner.getDeclaredMethod("{{method.Name}}"{{classes}});
                    method.setAccessible(true);
                    int[][] lists = { {{lists}} };
                    int[] sought = { {{sought}} };
                    int[] numbers = { {{numbers}} };
                    for (int index = 0; index < lists.length; index++) {
                        final Object list = {{madeList}};
                        final Object[] given = new Object[] { {{given}} };
                        final String[] outcome = { null };
                        Thread call = new Thread(() -> {
                            try {
                                outcome[0] = "\"returned\":" + plain(method.invoke(null, given));
                            } catch (java.lang.reflect.InvocationTargetException thrown) {
                                Throwable cause = thrown.getCause();
                                String message = cause.getMessage();
                                outcome[0] = "\"error\":" + quote(cause.getClass().getSimpleName() + (message != null ? ": " + message : ""));
                            } catch (Throwable other) {
                                outcome[0] = "\"error\":" + quote(other.toString());
                            }
                        });
                        call.setDaemon(true);
                        call.start();
                        call.join(2000);
                        if (call.isAlive()) {
                            System.out.println("{{Said}}{\"trial\":" + index + ",\"unfinished\":true}");
                            System.out.flush();
                            System.exit(0);
                        }
                        System.out.println("{{Said}}{\"trial\":" + index + "," + outcome[0] + ",\"after\":" + plain(list) + "}");
                    }
                    System.out.flush();
                    System.exit(0);
                }
            }
            """;
    }

    /// <summary>
    /// What to call the function with: each parameter's part, by name and in order, and each trial's list and value.
    /// </summary>
    private static string Plan(List<(string Name, string? Role)> roles, IReadOnlyList<Trial> trials) => JsonSerializer.Serialize(new
    {
        roles = roles.Where(role => role.Role is not null).ToDictionary(role => role.Name, role => role.Role),
        parameters = roles.Select(role => role.Role),
        trials = trials.Select(trial => new { list = trial.List, sought = trial.Sought, number = trial.Number }),
    });

    /// <summary>Each trial's answer, read from the lines the run printed; null unless every trial answered.</summary>
    private static IReadOnlyList<Result>? Results(TargetRunResult ran, IReadOnlyList<Trial> trials)
    {
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

    /// <summary>
    /// A value as the program's language writes it: None, True and 'text' in Python, null, true and "text" in
    /// JavaScript and Java.
    /// </summary>
    private static string AsWritten(JsonElement value, string file)
    {
        var python = file.EndsWith(".py", StringComparison.OrdinalIgnoreCase);
        return value.ValueKind switch
        {
            JsonValueKind.Null => python ? "None" : "null",
            JsonValueKind.True => python ? "True" : "true",
            JsonValueKind.False => python ? "False" : "false",
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => python ? "'" + value.GetString() + "'" : JsonSerializer.Serialize(value.GetString()),
            JsonValueKind.Array => "[" + string.Join(", ", value.EnumerateArray().Select(item => AsWritten(item, file))) + "]",
            _ => value.TryGetProperty("repr", out var shown) ? shown.GetString() ?? "" : value.GetRawText(),
        };
    }

    /// <summary>
    /// The calls put at the end of a JavaScript copy, inside the file's own scope: each trial's list and value given to
    /// the function by its parameters' parts, and what it gave back, the list after it, or what it threw, written out
    /// one line each; then Node is ended.
    /// </summary>
    private static string JavaScriptTrials(string function, string plan) => $$"""
        ;(function () {
            const fixfinderPlan = {{plan}};
            const fixfinderPlain = (value, depth) =>
                value === undefined || value === null || (typeof value === "number" && !Number.isFinite(value)) ? null
                : typeof value === "number" || typeof value === "boolean" || typeof value === "string" ? value
                : Array.isArray(value) && depth < 3 ? value.map(item => fixfinderPlain(item, depth + 1))
                : { repr: String(value).slice(0, 80) };
            fixfinderPlan.trials.forEach((trial, index) => {
                const list = trial.list.slice();
                const given = fixfinderPlan.parameters.map(role =>
                    role === "list" ? list : role === "sought" ? trial.sought : role === "number" ? trial.number
                    : role === "length" ? trial.list.length : undefined);
                const outcome = { trial: index };
                try {
                    outcome.returned = fixfinderPlain({{function}}(...given), 0);
                } catch (failure) {
                    outcome.error = ((failure && failure.name) || "Error") + (failure && failure.message ? ": " + String(failure.message).slice(0, 200) : "");
                }
                outcome.after = fixfinderPlain(list, 0);
                require("fs").writeSync(1, "{{Said}}" + JSON.stringify(outcome) + "\n");
            });
            process.exit(0);
        })();
        """;

    [GeneratedRegex(@"^[A-Za-z_$][\w$]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"^\s*package\s+(?<name>[\w.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex JavaPackage();

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
        list_parameter = next((parameter for parameter, role in plan["roles"].items() if role == "list"), None)

        for index, trial in enumerate(plan["trials"]):
            arguments = {}
            for parameter, role in plan["roles"].items():
                arguments[parameter] = (list(trial["list"]) if role == "list" else trial["sought"] if role == "sought"
                                        else trial["number"] if role == "number" else len(trial["list"]))
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
            outcome["after"] = plain(arguments[list_parameter]) if list_parameter else None
            say(outcome)

        os._exit(0)
        """;
}
