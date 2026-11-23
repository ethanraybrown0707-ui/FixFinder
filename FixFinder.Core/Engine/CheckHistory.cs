using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Engine;

/// <summary>What one finished check found, kept so the next one has something to be compared with.</summary>
/// <param name="When">When the check finished.</param>
/// <param name="File">The program that was checked.</param>
public sealed record CheckRecord(DateTimeOffset When, string File, int Errors, int Warnings, int Suggestions)
{
    /// <summary>Everything that is actually wrong. A suggestion is not a problem, so it is not counted as one.</summary>
    public int Problems => Errors + Warnings;

    /// <summary>
    /// Each finding as a fingerprint the next check can know it again by - its kind, then a hash of what it is and the code
    /// of the line it is on - and never the code itself. Empty in a record from before findings were fingerprinted.
    /// </summary>
    public IReadOnlyList<string> Fingerprints { get; init; } = [];

    /// <summary>Whether the program ran, so a finding only a run can find is not taken for fixed by a check where it did not.</summary>
    public bool Ran { get; init; }

    /// <summary>
    /// The idea behind each finding, as FixFinder Learn names it - OFFBYONE, DIVZERO - so the kinds of mistake a person makes
    /// in more than one program can be told; never the finding or its code. Empty in a record from before they were kept.
    /// </summary>
    public IReadOnlyList<string> Concepts { get; init; } = [];

    public static CheckRecord Of(string file, IEnumerable<Finding> findings, DateTimeOffset? when = null, bool ran = false)
    {
        var counted = findings.ToList();

        return new CheckRecord(
            when ?? DateTimeOffset.Now,
            file,
            counted.Count(f => f.Severity == Severity.Error),
            counted.Count(f => f.Severity == Severity.Warning),
            counted.Count(f => f.Severity == Severity.Suggestion))
        {
            Fingerprints = [.. counted.Select(Fingerprint)],
            Ran = ran,
            Concepts = [.. counted.Select(finding => Teaching.ConceptMap.Of(finding).Code)],
        };
    }

    /// <summary>
    /// What a finding is, as a later check knows it again: whether only a run finds it, and a hash of its rule - or its
    /// title, for one no rule names - its file's name and the code of its line, so moving the line up or down leaves it the
    /// same finding. The hash keeps no code: the history file is never a copy of anybody's source.
    /// </summary>
    public static string Fingerprint(Finding finding)
    {
        var what = finding.RuleId is { Length: > 0 } rule ? rule : finding.Title;
        var identity = $"{finding.Kind}\n{what}\n{Path.GetFileName(finding.InNotebook?.Notebook ?? finding.File)}\n{LineOf(finding)}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        return $"{(OnlyARunFinds(finding) ? RunPrefix : CodePrefix)}{hash}";
    }

    /// <summary>Begins the fingerprint of a finding only a run of the program finds.</summary>
    public const string RunPrefix = "run:";

    /// <summary>Begins the fingerprint of a finding found in the code, whether or not the program ran.</summary>
    public const string CodePrefix = "code:";

    /// <summary>
    /// Whether only a run finds it: a crash, what the program printed set beside what it should have, or a sort or a search
    /// called on lists of FixFinder's own. Everything else is found in the code itself - by the compiler, the patterns and
    /// the analyses - whether or not the program runs.
    /// </summary>
    public static bool OnlyARunFinds(Finding finding) =>
        finding.Kind == FindingKind.Runtime ||
        finding.RuleId is "wrong-output" or "stopped-before-expected-output" or "algorithm-sort-wrong" or "algorithm-search-wrong" or "algorithm-extreme-wrong" or "algorithm-prime-wrong" or "tests-miss-change";

    /// <summary>The code of the finding's line, trimmed, or nothing for a finding about the whole file.</summary>
    private static string LineOf(Finding finding)
    {
        if (finding.Line is not { } line || line < 1) return "";

        try
        {
            return System.IO.File.ReadLines(finding.File).Skip(line - 1).FirstOrDefault()?.Trim() ?? "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "";
        }
    }
}

