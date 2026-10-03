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
    /// Whether only a run finds it: a crash, or what the program printed set beside what it should have. Everything else
    /// is found in the code itself - by the compiler, the patterns and the analyses - whether or not the program runs.
    /// </summary>
    public static bool OnlyARunFinds(Finding finding) =>
        finding.Kind == FindingKind.Runtime || finding.RuleId is "wrong-output" or "stopped-before-expected-output";

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

    public int StillThereCount => Statuses.Values.Count(status => status == SinceLastCheck.StillThere);

    /// <summary>Where this finding stands, or null when it was not compared.</summary>
    public SinceLastCheck? StatusOf(Finding finding) => Statuses.TryGetValue(finding.Id, out var status) ? status : null;

    /// <summary>The comparison in a sentence: "Since the last check, 10 minutes ago: 2 fixed, 1 new and 3 still there."</summary>
    public string Summary(DateTimeOffset now)
    {
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
    public static CheckComparison? Of(CheckRecord? last, IReadOnlyList<Finding> now, bool ranNow, IReadOnlyList<Finding>? lastFindings = null)
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

        return new CheckComparison(last.When, statuses, fixedCount, notChecked, named);
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
