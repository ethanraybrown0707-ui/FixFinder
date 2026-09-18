using System.Text;

namespace FixFinder.Core.LocalFixes;

/// <summary>A source file read once and split into lines, keeping what is needed to write it back exactly.</summary>
public sealed class SourceFile
{
    private const long MaxBytes = 2 * 1024 * 1024;

    public required string Path { get; init; }

    public required IReadOnlyList<string> Lines { get; init; }

    public string LineEnding { get; init; } = "\n";

    public bool EndsWithNewline { get; init; } = true;

    public bool HasByteOrderMark { get; init; }

    public int Count => Lines.Count;

    public string? Line(int number) => number >= 1 && number <= Lines.Count ? Lines[number - 1] : null;

    public static SourceFile? Read(string? path)
    {
        if (path is not { Length: > 0 }) return null;

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBytes) return null;

            var bytes = File.ReadAllBytes(path);
            if (bytes.AsSpan(0, Math.Min(bytes.Length, 8192)).IndexOf((byte)0) >= 0) return null;

            var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var text = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));

            var endsWithNewline = text.EndsWith('\n');
            var body = !endsWithNewline ? text
                : text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2]
                : text[..^1];

            return new SourceFile
            {
                Path = System.IO.Path.GetFullPath(path),
                Lines = body.ReplaceLineEndings("\n").Split('\n'),
                LineEnding = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n",
                EndsWithNewline = endsWithNewline,
                HasByteOrderMark = bom,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public byte[] Render(IReadOnlyList<string> lines)
    {
        var text = string.Join(LineEnding, lines) + (EndsWithNewline ? LineEnding : "");
        var encoding = new UTF8Encoding(HasByteOrderMark);

        return [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];
    }
}
