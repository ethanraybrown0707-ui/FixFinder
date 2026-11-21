using FixFinder.Core;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Recognition;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// A sort or a search recognised in a Python, JavaScript or Java program, called on lists of FixFinder's own: one that works is said to have
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

    /// <summary>
    /// The program's recognised algorithms, each called on its lists by its own language - Python, Node for JavaScript,
    /// the JDK for Java; null when this computer has not got it.
    /// </summary>
    private async Task<AlgorithmTrial.Outcome?> TriedAsync(string fileName, string code)
    {
        var path = Path.Combine(_temp.Path, fileName);
        await File.WriteAllTextAsync(path, code);

        if (fileName.EndsWith(".py", StringComparison.Ordinal))
        {
            if (PythonFrontend.FindInterpreter() is not { } python) return null;
            var read = await PythonFrontend.ReadAsync([path], python);
            return await AlgorithmTrial.TryAsync(AlgorithmRecognition.Of(read), read, python, CancellationToken.None);
        }

        if (fileName.EndsWith(".java", StringComparison.Ordinal))
        {
            if (JavaFrontend.FindTools() is not { } jdk) return null;
            var compiled = await JavaFrontend.ReadAsync([path], jdk.Javac, jdk.Java);
            return await AlgorithmTrial.TryAsync(AlgorithmRecognition.Of(compiled), compiled, jdk.Javac, CancellationToken.None, jdk.Java);
        }

        if (TargetFactory.FindOnPath("node") is not { } node) return null;
        var program = await JavaScriptFrontend.ReadAsync([path]);
        return await AlgorithmTrial.TryAsync(AlgorithmRecognition.Of(program), program, node, CancellationToken.None);
    }

    [Theory]
    [InlineData("Search.java", "public class Search {\n    static int find(int[] items, int target) {\n        int low = 0;\n        int high = items.length - 1;\n        while (low <= high) {\n            int mid = low + (high - low) / 2;\n            if (items[mid] == target) {\n                return mid;\n            } else if (items[mid] < target) {\n                low = mid + 1;\n            } else {\n                high = mid - 1;\n            }\n        }\n        return -1;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(find(new int[] {1, 3, 5, 7}, 5));\n    }\n}\n",
        "Search.find: Called with 6 lists of FixFinder's own and 31 values, each in them or not, it found every one that was there and none that was not.")]
    [InlineData("search.js", "function find(items, target) {\n    let low = 0;\n    let high = items.length - 1;\n    while (low <= high) {\n        const mid = Math.floor((low + high) / 2);\n        if (items[mid] === target) {\n            return mid;\n        } else if (items[mid] < target) {\n            low = mid + 1;\n        } else {\n            high = mid - 1;\n        }\n    }\n    return -1;\n}\n\nconsole.log(find([1, 3, 5, 7], 5));\n",
        "find: Called with 6 lists of FixFinder's own and 31 values, each in them or not, it found every one that was there and none that was not.")]
    [InlineData("search.py", "def find(items, target):\n    low = 0\n    high = len(items) - 1\n    while low <= high:\n        mid = (low + high) // 2\n        if items[mid] == target:\n            return mid\n        elif items[mid] < target:\n            low = mid + 1\n        else:\n            high = mid - 1\n    return -1\n\n\nprint(find([1, 3, 5, 7], 5))\n",
        "find: Called with 6 lists of FixFinder's own and 31 values, each in them or not, it found every one that was there and none that was not.")]
    [InlineData("search.py", "def find_largest_first(items, target):\n    low = 0\n    high = len(items) - 1\n    while low <= high:\n        mid = (low + high) // 2\n        if items[mid] == target:\n            return mid\n        elif items[mid] > target:\n            low = mid + 1\n        else:\n            high = mid - 1\n    return -1\n\n\nprint(find_largest_first([9, 7, 5, 3], 7))\n",
        "find_largest_first: Called with 6 lists in reverse order of FixFinder's own and 31 values, each in them or not, it found every one that was there and none that was not.")]
    [InlineData("Sort.java", "public class Sort {\n    static void bubble(int[] items) {\n        for (int i = 0; i < items.length - 1; i++) {\n            for (int j = 0; j < items.length - 1 - i; j++) {\n                if (items[j] > items[j + 1]) {\n                    int held = items[j];\n                    items[j] = items[j + 1];\n                    items[j + 1] = held;\n                }\n            }\n        }\n    }\n\n    public static void main(String[] args) {\n        int[] items = {5, 2, 9, 1};\n        bubble(items);\n        System.out.println(java.util.Arrays.toString(items));\n    }\n}\n",
        "Sort.bubble: Called with 10 lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - it gave each back in order, with the same items.")]
    [InlineData("sort.js", "function bubble(items) {\n    for (let i = 0; i < items.length - 1; i++) {\n        for (let j = 0; j < items.length - 1 - i; j++) {\n            if (items[j] > items[j + 1]) {\n                [items[j], items[j + 1]] = [items[j + 1], items[j]];\n            }\n        }\n    }\n    return items;\n}\n\nconsole.log(bubble([5, 2, 9, 1]));\n",
        "bubble: Called with 10 lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - it gave each back in order, with the same items.")]
    [InlineData("sort.py", "def bubble(items):\n    n = len(items)\n    for i in range(n - 1):\n        for j in range(n - 1 - i):\n            if items[j] > items[j + 1]:\n                items[j], items[j + 1] = items[j + 1], items[j]\n    return items\n\n\nprint(bubble([5, 2, 9, 1]))\n",
        "bubble: Called with 10 lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - it gave each back in order, with the same items.")]
    [InlineData("Largest.java", "public class Largest {\n    static int largest(int[] items) {\n        int best = items[0];\n        for (int item : items) {\n            if (item > best) {\n                best = item;\n            }\n        }\n        return best;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(largest(new int[] {3, 9, 2}));\n    }\n}\n",
        "Largest.largest: Called with 9 lists of FixFinder's own - of one item, in order, in reverse order, all below zero, all above zero and with repeats - it gave the largest item of each.")]
    [InlineData("largest.js", "function smallest(items) {\n    let least = items[0];\n    for (let index = 1; index < items.length; index++) {\n        if (items[index] < least) {\n            least = items[index];\n        }\n    }\n    return least;\n}\n\nconsole.log(smallest([3, 9, 2]));\n",
        "smallest: Called with 9 lists of FixFinder's own - of one item, in order, in reverse order, all below zero, all above zero and with repeats - it gave the smallest item of each.")]
    [InlineData("largest.py", "def largest(items):\n    best = items[0]\n    for item in items:\n        if item > best:\n            best = item\n    return best\n\n\ndef smallest(items):\n    least = items[0]\n    for index in range(1, len(items)):\n        if items[index] < least:\n            least = items[index]\n    return least\n\n\nprint(largest([3, 9, 2]), smallest([3, 9, 2]))\n",
        "largest: Called with 9 lists of FixFinder's own - of one item, in order, in reverse order, all below zero, all above zero and with repeats - it gave the largest item of each.|smallest: Called with 9 lists of FixFinder's own - of one item, in order, in reverse order, all below zero, all above zero and with repeats - it gave the smallest item of each.")]
    [InlineData("Sort.java", "public class Sort {\n    static void insertion(int[] items) {\n        for (int i = 1; i < items.length; i++) {\n            int key = items[i];\n            int j = i - 1;\n            while (j >= 0 && items[j] > key) {\n                items[j + 1] = items[j];\n                j--;\n            }\n            items[j + 1] = key;\n        }\n    }\n\n    public static void main(String[] args) {\n        int[] items = {5, 2, 9, 1};\n        insertion(items);\n        System.out.println(java.util.Arrays.toString(items));\n    }\n}\n",
        "Sort.insertion: Called with 10 lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - it gave each back in order, with the same items.")]
    [InlineData("sort.js", "function insertion(items) {\n    for (let i = 1; i < items.length; i++) {\n        const key = items[i];\n        let j = i - 1;\n        while (j >= 0 && items[j] > key) {\n            items[j + 1] = items[j];\n            j--;\n        }\n        items[j + 1] = key;\n    }\n    return items;\n}\n\nconsole.log(insertion([5, 2, 9, 1]));\n",
        "insertion: Called with 10 lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - it gave each back in order, with the same items.")]
    [InlineData("sort.py", "def insertion(items):\n    for i in range(1, len(items)):\n        key = items[i]\n        j = i - 1\n        while j >= 0 and items[j] > key:\n            items[j + 1] = items[j]\n            j -= 1\n        items[j + 1] = key\n    return items\n\n\nprint(insertion([5, 2, 9, 1]))\n",
        "insertion: Called with 10 lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - it gave each back in order, with the same items.")]
    [InlineData("ListAlgorithms.java", "import java.util.ArrayList;\nimport java.util.Collections;\nimport java.util.List;\n\npublic class ListAlgorithms {\n    static int find(List<Integer> items, int target) {\n        int low = 0;\n        int high = items.size() - 1;\n        while (low <= high) {\n            int mid = (low + high) / 2;\n            if (items.get(mid) == target) {\n                return mid;\n            } else if (items.get(mid) < target) {\n                low = mid + 1;\n            } else {\n                high = mid - 1;\n            }\n        }\n        return -1;\n    }\n\n    static boolean contains(List<Integer> marks, Integer wanted) {\n        for (Integer mark : marks) {\n            if (mark.equals(wanted)) {\n                return true;\n            }\n        }\n        return false;\n    }\n\n    static void bubble(ArrayList<Integer> items) {\n        for (int i = 0; i < items.size() - 1; i++) {\n            for (int j = 0; j < items.size() - 1 - i; j++) {\n                if (items.get(j) > items.get(j + 1)) {\n                    Integer held = items.get(j);\n                    items.set(j, items.get(j + 1));\n                    items.set(j + 1, held);\n                }\n            }\n        }\n    }\n\n    static void selection(List<Integer> items) {\n        for (int i = 0; i < items.size() - 1; i++) {\n            int smallest = i;\n            for (int j = i + 1; j < items.size(); j++) {\n                if (items.get(j) < items.get(smallest)) {\n                    smallest = j;\n                }\n            }\n            Collections.swap(items, i, smallest);\n        }\n    }\n\n    static void insertion(List<Integer> items) {\n        for (int i = 1; i < items.size(); i++) {\n            int key = items.get(i);\n            int j = i - 1;\n            while (j >= 0 && items.get(j) > key) {\n                items.set(j + 1, items.get(j));\n                j--;\n            }\n            items.set(j + 1, key);\n        }\n    }\n\n    public static void main(String[] args) {\n        ArrayList<Integer> items = new ArrayList<>(List.of(3, 1, 2));\n        bubble(items);\n        System.out.println(items + \" \" + find(items, 2) + \" \" + contains(items, 3));\n    }\n}\n",
        "ListAlgorithms.find: Called with 6 lists of FixFinder's own and 31 values, each in them or not, it found every one that was there and none that was not.|ListAlgorithms.contains: Called with 7 lists of FixFinder's own and 38 values, each in them or not, it found every one that was there and none that was not.|ListAlgorithms.bubble: Called with 10 lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - it gave each back in order, with the same items.|ListAlgorithms.selection: Called with 10 lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - it gave each back in order, with the same items.|ListAlgorithms.insertion: Called with 10 lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - it gave each back in order, with the same items.")]
    [InlineData("Search.java", "public class Search {\n    static int position(int[] marks, int wanted) {\n        for (int index = 0; index < marks.length; index++) {\n            if (marks[index] == wanted) {\n                return index;\n            }\n        }\n        return -1;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(position(new int[] {4, 8, 15}, 8));\n    }\n}\n",
        "Search.position: Called with 7 lists of FixFinder's own and 38 values, each in them or not, it found every one that was there and none that was not.")]
    [InlineData("search.js", "function contains(names, wanted) {\n    for (const name of names) {\n        if (name === wanted) {\n            return true;\n        }\n    }\n    return false;\n}\n\nconsole.log(contains([\"ann\", \"bob\"], \"bob\"));\n",
        "contains: Called with 7 lists of FixFinder's own and 38 values, each in them or not, it found every one that was there and none that was not.")]
    [InlineData("search.py", "def position(names, wanted):\n    for index in range(len(names)):\n        if names[index] == wanted:\n            return index\n    return -1\n\n\ndef contains(names, wanted):\n    for name in names:\n        if name == wanted:\n            return True\n    return False\n\n\nprint(position([\"ann\", \"bob\"], \"bob\"), contains([\"ann\"], \"cy\"))\n",
        "position: Called with 7 lists of FixFinder's own and 38 values, each in them or not, it found every one that was there and none that was not.|contains: Called with 7 lists of FixFinder's own and 38 values, each in them or not, it found every one that was there and none that was not.")]
    [InlineData("Sort.java", "public class Sort {\n    static void selection(int[] items) {\n        for (int i = 0; i < items.length - 1; i++) {\n            int smallest = i;\n            for (int j = i + 1; j < items.length; j++) {\n                if (items[j] < items[smallest]) {\n                    smallest = j;\n                }\n            }\n            int held = items[i];\n            items[i] = items[smallest];\n            items[smallest] = held;\n        }\n    }\n\n    public static void main(String[] args) {\n        int[] items = {5, 2, 9, 1};\n        selection(items);\n        System.out.println(java.util.Arrays.toString(items));\n    }\n}\n",
        "Sort.selection: Called with 10 lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - it gave each back in order, with the same items.")]
    [InlineData("sort.js", "function selection(items) {\n    for (let i = 0; i < items.length - 1; i++) {\n        let smallest = i;\n        for (let j = i + 1; j < items.length; j++) {\n            if (items[j] < items[smallest]) {\n                smallest = j;\n            }\n        }\n        [items[i], items[smallest]] = [items[smallest], items[i]];\n    }\n    return items;\n}\n\nconsole.log(selection([5, 2, 9, 1]));\n",
        "selection: Called with 10 lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - it gave each back in order, with the same items.")]
    [InlineData("sort.py", "def selection(items):\n    for i in range(len(items)):\n        smallest = i\n        for j in range(i + 1, len(items)):\n            if items[j] < items[smallest]:\n                smallest = j\n        items[i], items[smallest] = items[smallest], items[i]\n    return items\n\n\nprint(selection([5, 2, 9, 1]))\n",
        "selection: Called with 10 lists of FixFinder's own - empty, of one item, in order, in reverse order, with repeats and below zero - it gave each back in order, with the same items.")]
    public async Task EachAlgorithmThatWorksIsSaidToHaveWorkedOnEveryList(string fileName, string code, string expected)
    {
        if (await TriedAsync(fileName, code) is not { } outcome) return;

        Assert.Empty(outcome.Findings);
        Assert.Equal(expected.Split('|'), outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried}"));
    }

    /// <summary>Two sorts that leave [2, 1] as it was, each shown it, and a sort written largest first, which is a sort.</summary>
    [Fact]
    public async Task ASortThatMissesALastPairOrAFirstItemIsShownTheShortestListItGetsWrong()
    {
        const string code = "def bubble_one_short(items):\n    n = len(items)\n    for i in range(n - 1):\n        for j in range(n - 2 - i):\n            if items[j] > items[j + 1]:\n                items[j], items[j + 1] = items[j + 1], items[j]\n    return items\n\n\ndef selection_copied(items):\n    for i in range(len(items)):\n        smallest = i\n        for j in range(i + 1, len(items)):\n            if items[j] < items[smallest]:\n                smallest = j\n        items[i], items[smallest] = items[smallest], items[smallest]\n    return items\n\n\ndef insertion_stops_early(items):\n    for i in range(1, len(items)):\n        key = items[i]\n        j = i - 1\n        while j > 0 and items[j] > key:\n            items[j + 1] = items[j]\n            j -= 1\n        items[j + 1] = key\n    return items\n\n\ndef largest_first(items):\n    n = len(items)\n    for i in range(n - 1):\n        for j in range(n - 1 - i):\n            if items[j] < items[j + 1]:\n                items[j], items[j + 1] = items[j + 1], items[j]\n\n\nprint(bubble_one_short([3, 1, 2]), selection_copied([3, 1, 2]), insertion_stops_early([3, 1, 2]), largest_first([1, 2]))\n";
        if (await TriedAsync("sorts.py", code) is not { } outcome) return;

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
        if (await TriedAsync("searches.py", code) is not { } outcome) return;

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

    /// <summary>The same two sorts written in JavaScript, each shown [2, 1].</summary>
    [Fact]
    public async Task AJavaScriptSortThatMissesALastPairOrAFirstItemIsShownTheShortestListItGetsWrong()
    {
        const string code = "function bubbleOneShort(items) {\n    for (let i = 0; i < items.length - 1; i++) {\n        for (let j = 0; j < items.length - 2 - i; j++) {\n            if (items[j] > items[j + 1]) {\n                [items[j], items[j + 1]] = [items[j + 1], items[j]];\n            }\n        }\n    }\n    return items;\n}\n\nfunction insertionStopsEarly(items) {\n    for (let i = 1; i < items.length; i++) {\n        const key = items[i];\n        let j = i - 1;\n        while (j > 0 && items[j] > key) {\n            items[j + 1] = items[j];\n            j--;\n        }\n        items[j + 1] = key;\n    }\n    return items;\n}\n\nconsole.log(bubbleOneShort([3, 1, 2]), insertionStopsEarly([3, 1, 2]));\n";
        if (await TriedAsync("sorts.js", code) is not { } outcome) return;

        Assert.Equal([
            "bubbleOneShort: Called with [2, 1], `bubbleOneShort` gave back [2, 1], which is not in order - in order it is [1, 2].",
            "insertionStopsEarly: Called with [2, 1], `insertionStopsEarly` gave back [2, 1], which is not in order - in order it is [1, 2].",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "2 algorithm-sort-wrong: `bubbleOneShort` does not sort every list | Called with [2, 1], `bubbleOneShort` gave back [2, 1], which is not in order - in order it is [1, 2].",
            "13 algorithm-sort-wrong: `insertionStopsEarly` does not sort every list | Called with [2, 1], `insertionStopsEarly` gave back [2, 1], which is not in order - in order it is [1, 2].",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>The same three searches written in JavaScript: the one that never ends is left when its run's time is up.</summary>
    [Fact]
    public async Task AJavaScriptSearchThatMissesAValueIsShownItAndOneThatNeverEndsIsLeftUntried()
    {
        const string code = "function findOneShort(items, target) {\n    let low = 0;\n    let high = items.length - 1;\n    while (low < high) {\n        const mid = Math.floor((low + high) / 2);\n        if (items[mid] === target) {\n            return mid;\n        } else if (items[mid] < target) {\n            low = mid + 1;\n        } else {\n            high = mid - 1;\n        }\n    }\n    return -1;\n}\n\nfunction findForever(items, target) {\n    let low = 0;\n    let high = items.length - 1;\n    while (low <= high) {\n        const mid = Math.floor((low + high) / 2);\n        if (items[mid] === target) {\n            return mid;\n        } else if (items[mid] < target) {\n            low = mid;\n        } else {\n            high = mid - 1;\n        }\n    }\n    return -1;\n}\n\nfunction givesUpEarly(names, wanted) {\n    for (let index = 0; index < names.length; index++) {\n        if (names[index] === wanted) {\n            return index;\n        } else {\n            return -1;\n        }\n    }\n    return -1;\n}\n\nconsole.log(findOneShort([1, 3, 5], 3), givesUpEarly([\"a\", \"b\"], \"b\"));\n";
        if (await TriedAsync("searches.js", code) is not { } outcome) return;

        Assert.Equal([
            "findOneShort: Called with [5] and 5, `findOneShort` gave -1 - but 5 is there, at position 0.",
            "findForever: not tried",
            "givesUpEarly: Called with [1, 3] and 3, `givesUpEarly` gave -1 - but 3 is there, at position 1.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "4 algorithm-search-wrong: `findOneShort` does not find every value | Called with [5] and 5, `findOneShort` gave -1 - but 5 is there, at position 0.",
            "34 algorithm-search-wrong: `givesUpEarly` does not find every value | Called with [1, 3] and 3, `givesUpEarly` gave -1 - but 3 is there, at position 1.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>The same two sorts written in Java, over an int[], each shown [2, 1] - read from the array it sorted in place.</summary>
    [Fact]
    public async Task AJavaSortThatMissesALastPairOrAFirstItemIsShownTheShortestArrayItGetsWrong()
    {
        const string code = "public class Sorts {\n    static void bubbleOneShort(int[] items) {\n        for (int i = 0; i < items.length - 1; i++) {\n            for (int j = 0; j < items.length - 2 - i; j++) {\n                if (items[j] > items[j + 1]) {\n                    int held = items[j];\n                    items[j] = items[j + 1];\n                    items[j + 1] = held;\n                }\n            }\n        }\n    }\n\n    static void insertionStopsEarly(int[] items) {\n        for (int i = 1; i < items.length; i++) {\n            int key = items[i];\n            int j = i - 1;\n            while (j > 0 && items[j] > key) {\n                items[j + 1] = items[j];\n                j--;\n            }\n            items[j + 1] = key;\n        }\n    }\n\n    public static void main(String[] args) {\n        int[] items = {3, 1, 2};\n        bubbleOneShort(items);\n        insertionStopsEarly(items);\n        System.out.println(java.util.Arrays.toString(items));\n    }\n}\n";
        if (await TriedAsync("Sorts.java", code) is not { } outcome) return;

        Assert.Equal([
            "Sorts.bubbleOneShort: Called with [2, 1], `Sorts.bubbleOneShort` gave back [2, 1], which is not in order - in order it is [1, 2].",
            "Sorts.insertionStopsEarly: Called with [2, 1], `Sorts.insertionStopsEarly` gave back [2, 1], which is not in order - in order it is [1, 2].",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "3 algorithm-sort-wrong: `Sorts.bubbleOneShort` does not sort every list | Called with [2, 1], `Sorts.bubbleOneShort` gave back [2, 1], which is not in order - in order it is [1, 2].",
            "15 algorithm-sort-wrong: `Sorts.insertionStopsEarly` does not sort every list | Called with [2, 1], `Sorts.insertionStopsEarly` gave back [2, 1], which is not in order - in order it is [1, 2].",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>The same three searches written in Java: the one that never ends is left when its call's 2 seconds are up.</summary>
    [Fact]
    public async Task AJavaSearchThatMissesAValueIsShownItAndOneThatNeverEndsIsLeftUntried()
    {
        const string code = "public class Searches {\n    static int findOneShort(int[] items, int target) {\n        int low = 0;\n        int high = items.length - 1;\n        while (low < high) {\n            int mid = (low + high) / 2;\n            if (items[mid] == target) {\n                return mid;\n            } else if (items[mid] < target) {\n                low = mid + 1;\n            } else {\n                high = mid - 1;\n            }\n        }\n        return -1;\n    }\n\n    static int findForever(int[] items, int target) {\n        int low = 0;\n        int high = items.length - 1;\n        while (low <= high) {\n            int mid = (low + high) / 2;\n            if (items[mid] == target) {\n                return mid;\n            } else if (items[mid] < target) {\n                low = mid;\n            } else {\n                high = mid - 1;\n            }\n        }\n        return -1;\n    }\n\n    static int givesUpEarly(int[] marks, int wanted) {\n        for (int index = 0; index < marks.length; index++) {\n            if (marks[index] == wanted) {\n                return index;\n            } else {\n                return -1;\n            }\n        }\n        return -1;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(findOneShort(new int[] {1, 3, 5}, 3) + \" \" + givesUpEarly(new int[] {1, 2}, 2));\n    }\n}\n";
        if (await TriedAsync("Searches.java", code) is not { } outcome) return;

        Assert.Equal([
            "Searches.findOneShort: Called with [5] and 5, `Searches.findOneShort` gave -1 - but 5 is there, at position 0.",
            "Searches.findForever: not tried",
            "Searches.givesUpEarly: Called with [1, 3] and 3, `Searches.givesUpEarly` gave -1 - but 3 is there, at position 1.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "5 algorithm-search-wrong: `Searches.findOneShort` does not find every value | Called with [5] and 5, `Searches.findOneShort` gave -1 - but 5 is there, at position 0.",
            "35 algorithm-search-wrong: `Searches.givesUpEarly` does not find every value | Called with [1, 3] and 3, `Searches.givesUpEarly` gave -1 - but 3 is there, at position 1.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>The short bubble sort written over an ArrayList, with get and set: [2, 1] is read from the list it changed.</summary>
    [Fact]
    public async Task AJavaSortOverAListThatMissesALastPairIsShownTheShortestListItGetsWrong()
    {
        const string code = "import java.util.ArrayList;\nimport java.util.List;\n\npublic class ListSorts {\n    static void bubbleOneShort(ArrayList<Integer> items) {\n        for (int i = 0; i < items.size() - 1; i++) {\n            for (int j = 0; j < items.size() - 2 - i; j++) {\n                if (items.get(j) > items.get(j + 1)) {\n                    Integer held = items.get(j);\n                    items.set(j, items.get(j + 1));\n                    items.set(j + 1, held);\n                }\n            }\n        }\n    }\n\n    public static void main(String[] args) {\n        ArrayList<Integer> items = new ArrayList<>(List.of(3, 1, 2));\n        bubbleOneShort(items);\n        System.out.println(items);\n    }\n}\n";
        if (await TriedAsync("ListSorts.java", code) is not { } outcome) return;

        Assert.Equal([
            "ListSorts.bubbleOneShort: Called with [2, 1], `ListSorts.bubbleOneShort` gave back [2, 1], which is not in order - in order it is [1, 2].",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "6 algorithm-sort-wrong: `ListSorts.bubbleOneShort` does not sort every list | Called with [2, 1], `ListSorts.bubbleOneShort` gave back [2, 1], which is not in order - in order it is [1, 2].",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A largest started at 0, wrong for a list all below zero, and a smallest started at 0, wrong for [5].</summary>
    [Fact]
    public async Task ASearchForTheLargestOrSmallestThatStartsAtZeroIsShownTheListItGetsWrong()
    {
        const string code = "def largest_from_zero(items):\n    best = 0\n    for item in items:\n        if item > best:\n            best = item\n    return best\n\n\ndef smallest_from_zero(items):\n    least = 0\n    for item in items:\n        if item < least:\n            least = item\n    return least\n\n\nprint(largest_from_zero([3, 9, 2]), smallest_from_zero([3, -9, 2]))\n";
        if (await TriedAsync("extremes.py", code) is not { } outcome) return;

        Assert.Equal([
            "largest_from_zero: Called with [-3, -7, -1], `largest_from_zero` gave 0 - but the largest item is -1.",
            "smallest_from_zero: Called with [5], `smallest_from_zero` gave 0 - but the smallest item is 5.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "3 algorithm-extreme-wrong: `largest_from_zero` does not give back the largest item of every list | Called with [-3, -7, -1], `largest_from_zero` gave 0 - but the largest item is -1.",
            "11 algorithm-extreme-wrong: `smallest_from_zero` does not give back the smallest item of every list | Called with [5], `smallest_from_zero` gave 0 - but the smallest item is 5.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>The largest started at 0, written in JavaScript.</summary>
    [Fact]
    public async Task AJavaScriptSearchForTheLargestThatStartsAtZeroIsShownTheListItGetsWrong()
    {
        const string code = "function largestFromZero(items) {\n    let best = 0;\n    for (const item of items) {\n        if (item > best) {\n            best = item;\n        }\n    }\n    return best;\n}\n\nconsole.log(largestFromZero([3, 9, 2]));\n";
        if (await TriedAsync("extremes.js", code) is not { } outcome) return;

        Assert.Equal([
            "largestFromZero: Called with [-3, -7, -1], `largestFromZero` gave 0 - but the largest item is -1.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "3 algorithm-extreme-wrong: `largestFromZero` does not give back the largest item of every list | Called with [-3, -7, -1], `largestFromZero` gave 0 - but the largest item is -1.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>The largest started at 0, written in Java.</summary>
    [Fact]
    public async Task AJavaSearchForTheLargestThatStartsAtZeroIsShownTheListItGetsWrong()
    {
        const string code = "public class Extremes {\n    static int largestFromZero(int[] items) {\n        int best = 0;\n        for (int item : items) {\n            if (item > best) {\n                best = item;\n            }\n        }\n        return best;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(largestFromZero(new int[] {3, 9, 2}));\n    }\n}\n";
        if (await TriedAsync("Extremes.java", code) is not { } outcome) return;

        Assert.Equal([
            "Extremes.largestFromZero: Called with [-3, -7, -1], `Extremes.largestFromZero` gave 0 - but the largest item is -1.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "4 algorithm-extreme-wrong: `Extremes.largestFromZero` does not give back the largest item of every list | Called with [-3, -7, -1], `Extremes.largestFromZero` gave 0 - but the largest item is -1.",
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

    /// <summary>A check of a JavaScript program does the same, with the Node it runs with.</summary>
    [Fact]
    public async Task ACheckOfAJavaScriptProgramSaysWhatTheTrialShowedToo()
    {
        if (TargetFactory.FindOnPath("node") is null) return;

        var path = Path.Combine(_temp.Path, "sorts.js");
        await File.WriteAllTextAsync(path, """
            function bubble(items) {
                for (let i = 0; i < items.length - 1; i++) {
                    for (let j = 0; j < items.length - 2 - i; j++) {
                        if (items[j] > items[j + 1]) {
                            [items[j], items[j + 1]] = [items[j + 1], items[j]];
                        }
                    }
                }
                return items;
            }

            console.log(bubble([3, 1, 2]));

            """);

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.JavaScript }.CheckAsync(TargetFactory.FromFile(path));

        var seen = Assert.Single(report.Recognised);
        Assert.Equal("Called with [2, 1], `bubble` gave back [2, 1], which is not in order - in order it is [1, 2].", seen.Tried);

        var wrong = Assert.Single(report.Findings, finding => finding.RuleId == "algorithm-sort-wrong");
        Assert.Equal((2, Confidence.Certain, "seen when it ran"), (wrong.Line, wrong.Confidence, Evidence.Source(wrong)));
    }

    /// <summary>A check of a Java program does the same, with the JDK it is built with.</summary>
    [Fact]
    public async Task ACheckOfAJavaProgramSaysWhatTheTrialShowedToo()
    {
        if (JavaFrontend.FindTools() is null) return;

        var path = Path.Combine(_temp.Path, "Sorts.java");
        await File.WriteAllTextAsync(path, """
            public class Sorts {
                static void bubble(int[] items) {
                    for (int i = 0; i < items.length - 1; i++) {
                        for (int j = 0; j < items.length - 2 - i; j++) {
                            if (items[j] > items[j + 1]) {
                                int held = items[j];
                                items[j] = items[j + 1];
                                items[j + 1] = held;
                            }
                        }
                    }
                }

                public static void main(String[] args) {
                    int[] items = {3, 1, 2};
                    bubble(items);
                    System.out.println(java.util.Arrays.toString(items));
                }
            }

            """);

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Java }.CheckAsync(TargetFactory.FromFile(path));

        var seen = Assert.Single(report.Recognised);
        Assert.Equal("Called with [2, 1], `Sorts.bubble` gave back [2, 1], which is not in order - in order it is [1, 2].", seen.Tried);

        var wrong = Assert.Single(report.Findings, finding => finding.RuleId == "algorithm-sort-wrong");
        Assert.Equal((3, Confidence.Certain, "seen when it ran"), (wrong.Line, wrong.Confidence, Evidence.Source(wrong)));
    }
}
