using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A loop after loops, in every language the analysis reads: a correct edit distance whose third loop runs, and a loop that
/// really never runs because the variable it counts with was not set back after an earlier loop.
/// </summary>
/// <remarks>
/// An edit distance fills a table's first column and first row with two loops, then the rest with a third. In Java, C# and
/// Python the third loop was said never to run, in Go the loop inside it, and in C, C++ and JavaScript the third loop too once
/// the lengths are checked not to be negative first. The third loop's own counter was not the cause: it is set to 1 as the
/// loop starts, and the solver had it so. What it had wrong was the length the counter is tested against. Leaving a loop
/// like i &lt;= first.length() after going round once means first is empty, and the code after a loop was worked out at that
/// point. When the loop's test was later reached with nothing new, the code after it was not looked at again, though what
/// could leave the loop had changed since - so the third loop's test kept a length of 0, which the narrowing passes at the
/// end, able only to take away, could not undo. In Go the first loop walks the table, and the code after it was not reached
/// until those passes, so the second loop was gone round once and the length kept was the second string's. The solver now
/// looks at the code after a loop again whenever the loop's test is reached with nothing new (Fixpoint.Solve).
/// <para>
/// The same stale state hid real mistakes: where a counter that is not set back really leaves the third loop unable to run,
/// an earlier loop had been gone round only once and the code after it never reached, so the loop that never runs was not
/// looked at, and nothing was said about it except in Python.
/// </para>
/// <para>
/// With the strings passed in, what i and first.length() each hold is not known at all, so their ranges cannot show the
/// third loop never starts. What the first loop leaves is kept as such: i past first.length(), until either is given
/// another value (AbstractState.IsPast). The bound must be one that changes only so - a whole number the function holds, or
/// the length of a text where texts never change - so Python and JavaScript parameters, which could be lists, are not
/// read as texts, and C and C++ use only numbers.
/// </para>
/// </remarks>
public class LoopAfterLoopTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The analysis' findings for one program, or null when the tools to read its language are not installed here.</summary>
    private async Task<IReadOnlyList<AnalysisFinding>?> AnalyseAsync(string name, string source)
    {
        var path = Path.Combine(_temp.Path, name);
        await File.WriteAllTextAsync(path, source.ReplaceLineEndings("\n"));

        IrProgram? program = Path.GetExtension(name) switch
        {
            ".java" => JavaFrontend.FindTools() is { } tools ? await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java) : null,
            ".cs" => await CSharpFrontend.ReadAsync([path]),
            ".go" => GoFrontend.FindGo(path) is { } go ? await GoFrontend.ReadAsync([path], go) : null,
            ".py" => PythonFrontend.FindInterpreter() is { } python ? await PythonFrontend.ReadAsync([path], python) : null,
            ".js" => await JavaScriptFrontend.ReadAsync([path]),
            ".c" or ".cpp" => await CFrontend.ReadAsync([path]),
            _ => throw new ArgumentException($"no reader for {name}"),
        };
        if (program is null) return null;

        Assert.Empty(program.Problems);
        var findings = AbstractChecks.Run(program, new SourceText());
        foreach (var finding in findings) output.WriteLine($"{name}:{finding.Span.Line} {finding.CheckId} {finding.Confidence}: {finding.Message}");
        return findings;
    }

    /// <summary>
    /// The edit distances print 3, the edit distance from kitten to sitting. The near misses print how often each later loop
    /// goes round, never 0: the earlier loop was left by a break, or by the other half of an and, the counter was set back,
    /// the bound was given another value, or it was changed out of sight - a list or a StringBuilder added to, a variable
    /// changed by a closure, through a pointer, through a C# ref or a C++ reference, or a C++ string appended to.
    /// </summary>
    public static TheoryData<string, string> CorrectPrograms => new()
    {
        {
            "Distance.java", """
            public class Distance {
                static int editDistance(String first, String second) {
                    int[][] table = new int[first.length() + 1][second.length() + 1];
                    for (int i = 0; i <= first.length(); i++) table[i][0] = i;
                    for (int j = 0; j <= second.length(); j++) table[0][j] = j;
                    for (int i = 1; i <= first.length(); i++) {
                        for (int j = 1; j <= second.length(); j++) {
                            if (first.charAt(i - 1) == second.charAt(j - 1)) {
                                table[i][j] = table[i - 1][j - 1];
                            } else {
                                table[i][j] = 1 + Math.min(table[i - 1][j], Math.min(table[i][j - 1], table[i - 1][j - 1]));
                            }
                        }
                    }
                    return table[first.length()][second.length()];
                }

                public static void main(String[] args) {
                    System.out.println(editDistance("kitten", "sitting"));
                }
            }
            """
        },
        {
            "DistanceWhile.java", """
            public class DistanceWhile {
                static int editDistance(String first, String second) {
                    int[][] table = new int[first.length() + 1][second.length() + 1];
                    int i = 0;
                    while (i <= first.length()) {
                        table[i][0] = i;
                        i++;
                    }
                    int j = 0;
                    while (j <= second.length()) {
                        table[0][j] = j;
                        j++;
                    }
                    i = 1;
                    while (i <= first.length()) {
                        j = 1;
                        while (j <= second.length()) {
                            int cost = first.charAt(i - 1) == second.charAt(j - 1) ? 0 : 1;
                            table[i][j] = Math.min(table[i - 1][j - 1] + cost, Math.min(table[i - 1][j], table[i][j - 1]) + 1);
                            j++;
                        }
                        i++;
                    }
                    return table[first.length()][second.length()];
                }

                public static void main(String[] args) {
                    System.out.println(editDistance("kitten", "sitting"));
                }
            }
            """
        },
        {
            "Program.cs", """
            using System;

            public static class Program
            {
                static int EditDistance(string first, string second)
                {
                    var table = new int[first.Length + 1, second.Length + 1];
                    for (int i = 0; i <= first.Length; i++) table[i, 0] = i;
                    for (int j = 0; j <= second.Length; j++) table[0, j] = j;
                    for (int i = 1; i <= first.Length; i++)
                    {
                        for (int j = 1; j <= second.Length; j++)
                        {
                            if (first[i - 1] == second[j - 1])
                            {
                                table[i, j] = table[i - 1, j - 1];
                            }
                            else
                            {
                                table[i, j] = 1 + Math.Min(table[i - 1, j], Math.Min(table[i, j - 1], table[i - 1, j - 1]));
                            }
                        }
                    }

                    return table[first.Length, second.Length];
                }

                public static void Main()
                {
                    Console.WriteLine(EditDistance("kitten", "sitting"));
                }
            }
            """
        },
        {
            "DistanceWhile.cs", """
            using System;

            class DistanceWhile
            {
                static int EditDistance(string first, string second)
                {
                    int[,] table = new int[first.Length + 1, second.Length + 1];
                    int i = 0;
                    while (i <= first.Length)
                    {
                        table[i, 0] = i;
                        i++;
                    }
                    int j = 0;
                    while (j <= second.Length)
                    {
                        table[0, j] = j;
                        j++;
                    }
                    i = 1;
                    while (i <= first.Length)
                    {
                        j = 1;
                        while (j <= second.Length)
                        {
                            int cost = first[i - 1] == second[j - 1] ? 0 : 1;
                            table[i, j] = Math.Min(table[i - 1, j - 1] + cost, Math.Min(table[i - 1, j], table[i, j - 1]) + 1);
                            j++;
                        }
                        i++;
                    }
                    return table[first.Length, second.Length];
                }

                static void Main()
                {
                    Console.WriteLine(EditDistance("kitten", "sitting"));
                }
            }
            """
        },
        {
            "main.go", """
            package main

            import "fmt"

            func editDistance(first, second string) int {
            	table := make([][]int, len(first)+1)
            	for i := range table {
            		table[i] = make([]int, len(second)+1)
            		table[i][0] = i
            	}
            	for j := 0; j <= len(second); j++ {
            		table[0][j] = j
            	}
            	for i := 1; i <= len(first); i++ {
            		for j := 1; j <= len(second); j++ {
            			if first[i-1] == second[j-1] {
            				table[i][j] = table[i-1][j-1]
            			} else {
            				table[i][j] = 1 + min(table[i-1][j], table[i][j-1], table[i-1][j-1])
            			}
            		}
            	}
            	return table[len(first)][len(second)]
            }

            func main() {
            	fmt.Println(editDistance("kitten", "sitting"))
            }
            """
        },
        {
            "distance_while.py", """
            def edit_distance(first, second):
                table = [[0] * (len(second) + 1) for _ in range(len(first) + 1)]
                i = 0
                while i <= len(first):
                    table[i][0] = i
                    i += 1
                j = 0
                while j <= len(second):
                    table[0][j] = j
                    j += 1
                i = 1
                while i <= len(first):
                    j = 1
                    while j <= len(second):
                        cost = 0 if first[i - 1] == second[j - 1] else 1
                        table[i][j] = min(table[i - 1][j - 1] + cost, min(table[i - 1][j], table[i][j - 1]) + 1)
                        j += 1
                    i += 1
                return table[len(first)][len(second)]


            print(edit_distance("kitten", "sitting"))
            """
        },
        {
            "distance.js", """
            function editDistance(first, second, rows, cols) {
              if (rows < 0 || cols < 0) return -1;
              const table = [];
              for (let k = 0; k <= rows; k++) table.push(new Array(cols + 1).fill(0));
              for (let i = 0; i <= rows; i++) table[i][0] = i;
              for (let j = 0; j <= cols; j++) table[0][j] = j;
              for (let i = 1; i <= rows; i++) {
                for (let j = 1; j <= cols; j++) {
                  if (first[i - 1] === second[j - 1]) {
                    table[i][j] = table[i - 1][j - 1];
                  } else {
                    table[i][j] = 1 + Math.min(table[i - 1][j], table[i][j - 1], table[i - 1][j - 1]);
                  }
                }
              }
              return table[rows][cols];
            }

            console.log(editDistance("kitten", "sitting", "kitten".length, "sitting".length));
            """
        },
        { "distance.c", CDistance },
        { "distance.cpp", CDistance },
        {
            "Near.java", """
            public class Near {
                static int afterBreak(int[] items, int n, int stop) {
                    int i;
                    for (i = 0; i < n; i++) if (items[i] == stop) break;
                    int ran = 0;
                    for (; i < n; i++) ran++;
                    return ran;
                }

                static int mergeLeftovers(int n1, int n2) {
                    int i = 0, j = 0, ran = 0;
                    while (i < n1 && j < n2) { i++; j++; }
                    while (i < n1) { i++; ran++; }
                    while (j < n2) { j++; ran++; }
                    return ran;
                }

                static int setBack(String first) {
                    int i = 0, ran = 0;
                    while (i < first.length()) i++;
                    i = 0;
                    while (i < first.length()) { i++; ran++; }
                    return ran;
                }

                static int boundChanged(String first) {
                    int i = 0, ran = 0;
                    while (i < first.length()) i++;
                    first = first + "ab";
                    while (i < first.length()) { i++; ran++; }
                    return ran;
                }

                static int numberBoundChanged(int n) {
                    int i = 0, ran = 0;
                    while (i < n) i++;
                    n += 2;
                    while (i < n) { i++; ran++; }
                    return ran;
                }

                static int builderGrows(StringBuilder text) {
                    int i = 0, ran = 0;
                    while (i < text.length()) i++;
                    text.append("ab");
                    while (i < text.length()) { i++; ran++; }
                    return ran;
                }

                static int spacesThenRest(String line) {
                    int i = 0, ran = 0;
                    while (i < line.length() && line.charAt(i) == ' ') i++;
                    while (i < line.length()) { i++; ran++; }
                    return ran;
                }

                public static void main(String[] args) {
                    System.out.println(afterBreak(new int[] {1, 2, 3, 4}, 4, 2) + " " + mergeLeftovers(2, 5) + " " + setBack("abc") + " " +
                        boundChanged("abc") + " " + numberBoundChanged(3) + " " + builderGrows(new StringBuilder("abc")) + " " + spacesThenRest("  ab"));
                }
            }
            """
        },
        {
            "Near.cs", """
            using System;
            using System.Collections.Generic;

            class Near
            {
                static void Grow(ref int n) { n += 2; }

                static int ThroughRef(int n)
                {
                    int i = 0, ran = 0;
                    while (i < n) i++;
                    Grow(ref n);
                    while (i < n) { i++; ran++; }
                    return ran;
                }

                static int ListGrows(List<int> items)
                {
                    int i = 0, ran = 0;
                    while (i < items.Count) i++;
                    items.Add(9);
                    while (i < items.Count) { i++; ran++; }
                    return ran;
                }

                static int LambdaChangesBound()
                {
                    int n = 3, i = 0, ran = 0;
                    Action grow = () => n += 2;
                    while (i < n) i++;
                    grow();
                    while (i < n) { i++; ran++; }
                    return ran;
                }

                static void Main()
                {
                    Console.WriteLine(ThroughRef(3) + " " + ListGrows(new List<int> { 1, 2, 3 }) + " " + LambdaChangesBound());
                }
            }
            """
        },
        {
            "main.go", """
            package main

            import "fmt"

            func closureChangesBound() int {
            	n := 3
            	i, ran := 0, 0
            	grow := func() { n += 2 }
            	for i < n {
            		i++
            	}
            	grow()
            	for i < n {
            		i++
            		ran++
            	}
            	return ran
            }

            func sliceGrows(items []int) int {
            	i, ran := 0, 0
            	for i < len(items) {
            		i++
            	}
            	items = append(items, 9)
            	for i < len(items) {
            		i++
            		ran++
            	}
            	return ran
            }

            func main() {
            	fmt.Println(closureChangesBound(), sliceGrows([]int{1, 2, 3}))
            }
            """
        },
        {
            "near.py", """
            def list_grows(items):
                i = 0
                ran = 0
                while i < len(items):
                    i += 1
                items.append(9)
                while i < len(items):
                    i += 1
                    ran += 1
                return ran


            def text_list_grows():
                letters = list("abc")
                i = 0
                ran = 0
                while i < len(letters):
                    i += 1
                letters.append("d")
                while i < len(letters):
                    i += 1
                    ran += 1
                return ran


            def closure_changes_bound():
                n = 3
                i = 0
                ran = 0

                def grow():
                    nonlocal n
                    n += 2

                while i < n:
                    i += 1
                grow()
                while i < n:
                    i += 1
                    ran += 1
                return ran


            print(list_grows([1, 2, 3]), text_list_grows(), closure_changes_bound())
            """
        },
        {
            "near.js", """
            function arrayGrows(items) {
              let i = 0, ran = 0;
              while (i < items.length) i++;
              items.push(9);
              while (i < items.length) { i++; ran++; }
              return ran;
            }

            function closureChangesBound() {
              let n = 3, i = 0, ran = 0;
              const grow = () => { n += 2; };
              while (i < n) i++;
              grow();
              while (i < n) { i++; ran++; }
              return ran;
            }

            console.log(arrayGrows([1, 2, 3]), closureChangesBound());
            """
        },
        {
            "near.c", """
            #include <stdio.h>

            void grow(int *n) { *n = *n + 2; }

            int through_pointer(int rows) {
                int i = 0, ran = 0;
                while (i <= rows) i++;
                grow(&rows);
                while (i <= rows) { i++; ran++; }
                return ran;
            }

            int written_through_pointer(int rows) {
                int *p = &rows;
                int i = 0, ran = 0;
                while (i <= rows) i++;
                *p = rows + 2;
                while (i <= rows) { i++; ran++; }
                return ran;
            }

            int main(void) {
                printf("%d %d\n", through_pointer(3), written_through_pointer(3));
                return 0;
            }
            """
        },
        {
            "near.cpp", """
            #include <cstdio>
            #include <string>

            void grow(int& n) { n = n + 2; }

            int through_reference(int rows) {
                int& alias = rows;
                int i = 0, ran = 0;
                while (i <= rows) i++;
                alias = rows + 2;
                while (i <= rows) { i++; ran++; }
                return ran;
            }

            int through_reference_parameter(int rows) {
                int i = 0, ran = 0;
                while (i <= rows) i++;
                grow(rows);
                while (i <= rows) { i++; ran++; }
                return ran;
            }

            int string_appended(std::string text) {
                int i = 0, ran = 0;
                while (i < (int) text.length()) i++;
                text.append("ab");
                while (i < (int) text.length()) { i++; ran++; }
                return ran;
            }

            int main() {
                std::printf("%d %d %d\n", through_reference(3), through_reference_parameter(3), string_appended("abc"));
                return 0;
            }
            """
        },
    };

    private const string CDistance = """
        #include <stdio.h>
        #include <string.h>

        static int table[16][16];

        int edit_distance(const char *first, const char *second, int rows, int cols) {
            if (rows < 0 || cols < 0) return -1;
            for (int i = 0; i <= rows; i++) table[i][0] = i;
            for (int j = 0; j <= cols; j++) table[0][j] = j;
            for (int i = 1; i <= rows; i++) {
                for (int j = 1; j <= cols; j++) {
                    int cost = first[i - 1] == second[j - 1] ? 0 : 1;
                    int best = table[i - 1][j - 1] + cost;
                    if (table[i - 1][j] + 1 < best) best = table[i - 1][j] + 1;
                    if (table[i][j - 1] + 1 < best) best = table[i][j - 1] + 1;
                    table[i][j] = best;
                }
            }
            return table[rows][cols];
        }

        int main(void) {
            printf("%d\n", edit_distance("kitten", "sitting", (int) strlen("kitten"), (int) strlen("sitting")));
            return 0;
        }
        """;

    [Theory]
    [MemberData(nameof(CorrectPrograms))]
    public async Task ALoopAfterLoopsThatRunsIsLeftAlone(string name, string source)
    {
        if (await AnalyseAsync(name, source) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-loop-never-runs");
        Assert.Empty(findings.Where(f => f.Severity != Severity.Suggestion));
    }

    /// <summary>
    /// The loop that never runs is the last one whose first line holds the marker. The edit distances print 0 rather than 3:
    /// the counter is not set back after the first loop, so the third loop starts past the end - with the strings written in
    /// the program, or passed in with their lengths. The others print 16 0: the second loop over the scores starts where the
    /// first one stopped.
    /// </summary>
    public static TheoryData<string, string, string> NeverRunningLoops => new()
    {
        {
            "NoReset.java", "for (; i <= first.length(); i++)", """
            public class NoReset {
                public static void main(String[] args) {
                    String first = "kitten";
                    String second = "sitting";
                    int[][] table = new int[first.length() + 1][second.length() + 1];
                    int i;
                    for (i = 0; i <= first.length(); i++) table[i][0] = i;
                    for (int j = 0; j <= second.length(); j++) table[0][j] = j;
                    for (; i <= first.length(); i++) {
                        for (int j = 1; j <= second.length(); j++) {
                            if (first.charAt(i - 1) == second.charAt(j - 1)) {
                                table[i][j] = table[i - 1][j - 1];
                            } else {
                                table[i][j] = 1 + Math.min(table[i - 1][j], Math.min(table[i][j - 1], table[i - 1][j - 1]));
                            }
                        }
                    }
                    System.out.println(table[first.length()][second.length()]);
                }
            }
            """
        },
        {
            "NoReset.cs", "for (; i <= first.Length; i++)", """
            using System;

            class NoReset
            {
                static void Main()
                {
                    string first = "kitten";
                    string second = "sitting";
                    int[,] table = new int[first.Length + 1, second.Length + 1];
                    int i;
                    for (i = 0; i <= first.Length; i++) table[i, 0] = i;
                    for (int j = 0; j <= second.Length; j++) table[0, j] = j;
                    for (; i <= first.Length; i++)
                    {
                        for (int j = 1; j <= second.Length; j++)
                        {
                            if (first[i - 1] == second[j - 1])
                                table[i, j] = table[i - 1, j - 1];
                            else
                                table[i, j] = 1 + Math.Min(table[i - 1, j], Math.Min(table[i, j - 1], table[i - 1, j - 1]));
                        }
                    }
                    Console.WriteLine(table[first.Length, second.Length]);
                }
            }
            """
        },
        {
            "main.go", "for ; i <= len(first); i++", """
            package main

            import "fmt"

            func main() {
            	first := "kitten"
            	second := "sitting"
            	table := make([][]int, len(first)+1)
            	for k := range table {
            		table[k] = make([]int, len(second)+1)
            	}
            	i := 0
            	for i = 0; i <= len(first); i++ {
            		table[i][0] = i
            	}
            	for j := 0; j <= len(second); j++ {
            		table[0][j] = j
            	}
            	for ; i <= len(first); i++ {
            		for j := 1; j <= len(second); j++ {
            			if first[i-1] == second[j-1] {
            				table[i][j] = table[i-1][j-1]
            			} else {
            				table[i][j] = 1 + min(table[i-1][j], table[i][j-1], table[i-1][j-1])
            			}
            		}
            	}
            	fmt.Println(table[len(first)][len(second)])
            }
            """
        },
        {
            "no_reset.py", "while i <= len(first):", """
            def main():
                first = "kitten"
                second = "sitting"
                table = [[0] * (len(second) + 1) for _ in range(len(first) + 1)]
                i = 0
                while i <= len(first):
                    table[i][0] = i
                    i += 1
                for j in range(len(second) + 1):
                    table[0][j] = j
                while i <= len(first):
                    for j in range(1, len(second) + 1):
                        if first[i - 1] == second[j - 1]:
                            table[i][j] = table[i - 1][j - 1]
                        else:
                            table[i][j] = 1 + min(table[i - 1][j], table[i][j - 1], table[i - 1][j - 1])
                    i += 1
                print(table[len(first)][len(second)])


            main()
            """
        },
        {
            "no_reset.js", "for (; i <= first.length; i++)", """
            function main() {
              const first = "kitten";
              const second = "sitting";
              const table = [];
              for (let k = 0; k <= first.length; k++) table.push(new Array(second.length + 1).fill(0));
              let i;
              for (i = 0; i <= first.length; i++) table[i][0] = i;
              for (let j = 0; j <= second.length; j++) table[0][j] = j;
              for (; i <= first.length; i++) {
                for (let j = 1; j <= second.length; j++) {
                  if (first[i - 1] === second[j - 1]) {
                    table[i][j] = table[i - 1][j - 1];
                  } else {
                    table[i][j] = 1 + Math.min(table[i - 1][j], table[i][j - 1], table[i - 1][j - 1]);
                  }
                }
              }
              console.log(table[first.length][second.length]);
            }

            main();
            """
        },
        { "no_reset.c", "for (; i <= rows; i++)", CNoReset },
        { "no_reset.cpp", "for (; i <= rows; i++)", CNoReset },
        {
            "NoResetParam.java", "for (; i <= first.length(); i++)", """
            public class NoResetParam {
                static int editDistance(String first, String second) {
                    int[][] table = new int[first.length() + 1][second.length() + 1];
                    int i;
                    for (i = 0; i <= first.length(); i++) table[i][0] = i;
                    for (int j = 0; j <= second.length(); j++) table[0][j] = j;
                    for (; i <= first.length(); i++) {
                        for (int j = 1; j <= second.length(); j++) {
                            if (first.charAt(i - 1) == second.charAt(j - 1)) {
                                table[i][j] = table[i - 1][j - 1];
                            } else {
                                table[i][j] = 1 + Math.min(table[i - 1][j], Math.min(table[i][j - 1], table[i - 1][j - 1]));
                            }
                        }
                    }
                    return table[first.length()][second.length()];
                }

                public static void main(String[] args) {
                    System.out.println(editDistance("kitten", "sitting"));
                }
            }
            """
        },
        {
            "NoResetParam.cs", "for (; i <= first.Length; i++)", """
            using System;

            class NoResetParam
            {
                static int EditDistance(string first, string second)
                {
                    int[,] table = new int[first.Length + 1, second.Length + 1];
                    int i;
                    for (i = 0; i <= first.Length; i++) table[i, 0] = i;
                    for (int j = 0; j <= second.Length; j++) table[0, j] = j;
                    for (; i <= first.Length; i++)
                    {
                        for (int j = 1; j <= second.Length; j++)
                        {
                            if (first[i - 1] == second[j - 1])
                                table[i, j] = table[i - 1, j - 1];
                            else
                                table[i, j] = 1 + Math.Min(table[i - 1, j], Math.Min(table[i, j - 1], table[i - 1, j - 1]));
                        }
                    }
                    return table[first.Length, second.Length];
                }

                static void Main()
                {
                    Console.WriteLine(EditDistance("kitten", "sitting"));
                }
            }
            """
        },
        {
            "main.go", "for ; i <= len(first); i++", """
            package main

            import "fmt"

            func editDistance(first string, second string) int {
            	table := make([][]int, len(first)+1)
            	for k := range table {
            		table[k] = make([]int, len(second)+1)
            	}
            	i := 0
            	for i = 0; i <= len(first); i++ {
            		table[i][0] = i
            	}
            	for j := 0; j <= len(second); j++ {
            		table[0][j] = j
            	}
            	for ; i <= len(first); i++ {
            		for j := 1; j <= len(second); j++ {
            			if first[i-1] == second[j-1] {
            				table[i][j] = table[i-1][j-1]
            			} else {
            				table[i][j] = 1 + min(table[i-1][j], table[i][j-1], table[i-1][j-1])
            			}
            		}
            	}
            	return table[len(first)][len(second)]
            }

            func main() {
            	fmt.Println(editDistance("kitten", "sitting"))
            }
            """
        },
        {
            "no_reset_param.py", "while i <= len(first):", """
            def edit_distance(first: str, second: str) -> int:
                table = [[0] * (len(second) + 1) for _ in range(len(first) + 1)]
                i = 0
                while i <= len(first):
                    table[i][0] = i
                    i += 1
                for j in range(len(second) + 1):
                    table[0][j] = j
                while i <= len(first):
                    for j in range(1, len(second) + 1):
                        if first[i - 1] == second[j - 1]:
                            table[i][j] = table[i - 1][j - 1]
                        else:
                            table[i][j] = 1 + min(table[i - 1][j], table[i][j - 1], table[i - 1][j - 1])
                    i += 1
                return table[len(first)][len(second)]


            print(edit_distance("kitten", "sitting"))
            """
        },
        { "no_reset_param.c", "for (; i <= rows; i++)", CNoResetParam },
        { "no_reset_param.cpp", "for (; i <= rows; i++)", CNoResetParam },
        {
            "Reuse.java", "while (i < scores.length)", """
            public class Reuse {
                public static void main(String[] args) {
                    int[] scores = {3, 5, 8};
                    int total = 0;
                    int i = 0;
                    while (i < scores.length) {
                        total += scores[i];
                        i++;
                    }
                    int best = 0;
                    while (i < scores.length) {
                        if (scores[i] > best) best = scores[i];
                        i++;
                    }
                    System.out.println(total + " " + best);
                }
            }
            """
        },
        {
            "Reuse.cs", "while (i < scores.Length)", """
            using System;

            class Reuse
            {
                static void Main()
                {
                    int[] scores = { 3, 5, 8 };
                    int total = 0;
                    int i = 0;
                    while (i < scores.Length)
                    {
                        total += scores[i];
                        i++;
                    }
                    int best = 0;
                    while (i < scores.Length)
                    {
                        if (scores[i] > best) best = scores[i];
                        i++;
                    }
                    Console.WriteLine(total + " " + best);
                }
            }
            """
        },
        {
            "main.go", "for i < len(scores)", """
            package main

            import "fmt"

            func main() {
            	scores := []int{3, 5, 8}
            	total := 0
            	i := 0
            	for i < len(scores) {
            		total += scores[i]
            		i++
            	}
            	best := 0
            	for i < len(scores) {
            		if scores[i] > best {
            			best = scores[i]
            		}
            		i++
            	}
            	fmt.Println(total, best)
            }
            """
        },
        {
            "reuse.py", "while i < len(scores):", """
            def main():
                scores = [3, 5, 8]
                total = 0
                i = 0
                while i < len(scores):
                    total += scores[i]
                    i += 1
                best = 0
                while i < len(scores):
                    if scores[i] > best:
                        best = scores[i]
                    i += 1
                print(total, best)


            main()
            """
        },
        {
            "reuse.js", "while (i < scores.length)", """
            function main() {
              const scores = [3, 5, 8];
              let total = 0;
              let i = 0;
              while (i < scores.length) {
                total += scores[i];
                i++;
              }
              let best = 0;
              while (i < scores.length) {
                if (scores[i] > best) best = scores[i];
                i++;
              }
              console.log(total, best);
            }

            main();
            """
        },
        { "reuse.c", "while (i < 3)", CReuse },
        { "reuse.cpp", "while (i < 3)", CReuse },
    };

    private const string CNoReset = """
        #include <stdio.h>

        int main(void) {
            const char first[] = "kitten";
            const char second[] = "sitting";
            int rows = 6, cols = 7;
            static int table[7][8];
            int i;
            for (i = 0; i <= rows; i++) table[i][0] = i;
            for (int j = 0; j <= cols; j++) table[0][j] = j;
            for (; i <= rows; i++) {
                for (int j = 1; j <= cols; j++) {
                    int cost = first[i - 1] == second[j - 1] ? 0 : 1;
                    int best = table[i - 1][j - 1] + cost;
                    if (table[i - 1][j] + 1 < best) best = table[i - 1][j] + 1;
                    if (table[i][j - 1] + 1 < best) best = table[i][j - 1] + 1;
                    table[i][j] = best;
                }
            }
            printf("%d\n", table[rows][cols]);
            return 0;
        }
        """;

    private const string CNoResetParam = """
        #include <stdio.h>
        #include <string.h>

        static int table[16][16];

        int edit_distance(const char *first, const char *second, int rows, int cols) {
            int i;
            for (i = 0; i <= rows; i++) table[i][0] = i;
            for (int j = 0; j <= cols; j++) table[0][j] = j;
            for (; i <= rows; i++) {
                for (int j = 1; j <= cols; j++) {
                    int cost = first[i - 1] == second[j - 1] ? 0 : 1;
                    int best = table[i - 1][j - 1] + cost;
                    if (table[i - 1][j] + 1 < best) best = table[i - 1][j] + 1;
                    if (table[i][j - 1] + 1 < best) best = table[i][j - 1] + 1;
                    table[i][j] = best;
                }
            }
            return table[rows][cols];
        }

        int main(void) {
            printf("%d\n", edit_distance("kitten", "sitting", (int) strlen("kitten"), (int) strlen("sitting")));
            return 0;
        }
        """;

    private const string CReuse = """
        #include <stdio.h>

        int main(void) {
            int scores[3] = {3, 5, 8};
            int total = 0;
            int i = 0;
            while (i < 3) {
                total += scores[i];
                i++;
            }
            int best = 0;
            while (i < 3) {
                if (scores[i] > best) best = scores[i];
                i++;
            }
            printf("%d %d\n", total, best);
            return 0;
        }
        """;

    [Theory]
    [MemberData(nameof(NeverRunningLoops))]
    public async Task ALoopThatNeverRunsIsStillReported(string name, string marker, string source)
    {
        if (await AnalyseAsync(name, source) is not { } findings) return;

        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var expected = Array.FindLastIndex(lines, line => line.Contains(marker, StringComparison.Ordinal)) + 1;

        var reported = findings.Where(f => f.CheckId == "analysis-loop-never-runs").ToList();
        Assert.Equal([expected], reported.Select(f => f.Span.Line));
        Assert.Equal(Confidence.Likely, reported.Single().Confidence);
    }
}
