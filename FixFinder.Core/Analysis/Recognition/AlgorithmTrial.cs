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
/// them, and values not in them, and must find every one that is there and none that is not; a prime test is called
/// with every number from 0 to 30, and a leap-year test with ten years, and each must say rightly which numbers are
/// prime, or which years are leap years; and a factorial is called with every number from 1 to 10, and 0, and must give
/// back each one's factorial; a reversal is called with lists of odd and even lengths, and must give each back in the
/// opposite order; and Euclid's greatest common divisor is called with pairs of numbers, 0 among them, and must give
/// back the greatest number dividing both of each; and the Fibonacci numbers are called with every number from 1 to 10,
/// and must start as they do and give back each as the two before it added; and a merge is called with pairs of lists in
/// order, and must give back every item of both, in order; and a test of order is called with lists in order and out of
/// order at the start, the middle and the end, and must say which are in order by its own comparison; and a palindrome
/// test is called with words of one letter up to seven, and must say which read the same backwards. What it does is said
/// beside the recognition, and the first list it got wrong becomes a finding, with what it was called with and what it
/// gave.
/// </summary>
/// <remarks>
/// For a Python function at the top of its file, or a JavaScript one at the top of a CommonJS file, whose parameters
/// are the list, what a search looks for, and a length - n, count, size, length - or have defaults; it is called from a
/// copy of the file's definitions alone, as the confirmations call one, so the program's own top-level code never runs.
/// For a static Java method whose list is an int[], a List&lt;Integer&gt; or an ArrayList&lt;Integer&gt;, what a search
/// looks for an int or an Integer, and a length an int - or, for a palindrome test, whose word is a String - called by
/// reflection from a small class compiled beside a copy of its file, so the program's own main never runs. A sort that gives back nothing is read from the list it was
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

    /// <summary>
    /// Lists to reverse, shortest first: empty, of one, of two either way, of three, of four, of five, with repeats and
    /// below zero - odd and even lengths, as a reversal in place stops at the middle of one and between two of the other.
    /// </summary>
    private static readonly int[][] ToReverse = [[], [1], [1, 2], [2, 1], [1, 2, 3], [3, 1, 2], [1, 2, 3, 4], [5, 2, 9, 1, 7], [4, 4, 1, 1], [7, -3, 0, 12]];

    /// <summary>
    /// Pairs of lists in order to merge, shortest first: both empty, one empty either way round, one item each either way,
    /// one running out before the other either way, the same item in both, and below zero.
    /// </summary>
    private static readonly (int[] First, int[] Second)[] ToMerge =
    [
        ([], []), ([1], []), ([], [2]), ([1], [2]), ([2], [1]), ([1, 3], [2]), ([2], [1, 3]), ([1, 2, 3], [4, 5]), ([4, 5], [1, 2, 3]),
        ([1, 4, 4], [2, 4, 6]), ([-3, 0], [-5, 7]),
    ];

    /// <summary>
    /// Lists to ask whether they are in order, shortest first: empty, of one, of two either way and the same twice, of three
    /// either way, and out of order at the start, in the middle and at the very end - the last pair, which a loop that stops
    /// one short never looks at.
    /// </summary>
    private static readonly int[][] ToCheckOrder = [[], [1], [1, 2], [2, 1], [1, 1], [1, 2, 3], [3, 2, 1], [2, 1, 3], [1, 3, 2], [1, 2, 4, 3], [4, 1, 2, 3], [-3, 0, 5]];

    /// <summary>
    /// Words to ask whether they are palindromes, shortest first: of one letter, of two the same and two that differ, then
    /// palindromes of odd and even lengths beside words that differ at their ends, or only in towards the middle - the
    /// pairs a loop that stops one short, or says yes at the first pair that matches, never gets to. The empty word is
    /// left out: whether it counts as one is a choice a course makes.
    /// </summary>
    private static readonly string[] ToTestAsPalindromes = ["a", "aa", "ab", "aba", "abb", "abba", "abca", "abcba", "abcda", "abccba", "abcdba", "racecar"];

    /// <summary>A list out of order, which a linear search needs no order to search.</summary>
    private static readonly int[] OutOfOrder = [5, 2, 9, 1];

    /// <summary>The names a parameter that is given the list's length goes by.</summary>
    private static readonly HashSet<string> LengthNames = new(["n", "count", "size", "length", "len", "num"], StringComparer.Ordinal);

    public sealed record Outcome(IReadOnlyList<AlgorithmSeen> Seen, IReadOnlyList<Finding> Findings);

    /// <param name="Number">What a prime test is given: a number on its own, with no list.</param>
    /// <param name="Other">What a merge is given beside the list: a second list, to merge with the first.</param>
    /// <param name="Text">What a palindrome test is given: a word, with no list.</param>
    /// <param name="KeyNumber">The key one of a pair that undo each other is given beside its word, when it is a number - a cipher's shift.</param>
    /// <param name="KeyText">The key one of a pair that undo each other is given beside its word, when it is a word.</param>
    private sealed record Trial(int[] List, int? Sought, int? Number = null, int[]? Other = null, string? Text = null, int? KeyNumber = null, string? KeyText = null);

    /// <summary>
    /// The numbers a prime test is called with: every one from 0 to 30, so 0, 1, 2, squares and primes are all among
    /// them.
    /// </summary>
    private const int HighestNumberTried = 30;

    /// <summary>
    /// The years a leap-year test is called with, those most often got wrong soonest: one 4 divides and one it does not,
    /// 2000 that 400 divides, 1900 and 2100 that 100 divides and 400 does not, then more of each kind.
    /// </summary>
    private static readonly int[] YearsTried = [2024, 2023, 2000, 1900, 2100, 2400, 1996, 2019, 1600, 1800];

    /// <summary>
    /// The numbers a factorial is called with: 1 to 10, whose factorials all fit in a 32-bit int, then 0, whose factorial
    /// is 1 - last, so a factorial written only for numbers from 1 is shown first going wrong on one of those, if it does.
    /// </summary>
    private static readonly int[] FactorialsTried = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 0];

    /// <summary>
    /// The Fibonacci numbers are called with every number from 1 up to this. Courses start them in different places -
    /// fib(1) is 0 in some, 1 in others - so no list of answers is held for them, only what every start shares: each is
    /// the two before it added.
    /// </summary>
    private const int HighestFibonacciTried = 10;

    /// <summary>
    /// The pairs a greatest common divisor is called with: each way round, sharing a divisor or none, the same number
    /// twice, one dividing the other - then a 0 in each place last, as every number divides 0, so the answer is the other.
    /// </summary>
    private static readonly (int First, int Second)[] PairsTried = [(12, 18), (18, 12), (7, 5), (21, 14), (9, 9), (1, 8), (100, 75), (17, 34), (5, 0), (0, 5)];

    /// <param name="Between">For a pair that undo each other, what the first gave back, which the second was given.</param>
    /// <param name="Stage">For a pair that undo each other, which of the two the call had reached: doing or undoing.</param>
    private sealed record Result(Trial Trial, JsonElement? Returned, JsonElement? After, string? Error, JsonElement? Between = null, string? Stage = null);

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
            if (recognition.IsUndoingPair)
            {
                if (PairToTurnBack(recognition, program) is not { } pair) continue;
                tried++;

                var (pairSaid, pairBroken) = await TurnedBackAsync(recognition, pair, program, runtime, java, cancellationToken);
                if (pairSaid is null) continue;
                seen[index] = recognition with { Tried = pairSaid };
                if (pairBroken is not null) findings.Add(FindingFor(recognition, pairBroken));
                continue;
            }

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
            recognition.List is null && !recognition.IsGivenANumber && !recognition.IsGreatestCommonDivisor)
            return null;

        // A sort is judged by its list; a search for the largest or smallest by the item it gives back; any other search by
        // what it looks for and what it gives back when it finds it.
        if (recognition.IsExtreme ? recognition.Gives != SearchGives.Item
            : recognition.IsNumberTest ? recognition.Sought is null || recognition.Gives != SearchGives.Truth
            : recognition.IsFactorial || recognition.IsFibonacci ? recognition.Sought is null
            : recognition.IsReversal ? false
            : recognition.IsGreatestCommonDivisor ? recognition.Sought is null || recognition.FirstNumber is null
            : recognition.IsMerge ? recognition.SecondList is null
            : recognition.IsInOrderTest ? recognition.OutOfOrder is null || recognition.Gives != SearchGives.Truth
            : recognition.IsPalindromeTest ? recognition.Gives != SearchGives.Truth
            : !recognition.IsSort && (recognition.Sought is null || recognition.Gives is null))
            return null;

        var function = language == SourceLanguage.Java ? JavaMethod(recognition, program) : TopLevelFunction(recognition, program);
        if (function is null || function.IsAsync || function.IsGenerator) return null;

        var roles = new List<(string Name, string? Role)>();
        foreach (var parameter in function.Parameters)
        {
            if (parameter.Kind is ParameterKind.Rest or ParameterKind.Keywords || !Identifier().IsMatch(parameter.Name)) return null;
            if (parameter.Name == recognition.List) roles.Add((parameter.Name, recognition.IsPalindromeTest ? "text" : "list"));
            else if (parameter.Name == recognition.Sought) roles.Add((parameter.Name, recognition.IsGivenANumber ? "number" : "sought"));
            else if (parameter.Name == recognition.FirstNumber) roles.Add((parameter.Name, "number"));
            else if (parameter.Name == recognition.SecondList) roles.Add((parameter.Name, "other"));
            else if (LengthNames.Contains(parameter.Name)) roles.Add((parameter.Name, "length"));
            else if (parameter.Default is not null) roles.Add((parameter.Name, null));
            else return null;

            // A Java method is found by its parameters' classes: an int[] for the list, an int for the rest.
            var role = roles[^1].Role;
            if (language == SourceLanguage.Java && JavaClassOf(parameter.Type, role) is null) return null;
        }

        if (recognition.IsGivenANumber) return roles.Any(role => role.Role == "number") ? roles : null;
        if (recognition.IsGreatestCommonDivisor) return roles.Any(role => role.Role == "number") && roles.Any(role => role.Role == "sought") ? roles : null;
        if (recognition.IsMerge) return roles.Any(role => role.Role == "list") && roles.Any(role => role.Role == "other") ? roles : null;

        // A length is given as the list's; a word has none of its own to give.
        if (recognition.IsPalindromeTest) return roles.Any(role => role.Role == "text") && roles.All(role => role.Role != "length") ? roles : null;
        return roles.Any(role => role.Role == "list") && (recognition.IsSort || recognition.IsExtreme || recognition.IsReversal || recognition.IsInOrderTest || roles.Any(role => role.Role == "sought")) ? roles : null;
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
            : recognition.IsReversal ? ToReverse.Select(list => new Trial(list, null)).ToList()
            : recognition.IsMerge ? ToMerge.Select(pair => new Trial(pair.First, null, Other: pair.Second)).ToList()
            : recognition.IsInOrderTest ? ToCheckOrder.Select(list => new Trial(list, null)).ToList()
            : recognition.IsPalindromeTest ? ToTestAsPalindromes.Select(word => new Trial([], null, Text: word)).ToList()
            : recognition.IsExtreme ? ToFindTheExtremeOf.Select(list => new Trial(list, null)).ToList()
            : recognition.IsPrimeTest ? Enumerable.Range(0, HighestNumberTried + 1).Select(number => new Trial([], null, number)).ToList()
            : recognition.IsLeapYearTest ? YearsTried.Select(year => new Trial([], null, year)).ToList()
            : recognition.IsFactorial ? FactorialsTried.Select(number => new Trial([], null, number)).ToList()
            : recognition.IsFibonacci ? Enumerable.Range(1, HighestFibonacciTried).Select(number => new Trial([], null, number)).ToList()
            : recognition.IsGreatestCommonDivisor ? PairsTried.Select(pair => new Trial([], pair.Second, pair.First)).ToList()
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
            : recognition.IsReversal ? JudgeReversal(recognition, results)
            : recognition.IsMerge ? JudgeMerge(recognition, results)
            : recognition.IsInOrderTest ? JudgeInOrderTest(recognition, results)
            : recognition.IsPalindromeTest ? JudgePalindromeTest(recognition, results)
            : recognition.IsExtreme ? JudgeExtreme(recognition, results)
            : recognition.IsPrimeTest ? JudgePrimeTest(recognition, results)
            : recognition.IsLeapYearTest ? JudgeLeapYearTest(recognition, results)
            : recognition.IsFactorial ? JudgeFactorial(recognition, results)
            : recognition.IsFibonacci ? JudgeFibonacci(recognition, results)
            : recognition.IsGreatestCommonDivisor ? JudgeGreatestCommonDivisor(recognition, results)
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
    /// Whether each list's answer was whether it is in order by the function's own comparison - no item followed by one the
    /// comparison calls out of order - and the first that was not, with the pair that shows it.
    /// </summary>
    private static (string? Said, string? Broken) JudgeInOrderTest(AlgorithmSeen recognition, IReadOnlyList<Result> results)
    {
        var name = $"`{recognition.Function}`";
        var outOfOrder = recognition.OutOfOrder!;
        bool OutOfOrder(int first, int second) => outOfOrder switch
        {
            ">" => first > second, ">=" => first >= second, "<" => first < second, "<=" => first <= second, _ => false,
        };

        foreach (var result in results)
        {
            var list = result.Trial.List;
            if (result.Error is { } error) return Broken($"Called with {Show(list)}, {name} stopped with {error}");
            if (result.Returned is not { ValueKind: JsonValueKind.True or JsonValueKind.False } returned) return (null, null);

            var firstOutOfOrder = Enumerable.Range(0, Math.Max(0, list.Length - 1)).Select(index => (int?)index).FirstOrDefault(index => OutOfOrder(list[index!.Value], list[index.Value + 1]));
            var inOrder = firstOutOfOrder is null;
            var saidInOrder = returned.ValueKind == JsonValueKind.True;
            if (saidInOrder == inOrder) continue;

            var gave = AsWritten(returned, recognition.File);
            return Broken(inOrder
                ? $"Called with {Show(list)}, {name} gave {gave} - but no item in it is followed by one its own test calls out of order"
                : $"Called with {Show(list)}, {name} gave {gave} - but in it {list[firstOutOfOrder!.Value]} is followed by {list[firstOutOfOrder.Value + 1]}, " +
                  $"and {list[firstOutOfOrder.Value]} {outOfOrder} {list[firstOutOfOrder.Value + 1]} is what its own test calls out of order");
        }

        return ($"Called with {results.Count} lists of FixFinder's own - empty, of one item, in order, and out of order at the start, in the middle and " +
                "at the very end - it said which were in order by its own test, every one right.", null);

        static (string?, string?) Broken(string what) => (what + ".", what);
    }

    /// <summary>
    /// Whether each word's answer was whether it reads the same backwards - and the first that was not, with the word
    /// backwards. A call stopped by a position past the end of the word is a finding; one stopped any other way - code
    /// written for a list, given a word it cannot change - is not judged, nor is an answer that is not a yes or a no.
    /// </summary>
    private static (string? Said, string? Broken) JudgePalindromeTest(AlgorithmSeen recognition, IReadOnlyList<Result> results)
    {
        var name = $"`{recognition.Function}`";
        string Written(string word) => AsWritten(JsonSerializer.SerializeToElement(word), recognition.File);

        foreach (var result in results)
        {
            var word = result.Trial.Text!;
            if (result.Error is { } error)
            {
                if (!PastTheEnd().IsMatch(error)) return (null, null);
                return Broken($"Called with {Written(word)}, {name} stopped with {error}");
            }

            if (result.Returned is not { ValueKind: JsonValueKind.True or JsonValueKind.False } returned) return (null, null);

            var backwards = new string(word.Reverse().ToArray());
            var palindrome = backwards == word;
            if ((returned.ValueKind == JsonValueKind.True) == palindrome) continue;

            var gave = AsWritten(returned, recognition.File);
            return Broken(palindrome
                ? $"Called with {Written(word)}, {name} gave {gave} - but {Written(word)} backwards is {Written(backwards)}, the same, so it is a palindrome"
                : $"Called with {Written(word)}, {name} gave {gave} - but {Written(word)} backwards is {Written(backwards)}, so it is not a palindrome");
        }

        return ($"Called with {results.Count} words of FixFinder's own - of one letter up to seven, palindromes of odd and even lengths, and words " +
                "that differ at their ends or only in towards the middle - it said which were palindromes, every one right.", null);

        static (string?, string?) Broken(string what) => (what + ".", what);
    }

    /// <summary>What a run says of a position past the end of a word: Python's IndexError, Java's StringIndexOutOfBoundsException.</summary>
    [GeneratedRegex(@"^(?:IndexError|(?:String|Array)?IndexOutOfBoundsException)\b")]
    private static partial Regex PastTheEnd();

    /// <summary>
    /// Whether each pair came back as one list holding every item of both, in order - and the first that did not, with
    /// what came back, what of the two lists was left out of it, and what it should be.
    /// </summary>
    private static (string? Said, string? Broken) JudgeMerge(AlgorithmSeen recognition, IReadOnlyList<Result> results)
    {
        var name = $"`{recognition.Function}`";
        foreach (var result in results)
        {
            var (first, second) = (result.Trial.List, result.Trial.Other!);
            var pair = $"{Show(first)} and {Show(second)}";
            if (result.Error is { } error) return Broken($"Called with {pair}, {name} stopped with {error}");
            if (Numbers(result.Returned) is not { } merged) return (null, null);

            var expected = Longs(first.Concat(second)).Order().ToList();
            if (merged.SequenceEqual(expected)) continue;

            // What of the two lists is not in what came back, counting each item as often as it is there.
            var missing = new List<long>(expected);
            foreach (var item in merged) missing.Remove(item);
            var why = missing.Count > 0 ? $" - {Show(missing)} {(missing.Count == 1 ? "is" : "are")} left out" : "";
            return Broken($"Called with {pair}, {name} gave back {Show(merged)}{why}; merged in order, it is {Show(expected)}");
        }

        return ($"Called with {results.Count} pairs of lists in order - empty, of one item, one running out before the other either way round, " +
                "with the same item in both and below zero - it gave back every item of both, in order.", null);

        static (string?, string?) Broken(string what) => (what + ".", what);
    }

    /// <summary>
    /// Whether each list came back in the opposite order - given back, or the list itself, reversed in place - and the
    /// first that did not, with what came back and what it should be.
    /// </summary>
    private static (string? Said, string? Broken) JudgeReversal(AlgorithmSeen recognition, IReadOnlyList<Result> results)
    {
        var name = $"`{recognition.Function}`";
        foreach (var result in results)
        {
            var list = Show(result.Trial.List);
            if (result.Error is { } error) return Broken($"Called with {list}, {name} stopped with {error}");

            var givenBack = Numbers(result.Returned);
            if ((givenBack ?? Numbers(result.After)) is not { } came) return (null, null);
            var reversed = Longs(result.Trial.List.Reverse());
            if (came.SequenceEqual(reversed)) continue;

            var what = givenBack is not null ? $"gave back {Show(came)}" : $"left the list it was given as {Show(came)}";
            return Broken($"Called with {list}, {name} {what} - reversed, it is {Show(reversed)}");
        }

        return ($"Called with {results.Count} lists of FixFinder's own - empty, of one item, of odd and even lengths, with repeats and below zero - " +
                "it gave each back in the opposite order.", null);

        static (string?, string?) Broken(string what) => (what + ".", what);
    }

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

    /// <summary>
    /// Whether each year's answer was whether it is a leap year - and the first that was not, with why: 4 divides 2024
    /// and 100 does not, 100 divides 1900 and 400 does not.
    /// </summary>
    private static (string? Said, string? Broken) JudgeLeapYearTest(AlgorithmSeen recognition, IReadOnlyList<Result> results)
    {
        var name = $"`{recognition.Function}`";
        foreach (var result in results)
        {
            var year = result.Trial.Number!.Value;
            if (result.Error is { } error) return Broken($"Called with {year}, {name} stopped with {error}");
            if (result.Returned is not { ValueKind: JsonValueKind.True or JsonValueKind.False } returned) return (null, null);

            var leap = year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
            var saidLeap = returned.ValueKind == JsonValueKind.True;
            if (saidLeap == leap) continue;

            var gave = AsWritten(returned, recognition.File);
            var why = year % 400 == 0 ? $"400 divides {year}, so it is a leap year"
                : year % 100 == 0 ? $"100 divides {year} and 400 does not, so it is not a leap year"
                : year % 4 == 0 ? $"4 divides {year} and 100 does not, so it is a leap year"
                : $"4 does not divide {year}, so it is not a leap year";
            return Broken($"Called with {year}, {name} gave {gave} - but {why}");
        }

        var years = $"{string.Join(", ", YearsTried.SkipLast(1))} and {YearsTried[^1]}";
        return ($"Called with the years {years}, it said which were leap years and which were not, every one right.", null);

        static (string?, string?) Broken(string what) => (what + ".", what);
    }

    /// <summary>
    /// Whether each number's answer was its factorial - and the first that was not, with what it should be: 5 factorial
    /// is 1 * 2 * 3 * 4 * 5, which is 120; 0 factorial is 1. A call that stopped is as wrong as a wrong answer.
    /// </summary>
    private static (string? Said, string? Broken) JudgeFactorial(AlgorithmSeen recognition, IReadOnlyList<Result> results)
    {
        var name = $"`{recognition.Function}`";
        foreach (var result in results)
        {
            var number = result.Trial.Number!.Value;
            var factorial = Enumerable.Range(1, number).Aggregate(1L, (product, factor) => product * factor);
            var why = number switch
            {
                0 => "0 factorial is 1, the product of no numbers at all",
                1 => "1 factorial is 1",
                _ => $"{number} factorial is {string.Join(" * ", Enumerable.Range(1, number))}, which is {factorial}",
            };
            if (result.Error is { } error) return Broken($"Called with {number}, {name} stopped with {error} - but {why}");
            if (result.Returned is not { ValueKind: JsonValueKind.Number } returned || !returned.TryGetInt64(out var given)) return (null, null);
            if (given == factorial) continue;

            return Broken($"Called with {number}, {name} gave {given} - but {why}");
        }

        return ("Called with every number from 1 to 10, and 0, it gave back each one's factorial, every one right.", null);

        static (string?, string?) Broken(string what) => (what + ".", what);
    }

    /// <summary>
    /// Whether each pair's answer was the greatest number dividing both - and the first that was not, with what it should
    /// be: the greatest dividing both 12 and 18 is 6; every number divides 0, so for 5 and 0 it is 5.
    /// </summary>
    private static (string? Said, string? Broken) JudgeGreatestCommonDivisor(AlgorithmSeen recognition, IReadOnlyList<Result> results)
    {
        var name = $"`{recognition.Function}`";
        foreach (var result in results)
        {
            var (first, second) = (result.Trial.Number!.Value, result.Trial.Sought!.Value);
            var greatest = GreatestDividingBoth(first, second);
            var why = first == 0 || second == 0
                ? $"every number divides 0, so the greatest dividing both {first} and {second} is {greatest}"
                : $"the greatest number dividing both {first} and {second} is {greatest}";
            if (result.Error is { } error) return Broken($"Called with {first} and {second}, {name} stopped with {error} - but {why}");
            if (result.Returned is not { ValueKind: JsonValueKind.Number } returned || !returned.TryGetInt64(out var given)) return (null, null);
            if (given == greatest) continue;

            return Broken($"Called with {first} and {second}, {name} gave {given} - but {why}");
        }

        return ($"Called with {results.Count} pairs of numbers - each way round, with a divisor in common and with none, the same number twice, " +
                "and with 0 - it gave back the greatest number dividing both of each, every one right.", null);

        static (string?, string?) Broken(string what) => (what + ".", what);

        static long GreatestDividingBoth(long one, long other)
        {
            while (other != 0) (one, other) = (other, one % other);
            return one;
        }
    }

    /// <summary>
    /// Whether the answers for 1 to 10 are Fibonacci numbers, whichever place they are started in: the first two one of
    /// the usual starts - 0 and 1, 1 and 1, or 1 and 2 - and each after them the two before it added. The first answer
    /// that is not is shown beside the two before it, as the function itself gave them.
    /// </summary>
    private static (string? Said, string? Broken) JudgeFibonacci(AlgorithmSeen recognition, IReadOnlyList<Result> results)
    {
        var name = $"`{recognition.Function}`";
        var answers = new List<long>();
        foreach (var result in results)
        {
            if (result.Error is { } error) return Broken($"Called with {result.Trial.Number}, {name} stopped with {error}");
            if (result.Returned is not { ValueKind: JsonValueKind.Number } returned || !returned.TryGetInt64(out var answer)) return (null, null);
            answers.Add(answer);
        }

        var (first, second) = (answers[0], answers[1]);
        if ((first, second) is not ((0, 1) or (1, 1) or (1, 2)))
            return Broken($"Called with 1 and with 2, {name} gave {first} and {second} - but the Fibonacci numbers start 0 and 1, 1 and 1, or 1 and 2");

        for (var index = 2; index < answers.Count; index++)
        {
            var (twoBefore, oneBefore) = (answers[index - 2], answers[index - 1]);
            if (answers[index] == twoBefore + oneBefore) continue;

            var number = results[index].Trial.Number!.Value;
            return Broken($"Called with {number}, {name} gave {answers[index]} - but it gave {twoBefore} for {number - 2} and {oneBefore} for {number - 1}, " +
                          $"and each Fibonacci number is the two before it added, so it should be {twoBefore + oneBefore}");
        }

        return ($"Called with every number from 1 to {HighestFibonacciTried}, it gave back {first} and {second} to start, and each after them as the two before it added.", null);

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
        var rule = recognition.IsSort ? "algorithm-sort-wrong" : recognition.IsReversal ? "algorithm-reversal-wrong" : recognition.IsExtreme ? "algorithm-extreme-wrong"
            : recognition.IsPrimeTest ? "algorithm-prime-wrong" : recognition.IsLeapYearTest ? "algorithm-leap-year-wrong"
            : recognition.IsFactorial ? "algorithm-factorial-wrong"
            : recognition.IsGreatestCommonDivisor ? "algorithm-gcd-wrong"
            : recognition.IsFibonacci ? "algorithm-fibonacci-wrong"
            : recognition.IsMerge ? "algorithm-merge-wrong"
            : recognition.IsInOrderTest ? "algorithm-in-order-wrong"
            : recognition.IsPalindromeTest ? "algorithm-palindrome-wrong"
            : recognition.IsUndoingPair ? "algorithm-round-trip-wrong" : "algorithm-search-wrong";
        var guide = Guidebook.For(recognition.File, FindingKind.Logic, rule);
        return new Finding
        {
            Kind = FindingKind.Logic,
            Severity = Severity.Error,
            Confidence = Confidence.Certain,
            File = recognition.File,
            Line = recognition.Line,
            Title = recognition.IsSort ? $"`{recognition.Function}` does not sort every list"
                : recognition.IsReversal ? $"`{recognition.Function}` does not reverse every list"
                : recognition.IsExtreme ? $"`{recognition.Function}` does not give back the {(recognition.IsLargest ? "largest" : "smallest")} item of every list"
                : recognition.IsPrimeTest ? $"`{recognition.Function}` does not say rightly which numbers are prime"
                : recognition.IsLeapYearTest ? $"`{recognition.Function}` does not say rightly which years are leap years"
                : recognition.IsFactorial ? $"`{recognition.Function}` does not work out every factorial rightly"
                : recognition.IsGreatestCommonDivisor ? $"`{recognition.Function}` does not give back the greatest common divisor of every pair"
                : recognition.IsFibonacci ? $"`{recognition.Function}` does not give back the Fibonacci numbers"
                : recognition.IsMerge ? $"`{recognition.Function}` does not merge every pair of lists"
                : recognition.IsInOrderTest ? $"`{recognition.Function}` does not say rightly which lists are in order"
                : recognition.IsPalindromeTest ? $"`{recognition.Function}` does not say rightly which words are palindromes"
                : recognition.IsUndoingPair ? $"`{recognition.Function}` and `{recognition.UndoneBy}` do not undo each other"
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
    private static Task<IReadOnlyList<Result>?> RunPythonAsync(AlgorithmSeen recognition, List<(string Name, string? Role)> roles, IReadOnlyList<Trial> trials,
        string python, CancellationToken cancellationToken) =>
        RunPythonScriptAsync(recognition.File, Script, [recognition.Function], Plan(roles, trials), trials, python, cancellationToken);

    /// <summary>
    /// A Python trial script run on a file, given the names of the functions it calls and the plan of what to call them
    /// with, and what it printed for each trial.
    /// </summary>
    private static async Task<IReadOnlyList<Result>?> RunPythonScriptAsync(string file, string script, IReadOnlyList<string> functionNames, string plan,
        IReadOnlyList<Trial> trials, string python, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-trial", Guid.NewGuid().ToString("N")[..12]);
        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, PlanFile), plan, new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, TrialFile), script, new UTF8Encoding(false), cancellationToken);

            var ran = await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = python,
                Arguments = $"-X utf8 \"{Path.Combine(folder, TrialFile)}\" \"{file}\" {string.Join(" ", functionNames)} \"{Path.Combine(folder, PlanFile)}\"",
                WorkingDirectory = Path.GetDirectoryName(file)!,
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
    private static Task<IReadOnlyList<Result>?> RunJavaScriptAsync(AlgorithmSeen recognition, List<(string Name, string? Role)> roles, IReadOnlyList<Trial> trials,
        IrProgram program, string node, CancellationToken cancellationToken) =>
        RunJavaScriptCopyAsync(recognition.File, JavaScriptTrials(recognition.Function, Plan(roles, trials)), trials, program, node, cancellationToken);

    /// <summary>
    /// A copy of a JavaScript file's definitions alone with the trial's calls at its end, compiled by Node under the
    /// program's own name, and what it printed for each trial; null for a file that cannot be copied so - an ES module.
    /// </summary>
    private static async Task<IReadOnlyList<Result>?> RunJavaScriptCopyAsync(string chosenFile, string calls, IReadOnlyList<Trial> trials,
        IrProgram program, string node, CancellationToken cancellationToken) =>
        await RanJavaScriptCopyAsync(chosenFile, calls, program, node, cancellationToken) is { } ran ? Results(ran, trials) : null;

    /// <summary>
    /// A copy of a JavaScript file's definitions alone with <paramref name="calls"/> at its end, compiled by Node under the
    /// program's own name, and the run; null for a file that cannot be copied so - an ES module - or a copy not made.
    /// </summary>
    private static async Task<TargetRunResult?> RanJavaScriptCopyAsync(string chosenFile, string calls, IrProgram program, string node,
        CancellationToken cancellationToken)
    {
        var file = Path.GetFullPath(chosenFile);
        if (await JavaScriptConfirmation.DefinitionsOfAsync(file, program, cancellationToken) is not { } kept) return null;

        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-trial", Guid.NewGuid().ToString("N")[..12]);
        try
        {
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, CopyFile);
            await File.WriteAllTextAsync(copy, string.Join("\n", kept) + "\n" + calls, new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, LoaderFile), JavaScriptConfirmation.Loader, new UTF8Encoding(false), cancellationToken);

            return await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = node,
                Arguments = $"\"{Path.Combine(folder, LoaderFile)}\" \"{file}\" \"{copy}\"",
                WorkingDirectory = Path.GetDirectoryName(file)!,
                Timeout = JavaScriptTimeout,
                StandardInput = "",
            }, cancellationToken);
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

    private static IrFunction? TopLevelFunction(AlgorithmSeen recognition, IrProgram program) => TopLevelFunction(recognition.Function, recognition.File, program);

    private static IrFunction? JavaMethod(AlgorithmSeen recognition, IrProgram program) => JavaMethod(recognition.Function, recognition.File, program);

    /// <summary>A Python or JavaScript function declared at the top of its file, by the name it is called.</summary>
    private static IrFunction? TopLevelFunction(string name, string file, IrProgram program) =>
        Identifier().IsMatch(name)
            ? program.AllFunctions.FirstOrDefault(each => each.FullName == name && each.Owner is null && each.EnclosedBy is null or IrFunction.ModuleBody &&
                                                          string.Equals(Path.GetFullPath(each.Span.File), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>A static method of a Java class, by its class and name: Search.find.</summary>
    private static IrFunction? JavaMethod(string name, string file, IrProgram program) =>
        program.AllFunctions.FirstOrDefault(each => each.FullName == name && each is { IsStatic: true, Owner: not null } &&
                                                    string.Equals(Path.GetFullPath(each.Span.File), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The class a Java parameter is found by, for its part: int[], List&lt;Integer&gt; or ArrayList&lt;Integer&gt; for
    /// the list, int or Integer for what is looked for, int for a length.
    /// </summary>
    private static string? JavaClassOf(IrType type, string? role) => (role, type.Name) switch
    {
        ("list" or "other", "array") when type.Arguments is [{ Name: "int" }] => "int[].class",
        ("list" or "other", "List" or "ArrayList") when type.Arguments is [{ Name: "Integer" }] => $"java.util.{type.Name}.class",
        ("sought" or "length" or "number", "int") => "int.class",
        ("sought", "Integer") => "Integer.class",
        ("text", "String") => "String.class",
        _ => null,
    };

    /// <summary>
    /// The method called once on each trial, by reflection, from a small class compiled beside a copy of its file: what
    /// it gave back, the array after the call, or what it threw. Null when the copy does not compile on its own, or the
    /// run gave no answer for every trial.
    /// </summary>
    private static Task<IReadOnlyList<Result>?> RunJavaAsync(AlgorithmSeen recognition, IrFunction method, List<(string Name, string? Role)> roles,
        IReadOnlyList<Trial> trials, string javac, string java, CancellationToken cancellationToken) =>
        RunJavaTrialClassAsync(recognition.File, method.Owner!, owner => JavaTrials(owner, method, roles, trials), trials, javac, java, cancellationToken);

    /// <summary>
    /// A small class that calls a file's methods, compiled beside a copy of the file and run, and what it printed for each
    /// trial. The class is written for the owner's name as Java loads it - its package and its nesting - by trialClassFor.
    /// </summary>
    private static async Task<IReadOnlyList<Result>?> RunJavaTrialClassAsync(string file, string methodsOwner, Func<string, string> trialClassFor,
        IReadOnlyList<Trial> trials, string javac, string java, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-trial", Guid.NewGuid().ToString("N")[..12]);
        try
        {
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, Path.GetFileName(file));
            File.Copy(file, copy);
            var source = await File.ReadAllTextAsync(copy, cancellationToken);
            var owner = (JavaPackage().Match(source) is { Success: true } package ? package.Groups["name"].Value + "." : "") + methodsOwner.Replace('.', '$');
            var trialClass = Path.Combine(folder, JavaTrialClass + ".java");
            await File.WriteAllTextAsync(trialClass, trialClassFor(owner), new UTF8Encoding(false), cancellationToken);

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
                WorkingDirectory = Path.GetDirectoryName(file) ?? folder,
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
    /// What both Java trials' classes hold: quote(text) writes a string as JSON does, boxed(items) makes an ArrayList of an
    /// int[], and plain(value) writes what a method gave back as JSON can hold it.
    /// </summary>
    private const string JavaTrialHelpers = """
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
        """;

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
            "other" => "secondList",
            "text" => "texts[index]",
            _ => "lists[index].length",
        }));
        var numbers = string.Join(", ", trials.Select(trial => trial.Number ?? 0));

        // Each word as a Java string literal: the words are letters alone, which JSON writes as Java does.
        var texts = string.Join(", ", trials.Select(trial => trial.Text is { } word ? JsonSerializer.Serialize(word) : "null"));

        // An array is given a copy of the trial's; a list, a fresh ArrayList of the same numbers; a prime test, no list.
        var listType = method.Parameters.Zip(roles).FirstOrDefault(pair => pair.Second.Role == "list").First?.Type;
        var madeList = listType is null ? "null" : listType.Name == "array" ? "lists[index].clone()" : "boxed(lists[index])";
        var otherType = method.Parameters.Zip(roles).FirstOrDefault(pair => pair.Second.Role == "other").First?.Type;
        var madeOther = otherType is null ? "null" : otherType.Name == "array" ? "others[index].clone()" : "boxed(others[index])";
        var others = string.Join(", ", trials.Select(trial => "{" + string.Join(", ", trial.Other ?? []) + "}"));
        return $$"""
            public class {{JavaTrialClass}} {
                {{JavaTrialHelpers}}

                public static void main(String[] args) throws Exception {
                    Class<?> owner = Class.forName("{{owner}}");
                    final java.lang.reflect.Method method = owner.getDeclaredMethod("{{method.Name}}"{{classes}});
                    method.setAccessible(true);
                    int[][] lists = { {{lists}} };
                    int[][] others = { {{others}} };
                    int[] sought = { {{sought}} };
                    int[] numbers = { {{numbers}} };
                    String[] texts = { {{texts}} };
                    for (int index = 0; index < lists.length; index++) {
                        final Object list = {{madeList}};
                        final Object secondList = {{madeOther}};
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
        trials = trials.Select(trial => new { list = trial.List, sought = trial.Sought, number = trial.Number, other = trial.Other, text = trial.Text }),
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
                root.TryGetProperty("error", out var error) ? error.GetString() : null,
                root.TryGetProperty("between", out var between) ? between.Clone() : null,
                root.TryGetProperty("stage", out var stage) ? stage.GetString() : null));
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
            {{JavaScriptPlain}}
            fixfinderPlan.trials.forEach((trial, index) => {
                const list = trial.list.slice();
                const given = fixfinderPlan.parameters.map(role =>
                    role === "list" ? list : role === "sought" ? trial.sought : role === "number" ? trial.number
                    : role === "other" ? trial.other.slice() : role === "text" ? trial.text : role === "length" ? trial.list.length : undefined);
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

    /// <summary>
    /// What both JavaScript trials write a value with, as JSON can hold it: undefined, null and a number that is not
    /// finite as null, a list's items a level at a time, and anything else as the text it is shown as.
    /// </summary>
    private const string JavaScriptPlain = """
        const fixfinderPlain = (value, depth) =>
            value === undefined || value === null || (typeof value === "number" && !Number.isFinite(value)) ? null
            : typeof value === "number" || typeof value === "boolean" || typeof value === "string" ? value
            : Array.isArray(value) && depth < 3 ? value.map(item => fixfinderPlain(item, depth + 1))
            : { repr: String(value).slice(0, 80) };
        """;

    [GeneratedRegex(@"^[A-Za-z_$][\w$]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"^\s*package\s+(?<name>[\w.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex JavaPackage();

    /// <summary>
    /// What both Python trials begin with: definitions(target) runs a file's definitions alone - its functions, classes,
    /// imports and constants - and gives back what they define; plain(value) writes a value as JSON can hold it; and
    /// say(outcome) prints what one call did, a line of its own.
    /// </summary>
    private const string PythonTrialHelpers = """
        import ast, json, os, sys, threading

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

        def definitions(target):
            sys.path.insert(0, os.path.dirname(os.path.abspath(target)))
            with open(target, encoding="utf-8") as source:
                tree = ast.parse(source.read(), target)
            tree.body = [node for node in tree.body if kept(node)]
            space = {"__name__": "fixfinder_trial", "__file__": target}
            exec(compile(tree, target, "exec"), space)
            return space
        """;

    /// <summary>
    /// Calls the function once for each trial, from the file's definitions alone, and prints, for each, what it gave back,
    /// the list after the call, or what stopped it. A call still going after 2 seconds is said, and the trials end there.
    /// </summary>
    private const string Script = PythonTrialHelpers + "\n\n" + """
        target, name, plan_file = sys.argv[1], sys.argv[2], sys.argv[3]
        with open(plan_file, encoding="utf-8") as given:
            plan = json.load(given)

        function = definitions(target)[name]
        list_parameter = next((parameter for parameter, role in plan["roles"].items() if role == "list"), None)

        for index, trial in enumerate(plan["trials"]):
            arguments = {}
            for parameter, role in plan["roles"].items():
                arguments[parameter] = (list(trial["list"]) if role == "list" else trial["sought"] if role == "sought"
                                        else trial["number"] if role == "number" else list(trial["other"]) if role == "other"
                                        else trial["text"] if role == "text" else len(trial["list"]))
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
