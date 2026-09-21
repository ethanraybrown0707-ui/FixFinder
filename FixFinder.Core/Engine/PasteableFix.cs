using System.Text;
using FixFinder.Core.Patching;

namespace FixFinder.Core.Engine;

/// <summary>Something the user can put on the clipboard and paste straight in.</summary>
public sealed record PasteableText(string Text, string Description, string? Where = null);

/// <summary>Turns a found fix into text worth pasting.</summary>
public static class PasteableFix
{
    public static PasteableText? For(ExaminedCandidate examined)
    {
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

        if (harvest.Snippets is [{ Code: { Length: > 0 } code }, ..])
            return new PasteableText(code.TrimEnd(), "the code from the answer");

        return null;
    }

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
