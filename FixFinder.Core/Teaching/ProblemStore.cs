using System.Text.Json;
using FixFinder.Core.Checking;
using FixFinder.Core.Engine;

namespace FixFinder.Core.Teaching;

/// <summary>
/// What FixFinder said of one problem it found, kept for FixFinder Learn to show beside the lesson when the problem's code
/// is pasted into it on this computer.
/// </summary>
public sealed record ProblemDetails
{
    /// <summary>The problem's code, as FixFinder showed it.</summary>
    public required string Code { get; init; }

    /// <summary>What identifies the problem - its file, line, rule and the code of its line - to tell apart two problems whose codes happen to match.</summary>
    public required string Identity { get; init; }

    public required string Title { get; init; }

    public required string Explanation { get; init; }

    public required string WhyItMatters { get; init; }

    public required string SuggestedFix { get; init; }

    /// <summary>Where it is: "marks.py, line 12", or the cell and line of a notebook.</summary>
    public required string Location { get; init; }

    /// <summary>The line of the program it is on, as it was when it was checked, or null when it is on no one line.</summary>
    public string? LineOfCode { get; init; }

    /// <summary>The program's own lines and the lines the fix would leave them as, "- " before an old one and "+ " before a new one.</summary>
    public IReadOnlyList<string> Change { get; init; } = [];

    public required string Severity { get; init; }

    public required DateTimeOffset FoundAt { get; init; }
}

/// <summary>
/// Keeps the problems FixFinder found, as it said them, on this computer for FixFinder Learn: one small file per problem
/// code, under the application data folder. Nothing is sent anywhere.
/// </summary>
/// <remarks>
/// A problem is kept with the line of code it is on, which is what lets FixFinder Learn show the learner their own mistake
/// - so only the most recent problems are kept, and the rest are deleted as new ones arrive. Two problems whose codes
/// happen to match - one in about thirty million - are both kept under it, and FixFinder Learn says there are two rather
/// than show one as the other.
/// </remarks>
public sealed class ProblemStore(string? folder = null, int mostKept = ProblemStore.UsualMostKept)
{
    /// <summary>The most problems kept, usually; the oldest go first.</summary>
    public const int UsualMostKept = 500;

    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FixFinder", "learn", "problems");

    public string Folder { get; } = folder ?? DefaultFolder;

    /// <summary>The most problem codes kept here; the problems found longest ago are deleted first.</summary>
    public int MostKept { get; } = mostKept;

    /// <summary>
    /// The code for a finding, or null for a finding in a file of no language FixFinder teaches. The same problem on the
    /// same line of code has the same code each time it is found.
    /// </summary>
    public static ProblemCode? CodeFor(Finding finding)
    {
        var file = finding.InNotebook?.Notebook ?? finding.File;
        if (CodeLanguage.Of(file) is not { } language || !ProblemCode.Names(language)) return null;

        return ProblemCode.For(language, ConceptMap.Of(finding), IdentityOf(finding));
    }

    /// <summary>Keeps what FixFinder said of each finding, under its code; says how many it kept.</summary>
    public int Keep(IEnumerable<Finding> findings, DateTimeOffset foundAt)
    {
        var kept = 0;

        foreach (var finding in findings)
        {
            if (CodeFor(finding) is not { } code) continue;

            var details = new ProblemDetails
            {
                Code = code.Text,
                Identity = IdentityOf(finding),
                Title = finding.Title,
                Explanation = finding.Explanation,
                WhyItMatters = finding.WhyItMatters,
                SuggestedFix = finding.SuggestedFix,
                Location = finding.Location,
                LineOfCode = LineOf(finding),
                Change = finding.Change?.Lines
                    .Where(line => line.Kind != ChangeKind.Context)
                    .Select(line => (line.Kind == ChangeKind.Removed ? "- " : "+ ") + line.Text)
                    .ToList() ?? [],
                Severity = finding.Severity.ToString(),
                FoundAt = foundAt,
            };

            if (Write(code, details)) kept++;
        }

        if (kept > 0) Prune();
        return kept;
    }

    /// <summary>What was kept under a code - usually one problem, newest first - or nothing when it was found on another computer.</summary>
    public IReadOnlyList<ProblemDetails> Find(ProblemCode code)
    {
        try
        {
            var file = FileFor(code);
            if (!File.Exists(file)) return [];

            return (JsonSerializer.Deserialize<List<ProblemDetails>>(File.ReadAllText(file), JsonOptions.Default) ?? [])
                .Where(details => string.Equals(details.Code, code.Text, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(details => details.FoundAt)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>The most recently found problems, newest first, for FixFinder Learn to list.</summary>
    public IReadOnlyList<ProblemDetails> Recent(int count)
    {
        try
        {
            if (!Directory.Exists(Folder)) return [];

            return new DirectoryInfo(Folder).GetFiles("*.json")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(count)
                .SelectMany(file => ReadAll(file.FullName))
                .OrderByDescending(details => details.FoundAt)
                .Take(count)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Forgets every problem kept, for somebody who would rather none of their code was kept at all.</summary>
    public void Forget()
    {
        try
        {
            if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>What identifies a problem: where it is, what found it, what it says and the code of its line.</summary>
    private static string IdentityOf(Finding finding) => $"{finding.Id}|{LineOf(finding)}";

    /// <summary>The line of the program a finding is on, trimmed, or null when it names none or the file cannot be read.</summary>
    private static string? LineOf(Finding finding)
    {
        if (finding.Line is not { } line || line < 1) return null;

        try
        {
            var lines = File.ReadAllLines(finding.File);
            return line <= lines.Length ? lines[line - 1].Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private string FileFor(ProblemCode code) => Path.Combine(Folder, code.Key + ".json");

    /// <summary>Writes a problem under its code, keeping any other problem already kept under the same letters.</summary>
    private bool Write(ProblemCode code, ProblemDetails details)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var file = FileFor(code);

            var others = ReadAll(file).Where(existing => existing.Identity != details.Identity);
            File.WriteAllText(file, JsonSerializer.Serialize(others.Prepend(details).ToList(), JsonOptions.Default));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IReadOnlyList<ProblemDetails> ReadAll(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<List<ProblemDetails>>(File.ReadAllText(file), JsonOptions.Default) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>Deletes the oldest problems beyond the most that are kept.</summary>
    private void Prune()
    {
        try
        {
            foreach (var old in new DirectoryInfo(Folder).GetFiles("*.json").OrderByDescending(file => file.LastWriteTimeUtc).Skip(MostKept))
                old.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
