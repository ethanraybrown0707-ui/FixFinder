using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Checking;

/// <summary>
/// The errors go gives for a module a program's go.mod requires that is not in Go's module cache on this computer -
/// told apart from mistakes in the code, and said once, naming the modules and how to download them.
/// </summary>
/// <remarks>
/// Every go command FixFinder starts has GOPROXY=off, so go does not download such a module: it prints "go: downloading"
/// the module, which with no proxy downloads nothing, and then "module lookup disabled by GOPROXY=off" at each import of
/// it - as Go 1.27 says it. The module is the one of the go.mod's requirements whose path the import's path starts with.
/// </remarks>
public static partial class GoModuleErrors
{
    [GeneratedRegex(@"module lookup disabled by GOPROXY=off")]
    private static partial Regex LookupDisabled();

    [GeneratedRegex(@"""(?<path>[^""]+)""")]
    private static partial Regex QuotedPath();

    [GeneratedRegex(@"^require\s*\($")]
    private static partial Regex RequireBlockStart();

    /// <summary>A requirement of a go.mod: "require example.com/marks v1.2.0", or a line of a require block.</summary>
    [GeneratedRegex(@"^(?:require\s+)?(?<path>[^\s()]+)\s+(?<version>v\S+)$")]
    private static partial Regex Requirement();

    /// <summary>The errors that are mistakes in the code, and the note that says what the rest were.</summary>
    public static LibraryErrors.Sorted Sort(IReadOnlyList<ParsedError> errors, string chosen)
    {
        var notCached = errors.Where(error => LookupDisabled().IsMatch(error.Message ?? "")).ToList();
        if (notCached.Count == 0) return new LibraryErrors.Sorted(errors, null, 0);

        var requirements = Requirements(DeclaredGo.Of(ProgramCopy.OriginalOf(chosen))?.Module);
        var imported = notCached.Select(error => (File: error.Frames.FirstOrDefault()?.File, Import: ImportAt(error))).ToList();
        var imports = imported.Select(each => each.Import).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var importers = imported.Select(each => each.File).OfType<string>().Select(file => Path.GetFileName(file)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // The module an import is from: the requirement whose path is the import's, or the longest that starts it.
        var modules = imports
            .Select(import => requirements.Where(requirement => import == requirement.Path || import.StartsWith(requirement.Path + "/", StringComparison.Ordinal))
                .OrderByDescending(requirement => requirement.Path.Length)
                .Select(requirement => $"{requirement.Path} {requirement.Version}")
                .FirstOrDefault())
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var what = imports.Count > 0 && modules.Count > 0 && importers.Count > 0
            ? $"{And(importers)} {(importers.Count == 1 ? "imports" : "import")} {And(imports)}, from the module{(modules.Count == 1 ? "" : "s")} {And(modules)} " +
              $"its go.mod requires, which {(modules.Count == 1 ? "is" : "are")} not in Go's module cache on this computer."
            : "A module its go.mod requires is not in Go's module cache on this computer.";

        var note = $"{what} FixFinder never downloads anything - it runs go with GOPROXY=off, so the \"go: downloading\" go printed downloaded nothing - " +
                   "and so the program was not built, which is not a mistake in the code. Running go mod download in its module's folder downloads the " +
                   "modules it needs, and FixFinder builds it after that.";

        return new LibraryErrors.Sorted(errors.Except(notCached).ToList(), note, notCached.Count, "It needs a module that is not on this computer, so it was not built");
    }

    /// <summary>The path the import at an error's place imports, read from the line of the file the error is at.</summary>
    private static string? ImportAt(ParsedError error)
    {
        if (error.Frames.FirstOrDefault() is not { File: { } file, Line: > 0 and var line }) return null;

        try
        {
            var text = File.ReadLines(file).Skip(line - 1).FirstOrDefault();
            return text is not null && QuotedPath().Match(text) is { Success: true } quoted ? quoted.Groups["path"].Value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>What a go.mod requires: each module's path and version, from its require lines and blocks.</summary>
    private static List<(string Path, string Version)> Requirements(string? goMod)
    {
        var found = new List<(string, string)>();
        if (goMod is null) return found;

        IEnumerable<string> lines;
        try
        {
            lines = File.ReadAllLines(goMod);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return found;
        }

        var inRequireBlock = false;
        foreach (var line in lines.Select(raw => raw.Split("//")[0].Trim()).Where(line => line.Length > 0))
        {
            if (inRequireBlock && line.StartsWith(')'))
            {
                inRequireBlock = false;
                continue;
            }

            if (!inRequireBlock && RequireBlockStart().IsMatch(line))
            {
                inRequireBlock = true;
                continue;
            }

            if ((inRequireBlock || line.StartsWith("require ", StringComparison.Ordinal)) && Requirement().Match(line) is { Success: true } requirement)
                found.Add((requirement.Groups["path"].Value, requirement.Groups["version"].Value));
        }

        return found;
    }

    private static string And(IReadOnlyList<string> items) =>
        items.Count == 1 ? items[0] : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";
}
