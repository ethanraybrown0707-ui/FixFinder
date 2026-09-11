using System.Text.RegularExpressions;

namespace FixFinder.Core.Patching;

/// <summary>
/// Reads a unified diff into <see cref="FilePatch"/>es, or refuses it.
/// </summary>
/// <remarks>
/// A plain state machine, and deliberately an unforgiving one. Every input it sees came from a
/// stranger's issue comment on the public internet, and its output decides what gets written to
/// your source tree - so anything it cannot account for exactly is refused rather than guessed
/// at. There is no repair pass, no fuzzy matching and no partial acceptance: a patch either
/// reconciles line for line, or it does not apply.
/// <para>
/// The two details that break naive parsers, both learnt the hard way:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A context line is a space followed by text - but a context line that was <i>empty</i> is a
/// lone space, and every markdown renderer on the internet strips trailing whitespace. By the
/// time a diff has been pasted into an issue and rendered back out, those lines are empty
/// strings. Treating an empty line inside a hunk as the end of the hunk is the single most
/// common reason a perfectly good patch "fails to parse".
/// </description></item>
/// <item><description>
/// In <c>@@ -a,b +c,d @@</c> the counts are optional and absent means one, so <c>@@ -1 +1 @@</c>
/// is legal and means a single line on each side.
/// </description></item>
/// </list>
/// </remarks>
public static partial class UnifiedDiffParser
{
    /// <summary>Largest diff worth reading. Beyond this it is a release, not a fix.</summary>
    private const int MaximumLines = 50_000;

    [GeneratedRegex(@"^@@+ -(?<oldStart>\d+)(?:,(?<oldCount>\d+))? \+(?<newStart>\d+)(?:,(?<newCount>\d+))? @@+(?<heading>.*)$")]
    private static partial Regex HunkHeaderPattern();

    [GeneratedRegex(@"^diff --git (?<a>.+?) (?<b>.+)$")]
    private static partial Regex GitHeaderPattern();

    /// <summary>True when the text looks like it contains a unified diff at all.</summary>
    /// <remarks>
    /// Cheap and permissive on purpose: it decides only whether the full parse is worth running,
    /// and the parse itself is the thing that decides whether a diff is real.
    /// </remarks>
    public static bool LooksLikeDiff(string text) =>
        text.Contains("@@ -", StringComparison.Ordinal) ||
        text.Contains("diff --git ", StringComparison.Ordinal);

    public static ParsedPatch Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ParsedPatch.Refused("the text was empty");

        // Normalised at parse time so a diff pasted through a Windows editor and one straight
        // from git are the same thing here. The file's own line ending is decided at apply time
        // from the file itself, never from the patch.
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

            // A bare diff with no git header: "--- a/x" then "+++ b/y".
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
            // Refused outright rather than skipped. A patch that is partly binary cannot be
            // applied whole, and applying only the text half would leave the tree in a state
            // neither the author nor the user ever intended.
            return ParsedPatch.Refused("the patch contains binary content, which cannot be applied as text");
        }

        if (files.All(f => f.Hunks.Count == 0 && !f.IsDeletion && !f.IsRename))
            return ParsedPatch.Refused("the patch names files but changes nothing in them");

        return new ParsedPatch(files);
    }

    /// <summary>Convenience wrapper for callers that only care whether it worked.</summary>
    public static bool TryParse(string text, out ParsedPatch patch)
    {
        patch = Parse(text);
        return patch.Ok;
    }

    // ------------------------------------------------------------------ files

    private readonly record struct FileResult(FilePatch? File, string? Rejection);

    /// <summary>Reads one file section introduced by <c>diff --git</c>, extended headers and all.</summary>
    private static FileResult ParseGitFile(string[] lines, ref int index)
    {
        var header = GitHeaderPattern().Match(lines[index]);
        index++;

        string? oldPath = StripPrefix(header.Groups["a"].Value);
        string? newPath = StripPrefix(header.Groups["b"].Value);

        bool isNew = false, isDeleted = false, isRename = false, isBinary = false;

        // The extended headers sit between "diff --git" and the first "---" or "@@".
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

            // Anything else before the first hunk is prose from the commit message.
            index++;
        }

        var hunks = ParseHunks(lines, ref index, out var rejection);
        if (rejection is not null) return new FileResult(null, rejection);

        return new FileResult(
            Build(oldPath, newPath, hunks, isNew, isDeleted, isRename, isBinary), null);
    }

    /// <summary>Reads a file section that has no git header, just <c>---</c> and <c>+++</c>.</summary>
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

    // ------------------------------------------------------------------ hunks

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

            // Absent means one. "@@ -1 +1 @@" is a legal single-line hunk.
            var oldCount = match.Groups["oldCount"].Success ? int.Parse(match.Groups["oldCount"].Value) : 1;
            var newCount = match.Groups["newCount"].Success ? int.Parse(match.Groups["newCount"].Value) : 1;

            var heading = match.Groups["heading"].Value.Trim();
            index++;

            var body = ReadHunkBody(lines, ref index, oldCount, newCount);

            var seenOld = body.Count(l => l.Kind is DiffLineKind.Context or DiffLineKind.Removed);
            var seenNew = body.Count(l => l.Kind is DiffLineKind.Context or DiffLineKind.Added);

            // The reconciliation check, and the reason there is no repair path. A hunk whose
            // header disagrees with its body has been truncated, reflowed or hand-edited, and
            // any guess about which is wrong would be a guess about what to write to disk.
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

    /// <summary>
    /// Reads hunk lines until the header's counts are satisfied.
    /// </summary>
    /// <remarks>
    /// Driven by the counts rather than by looking for the end, which is what makes an empty
    /// context line survive. A hunk that has not yet met its promised line counts is still
    /// running, so an empty string inside it is an empty context line whose trailing space a
    /// markdown renderer removed - not a blank line after the hunk.
    /// </remarks>
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
                // "\ No newline at end of file" belongs to the line above it.
                if (body.Count > 0) body[^1] = body[^1] with { NoNewlineAtEnd = true };
                index++;
                continue;
            }

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
                    // Not a hunk line at all - the hunk ended early and the counts will not
                    // reconcile, which the caller turns into a refusal.
                    return body;
            }

            index++;
        }

        // A trailing no-newline marker sits after the last counted line.
        if (index < lines.Length && lines[index].StartsWith(@"\", StringComparison.Ordinal))
        {
            if (body.Count > 0) body[^1] = body[^1] with { NoNewlineAtEnd = true };
            index++;
        }

        return body;
    }

    // ------------------------------------------------------------------ paths

    /// <summary>Reads a path from a <c>---</c> or <c>+++</c> line, dropping any timestamp.</summary>
    private static string? ReadPath(string raw)
    {
        var text = raw.Trim();

        // Classic diff appends a tab and a timestamp; git does not.
        var tab = text.IndexOf('\t');
        if (tab >= 0) text = text[..tab];

        text = text.Trim();

        return text is "/dev/null" or "" ? null : StripPrefix(text);
    }

    /// <summary>
    /// Removes git's <c>a/</c> and <c>b/</c> prefixes.
    /// </summary>
    /// <remarks>
    /// Only ever a prefix strip. Resolving a path to somewhere on this disk is
    /// <see cref="SourcePathMapper"/>'s job, and keeping the two apart is deliberate: this class
    /// must not be in a position to produce an absolute path at all.
    /// </remarks>
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
