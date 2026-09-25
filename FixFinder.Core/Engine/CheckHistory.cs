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

    public static CheckRecord Of(string file, IEnumerable<Finding> findings, DateTimeOffset? when = null)
    {
        var counted = findings.ToList();

        return new CheckRecord(
            when ?? DateTimeOffset.Now,
            file,
            counted.Count(f => f.Severity == Severity.Error),
            counted.Count(f => f.Severity == Severity.Warning),
            counted.Count(f => f.Severity == Severity.Suggestion));
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
/// It holds counts and times, never code and never findings: the file itself is the record of what is in it, and a
/// copy of somebody's source sitting in a history file is a liability rather than a feature.
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
    private static string Ago(TimeSpan since) => since switch
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
