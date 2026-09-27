using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Tests;

/// <summary>
/// Things that must hold for every input, rather than for the inputs somebody thought of.
/// </summary>
/// <remarks>
/// The examples elsewhere say what FixFinder does with a particular program. These say what it may never do with any
/// program: never show a change that applying the fix would not produce, never call a fix verified without running it,
/// never lose a finding while grouping them. An example passes because it was chosen; a property passes because a few
/// hundred inputs went looking for a way to break it.
/// <para>
/// The inputs are random but the run is not: every case comes from a fixed seed, so a failure names a case that can be
/// reproduced exactly rather than one that has gone by the time anybody reads about it.
/// </para>
/// </remarks>
public class PropertyTests
{
    private const int Cases = 300;

    /// <summary>A number generator with a written-down starting point, so the same run happens every time.</summary>
    private static Random From(int seed) => new(seed);

    private static string Line(Random random)
    {
        var words = new[] { "total", "count", "items", "readings", "i", "n", "x", "value", "names", "found" };
        var symbols = new[] { " = ", " + ", " < ", " <= ", "(", ")", "[", "]", ".", ", ", " " };

        var length = random.Next(0, 9);
        var text = new System.Text.StringBuilder(new string(' ', random.Next(0, 9)));

        for (var i = 0; i < length; i++)
        {
            text.Append(words[random.Next(words.Length)]);
            text.Append(symbols[random.Next(symbols.Length)]);
        }

        return text.ToString();
    }

    private static SourceFile FileOf(Random random, int lines) =>
        new() { Path = @"C:\work\thing.py", Lines = [.. Enumerable.Range(0, lines).Select(_ => Line(random))] };

    /// <summary>
    /// The lines the change shows as staying and arriving are exactly the lines applying the fix would leave. If these
    /// ever disagreed, the reader would be shown one thing and given another.
    /// </summary>
    [Fact]
    public void AShownChangeIsAlwaysWhatApplyingTheFixWouldDo()
    {
        var random = From(20260925);

        for (var i = 0; i < Cases; i++)
        {
            var source = FileOf(random, random.Next(1, 12));
            var start = random.Next(1, source.Count + 1);
            var remove = random.Next(0, Math.Min(3, source.Count - start + 1) + 1);
            var added = Enumerable.Range(0, random.Next(0, 3)).Select(_ => Line(random)).ToList();

            var fix = new LocalFix
            {
                RuleId = "rule", Title = "Title", Explanation = "Explanation", File = source.Path,
                StartLine = start, RemoveCount = remove, NewLines = added,
            };

            if (CodeChange.From(fix, source) is not { } change) continue;

            var applied = fix.ApplyTo(source);
            Assert.NotNull(applied);

            // Every line the change calls added must really be there afterwards, at the place it says.
            var arriving = change.Lines.Where(l => l.Kind == ChangeKind.Added).ToList();
            foreach (var line in arriving)
            {
                Assert.True(line.Number >= 1 && line.Number <= applied.Count, $"case {i}: added line {line.Number} is outside the fixed file");
                Assert.Equal(applied[line.Number - 1], line.Text);
            }

            // And every line it calls removed must really be in the file as it stands.
            foreach (var line in change.Lines.Where(l => l.Kind == ChangeKind.Removed))
            {
                Assert.Equal(source.Line(line.Number), line.Text);
            }
        }
    }

    /// <summary>The marked part of a line is a real part of that line, and putting the three pieces back gives it back.</summary>
    [Fact]
    public void TheMarkedPartOfALineAlwaysAddsBackUpToTheLine()
    {
        var random = From(7);

        for (var i = 0; i < Cases; i++)
        {
            var source = FileOf(random, random.Next(1, 6));
            var line = random.Next(1, source.Count + 1);

            var fix = LocalFix.ReplaceLine("rule", "Title", "Explanation", source.Path, line, Line(random));

            if (CodeChange.From(fix, source) is not { } change) continue;

            foreach (var shown in change.Lines)
            {
                Assert.Equal(shown.Text, shown.Before + shown.Changed + shown.After);
                Assert.True(shown.HighlightStart >= 0, $"case {i}: highlight starts before the line");
                Assert.True(shown.HighlightStart + shown.HighlightLength <= shown.Text.Length, $"case {i}: highlight runs off the line");
            }
        }
    }

    /// <summary>Whatever the stages say, verified means the program was run and the failure did not come back.</summary>
    [Fact]
    public void NothingIsEverVerifiedWithoutHavingBeenRun()
    {
        var random = From(31);
        var stages = Enum.GetValues<VerificationStage>();
        var results = Enum.GetValues<StageResult>();

        for (var i = 0; i < Cases; i++)
        {
            var verification = Verification.NotTested;

            foreach (var stage in stages.OrderBy(_ => random.Next()))
            {
                if (random.Next(4) == 0) continue;
                verification = verification.With(stage, results[random.Next(results.Length)], "whatever happened");
            }

            if (!verification.IsVerified) continue;

            Assert.Equal(StageResult.Passed, verification.ResultOf(VerificationStage.Ran));
            Assert.DoesNotContain(verification.Steps, step => step.Result == StageResult.Failed);
            Assert.Equal("Fix verified", verification.Summary);
        }
    }

