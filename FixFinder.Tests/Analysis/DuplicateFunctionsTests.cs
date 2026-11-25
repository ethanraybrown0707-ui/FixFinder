using FixFinder.Core;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Functions written twice: in each language whose code is read, two functions that add up a list, the same lines but for
/// their own names, are one finding on the second, naming the first - and functions alike but for a field, a word in a
/// string, a number or the function they call are not.
/// </summary>
public class DuplicateFunctionsTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<CheckReport> CheckCodeAsync(string fileName, string code)
    {
        var file = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(file, code.ReplaceLineEndings("\n"));
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Of(file) ?? CodeLanguage.Any };
        var report = await checker.CheckCodeAsync(launch);
        foreach (var finding in report.Findings) output.WriteLine($"[{finding.RuleId}] {finding.Line}: {finding.Title} :: {finding.Found}");
        return report;
    }

    /// <summary>Whether the language's own reader is on this computer: Python's and Java's, and Go's, are programs of their own.</summary>
    private static bool CanBeRead(string fileName) => Path.GetExtension(fileName) switch
    {
        ".py" => LocalFixLiveTests.Available("python"),
        ".java" => Toolchains.FindJavac() is not null,
        ".go" => GoFrontend.FindGo(fileName) is not null,
        _ => true,
    };

    public static TheoryData<string, string, int, string, string, int, int> WrittenTwice => new()
    {
        { "marks.py", """
            def average_mark(marks):
                total = 0
                for mark in marks:
                    total += mark
                if len(marks) == 0:
                    return 0
                return total / len(marks)


            def average_score(scores):
                sum_so_far = 0
                for score in scores:
                    sum_so_far += score
                if len(scores) == 0:
                    return 0
                return sum_so_far / len(scores)


            def average_age(ages):
                total = 0
                for age in ages:
                    total += age
                if len(ages) == 0:
                    return -1
                return total / len(ages)


            print(average_mark([60, 70]), average_score([1, 2]), average_age([30]))
            """, 10, "average_score", "average_mark", 1, 7 },
        { "Marks.java", """
            public class Marks {
                static int sumOf(int[] values) {
                    int total = 0;
                    for (int value : values) {
                        total += value;
                    }
                    return total;
                }

                static int totalOf(int[] items) {
                    int sum = 0;
                    for (int item : items) {
                        sum += item;
                    }
                    return sum;
                }

                public static void main(String[] args) {
                    System.out.println(sumOf(new int[] {1, 2}) + totalOf(new int[] {3}));
                }
            }
            """, 10, "totalOf", "sumOf", 2, 7 },
        { "marks.js", """
            function sumOf(values) {
                let total = 0;
                for (const value of values) {
                    total += value;
                }
                return total;
            }

            function totalOf(items) {
                let sum = 0;
                for (const item of items) {
                    sum += item;
                }
                return sum;
            }

            console.log(sumOf([1, 2]) + totalOf([3]));
            """, 9, "totalOf", "sumOf", 1, 7 },
        { "Marks.cs", """
            using System;

            class Marks
            {
                static int SumOf(int[] values)
                {
                    int total = 0;
                    foreach (var value in values)
                    {
                        total += value;
                    }
                    return total;
                }

                static int TotalOf(int[] items)
                {
                    int sum = 0;
                    foreach (var item in items)
                    {
                        sum += item;
                    }
                    return sum;
                }

                static void Main()
                {
                    Console.WriteLine(SumOf(new[] { 1, 2 }) + TotalOf(new[] { 3 }));
                }
            }
            """, 15, "TotalOf", "SumOf", 5, 9 },
        { "marks.go", """
            package main

            import "fmt"

            func sumOf(values []int) int {
            	total := 0
            	for _, value := range values {
            		total += value
            	}
            	return total
            }

            func totalOf(items []int) int {
            	sum := 0
            	for _, item := range items {
            		sum += item
            	}
            	return sum
            }

            func main() {
            	fmt.Println(sumOf([]int{1, 2}) + totalOf([]int{3}))
            }
            """, 13, "totalOf", "sumOf", 5, 7 },
        { "marks.c", """
            #include <stdio.h>

            int sum_of(const int values[], int count) {
                int total = 0;
                for (int index = 0; index < count; index++) {
                    total += values[index];
                }
                return total;
            }

            int total_of(const int items[], int length) {
                int sum = 0;
                for (int position = 0; position < length; position++) {
                    sum += items[position];
                }
                return sum;
            }

            int main(void) {
                int marks[2] = {1, 2};
                printf("%d\n", sum_of(marks, 2) + total_of(marks, 2));
                return 0;
            }
            """, 11, "total_of", "sum_of", 3, 7 },
    };

    [Theory]
    [MemberData(nameof(WrittenTwice))]
    public async Task TheSecondOfTwoFunctionsWrittenTheSameIsSaidToCopyTheFirst(
        string fileName, string code, int line, string copy, string first, int firstLine, int lines)
    {
        if (!CanBeRead(fileName)) return;

        var report = await CheckCodeAsync(fileName, code);

        var written = Assert.Single(report.Findings, finding => finding.RuleId == "analysis-duplicate-function");
        Assert.Equal((line, Severity.Suggestion, "A function written twice"), (written.Line, written.Severity, written.Title));
        Assert.Equal("Certain (functions compared line by line)", FixFinder.Core.Checking.Evidence.Labelled(written));
        Assert.Equal($"`{copy}` on line {line} is written the same as `{first}` on line {firstLine}: the same {lines} lines, but for the names " +
                     "of their own parameters and variables and their own names.", written.Found);
    }

    /// <summary>
    /// Two methods alike but for the field they compare, two functions but for a word they print, a number, or the function
    /// they call: each does something the other does not, so none is said to be written twice.
    /// </summary>
    [Fact]
    public async Task FunctionsAlikeButForAFieldAWordANumberOrACallAreNotCopies()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var report = await CheckCodeAsync("not_copies.py", """
            class Box:
                def __init__(self, width, height):
                    self.width = width
                    self.height = height

                def wider_than(self, other):
                    if self.width > other.width:
                        return True
                    print("not wider")
                    return False

                def taller_than(self, other):
                    if self.height > other.height:
                        return True
                    print("not wider")
                    return False


            def greet(names):
                for name in names:
                    print("hello", name)
                print("done greeting")
                return len(names)


            def farewell(names):
                for name in names:
                    print("goodbye", name)
                print("done greeting")
                return len(names)


            def count_big(numbers):
                found = 0
                for number in numbers:
                    if number > 10:
                        found += 1
                return found


            def count_large(numbers):
                found = 0
                for number in numbers:
                    if number > 100:
                        found += 1
                return found


            def sum_doubled(values):
                total = 0
                for value in values:
                    total += double(value)
                return total


            def sum_tripled(values):
                total = 0
                for value in values:
                    total += triple(value)
                return total


            def double(value):
                return value * 2


            def triple(value):
                return value * 3


            print(count_big([5, 50]), count_large([5, 500]), sum_doubled([1]), sum_tripled([1]), greet(["a"]), farewell(["b"]))
            print(Box(1, 2).wider_than(Box(2, 1)), Box(1, 2).taller_than(Box(2, 1)))
            """);

        Assert.DoesNotContain(report.Findings, finding => finding.RuleId == "analysis-duplicate-function");
    }
}
