namespace FixFinder.Core.Parsing;

/// <summary>
/// Picks the frame a fix most likely belongs in, and with it the answer to "is searching the
/// web worth anything for this crash?"
/// </summary>
/// <remarks>
/// Selection runs against the <b>root cause</b>, not the outermost exception. A wrapped failure
/// prints "Could not load the basket" at the top, but the frame worth looking at is wherever the
/// inner NullReferenceException actually came from.
/// </remarks>
public static class CulpritFrameSelector
{
    /// <summary>
    /// Chooses a culprit frame for <paramref name="error"/> and every error in its cause chain.
    /// </summary>
    /// <remarks>
    /// Preference order:
    /// <list type="number">
    /// <item>the innermost frame inside a configured source root - the user's own code;</item>
    /// <item>the innermost frame that is neither vendored nor runtime, for the common case
    /// where no source root has been set yet;</item>
    /// <item>the innermost frame of any kind, so there is always an answer when there are frames
    /// at all.</item>
    /// </list>
    /// </remarks>
    public static ErrorFrame? Select(ParsedError error, IReadOnlyList<string> sourceRoots)
    {
        FrameClassifier.Classify(error, sourceRoots);

        foreach (var cause in error.Causes) Select(cause, sourceRoots);

        var target = error.RootCause;
        var chosen = Choose(target.Frames);

        // The culprit is set on both the root cause and the error handed in, so callers that
        // only ever look at the top-level ParsedError still get a useful location.
        target.CulpritFrame = chosen;
        error.CulpritFrame = chosen ?? Choose(error.Frames);

        return error.CulpritFrame;
    }

    private static ErrorFrame? Choose(IReadOnlyList<ErrorFrame> frames)
    {
        if (frames.Count == 0) return null;

        foreach (var frame in frames)
            if (frame.Origin == FrameOrigin.FirstParty) return frame;

        foreach (var frame in frames)
            if (frame.Origin == FrameOrigin.Unknown && frame.File is not null) return frame;

        return frames[0];
    }

    /// <summary>
    /// True when the crash is in the user's own code, where a web search is unlikely to help.
    /// </summary>
    /// <remarks>
    /// <see cref="FrameOrigin.Unknown"/> counts as first-party. Once a source root is set,
    /// anything that is neither vendored nor runtime is almost certainly the user's own file
    /// that simply was not matched - and over-promising that search will help is the failure
    /// mode worth avoiding here.
    /// </remarks>
    public static bool CulpritIsFirstParty(ParsedError error) =>
        error.CulpritFrame?.Origin is FrameOrigin.FirstParty or FrameOrigin.Unknown;

    /// <summary>
    /// The nearest third-party module in the chain, which becomes an extra search term.
    /// </summary>
    /// <remarks>
    /// When a crash comes out of a library, the library's name is the single most valuable thing
    /// to put in the query - it is what turns a generic "NullReferenceException" search into one
    /// that can actually find the right issue in the right repository.
    /// </remarks>
    public static string? NearestThirdPartyModule(ParsedError error)
    {
        foreach (var frame in error.RootCause.Frames)
        {
            if (frame.Origin != FrameOrigin.ThirdParty) continue;
            if (frame.Module is { Length: > 0 }) return frame.Module;
            if (frame.File is not null) return PackageNameFromPath(frame.File);
        }

        return null;
    }

    /// <summary>Pulls the package name out of a vendored path, e.g. .../node_modules/express/lib/x.js -> express.</summary>
    private static string? PackageNameFromPath(string file)
    {
        var parts = file.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < parts.Length - 1; i++)
        {
            var isVendorDirectory = parts[i] is "node_modules" or "site-packages" or "dist-packages"
                or "packages" or "registry" or "gems" or "vendor";
            if (!isVendorDirectory) continue;

            var name = parts[i + 1];

            // npm scoped packages are two segments: @scope/name.
            if (name.StartsWith('@') && i + 2 < parts.Length) return $"{name}/{parts[i + 2]}";

            return name;
        }

        return null;
    }
}
