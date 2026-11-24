using FixFinder.Core;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Recognition;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// A sort, a search, a reversal, a merge, a test of order, a prime test, a leap-year test, a factorial, Euclid's greatest common divisor or the Fibonacci numbers recognised in a Python, JavaScript or Java program, called on lists, numbers or years of FixFinder's own: one that works is said to have
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
    [InlineData("Fact.java", "public class Fact {\n    static long factorial(int n) {\n        long result = 1;\n        for (int i = 1; i <= n; i++) {\n            result *= i;\n        }\n        return result;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(factorial(5));\n    }\n}\n",
        "Fact.factorial: Called with every number from 1 to 10, and 0, it gave back each one's factorial, every one right.")]
    [InlineData("fact.js", "function factorial(n) {\n    let result = 1;\n    for (let i = 2; i <= n; i++) {\n        result *= i;\n    }\n    return result;\n}\n\nconsole.log(factorial(5));\n",
        "factorial: Called with every number from 1 to 10, and 0, it gave back each one's factorial, every one right.")]
    [InlineData("fact.py", "def factorial(n):\n    result = 1\n    for i in range(1, n + 1):\n        result *= i\n    return result\n\n\nprint(factorial(5))\n",
        "factorial: Called with every number from 1 to 10, and 0, it gave back each one's factorial, every one right.")]
    [InlineData("fact_down.py", "def factorial(n):\n    product = 1\n    while n > 1:\n        product *= n\n        n -= 1\n    return product\n\n\nprint(factorial(5))\n",
        "factorial: Called with every number from 1 to 10, and 0, it gave back each one's factorial, every one right.")]
    [InlineData("fact_recursive.py", "def factorial(n):\n    if n <= 1:\n        return 1\n    return n * factorial(n - 1)\n\n\nprint(factorial(5))\n",
        "factorial: Called with every number from 1 to 10, and 0, it gave back each one's factorial, every one right.")]
    [InlineData("Rev.java", "public class Rev {\n    static void reverse(int[] items) {\n        for (int left = 0, right = items.length - 1; left < right; left++, right--) {\n            int held = items[left];\n            items[left] = items[right];\n            items[right] = held;\n        }\n    }\n\n    public static void main(String[] args) {\n        int[] items = {1, 2, 3};\n        reverse(items);\n        System.out.println(java.util.Arrays.toString(items));\n    }\n}\n",
        "Rev.reverse: Called with 10 lists of FixFinder's own - empty, of one item, of odd and even lengths, with repeats and below zero - it gave each back in the opposite order.")]
    [InlineData("rev.js", "function reverse(items) {\n    let left = 0;\n    let right = items.length - 1;\n    while (left < right) {\n        [items[left], items[right]] = [items[right], items[left]];\n        left++;\n        right--;\n    }\n    return items;\n}\n\nconsole.log(reverse([1, 2, 3]));\n",
        "reverse: Called with 10 lists of FixFinder's own - empty, of one item, of odd and even lengths, with repeats and below zero - it gave each back in the opposite order.")]
    [InlineData("rev_front.js", "function reversedCopy(items) {\n    const result = [];\n    for (const item of items) {\n        result.unshift(item);\n    }\n    return result;\n}\n\nconsole.log(reversedCopy([1, 2, 3]));\n",
        "reversedCopy: Called with 10 lists of FixFinder's own - empty, of one item, of odd and even lengths, with repeats and below zero - it gave each back in the opposite order.")]
    [InlineData("rev.py", "def reverse(items):\n    left = 0\n    right = len(items) - 1\n    while left < right:\n        items[left], items[right] = items[right], items[left]\n        left += 1\n        right -= 1\n    return items\n\n\nprint(reverse([1, 2, 3]))\n",
        "reverse: Called with 10 lists of FixFinder's own - empty, of one item, of odd and even lengths, with repeats and below zero - it gave each back in the opposite order.")]
    [InlineData("rev_back.py", "def reversed_copy(items):\n    result = []\n    for i in range(len(items) - 1, -1, -1):\n        result.append(items[i])\n    return result\n\n\nprint(reversed_copy([1, 2, 3]))\n",
        "reversed_copy: Called with 10 lists of FixFinder's own - empty, of one item, of odd and even lengths, with repeats and below zero - it gave each back in the opposite order.")]
    [InlineData("rev_front.py", "def reversed_copy(items):\n    result = []\n    for item in items:\n        result.insert(0, item)\n    return result\n\n\nprint(reversed_copy([1, 2, 3]))\n",
        "reversed_copy: Called with 10 lists of FixFinder's own - empty, of one item, of odd and even lengths, with repeats and below zero - it gave each back in the opposite order.")]
    [InlineData("rev_half.py", "def reverse(items):\n    for i in range(len(items) // 2):\n        items[i], items[len(items) - 1 - i] = items[len(items) - 1 - i], items[i]\n\n\nmarks = [1, 2, 3]\nreverse(marks)\nprint(marks)\n",
        "reverse: Called with 10 lists of FixFinder's own - empty, of one item, of odd and even lengths, with repeats and below zero - it gave each back in the opposite order.")]
    [InlineData("Gcd.java", "public class Gcd {\n    static int gcd(int a, int b) {\n        if (b == 0) {\n            return a;\n        }\n        return gcd(b, a % b);\n    }\n\n    public static void main(String[] args) {\n        System.out.println(gcd(12, 18));\n    }\n}\n",
        "Gcd.gcd: Called with 10 pairs of numbers - each way round, with a divisor in common and with none, the same number twice, and with 0 - it gave back the greatest number dividing both of each, every one right.")]
    [InlineData("gcd.js", "function gcd(a, b) {\n    while (b !== 0) {\n        const held = b;\n        b = a % b;\n        a = held;\n    }\n    return a;\n}\n\nconsole.log(gcd(12, 18));\n",
        "gcd: Called with 10 pairs of numbers - each way round, with a divisor in common and with none, the same number twice, and with 0 - it gave back the greatest number dividing both of each, every one right.")]
    [InlineData("gcd.py", "def gcd(a, b):\n    while b != 0:\n        a, b = b, a % b\n    return a\n\n\nprint(gcd(12, 18))\n",
        "gcd: Called with 10 pairs of numbers - each way round, with a divisor in common and with none, the same number twice, and with 0 - it gave back the greatest number dividing both of each, every one right.")]
    [InlineData("gcd_calls.py", "def gcd(a, b):\n    if b == 0:\n        return a\n    return gcd(b, a % b)\n\n\nprint(gcd(12, 18))\n",
        "gcd: Called with 10 pairs of numbers - each way round, with a divisor in common and with none, the same number twice, and with 0 - it gave back the greatest number dividing both of each, every one right.")]
    [InlineData("gcd_held.py", "def gcd(first, second):\n    while second > 0:\n        remainder = first % second\n        first = second\n        second = remainder\n    return first\n\n\nprint(gcd(12, 18))\n",
        "gcd: Called with 10 pairs of numbers - each way round, with a divisor in common and with none, the same number twice, and with 0 - it gave back the greatest number dividing both of each, every one right.")]
    [InlineData("Fib.java", "public class Fib {\n    static long fib(int n) {\n        long a = 0;\n        long b = 1;\n        for (int i = 0; i < n; i++) {\n            long next = a + b;\n            a = b;\n            b = next;\n        }\n        return a;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(fib(10));\n    }\n}\n",
        "Fib.fib: Called with every number from 1 to 10, it gave back 1 and 1 to start, and each after them as the two before it added.")]
    [InlineData("fib.js", "function fib(n) {\n    let a = 0;\n    let b = 1;\n    for (let i = 0; i < n; i++) {\n        [a, b] = [b, a + b];\n    }\n    return a;\n}\n\nconsole.log(fib(10));\n",
        "fib: Called with every number from 1 to 10, it gave back 1 and 1 to start, and each after them as the two before it added.")]
    [InlineData("fib.py", "def fib(n):\n    a, b = 0, 1\n    for _ in range(n):\n        a, b = b, a + b\n    return a\n\n\nprint([fib(n) for n in range(10)])\n",
        "fib: Called with every number from 1 to 10, it gave back 1 and 1 to start, and each after them as the two before it added.")]
    [InlineData("fib_calls.py", "def fib(n):\n    if n <= 1:\n        return n\n    return fib(n - 1) + fib(n - 2)\n\n\nprint(fib(10))\n",
        "fib: Called with every number from 1 to 10, it gave back 1 and 1 to start, and each after them as the two before it added.")]
    [InlineData("fib_from_one.py", "def fib(n):\n    if n == 1 or n == 2:\n        return 1\n    return fib(n - 1) + fib(n - 2)\n\n\nprint(fib(10))\n",
        "fib: Called with every number from 1 to 10, it gave back 1 and 1 to start, and each after them as the two before it added.")]
    [InlineData("fib_from_zero.py", "def fib(n):\n    a, b = 0, 1\n    for _ in range(n - 1):\n        a, b = b, a + b\n    return a\n\n\nprint(fib(10))\n",
        "fib: Called with every number from 1 to 10, it gave back 0 and 1 to start, and each after them as the two before it added.")]
    [InlineData("fib_held.py", "def fib(n):\n    previous = 1\n    current = 1\n    for _ in range(n - 1):\n        following = previous + current\n        previous = current\n        current = following\n    return previous\n\n\nprint(fib(10))\n",
        "fib: Called with every number from 1 to 10, it gave back 1 and 1 to start, and each after them as the two before it added.")]
    [InlineData("fib_list.py", "def fib(n):\n    numbers = [0, 1]\n    for i in range(2, n + 1):\n        numbers.append(numbers[i - 1] + numbers[i - 2])\n    return numbers[n]\n\n\nprint(fib(10))\n",
        "fib: Called with every number from 1 to 10, it gave back 1 and 1 to start, and each after them as the two before it added.")]
    [InlineData("fib_stairs.py", "def ways_up_stairs(n):\n    a, b = 1, 1\n    for _ in range(n):\n        a, b = b, a + b\n    return a\n\n\nprint(ways_up_stairs(10))\n",
        "ways_up_stairs: Called with every number from 1 to 10, it gave back 1 and 2 to start, and each after them as the two before it added.")]
    [InlineData("Merge.java", "public class Merge {\n    static int[] merge(int[] first, int[] second) {\n        int[] result = new int[first.length + second.length];\n        int i = 0, j = 0, k = 0;\n        while (i < first.length && j < second.length) {\n            if (first[i] <= second[j]) {\n                result[k++] = first[i++];\n            } else {\n                result[k++] = second[j++];\n            }\n        }\n        while (i < first.length) {\n            result[k++] = first[i++];\n        }\n        while (j < second.length) {\n            result[k++] = second[j++];\n        }\n        return result;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(java.util.Arrays.toString(merge(new int[] {1, 3}, new int[] {2})));\n    }\n}\n",
        "Merge.merge: Called with 11 pairs of lists in order - empty, of one item, one running out before the other either way round, with the same item in both and below zero - it gave back every item of both, in order.")]
    [InlineData("merge.js", "function merge(first, second) {\n    const result = [];\n    let i = 0;\n    let j = 0;\n    while (i < first.length && j < second.length) {\n        if (first[i] <= second[j]) {\n            result.push(first[i]);\n            i++;\n        } else {\n            result.push(second[j]);\n            j++;\n        }\n    }\n    return result.concat(first.slice(i), second.slice(j));\n}\n\nconsole.log(merge([1, 3], [2]));\n",
        "merge: Called with 11 pairs of lists in order - empty, of one item, one running out before the other either way round, with the same item in both and below zero - it gave back every item of both, in order.")]
    [InlineData("merge.py", "def merge(first, second):\n    result = []\n    i = 0\n    j = 0\n    while i < len(first) and j < len(second):\n        if first[i] <= second[j]:\n            result.append(first[i])\n            i += 1\n        else:\n            result.append(second[j])\n            j += 1\n    result.extend(first[i:])\n    result.extend(second[j:])\n    return result\n\n\nprint(merge([1, 3], [2]))\n",
        "merge: Called with 11 pairs of lists in order - empty, of one item, one running out before the other either way round, with the same item in both and below zero - it gave back every item of both, in order.")]
    [InlineData("Order.java", "public class Order {\n    static boolean isSorted(int[] items) {\n        for (int i = 0; i + 1 < items.length; i++) {\n            if (items[i] > items[i + 1]) {\n                return false;\n            }\n        }\n        return true;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(isSorted(new int[] {1, 2, 3}));\n    }\n}\n",
        "Order.isSorted: Called with 12 lists of FixFinder's own - empty, of one item, in order, and out of order at the start, in the middle and at the very end - it said which were in order by its own test, every one right.")]
    [InlineData("order.js", "function isSorted(items) {\n    for (let i = 0; i < items.length - 1; i++) {\n        if (items[i] > items[i + 1]) {\n            return false;\n        }\n    }\n    return true;\n}\n\nconsole.log(isSorted([1, 2, 3]));\n",
        "isSorted: Called with 12 lists of FixFinder's own - empty, of one item, in order, and out of order at the start, in the middle and at the very end - it said which were in order by its own test, every one right.")]
    [InlineData("order.py", "def is_sorted(items):\n    for i in range(len(items) - 1):\n        if items[i] > items[i + 1]:\n            return False\n    return True\n\n\nprint(is_sorted([1, 2, 3]))\n",
        "is_sorted: Called with 12 lists of FixFinder's own - empty, of one item, in order, and out of order at the start, in the middle and at the very end - it said which were in order by its own test, every one right.")]
    [InlineData("order_falling.py", "def is_falling(items):\n    for i in range(len(items) - 1):\n        if items[i] < items[i + 1]:\n            return False\n    return True\n\n\nprint(is_falling([3, 2, 1]))\n",
        "is_falling: Called with 12 lists of FixFinder's own - empty, of one item, in order, and out of order at the start, in the middle and at the very end - it said which were in order by its own test, every one right.")]
    [InlineData("order_strict.py", "def strictly_rising(items):\n    for i in range(1, len(items)):\n        if items[i - 1] >= items[i]:\n            return False\n    return True\n\n\nprint(strictly_rising([1, 2, 3]))\n",
        "strictly_rising: Called with 12 lists of FixFinder's own - empty, of one item, in order, and out of order at the start, in the middle and at the very end - it said which were in order by its own test, every one right.")]
    [InlineData("Leap.java", "public class Leap {\n    static boolean isLeap(int year) {\n        return year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);\n    }\n\n    public static void main(String[] args) {\n        System.out.println(isLeap(2024));\n    }\n}\n",
        "Leap.isLeap: Called with the years 2024, 2023, 2000, 1900, 2100, 2400, 1996, 2019, 1600 and 1800, it said which were leap years and which were not, every one right.")]
    [InlineData("leap.js", "function isLeapYear(year) {\n    if (year % 400 === 0) {\n        return true;\n    }\n    if (year % 100 === 0) {\n        return false;\n    }\n    return year % 4 === 0;\n}\n\nconsole.log(isLeapYear(2024));\n",
        "isLeapYear: Called with the years 2024, 2023, 2000, 1900, 2100, 2400, 1996, 2019, 1600 and 1800, it said which were leap years and which were not, every one right.")]
    [InlineData("leap.py", "def is_leap(year):\n    return year % 4 == 0 and (year % 100 != 0 or year % 400 == 0)\n\n\nprint(is_leap(2024))\n",
        "is_leap: Called with the years 2024, 2023, 2000, 1900, 2100, 2400, 1996, 2019, 1600 and 1800, it said which were leap years and which were not, every one right.")]
    [InlineData("leap_steps.py", "def is_leap(year):\n    if year % 400 == 0:\n        return True\n    if year % 100 == 0:\n        return False\n    return year % 4 == 0\n\n\nprint(is_leap(1900))\n",
        "is_leap: Called with the years 2024, 2023, 2000, 1900, 2100, 2400, 1996, 2019, 1600 and 1800, it said which were leap years and which were not, every one right.")]
    [InlineData("Prime.java", "public class Prime {\n    static boolean isPrime(int n) {\n        if (n < 2) {\n            return false;\n        }\n        for (int i = 2; i * i <= n; i++) {\n            if (n % i == 0) {\n                return false;\n            }\n        }\n        return true;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(isPrime(17));\n    }\n}\n",
        "Prime.isPrime: Called with every number from 0 to 30, it said which were prime and which were not, every one right.")]
    [InlineData("prime.js", "function isPrime(n) {\n    if (n < 2) {\n        return false;\n    }\n    for (let i = 2; i * i <= n; i++) {\n        if (n % i === 0) {\n            return false;\n        }\n    }\n    return true;\n}\n\nconsole.log(isPrime(17));\n",
        "isPrime: Called with every number from 0 to 30, it said which were prime and which were not, every one right.")]
    [InlineData("prime.py", "def is_prime(n):\n    if n < 2:\n        return False\n    for i in range(2, int(n ** 0.5) + 1):\n        if n % i == 0:\n            return False\n    return True\n\n\nprint([number for number in range(20) if is_prime(number)])\n",
        "is_prime: Called with every number from 0 to 30, it said which were prime and which were not, every one right.")]
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

    /// <summary>A prime test with no answer of its own for 0 and 1, one whose divisors stop too soon, and one whose run up to the number itself.</summary>
    [Fact]
    public async Task APrimeTestIsShownTheSmallestNumberItGetsWrong()
    {
        const string code = "def prime_without_small(n):\n    for i in range(2, n):\n        if n % i == 0:\n            return False\n    return True\n\n\ndef prime_stops_early(n):\n    if n < 2:\n        return False\n    for i in range(2, n // 2):\n        if n % i == 0:\n            return False\n    return True\n\n\ndef prime_up_to_itself(n):\n    if n < 2:\n        return False\n    for i in range(2, n + 1):\n        if n % i == 0:\n            return False\n    return True\n\n\nprint(prime_without_small(7), prime_stops_early(7), prime_up_to_itself(7))\n";
        if (await TriedAsync("primes.py", code) is not { } outcome) return;

        Assert.Equal([
            "prime_without_small: Called with 0, `prime_without_small` gave True - but 0 is not prime.",
            "prime_stops_early: Called with 4, `prime_stops_early` gave True - but 4 is 2 times 2.",
            "prime_up_to_itself: Called with 2, `prime_up_to_itself` gave False - but 2 is prime.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "2 algorithm-prime-wrong: `prime_without_small` does not say rightly which numbers are prime | Called with 0, `prime_without_small` gave True - but 0 is not prime.",
            "11 algorithm-prime-wrong: `prime_stops_early` does not say rightly which numbers are prime | Called with 4, `prime_stops_early` gave True - but 4 is 2 times 2.",
            "20 algorithm-prime-wrong: `prime_up_to_itself` does not say rightly which numbers are prime | Called with 2, `prime_up_to_itself` gave False - but 2 is prime.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>The prime test with no answer of its own for 0 and 1, written in JavaScript.</summary>
    [Fact]
    public async Task AJavaScriptPrimeTestIsShownTheSmallestNumberItGetsWrong()
    {
        const string code = "function primeWithoutSmall(n) {\n    for (let i = 2; i < n; i++) {\n        if (n % i === 0) {\n            return false;\n        }\n    }\n    return true;\n}\n\nconsole.log(primeWithoutSmall(7));\n";
        if (await TriedAsync("primes.js", code) is not { } outcome) return;

        Assert.Equal([
            "primeWithoutSmall: Called with 0, `primeWithoutSmall` gave true - but 0 is not prime.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "2 algorithm-prime-wrong: `primeWithoutSmall` does not say rightly which numbers are prime | Called with 0, `primeWithoutSmall` gave true - but 0 is not prime.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>The prime test whose divisors stop too soon, written in Java: 4 is 2 times 2.</summary>
    [Fact]
    public async Task AJavaPrimeTestIsShownTheSmallestNumberItGetsWrong()
    {
        const string code = "public class Primes {\n    static boolean primeStopsEarly(int n) {\n        if (n < 2) {\n            return false;\n        }\n        for (int i = 2; i < n / 2; i++) {\n            if (n % i == 0) {\n                return false;\n            }\n        }\n        return true;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(primeStopsEarly(7));\n    }\n}\n";
        if (await TriedAsync("Primes.java", code) is not { } outcome) return;

        Assert.Equal([
            "Primes.primeStopsEarly: Called with 4, `Primes.primeStopsEarly` gave true - but 4 is 2 times 2.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "6 algorithm-prime-wrong: `Primes.primeStopsEarly` does not say rightly which numbers are prime | Called with 4, `Primes.primeStopsEarly` gave true - but 4 is 2 times 2.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A leap-year test that asks of 4 alone, one that leaves out 400, and one that asks of 4 before 100 and 400.</summary>
    [Fact]
    public async Task ALeapYearTestIsShownTheFirstYearItGetsWrong()
    {
        const string code = "def is_leap_four(year):\n    return year % 4 == 0\n\n\ndef is_leap_no_400(year):\n    return year % 4 == 0 and year % 100 != 0\n\n\ndef is_leap_order(year):\n    if year % 4 == 0:\n        return True\n    elif year % 100 == 0:\n        return False\n    elif year % 400 == 0:\n        return True\n    return False\n\n\nprint(is_leap_four(2024), is_leap_no_400(2024), is_leap_order(2024))\n";
        if (await TriedAsync("leap_years.py", code) is not { } outcome) return;

        Assert.Equal([
            "is_leap_four: Called with 1900, `is_leap_four` gave True - but 100 divides 1900 and 400 does not, so it is not a leap year.",
            "is_leap_no_400: Called with 2000, `is_leap_no_400` gave False - but 400 divides 2000, so it is a leap year.",
            "is_leap_order: Called with 1900, `is_leap_order` gave True - but 100 divides 1900 and 400 does not, so it is not a leap year.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "2 algorithm-leap-year-wrong: `is_leap_four` does not say rightly which years are leap years | Called with 1900, `is_leap_four` gave True - but 100 divides 1900 and 400 does not, so it is not a leap year.",
            "6 algorithm-leap-year-wrong: `is_leap_no_400` does not say rightly which years are leap years | Called with 2000, `is_leap_no_400` gave False - but 400 divides 2000, so it is a leap year.",
            "10 algorithm-leap-year-wrong: `is_leap_order` does not say rightly which years are leap years | Called with 1900, `is_leap_order` gave True - but 100 divides 1900 and 400 does not, so it is not a leap year.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>The leap-year test that leaves out 400, written in JavaScript: 2000 was a leap year.</summary>
    [Fact]
    public async Task AJavaScriptLeapYearTestIsShownTheFirstYearItGetsWrong()
    {
        const string code = "function isLeapYear(year) {\n    return year % 4 === 0 && year % 100 !== 0;\n}\n\nconsole.log(isLeapYear(2000));\n";
        if (await TriedAsync("leapNo400.js", code) is not { } outcome) return;

        Assert.Equal([
            "isLeapYear: Called with 2000, `isLeapYear` gave false - but 400 divides 2000, so it is a leap year.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "2 algorithm-leap-year-wrong: `isLeapYear` does not say rightly which years are leap years | Called with 2000, `isLeapYear` gave false - but 400 divides 2000, so it is a leap year.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A leap-year test in Java whose or comes before its and: 1900 is taken for a leap year.</summary>
    [Fact]
    public async Task AJavaLeapYearTestIsShownTheFirstYearItGetsWrong()
    {
        const string code = "public class LeapOr {\n    static boolean isLeap(int year) {\n        return year % 4 == 0 || year % 400 == 0 && year % 100 != 0;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(isLeap(1900));\n    }\n}\n";
        if (await TriedAsync("LeapOr.java", code) is not { } outcome) return;

        Assert.Equal([
            "LeapOr.isLeap: Called with 1900, `LeapOr.isLeap` gave true - but 100 divides 1900 and 400 does not, so it is not a leap year.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "3 algorithm-leap-year-wrong: `LeapOr.isLeap` does not say rightly which years are leap years | Called with 1900, `LeapOr.isLeap` gave true - but 100 divides 1900 and 400 does not, so it is not a leap year.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A factorial whose loop stops one short, and one whose product starts at 0.</summary>
    [Fact]
    public async Task AFactorialIsShownTheFirstNumberItGetsWrong()
    {
        const string code = "def factorial_short(n):\n    result = 1\n    for i in range(1, n):\n        result *= i\n    return result\n\n\ndef factorial_from_zero(n):\n    result = 0\n    for i in range(1, n + 1):\n        result = result * i\n    return result\n\n\nprint(factorial_short(5), factorial_from_zero(5))\n";
        if (await TriedAsync("factorials.py", code) is not { } outcome) return;

        Assert.Equal([
            "factorial_short: Called with 2, `factorial_short` gave 1 - but 2 factorial is 1 * 2, which is 2.",
            "factorial_from_zero: Called with 1, `factorial_from_zero` gave 0 - but 1 factorial is 1.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "3 algorithm-factorial-wrong: `factorial_short` does not work out every factorial rightly | Called with 2, `factorial_short` gave 1 - but 2 factorial is 1 * 2, which is 2.",
            "10 algorithm-factorial-wrong: `factorial_from_zero` does not work out every factorial rightly | Called with 1, `factorial_from_zero` gave 0 - but 1 factorial is 1.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>
    /// A factorial calling itself that stops only at 1, called with 0: it never stops, until Python's limit on calls
    /// within calls ends it - whose words differ between versions of Python, so only where they start and end are held.
    /// </summary>
    [Fact]
    public async Task AFactorialThatStopsOnlyAtOneIsShownGoingWrongOnZero()
    {
        const string code = "def factorial_from_one(n):\n    if n == 1:\n        return 1\n    return n * factorial_from_one(n - 1)\n\n\nprint(factorial_from_one(5))\n";
        if (await TriedAsync("factorial_from_one.py", code) is not { } outcome) return;

        var tried = Assert.Single(outcome.Seen).Tried!;
        Assert.StartsWith("Called with 0, `factorial_from_one` stopped with RecursionError: maximum recursion depth exceeded", tried);
        Assert.EndsWith(" - but 0 factorial is 1, the product of no numbers at all.", tried);
        var finding = Assert.Single(outcome.Findings);
        Assert.Equal((4, "algorithm-factorial-wrong", Confidence.Certain), (finding.Line, finding.RuleId, finding.Confidence));
        Assert.Equal(tried, finding.Found);
    }

    /// <summary>A factorial calling itself that stops only at 1, written in JavaScript: given 0, it calls itself until the stack is full.</summary>
    [Fact]
    public async Task AJavaScriptFactorialThatStopsOnlyAtOneIsShownGoingWrongOnZero()
    {
        const string code = "function factorial(n) {\n    if (n === 1) {\n        return 1;\n    }\n    return n * factorial(n - 1);\n}\n\nconsole.log(factorial(5));\n";
        if (await TriedAsync("factorial.js", code) is not { } outcome) return;

        Assert.Equal([
            "factorial: Called with 0, `factorial` stopped with RangeError: Maximum call stack size exceeded - but 0 factorial is 1, the product of no numbers at all.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "5 algorithm-factorial-wrong: `factorial` does not work out every factorial rightly | Called with 0, `factorial` stopped with RangeError: Maximum call stack size exceeded - but 0 factorial is 1, the product of no numbers at all.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A factorial in Java whose loop stops one short: 2 factorial is 2.</summary>
    [Fact]
    public async Task AJavaFactorialIsShownTheFirstNumberItGetsWrong()
    {
        const string code = "public class FactShort {\n    static int factorial(int n) {\n        int result = 1;\n        for (int i = 1; i < n; i++) {\n            result = result * i;\n        }\n        return result;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(factorial(5));\n    }\n}\n";
        if (await TriedAsync("FactShort.java", code) is not { } outcome) return;

        Assert.Equal([
            "FactShort.factorial: Called with 2, `FactShort.factorial` gave 1 - but 2 factorial is 1 * 2, which is 2.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "4 algorithm-factorial-wrong: `FactShort.factorial` does not work out every factorial rightly | Called with 2, `FactShort.factorial` gave 1 - but 2 factorial is 1 * 2, which is 2.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A reversal swapping all the way along, given back and in place, and one going back from the end that stops before position 0.</summary>
    [Fact]
    public async Task AReversalIsShownTheShortestListItGetsWrong()
    {
        const string code = "def reverse_all(items):\n    for i in range(len(items)):\n        items[i], items[len(items) - 1 - i] = items[len(items) - 1 - i], items[i]\n    return items\n\n\ndef reversed_short(items):\n    result = []\n    for i in range(len(items) - 1, 0, -1):\n        result.append(items[i])\n    return result\n\n\ndef reverse_in_place_all(items):\n    for i in range(len(items)):\n        items[i], items[len(items) - 1 - i] = items[len(items) - 1 - i], items[i]\n\n\nprint(reverse_all([1, 2, 3]), reversed_short([1, 2, 3]))\n";
        if (await TriedAsync("reversals.py", code) is not { } outcome) return;

        Assert.Equal([
            "reverse_all: Called with [1, 2], `reverse_all` gave back [1, 2] - reversed, it is [2, 1].",
            "reversed_short: Called with [1], `reversed_short` gave back [] - reversed, it is [1].",
            "reverse_in_place_all: Called with [1, 2], `reverse_in_place_all` left the list it was given as [1, 2] - reversed, it is [2, 1].",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "2 algorithm-reversal-wrong: `reverse_all` does not reverse every list | Called with [1, 2], `reverse_all` gave back [1, 2] - reversed, it is [2, 1].",
            "9 algorithm-reversal-wrong: `reversed_short` does not reverse every list | Called with [1], `reversed_short` gave back [] - reversed, it is [1].",
            "15 algorithm-reversal-wrong: `reverse_in_place_all` does not reverse every list | Called with [1, 2], `reverse_in_place_all` left the list it was given as [1, 2] - reversed, it is [2, 1].",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A reversal swapping all the way along, written in JavaScript: each pair is swapped twice.</summary>
    [Fact]
    public async Task AJavaScriptReversalIsShownTheShortestListItGetsWrong()
    {
        const string code = "function reverse(items) {\n    for (let i = 0; i < items.length; i++) {\n        const held = items[i];\n        items[i] = items[items.length - 1 - i];\n        items[items.length - 1 - i] = held;\n    }\n    return items;\n}\n\nconsole.log(reverse([1, 2, 3]));\n";
        if (await TriedAsync("reverse.js", code) is not { } outcome) return;

        Assert.Equal([
            "reverse: Called with [1, 2], `reverse` gave back [1, 2] - reversed, it is [2, 1].",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "2 algorithm-reversal-wrong: `reverse` does not reverse every list | Called with [1, 2], `reverse` gave back [1, 2] - reversed, it is [2, 1].",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A reversal in Java copying from one position too far along: items[items.length - i] is past the end when i is 0.</summary>
    [Fact]
    public async Task AJavaReversalIsShownTheShortestListItGetsWrong()
    {
        const string code = "public class RevCopy {\n    static int[] reversed(int[] items) {\n        int[] result = new int[items.length];\n        for (int i = 0; i < items.length; i++) {\n            result[i] = items[items.length - i];\n        }\n        return result;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(java.util.Arrays.toString(reversed(new int[] {1, 2, 3})));\n    }\n}\n";
        if (await TriedAsync("RevCopy.java", code) is not { } outcome) return;

        Assert.Equal([
            "RevCopy.reversed: Called with [1], `RevCopy.reversed` stopped with ArrayIndexOutOfBoundsException: Index 1 out of bounds for length 1.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "4 algorithm-reversal-wrong: `RevCopy.reversed` does not reverse every list | Called with [1], `RevCopy.reversed` stopped with ArrayIndexOutOfBoundsException: Index 1 out of bounds for length 1.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>
    /// Euclid's loop giving back the divisor once it has become 0, and one running while a % b is not 0, which divides by
    /// 0 when given it - whose words for that differ between versions of Python, so only where they start and end are held.
    /// </summary>
    [Fact]
    public async Task AGreatestCommonDivisorIsShownTheFirstPairItGetsWrong()
    {
        const string code = "def gives_back_the_divisor(a, b):\n    while b != 0:\n        a, b = b, a % b\n    return b\n\n\ndef stops_at_no_remainder(a, b):\n    while a % b != 0:\n        a, b = b, a % b\n    return b\n\n\nprint(gives_back_the_divisor(12, 18), stops_at_no_remainder(12, 18))\n";
        if (await TriedAsync("divisors.py", code) is not { } outcome) return;

        Assert.Equal(2, outcome.Seen.Count);
        Assert.Equal("Called with 12 and 18, `gives_back_the_divisor` gave 0 - but the greatest number dividing both 12 and 18 is 6.", outcome.Seen[0].Tried);
        Assert.StartsWith("Called with 5 and 0, `stops_at_no_remainder` stopped with ZeroDivisionError", outcome.Seen[1].Tried);
        Assert.EndsWith(" - but every number divides 0, so the greatest dividing both 5 and 0 is 5.", outcome.Seen[1].Tried);
        Assert.Equal([(2, "algorithm-gcd-wrong"), (8, "algorithm-gcd-wrong")], outcome.Findings.Select(finding => (finding.Line, finding.RuleId)));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>Euclid's loop giving back the divisor once it has become 0, written in JavaScript.</summary>
    [Fact]
    public async Task AJavaScriptGreatestCommonDivisorIsShownTheFirstPairItGetsWrong()
    {
        const string code = "function gcd(a, b) {\n    while (b !== 0) {\n        const held = b;\n        b = a % b;\n        a = held;\n    }\n    return b;\n}\n\nconsole.log(gcd(12, 18));\n";
        if (await TriedAsync("gcd.js", code) is not { } outcome) return;

        Assert.Equal([
            "gcd: Called with 12 and 18, `gcd` gave 0 - but the greatest number dividing both 12 and 18 is 6.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "2 algorithm-gcd-wrong: `gcd` does not give back the greatest common divisor of every pair | Called with 12 and 18, `gcd` gave 0 - but the greatest number dividing both 12 and 18 is 6.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>Euclid's loop giving back the divisor once it has become 0, written in Java.</summary>
    [Fact]
    public async Task AJavaGreatestCommonDivisorIsShownTheFirstPairItGetsWrong()
    {
        const string code = "public class GcdGivesB {\n    static int gcd(int a, int b) {\n        while (b != 0) {\n            int held = b;\n            b = a % b;\n            a = held;\n        }\n        return b;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(gcd(12, 18));\n    }\n}\n";
        if (await TriedAsync("GcdGivesB.java", code) is not { } outcome) return;

        Assert.Equal([
            "GcdGivesB.gcd: Called with 12 and 18, `GcdGivesB.gcd` gave 0 - but the greatest number dividing both 12 and 18 is 6.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "3 algorithm-gcd-wrong: `GcdGivesB.gcd` does not give back the greatest common divisor of every pair | Called with 12 and 18, `GcdGivesB.gcd` gave 0 - but the greatest number dividing both 12 and 18 is 6.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A pair moved on one after the other, which doubles, and a pair started at 2 and 3.</summary>
    [Fact]
    public async Task FibonacciNumbersThatAreNotTheTwoBeforeAddedAreShownWhereTheyGoWrong()
    {
        const string code = "def doubling_fib(n):\n    a, b = 0, 1\n    for _ in range(n):\n        a = b\n        b = a + b\n    return a\n\n\ndef fib_from_two_and_three(n):\n    a, b = 2, 3\n    for _ in range(n):\n        a, b = b, a + b\n    return a\n\n\nprint(doubling_fib(10), fib_from_two_and_three(10))\n";
        if (await TriedAsync("fibs.py", code) is not { } outcome) return;

        Assert.Equal([
            "doubling_fib: Called with 3, `doubling_fib` gave 4 - but it gave 1 for 1 and 2 for 2, and each Fibonacci number is the two before it added, so it should be 3.",
            "fib_from_two_and_three: Called with 1 and with 2, `fib_from_two_and_three` gave 3 and 5 - but the Fibonacci numbers start 0 and 1, 1 and 1, or 1 and 2.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "3 algorithm-fibonacci-wrong: `doubling_fib` does not give back the Fibonacci numbers | Called with 3, `doubling_fib` gave 4 - but it gave 1 for 1 and 2 for 2, and each Fibonacci number is the two before it added, so it should be 3.",
            "11 algorithm-fibonacci-wrong: `fib_from_two_and_three` does not give back the Fibonacci numbers | Called with 1 and with 2, `fib_from_two_and_three` gave 3 and 5 - but the Fibonacci numbers start 0 and 1, 1 and 1, or 1 and 2.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A pair moved on one after the other, written in JavaScript: a = b, then b = a + b.</summary>
    [Fact]
    public async Task JavaScriptFibonacciNumbersThatDoubleAreShownWhereTheyGoWrong()
    {
        const string code = "function fib(n) {\n    let a = 0;\n    let b = 1;\n    for (let i = 0; i < n; i++) {\n        a = b;\n        b = a + b;\n    }\n    return a;\n}\n\nconsole.log(fib(10));\n";
        if (await TriedAsync("fib.js", code) is not { } outcome) return;

        Assert.Equal([
            "fib: Called with 3, `fib` gave 4 - but it gave 1 for 1 and 2 for 2, and each Fibonacci number is the two before it added, so it should be 3.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "4 algorithm-fibonacci-wrong: `fib` does not give back the Fibonacci numbers | Called with 3, `fib` gave 4 - but it gave 1 for 1 and 2 for 2, and each Fibonacci number is the two before it added, so it should be 3.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>Fibonacci numbers in Java calling themselves that stop only at 1: given 2, fib(0) calls itself until the stack is full.</summary>
    [Fact]
    public async Task JavaFibonacciNumbersThatNeverStopAreShownWhereTheyGoWrong()
    {
        const string code = "public class FibCalls {\n    static int fib(int n) {\n        if (n == 1) {\n            return 1;\n        }\n        return fib(n - 1) + fib(n - 2);\n    }\n\n    public static void main(String[] args) {\n        System.out.println(fib(10));\n    }\n}\n";
        if (await TriedAsync("FibCalls.java", code) is not { } outcome) return;

        Assert.Equal([
            "FibCalls.fib: Called with 2, `FibCalls.fib` stopped with StackOverflowError.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "6 algorithm-fibonacci-wrong: `FibCalls.fib` does not give back the Fibonacci numbers | Called with 2, `FibCalls.fib` stopped with StackOverflowError.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A merge that takes in neither list's rest, one that takes in only the first's, and one that takes the larger item.</summary>
    [Fact]
    public async Task AMergeIsShownTheShortestPairItGetsWrong()
    {
        const string code = "def merge_without_the_rest(first, second):\n    result = []\n    i = 0\n    j = 0\n    while i < len(first) and j < len(second):\n        if first[i] <= second[j]:\n            result.append(first[i])\n            i += 1\n        else:\n            result.append(second[j])\n            j += 1\n    return result\n\n\ndef merge_with_the_first_rest_only(first, second):\n    result = []\n    i = 0\n    j = 0\n    while i < len(first) and j < len(second):\n        if first[i] < second[j]:\n            result.append(first[i])\n            i += 1\n        else:\n            result.append(second[j])\n            j += 1\n    result.extend(first[i:])\n    return result\n\n\ndef merge_taking_the_larger(first, second):\n    result = []\n    i = 0\n    j = 0\n    while i < len(first) and j < len(second):\n        if first[i] > second[j]:\n            result.append(first[i])\n            i += 1\n        else:\n            result.append(second[j])\n            j += 1\n    result.extend(first[i:])\n    result.extend(second[j:])\n    return result\n\n\nprint(merge_without_the_rest([1, 3], [2]))\n";
        if (await TriedAsync("merges.py", code) is not { } outcome) return;

        Assert.Equal([
            "merge_without_the_rest: Called with [1] and [], `merge_without_the_rest` gave back [] - [1] is left out; merged in order, it is [1].",
            "merge_with_the_first_rest_only: Called with [] and [2], `merge_with_the_first_rest_only` gave back [] - [2] is left out; merged in order, it is [2].",
            "merge_taking_the_larger: Called with [1] and [2], `merge_taking_the_larger` gave back [2, 1]; merged in order, it is [1, 2].",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "5 algorithm-merge-wrong: `merge_without_the_rest` does not merge every pair of lists | Called with [1] and [], `merge_without_the_rest` gave back [] - [1] is left out; merged in order, it is [1].",
            "19 algorithm-merge-wrong: `merge_with_the_first_rest_only` does not merge every pair of lists | Called with [] and [2], `merge_with_the_first_rest_only` gave back [] - [2] is left out; merged in order, it is [2].",
            "34 algorithm-merge-wrong: `merge_taking_the_larger` does not merge every pair of lists | Called with [1] and [2], `merge_taking_the_larger` gave back [2, 1]; merged in order, it is [1, 2].",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A merge written in JavaScript that takes in neither list's rest.</summary>
    [Fact]
    public async Task AJavaScriptMergeThatLeavesOutTheRestIsShownTheShortestPairItGetsWrong()
    {
        const string code = "function merge(first, second) {\n    const result = [];\n    let i = 0;\n    let j = 0;\n    while (i < first.length && j < second.length) {\n        if (first[i] <= second[j]) {\n            result.push(first[i]);\n            i++;\n        } else {\n            result.push(second[j]);\n            j++;\n        }\n    }\n    return result;\n}\n\nconsole.log(merge([1, 3], [2]));\n";
        if (await TriedAsync("merge.js", code) is not { } outcome) return;

        Assert.Equal([
            "merge: Called with [1] and [], `merge` gave back [] - [1] is left out; merged in order, it is [1].",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "5 algorithm-merge-wrong: `merge` does not merge every pair of lists | Called with [1] and [], `merge` gave back [] - [1] is left out; merged in order, it is [1].",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A merge written in Java into an array as long as both lists, whose places for the rest are left at 0.</summary>
    [Fact]
    public async Task AJavaMergeThatLeavesOutTheRestIsShownTheShortestPairItGetsWrong()
    {
        const string code = "public class MergeNoRest {\n    static int[] merge(int[] first, int[] second) {\n        int[] result = new int[first.length + second.length];\n        int i = 0, j = 0, k = 0;\n        while (i < first.length && j < second.length) {\n            if (first[i] <= second[j]) {\n                result[k++] = first[i++];\n            } else {\n                result[k++] = second[j++];\n            }\n        }\n        return result;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(java.util.Arrays.toString(merge(new int[] {1, 3}, new int[] {2})));\n    }\n}\n";
        if (await TriedAsync("MergeNoRest.java", code) is not { } outcome) return;

        Assert.Equal([
            "MergeNoRest.merge: Called with [1] and [], `MergeNoRest.merge` gave back [0] - [1] is left out; merged in order, it is [1].",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "5 algorithm-merge-wrong: `MergeNoRest.merge` does not merge every pair of lists | Called with [1] and [], `MergeNoRest.merge` gave back [0] - [1] is left out; merged in order, it is [1].",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A test of order whose loop stops a pair short, and one that says yes at the first pair in order.</summary>
    [Fact]
    public async Task ATestOfOrderIsShownTheShortestListItGetsWrong()
    {
        const string code = "def sorted_short_of_the_end(items):\n    for i in range(len(items) - 2):\n        if items[i] > items[i + 1]:\n            return False\n    return True\n\n\ndef sorted_at_the_first_pair(items):\n    for i in range(len(items) - 1):\n        if items[i] > items[i + 1]:\n            return False\n        else:\n            return True\n    return True\n\n\nprint(sorted_short_of_the_end([1, 2, 4, 3]))\n";
        if (await TriedAsync("order_tests.py", code) is not { } outcome) return;

        Assert.Equal([
            "sorted_short_of_the_end: Called with [2, 1], `sorted_short_of_the_end` gave True - but in it 2 is followed by 1, and 2 > 1 is what its own test calls out of order.",
            "sorted_at_the_first_pair: Called with [1, 3, 2], `sorted_at_the_first_pair` gave True - but in it 3 is followed by 2, and 3 > 2 is what its own test calls out of order.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "2 algorithm-in-order-wrong: `sorted_short_of_the_end` does not say rightly which lists are in order | Called with [2, 1], `sorted_short_of_the_end` gave True - but in it 2 is followed by 1, and 2 > 1 is what its own test calls out of order.",
            "9 algorithm-in-order-wrong: `sorted_at_the_first_pair` does not say rightly which lists are in order | Called with [1, 3, 2], `sorted_at_the_first_pair` gave True - but in it 3 is followed by 2, and 3 > 2 is what its own test calls out of order.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A test of order written in JavaScript whose loop, comparing each item with the one before, starts at 1 and stops a pair short.</summary>
    [Fact]
    public async Task AJavaScriptTestOfOrderIsShownTheShortestListItGetsWrong()
    {
        const string code = "function isSorted(items) {\n    for (let i = 1; i < items.length - 1; i++) {\n        if (items[i - 1] > items[i]) {\n            return false;\n        }\n    }\n    return true;\n}\n\nconsole.log(isSorted([1, 2, 4, 3]));\n";
        if (await TriedAsync("order.js", code) is not { } outcome) return;

        Assert.Equal([
            "isSorted: Called with [2, 1], `isSorted` gave true - but in it 2 is followed by 1, and 2 > 1 is what its own test calls out of order.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "2 algorithm-in-order-wrong: `isSorted` does not say rightly which lists are in order | Called with [2, 1], `isSorted` gave true - but in it 2 is followed by 1, and 2 > 1 is what its own test calls out of order.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A test of order written in Java whose loop stops a pair short.</summary>
    [Fact]
    public async Task AJavaTestOfOrderIsShownTheShortestListItGetsWrong()
    {
        const string code = "public class OrderShort {\n    static boolean isSorted(int[] items) {\n        for (int i = 0; i < items.length - 2; i++) {\n            if (items[i] > items[i + 1]) {\n                return false;\n            }\n        }\n        return true;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(isSorted(new int[] {1, 2, 4, 3}));\n    }\n}\n";
        if (await TriedAsync("OrderShort.java", code) is not { } outcome) return;

        Assert.Equal([
            "OrderShort.isSorted: Called with [2, 1], `OrderShort.isSorted` gave true - but in it 2 is followed by 1, and 2 > 1 is what its own test calls out of order.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "3 algorithm-in-order-wrong: `OrderShort.isSorted` does not say rightly which lists are in order | Called with [2, 1], `OrderShort.isSorted` gave true - but in it 2 is followed by 1, and 2 > 1 is what its own test calls out of order.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A palindrome test that works, and ones that stop a pair short of the middle, read past the end of the word and say yes at the first pair that matches.</summary>
    [Fact]
    public async Task APalindromeTestIsShownTheShortestWordItGetsWrong()
    {
        const string code = "def is_palindrome(word):\n    left = 0\n    right = len(word) - 1\n    while left < right:\n        if word[left] != word[right]:\n            return False\n        left += 1\n        right -= 1\n    return True\n\n\ndef palindrome_short_of_the_middle(word):\n    for i in range(len(word) // 2 - 1):\n        if word[i] != word[len(word) - 1 - i]:\n            return False\n    return True\n\n\ndef palindrome_past_the_end(word):\n    for i in range(len(word) // 2):\n        if word[i] != word[len(word) - i]:\n            return False\n    return True\n\n\ndef palindrome_at_the_first_pair(word):\n    for i in range(len(word) // 2):\n        if word[i] != word[len(word) - 1 - i]:\n            return False\n        else:\n            return True\n    return True\n\n\nprint(is_palindrome(\"level\"))\n";
        if (await TriedAsync("palindromes.py", code) is not { } outcome) return;

        Assert.Equal([
            "is_palindrome: Called with 12 words of FixFinder's own - of one letter up to seven, palindromes of odd and even lengths, and words that differ at their ends or only in towards the middle - it said which were palindromes, every one right.",
            "palindrome_short_of_the_middle: Called with 'ab', `palindrome_short_of_the_middle` gave True - but 'ab' backwards is 'ba', so it is not a palindrome.",
            "palindrome_past_the_end: Called with 'aa', `palindrome_past_the_end` stopped with IndexError: string index out of range.",
            "palindrome_at_the_first_pair: Called with 'abca', `palindrome_at_the_first_pair` gave True - but 'abca' backwards is 'acba', so it is not a palindrome.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "13 algorithm-palindrome-wrong: `palindrome_short_of_the_middle` does not say rightly which words are palindromes | Called with 'ab', `palindrome_short_of_the_middle` gave True - but 'ab' backwards is 'ba', so it is not a palindrome.",
            "20 algorithm-palindrome-wrong: `palindrome_past_the_end` does not say rightly which words are palindromes | Called with 'aa', `palindrome_past_the_end` stopped with IndexError: string index out of range.",
            "27 algorithm-palindrome-wrong: `palindrome_at_the_first_pair` does not say rightly which words are palindromes | Called with 'abca', `palindrome_at_the_first_pair` gave True - but 'abca' backwards is 'acba', so it is not a palindrome.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>A palindrome test written for a list it adds to: given a word, it stops with no append, which is not judged.</summary>
    [Fact]
    public async Task APalindromeTestThatStopsOnAWordForWantOfAListIsNotJudged()
    {
        const string code = "def is_palindrome(items):\n    items.append(None)\n    items.pop()\n    for i in range(len(items) // 2):\n        if items[i] != items[len(items) - 1 - i]:\n            return False\n    return True\n\n\nprint(is_palindrome([1, 2, 1]))\n";
        if (await TriedAsync("palindrome_list.py", code) is not { } outcome) return;

        Assert.Equal([
            "is_palindrome: not tried",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Empty(outcome.Findings);
    }

    /// <summary>A palindrome test written in JavaScript that works, and one that reads past the end of the word, where JavaScript gives undefined.</summary>
    [Fact]
    public async Task AJavaScriptPalindromeTestIsShownTheShortestWordItGetsWrong()
    {
        const string code = "function isPalindrome(word) {\n    let left = 0;\n    let right = word.length - 1;\n    while (left < right) {\n        if (word[left] !== word[right]) {\n            return false;\n        }\n        left++;\n        right--;\n    }\n    return true;\n}\n\nfunction isPalindromePastTheEnd(word) {\n    for (let i = 0; i < word.length / 2; i++) {\n        if (word[i] !== word[word.length - i]) {\n            return false;\n        }\n    }\n    return true;\n}\n\nconsole.log(isPalindrome(\"level\"), isPalindromePastTheEnd(\"level\"));\n";
        if (await TriedAsync("palindromes.js", code) is not { } outcome) return;

        Assert.Equal([
            "isPalindrome: Called with 12 words of FixFinder's own - of one letter up to seven, palindromes of odd and even lengths, and words that differ at their ends or only in towards the middle - it said which were palindromes, every one right.",
            "isPalindromePastTheEnd: Called with \"a\", `isPalindromePastTheEnd` gave false - but \"a\" backwards is \"a\", the same, so it is a palindrome.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "15 algorithm-palindrome-wrong: `isPalindromePastTheEnd` does not say rightly which words are palindromes | Called with \"a\", `isPalindromePastTheEnd` gave false - but \"a\" backwards is \"a\", the same, so it is a palindrome.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>Palindrome tests written in Java over a String: one that reads past its end and one that says yes at the first pair that matches.</summary>
    [Fact]
    public async Task AJavaPalindromeTestIsShownTheShortestWordItGetsWrong()
    {
        const string code = "public class Palindromes {\n    static boolean pastTheEnd(String word) {\n        int left = 0;\n        int right = word.length();\n        while (left < right) {\n            if (word.charAt(left) != word.charAt(right)) {\n                return false;\n            }\n            left++;\n            right--;\n        }\n        return true;\n    }\n\n    static boolean atTheFirstPair(String word) {\n        for (int i = 0; i < word.length() / 2; i++) {\n            if (word.charAt(i) != word.charAt(word.length() - 1 - i)) {\n                return false;\n            } else {\n                return true;\n            }\n        }\n        return true;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(pastTheEnd(\"level\") + \" \" + atTheFirstPair(\"level\"));\n    }\n}\n";
        if (await TriedAsync("Palindromes.java", code) is not { } outcome) return;

        Assert.Equal([
            "Palindromes.pastTheEnd: Called with \"a\", `Palindromes.pastTheEnd` stopped with StringIndexOutOfBoundsException: Index 1 out of bounds for length 1.",
            "Palindromes.atTheFirstPair: Called with \"abca\", `Palindromes.atTheFirstPair` gave true - but \"abca\" backwards is \"acba\", so it is not a palindrome.",
        ], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal([
            "5 algorithm-palindrome-wrong: `Palindromes.pastTheEnd` does not say rightly which words are palindromes | Called with \"a\", `Palindromes.pastTheEnd` stopped with StringIndexOutOfBoundsException: Index 1 out of bounds for length 1.",
            "16 algorithm-palindrome-wrong: `Palindromes.atTheFirstPair` does not say rightly which words are palindromes | Called with \"abca\", `Palindromes.atTheFirstPair` gave true - but \"abca\" backwards is \"acba\", so it is not a palindrome.",
        ], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
        Assert.All(outcome.Findings, finding => Assert.Equal(Confidence.Certain, finding.Confidence));
    }

    /// <summary>
    /// Pairs named to undo each other, each called with words or numbers of FixFinder's own and the other with what it gave
    /// back: a Caesar cipher with a number shift, a Vigenère cipher with a word key, a run-length code and Roman numerals,
    /// whose reader comes first, that turn every input back; a run-length decoder reading each count as one digit, a
    /// decrypt copied from encrypt that still adds the shift, and a Roman numeral reader that never takes away; a pair that
    /// prints and gives back nothing, one that gives back capitals, one that gives back bytes and, in JavaScript, one
    /// written for objects, none of them judged; and in JavaScript and Java a Caesar cipher that turns every input back,
    /// and one whose decrypt takes a remainder below zero.
    /// </summary>
    [Theory]
    [InlineData("caesar.py", "def encrypt(text, shift):\n    letters = []\n    for letter in text:\n        letters.append(chr((ord(letter) - ord(\"a\") + shift) % 26 + ord(\"a\")))\n    return \"\".join(letters)\n\n\ndef decrypt(text, shift):\n    letters = []\n    for letter in text:\n        letters.append(chr((ord(letter) - ord(\"a\") - shift) % 26 + ord(\"a\")))\n    return \"\".join(letters)\n\n\nprint(decrypt(encrypt(\"hello\", 3), 3))\n",
        "encrypt: Called with 9 words of FixFinder's own, of lower-case letters alone, from one letter to the whole alphabet, each with the key 3 and then with 25 - 18 calls - `encrypt` gave back something every time, and `decrypt` turned each back into what it was.",
        null)]
    [InlineData("vigenere.py", "def encode(text, key):\n    letters = []\n    for position, letter in enumerate(text):\n        shift = ord(key[position % len(key)]) - ord(\"a\")\n        letters.append(chr((ord(letter) - ord(\"a\") + shift) % 26 + ord(\"a\")))\n    return \"\".join(letters)\n\n\ndef decode(text, key):\n    letters = []\n    for position, letter in enumerate(text):\n        shift = ord(key[position % len(key)]) - ord(\"a\")\n        letters.append(chr((ord(letter) - ord(\"a\") - shift) % 26 + ord(\"a\")))\n    return \"\".join(letters)\n\n\nprint(decode(encode(\"hello\", \"key\"), \"key\"))\n",
        "encode: Called with 9 words of FixFinder's own, of lower-case letters alone, from one letter to the whole alphabet, each with the word key as the key - 9 calls - `encode` gave back something every time, and `decode` turned each back into what it was.",
        null)]
    [InlineData("rle.py", "def compress(text):\n    result = \"\"\n    position = 0\n    while position < len(text):\n        count = 1\n        while position + count < len(text) and text[position + count] == text[position]:\n            count += 1\n        result += str(count) + text[position]\n        position += count\n    return result\n\n\ndef decompress(text):\n    result = \"\"\n    digits = \"\"\n    for character in text:\n        if character.isdigit():\n            digits += character\n        else:\n            result += character * int(digits)\n            digits = \"\"\n    return result\n\n\nprint(decompress(compress(\"aaabccdddd\")))\n",
        "compress: Called with 9 words of FixFinder's own, of lower-case letters alone, from one letter to the whole alphabet - 9 calls - `compress` gave back something every time, and `decompress` turned each back into what it was.",
        null)]
    [InlineData("roman.py", "VALUES = [(1000, \"M\"), (900, \"CM\"), (500, \"D\"), (400, \"CD\"), (100, \"C\"), (90, \"XC\"),\n          (50, \"L\"), (40, \"XL\"), (10, \"X\"), (9, \"IX\"), (5, \"V\"), (4, \"IV\"), (1, \"I\")]\nLETTERS = {\"M\": 1000, \"D\": 500, \"C\": 100, \"L\": 50, \"X\": 10, \"V\": 5, \"I\": 1}\n\n\ndef roman_to_int(text):\n    total = 0\n    for position, letter in enumerate(text):\n        value = LETTERS[letter]\n        if position + 1 < len(text) and LETTERS[text[position + 1]] > value:\n            total -= value\n        else:\n            total += value\n    return total\n\n\ndef int_to_roman(number):\n    result = \"\"\n    for value, letters in VALUES:\n        while number >= value:\n            result += letters\n            number -= value\n    return result\n\n\nprint(roman_to_int(int_to_roman(1994)))\n",
        "roman_to_int: Called with 10 numbers of FixFinder's own, from 1 to 3999 - 10 calls - `int_to_roman` gave back something every time, and `roman_to_int` turned each back into what it was.",
        null)]
    [InlineData("rle_one_digit.py", "def compress(text):\n    result = \"\"\n    position = 0\n    while position < len(text):\n        count = 1\n        while position + count < len(text) and text[position + count] == text[position]:\n            count += 1\n        result += str(count) + text[position]\n        position += count\n    return result\n\n\ndef decompress(text):\n    result = \"\"\n    for position in range(0, len(text), 2):\n        result += text[position + 1] * int(text[position])\n    return result\n\n\nprint(decompress(compress(\"aaabccdddd\")))\n",
        "compress: Called with 'aaaaaaaaaaaab', `compress` gave back '12a1b'; given that, `decompress` stopped with ValueError: invalid literal for int() with base 10: 'a'.",
        "1 algorithm-round-trip-wrong: `compress` and `decompress` do not undo each other | Called with 'aaaaaaaaaaaab', `compress` gave back '12a1b'; given that, `decompress` stopped with ValueError: invalid literal for int() with base 10: 'a'.")]
    [InlineData("caesar_copied.py", "def encrypt(text, shift):\n    letters = []\n    for letter in text:\n        letters.append(chr((ord(letter) - ord(\"a\") + shift) % 26 + ord(\"a\")))\n    return \"\".join(letters)\n\n\ndef decrypt(text, shift):\n    letters = []\n    for letter in text:\n        letters.append(chr((ord(letter) - ord(\"a\") + shift) % 26 + ord(\"a\")))\n    return \"\".join(letters)\n\n\nprint(decrypt(encrypt(\"hello\", 3), 3))\n",
        "encrypt: Called with 'a' and 3, `encrypt` gave back 'd', and given that and 3, `decrypt` gave back 'g' - not 'a', what `encrypt` was given.",
        "1 algorithm-round-trip-wrong: `encrypt` and `decrypt` do not undo each other | Called with 'a' and 3, `encrypt` gave back 'd', and given that and 3, `decrypt` gave back 'g' - not 'a', what `encrypt` was given.")]
    [InlineData("roman_adds.py", "VALUES = [(1000, \"M\"), (900, \"CM\"), (500, \"D\"), (400, \"CD\"), (100, \"C\"), (90, \"XC\"),\n          (50, \"L\"), (40, \"XL\"), (10, \"X\"), (9, \"IX\"), (5, \"V\"), (4, \"IV\"), (1, \"I\")]\nLETTERS = {\"M\": 1000, \"D\": 500, \"C\": 100, \"L\": 50, \"X\": 10, \"V\": 5, \"I\": 1}\n\n\ndef roman_to_int(text):\n    total = 0\n    for position, letter in enumerate(text):\n        value = LETTERS[letter]\n        total += value\n    return total\n\n\ndef int_to_roman(number):\n    result = \"\"\n    for value, letters in VALUES:\n        while number >= value:\n            result += letters\n            number -= value\n    return result\n\n\nprint(roman_to_int(int_to_roman(1994)))\n",
        "roman_to_int: Called with 4, `int_to_roman` gave back 'IV', and given that, `roman_to_int` gave back 6 - not 4, what `int_to_roman` was given.",
        "6 algorithm-round-trip-wrong: `roman_to_int` and `int_to_roman` do not undo each other | Called with 4, `int_to_roman` gave back 'IV', and given that, `roman_to_int` gave back 6 - not 4, what `int_to_roman` was given.")]
    [InlineData("printed.py", "def encrypt(text, shift):\n    print(\"\".join(chr((ord(letter) - ord(\"a\") + shift) % 26 + ord(\"a\")) for letter in text))\n\n\ndef decrypt(text, shift):\n    print(\"\".join(chr((ord(letter) - ord(\"a\") - shift) % 26 + ord(\"a\")) for letter in text))\n\n\nencrypt(\"hello\", 3)\n",
        "encrypt: not tried",
        null)]
    [InlineData("capitals.py", "def encode(text):\n    return text.upper()\n\n\ndef decode(text):\n    return text.upper()\n\n\nprint(decode(encode(\"hello\")))\n",
        "encode: not tried",
        null)]
    [InlineData("utf8_bytes.py", "def encode(text):\n    return text.encode(\"utf-8\")\n\n\ndef decode(data):\n    return data.decode(\"utf-8\")\n\n\nprint(decode(encode(\"hello\")))\n",
        "encode: not tried",
        null)]
    [InlineData("pointObjects.js", "function pointToJSON(point) {\n    return { x: point.x, y: point.y };\n}\n\nfunction pointFromJSON(object) {\n    return { x: Number(object.x), y: Number(object.y) };\n}\n\nconsole.log(pointFromJSON(pointToJSON({ x: 1, y: 2 })));\n",
        "pointToJSON: not tried",
        null)]
    [InlineData("caesarRight.js", "function encryptMessage(text, shift) {\n    let result = \"\";\n    for (const letter of text) {\n        result += String.fromCharCode((letter.charCodeAt(0) - 97 + shift) % 26 + 97);\n    }\n    return result;\n}\n\nfunction decryptMessage(text, shift) {\n    let result = \"\";\n    for (const letter of text) {\n        result += String.fromCharCode((letter.charCodeAt(0) - 97 - shift + 26) % 26 + 97);\n    }\n    return result;\n}\n\nconsole.log(decryptMessage(encryptMessage(\"hello\", 3), 3));\n",
        "encryptMessage: Called with 9 words of FixFinder's own, of lower-case letters alone, from one letter to the whole alphabet, each with the key 3 and then with 25 - 18 calls - `encryptMessage` gave back something every time, and `decryptMessage` turned each back into what it was.",
        null)]
    [InlineData("caesarBelowZero.js", "function encryptMessage(text, shift) {\n    let result = \"\";\n    for (const letter of text) {\n        result += String.fromCharCode((letter.charCodeAt(0) - 97 + shift) % 26 + 97);\n    }\n    return result;\n}\n\nfunction decryptMessage(text, shift) {\n    let result = \"\";\n    for (const letter of text) {\n        result += String.fromCharCode((letter.charCodeAt(0) - 97 - shift) % 26 + 97);\n    }\n    return result;\n}\n\nconsole.log(decryptMessage(encryptMessage(\"hello\", 3), 3));\n",
        "encryptMessage: Called with \"abc\" and 25, `encryptMessage` gave back \"zab\", and given that and 25, `decryptMessage` gave back \"aHI\" - not \"abc\", what `encryptMessage` was given.",
        "1 algorithm-round-trip-wrong: `encryptMessage` and `decryptMessage` do not undo each other | Called with \"abc\" and 25, `encryptMessage` gave back \"zab\", and given that and 25, `decryptMessage` gave back \"aHI\" - not \"abc\", what `encryptMessage` was given.")]
    [InlineData("Caesar.java", "public class Caesar {\n    static String encrypt(String text, int shift) {\n        StringBuilder result = new StringBuilder();\n        for (char letter : text.toCharArray()) {\n            result.append((char) ((letter - 'a' + shift) % 26 + 'a'));\n        }\n        return result.toString();\n    }\n\n    static String decrypt(String text, int shift) {\n        StringBuilder result = new StringBuilder();\n        for (char letter : text.toCharArray()) {\n            result.append((char) ((letter - 'a' - shift + 26) % 26 + 'a'));\n        }\n        return result.toString();\n    }\n\n    public static void main(String[] args) {\n        System.out.println(decrypt(encrypt(\"hello\", 3), 3));\n    }\n}\n",
        "Caesar.encrypt: Called with 9 words of FixFinder's own, of lower-case letters alone, from one letter to the whole alphabet, each with the key 3 and then with 25 - 18 calls - `Caesar.encrypt` gave back something every time, and `Caesar.decrypt` turned each back into what it was.",
        null)]
    [InlineData("CaesarBelowZero.java", "public class CaesarBelowZero {\n    static String encrypt(String text, int shift) {\n        StringBuilder result = new StringBuilder();\n        for (char letter : text.toCharArray()) {\n            result.append((char) ((letter - 'a' + shift) % 26 + 'a'));\n        }\n        return result.toString();\n    }\n\n    static String decrypt(String text, int shift) {\n        StringBuilder result = new StringBuilder();\n        for (char letter : text.toCharArray()) {\n            result.append((char) ((letter - 'a' - shift) % 26 + 'a'));\n        }\n        return result.toString();\n    }\n\n    public static void main(String[] args) {\n        System.out.println(decrypt(encrypt(\"hello\", 3), 3));\n    }\n}\n",
        "CaesarBelowZero.encrypt: Called with \"abc\" and 25, `CaesarBelowZero.encrypt` gave back \"zab\", and given that and 25, `CaesarBelowZero.decrypt` gave back \"aHI\" - not \"abc\", what `CaesarBelowZero.encrypt` was given.",
        "2 algorithm-round-trip-wrong: `CaesarBelowZero.encrypt` and `CaesarBelowZero.decrypt` do not undo each other | Called with \"abc\" and 25, `CaesarBelowZero.encrypt` gave back \"zab\", and given that and 25, `CaesarBelowZero.decrypt` gave back \"aHI\" - not \"abc\", what `CaesarBelowZero.encrypt` was given.")]
    public async Task APairNamedToUndoEachOtherIsHeldToIt(string fileName, string code, string tried, string? found)
    {
        if (await TriedAsync(fileName, code) is not { } outcome) return;

        Assert.Equal([tried], outcome.Seen.Select(seen => $"{seen.Function}: {seen.Tried ?? "not tried"}"));
        Assert.Equal(found is null ? [] : [found], outcome.Findings.Select(finding => $"{finding.Line} {finding.RuleId}: {finding.Title} | {finding.Found}"));
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
