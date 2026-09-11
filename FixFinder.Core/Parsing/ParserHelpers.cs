using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing;

/// <summary>Small pieces every stack-trace parser needs, kept in one place so they behave alike.</summary>
internal static class ParserHelpers
{
    /// <summary>
    /// Cleans a file path as printed in a trace into something the filesystem understands.
    /// </summary>
    /// <remarks>
    /// The awkward case is Node and ESM, which print <c>file:///C:/src/app.js</c>. Left as-is
    /// that never matches anything under a source root, so path mapping silently finds nothing.
    /// </remarks>
    public static string? CleanFilePath(string? path)
    {
        if (path is null) return null;

        var cleaned = path.Trim().Trim('"', '\'');
        if (cleaned.Length == 0) return null;

        if (cleaned.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) ||
            cleaned.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                cleaned = new Uri(cleaned).LocalPath;
            }
            catch (UriFormatException)
            {
                // Not a real URI after all - keep the original text rather than losing it.
            }
        }

        return cleaned;
    }

    /// <summary>Joins the claimed slice back into the text the program actually printed.</summary>
    public static string RawTextOf(IReadOnlyList<CapturedLine> lines, int start, int endExclusive)
    {
        var count = Math.Max(0, Math.Min(endExclusive, lines.Count) - start);
        return string.Join(Environment.NewLine, lines.Skip(start).Take(count).Select(l => l.Text));
    }

    /// <summary>Counts how many lines satisfy <paramref name="predicate"/>, for scoring in Detect.</summary>
    public static int CountMatching(IReadOnlyList<string> lines, Func<string, bool> predicate)
    {
        var count = 0;
        foreach (var line in lines)
            if (predicate(line)) count++;
        return count;
    }
}
