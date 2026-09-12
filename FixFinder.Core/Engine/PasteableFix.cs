using System.Text;
using FixFinder.Core.Patching;
using FixFinder.Core.Sources;

namespace FixFinder.Core.Engine;

/// <summary>Something the user can put on the clipboard and paste straight in.</summary>
/// <param name="Text">Exactly what goes on the clipboard, with nothing decorative in it.</param>
/// <param name="Description">What it is, for the button's confirmation line.</param>
/// <param name="Where">The file and line it belongs at, when that is known.</param>
public sealed record PasteableText(string Text, string Description, string? Where = null);

/// <summary>
/// Turns a found fix into text worth pasting.
/// </summary>
/// <remarks>
/// <b>A unified diff is not pasteable.</b> That is the whole point of this file. Every diff in
/// this tool is written for a machine to apply - the markers, the hunk headers and the removed
/// lines are instructions, and pasting them into a source file produces something that does not
/// compile. What a person needs is the code as it should end up: the new side of the hunk,
/// context and additions in order, with the markers stripped and the removals gone.
/// <para>
/// That block replaces the old one exactly, because the context lines are carried with it. Copy
/// it, select the corresponding lines in the editor, paste.
/// </para>
/// <para>
/// A command is copied as a command, because it is pasted into a terminal rather than into code,
/// and a prose answer's code block is copied as it was written - there is nothing to reconstruct.
/// </para>
/// </remarks>
public static class PasteableFix
{
    /// <summary>What to copy for this result, or null when there is nothing worth copying.</summary>
    public static PasteableText? For(ExaminedCandidate examined)
    {
        // A missing package is fixed in a terminal, not in the file.
        if (examined.Candidate.Command is { Length: > 0 } command)
            return new PasteableText(command, examined.Candidate.CommandDescription);

        if (examined.Harvest is not { } harvest) return null;

        if (harvest.Patches is [{ Files: [{ } file, ..] }, ..])
        {
            if (Corrected(file) is { Length: > 0 } corrected)
            {
                var at = file.Hunks.Count == 1
                    ? $"{file.TargetPath}, from line {file.Hunks[0].NewStart}"
                    : file.TargetPath;

                return new PasteableText(corrected, "the corrected lines", at);
            }
        }

        // Prose. The code block is already what somebody wrote to be read and copied.
        if (harvest.Snippets is [{ Code: { Length: > 0 } code }, ..])
            return new PasteableText(code.TrimEnd(), "the code from the answer");

        return null;
    }

    /// <summary>
    /// The file as the patch would leave it, for the lines it touches.
    /// </summary>
    /// <remarks>
    /// Hunks are separated by a marker rather than run together, because they are not contiguous
    /// in the file - pasting them as one block would silently delete everything between them.
    /// A single hunk, which is the common case and always the case for a runtime correction,
    /// comes out clean with no marker at all.
    /// </remarks>
    private static string Corrected(FilePatch file)
    {
        var blocks = new List<string>();

        foreach (var hunk in file.Hunks)
        {
            var lines = hunk.NewSide.Select(l => l.Text).ToList();
            if (lines.Count > 0) blocks.Add(string.Join("\n", lines));
        }

        if (blocks.Count == 0) return "";
        if (blocks.Count == 1) return blocks[0];

        var joined = new StringBuilder();

        for (var i = 0; i < blocks.Count; i++)
        {
            if (i > 0)
            {
                joined.Append("\n\n");
                joined.Append($"# ... separate block, at line {file.Hunks[i].NewStart} ...\n\n");
            }

            joined.Append(blocks[i]);
        }

        return joined.ToString();
    }
}
