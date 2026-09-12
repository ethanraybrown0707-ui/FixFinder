using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;
using FixFinder.Core.Ranking;

namespace FixFinder.Core.Sources;

/// <summary>
/// Turns a runtime's own "Did you mean" into a patch.
/// </summary>
/// <remarks>
/// The one kind of fix that works on code only you have. Everything else in this tool searches
/// what other people published, which can only ever answer a problem somebody else also had - so
/// a typo in your own file, the commonest bug there is, was permanently out of reach.
/// <para>
/// Except that the runtime already solved it. Python 3.12 and later, gcc and clang all compare
/// the unknown name against what is actually in scope and print the answer:
/// <c>NameError: name 'avarage' is not defined. Did you mean: 'average'?</c>. That is not a guess
/// this tool is making; it is a fact the interpreter established, with the whole symbol table in
/// front of it, and it is thrown away every time it is printed.
/// </para>
/// <para>
/// <b>Deterministic, and refused when it is not.</b> The replacement happens only when the wrong
/// name appears exactly once on the line the error names. Twice and there is no way to know which
/// one was meant, so nothing is offered - the same rule the path mapper uses for two files of the
/// same name, and for the same reason.
/// </para>
/// </remarks>
public static partial class RuntimeSuggestion
{
    /// <summary>The suggestion itself, in each spelling the runtimes use.</summary>
    /// <remarks>
    /// Python ends with a question mark and a colon after "mean"; gcc and clang use neither. Both
    /// are matched rather than normalised, because the punctuation is how you tell which runtime
    /// produced it if this ever needs to differ by language.
    /// </remarks>
    [GeneratedRegex(@"[Dd]id you mean:?\s*['""`‘“]?(?<right>[A-Za-z_][A-Za-z0-9_]*)['""`’”]?\s*\??")]
    private static partial Regex SuggestionPattern();

    /// <summary>
    /// Where the misspelt name sits, per error kind.
    /// </summary>
    /// <remarks>
    /// Matched by kind rather than by taking the first quoted word, because the first quoted word
    /// is often something else entirely: in <c>'Supply' object has no attribute 'heavey'</c> it is
    /// the class, and replacing that would rename the type rather than fix the typo.
    /// </remarks>
    [GeneratedRegex(@"has no attribute\s+['""](?<wrong>[A-Za-z_][A-Za-z0-9_]*)['""]")]
    private static partial Regex AttributePattern();

