using System.Diagnostics;
using FixFinder.Core;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Tests;

/// <summary>
/// Work that grows with the data. Most of these check that nothing is said: a performance warning nobody can act on
/// is worse than silence, so the pattern has to be one whose cost is visible in the shape of the code. A change is only
/// offered where it gives the same answers, and the tests that can run the program run it both ways to show that it does.
/// </summary>
public class PerformanceCheckTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Returns nothing at all when Python is not installed, so the assertions are skipped rather than wrong.</summary>
    private async Task<IReadOnlyList<AnalysisFinding>?> CheckAsync(string code, string name = "shop.py")
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = await WriteAsync(name, code);
        return PerformanceChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText());
    }

    private async Task<string> WriteAsync(string name, string code)
    {
        var path = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return path;
    }

    /// <summary>The file as it would be with the change made.</summary>
    private static string Changed(LocalFix fix) =>
        string.Join("\n", fix.ApplyTo(SourceFile.Read(fix.File)!)!) + "\n";

    /// <summary>What a program prints, run to the end, or null when it could not be run here.</summary>
    private static async Task<string?> OutputAsync(string program, string arguments, string folder)
    {
        var start = new ProcessStartInfo(program, arguments)
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        try
        {
            using var process = Process.Start(start)!;
            var printed = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0 ? (await printed).ReplaceLineEndings("\n") : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            return null;
        }
    }

    [Fact]
    public async Task SearchingAListInsideALoopIsWorthSaying()
    {
        if (await CheckAsync("""
            def matching(people):
                names = ["ada", "grace"]
                found = []
                for person in people:
                    if person in names:
                        found.append(person)
                return found
            """) is not { } found) return;

        var finding = Assert.Single(found, f => f.CheckId == "analysis-repeated-search");

        Assert.Equal(FindingKind.Performance, finding.Kind);
        Assert.Equal(Severity.Suggestion, finding.Severity);
        Assert.Equal(Confidence.Likely, finding.Confidence);
        Assert.Contains("`names` is a list", finding.Message);
    }

    [Fact]
    public async Task TheSameSearchOutsideALoopIsNotWorthSaying()
    {
        if (await CheckAsync("""
            def matching(person, names):
                if person in names:
                    return person
                return None
            """) is not { } found) return;

        Assert.Empty(found);
    }

    /// <summary>
    /// A list the loop itself builds is a different list on every pass, so it is not the same search repeated - and
    /// putting it in a set would change what the program does, not how long it takes.
    /// </summary>
    [Fact]
    public async Task ACollectionTheLoopChangesIsLeftAlone()
    {
        if (await CheckAsync("""
            def unique(people):
                seen = []
                for person in people:
                    if person not in seen:
                        seen.append(person)
                return seen
            """) is not { } found) return;

        Assert.Empty(found);
    }

    [Fact]
    public async Task ACollectionAssignedInsideTheLoopIsLeftAlone()
    {
        if (await CheckAsync("""
            def matching(people):
                found = []
                for person in people:
                    names = lookup(person)
                    if person in names:
                        found.append(person)
                return found
            """) is not { } found) return;

        Assert.Empty(found);
    }

    [Fact]
    public async Task ASearchNestedDeeperInsideTheLoopStillCounts()
    {
        if (await CheckAsync("""
            def matching(people, names):
                found = []
                for person in people:
                    if person.active:
                        if person.name in names:
                            found.append(person)
                return found
            """) is not { } found) return;

        Assert.Single(found, f => f.CheckId == "analysis-repeated-search");
    }

    /// <summary>A search inside a longer condition is still a search: Python's and, or and not are looked through too.</summary>
    [Fact]
    public async Task ASearchInsideALongerConditionStillCounts()
    {
        if (await CheckAsync("""
            def matching(people):
                names = ["ada", "grace"]
                found = 0
                for person in people:
                    if person and not person in names:
                        found += 1
                return found
            """) is not { } found) return;

        Assert.Single(found, f => f.CheckId == "analysis-repeated-search");
    }

    [Fact]
    public async Task APlainLoopWithNoSearchInItSaysNothing()
    {
        if (await CheckAsync("""
            def total(prices):
                running = 0
                for price in prices:
                    running += price
                return running
            """) is not { } found) return;

        Assert.Empty(found);
    }

    /// <summary>A performance finding is never an error: the program is right, it is only doing more work than it needs.</summary>
    [Fact]
    public async Task NothingHereIsEverReportedAsAnError()
    {
        if (await CheckAsync("""
            def matching(people, names):
                for person in people:
                    if person in names:
                        print(person)
            """) is not { } found) return;

        Assert.All(found, finding => Assert.Equal(Severity.Suggestion, finding.Severity));
    }

    /// <summary>A set, a dictionary and a range go straight to what they are asked for, so searching one costs nothing extra.</summary>
    [Fact]
    public async Task ASetADictionaryOrARangeIsNeverCalledSlow()
    {
        if (await CheckAsync("""
            def matching(people, prices):
                allowed = {"ada", "grace"}
                ages = {"ada": 36}
                wanted = set(prices)
                small = range(10)
                found = 0
                for person in people:
                    if person in allowed or person in ages or person in wanted or len(person) in small:
                        found += 1
                return found
            """) is not { } found) return;

        Assert.Empty(found);
    }

    /// <summary>
    /// A parameter with no type could be a list or a set. FixFinder says what it would mean if it is a list, says that it
    /// cannot see which, and offers no change.
    /// </summary>
    [Fact]
    public async Task WhatCannotBeSeenIsSaidToBeUncertain()
    {
        if (await CheckAsync("""
            def matching(people, names):
                found = []
                for person in people:
                    if person in names:
                        found.append(person)
                return found
            """) is not { } found) return;

        var finding = Assert.Single(found);

        Assert.Equal(Confidence.Possible, finding.Confidence);
        Assert.StartsWith("If `names` is a list, a tuple or a string", finding.Message);
        Assert.Contains("FixFinder cannot see what `names` is here", finding.Message);
        Assert.Null(finding.Fix);
    }

    /// <summary>Searching a string looks for a piece of text, which a set of its characters cannot do.</summary>
    [Fact]
    public async Task AStringIsSearchedFromTheStartButNeverTurnedIntoASet()
    {
        if (await CheckAsync("""
            def count_vowels(words):
                vowels = "aeiou"
                count = 0
                for word in words:
                    if word in vowels:
                        count += 1
                return count
            """) is not { } found) return;

        var finding = Assert.Single(found);

        Assert.Contains("`vowels` is a string", finding.Message);
        Assert.Null(finding.Fix);
    }

    /// <summary>The change is written from the code itself, and running the program both ways prints the same.</summary>
    [Fact]
    public async Task TheSetGivesTheSameAnswersAsTheList()
    {
        const string code = """
            def kept_words(text):
                stop_words = ["the", "a", "an", "of"]
                kept = []
                for word in text.lower().split():
                    if word not in stop_words:
                        kept.append(word)
                return kept


            print(kept_words("The cat of the house sat on a mat"))
            print(kept_words(""))
            """;

        if (await CheckAsync(code, "words.py") is not { } found || PythonFrontend.FindInterpreter() is not { } python) return;

        var finding = Assert.Single(found);
        var fix = Assert.IsType<LocalFix>(finding.Fix);

        Assert.Equal(4, fix.StartLine);
        Assert.Equal(
            ["    stop_words_set = set(stop_words)", "    for word in text.lower().split():", "        if word not in stop_words_set:"],
            fix.NewLines);

        var changed = await WriteAsync(Path.Combine("changed", "words.py"), Changed(fix));
        var before = await OutputAsync(python, "words.py", _temp.Path);
        var after = await OutputAsync(python, "words.py", Path.GetDirectoryName(changed)!);

        Assert.Equal("['cat', 'house', 'sat', 'on', 'mat']\n[]\n", before);
        Assert.Equal(before, after);
    }

    /// <summary>Handed to other code, the list may be kept and changed while the loop runs, so the set could be out of date.</summary>
    [Fact]
    public async Task AListHandedToOtherCodeGetsNoChange()
    {
        if (await CheckAsync("""
            def matching(words, registry):
                stop_words = ["the", "a"]
                registry.remember(stop_words)
                kept = []
                for word in words.split():
                    if word not in stop_words:
                        kept.append(word)
                return kept
            """) is not { } found) return;

        Assert.Null(Assert.Single(found).Fix);
    }

    /// <summary>A set can only hold values with a hash; looking for a list or a dictionary in one fails where the list did not.</summary>
    [Fact]
    public async Task WhatIsLookedForHasToBeAPlainValue()
    {
        if (await CheckAsync("""
            def matching(people):
                names = ["ada", "grace"]
                found = []
                for person in people:
                    if person in names:
                        found.append(person)
                return found
            """) is not { } found) return;

        Assert.Null(Assert.Single(found).Fix);
    }

    /// <summary>A list made at the top of a module is a global every function there can change.</summary>
    [Fact]
    public async Task AListEveryFunctionCanReachGetsNoChange()
    {
        if (await CheckAsync("""
            stop_words = ["the", "a"]


            def forget(word):
                stop_words.remove(word)


            for word in "the cat sat".split():
                if word in stop_words:
                    forget(word)
            """) is not { } found) return;

        Assert.All(found, finding => Assert.Null(finding.Fix));
    }

    /// <summary>Python counts a column in bytes; letters outside ASCII earlier on the line must not move the change.</summary>
    [Fact]
    public async Task LettersOutsideAsciiEarlierOnTheLineDoNotMoveTheChange()
    {
        if (await CheckAsync("""
            def kept(text):
                stop_words = ["le", "la", "les"]
                result = []
                for word in text.split():
                    if word != "café" and word not in stop_words:
                        result.append(word)
                return result
            """) is not { } found) return;

        var fix = Assert.IsType<LocalFix>(Assert.Single(found).Fix);

        Assert.Equal("        if word != \"café\" and word not in stop_words_set:", fix.NewLines[^1]);
    }

    [Fact]
    public async Task AJavaListGetsAHashSetThatAnswersTheSame()
    {
        if (JavaFrontend.FindTools() is not { } tools) return;

        const string code = """
            import java.util.*;

            public class Main {
                static int countKnown(List<String> people) {
                    List<String> names = new ArrayList<>(List.of("ada", "grace", "alan"));
                    Set<String> banned = new HashSet<>(Set.of("linus"));
                    int found = 0;
                    for (String person : people) {
                        if (names.contains(person) && !banned.contains(person)) {
                            found++;
                        }
                    }
                    return found;
                }

                public static void main(String[] args) {
                    System.out.println(countKnown(List.of("ada", "linus", "alan", "ada")));
                    System.out.println(countKnown(List.of()));
                }
            }
            """;

        var path = await WriteAsync("Main.java", code);
        var found = PerformanceChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText());

        var finding = Assert.Single(found);
        Assert.Contains("`names` is a list", finding.Message);

        var fix = Assert.IsType<LocalFix>(finding.Fix);
        Assert.Equal("        Set<String> namesSet = new HashSet<>(names);", fix.NewLines[0]);
        Assert.Contains("namesSet.contains(person) && !banned.contains(person)", fix.NewLines[^1]);

        // The original not compiling here says something about this machine; the changed copy not compiling is a failure.
        var changedFolder = Path.GetDirectoryName(await WriteAsync(Path.Combine("changed", "Main.java"), Changed(fix)))!;
        if (await OutputAsync(tools.Javac, "Main.java", _temp.Path) is null) return;
        Assert.NotNull(await OutputAsync(tools.Javac, "Main.java", changedFolder));

        var before = await OutputAsync(tools.Java, "-cp . Main", _temp.Path);
        var after = await OutputAsync(tools.Java, "-cp . Main", changedFolder);

        Assert.Equal("3\n0\n", before);
        Assert.Equal(before, after);
    }

    /// <summary>A line put before a loop that is the body of an if without braces would become the if's body instead.</summary>
    [Fact]
    public async Task AJavaLoopThatIsTheBodyOfAnIfWithoutBracesGetsNoChange()
    {
        if (JavaFrontend.FindTools() is not { } tools) return;

        var path = await WriteAsync("Main.java", """
            import java.util.*;

            public class Main {
                static int countKnown(List<String> people, boolean ready) {
                    List<String> names = new ArrayList<>(List.of("ada", "grace"));
                    int found = 0;
                    if (ready)
                        for (String person : people) {
                            if (names.contains(person)) found++;
                        }
                    return found;
                }
            }
            """);

        var found = PerformanceChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText());

        Assert.Null(Assert.Single(found).Fix);
    }

    [Fact]
    public async Task ACSharpListGetsAHashSetAndAHashSetIsLeftAlone()
    {
        var path = await WriteAsync("Counting.cs", """
            using System.Collections.Generic;

            public static class Counting
            {
                public static int CountKnown(List<string> people)
                {
                    var names = new List<string> { "ada", "grace", "alan" };
                    var banned = new HashSet<string> { "linus" };
                    var found = 0;
                    foreach (var person in people)
                    {
                        if (names.Contains(person) && !banned.Contains(person))
                        {
                            found++;
                        }
                    }
                    return found;
                }
            }
            """);

        var found = PerformanceChecks.Run(await CSharpFrontend.ReadAsync([path]), new SourceText());

        var finding = Assert.Single(found);
        var fix = Assert.IsType<LocalFix>(finding.Fix);

        Assert.Equal("        var namesSet = new HashSet<string>(names);", fix.NewLines[0]);
        Assert.Contains("namesSet.Contains(person) && !banned.Contains(person)", fix.NewLines[^1]);
    }

    [Fact]
    public async Task AJavaScriptArrayGetsASetThatAnswersTheSame()
    {
        const string code = """
            function countKnown(people) {
                const names = ["ada", "grace", "alan", NaN];
                const banned = new Set(["linus"]);
                let found = 0;
                for (const person of people) {
                    if (names.includes(person) && !banned.has(person)) {
                        found++;
                    }
                }
                return found;
            }

            console.log(countKnown(["ada", "linus", "alan", NaN, "ada"]));
            console.log(countKnown([]));
            """;

        var path = await WriteAsync("count.js", code);
        var found = PerformanceChecks.Run(await JavaScriptFrontend.ReadAsync([path]), new SourceText());

        var fix = Assert.IsType<LocalFix>(Assert.Single(found).Fix);
        Assert.Equal("    const namesSet = new Set(names);", fix.NewLines[0]);
        Assert.Contains("namesSet.has(person) && !banned.has(person)", fix.NewLines[^1]);

        if (FixFinder.Core.Execution.TargetFactory.FindOnPath("node") is not { } node) return;

        var changed = await WriteAsync(Path.Combine("changed", "count.js"), Changed(fix));
        var before = await OutputAsync(node, "count.js", _temp.Path);
        var after = await OutputAsync(node, "count.js", Path.GetDirectoryName(changed)!);

        // NaN is found by includes and by a Set alike, which is why the change is safe for any array at all.
        Assert.Equal("4\n0\n", before);
        Assert.Equal(before, after);
    }

    /// <summary>
    /// Java text built with += in a loop: every piece is added to a StringBuilder instead, the text is made from it once
    /// the loop ends, and the program prints exactly what it printed before - including text = text + grade + i, whose
    /// pieces are added one after the other rather than summed first.
    /// </summary>
    [Fact]
    public async Task JavaTextBuiltInALoopGetsAStringBuilderThatWritesTheSame()
    {
        if (JavaFrontend.FindTools() is not { } tools) return;

        const string code = """
            public class Main {
                static String report(int[] marks, String name) {
                    String text = "Marks for ";
                    text += name;
                    text = text + ":";
                    for (int i = 0; i < marks.length; i++) {
                        char grade = marks[i] >= 70 ? 'A' : 'B';
                        text += " " + marks[i];
                        text = text + grade + i;
                        if (marks[i] > 90) {
                            text += '!';
                        }
                    }
                    return text;
                }

                public static void main(String[] args) {
                    System.out.println(report(new int[] {95, 64, 72}, "ada"));
                    System.out.println(report(new int[0], "alan"));
                }
            }
            """;

        var path = await WriteAsync("Main.java", code);
        var found = PerformanceChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText());

        var finding = Assert.Single(found, f => f.CheckId == TextBuiltInALoop.Rule);
        Assert.Equal(8, finding.Span.Line);
        Assert.StartsWith("`text` is a String, and a String never changes once it is made, so `text += \" \" + marks[i]` makes a new one", finding.Message);
        Assert.Equal(FindingKind.Performance, finding.Kind);

        var fix = Assert.IsType<LocalFix>(finding.Fix);
        Assert.Equal("        StringBuilder textBuilder = new StringBuilder().append(text);", fix.NewLines[0]);
        Assert.Contains("            textBuilder.append(\" \" + marks[i]);", fix.NewLines);
        Assert.Contains("            textBuilder.append(grade).append(i);", fix.NewLines);
        Assert.Contains("                textBuilder.append('!');", fix.NewLines);
        Assert.Equal("        text = textBuilder.toString();", fix.NewLines[^1]);

        var changedFolder = Path.GetDirectoryName(await WriteAsync(Path.Combine("changed", "Main.java"), Changed(fix)))!;
        if (await OutputAsync(tools.Javac, "Main.java", _temp.Path) is null) return;
        Assert.NotNull(await OutputAsync(tools.Javac, "Main.java", changedFolder));

        var before = await OutputAsync(tools.Java, "-cp . Main", _temp.Path);
        var after = await OutputAsync(tools.Java, "-cp . Main", changedFolder);

        Assert.Equal("Marks for ada: 95A0! 64B1 72A2\nMarks for alan:\n", before);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task CSharpTextBuiltInALoopGetsAStringBuilder()
    {
        const string code = """
            using System.Collections.Generic;

            public static class Report
            {
                public static string Lines(List<string> names)
                {
                    string text = "";
                    foreach (var name in names)
                    {
                        text += name.ToUpper();
                        text += '\n';
                    }
                    return text;
                }
            }
            """;

        var path = await WriteAsync("Report.cs", code);
        var found = PerformanceChecks.Run(await CSharpFrontend.ReadAsync([path]), new SourceText());

        var finding = Assert.Single(found, f => f.CheckId == TextBuiltInALoop.Rule);
        var fix = Assert.IsType<LocalFix>(finding.Fix);
        Assert.Equal("        var textBuilder = new System.Text.StringBuilder(text);", fix.NewLines[0]);
        Assert.Contains("            textBuilder.Append(name.ToUpper());", fix.NewLines);
        Assert.Equal("        text = textBuilder.ToString();", fix.NewLines[^1]);

        var changed = await WriteAsync(Path.Combine("changed", "Report.cs"), Changed(fix));
        Assert.Empty((await CSharpFrontend.ReadAsync([changed])).Problems);
    }

    /// <summary>
    /// The cost is the same whatever else the loop does, so each of these is still reported - but a StringBuilder would
    /// not give the same text, or the same program, so no change is offered.
    /// </summary>
    [Theory]
    [InlineData("the text is read inside the loop", "String text = \"\";\n        for (String word : words) {\n            if (text.length() > 20) break;\n            text += word;\n        }")]
    [InlineData("the text starts as null", "String text = null;\n        for (String word : words) {\n            text += word;\n        }")]
    [InlineData("a piece is an array, which + and append write differently", "String text = \"\";\n        char[] letters = {'a', 'b'};\n        for (String word : words) {\n            text += letters;\n        }")]
    [InlineData("the loop is inside a try", "String text = \"\";\n        try {\n            for (String word : words) {\n                text += word;\n            }\n        } catch (RuntimeException problem) {\n            return text;\n        }")]
    public async Task JavaTextBuiltInALoopIsReportedWithoutAChangeWhereTheChangeCouldDiffer(string shape, string body)
    {
        if (JavaFrontend.FindTools() is not { } tools) return;

        var path = await WriteAsync("Main.java", $"import java.util.*;\n\npublic class Main {{\n    static String join(List<String> words) {{\n        {body}\n        return text;\n    }}\n}}\n");
        var found = PerformanceChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText());

        var finding = Assert.Single(found, f => f.CheckId == TextBuiltInALoop.Rule);
        Assert.True(finding.Fix is null, shape);
    }

    /// <summary>A line of text started afresh on each pass of the outer loop only grows inside the inner one, which is the one reported.</summary>
    [Fact]
    public async Task TextStartedAfreshEachPassIsReportedAgainstTheLoopThatBuildsIt()
    {
        if (JavaFrontend.FindTools() is not { } tools) return;

        var path = await WriteAsync("Main.java", """
            public class Main {
                static void grid(int rows, int columns) {
                    for (int row = 0; row < rows; row++) {
                        String line = "";
                        for (int column = 0; column < columns; column++) {
                            line += row * column + " ";
                        }
                        System.out.println(line);
                    }
                }
            }
            """);
        var found = PerformanceChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText());

        var finding = Assert.Single(found, f => f.CheckId == TextBuiltInALoop.Rule);
        Assert.Contains("the loop that begins on line 5", finding.Message);
    }

    /// <summary>CPython usually grows a string in place, and JavaScript engines join strings lazily, so there nothing is said.</summary>
    [Fact]
    public async Task TextBuiltInALoopIsNotReportedInPythonOrJavaScript()
    {
        if (await CheckAsync("def join(words):\n    text = \"\"\n    for word in words:\n        text += word\n    return text\n") is { } python)
            Assert.DoesNotContain(python, f => f.CheckId == TextBuiltInALoop.Rule);

        var script = await WriteAsync("join.js", "function join(words) {\n  let text = '';\n  for (const word of words) {\n    text += word;\n  }\n  return text;\n}\n");
        Assert.DoesNotContain(PerformanceChecks.Run(await JavaScriptFrontend.ReadAsync([script]), new SourceText()), f => f.CheckId == TextBuiltInALoop.Rule);
    }

    /// <summary>A change from an analysis is shown only once a copy of the file with it has been checked and compiles.</summary>
    [Fact]
    public async Task AChangeIsOnlyShownOnceItsCopyCompiles()
    {
        if (await CheckAsync("""
            def kept_words(text):
                stop_words = ["the", "a"]
                kept = []
                for word in text.split():
                    if word not in stop_words:
                        kept.append(word)
                return kept
            """) is not { } found) return;

        var analysis = Assert.Single(found);
        var source = SourceFile.Read(analysis.Span.File)!;

        var checkedFinding = FindingFactory.FromAnalysis(analysis, source, "Not checked - Python is not compiled.", fixCompiles: true);
        Assert.NotNull(checkedFinding.Change);
        Assert.True(checkedFinding.ExampleIsFromYourCode);
        Assert.Contains("stop_words_set = set(stop_words)", checkedFinding.CorrectedExample);

        var failedFinding = FindingFactory.FromAnalysis(analysis, source, null, fixCompiles: false);
        Assert.Null(failedFinding.Change);
        Assert.Null(failedFinding.Fix);
        Assert.False(failedFinding.ExampleIsFromYourCode);
    }
}
