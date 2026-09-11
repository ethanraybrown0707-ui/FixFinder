using System.Net;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Patching;

/// <summary>A run of code lifted out of an issue comment or an answer.</summary>
/// <param name="Language">The fence's language tag, or the HTML class, where there was one.</param>
/// <param name="Code">The block's contents, with line endings normalised.</param>
/// <param name="Patch">The parsed diff when this block was one, otherwise null.</param>
/// <param name="Rejection">Why it was not usable as a patch, when it looked like one but failed.</param>
public sealed record CodeBlock(string? Language, string Code, ParsedPatch? Patch = null, string? Rejection = null)
{
    public bool IsAppliablePatch => Patch is { Ok: true };

    /// <summary>True when the block looked like a diff but could not be parsed.</summary>
    public bool IsFailedPatch => Rejection is not null;

    public int LineCount => Code.Count(c => c == '\n') + 1;

    public string Summary => Patch is { Ok: true } patch
        ? $"patch: {patch.Summary}"
        : Rejection is not null
            ? $"looked like a diff but could not be used: {Rejection}"
            : $"{LineCount} line(s) of {Language ?? "code"}";
}

/// <summary>
/// Pulls code out of the prose a candidate is made of.
/// </summary>
/// <remarks>
/// The bridge between the two tiers. Bodies arrive as markdown from GitHub and as HTML from
/// Stack Overflow, and somewhere inside a few of them is a real unified diff. Everything found
/// here is offered to <see cref="UnifiedDiffParser"/>; whatever parses becomes appliable, and
/// whatever does not is <b>kept and shown as a snippet</b> rather than thrown away.
/// <para>
/// That last point is the whole design. A code block that is not a diff is usually the most
/// useful thing on the page - it is the corrected function, written out - and discarding it
/// because a machine cannot apply it automatically would throw away the answer in order to
/// preserve a tidy pipeline.
/// </para>
/// </remarks>
public static partial class CodeBlockExtractor
{
    [GeneratedRegex(@"^[ \t]*```+[ \t]*(?<lang>[A-Za-z0-9_+#-]*)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex FencePattern();

    /// <summary>
    /// Matches a Stack Overflow code block, capturing the attributes of both tags.
    /// </summary>
    /// <remarks>
    /// Both, because the language class lands on either one depending on how the post was
    /// written: <c>pre class="lang-py"</c> from the editor's own highlighting, and
    /// <c>code class="hljs language-python"</c> from the renderer. Reading only one of them
    /// loses the language on about half of real answers.
    /// </remarks>
    [GeneratedRegex(@"<pre(?<preAttrs>[^>]*)>\s*(?:<code(?<codeAttrs>[^>]*)>)?(?<code>.*?)(?:</code>)?\s*</pre>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PreBlockPattern();

    [GeneratedRegex(@"class\s*=\s*[""'](?<class>[^""']*)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ClassPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    /// <summary>Fence tags that say outright that a block is a diff.</summary>
    private static readonly string[] DiffLanguages = ["diff", "patch", "udiff"];

    /// <summary>Line starts that mark a block as a diff even with no tag at all.</summary>
    private static readonly string[] DiffOpeners = ["diff --git ", "--- ", "Index: ", "@@ "];

    /// <summary>Extracts every code block from a candidate's body.</summary>
    /// <param name="isHtml">True for a Stack Overflow body, false for GitHub markdown.</param>
    public static IReadOnlyList<CodeBlock> Extract(string? body, bool isHtml)
    {
        if (string.IsNullOrWhiteSpace(body)) return [];

        var blocks = isHtml ? FromHtml(body) : FromMarkdown(body);

        // A diff pasted with no fence at all is common in issue bodies, where people paste the
        // output of git diff straight in. Only looked for when nothing fenced turned out to be
        // a patch, so a properly fenced diff is never found twice.
        if (!blocks.Any(b => b.Patch is not null))
        {
            var loose = FromUnfencedRun(body, isHtml);
            if (loose is not null) blocks.Add(loose);
        }

        return blocks;
    }

    /// <summary>The parsed patches among the blocks, best-formed first.</summary>
    public static IReadOnlyList<ParsedPatch> PatchesIn(IEnumerable<CodeBlock> blocks) =>
        [.. blocks.Where(b => b.IsAppliablePatch).Select(b => b.Patch!)];

    // ------------------------------------------------------------------ markdown

    private static List<CodeBlock> FromMarkdown(string body)
    {
        var blocks = new List<CodeBlock>();
        var text = body.ReplaceLineEndings("\n");

        var fences = FencePattern().Matches(text);

        // Fences pair up: open, close, open, close. An unpaired trailing fence is ignored
        // rather than read to the end of the document, which would swallow the whole comment.
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

    // ------------------------------------------------------------------ html

    /// <summary>
    /// Reads Stack Overflow's markup, where code is <c>pre</c> wrapping <c>code</c>.
    /// </summary>
    /// <remarks>
    /// Entity decoding is not optional here. A diff in an answer arrives with every
    /// <c>&lt;</c> and <c>&amp;</c> escaped, and a parser fed the escaped form sees lines that
    /// begin with an ampersand rather than a plus or a minus.
    /// </remarks>
    private static List<CodeBlock> FromHtml(string body)
    {
        var blocks = new List<CodeBlock>();

        foreach (Match match in PreBlockPattern().Matches(body))
        {
            var inner = match.Groups["code"].Value;

            // Nested spans from syntax highlighting carry no meaning once this is plain text.
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

    // ------------------------------------------------------------------ unfenced

    /// <summary>
    /// Finds a diff that was pasted with no fence around it.
    /// </summary>
    /// <remarks>
    /// Starts at the first line that opens a diff and runs to the end of the body. That is
    /// deliberately crude - trimming it correctly would mean knowing where the diff stops, which
    /// is what the parser is for - and the parser's own reconciliation check is what decides
    /// whether the result is usable.
    /// </remarks>
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

        // Only worth reporting when it actually parsed. An unfenced run that fails is almost
        // always ordinary prose that happened to start with a dash, and surfacing that as a
        // "failed patch" would be noise on nearly every candidate.
        return classified.Patch is { Ok: true } ? classified : null;
    }

    // ------------------------------------------------------------------ classification

    /// <summary>
    /// Decides whether a block is a patch by parsing it, never by looking at it.
    /// </summary>
    /// <remarks>
    /// A fence tagged <c>diff</c> is a claim, not a fact. The only thing that makes a block
    /// appliable is that the parser accepted it, so the tag is used to decide whether a failure
    /// is worth reporting - not whether to try.
    /// </remarks>
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
