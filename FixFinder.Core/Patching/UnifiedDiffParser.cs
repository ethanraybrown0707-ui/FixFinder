using System.Text.RegularExpressions;

namespace FixFinder.Core.Patching;

/// <summary>Reads a unified diff into <c>FilePatch</c>es, or refuses it.</summary>
public static partial class UnifiedDiffParser
{
    private const int MaximumLines = 50_000;

    [GeneratedRegex(@"^@@+ -(?<oldStart>\d+)(?:,(?<oldCount>\d+))? \+(?<newStart>\d+)(?:,(?<newCount>\d+))? @@+(?<heading>.*)$")]
    private static partial Regex HunkHeaderPattern();

    [GeneratedRegex(@"^diff --git (?<a>.+?) (?<b>.+)$")]
    private static partial Regex GitHeaderPattern();

    public static bool LooksLikeDiff(string text) =>
        text.Contains("@@ -", StringComparison.Ordinal) ||
        text.Contains("diff --git ", StringComparison.Ordinal);

    public static ParsedPatch Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ParsedPatch.Refused("the text was empty");

        var lines = text.ReplaceLineEndings("\n").Split('\n');

        if (lines.Length > MaximumLines)
            return ParsedPatch.Refused($"the diff is {lines.Length} lines, which is far past anything that could be a fix");