    [GeneratedRegex(@"name\s+['""](?<wrong>[A-Za-z_][A-Za-z0-9_]*)['""]\s+is not defined")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"cannot import name\s+['""](?<wrong>[A-Za-z_][A-Za-z0-9_]*)['""]")]
    private static partial Regex ImportPattern();

    /// <summary>gcc and clang: <c>'avarage' undeclared</c>, then the suggestion.</summary>
    [GeneratedRegex(@"['""‘](?<wrong>[A-Za-z_][A-Za-z0-9_]*)['""’]\s+undeclared")]
    private static partial Regex UndeclaredPattern();

    /// <summary>What the runtime said, once it has been read.</summary>
    /// <param name="Wrong">The name as written.</param>
    /// <param name="Right">The name the runtime says was meant.</param>
    public sealed record Correction(string Wrong, string Right, string File, int Line);

    /// <summary>Reads a suggestion out of an error, or returns null when there is not one.</summary>
    public static Correction? Read(ParsedError error)
    {
        var text = error.Message ?? "";

        var suggestion = SuggestionPattern().Match(text);
        if (!suggestion.Success) return null;

        var wrong =
            FirstGroup(AttributePattern(), text) ??
            FirstGroup(NamePattern(), text) ??
            FirstGroup(ImportPattern(), text) ??
            FirstGroup(UndeclaredPattern(), text);

        if (wrong is null) return null;

        var right = suggestion.Groups["right"].Value;
        if (right.Length == 0 || string.Equals(wrong, right, StringComparison.Ordinal)) return null;

        // The frame that threw. Order 0 is where it went wrong in every parser here, Python
        // included, because that parser reverses the order the interpreter prints them in.
        var frame = error.CulpritFrame ?? error.Frames.FirstOrDefault();

        if (frame?.File is not { Length: > 0 } file || frame.Line is not { } line) return null;

        return new Correction(wrong, right, file, line);
    }

    private static string? FirstGroup(Regex pattern, string text)
    {
        var match = pattern.Match(text);
        return match.Success ? match.Groups["wrong"].Value : null;
    }

    /// <summary>
    /// Builds a candidate carrying the correction as a unified diff, or null.
    /// </summary>
    /// <remarks>
    /// The diff goes in the body as a fenced block rather than being handed over as a parsed
    /// patch, so that it travels the identical road every other patch takes - extracted, parsed,
    /// path-mapped, context-matched, previewed, backed up, applied, verified. A locally produced
    /// fix that skipped any of those would be the one patch in the tool nobody had checked.
    /// </remarks>
    public static FixCandidate? For(ParsedError error, string? sourceRoot)
    {
        if (Read(error) is not { } correction) return null;
        if (Diff(correction, sourceRoot) is not { } diff) return null;

        var title = $"{error.ExceptionType}: {error.Message}";

        var body = new StringBuilder()
            .AppendLine($"{error.LanguageId} reported this itself, having compared `{correction.Wrong}`")
            .AppendLine($"against the names actually in scope, and answered `{correction.Right}`.")
            .AppendLine()
            .AppendLine("```diff")
            .Append(diff)
            .AppendLine("```")
            .ToString();

        var candidate = new FixCandidate
        {
            SourceName = "The runtime itself",
            Id = $"{error.LanguageId}:did-you-mean",
            Title = title.Length > 160 ? title[..160] : title,
            Url = "",
            BodyText = body,
            RawBody = body,
            RawBodyIsHtml = false,
            Tier = FixTier.AutoAppliable,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow,
            AnswerNoun = "suggestions",
        };

        // Not ranked against the search results, because it is not one. Every weight in the
        // ranker is a way of guessing how likely a stranger's post is to be about this crash;
        // this came out of this crash, naming this file and this line.
        candidate.Score = 100;
        candidate.ScoreComponents =
        [
            new ScoreComponent(
                "The runtime's own answer", 1.0, 1.0,
                $"{error.LanguageId} compared '{correction.Wrong}' against the names in scope and " +
                $"named '{correction.Right}' itself - this is not a search result"),
        ];

        return candidate;
    }

    /// <summary>
    /// Renders the one-line change as a unified diff, or null when it cannot be made safely.
    /// </summary>
    private static string? Diff(Correction correction, string? sourceRoot)
    {
        string[] lines;

        try
        {
            if (!File.Exists(correction.File)) return null;

            lines = File.ReadAllLines(correction.File);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var index = correction.Line - 1;
        if (index < 0 || index >= lines.Length) return null;

        var original = lines[index];
        var word = new Regex($@"\b{Regex.Escape(correction.Wrong)}\b");

        // Exactly once, or not at all. Two occurrences on one line and there is no way to know
        // which the runtime meant, and guessing would edit code nobody looked at.
        if (word.Matches(original).Count != 1) return null;

        var fixedLine = word.Replace(original, correction.Right, 1);
        if (fixedLine == original) return null;

        var path = RelativePath(correction.File, sourceRoot);

        var before = index > 0 ? lines[index - 1] : null;
        var after = index + 1 < lines.Length ? lines[index + 1] : null;

        var oldStart = before is null ? correction.Line : correction.Line - 1;
        var count = 1 + (before is null ? 0 : 1) + (after is null ? 0 : 1);

        var diff = new StringBuilder()
            .Append("--- a/").Append(path).Append('\n')
            .Append("+++ b/").Append(path).Append('\n')
            .Append($"@@ -{oldStart},{count} +{oldStart},{count} @@\n");

        if (before is not null) diff.Append(' ').Append(before).Append('\n');

        diff.Append('-').Append(original).Append('\n');
        diff.Append('+').Append(fixedLine).Append('\n');

        if (after is not null) diff.Append(' ').Append(after).Append('\n');

        return diff.ToString();
    }

    /// <summary>The path as the patch should state it, relative to the source root where it can be.</summary>
    private static string RelativePath(string file, string? sourceRoot)
    {
        if (sourceRoot is not { Length: > 0 }) return Path.GetFileName(file);

        try
        {
            var relative = Path.GetRelativePath(sourceRoot, file);

            return relative.StartsWith("..", StringComparison.Ordinal)
                ? Path.GetFileName(file)
                : relative.Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return Path.GetFileName(file);
        }
    }
}
