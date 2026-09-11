using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Patching;

/// <summary>How a source root was arrived at. Always shown to the user alongside the path.</summary>
public enum SourceRootOrigin
{
    NotFound,

    /// <summary>The user typed or browsed to it. Wins over everything else.</summary>
    UserSpecified,

    /// <summary>Read from file paths embedded in the stack trace.</summary>
    StackTracePaths,

    /// <summary>Read from the document table of a portable PDB beside the target.</summary>
    PortablePdb,

    /// <summary>Guessed from a naming convention. Offered as a suggestion, never applied silently.</summary>
    SiblingHeuristic,
}

/// <param name="Path">The resolved folder, or null.</param>
/// <param name="Origin">How it was found.</param>
/// <param name="Explanation">Plain-English account, shown under the field in step 2.</param>
/// <param name="NeedsConfirmation">True for a guess the user should look at before it is used.</param>
public sealed record SourceRootResult(
    string? Path, SourceRootOrigin Origin, string Explanation, bool NeedsConfirmation = false)
{
    public static SourceRootResult NotFound(string explanation) =>
        new(null, SourceRootOrigin.NotFound, explanation);
}

/// <summary>
/// Works out where the target program's source code lives.
/// </summary>
/// <remarks>
/// Nothing downstream is safe without this. It bounds every file FixFinder may write, so the
/// result and <b>how it was reached</b> are always surfaced and always editable - a source root
/// that was guessed and one the user typed carry very different weight, and hiding the
/// difference would be the wrong kind of convenience.
/// </remarks>
public static class SourceRootResolver
{
    /// <summary>Files that mark the root of a project in the languages FixFinder parses.</summary>
    private static readonly string[] ProjectMarkers =
    [
        "*.sln", "*.csproj", "*.fsproj", "*.vbproj",
        "package.json", "pyproject.toml", "setup.py", "requirements.txt",
        "go.mod", "Cargo.toml", "Gemfile", "pom.xml", "build.gradle", "build.gradle.kts",
        "CMakeLists.txt", "Makefile", "composer.json", ".git",
    ];

    /// <summary>How far up from a source file to look for a project marker.</summary>
    private const int MaxWalkUp = 8;

    /// <summary>
    /// Resolves a source root, trying each strategy in descending order of trustworthiness.
    /// </summary>
    /// <param name="userSpecified">What the user typed in step 2, if anything.</param>
    /// <param name="error">The parsed error, whose frames carry real paths when built locally.</param>
    /// <param name="spec">The target, used to find a PDB and to try the sibling heuristic.</param>
    public static SourceRootResult Resolve(string? userSpecified, ParsedError? error, TargetSpec? spec)
    {
        if (!string.IsNullOrWhiteSpace(userSpecified))
        {
            var trimmed = userSpecified.Trim();
            return Directory.Exists(trimmed)
                ? new SourceRootResult(Path.GetFullPath(trimmed), SourceRootOrigin.UserSpecified, "you set this")
                : SourceRootResult.NotFound($"'{trimmed}' is not a folder that exists.");
        }

        if (error is not null && FromStackTrace(error) is { } fromTrace) return fromTrace;
        if (spec is not null && FromPdb(spec) is { } fromPdb) return fromPdb;
        if (spec is not null && FromSiblingFolder(spec) is { } fromSibling) return fromSibling;

        return SourceRootResult.NotFound(
            "Could not work out where the source is. The stack trace carries no local file " +
            "paths, and there is no readable PDB beside the target. Set the folder with Browse.");
    }

