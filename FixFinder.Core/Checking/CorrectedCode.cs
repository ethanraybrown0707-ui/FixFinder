using FixFinder.Core.LocalFixes;
using FixFinder.Core.Patching;
using SourceFile = FixFinder.Core.LocalFixes.SourceFile;
using FixFinder.Core.Sources;

namespace FixFinder.Core.Checking;

/// <summary>The lines of a file as a fix leaves them, with a line either side so they are easy to find.</summary>
public static class CorrectedCode
{
    public static string From(LocalFix fix, SourceFile? source)
    {
        var lines = new List<string>();

        if (source is not null && fix.StartLine >= 2 && source.Line(fix.StartLine - 1) is { } before && before.Trim().Length > 0)
            lines.Add(before);

        lines.AddRange(fix.NewLines);

        if (source is not null && source.Line(fix.StartLine + fix.RemoveCount) is { } after && after.Trim().Length > 0)
            lines.Add(after);

        return Dedented(lines);
    }

    public static string? From(FixCandidate candidate)
    {
        if (candidate.LocalFix is { } fix) return From(fix, SourceFile.Read(fix.File));

        if (DiffIn(candidate.BodyText) is not { } diff) return null;

        var patch = UnifiedDiffParser.Parse(diff);
        if (!patch.Ok || patch.Files[0].Hunks is not [var hunk, ..]) return null;

        return Dedented(hunk.NewSide.Select(line => line.Text).ToList());
    }

    private static string? DiffIn(string body)
    {
        const string fence = "```diff";

        var start = body.IndexOf(fence, StringComparison.Ordinal);
        if (start < 0) return null;

        start = body.IndexOf('\n', start) + 1;
        var end = body.IndexOf("```", start, StringComparison.Ordinal);

        return start > 0 && end > start ? body[start..end] : null;
    }

    private static string Dedented(IReadOnlyList<string> lines)
    {
        var indents = lines.Where(line => line.Trim().Length > 0).Select(line => line.Length - line.TrimStart().Length).ToList();
        var common = indents.Count > 0 ? indents.Min() : 0;

        return string.Join("\n", lines.Select(line => line.Length >= common ? line[common..].TrimEnd() : line.TrimEnd()));
    }
}
