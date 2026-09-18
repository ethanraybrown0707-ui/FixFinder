using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes;

/// <summary>Everything a rule may look at: the error, what else was reported with it, and the code.</summary>
public sealed class LocalFixContext
{
    private readonly Dictionary<string, SourceFile?> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _resolved = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> NotSource = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "out", "target", "build", "node_modules", ".git", "__pycache__", ".venv", "venv",
    };

    public required ParsedError Error { get; init; }

    public IReadOnlyList<ParsedError> Others { get; init; } = [];

    public IReadOnlyList<CapturedLine> Output { get; init; } = [];

    public string? SourceRoot { get; init; }

    public bool FromBuild { get; init; }

    public string? PythonInterpreter { get; init; }

    public CodeLanguage Language { get; init; } = CodeLanguage.Any;

    public IEnumerable<ParsedError> AllErrors => Others.Prepend(Error);

    public ErrorFrame? Frame => OwnFrame(Error);

    public static ErrorFrame? OwnFrame(ParsedError error) =>
        error.CulpritFrame is { } culprit && error.Frames.Contains(culprit)
            ? culprit
            : error.Frames.FirstOrDefault(frame => frame.Origin == FrameOrigin.FirstParty) ?? error.Frames.FirstOrDefault();

    public string? Resolve(string? file)
    {
        if (file is not { Length: > 0 }) return null;
        if (_resolved.TryGetValue(file, out var known)) return known;

        string? found = null;

        try
        {
            if (Path.IsPathRooted(file))
            {
                found = File.Exists(file) ? Path.GetFullPath(file) : null;
            }
            else if (SourceRoot is { Length: > 0 } root && Directory.Exists(root))
            {
                var direct = Path.Combine(root, file);

                if (File.Exists(direct))
                {
                    found = Path.GetFullPath(direct);
                }
                else
                {
                    var options = new EnumerationOptions
                    {
                        RecurseSubdirectories = true, MaxRecursionDepth = 8, IgnoreInaccessible = true,
                    };

                    var matches = Directory.EnumerateFiles(root, Path.GetFileName(file), options)
                        .Where(path => !InsideNonSource(path, root))
                        .Take(2)
                        .ToList();

                    if (matches.Count == 1) found = Path.GetFullPath(matches[0]);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            found = null;
        }

        _resolved[file] = found;
        return found;
    }

    public SourceFile? Read(string? file)
    {
        if (Resolve(file) is not { } path) return null;

        if (!_files.TryGetValue(path, out var source)) _files[path] = source = SourceFile.Read(path);

        return source;
    }

    private static bool InsideNonSource(string path, string root) =>
        Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .SkipLast(1)
            .Any(NotSource.Contains);
}
