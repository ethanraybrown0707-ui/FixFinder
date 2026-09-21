using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing;

/// <summary>Small pieces every stack-trace parser needs, kept in one place so they behave alike.</summary>
internal static class ParserHelpers
{
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
            }
        }

        return cleaned;
    }

    public static string RawTextOf(IReadOnlyList<CapturedLine> lines, int start, int endExclusive)
    {
        var count = Math.Max(0, Math.Min(endExclusive, lines.Count) - start);
        return string.Join(Environment.NewLine, lines.Skip(start).Take(count).Select(l => l.Text));
    }

    public static int CountMatching(IReadOnlyList<string> lines, Func<string, bool> predicate)
    {
        var count = 0;
        foreach (var line in lines)
            if (predicate(line)) count++;
        return count;
    }
}
