using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// How a finding points at another line of the program. A line in the finding's own file is named by its number alone;
/// a line in another file - a function it calls, written somewhere else - is named with that file, since the number on
/// its own would send the reader to the wrong place.
/// </summary>
internal static class Places
{
    /// <summary>"line 6", or "line 6 of helpers.py" when <paramref name="place"/> is in a different file from <paramref name="from"/>.</summary>
    public static string Line(SourceSpan place, SourceSpan from) =>
        SameFile(place, from) ? $"line {place.Line}" : $"line {place.Line} of {Path.GetFileName(place.File)}";

    public static bool SameFile(SourceSpan first, SourceSpan second) => string.Equals(first.File, second.File, StringComparison.OrdinalIgnoreCase);
}
