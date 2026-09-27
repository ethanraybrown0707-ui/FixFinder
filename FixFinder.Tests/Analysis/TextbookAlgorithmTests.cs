using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;
using FixFinder.Core.Logic;
using FixFinder.Core.LocalFixes;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// The algorithms and data structures a computer science degree teaches, written correctly, and read by the analysis
/// engine - which has to find nothing wrong with any of them.
/// </summary>
/// <remarks>
/// The coursework tests check that a mistake is found and fixed. These check the other half, which matters as much for
/// a tool students run on their own work: that correct work is left alone. A student who hands in a right binary
/// search and is told it is wrong has been done a disservice, and learns to ignore the tool. Every program here is the
/// version found in a textbook, and each is correct, so any error or warning on any of them is a false alarm.
/// <para>
/// Suggestions are allowed. A suggestion is about how much work a program does, not whether it is right, and is never a
/// claim of a mistake.
/// </para>
/// </remarks>
public class TextbookAlgorithmTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string> Python => new()
    {
        {
            "binary search", """
            def binary_search(items, target):
                low, high = 0, len(items) - 1
                while low <= high:
                    middle = (low + high) // 2
                    if items[middle] == target:
                        return middle
                    if items[middle] < target:
                        low = middle + 1
                    else:
                        high = middle - 1
                return -1

            print(binary_search([1, 3, 5, 7, 9], 7))
            """
        },
        {
            "insertion sort", """
            def insertion_sort(items):
                for i in range(1, len(items)):
                    key = items[i]
                    j = i - 1
                    while j >= 0 and items[j] > key:
                        items[j + 1] = items[j]
                        j -= 1
                    items[j + 1] = key
                return items

            print(insertion_sort([5, 2, 9, 1]))
            """
        },
        {
            "merge sort", """
            def merge(left, right):
                merged = []
                i = j = 0
                while i < len(left) and j < len(right):
                    if left[i] <= right[j]:
                        merged.append(left[i])
                        i += 1
                    else:
                        merged.append(right[j])
                        j += 1
                merged.extend(left[i:])
                merged.extend(right[j:])
                return merged

            def merge_sort(items):
                if len(items) <= 1:
                    return items
                middle = len(items) // 2
                return merge(merge_sort(items[:middle]), merge_sort(items[middle:]))

            print(merge_sort([5, 2, 9, 1, 7]))
            """
        },
        {
            "quicksort", """
            def partition(items, low, high):
                pivot = items[high]
                i = low - 1
                for j in range(low, high):
                    if items[j] <= pivot:
                        i += 1
                        items[i], items[j] = items[j], items[i]
                items[i + 1], items[high] = items[high], items[i + 1]
                return i + 1

            def quicksort(items, low, high):
                if low < high:
                    split = partition(items, low, high)
                    quicksort(items, low, split - 1)
                    quicksort(items, split + 1, high)

            numbers = [5, 2, 9, 1, 7]
            quicksort(numbers, 0, len(numbers) - 1)
            print(numbers)
            """
        },
        {
            "breadth-first search", """
            from collections import deque

            def breadth_first(graph, start):
                seen = {start}
                queue = deque([start])
                order = []
                while queue:
                    node = queue.popleft()
                    order.append(node)
                    for neighbour in graph[node]:
                        if neighbour not in seen:
                            seen.add(neighbour)
                            queue.append(neighbour)
                return order

            print(breadth_first({"a": ["b", "c"], "b": ["d"], "c": ["d"], "d": []}, "a"))
            """
        },
        {
            "depth-first search", """
            def depth_first(graph, node, seen=None):
                if seen is None:
                    seen = set()
                seen.add(node)
                for neighbour in graph[node]:
                    if neighbour not in seen:
                        depth_first(graph, neighbour, seen)
                return seen

            print(sorted(depth_first({"a": ["b"], "b": ["c"], "c": []}, "a")))
            """
        },
        {
            "reverse a linked list", """
            class Node:
                def __init__(self, value, next_node=None):
                    self.value = value
                    self.next = next_node

            def reverse(head):
                previous = None
                current = head
                while current is not None:
                    following = current.next
                    current.next = previous
                    previous = current
                    current = following
                return previous

            head = reverse(Node(1, Node(2, Node(3))))
            while head is not None:
                print(head.value)
                head = head.next
            """
        },
        {
            "stack", """
            class Stack:
                def __init__(self):
                    self.items = []

                def push(self, item):
                    self.items.append(item)

                def pop(self):
                    if not self.items:
                        raise IndexError("pop from an empty stack")
                    return self.items.pop()

                def is_empty(self):
                    return len(self.items) == 0

            stack = Stack()
            stack.push(1)
            stack.push(2)
            print(stack.pop())
            """
        },
        {
            "binary search tree", """
            class Tree:
                def __init__(self, value):
                    self.value = value
                    self.left = None
                    self.right = None

            def insert(root, value):
                if root is None:
                    return Tree(value)
                if value < root.value:
                    root.left = insert(root.left, value)
                else:
                    root.right = insert(root.right, value)
                return root

            def contains(root, value):
                while root is not None:
                    if value == root.value:
                        return True
                    root = root.left if value < root.value else root.right
                return False

            root = None
            for value in [5, 3, 8, 1]:
                root = insert(root, value)
            print(contains(root, 8))
            """
        },
        {
            "memoised fibonacci", """
            def fibonacci(n, known={0: 0, 1: 1}):
                if n not in known:
                    known[n] = fibonacci(n - 1) + fibonacci(n - 2)
                return known[n]

            print(fibonacci(30))
            """
        },
        {
            "greatest common divisor", """
            def gcd(a, b):
                while b != 0:
                    a, b = b, a % b
                return a

            print(gcd(48, 18))
            """
        },
        {
            "average with an empty guard", """
            def average(marks):
                if len(marks) == 0:
                    return 0
                return sum(marks) / len(marks)

            print(average([70, 45, 90]))
            """
        },
        {
            "two sum", """
            def two_sum(numbers, target):
                seen = {}
                for index, number in enumerate(numbers):
                    wanted = target - number
                    if wanted in seen:
                        return seen[wanted], index
                    seen[number] = index
                return None

            print(two_sum([2, 7, 11, 15], 9))
            """
        },
        {
            "matrix multiplication", """
            def multiply(a, b):
                rows, inner, columns = len(a), len(b), len(b[0])
                result = [[0] * columns for _ in range(rows)]
                for i in range(rows):
                    for j in range(columns):
                        for k in range(inner):
                            result[i][j] += a[i][k] * b[k][j]
                return result

            print(multiply([[1, 2], [3, 4]], [[5, 6], [7, 8]]))
            """
        },
    };

    public static TheoryData<string, string> C => new()
    {
        {
            "binary search", """
            #include <stdio.h>

            int binary_search(const int *items, int count, int target) {
                int low = 0, high = count - 1;
                while (low <= high) {
                    int middle = low + (high - low) / 2;
                    if (items[middle] == target) return middle;
                    if (items[middle] < target) low = middle + 1;
                    else high = middle - 1;
                }
                return -1;
            }

            int main(void) {
                int items[] = {1, 3, 5, 7, 9};
                printf("%d\n", binary_search(items, 5, 7));
                return 0;
            }
            """
        },
        {
            "insertion sort", """
            #include <stdio.h>

            void insertion_sort(int *items, int count) {
                for (int i = 1; i < count; i++) {
                    int key = items[i];
                    int j = i - 1;
                    while (j >= 0 && items[j] > key) {
                        items[j + 1] = items[j];
                        j--;
                    }
                    items[j + 1] = key;
                }
            }

            int main(void) {
                int items[] = {5, 2, 9, 1};
                insertion_sort(items, 4);
                for (int i = 0; i < 4; i++) printf("%d ", items[i]);
                return 0;
            }
            """
        },
        {
            "linked list built and freed", """
            #include <stdio.h>
            #include <stdlib.h>

            struct node { int value; struct node *next; };

            int main(void) {
                struct node *head = NULL;
                for (int i = 0; i < 3; i++) {
                    struct node *made = malloc(sizeof *made);
                    if (made == NULL) return 1;
                    made->value = i;
                    made->next = head;
                    head = made;
                }
                while (head != NULL) {
                    struct node *next = head->next;
                    printf("%d\n", head->value);
                    free(head);
                    head = next;
                }
                return 0;
            }
            """
        },
        {
            "string length", """
            #include <stdio.h>

            int length(const char *text) {
                int count = 0;
                while (text[count] != '\0') count++;
                return count;
            }

            int main(void) {
                printf("%d\n", length("hello"));
                return 0;
            }
            """
        },
        {
            "bounded array stack", """
            #include <stdio.h>

            #define CAPACITY 8

            struct stack { int items[CAPACITY]; int size; };

            int push(struct stack *s, int value) {
                if (s->size == CAPACITY) return 0;
                s->items[s->size++] = value;
                return 1;
            }

            int pop(struct stack *s, int *value) {
                if (s->size == 0) return 0;
                *value = s->items[--s->size];
                return 1;
            }

            int main(void) {
                struct stack s = { .size = 0 };
                int value = 0;
                push(&s, 4);
                if (pop(&s, &value)) printf("%d\n", value);
                return 0;
            }
            """
        },
        {
            "greatest common divisor", """
            #include <stdio.h>

            int gcd(int a, int b) {
                while (b != 0) {
                    int rest = a % b;
                    a = b;
                    b = rest;
                }
                return a;
            }

            int main(void) {
                printf("%d\n", gcd(48, 18));
                return 0;
            }
            """
        },
    };

    /// <summary>
    /// The same textbook programs with the classic mistake put back, and what the analysis must say about each.
    /// </summary>
    /// <remarks>
    /// Without these, the suite above proves nothing: an analysis that never reported anything at all would leave every
    /// correct program alone too. These show it is actually looking, by finding the mistake a lecturer would circle.
    /// </remarks>
    public static TheoryData<string, string, string> PythonMistakes => new()
    {
        {
            "average without the empty guard", "analysis-division-by-zero", """
            def average(marks):
                return sum(marks) / len(marks)

            print(average([70, 45, 90]))
            """
        },
        // Not here, deliberately: using what a search returns when it may have found nothing. A function that returns a
        // value or None is summarised as "nullness unknown" rather than "possibly None", because that shape is in almost
        // every program and nearly every caller copes with it - reporting each call would bury the report. It is found
        // when the program runs instead. Asserting it here would be asserting something FixFinder chooses not to claim.
        {
            "a position past the end of a list", "analysis-index-out-of-range", """
            scores = [70, 45, 90]
            print(scores[3])
            """
        },
    };

    public static TheoryData<string, string, string> CMistakes => new()
    {
        {
            "a linked list used after it is freed", "analysis-use-after-free", """
            #include <stdio.h>
            #include <stdlib.h>

            struct node { int value; struct node *next; };

            int main(void) {
                struct node *head = malloc(sizeof *head);
                if (head == NULL) return 1;
                head->value = 1;
                head->next = NULL;
                free(head);
                printf("%d\n", head->value);
                return 0;
            }
            """
        },
        {
            "a running total that starts from nothing", "logic-uninitialised-total", """
            #include <stdio.h>

            int total_of(const int *items, int count) {
                int total;
                for (int i = 0; i < count; i++) total += items[i];
                return total;
            }

            int main(void) {
                int items[] = {1, 2, 3};
                printf("%d\n", total_of(items, 3));
                return 0;
            }
            """
        },
    };

    [Theory]
    [MemberData(nameof(PythonMistakes))]
    public async Task TheTextbookMistakeIsFoundInPython(string name, string check, string source)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return;

        var file = Path.Combine(_temp.Path, "mistake.py");
        await File.WriteAllTextAsync(file, source.ReplaceLineEndings("\n"));

        var found = AbstractChecks.Run(await PythonFrontend.ReadAsync([file], python), new SourceText());

        foreach (var finding in found) output.WriteLine($"{name}: {finding.CheckId} line {finding.Span.Line}");
        Assert.Contains(found, finding => finding.CheckId == check);
    }

    /// <summary>
    /// Both of the lanes that read the code without running it are asked, because a mistake is found by whichever one
    /// knows its shape: following values through the code finds a pointer used after it is freed, and the patterns find
    /// a total that was never given a starting value.
    /// </summary>
    [Theory]
    [MemberData(nameof(CMistakes))]
    public async Task TheTextbookMistakeIsFoundInC(string name, string check, string source)
    {
        var file = Path.Combine(_temp.Path, "mistake.c");
        await File.WriteAllTextAsync(file, source.ReplaceLineEndings("\n"));

        var followed = AbstractChecks.Run(await CFrontend.ReadAsync([file]), new SourceText()).Select(f => (f.CheckId, f.Span.Line));
        var patterns = LogicPatterns.Scan(SourceFile.Read(file)!).Select(f => (CheckId: f.PatternId, f.Line));
        var found = followed.Concat(patterns).ToList();

        foreach (var (id, line) in found) output.WriteLine($"{name}: {id} line {line}");
        Assert.Contains(found, finding => finding.CheckId == check);
    }

    /// <summary>
    /// Every claim of a mistake either lane that reads the code makes about it. Both are asked, because a false alarm
    /// from the patterns is as much a false alarm as one from following the values, and asking one would miss the other.
    /// </summary>
    private List<string> Mistakes(string file, IEnumerable<AnalysisFinding> followed, string name)
    {
        var mistakes = followed
            .Where(f => f.Severity != Severity.Suggestion)
            .Select(f => $"line {f.Span.Line} [{f.Severity}/{f.Confidence}] {f.CheckId}: {f.Message}")
            .Concat(LogicPatterns.Scan(SourceFile.Read(file)!)
                .Where(f => f.Severity != Severity.Suggestion)
                .Select(f => $"line {f.Line} [{f.Severity}/{f.Confidence}] {f.PatternId}: {f.Message}"))
            .ToList();

        foreach (var mistake in mistakes) output.WriteLine($"{name}: {mistake}");

        return mistakes;
    }

    [Theory]
    [MemberData(nameof(Python))]
    public async Task CorrectTextbookPythonIsLeftAlone(string name, string source)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return;

        var file = Path.Combine(_temp.Path, "textbook.py");
        await File.WriteAllTextAsync(file, source.ReplaceLineEndings("\n"));

        var followed = AbstractChecks.Run(await PythonFrontend.ReadAsync([file], python), new SourceText());

        Assert.Empty(Mistakes(file, followed, name));
    }

    [Theory]
    [MemberData(nameof(C))]
    public async Task CorrectTextbookCIsLeftAlone(string name, string source)
    {
        var file = Path.Combine(_temp.Path, "textbook.c");
        await File.WriteAllTextAsync(file, source.ReplaceLineEndings("\n"));

        var followed = AbstractChecks.Run(await CFrontend.ReadAsync([file]), new SourceText());

        Assert.Empty(Mistakes(file, followed, name));
    }
}
