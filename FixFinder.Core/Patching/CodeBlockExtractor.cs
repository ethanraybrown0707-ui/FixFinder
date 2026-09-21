using System.Net;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Patching;

/// <summary>A run of code lifted out of an issue comment or an answer.</summary>
public sealed record CodeBlock(string? Language, string Code, ParsedPatch? Patch = null, string? Rejection = null)
{
    public bool IsAppliablePatch => Patch is { Ok: true };

    public bool IsFailedPatch => Rejection is not null;

    public int LineCount => Code.Count(c => c == '\n') + 1;

    public string Summary => Patch is { Ok: true } patch
        ? $"patch: {patch.Summary}"
        : Rejection is not null
            ? $"looked like a diff but could not be used: {Rejection}"
            : $"{LineCount} line(s) of {Language ?? "code"}";
}

/// <summary>Pulls code out of the prose a candidate is made of.</summary>
public static partial class CodeBlockExtractor
{
    [GeneratedRegex(@"^[ \t]*```+[ \t]*(?<lang>[A-Za-z0-9_+#-]*)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex FencePattern();

    [GeneratedRegex(@"<pre(?<preAttrs>[^>]*)>\s*(?:<code(?<codeAttrs>[^>]*)>)?(?<code>.*?)(?:</code>)?\s*</pre>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PreBlockPattern();

    [GeneratedRegex(@"class\s*=\s*[""'](?<class>[^""']*)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ClassPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    private static readonly string[] DiffLanguages = ["diff", "patch", "udiff"];

    private static readonly string[] DiffOpeners = ["diff --git ", "--- ", "Index: ", "@@ "];

    public static IReadOnlyList<CodeBlock> Extract(string? body, bool isHtml)
    {
        if (string.IsNullOrWhiteSpace(body)) return [];

        var blocks = isHtml ? FromHtml(body) : FromMarkdown(body);

        if (!blocks.Any(b => b.Patch is not null))
        {
            var loose = FromUnfencedRun(body, isHtml);
            if (loose is not null) blocks.Add(loose);
        }

        return blocks;
    }

    public static IReadOnlyList<ParsedPatch> PatchesIn(IEnumerable<CodeBlock> blocks) =>
        [.. blocks.Where(b => b.IsAppliablePatch).Select(b => b.Patch!)];

    private static List<CodeBlock> FromMarkdown(string body)
    {
        var blocks = new List<CodeBlock>();
        var text = body.ReplaceLineEndings("\n");

        var fences = FencePattern().Matches(text);

        for (var i = 0; i + 1 < fences.Count; i += 2)
        {
            var open = fences[i];
            var close = fences[i + 1];

            var start = open.Index + open.Length;
            if (start >= close.Index) continue;

            var code = text[start..close.Index].Trim('\n');
            var language = open.Groups["lang"].Value;

            blocks.Add(Classify(language.Length > 0 ? language : null, code));
        }

        return blocks;
    }

    private static List<CodeBlock> FromHtml(string body)
    {
        var blocks = new List<CodeBlock>();

        foreach (Match match in PreBlockPattern().Matches(body))
        {
            var inner = match.Groups["code"].Value;

            var code = WebUtility.HtmlDecode(TagPattern().Replace(inner, "")).Trim('\n');
            if (code.Trim().Length == 0) continue;

            blocks.Add(Classify(
                ClassOf(match.Groups["codeAttrs"].Value) ?? ClassOf(match.Groups["preAttrs"].Value),
                code));
        }

        return blocks;
    }

    private static string? ClassOf(string attributes)
    {
        var match = ClassPattern().Match(attributes);
        var value = match.Success ? match.Groups["class"].Value.Trim() : "";

        return value.Length > 0 ? value : null;
    }

    private static CodeBlock? FromUnfencedRun(string body, bool isHtml)
    {
        var text = isHtml
            ? WebUtility.HtmlDecode(TagPattern().Replace(body, ""))
            : body;

        text = text.ReplaceLineEndings("\n");
        var lines = text.Split('\n');

        var start = Array.FindIndex(lines, line =>
            DiffOpeners.Any(opener => line.StartsWith(opener, StringComparison.Ordinal)));

        if (start < 0) return null;

        var run = string.Join("\n", lines[start..]).Trim('\n');

        if (!UnifiedDiffParser.LooksLikeDiff(run)) return null;

        var classified = Classify(null, run);

        return classified.Patch is { Ok: true } ? classified : null;
    }

    private static CodeBlock Classify(string? language, string code)
    {
        var tagged = language is not null &&
                     DiffLanguages.Any(d => language.Contains(d, StringComparison.OrdinalIgnoreCase));

        if (!tagged && !UnifiedDiffParser.LooksLikeDiff(code))
            return new CodeBlock(language, code);

        var patch = UnifiedDiffParser.Parse(code);

        return patch.Ok
            ? new CodeBlock(language, code, patch)
            : new CodeBlock(language, code, null, patch.Rejection);
    }
}
