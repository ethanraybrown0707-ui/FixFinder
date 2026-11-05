using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking;

/// <summary>Why one finding is held to follow from another.</summary>
public enum RelationKind
{
    /// <summary>
    /// Both say the same name has no definition. One missing definition explains every one of them, and writing it
    /// removes them all.
    /// </summary>
    SameMissingName,

    /// <summary>
    /// A run that recorded where the value the program stopped on came from showed it was made on the line the other
    /// finding is about - so the other is the mistake, and this is where it showed.
    /// </summary>
    ValueMadeThere,
}

/// <param name="RootId">The finding this one follows from.</param>
/// <param name="Confidence">How sure FixFinder is that fixing the root removes this one.</param>
public sealed record Relation(string RootId, RelationKind Kind, Confidence Confidence)
{
    public string Because => Kind switch
    {
        RelationKind.SameMissingName => "the same name has no definition here",
        RelationKind.ValueMadeThere => "a run showed the value it stopped on was made there",
        _ => "it follows from the same problem",
    };
}

/// <summary>
/// Finds the diagnostics that are consequences of one underlying problem, so a reader fixes the cause rather than
/// working down a list of symptoms.
/// </summary>
/// <remarks>
/// The bar is evidence, not resemblance. Findings are never grouped for being near each other or for reading alike:
/// a compiler reporting that <c>total</c> has no definition on four lines is four reports of one missing declaration,
/// and that is a fact about the name, not about the wording. Where the evidence is not that specific the findings are
/// left alone, because a wrong grouping hides a real problem inside another one.
/// </remarks>
public static partial class RootCauses
{
    /// <summary>Compiler codes that mean "this name has no definition".</summary>
    private static readonly HashSet<string> MissingName = new(StringComparer.OrdinalIgnoreCase)
    {
        "CS0103", "CS0246", "C2065", "NameError",
    };

    /// <summary>The checks and rules that say a line or a function gives back None: what a crash on None can follow from.</summary>
    private static readonly HashSet<string> MakesNone = new(StringComparer.Ordinal)
    {
        "logic-python-none-returned-assigned", "logic-python-returns-nothing-sometimes", "logic-python-print-instead-of-return",
        "logic-python-return-print", "python-missing-return", "python-in-place-result",
    };

    /// <summary>The same thing said by compilers that give no code for it.</summary>
    [GeneratedRegex(@"(?:cannot find symbol|undefined(?::|\s+(?:reference to|variable|name))|is not defined|has no attribute)", RegexOptions.IgnoreCase)]
    private static partial Regex SaysMissing();

    /// <summary>The name a diagnostic is about: the one thing it quotes, in whichever way that compiler quotes it.</summary>
    [GeneratedRegex(@"[`'‘“""]\s*(?<name>[A-Za-z_]\w*)\s*[`'’”""]|undefined:\s*(?<name>[A-Za-z_]\w*)|symbol:\s*variable\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex Quoted();

    /// <summary>
    /// Links each finding that follows from another to the one it follows from, and puts roots before what they
    /// explain. Findings with nothing to link to come back untouched.
    /// </summary>
    public static IReadOnlyList<Finding> Link(IReadOnlyList<Finding> findings)
    {
        var named = findings
            .Select((finding, index) => (Finding: finding, Index: index, Name: MissingNameIn(finding)))
            .Where(item => item.Name is not null)
            .ToList();

        var linked = findings.ToArray();

        foreach (var group in named.GroupBy(item => (item.Finding.File, item.Name), FileAndName))
        {
            var members = group.OrderBy(item => item.Finding.Line ?? int.MaxValue).ThenBy(item => item.Index).ToList();
            if (members.Count < 2) continue;

            var root = members[0].Finding;

            foreach (var (finding, index, _) in members.Skip(1))
            {
                // Two reports of the same mistake in the same place are the same finding. Saying one follows from the
                // other would tell the reader to fix the line they are already looking at.
                if (string.Equals(finding.Id, root.Id, StringComparison.Ordinal)) continue;

                // Certain of the relationship, which is not a claim about the finding itself: both name the same
                // undefined thing in the same file, so one definition settles both.
                linked[index] = finding with { CausedBy = new Relation(root.Id, RelationKind.SameMissingName, Confidence.Certain) };
            }
        }

        for (var index = 0; index < linked.Length; index++)
        {
            if (linked[index] is not { RootCause: { } cause, CausedBy: null } stopped) continue;

            // The finding that says a line or function gives back None, on a line the value came through, nearest the root
            // cause - the line that gave it first, then the function that gave it back. Any other finding on those lines is
            // about something else, however near.
            var maker = cause.LinesInvolved
                .Select(line => linked.FirstOrDefault(other => !ReferenceEquals(other, stopped) && other.Line == line && MakesNone.Contains(other.RuleId) &&
                                                               other.CausedBy is null && string.Equals(other.File, stopped.File, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(other => other is not null);

            // Likely, not certain: the run showed where the value came from, which says the other finding is the mistake,
            // not that changing that line is the only change that stops this one.
            if (maker is not null) linked[index] = stopped with { CausedBy = new Relation(maker.Id, RelationKind.ValueMadeThere, Confidence.Likely) };
        }

        return Ordered(linked);
    }

    /// <summary>
    /// Roots first, then what follows from them, then everything else as it was.
    /// </summary>
    /// <remarks>
    /// Which finding has been placed is tracked by where it sits, not by what it is called. Two findings can share a
    /// name - same file, same line, same rule, same title - and treating the name as the finding drops one of them,
    /// which is the one thing rearranging a report must never do.
    /// </remarks>
    private static IReadOnlyList<Finding> Ordered(IReadOnlyList<Finding> findings)
    {
        if (findings.All(finding => finding.CausedBy is null)) return findings;

        var placed = new List<Finding>(findings.Count);
        var taken = new bool[findings.Count];

        for (var root = 0; root < findings.Count; root++)
        {
            if (taken[root] || findings[root].CausedBy is not null) continue;

            taken[root] = true;
            placed.Add(findings[root]);

            for (var follower = 0; follower < findings.Count; follower++)
            {
                if (taken[follower] || findings[follower].CausedBy?.RootId != findings[root].Id) continue;

                taken[follower] = true;
                placed.Add(findings[follower]);
            }
        }

        // Anything whose cause is not in this report keeps its place rather than disappearing.
        for (var left = 0; left < findings.Count; left++)
        {
            if (!taken[left]) placed.Add(findings[left]);
        }

        return placed;
    }

    /// <summary>The undefined name a finding is about, or null when it is not about one.</summary>
    private static string? MissingNameIn(Finding finding)
    {
        var message = finding.Error?.Message ?? finding.Found ?? finding.Explanation;
        var code = finding.Error?.ErrorCode ?? finding.RuleId;

        if (!MissingName.Contains(code ?? "") && !SaysMissing().IsMatch(message) &&
            !MissingName.Contains(finding.Error?.ShortExceptionType ?? ""))
        {
            return null;
        }

        var quoted = Quoted().Match(message);
        return quoted.Success ? quoted.Groups["name"].Value : null;
    }

    private static readonly FileAndNameComparer FileAndName = new();

    private sealed class FileAndNameComparer : IEqualityComparer<(string File, string? Name)>
    {
        public bool Equals((string File, string? Name) a, (string File, string? Name) b) =>
            string.Equals(a.File, b.File, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Name, b.Name, StringComparison.Ordinal);

        public int GetHashCode((string File, string? Name) key) =>
            HashCode.Combine(key.File.ToUpperInvariant(), key.Name);
    }
}