        var files = new List<FilePatch>();
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index];

            if (GitHeaderPattern().IsMatch(line))
            {
                var parsed = ParseGitFile(lines, ref index);
                if (parsed.Rejection is not null) return ParsedPatch.Refused(parsed.Rejection);
                if (parsed.File is not null) files.Add(parsed.File);
                continue;
            }

            if (line.StartsWith("--- ", StringComparison.Ordinal) &&
                index + 1 < lines.Length &&
                lines[index + 1].StartsWith("+++ ", StringComparison.Ordinal))
            {
                var parsed = ParseBareFile(lines, ref index);
                if (parsed.Rejection is not null) return ParsedPatch.Refused(parsed.Rejection);
                if (parsed.File is not null) files.Add(parsed.File);
                continue;
            }

            index++;
        }

        if (files.Count == 0) return ParsedPatch.Refused("no file headers or hunks were found");

        if (files.Any(f => f.IsBinary))
        {
            return ParsedPatch.Refused("the patch contains binary content, which cannot be applied as text");
        }

        if (files.All(f => f.Hunks.Count == 0 && !f.IsDeletion && !f.IsRename))
            return ParsedPatch.Refused("the patch names files but changes nothing in them");

        return new ParsedPatch(files);
    }

    public static bool TryParse(string text, out ParsedPatch patch)
    {
        patch = Parse(text);
        return patch.Ok;
    }

    private readonly record struct FileResult(FilePatch? File, string? Rejection);

    private static FileResult ParseGitFile(string[] lines, ref int index)
    {
        var header = GitHeaderPattern().Match(lines[index]);
        index++;

        string? oldPath = StripPrefix(header.Groups["a"].Value);
        string? newPath = StripPrefix(header.Groups["b"].Value);

        bool isNew = false, isDeleted = false, isRename = false, isBinary = false;

        while (index < lines.Length)
        {
            var line = lines[index];

            if (line.StartsWith("@@", StringComparison.Ordinal)) break;
            if (line.StartsWith("diff --git ", StringComparison.Ordinal)) break;

            if (line.StartsWith("new file mode", StringComparison.Ordinal)) { isNew = true; index++; continue; }
            if (line.StartsWith("deleted file mode", StringComparison.Ordinal)) { isDeleted = true; index++; continue; }
            if (line.StartsWith("rename from ", StringComparison.Ordinal)) { isRename = true; oldPath = line[12..].Trim(); index++; continue; }
            if (line.StartsWith("rename to ", StringComparison.Ordinal)) { isRename = true; newPath = line[10..].Trim(); index++; continue; }
            if (line.StartsWith("copy from ", StringComparison.Ordinal) ||
                line.StartsWith("copy to ", StringComparison.Ordinal) ||
                line.StartsWith("old mode", StringComparison.Ordinal) ||
                line.StartsWith("new mode", StringComparison.Ordinal) ||
                line.StartsWith("similarity index", StringComparison.Ordinal) ||
                line.StartsWith("dissimilarity index", StringComparison.Ordinal) ||
                line.StartsWith("index ", StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            if (line.StartsWith("GIT binary patch", StringComparison.Ordinal) ||
                line.StartsWith("Binary files ", StringComparison.Ordinal))
            {
                isBinary = true;
                index++;
                continue;
            }

            if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                oldPath = ReadPath(line[4..]);
                index++;

                if (index < lines.Length && lines[index].StartsWith("+++ ", StringComparison.Ordinal))
                {
                    newPath = ReadPath(lines[index][4..]);
                    index++;
                }

                continue;
            }

            index++;
        }

        var hunks = ParseHunks(lines, ref index, out var rejection);
        if (rejection is not null) return new FileResult(null, rejection);

        return new FileResult(
            Build(oldPath, newPath, hunks, isNew, isDeleted, isRename, isBinary), null);
    }

    private static FileResult ParseBareFile(string[] lines, ref int index)
    {
        var oldPath = ReadPath(lines[index][4..]);
        var newPath = ReadPath(lines[index + 1][4..]);
        index += 2;

        var hunks = ParseHunks(lines, ref index, out var rejection);
        if (rejection is not null) return new FileResult(null, rejection);

        return new FileResult(
            Build(oldPath, newPath, hunks, isNew: oldPath is null, isDeleted: newPath is null,
                  isRename: false, isBinary: false),
            null);
    }

    private static FilePatch Build(
        string? oldPath, string? newPath, IReadOnlyList<DiffHunk> hunks,
        bool isNew, bool isDeleted, bool isRename, bool isBinary) =>
        new(oldPath, newPath, hunks)
        {
            IsNewFile = isNew || oldPath is null,
            IsDeletion = isDeleted || newPath is null,
            IsRename = isRename || (oldPath is not null && newPath is not null &&
                                    !string.Equals(oldPath, newPath, StringComparison.Ordinal)),
            IsBinary = isBinary,
        };

    private static List<DiffHunk> ParseHunks(string[] lines, ref int index, out string? rejection)
    {
        rejection = null;
        var hunks = new List<DiffHunk>();

        while (index < lines.Length)
        {
            var match = HunkHeaderPattern().Match(lines[index]);
            if (!match.Success) break;

            var oldStart = int.Parse(match.Groups["oldStart"].Value);
            var newStart = int.Parse(match.Groups["newStart"].Value);

            var oldCount = match.Groups["oldCount"].Success ? int.Parse(match.Groups["oldCount"].Value) : 1;
            var newCount = match.Groups["newCount"].Success ? int.Parse(match.Groups["newCount"].Value) : 1;

            var heading = match.Groups["heading"].Value.Trim();
            index++;

            var body = ReadHunkBody(lines, ref index, oldCount, newCount);

            var seenOld = body.Count(l => l.Kind is DiffLineKind.Context or DiffLineKind.Removed);
            var seenNew = body.Count(l => l.Kind is DiffLineKind.Context or DiffLineKind.Added);

            if (seenOld != oldCount || seenNew != newCount)
            {
                rejection =
                    $"hunk at line {oldStart} does not add up: the header promises {oldCount} original and " +
                    $"{newCount} resulting lines, but the body has {seenOld} and {seenNew}. " +
                    "The diff was probably truncated or reformatted in transit.";

                return hunks;
            }

            hunks.Add(new DiffHunk(oldStart, oldCount, newStart, newCount, body, heading));
        }

        return hunks;
    }

    private static List<DiffLine> ReadHunkBody(string[] lines, ref int index, int oldCount, int newCount)
    {
        var body = new List<DiffLine>();
        var seenOld = 0;
        var seenNew = 0;

        while (index < lines.Length && (seenOld < oldCount || seenNew < newCount))
        {
            var line = lines[index];

            if (line.StartsWith(@"\", StringComparison.Ordinal))
            {
                if (body.Count > 0) body[^1] = body[^1] with { NoNewlineAtEnd = true };
                index++;
                continue;
            }

            // An unchanged line is a lone space, which markdown strips, so an empty line is still context.
            if (line.Length == 0)
            {
                body.Add(new DiffLine(DiffLineKind.Context, ""));
                seenOld++;
                seenNew++;
                index++;
                continue;
            }

            switch (line[0])
            {
                case ' ':
                    body.Add(new DiffLine(DiffLineKind.Context, line[1..]));
                    seenOld++;
                    seenNew++;
                    break;

                case '+':
                    body.Add(new DiffLine(DiffLineKind.Added, line[1..]));
                    seenNew++;
                    break;

                case '-':
                    body.Add(new DiffLine(DiffLineKind.Removed, line[1..]));
                    seenOld++;
                    break;

                default:
                    return body;
            }

            index++;
        }

        if (index < lines.Length && lines[index].StartsWith(@"\", StringComparison.Ordinal))
        {
            if (body.Count > 0) body[^1] = body[^1] with { NoNewlineAtEnd = true };
            index++;
        }

        return body;
    }

    private static string? ReadPath(string raw)
    {
        var text = raw.Trim();

        var tab = text.IndexOf('\t');
        if (tab >= 0) text = text[..tab];

        text = text.Trim();

        return text is "/dev/null" or "" ? null : StripPrefix(text);
    }

    private static string? StripPrefix(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var text = path.Trim().Trim('"');

        if (text == "/dev/null") return null;

        if (text.StartsWith("a/", StringComparison.Ordinal) ||
            text.StartsWith("b/", StringComparison.Ordinal))
        {
            text = text[2..];
        }

        return text.Length == 0 ? null : text;
    }
}
