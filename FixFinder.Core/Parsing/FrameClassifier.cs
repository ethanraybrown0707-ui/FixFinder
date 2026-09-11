namespace FixFinder.Core.Parsing;

/// <summary>
/// Decides whose code each frame belongs to: the user's, a package's, or the runtime's.
/// </summary>
/// <remarks>
/// This is what lets FixFinder be honest about when searching the web is pointless. If the
/// innermost frame is inside the user's own source, no GitHub issue or Stack Overflow answer
/// exists for it - it is their bug, in their code, written today. Saying so is far more useful
/// than presenting thirty confident-looking but irrelevant links.
/// </remarks>
public static class FrameClassifier
{
    /// <summary>
    /// Path fragments that mean "somebody else's code".
    /// </summary>
    /// <remarks>
    /// Written with forward slashes and matched against a separator-normalised path, so one
    /// entry covers both Windows and POSIX spellings.
    /// </remarks>
    private static readonly string[] VendorSegments =
    [
        "/node_modules/",
        "/site-packages/",
        "/dist-packages/",
        "/.cargo/registry/",
        "/.rustup/",
        "/pkg/mod/",
        "/.nuget/packages/",
        "/.gradle/caches/",
        "/.m2/repository/",
        "/vendor/",
        "/gems/",
        "/bower_components/",
    ];

    /// <summary>Fragments that mean the language runtime or standard library itself.</summary>
    private static readonly string[] RuntimeSegments =
    [
        "node:",
        "/microsoft.netcore.app/",
        "/microsoft.windowsdesktop.app/",
        "/library/std/src/",
        "/rustc/",
        "/usr/lib/",
        "/usr/local/lib/",
        "/lib/python",
        "/python3",
        "/goroot/",
        "java.base/",
        "/jdk",
    ];

    /// <summary>
    /// True when a path sits inside installed dependencies or the language runtime.
    /// </summary>
    /// <remarks>
    /// Exposed separately from <see cref="Classify"/> because it answers a question that has
    /// nothing to do with a parsed frame: whether a path is somewhere FixFinder should be
    /// willing to write. <see cref="Patching.SourceRootResolver"/> asks it before rooting
    /// anywhere, so a crash inside a library cannot end up bounding patches to the inside of
    /// that library's install folder.
    /// </remarks>
    public static bool IsVendored(string? file)
    {
        if (file is null) return false;

        var normalized = file.Replace('\\', '/');

        return VendorSegments.Any(s => normalized.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
               RuntimeSegments.Any(s => normalized.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Assigns <see cref="ErrorFrame.Origin"/> to every frame of an error and its causes.</summary>
    public static void Classify(ParsedError error, IReadOnlyList<string> sourceRoots)
    {
        var roots = sourceRoots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(NormalizeDirectory)
            .ToArray();

        foreach (var frame in error.Frames)
        {
            // A parser that already knows better - Node marking its own node:internal frames -
            // wins over path guessing.
            if (frame.Origin != FrameOrigin.Unknown) continue;
            frame.Origin = ClassifyOne(frame.File, roots);
        }

        foreach (var cause in error.Causes) Classify(cause, sourceRoots);
    }

    private static FrameOrigin ClassifyOne(string? file, IReadOnlyList<string> roots)
    {
        if (file is null) return FrameOrigin.Unknown;

        var normalized = Normalize(file);

        // Vendor and runtime are checked before the source root, because a package restored
        // *into* the project folder (node_modules is the obvious case) is inside the root but
        // is emphatically not the user's code.
        foreach (var segment in VendorSegments)
            if (normalized.Contains(segment, StringComparison.OrdinalIgnoreCase)) return FrameOrigin.ThirdParty;

        foreach (var segment in RuntimeSegments)
            if (normalized.Contains(segment, StringComparison.OrdinalIgnoreCase)) return FrameOrigin.Runtime;

        foreach (var root in roots)
            if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return FrameOrigin.FirstParty;

        return FrameOrigin.Unknown;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string NormalizeDirectory(string path)
    {
        var normalized = Normalize(path.Trim());
        return normalized.EndsWith('/') ? normalized : normalized + '/';
    }
}