    /// <summary>
    /// Walks up from a frame's file to the nearest project marker.
    /// </summary>
    /// <remarks>
    /// The highest-value strategy in practice: anything compiled or run on this machine has real
    /// paths in its trace, and those paths point at the actual source, not at a guess.
    /// </remarks>
    private static SourceRootResult? FromStackTrace(ParsedError error)
    {
        string? firstRealFile = null;

        foreach (var frame in EnumerateFrames(error))
        {
            if (frame.File is null) continue;
            if (!Path.IsPathRooted(frame.File)) continue;
            if (!File.Exists(frame.File)) continue;

            // Never root in somebody else's package. The innermost frame of a crash inside a
            // library is in site-packages or node_modules, and rooting there would bound every
            // file FixFinder may write to the inside of an installed dependency - the one place
            // a patch must never land, since the next install overwrites it and the change
            // belongs upstream anyway.
            if (FrameClassifier.IsVendored(frame.File)) continue;

            firstRealFile ??= frame.File;

            var root = WalkUpToProjectMarker(Path.GetDirectoryName(frame.File));
            if (root is null) continue;

            return new SourceRootResult(root, SourceRootOrigin.StackTracePaths,
                $"found by walking up from {Path.GetFileName(frame.File)} in the stack trace to the nearest project file");
        }

        if (firstRealFile is null) return null;

        // The trace points at a real file on this machine, but nothing above it looks like a
        // project - a loose script, or a folder with no manifest. Its own directory is the only
        // defensible answer, and it is a good one: it bounds writes to where the crashing file
        // actually lives. Flagged for confirmation because it is narrower than a project root
        // and a patch touching a sibling folder would be refused under it.
        var directory = Path.GetDirectoryName(firstRealFile)!;

        return new SourceRootResult(directory, SourceRootOrigin.StackTracePaths,
            $"taken from the folder holding {Path.GetFileName(firstRealFile)} - the stack trace points there, " +
            "but no project file (.csproj, package.json, pyproject.toml, go.mod, .git and so on) was found above it",
            NeedsConfirmation: true);
    }

    private static SourceRootResult? FromPdb(TargetSpec spec)
    {
        var common = PortablePdbReader.FindCommonRoot(spec.ExecutablePath);
        if (common is null) return null;

        var root = WalkUpToProjectMarker(common) ?? common;

        return new SourceRootResult(root, SourceRootOrigin.PortablePdb,
            $"read from {Path.GetFileNameWithoutExtension(spec.ExecutablePath)}.pdb, which records the path of every source file in the build");
    }

    /// <summary>
    /// Handles the "…\Foo-broken\app.dll next to …\Foo\" convention.
    /// </summary>
    /// <remarks>
    /// Returned with <c>NeedsConfirmation</c> set. It is a guess from a folder name and nothing
    /// more, and a wrong source root is the one mistake that could put a patch in the wrong
    /// repository - so this one always stops and asks.
    /// </remarks>
    private static SourceRootResult? FromSiblingFolder(TargetSpec spec)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(spec.ExecutablePath));

        for (var depth = 0; depth < MaxWalkUp && directory is not null; depth++)
        {
            var name = Path.GetFileName(directory);
            var suffix = new[] { "-broken", "-copy", "-backup", "-old" }
                .FirstOrDefault(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase));

            if (suffix is not null)
            {
                var sibling = Path.Combine(
                    Path.GetDirectoryName(directory) ?? "", name[..^suffix.Length]);

                if (Directory.Exists(sibling) && HasProjectMarker(sibling))
                {
                    return new SourceRootResult(sibling, SourceRootOrigin.SiblingHeuristic,
                        $"guessed from the folder name: '{name}' sits beside '{Path.GetFileName(sibling)}', which looks like a project",
                        NeedsConfirmation: true);
                }
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static IEnumerable<ErrorFrame> EnumerateFrames(ParsedError error)
    {
        // Root cause first: its frames are where the failure actually happened.
        foreach (var frame in error.RootCause.Frames) yield return frame;

        foreach (var frame in error.Frames) yield return frame;

        foreach (var cause in error.Causes)
        foreach (var frame in EnumerateFrames(cause))
            yield return frame;
    }

    private static string? WalkUpToProjectMarker(string? directory)
    {
        for (var depth = 0; depth < MaxWalkUp && directory is not null; depth++)
        {
            if (HasProjectMarker(directory)) return directory;
            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static bool HasProjectMarker(string directory)
    {
        try
        {
            foreach (var marker in ProjectMarkers)
            {
                if (marker.Contains('*'))
                {
                    if (Directory.EnumerateFiles(directory, marker).Any()) return true;
                    continue;
                }

                if (File.Exists(Path.Combine(directory, marker))) return true;
                if (Directory.Exists(Path.Combine(directory, marker))) return true;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // A folder we cannot read is a folder we cannot use as a source root.
            return false;
        }

        return false;
    }
}