/// <summary>How long a finding still there has been there: found by this check and each of the checks before it.</summary>
/// <param name="Checks">How many checks in a row found it, this one counted.</param>
/// <param name="Since">When the first of them finished.</param>
public sealed record FindingStreak(int Checks, DateTimeOffset Since)
{
    /// <summary>"Found by each of the last 4 checks of this program, since 2 days ago."</summary>
    public string Said(DateTimeOffset now) =>
        $"Found by each of the last {Checks} checks of this program, since {CheckHistory.Ago(now - Since)}.";
}

/// <summary>Where a finding stands against the last check of the same program.</summary>
public enum SinceLastCheck
{
    /// <summary>The last check did not find it.</summary>
    New,

    /// <summary>The last check found it too.</summary>
    StillThere,
}

/// <summary>
/// This check beside the last check of the same program: which of today's findings are new and which are still there,
/// how many of the last check's are gone - and, for those that only a run could find, gone because the program did not
/// run this time rather than because they were fixed.
/// </summary>
/// <param name="LastChecked">When the last check finished.</param>
/// <param name="Statuses">Where each of this check's findings stands, by its <see cref="Finding.Id"/>.</param>
/// <param name="FixedCount">How many of the last check's findings this check did not find.</param>
/// <param name="NotCheckedCount">How many it could not have found, as the program did not run this time.</param>
/// <param name="Fixed">Those fixed, by name - only when the last check's findings are still to hand, as within one session.</param>
public sealed record CheckComparison(
    DateTimeOffset LastChecked,
    IReadOnlyDictionary<string, SinceLastCheck> Statuses,
    int FixedCount,
    int NotCheckedCount,
    IReadOnlyList<Finding> Fixed)
{
    public int NewCount => Statuses.Values.Count(status => status == SinceLastCheck.New);

    /// <summary>
    /// For each finding still there, by its <see cref="Finding.Id"/>, how many checks in a row have found it - this one and
    /// those before it - when the earlier checks were given; empty otherwise.
    /// </summary>
    public IReadOnlyDictionary<string, FindingStreak> Streaks { get; init; } = new Dictionary<string, FindingStreak>(StringComparer.Ordinal);

    /// <summary>The program's problems over its last checks, when it has been checked often enough to show a direction.</summary>
    public string? Trend { get; init; }

    /// <summary>How long this finding has been there, or null when it is new, or the earlier checks were not given.</summary>
    public FindingStreak? StreakOf(Finding finding) => Streaks.TryGetValue(finding.Id, out var streak) ? streak : null;

    public int StillThereCount => Statuses.Values.Count(status => status == SinceLastCheck.StillThere);

    /// <summary>Where this finding stands, or null when it was not compared.</summary>
    public SinceLastCheck? StatusOf(Finding finding) => Statuses.TryGetValue(finding.Id, out var status) ? status : null;

    /// <summary>The comparison in a sentence: "Since the last check, 10 minutes ago: 2 fixed, 1 new and 3 still there."</summary>
    public string Summary(DateTimeOffset now)
    {
        if (FixedCount + NewCount + StillThereCount + NotCheckedCount == 0)
            return $"Since the last check, {CheckHistory.Ago(now - LastChecked)}: still nothing found.";

        var parts = new List<string> { $"{FixedCount} fixed", $"{NewCount} new", $"{StillThereCount} still there" };
        var said = $"Since the last check, {CheckHistory.Ago(now - LastChecked)}: {parts[0]}, {parts[1]} and {parts[2]}.";

        return NotCheckedCount == 0 ? said
            : $"{said} {NotCheckedCount} more found last time {(NotCheckedCount == 1 ? "was" : "were")} not looked for, as the program did not run this time.";
    }

    /// <summary>
    /// Compares this check's findings with the last check's fingerprints, each matched once - the same mistake twice is two
    /// findings - or returns null when there was no last check with fingerprints to compare.
    /// </summary>
    /// <param name="lastFindings">
    /// The last check's findings themselves, when they are still to hand, to name those fixed: the list the last record was
    /// made from, in its order.
    /// </param>
    /// <param name="earlier">
    /// The program's checks before the last one, newest first, to say how long each finding still there has been there.
    /// </param>
    public static CheckComparison? Of(
        CheckRecord? last, IReadOnlyList<Finding> now, bool ranNow, IReadOnlyList<Finding>? lastFindings = null, IReadOnlyList<CheckRecord>? earlier = null)
    {
        if (last is null || (last.Fingerprints.Count == 0 && last.Errors + last.Warnings + last.Suggestions > 0)) return null;

        var unmatched = last.Fingerprints.GroupBy(fingerprint => fingerprint, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var statuses = new Dictionary<string, SinceLastCheck>(StringComparer.Ordinal);

        foreach (var finding in now)
        {
            var fingerprint = CheckRecord.Fingerprint(finding);
            var stillThere = unmatched.TryGetValue(fingerprint, out var left) && left > 0;
            if (stillThere) unmatched[fingerprint] = left - 1;
            statuses[finding.Id] = stillThere ? SinceLastCheck.StillThere : SinceLastCheck.New;
        }

        // Only a run finds a crash, or a wrong answer: when the program ran last time and not this time, those are not known to be fixed.
        static bool NeedsARun(string fingerprint) => fingerprint.StartsWith(CheckRecord.RunPrefix, StringComparison.Ordinal);

        var gone = unmatched.Where(pair => pair.Value > 0).ToList();
        var notChecked = last.Ran && !ranNow ? gone.Where(pair => NeedsARun(pair.Key)).Sum(pair => pair.Value) : 0;
        var fixedCount = gone.Sum(pair => pair.Value) - notChecked;

        // Named only from the findings themselves, which a check keeps in memory, never from the history on disk - each by the
        // fingerprint it was given then, in the same order, since its line may read differently now.
        var gonePrints = gone.Where(pair => !(last.Ran && !ranNow && NeedsARun(pair.Key))).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var named = new List<Finding>();
        if (lastFindings is not null && lastFindings.Count == last.Fingerprints.Count)
        {
            for (var index = 0; index < lastFindings.Count; index++)
            {
                var fingerprint = last.Fingerprints[index];
                if (!gonePrints.TryGetValue(fingerprint, out var count) || count == 0) continue;

                gonePrints[fingerprint] = count - 1;
                named.Add(lastFindings[index]);
            }
        }

        return new CheckComparison(last.When, statuses, fixedCount, notChecked, named) { Streaks = StreaksOf(now, last, earlier ?? []) };
    }

    /// <summary>
    /// How many checks in a row found each finding still there: this one, the last, and each before it, newest first, until one
    /// did not. A check in which the program did not run neither counts nor ends the row for a finding only a run can find.
    /// </summary>
    private static Dictionary<string, FindingStreak> StreaksOf(IReadOnlyList<Finding> now, CheckRecord last, IReadOnlyList<CheckRecord> earlier)
    {
        var streaks = new Dictionary<string, FindingStreak>(StringComparer.Ordinal);
        var checksBefore = new[] { last }.Concat(earlier).Where(check => check.Fingerprints.Count > 0 || check.Errors + check.Warnings + check.Suggestions == 0).ToList();

        foreach (var finding in now)
        {
            // A finding the last check did not find is new, however long ago a check before that found it.
            var fingerprint = CheckRecord.Fingerprint(finding);
            if (!last.Fingerprints.Contains(fingerprint, StringComparer.Ordinal)) continue;
            var onlyARunFinds = fingerprint.StartsWith(CheckRecord.RunPrefix, StringComparison.Ordinal);
            var (checks, since) = (1, (DateTimeOffset?)null);

            foreach (var check in checksBefore)
            {
                if (check.Fingerprints.Contains(fingerprint, StringComparer.Ordinal))
                {
                    checks++;
                    since = check.When;
                    continue;
                }

                if (onlyARunFinds && !check.Ran) continue;
                break;
            }

            if (since is { } first) streaks[finding.Id] = new FindingStreak(checks, first);
        }

        return streaks;
    }
}

/// <summary>
/// What FixFinder found the last few times, so a report can say whether things are getting better.
/// </summary>
/// <remarks>
/// A count on its own says little - five problems is bad news or good news depending on what it was before. Keeping
/// the last check of each file lets the report say "three fewer than last time", which is the thing somebody working
/// through a list actually wants to know.
/// <para>
/// It holds counts, times and fingerprints - a hash of each finding, which lets the next check know it again - never code
/// and never the findings themselves: the file itself is the record of what is in it, and a copy of somebody's source
/// sitting in a history file is a liability rather than a feature. Which findings were fixed is named only from a check
/// still held in memory.
/// </para>
/// </remarks>
public sealed class CheckHistory
{
    /// <summary>How many checks are remembered. Enough to see a direction of travel, not a diary.</summary>
    public const int MostKept = 200;

    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FixFinder", "history.json");

    public List<CheckRecord> Checks { get; set; } = [];

    public static CheckHistory Load(string? path = null)
    {
        var file = path ?? FilePath;

        try
        {
            if (!File.Exists(file)) return new CheckHistory();

            return JsonSerializer.Deserialize<CheckHistory>(File.ReadAllText(file), JsonOptions.Default) ?? new CheckHistory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new CheckHistory();
        }
    }

    /// <summary>Every earlier check of this file, newest first.</summary>
    public IReadOnlyList<CheckRecord> ChecksOf(string file) =>
        [.. Checks.Where(check => string.Equals(check.File, file, StringComparison.OrdinalIgnoreCase)).OrderByDescending(check => check.When)];

    /// <summary>How many of a program's last checks the trend of its problems is said over.</summary>
    public const int MostInTheTrend = 8;

    /// <summary>
    /// The program's problems - errors and warnings - over its last checks, oldest first and this one last: "Problems in the
    /// last 5 checks of this program, oldest first: 7, 5, 5, 3, 2." Null with fewer than 3 checks, which show no direction.
    /// </summary>
    public static string? Trend(IReadOnlyList<CheckRecord> before, CheckRecord now)
    {
        var counted = before.Take(MostInTheTrend - 1).Reverse().Append(now).ToList();
        if (counted.Count < 3) return null;

        return $"Problems in the last {counted.Count} checks of this program, oldest first: {string.Join(", ", counted.Select(check => check.Problems))}.";
    }

    /// <summary>The last time this file was checked before now, or null the first time it is seen.</summary>
    public CheckRecord? LastTime(string file) =>
        Checks.Where(check => string.Equals(check.File, file, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(check => check.When)
            .FirstOrDefault();

    /// <summary>Adds a check to the history, oldest dropped first, and says whether it could be written down.</summary>
    public bool Record(CheckRecord check, string? path = null)
    {
        Checks.Add(check);

        if (Checks.Count > MostKept)
        {
            Checks = [.. Checks.OrderByDescending(c => c.When).Take(MostKept).OrderBy(c => c.When)];
        }

        try
        {
            var file = path ?? FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(this, JsonOptions.Default));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// How this check compares with the one before it, in a sentence, or nothing when there is nothing to compare with.
    /// </summary>
    public static string? Since(CheckRecord? before, CheckRecord now)
    {
        if (before is null) return null;

        var ago = Ago(now.When - before.When);
        var change = now.Problems - before.Problems;

        return change switch
        {
            0 when now.Problems == 0 => $"Still nothing wrong, {ago}.",
            0 => $"The same {Many(now.Problems, "problem")} as {ago}.",
            < 0 when now.Problems == 0 => $"All {Many(before.Problems, "problem")} from {ago} are gone.",
            < 0 => $"{Many(-change, "problem")} fewer than {ago}.",
            _ => $"{Many(change, "problem")} more than {ago}.",
        };
    }

    private static string Many(int count, string thing) => count == 1 ? $"1 {thing}" : $"{count} {thing}s";

    /// <summary>Roughly how long ago, in the words somebody would use rather than to the second.</summary>
    internal static string Ago(TimeSpan since) => since switch
    {
        { TotalMinutes: < 2 } => "a moment ago",
        { TotalMinutes: < 60 } => $"{(int)since.TotalMinutes} minutes ago",
        { TotalHours: < 2 } => "an hour ago",
        { TotalHours: < 24 } => $"{(int)since.TotalHours} hours ago",
        { TotalDays: < 2 } => "yesterday",
        { TotalDays: < 14 } => $"{(int)since.TotalDays} days ago",
        _ => "last time",
    };
}
