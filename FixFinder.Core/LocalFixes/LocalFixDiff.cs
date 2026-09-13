using System.Text;

namespace FixFinder.Core.LocalFixes;

/// <summary>Writes a <see cref="LocalFix"/> as a unified diff.</summary>
/// <remarks>
/// A diff rather than a finished block of code, so that a fix worked out here travels exactly the
/// road a patch downloaded from a stranger does - extracted, parsed, path-mapped and matched against
/// the file with exact context - before anyone is shown it. One line of context either side is
/// enough to pin it: the planner refuses a match that is not unique.
/// </remarks>
public static class LocalFixDiff
{
    /// <summary>The diff, or null when this change cannot be stated safely as one.</summary>
    /// <remarks>
    /// A change that reaches the end of a file with no final newline is refused. Saying it
    /// correctly needs <c>\ No newline at end of file</c> on one side and not the other, and a
    /// rule that got that wrong would hand over a block that silently joins two lines.
    /// </remarks>
    public static string? Render(SourceFile source, LocalFix fix, string path)
    {
        var total = source.Count;
        var start = fix.StartLine;
        var removed = fix.RemoveCount;

        if (start < 1 || start - 1 + removed > total) return null;

        var before = start >= 2 ? start - 1 : 0;
        var after = start + removed <= total ? start + removed : 0;

        if (after == 0 && !source.EndsWithNewline) return null;

        var oldStart = before > 0 ? before : start;
        var oldCount = (before > 0 ? 1 : 0) + removed + (after > 0 ? 1 : 0);
        var newCount = (before > 0 ? 1 : 0) + fix.NewLines.Count + (after > 0 ? 1 : 0);

        if (oldCount == 0 || newCount == 0) return null;

        var diff = new StringBuilder()
            .Append("--- a/").Append(path).Append('\n')
            .Append("+++ b/").Append(path).Append('\n')
            .Append($"@@ -{oldStart},{oldCount} +{oldStart},{newCount} @@\n");

        if (before > 0) diff.Append(' ').Append(source.Lines[before - 1]).Append('\n');

        for (var i = 0; i < removed; i++)
            diff.Append('-').Append(source.Lines[start - 1 + i]).Append('\n');

        foreach (var line in fix.NewLines)
            diff.Append('+').Append(line).Append('\n');

        if (after > 0)
        {
            diff.Append(' ').Append(source.Lines[after - 1]).Append('\n');
            if (after == total && !source.EndsWithNewline) diff.Append("\\ No newline at end of file\n");
        }

        return diff.ToString();
    }
}
