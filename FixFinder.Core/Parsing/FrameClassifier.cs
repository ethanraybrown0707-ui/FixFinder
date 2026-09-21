namespace FixFinder.Core.Parsing;

/// <summary>Decides whose code each frame belongs to: the user's, a package's, or the runtime's.</summary>
public static class FrameClassifier
{
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
        "/program files/go/src/",
        "/usr/local/go/src/",
        "/usr/lib/go/src/",
        "/usr/lib/golang/src/",
        "/sdk/go1.",
        "/cellar/go/",
        "/hostedtoolcache/",
        "java.base/",
        "/jdk",
        "/microsoft visual studio/",
        "/include/c++/",
        "/lib/gcc/",
        "/windowsapps/",
        "/windows kits/",
    ];

    public static bool IsVendored(string? file)
    {
        if (file is null) return false;

        var normalized = file.Replace('\\', '/');

        return VendorSegments.Any(s => normalized.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
               RuntimeSegments.Any(s => normalized.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
               IsPythonStandardLibrary(normalized);
    }

    public static void Classify(ParsedError error, IReadOnlyList<string> sourceRoots)
    {
        var roots = sourceRoots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(NormalizeDirectory)
            .ToArray();

        foreach (var frame in error.Frames)
        {
            if (frame.Origin != FrameOrigin.Unknown) continue;
            frame.Origin = ClassifyOne(frame.File, roots);

            if (frame.Origin == FrameOrigin.Unknown && error.LanguageId == "java" && IsJdkSymbol(frame.Symbol))
                frame.Origin = FrameOrigin.Runtime;

            if (frame.Origin == FrameOrigin.Unknown && error.LanguageId == "python" && IsPythonStandardLibrary(frame.File))
                frame.Origin = FrameOrigin.Runtime;
        }

        foreach (var cause in error.Causes) Classify(cause, sourceRoots);
    }

    private static bool IsPythonStandardLibrary(string? file)
    {
        if (file is null) return false;

        var normalized = Normalize(file);
        var lib = normalized.LastIndexOf("/lib/", StringComparison.OrdinalIgnoreCase);
        if (lib <= 0) return false;

        var home = normalized[..lib];

        try
        {
            return File.Exists(home + "/python.exe") || File.Exists(home + "/python") || File.Exists(home + "/bin/python3");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsJdkSymbol(string? symbol) =>
        symbol is not null &&
        new[] { "java.", "javax.", "jdk.", "sun.", "com.sun." }.Any(prefix => symbol.StartsWith(prefix, StringComparison.Ordinal));

    private static FrameOrigin ClassifyOne(string? file, IReadOnlyList<string> roots)
    {
        if (file is null) return FrameOrigin.Unknown;

        var normalized = Normalize(file);

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