    /// <summary>Grouping rearranges findings and marks some of them. It never invents one and never loses one.</summary>
    [Fact]
    public void GroupingNeverLosesOrDuplicatesAFinding()
    {
        var random = From(1904);
        var names = new[] { "total", "count", "names", "value" };
        var files = new[] { @"C:\work\One.cs", @"C:\work\Two.cs" };

        for (var i = 0; i < Cases; i++)
        {
            var findings = Enumerable.Range(0, random.Next(0, 9)).Select(n => new Finding
            {
                Kind = FindingKind.Syntax,
                Severity = Severity.Error,
                Confidence = Confidence.Certain,
                File = files[random.Next(files.Length)],
                Line = random.Next(1, 40),
                Title = "The name does not exist",
                Explanation = $"The name '{names[random.Next(names.Length)]}' does not exist in the current context",
                WhyItMatters = "why",
                SuggestedFix = "fix",
                CorrectedExample = "",
                RuleId = "CS0103",
            }).ToList();

            var linked = RootCauses.Link(findings);

            Assert.Equal(findings.Count, linked.Count);
            Assert.Equal(
                findings.Select(f => f.Id).OrderBy(id => id, StringComparer.Ordinal),
                linked.Select(f => f.Id).OrderBy(id => id, StringComparer.Ordinal));

            // Nothing follows from itself, and everything that follows from something follows from a finding that is here.
            var here = linked.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var finding in linked.Where(f => f.CausedBy is not null))
            {
                Assert.NotEqual(finding.Id, finding.CausedBy!.RootId);
                Assert.Contains(finding.CausedBy.RootId, here);
            }
        }
    }

    /// <summary>A cause is always placed before what follows from it, so a reader meets it first.</summary>
    [Fact]
    public void ACauseAlwaysComesBeforeWhatFollowsFromIt()
    {
        var random = From(55);

        for (var i = 0; i < Cases; i++)
        {
            var name = random.Next(2) == 0 ? "total" : "count";

            var findings = Enumerable.Range(0, random.Next(2, 7)).Select(n => new Finding
            {
                Kind = FindingKind.Syntax,
                Severity = Severity.Error,
                Confidence = Confidence.Certain,
                File = @"C:\work\One.cs",
                Line = random.Next(1, 30),
                Title = "The name does not exist",
                Explanation = $"The name '{name}' does not exist in the current context",
                WhyItMatters = "why",
                SuggestedFix = "fix",
                CorrectedExample = "",
                RuleId = "CS0103",
            }).ToList();

            var linked = RootCauses.Link(findings);

            // Findings can share a name, so the earliest place a name appears is the one to measure against.
            var at = linked
                .Select((finding, index) => (finding.Id, index))
                .GroupBy(p => p.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Min(p => p.index), StringComparer.Ordinal);

            foreach (var finding in linked.Where(f => f.CausedBy is not null))
            {
                Assert.True(at[finding.CausedBy!.RootId] < at[finding.Id], $"case {i}: a cause was placed after what follows from it");
            }
        }
    }

    /// <summary>A trace shows real passes, in order, and never claims a pass failed when the recording was cut short.</summary>
    [Fact]
    public void ATraceNeverClaimsAFailingPassItDidNotSee()
    {
        var random = From(880);

        for (var i = 0; i < Cases; i++)
        {
            var visits = Enumerable.Range(0, random.Next(0, 60))
                .Select(n => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["i"] = n.ToString(),
                    ["item"] = Line(random),
                })
                .ToList();

            var cut = random.Next(2) == 0;
            var failed = random.Next(2) == 0;

            if (StateTrace.From(9, visits, ["i", "item"], failed, cut) is not { } trace) continue;

            Assert.True(trace.Rows.Count <= StateTrace.MostRowsShown, $"case {i}: more rows shown than the limit allows");
            Assert.Equal(Enumerable.Range(1, trace.Rows.Count), trace.Rows.Select(r => r.Number));
            Assert.All(trace.Rows, row => Assert.Equal(trace.Columns.Count, row.Values.Count));

            var marked = trace.Rows.Count(r => r.IsWhereItFailed);
            Assert.True(marked <= 1, $"case {i}: more than one pass marked as the failing one");

            if (cut) Assert.Equal(0, marked);
            if (!failed) Assert.Equal(0, marked);
        }
    }

    /// <summary>Every level always has words in it, and the middle one is always exactly what was written.</summary>
    [Fact]
    public void EveryLevelAlwaysSaysSomething()
    {
        var random = From(404);

        for (var i = 0; i < Cases; i++)
        {
            var student = Line(random) + "x";
            var beginner = random.Next(3) == 0 ? null : Line(random);
            var technical = random.Next(3) == 0 ? "" : Line(random);

            var explained = Explained.Of(student, beginner, technical);

            Assert.Equal(student, explained.At(ExplanationLevel.Student));

            foreach (var level in Enum.GetValues<ExplanationLevel>())
            {
                Assert.False(string.IsNullOrWhiteSpace(explained.At(level)), $"case {i}: {level} had nothing to say");
            }

            if (string.IsNullOrWhiteSpace(beginner)) Assert.Equal(student, explained.At(ExplanationLevel.Beginner));
            if (string.IsNullOrWhiteSpace(technical)) Assert.Equal(student, explained.At(ExplanationLevel.Technical));
        }
    }
}
