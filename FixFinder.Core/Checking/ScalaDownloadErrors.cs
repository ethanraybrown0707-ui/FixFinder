using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.Checking;

/// <summary>
/// What Scala CLI says when something a program needs is not in its cache on this computer - a library a using directive
/// names, or the version of Scala it asks for - told apart from mistakes in the code, and said once, naming each.
/// </summary>
/// <remarks>
/// FixFinder runs Scala CLI offline, so it downloads nothing: what is not in the cache, it reports as
/// "Error downloading org.typelevel:cats-core_3:2.10.0" - the group, the name with the Scala it is built for, and the version.
/// </remarks>
public static class ScalaDownloadErrors
{
    private const string Lead = "Error downloading ";

    /// <summary>The errors that are mistakes in the code, and the note that says what the rest were.</summary>
    public static LibraryErrors.Sorted Sort(IReadOnlyList<ParsedError> errors, string chosen)
    {
        var notCached = errors.Where(error => error.ExceptionType == ScalaCompileParser.MissingDownload).ToList();
        if (notCached.Count == 0) return new LibraryErrors.Sorted(errors, null, 0);

        var named = notCached
            .Select(error => (error.Message ?? "").StartsWith(Lead, StringComparison.Ordinal) ? (error.Message ?? "")[Lead.Length..].Trim() : "")
            .Where(artifact => artifact.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var what = named.Count switch
        {
            0 => "Something it needs is not in Scala CLI's cache on this computer.",
            1 => $"It needs {named[0]}, which is not in Scala CLI's cache on this computer.",
            _ => $"It needs {string.Join(", ", named.Take(named.Count - 1))} and {named[^1]}, which are not in Scala CLI's cache on this computer.",
        };

        var note = $"{what} FixFinder never downloads anything - it runs Scala CLI offline - so {Path.GetFileName(chosen)} was not built, which is not a " +
                   "mistake in the code. Building it once with Scala CLI while online - scala-cli compile . in its folder - downloads what it needs, and FixFinder " +
                   "builds it after that.";

        return new LibraryErrors.Sorted(errors.Except(notCached).ToList(), note, notCached.Count, "It needs something that is not on this computer, so it was not built");
    }
}
