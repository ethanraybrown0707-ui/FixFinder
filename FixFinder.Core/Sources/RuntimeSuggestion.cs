using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Sources;

/// <summary>Turns a runtime's own "Did you mean" into a patch.</summary>
public static partial class RuntimeSuggestion
{
    [GeneratedRegex(@"[Dd]id you mean[:?]?\s*['""`‘“]?(?<right>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?)['""`’”]?\s*\??")]
    private static partial Regex SuggestionPattern();

    [GeneratedRegex(@"has no attribute\s+['""](?<wrong>[A-Za-z_][A-Za-z0-9_]*)['""]")]
    private static partial Regex AttributePattern();

    [GeneratedRegex(@"name\s+['""](?<wrong>[A-Za-z_][A-Za-z0-9_]*)['""]\s+is not defined")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"cannot import name\s+['""](?<wrong>[A-Za-z_][A-Za-z0-9_]*)['""]")]
    private static partial Regex ImportPattern();

    [GeneratedRegex(@"['""‘](?<wrong>[A-Za-z_][A-Za-z0-9_]*)['""’]\s+undeclared")]
    private static partial Regex UndeclaredPattern();

    [GeneratedRegex(@"undeclared identifier\s+['""‘](?<wrong>[A-Za-z_][A-Za-z0-9_]*)['""’]")]
    private static partial Regex UndeclaredIdentifierPattern();

    [GeneratedRegex(@"undefined (?:local variable or method|method)\s+['""`‘](?<wrong>[A-Za-z_][A-Za-z0-9_]*[?!=]?)['""`’]")]
    private static partial Regex UndefinedPattern();

    /// <summary>What the runtime said, once it has been read.</summary>
    public sealed record Correction(string Wrong, string Right, string File, int Line)
    {
        public bool IsAttribute { get; init; }
    }

    public static Correction? Read(ParsedError error)
    {
        var text = error.Message ?? "";

        var suggestion = SuggestionPattern().Match(text);

        if (!suggestion.Success && error.RawText is { Length: > 0 } raw)
            suggestion = SuggestionPattern().Match(raw);

        if (!suggestion.Success) return null;

        var wrong =
            FirstGroup(AttributePattern(), text) ??
            FirstGroup(NamePattern(), text) ??
            FirstGroup(ImportPattern(), text) ??
            FirstGroup(UndeclaredPattern(), text) ??
            FirstGroup(UndeclaredIdentifierPattern(), text) ??
            FirstGroup(UndefinedPattern(), text);

        if (wrong is null) return null;

        var right = suggestion.Groups["right"].Value;
        if (right.Length == 0 || string.Equals(wrong, right, StringComparison.Ordinal)) return null;

        var frame = FixFinder.Core.LocalFixes.LocalFixContext.OwnFrame(error);

        if (frame?.File is not { Length: > 0 } file || frame.Line is not { } line) return null;

        return new Correction(wrong, right, file, line) { IsAttribute = FirstGroup(AttributePattern(), text) is not null };
    }

    private static string? FirstGroup(Regex pattern, string text)
    {
        var match = pattern.Match(text);
        return match.Success ? match.Groups["wrong"].Value : null;
    }

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
        string fixedLine;

        if (word.Matches(original).Count == 1)
        {
            fixedLine = word.Replace(original, correction.Right, 1);
        }
        else if (correction.IsAttribute &&
                 Regex.Matches(original, $@"\.\s*{Regex.Escape(correction.Wrong)}\b") is { Count: 1 } access)
        {
            var at = access[0].Index + access[0].Length - correction.Wrong.Length;
            fixedLine = original[..at] + correction.Right + original[(at + correction.Wrong.Length)..];
        }
        else
        {
            return null;
        }

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

    internal static string RelativePath(string file, string? sourceRoot)
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
