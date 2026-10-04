namespace FixFinder.Core.Execution.Versions;

/// <summary>
/// Which of a language's toolchains on this computer runs a program, and the words that say which and why: the usual
/// one - the one a terminal would run - when it is new enough, and otherwise the oldest that is, which is the nearest to
/// what the program asks for; with none new enough, the usual one, and saying what is missing.
/// </summary>
/// <remarks>
/// What decides is what the program's project declares - pyproject.toml's requires-python, package.json's engines,
/// go.mod's go line - and what its code needs, each with the reason, which is said in how it ran.
/// </remarks>
public static class ToolchainChoice
{
    /// <summary>A version the toolchain has to be at least, and why.</summary>
    public sealed record AtLeast(LanguageVersion Version, string Because);

    /// <summary>A version the toolchain has to be at most, and why.</summary>
    public sealed record AtMost(LanguageVersion Version, string Because);

    /// <summary>The toolchain chosen and the clause that names it, and the sentence that says what the code needs, when nothing else has said it.</summary>
    /// <param name="Toolchain">The toolchain that runs the program.</param>
    /// <param name="Explained">"Python 3.12.4 (installed for this user), as app.py uses a match statement at line 3, which Python 3.10 added".</param>
    /// <param name="CodeNeeds">"Its code needs Python 3.10 or later: app.py uses a match statement at line 3, which Python 3.10 added."</param>
    public sealed record Chosen(VersionedToolchain Toolchain, string Explained, string? CodeNeeds);

    /// <param name="language">The language's name, as a reader is told it: Python, Node.js, Go.</param>
    /// <param name="installed">Every toolchain of the language found on this computer.</param>
    /// <param name="usual">The one a terminal would run, when there is one.</param>
    /// <param name="atLeast">What the toolchain has to be at least, and why: the project's declared version, the code's needs.</param>
    /// <param name="atMost">What it has to be at most, and why.</param>
    /// <param name="codeNeeds">What the code itself needs, which is said even when the usual toolchain has it.</param>
    public static Chosen? Choose(
        string language, IReadOnlyList<VersionedToolchain> installed, VersionedToolchain? usual,
        IReadOnlyList<AtLeast> atLeast, IReadOnlyList<AtMost> atMost, AtLeast? codeNeeds)
    {
        bool Fits(VersionedToolchain toolchain) =>
            atLeast.All(need => toolchain.Version >= need.Version) && atMost.All(limit => toolchain.Version <= limit.Version);

        var toolchain = usual is not null && Fits(usual) ? usual
            : installed.Where(Fits).OrderBy(each => each.Version).FirstOrDefault()
              ?? usual
              ?? installed.OrderByDescending(each => each.Version).FirstOrDefault();

        if (toolchain is null) return null;

        var said = new HashSet<string>(StringComparer.Ordinal);
        var explained = $"{language} {toolchain.VersionText} ({toolchain.FoundIn})";

        if (toolchain != usual &&
            atLeast.FirstOrDefault(need => toolchain.Version >= need.Version && (usual is null || usual.Version < need.Version)) is { } neededLater &&
            said.Add(neededLater.Because))
        {
            explained += $", as {neededLater.Because}";
        }
        else if (toolchain != usual &&
                 atMost.FirstOrDefault(limit => toolchain.Version <= limit.Version && (usual is null || usual.Version > limit.Version)) is { } neededEarlier &&
                 said.Add(neededEarlier.Because))
        {
            explained += $", as {neededEarlier.Because}";
        }

        // A need no toolchain here meets is said as that; one that some toolchain meets, but none together with the rest, as that.
        foreach (var need in atLeast.Where(need => toolchain.Version < need.Version && said.Add(need.Because)))
        {
            explained += installed.Any(other => other.Version >= need.Version)
                ? $", though {need.Because}, and no {language} on this computer meets that and the rest of what it needs"
                : $", though {need.Because}, and no {language} of {need.Version} or later is on this computer";
        }

        foreach (var limit in atMost.Where(limit => toolchain.Version > limit.Version && said.Add(limit.Because)))
        {
            explained += installed.Any(other => other.Version <= limit.Version)
                ? $", though {limit.Because}, and no {language} on this computer meets that and the rest of what it needs"
                : $", though {limit.Because}, and no {language} of {limit.Version} or earlier is on this computer";
        }

        var codeNeedsSentence = codeNeeds is not null && !said.Contains(codeNeeds.Because)
            ? $"Its code needs {language} {codeNeeds.Version} or later: {codeNeeds.Because}."
            : null;

        return new Chosen(toolchain, explained, codeNeedsSentence);
    }

    /// <summary>
    /// What the code needs when the toolchain was not chosen for it - a project's own environment, say: said as a sentence
    /// when that toolchain has it, and as what is missing when it does not.
    /// </summary>
    public static string? Said(string language, VersionedToolchain? toolchain, AtLeast? codeNeeds)
    {
        if (codeNeeds is null) return null;

        if (toolchain is not null && toolchain.Version < codeNeeds.Version)
            return $"Its code needs {language} {codeNeeds.Version} or later: {codeNeeds.Because} - and it ran with {language} {toolchain.VersionText}.";

        return $"Its code needs {language} {codeNeeds.Version} or later: {codeNeeds.Because}.";
    }

}
