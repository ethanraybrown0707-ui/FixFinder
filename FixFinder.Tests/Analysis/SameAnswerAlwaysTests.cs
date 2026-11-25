using FixFinder.Core;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A yes-or-no function that always gives the same answer - an is_prime whose divisor found returns True, as its end
/// does - in each language whose code is read; and in Python, a prime test that answers both ways, a placeholder with one
/// return, and a function that can finish with no return at all, which are not.
/// </summary>
public class SameAnswerAlwaysTests(ITestOutputHelper output) : IDisposable
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

    public static TheoryData<string, string, int, string> AlwaysTheSame => new()
    {
        { "always_true.py", """
            def is_prime(n):
                if n < 2:
                    return True
                for divisor in range(2, n):
                    if n % divisor == 0:
                        return True
                return True


            def is_prime_rightly(n):
                if n < 2:
                    return False
                for divisor in range(2, n):
                    if n % divisor == 0:
                        return False
                return True


            def is_admin(user):
                return True


            def has_space(text):
                for letter in text:
                    if letter == " ":
                        return True
                if text.endswith("\t"):
                    return True


            print(is_prime(4), is_prime_rightly(4), is_admin("ada"), has_space("ab"))
            """, 1, "`is_prime` always says True, whatever it is asked about: each of its 3 returns gives back True - on lines 3, 6 and 7 - so asking it tells the caller nothing." },
        { "Primes.java", """
            public class Primes {
                static boolean isPrime(int n) {
                    for (int divisor = 2; divisor < n; divisor++) {
                        if (n % divisor == 0) {
                            return true;
                        }
                    }
                    return true;
                }

                public static void main(String[] args) {
                    System.out.println(isPrime(4));
                }
            }
            """, 2, "`isPrime` always says true, whatever it is asked about: each of its 2 returns gives back true - on lines 5 and 8 - so asking it tells the caller nothing." },
        { "duplicates.js", """
            function hasDuplicates(items) {
                for (let first = 0; first < items.length; first++) {
                    for (let second = first + 1; second < items.length; second++) {
                        if (items[first] === items[second]) {
                            return true;
                        }
                    }
                }
                return true;
            }

            console.log(hasDuplicates([1, 2, 3]));
            """, 1, "`hasDuplicates` always says true, whatever it is asked about: each of its 2 returns gives back true - on lines 5 and 9 - so asking it tells the caller nothing." },
        { "Valid.cs", """
            using System;

            class Valid
            {
                static bool IsValidMark(int mark)
                {
                    if (mark < 0)
                    {
                        return false;
                    }
                    if (mark > 100)
                    {
                        return false;
                    }
                    return false;
                }

                static void Main()
                {
                    Console.WriteLine(IsValidMark(70));
                }
            }
            """, 5, "`IsValidMark` always says false, whatever it is asked about: each of its 3 returns gives back false - on lines 9, 13 and 15 - so asking it tells the caller nothing." },
        { "even.go", """
            package main

            import "fmt"

            func isEven(number int) bool {
            	if number%2 == 0 {
            		return true
            	}
            	return true
            }

            func main() {
            	fmt.Println(isEven(3))
            }
            """, 5, "`isEven` always says true, whatever it is asked about: each of its 2 returns gives back true - on lines 7 and 9 - so asking it tells the caller nothing." },
        { "sorted.c", """
            #include <stdbool.h>
            #include <stdio.h>

            bool is_sorted(const int values[], int count) {
                for (int index = 1; index < count; index++) {
                    if (values[index - 1] > values[index]) {
                        return true;
                    }
                }
                return true;
            }

            int main(void) {
                int marks[3] = {3, 1, 2};
                printf("%d", is_sorted(marks, 3));
                return 0;
            }
            """, 4, "`is_sorted` always says true, whatever it is asked about: each of its 2 returns gives back true - on lines 7 and 10 - so asking it tells the caller nothing." },
        { "vowel.cpp", """
            #include <iostream>
            #include <string>

            bool hasVowel(const std::string& word) {
                for (char letter : word) {
                    if (letter == 'a' || letter == 'e' || letter == 'i' || letter == 'o' || letter == 'u') {
                        return false;
                    }
                }
                return false;
            }

            bool isShort(const std::string& word) {
                if (word.size() < 4) {
                    return true;
                }
                return false;
            }

            int main() {
                std::cout << hasVowel("cat") << isShort("cat") << std::endl;
                return 0;
            }
            """, 4, "`hasVowel` always says false, whatever it is asked about: each of its 2 returns gives back false - on lines 7 and 10 - so asking it tells the caller nothing." },
    };

    [Theory]
    [MemberData(nameof(AlwaysTheSame))]
    public async Task AYesOrNoFunctionWhoseEveryReturnGivesTheSameAnswerIsSaidToTellNothing(string fileName, string code, int line, string said)
    {
        if (!CanBeRead(fileName)) return;

        var report = await CheckCodeAsync(fileName, code);

        var always = Assert.Single(report.Findings, finding => finding.RuleId == "analysis-same-answer-always");
        Assert.Equal((line, Severity.Warning, "A yes-or-no function that always gives the same answer"), (always.Line, always.Severity, always.Title));
        Assert.Equal("Likely (every return read)", FixFinder.Core.Checking.Evidence.Labelled(always));
        Assert.Equal(said, always.Found);
    }

    /// <summary>The Python file holds, besides is_prime, a prime test answering both ways, a one-return placeholder and a function that can end with no return.</summary>
    [Fact]
    public async Task APrimeTestAnsweringBothWaysAPlaceholderAndAFunctionThatCanEndWithNoReturnAreNotSaid()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var report = await CheckCodeAsync("always_true.py", """
            def is_prime(n):
                if n < 2:
                    return True
                for divisor in range(2, n):
                    if n % divisor == 0:
                        return True
                return True


            def is_prime_rightly(n):
                if n < 2:
                    return False
                for divisor in range(2, n):
                    if n % divisor == 0:
                        return False
                return True


            def is_admin(user):
                return True


            def has_space(text):
                for letter in text:
                    if letter == " ":
                        return True
                if text.endswith("\t"):
                    return True


            print(is_prime(4), is_prime_rightly(4), is_admin("ada"), has_space("ab"))
            """);

        Assert.Equal(["is_prime"], report.Findings.Where(finding => finding.RuleId == "analysis-same-answer-always").Select(finding => finding.Found!.Split('`')[1]));
    }
}
