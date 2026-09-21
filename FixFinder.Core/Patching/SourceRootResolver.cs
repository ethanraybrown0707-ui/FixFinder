using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Patching;

/// <summary>How a source root was arrived at.</summary>
public enum SourceRootOrigin
{
    NotFound,

    UserSpecified,

    StackTracePaths,

    PortablePdb,

    SiblingHeuristic,
}

public sealed record SourceRootResult(
    string? Path, SourceRootOrigin Origin, string Explanation, bool NeedsConfirmation = false)
{
    public static SourceRootResult NotFound(string explanation) =>
        new(null, SourceRootOrigin.NotFound, explanation);
}

/// <summary>Works out where the target program's source code lives.</summary>
public static class SourceRootResolver
{
    private static readonly string[] ProjectMarkers =
    [
        "*.sln", "*.csproj", "*.fsproj", "*.vbproj",
        "package.json", "pyproject.toml", "setup.py", "requirements.txt",
        "go.mod", "Cargo.toml", "Gemfile", "pom.xml", "build.gradle", "build.gradle.kts",
        "CMakeLists.txt", "Makefile", "composer.json", ".git",
    ];

    private const int MaxWalkUp = 8;

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

    private static SourceRootResult? FromStackTrace(ParsedError error)
    {
        string? firstRealFile = null;

        foreach (var frame in EnumerateFrames(error))
        {
            if (frame.File is null) continue;
            if (!Path.IsPathRooted(frame.File)) continue;
            if (!File.Exists(frame.File)) continue;

            if (FrameClassifier.IsVendored(frame.File)) continue;

            firstRealFile ??= frame.File;

            var root = WalkUpToProjectMarker(Path.GetDirectoryName(frame.File));
            if (root is null) continue;

            return new SourceRootResult(root, SourceRootOrigin.StackTracePaths,
                $"found by walking up from {Path.GetFileName(frame.File)} in the stack trace to the nearest project file");
        }

        if (firstRealFile is null) return null;

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
            return false;
        }

        return false;
    }
}
