using FixFinder.Core.Patching;

namespace FixFinder.Gui;

/// <summary>What a rendered diff line is, for colouring.</summary>
public enum DiffRowKind
{
    FileHeader,
    HunkHeader,
    Context,
    Added,
    Removed,
    Note,
}

/// <summary>
/// One line of a rendered diff, shaped for the preview window's list.
/// </summary>
/// <remarks>
/// A view type, so the colouring rules live in XAML and <c>DiffLine</c> stays a description of
/// a patch rather than of a display.
/// </remarks>
public sealed class DiffRow
{
    public required string Text { get; init; }
    public required DiffRowKind Kind { get; init; }

    /// <summary>
    /// The line itself, so a blank row reads as blank rather than as a type name.
    /// </summary>
    /// <remarks>
    /// WPF falls back to <c>ToString()</c> when a bound automation name is empty, which turned
    /// the blank separator rows into "FixFinder.Gui.DiffRow" for anything reading the window
    /// through UI Automation - a screen reader, or the tooling used to drive it.
    /// </remarks>
    public override string ToString() => Text;

    public bool IsAdded => Kind == DiffRowKind.Added;
    public bool IsRemoved => Kind == DiffRowKind.Removed;
    public bool IsHeader => Kind is DiffRowKind.FileHeader or DiffRowKind.HunkHeader;
    public bool IsNote => Kind == DiffRowKind.Note;

    /// <summary>Renders a plan as the diff it would carry out, file by file.</summary>
    public static List<DiffRow> Render(ApplyPlan plan)
    {
        var rows = new List<DiffRow>();

        foreach (var file in plan.Files)
        {
            rows.Add(new DiffRow { Kind = DiffRowKind.FileHeader, Text = file.Path.PatchPath });

            rows.Add(new DiffRow
            {
                Kind = DiffRowKind.Note,
                Text = file.Path.FullPath is null
                    ? $"    {file.Explanation}"
                    : $"    → {file.Path.FullPath}  ({file.Path.Explanation})",
            });

            if (!file.Ok)
            {
                rows.Add(new DiffRow { Kind = DiffRowKind.Removed, Text = $"    REFUSED: {file.Explanation}" });
                continue;
            }

            foreach (var hunk in file.Hunks)
            {
                rows.Add(new DiffRow { Kind = DiffRowKind.HunkHeader, Text = hunk.Hunk.Header });
                rows.Add(new DiffRow { Kind = DiffRowKind.Note, Text = $"    {hunk.Explanation}" });

                foreach (var line in hunk.Hunk.Lines)
                {
                    rows.Add(new DiffRow
                    {
                        Kind = line.Kind switch
                        {
                            DiffLineKind.Added => DiffRowKind.Added,
                            DiffLineKind.Removed => DiffRowKind.Removed,
                            _ => DiffRowKind.Context,
                        },
                        Text = line.Kind switch
                        {
                            DiffLineKind.Added => "+" + line.Text,
                            DiffLineKind.Removed => "-" + line.Text,
                            _ => " " + line.Text,
                        },
                    });
                }
            }

            rows.Add(new DiffRow { Kind = DiffRowKind.Context, Text = "" });
        }

        return rows;
    }

    /// <summary>Renders a code block that is not a patch, as something to read and copy.</summary>
    public static List<DiffRow> Render(IReadOnlyList<CodeBlock> snippets)
    {
        var rows = new List<DiffRow>();

        foreach (var snippet in snippets)
        {
            rows.Add(new DiffRow
            {
                Kind = DiffRowKind.FileHeader,
                Text = snippet.Language is { Length: > 0 } language ? $"[{language}]" : "[code]",
            });

            if (snippet.IsFailedPatch)
            {
                rows.Add(new DiffRow
                {
                    Kind = DiffRowKind.Note,
                    Text = $"    This looked like a diff but could not be used: {snippet.Rejection}",
                });
            }

            foreach (var line in snippet.Code.ReplaceLineEndings("\n").Split('\n'))
                rows.Add(new DiffRow { Kind = DiffRowKind.Context, Text = "  " + line });

            rows.Add(new DiffRow { Kind = DiffRowKind.Context, Text = "" });
        }

        return rows;
    }
}
