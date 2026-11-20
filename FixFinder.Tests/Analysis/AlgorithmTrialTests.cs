using FixFinder.Core;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Recognition;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// A sort or a search recognised in a Python program, called on lists of FixFinder's own: one that works is said to have
/// given every list back in order, or found every value that was there and none that was not; one that does not is a
/// certain finding, shown with the shortest list it got wrong and what it gave back; one whose call never ends is left
/// untried, and nothing is said of it. The sentences are those the trials gave for these programs.
/// </summary>
public class AlgorithmTrialTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The program's recognised algorithms, each called on its lists; null when this computer has no Python.</summary>
    private async Task<AlgorithmTrial.Outcome?> TriedAsync(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, "program.py");
        await File.WriteAllTextAsync(path, code);
        var program = await PythonFrontend.ReadAsync([path], python);
        return await AlgorithmTrial.TryAsync(AlgorithmRecognition.Of(program), program, python, CancellationToken.None);
    }

    [Theory]
    [InlineData("def find(items, target):\n    low = 0\n    high = len(items) - 1\n    while low <= high:\n        mid = (low + high) // 2\n        if items[mid] == target:\n            return mid\n        elif items[mid] < target:\n            low = mid + 1\n        else:\n            high = mid - 1\n    return -1\n\n\nprint(find([1, 3, 5, 7], 5))\n",
        "find: Called with 6 lists of FixFinder's own and 31 values, each in them or not, it found every one that was there and none that was not.")]
    [InlineData("def find_largest_first(items, target):\n    low = 0\n    high = len(items) - 1\n    while low <= high:\n        mid = (low + high) // 2\n        if items[mid] == target:\n            return mid\n        elif items[mid] > target:\n            low = mid + 1\n        else:\n            high = mid - 1\n    return -1\n\n\nprint(find_largest_first([9, 7, 5, 3], 7))\n",
        "find_largest_first: Called with 6 lists in reverse order of FixFinder's own and 31 values, each in them or not, it found every one that was there and none that was not.")]
    [InlineData("def bubble(items):\n    n = len(items)\n    for i in range(n - 1):\n        for j in range(n - 1 - i):\n            if items[j] > items[j + 1]:\n                items[j], items[j + 1] = items[j + 1], items[j]\n    return items\n\n\nprint(bubble([5, 2, 9, 1]))\n",
        "bubble: Called with 10 lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - it gave each back in order, with the same items.")]
    [InlineData("def insertion(items):\n    for i in range(1, len(items)):\n        key = items[i]\n        j = i - 1\n        while j >= 0 and items[j] > key:\n            items[j + 1] = items[j]\n            j -= 1\n        items[j + 1] = key\n    return items\n\n\nprint(insertion([5, 2, 9, 1]))\n",
        "insertion: Called with 10 lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - it gave each back in order, with the same items.")]
    [InlineData("def position(names, wanted):\n    for index in range(len(names)):\n        if names[index] == wanted:\n            return index\n    return -1\n\n\ndef contains(names, wanted):\n    for name in names:\n        if name == wanted:\n            return True\n    return False\n\n\nprint(position([\"ann\", \"bob\"], \"bob\"), contains([\"ann\"], \"cy\"))\n",
        "position: Called with 7 lists of FixFinder's own and 38 values, each in them or not, it found every one that was there and none that was not.|contains: Called with 7 lists of FixFinder's own and 38 values, each in them or not, it found every one that was there and none that was not.")]
    [InlineData("def selection(items):\n    for i in range(len(items)):\n        smallest = i\n        for j in range(i + 1, len(items)):\n            if items[j] < items[smallest]:\n                smallest = j\n        items[i], items[smallest] = items[smallest], items[i]\n    return items\n\n\nprint(selection([5, 2, 9, 1]))\n",
        "selection: Called with 10 lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - it gave each back in order, with the same items.")]
    public async Task EachAlgorithmThatWorksIsSaidToHaveWorkedOnEveryList(string code, string expected)
    {
        if (await TriedAsync(code) is not { } outcome) return;

        Assert.Empty(outcome.Findings);
        Assert.Equal(expected.Split('|'), outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried}"));
    }

    /// <summary>Two sorts that leave [2, 1] as it was, each shown it, and a sort written largest first, which is a sort.</summary>
    [Fact]
    public async Task ASortThatMissesALastPairOrAFirstItemIsShownTheShortestListItGetsWrong()
    {
        const string code = "def bubble_one_short(items):\n    n = len(items)\n    for i in range(n - 1):\n        for j in range(n - 2 - i):\n            if items[j] > items[j + 1]:\n                items[j], items[j + 1] = items[j + 1], items[j]\n    return items\n\n\ndef selection_copied(items):\n    for i in range(len(items)):\n        smallest = i\n        for j in range(i + 1, len(items)):\n            if items[j] < items[smallest]:\n                smallest = j\n        items[i], items[smallest] = items[smallest], items[smallest]\n    return items\n\n\ndef insertion_stops_early(items):\n    for i in range(1, len(items)):\n        key = items[i]\n        j = i - 1\n        while j > 0 and items[j] > key:\n            items[j + 1] = items[j]\n            j -= 1\n        items[j + 1] = key\n    return items\n\n\ndef largest_first(items):\n    n = len(items)\n    for i in range(n - 1):\n        for j in range(n - 1 - i):\n            if items[j] < items[j + 1]:\n                items[j], items[j + 1] = items[j + 1], items[j]\n\n\nprint(bubble_one_short([3, 1, 2]), selection_copied([3, 1, 2]), insertion_stops_early([3, 1, 2]), largest_first([1, 2]))\n";
        if (await TriedAsync(code) is not { } outcome) return;

        Assert.Equal([
            "bubble_one_short: Called with [2, 1], `bubble_one_short` gave back [2, 1], which is not in order - in order it is [1, 2].",
            "insertion_stops_early: Called with [2, 1], `insertion_stops_early` gave back [2, 1], which is not in order - in order it is [1, 2].",
            "largest_first: Called with 10 lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - it gave each back largest first, with the same items.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "3 algorithm-sort-wrong: `bubble_one_short` does not sort every list | Called with [2, 1], `bubble_one_short` gave back [2, 1], which is not in order - in order it is [1, 2].",
            "21 algorithm-sort-wrong: `insertion_stops_early` does not sort every list | Called with [2, 1], `insertion_stops_early` gave back [2, 1], which is not in order - in order it is [1, 2].",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A binary search that misses the only item, a linear search that gives up after the first, and one that never ends.</summary>
    [Fact]
    public async Task ASearchThatMissesAValueIsShownTheListAndTheValueAndOneThatNeverEndsIsLeftUntried()
    {
        const string code = "def find_one_short(items, target):\n    low = 0\n    high = len(items) - 1\n    while low < high:\n        mid = (low + high) // 2\n        if items[mid] == target:\n            return mid\n        elif items[mid] < target:\n            low = mid + 1\n        else:\n            high = mid - 1\n    return -1\n\n\ndef find_forever(items, target):\n    low = 0\n    high = len(items) - 1\n    while low <= high:\n        mid = (low + high) // 2\n        if items[mid] == target:\n            return mid\n        elif items[mid] < target:\n            low = mid\n        else:\n            high = mid - 1\n    return -1\n\n\ndef gives_up_early(names, wanted):\n    for index in range(len(names)):\n        if names[index] == wanted:\n            return index\n        else:\n            return -1\n    return -1\n\n\nprint(find_one_short([1, 3, 5], 3), gives_up_early([\"a\", \"b\"], \"b\"))\n";
        if (await TriedAsync(code) is not { } outcome) return;

        Assert.Equal([
            "find_one_short: Called with [5] and 5, `find_one_short` gave -1 - but 5 is there, at position 0.",
            "find_forever: not tried",
            "gives_up_early: Called with [1, 3] and 3, `gives_up_early` gave -1 - but 3 is there, at position 1.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "4 algorithm-search-wrong: `find_one_short` does not find every value | Called with [5] and 5, `find_one_short` gave -1 - but 5 is there, at position 0.",
            "30 algorithm-search-wrong: `gives_up_early` does not find every value | Called with [1, 3] and 3, `gives_up_early` gave -1 - but 3 is there, at position 1.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A check of a Python program carries the trial on its recognition, and the sort that went wrong as a finding seen when it ran.</summary>
    [Fact]
    public async Task ACheckSaysWhatTheTrialShowedAndFindsTheSortThatWentWrong()
    {
        if (PythonFrontend.FindInterpreter() is null) return;

        var path = Path.Combine(_temp.Path, "sorts.py");
        await File.WriteAllTextAsync(path, """
            def bubble(items):
                n = len(items)
                for i in range(n - 1):
                    for j in range(n - 2 - i):
                        if items[j] > items[j + 1]:
                            items[j], items[j + 1] = items[j + 1], items[j]
                return items


            print(bubble([3, 1, 2]))

            """);

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Python }.CheckAsync(TargetFactory.FromFile(path));

        var seen = Assert.Single(report.Recognised);
        Assert.Equal("Called with [2, 1], `bubble` gave back [2, 1], which is not in order - in order it is [1, 2].", seen.Tried);

        var wrong = Assert.Single(report.Findings, finding => finding.RuleId == "algorithm-sort-wrong");
        Assert.Equal((3, Confidence.Certain, "seen when it ran"), (wrong.Line, wrong.Confidence, Evidence.Source(wrong)));
        Assert.Equal("`bubble` does not sort every list", wrong.Title);
        Assert.Equal("1 possible mistake in the code", report.LogicSummary);
    }
}
