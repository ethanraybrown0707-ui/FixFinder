using System.Net;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Sources;

/// <summary>Turns a Stack Exchange answer body into readable plain text.</summary>
public static partial class HtmlText
{
    [GeneratedRegex(@"<br\s*/?>|</p>|</div>|</li>|</h\d>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakPattern();

    [GeneratedRegex(@"</pre>", RegexOptions.IgnoreCase)]
    private static partial Regex PreEndPattern();

    [GeneratedRegex(@"<li[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRunPattern();

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex TrailingSpacePattern();

    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        var text = html;

        text = PreEndPattern().Replace(text, "\n");
        text = BreakPattern().Replace(text, "\n");
        text = ListItemPattern().Replace(text, "\n  - ");
        text = TagPattern().Replace(text, "");

        text = WebUtility.HtmlDecode(text);

        text = text.ReplaceLineEndings("\n");
        text = TrailingSpacePattern().Replace(text, "\n");
        text = BlankRunPattern().Replace(text, "\n\n");

        return text.Trim();
    }

    public static string Excerpt(string? text, int maximum)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var flat = text.ReplaceLineEndings(" ").Trim();
        while (flat.Contains("  ", StringComparison.Ordinal)) flat = flat.Replace("  ", " ");

        if (flat.Length <= maximum) return flat;

        var cut = flat.LastIndexOf(' ', Math.Min(maximum, flat.Length - 1));
        return (cut > maximum / 2 ? flat[..cut] : flat[..maximum]) + "…";
    }
}
