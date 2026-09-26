using FixFinder.Core;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;

namespace FixFinder.Tests;

/// <summary>
/// Taking from, or adding to, the front of a list inside a loop: reported where the code shows the list moves every
/// other item to do it, and nowhere else.
/// </summary>
public class ListFrontCheckTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<string> WriteAsync(string name, string code)
    {
        var path = Path.Combine(_temp.Path, name);
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return path;
    }

    /// <summary>What the check finds in one Python file, or nothing at all when Python is not installed.</summary>
    private async Task<IReadOnlyList<AnalysisFinding>?> PythonAsync(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = await WriteAsync("search.py", code);
        return Found(PerformanceChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> JavaAsync(string code)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var path = await WriteAsync("Graph.java", code);
        return Found(PerformanceChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>> CSharpAsync(string code)
    {
        var path = await WriteAsync("Jobs.cs", code);
        return Found(PerformanceChecks.Run(await CSharpFrontend.ReadAsync([path]), new SourceText()));
    }

    private static IReadOnlyList<AnalysisFinding> Found(IEnumerable<AnalysisFinding> findings) =>
        findings.Where(f => f.CheckId == FrontOfAListInALoop.Rule).ToList();

    [Fact]
    public async Task APythonQueueEmptiedFromTheFrontIsReported()
    {
        if (await PythonAsync("""
            def breadth_first(graph, start):
                seen = {start}
                queue = [start]
                order = []
                while queue:
                    node = queue.pop(0)
                    order.append(node)
                    for neighbour in graph[node]:
                        if neighbour not in seen:
                            seen.add(neighbour)
                            queue.append(neighbour)
                return order
            """) is not { } found) return;

        var finding = Assert.Single(found);
        Assert.Equal(6, finding.Span.Line);
        Assert.Equal(
            "`queue.pop(0)` takes the first item out of `queue`, a list, and every item after it moves one place towards the front to close " +
            "the gap - on every pass of the loop that begins on line 5, so the work grows with the number of passes times the length of `queue`",
            finding.Message);
        Assert.Equal(Severity.Suggestion, finding.Severity);
        Assert.Equal(FindingKind.Performance, finding.Kind);
        Assert.Null(finding.Fix);
    }

    [Fact]
    public async Task PuttingEachItemInFrontOfAPythonListIsReported()
    {
        if (await PythonAsync("""
            def newest_first(lines):
                history = []
                for line in lines:
                    history.insert(0, line.strip())
                return history
            """) is not { } found) return;

        var finding = Assert.Single(found);
        Assert.Equal(4, finding.Span.Line);
        Assert.StartsWith(
            "`history.insert(0, line.strip())` puts an item in front of everything in `history`, a list, and every item already there moves " +
            "one place along to make room - on every pass of the loop that begins on line 3",
            finding.Message);
    }

    /// <summary>A loop with a written number of passes inside one that grows: the growing loop is the one reported.</summary>
    [Fact]
    public async Task TheLoopReportedIsTheOneWhosePassesGrow()
    {
        if (await PythonAsync("""
            def drain(items):
                waiting = list(items)
                while waiting:
                    for step in range(2):
                        if waiting:
                            waiting.pop(0)
                return waiting
            """) is not { } found) return;

        var finding = Assert.Single(found);
        Assert.Contains("the loop that begins on line 3", finding.Message);
    }

    /// <summary>
    /// Each of these either runs a number of times written into the code, or takes from something the code does not
    /// show is a list - a deque, a dictionary, a parameter nothing describes - or walks the list it takes from, which is
    /// a different mistake with a check of its own.
    /// </summary>
    [Theory]
    [InlineData("a loop over range(3)", "def f(items):\n    queue = list(items)\n    for turn in range(3):\n        queue.pop(0)\n    return queue\n")]
    [InlineData("a loop over a list written out", "def f():\n    names = []\n    for name in [\"ada\", \"alan\"]:\n        names.insert(0, name)\n    return names\n")]
    [InlineData("a deque", "from collections import deque\n\ndef f(items):\n    queue = deque()\n    for item in items:\n        queue.insert(0, item)\n    return queue\n")]
    [InlineData("a dictionary, whose pop(0) takes the key 0", "def f(rounds):\n    counts = {0: 1}\n    while rounds:\n        counts.pop(0)\n        counts[0] = rounds\n        rounds -= 1\n")]
    [InlineData("a parameter nothing describes", "def f(queue):\n    while queue:\n        queue.pop(0)\n")]
    [InlineData("the loop walks the same list", "def f(items):\n    names = list(items)\n    for name in names:\n        names.pop(0)\n")]
    [InlineData("the last item, which moves nothing", "def f(items):\n    stack = list(items)\n    while stack:\n        stack.pop()\n")]
    public async Task WhatTheCodeDoesNotShowIsLeftAlone(string shape, string code)
    {
        if (await PythonAsync(code) is not { } found) return;

        Assert.True(found.Count == 0, $"{shape}: {string.Join("; ", found.Select(f => f.Message))}");
    }

    [Fact]
    public async Task AJavaArrayListUsedAsAQueueIsReported()
    {
        if (await JavaAsync("""
            import java.util.*;

            public class Graph {
                static List<Integer> order(Map<Integer, List<Integer>> edges, int start) {
                    List<Integer> order = new ArrayList<>();
                    List<Integer> queue = new ArrayList<>();
                    Set<Integer> seen = new HashSet<>();
                    queue.add(start);
                    seen.add(start);
                    while (!queue.isEmpty()) {
                        int node = queue.remove(0);
                        order.add(node);
                        for (int next : edges.getOrDefault(node, List.of())) {
                            if (seen.add(next)) {
                                queue.add(next);
                            }
                        }
                    }
                    return order;
                }
            }
            """) is not { } found) return;

        var finding = Assert.Single(found);
        Assert.Equal(11, finding.Span.Line);
        Assert.StartsWith("`queue.remove(0)` takes the first item out of `queue`, an ArrayList, and every item after it moves", finding.Message);
        Assert.Contains("the loop that begins on line 10", finding.Message);
    }

    /// <summary>
    /// A LinkedList takes from its front without moving anything, a List parameter could be one, and remove(0L) removes
    /// the item equal to 0 rather than the first - so none of these is reported.
    /// </summary>
    [Theory]
    [InlineData("a LinkedList", "List<Integer> queue = new LinkedList<>(items);\n        while (!queue.isEmpty()) {\n            queue.remove(0);\n        }")]
    [InlineData("a List that could be anything", "List<Integer> queue = items;\n        while (!queue.isEmpty()) {\n            queue.remove(0);\n        }")]
    [InlineData("remove(0L) on a list of Longs", "List<Long> ids = new ArrayList<>();\n        for (int item : items) {\n            ids.add((long) item);\n            ids.remove(0L);\n        }")]
    [InlineData("a loop kept below a written number by one side of an and", "List<Integer> copy = new ArrayList<>(items);\n        for (int i = 0; i < 3 && !copy.isEmpty(); i++) {\n            copy.remove(0);\n        }")]
    public async Task AJavaListNotShownToBeAnArrayListIsLeftAlone(string shape, string body)
    {
        if (await JavaAsync($"import java.util.*;\n\npublic class Graph {{\n    static void drain(List<Integer> items) {{\n        {body}\n    }}\n}}\n") is not { } found) return;

        Assert.True(found.Count == 0, $"{shape}: {string.Join("; ", found.Select(f => f.Message))}");
    }

    [Fact]
    public async Task ACSharpListTakenFromAndAddedToAtTheFrontIsReportedForEach()
    {
        var found = await CSharpAsync("""
            using System.Collections.Generic;

            public static class Jobs
            {
                public static List<string> RunAll(IEnumerable<string> names)
                {
                    var waiting = new List<string>(names);
                    var done = new List<string>();
                    while (waiting.Count > 0)
                    {
                        var job = waiting[0];
                        waiting.RemoveAt(0);
                        done.Insert(0, job);
                    }
                    return done;
                }
            }
            """);

        Assert.Collection(found.OrderBy(f => f.Span.Line),
            taken =>
            {
                Assert.Equal(12, taken.Span.Line);
                Assert.StartsWith("`waiting.RemoveAt(0)` takes the first item out of `waiting`, a List,", taken.Message);
                Assert.Contains("the loop that begins on line 9", taken.Message);
            },
            added =>
            {
                Assert.Equal(13, added.Span.Line);
                Assert.StartsWith("`done.Insert(0, job)` puts an item in front of everything in `done`, a List,", added.Message);
            });
    }

    /// <summary>A var given new List&lt;int&gt; { start } is a List: new names the type the var takes.</summary>
    [Fact]
    public async Task ACSharpListMadeWithItemsIsKnownToBeAList()
    {
        var found = await CSharpAsync("""
            using System.Collections.Generic;

            public static class Graph
            {
                public static List<int> BreadthFirst(Dictionary<int, List<int>> edges, int start)
                {
                    var order = new List<int>();
                    var queue = new List<int> { start };
                    while (queue.Count > 0)
                    {
                        var node = queue[0];
                        queue.RemoveAt(0);
                        order.Add(node);
                        queue.AddRange(edges.GetValueOrDefault(node, new List<int>()));
                    }
                    return order;
                }
            }
            """);

        var finding = Assert.Single(found);
        Assert.Equal(12, finding.Span.Line);
        Assert.StartsWith("`queue.RemoveAt(0)` takes the first item out of `queue`, a List,", finding.Message);
    }

    [Fact]
    public async Task ACSharpIListParameterIsLeftAlone()
    {
        var found = await CSharpAsync("""
            using System.Collections.Generic;

            public static class Jobs
            {
                public static void Drain(IList<int> items)
                {
                    while (items.Count > 0)
                    {
                        items.RemoveAt(0);
                    }
                }
            }
            """);

        Assert.Empty(found);
    }

    /// <summary>How long shift() takes is up to the JavaScript engine, not the language, so nothing is said.</summary>
    [Fact]
    public async Task JavaScriptShiftIsLeftAlone()
    {
        var path = await WriteAsync("queue.js", "function drain(items) {\n  const queue = [...items];\n  while (queue.length > 0) {\n    queue.shift();\n  }\n}\n");

        Assert.Empty(Found(PerformanceChecks.Run(await JavaScriptFrontend.ReadAsync([path]), new SourceText())));
    }

    /// <summary>Each language's guide shows what takes from the front without moving the rest.</summary>
    [Theory]
    [InlineData("search.py", "popleft()")]
    [InlineData("Graph.java", "new ArrayDeque<>()")]
    [InlineData("Jobs.cs", "Dequeue()")]
    public void TheGuideShowsWhatToUseInstead(string file, string example)
    {
        var guide = Guidebook.For(file, FindingKind.Performance, FrontOfAListInALoop.Rule);

        Assert.Contains(example, guide.Example);
        Assert.Equal("Taking from or adding to the front of a list in a loop", guide.Title);
    }
}
