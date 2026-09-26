namespace FixFinder.Core.Checking;

/// <summary>Where to read more about what a finding is about, and what to call that place.</summary>
/// <param name="SiteName">The documentation being offered, named so the reader knows where they are being sent.</param>
/// <param name="Term">What will be searched for.</param>
public sealed record FurtherReading(string SiteName, string Term, string Url);

/// <summary>
/// Sends a reader to the official documentation for the language they are writing in.
/// </summary>
/// <remarks>
/// These are searches, never deep links to particular pages. A link to a specific page is a claim that the page exists
/// and says what it is being cited for, and pages move; a search on the official site cannot be wrong about anything,
/// and if it finds nothing then nothing false was said. Every address here was fetched and checked rather than
/// remembered - one that looked obvious turned out to be a 404 and is not in this list.
/// <para>
/// Only languages with a documentation search that was actually verified appear. A language missing from here gets no
/// link rather than a guessed one.
/// </para>
/// </remarks>
public static class Documentation
{
    private sealed record Site(string Name, string Before, string After = "");

    /// <summary>The official documentation search for each language, by the extension of the file being checked.</summary>
    private static readonly Dictionary<string, Site> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".py"] = new("the Python documentation", "https://docs.python.org/3/search.html?q="),
        [".pyw"] = new("the Python documentation", "https://docs.python.org/3/search.html?q="),

        [".js"] = new("MDN Web Docs", "https://developer.mozilla.org/en-US/search?q="),
        [".mjs"] = new("MDN Web Docs", "https://developer.mozilla.org/en-US/search?q="),
        [".cjs"] = new("MDN Web Docs", "https://developer.mozilla.org/en-US/search?q="),

        [".cs"] = new("Microsoft Learn", "https://learn.microsoft.com/en-us/search/?terms="),

        [".c"] = new("cppreference", "https://en.cppreference.com/w/index.php?search="),
        [".h"] = new("cppreference", "https://en.cppreference.com/w/index.php?search="),
        [".cpp"] = new("cppreference", "https://en.cppreference.com/w/index.php?search="),
        [".cc"] = new("cppreference", "https://en.cppreference.com/w/index.php?search="),
        [".cxx"] = new("cppreference", "https://en.cppreference.com/w/index.php?search="),
        [".hpp"] = new("cppreference", "https://en.cppreference.com/w/index.php?search="),

        [".go"] = new("the Go package documentation", "https://pkg.go.dev/search?q="),

        [".java"] = new("the Oracle Java documentation", "https://docs.oracle.com/search/?q="),
    };

    /// <summary>Where to read more about this finding, or null when there is nowhere reliable to send anybody.</summary>
    public static FurtherReading? For(Finding finding)
    {
        if (!ByExtension.TryGetValue(Path.GetExtension(finding.File), out var site)) return null;
        if (Subject(finding) is not { Length: > 0 } term) return null;

        return new FurtherReading(site.Name, term, site.Before + Uri.EscapeDataString(term) + site.After);
    }

    /// <summary>
    /// What this finding is about, in the words the documentation would use: what the language called the failure, or
    /// failing that, what the rule that found it is named after.
    /// </summary>
    private static string? Subject(Finding finding)
    {
        if (finding.Error?.ShortExceptionType is { Length: > 0 } thrown) return thrown;
        if (finding.Error?.ErrorCode is { Length: > 0 } code) return code;

        var rule = finding.RuleId;
        if (LookedUpAs.TryGetValue((rule, Guides.Guidebook.LanguageName(finding.File)), out var named)) return named;

        // Rule names read as what they are about once the language prefix is off: analysis-division-by-zero.
        if (rule.Length == 0) return null;

        var withoutPrefix = rule.Split('-', 2) is [var first, var rest] && Prefixes.Contains(first) ? rest : rule;
        var words = withoutPrefix.Replace('-', ' ').Trim();

        return words.Length < 3 ? null : words;
    }

    /// <summary>
    /// Where the useful thing to read about is what the fix uses rather than what the rule is called: a loop that searches
    /// a list is sped up with the language's set, so that is what is looked up.
    /// </summary>
    private static readonly Dictionary<(string Rule, string Language), string> LookedUpAs = new()
    {
        [("analysis-repeated-search", "Python")] = "set",
        [("analysis-repeated-search", "Java")] = "HashSet",
        [("analysis-repeated-search", "C#")] = "HashSet",
        [("analysis-repeated-search", "JavaScript")] = "Set",
    };

    private static readonly HashSet<string> Prefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "analysis", "logic", "python", "java", "csharp", "c", "cpp", "js", "go", "local",
    };
}
