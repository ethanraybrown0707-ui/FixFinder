using FixFinder.Core.Engine;

namespace FixFinder.Core.Teaching;

/// <summary>A kind of mistake this check found that the person has made again and again lately: the idea, how often, and where.</summary>
/// <param name="Times">How many separate mistakes of this kind the checks found, this check's among them - each once however often it was checked.</param>
/// <param name="OtherPrograms">The other programs whose checks found one, each by its whole path.</param>
public sealed record RecurringMistake(Concept Concept, int Times, IReadOnlyList<string> OtherPrograms);

/// <summary>
/// Which of the kinds of mistake a check found the person keeps making - a missing ; made again after it was put right, the
/// same idea behind a mistake in more than one program - a habit to change rather than a slip, read from the history of
/// checks, which keeps the idea behind each finding and a fingerprint of it, never its code.
/// </summary>
/// <remarks>
/// A mistake counts once however many checks found it: the same unfixed ; seen by five checks is one mistake, a ; missed on
/// another line, or missed again after it was put right, is another. A kind is said when it has been made <see cref="TimesToSay"/>
/// times or more in the last 30 days, or in another program as well as this one. A mistake taught only as reading what
/// went wrong, the idea a finding of no other is given, says nothing about a habit; a check from before the ideas were
/// kept found none.
/// </remarks>
public static class RecurringMistakes
{
    /// <summary>How far back the checks are looked at.</summary>
    public static readonly TimeSpan Lately = TimeSpan.FromDays(30);

    /// <summary>The most kinds of mistake said at once, those made most often first.</summary>
    public const int MostSaid = 3;

    /// <summary>How many separate mistakes of a kind, in one program alone, make it one to say.</summary>
    public const int TimesToSay = 3;

    /// <summary>The kinds of mistake <paramref name="now"/> found that the person has made again and again in the last 30 days.</summary>
    public static IReadOnlyList<RecurringMistake> Of(IEnumerable<CheckRecord> history, CheckRecord now)
    {
        var lately = history.Where(check => check.When >= now.When - Lately && check.When <= now.When).Append(now).ToList();
        var recurring = new List<RecurringMistake>();

        foreach (var concept in Concepts.All.Where(concept => concept != Concepts.ReadingErrors && now.Concepts.Contains(concept.Code, StringComparer.Ordinal)))
        {
            // Each separate mistake once: by the program it is in and its fingerprint, whatever checks found it.
            var found = lately.SelectMany(check => MistakesOf(check, concept.Code)).ToList();
            var mistakes = found.Select(mistake => $"{mistake.Program.ToUpperInvariant()}|{mistake.Fingerprint}").ToHashSet(StringComparer.Ordinal);
            var others = found.Select(mistake => mistake.Program).Where(program => !SameProgram(program, now.File))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(program => program, StringComparer.OrdinalIgnoreCase).ToList();

            if (others.Count > 0 || mistakes.Count >= TimesToSay) recurring.Add(new RecurringMistake(concept, mistakes.Count, others));
        }

        return [.. recurring.OrderByDescending(mistake => mistake.Times).ThenByDescending(mistake => mistake.OtherPrograms.Count)
            .ThenBy(mistake => Concepts.All.ToList().IndexOf(mistake.Concept)).Take(MostSaid)];
    }

    /// <summary>
    /// How many separate mistakes of each kind - by its idea's code - the checks of the last 30 days found in programs of one
    /// language: what FixFinder Learn puts first. A mistake counts once however many checks found it; one taught only as
    /// reading what went wrong is left out.
    /// </summary>
    public static IReadOnlyDictionary<string, int> TimesLately(IEnumerable<CheckRecord> history, string languageName, DateTimeOffset now)
    {
        var lately = history.Where(check => check.When >= now - Lately && check.When <= now && CodeLanguage.Of(check.File)?.Name == languageName).ToList();

        return Concepts.All.Where(concept => concept != Concepts.ReadingErrors)
            .Select(concept => (concept.Code, Times: lately.SelectMany(check => MistakesOf(check, concept.Code))
                .Select(mistake => $"{mistake.Program.ToUpperInvariant()}|{mistake.Fingerprint}").Distinct(StringComparer.Ordinal).Count()))
            .Where(counted => counted.Times > 0)
            .ToDictionary(counted => counted.Code, counted => counted.Times, StringComparer.Ordinal);
    }

    /// <summary>The check's mistakes of one kind, each as the program it is in and its fingerprint.</summary>
    private static IEnumerable<(string Program, string Fingerprint)> MistakesOf(CheckRecord check, string conceptCode)
    {
        if (check.Concepts.Count != check.Fingerprints.Count) yield break;

        for (var index = 0; index < check.Concepts.Count; index++)
        {
            if (check.Concepts[index] == conceptCode) yield return (check.File, check.Fingerprints[index]);
        }
    }

    /// <summary>
    /// The kind of mistake in sentences: how often and where, the habit that stops it, and its lesson - "Where a statement
    /// ends has turned up 4 times in this program in the last 30 days. What stops it: ..."
    /// </summary>
    public static string Said(RecurringMistake mistake)
    {
        var days = $"{Lately.TotalDays:0}";
        var where = mistake.OtherPrograms.Count == 0
            ? $"{mistake.Concept.Title} has turned up {mistake.Times} times in this program in the last {days} days."
            : $"{mistake.Concept.Title} has turned up {Times(mistake.Times)} in the last {days} days, in {mistake.OtherPrograms.Count + 1} of your programs: {Programs(mistake.OtherPrograms)}.";

        return $"{where} What stops it: {Habits.For(mistake.Concept)} FixFinder Learn has a lesson on it, \"{mistake.Concept.Title}\".";
    }

    private static string Times(int times) => times == 1 ? "once" : times == 2 ? "twice" : $"{times} times";

    /// <summary>This program and the others, each by its file's name - with its folder's when two have the same name.</summary>
    private static string Programs(IReadOnlyList<string> others)
    {
        var sameName = others.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var named = others.Select(program => sameName.Contains(Path.GetFileName(program))
            ? Path.Combine(Path.GetFileName(Path.GetDirectoryName(program)) ?? "", Path.GetFileName(program))
            : Path.GetFileName(program)).ToList();

        return named.Count == 1 ? $"this one and {named[0]}" : $"this one, {string.Join(", ", named.SkipLast(1))} and {named[^1]}";
    }

    private static bool SameProgram(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}
