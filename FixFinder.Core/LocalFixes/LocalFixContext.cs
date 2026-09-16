using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes;

/// <summary>Everything a rule may look at: the error, what else was reported with it, and the code.</summary>
public sealed class LocalFixContext
{
    private readonly Dictionary<string, SourceFile?> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _resolved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Folders that hold build output or installed packages rather than the user's code.</summary>
    private static readonly HashSet<string> NotSource = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "out", "target", "build", "node_modules", ".git", "__pycache__", ".venv", "venv",
    };

    public required ParsedError Error { get; init; }

    /// <summary>The other errors reported in the same output, for rules that fix several at once.</summary>
    public IReadOnlyList<ParsedError> Others { get; init; } = [];

    /// <summary>Everything the failing step printed, including what the parsers did not keep.</summary>
    public IReadOnlyList<CapturedLine> Output { get; init; } = [];

    public string? SourceRoot { get; init; }

    /// <summary>True when the error came from a build rather than from running the program.</summary>
    public bool FromBuild { get; init; }

    /// <summary>The interpreter that ran the program, so the check parses with the same Python.</summary>
    public string? PythonInterpreter { get; init; }

    /// <summary>The language the person said the program is in; only its rules propose anything.</summary>
    public CodeLanguage Language { get; init; } = CodeLanguage.Any;

    public IEnumerable<ParsedError> AllErrors => Others.Prepend(Error);

    /// <summary>The frame the error happened in - its own, never one from a cause it wraps.</summary>
    public ErrorFrame? Frame => OwnFrame(Error);

    /// <summary>The frame an error itself was raised in.</summary>
    /// <remarks>
    /// The culprit frame is chosen from the root cause, which is right for searching - the first
    /// exception in a chain is usually the real problem. It is wrong for correcting a line: a
    /// <c>NameError</c> raised by <c>except valueerror:</c> while a <c>ValueError</c> was being handled
    /// is about the except line, and the culprit frame points at the line that raised the ValueError,
    /// where the misspelt name does not appear.
    /// </remarks>
    public static ErrorFrame? OwnFrame(ParsedError error) =>
        error.CulpritFrame is { } culprit && error.Frames.Contains(culprit)
            ? culprit
            : error.Frames.FirstOrDefault(frame => frame.Origin == FrameOrigin.FirstParty) ?? error.Frames.FirstOrDefault();

    /// <summary>The file a frame names, as a full path that exists, or null.</summary>
    /// <remarks>
    /// Java prints a bare file name in its stack frames, so a runtime Java error names
    /// <c>App.java</c> and nothing else. That name is looked for under the source root and accepted
    /// only when exactly one file has it - the rule the patch planner applies, for the same reason.
    /// </remarks>
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

    /// <summary>A file named by the error, read, or null.</summary>
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
