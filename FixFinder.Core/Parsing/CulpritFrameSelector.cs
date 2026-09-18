namespace FixFinder.Core.Parsing;

/// <summary>Picks the frame a fix most likely belongs in, and with it the answer to "is searching the web worth anything for this
/// crash?"</summary>
public static class CulpritFrameSelector
{
    public static ErrorFrame? Select(ParsedError error, IReadOnlyList<string> sourceRoots)
    {
        FrameClassifier.Classify(error, sourceRoots);

        foreach (var cause in error.Causes) Select(cause, sourceRoots);

        var target = error.RootCause;
        var chosen = Choose(target.Frames);

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

    public static bool CulpritIsFirstParty(ParsedError error) =>
        error.CulpritFrame?.Origin is FrameOrigin.FirstParty or FrameOrigin.Unknown;

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

    private static string? PackageNameFromPath(string file)
    {
        var parts = file.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < parts.Length - 1; i++)
        {
            var isVendorDirectory = parts[i] is "node_modules" or "site-packages" or "dist-packages"
                or "packages" or "registry" or "gems" or "vendor";
            if (!isVendorDirectory) continue;

            var name = parts[i + 1];

            if (name.StartsWith('@') && i + 2 < parts.Length) return $"{name}/{parts[i + 2]}";

            return name;
        }

        return null;
    }
}
