namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// Execution trace compression: the lines a run went through, written short enough to read. Each stretch that repeats
/// straight after itself becomes one group with a count - found at the shortest length first, so an inner loop folds
/// before the loop around it - and lines that follow one another become a range: <c>1-4, (5-7)×3, 5, 8</c>.
/// </summary>
public static class TraceCompression
{
    private abstract record Piece
    {
        public abstract string Key { get; }
    }

    private sealed record Line(int Number) : Piece
    {
        public override string Key => Number.ToString();
    }

    private sealed record Repeat(IReadOnlyList<Piece> Body, int Times) : Piece
    {
        public override string Key => $"({string.Join(" ", Body.Select(p => p.Key))})x{Times}";
    }

    public static string Compress(IReadOnlyList<int> lines, int longestLoop = 16, int longestText = 240)
    {
        var pieces = lines.Select(line => (Piece)new Line(line)).ToList();

        for (var changed = true; changed;)
        {
            changed = false;

            for (var period = 1; period <= longestLoop; period++)
            {
                for (var at = 0; at + 2 * period <= pieces.Count; at++)
                {
                    if (!Same(pieces, at, at + period, period)) continue;

                    var times = 2;
                    while (at + (times + 1) * period <= pieces.Count && Same(pieces, at, at + times * period, period)) times++;

                    var body = pieces.GetRange(at, period);
                    pieces.RemoveRange(at, times * period);
                    pieces.Insert(at, new Repeat(body, times));
                    changed = true;
                }
            }
        }

        var text = Render(pieces);
        return text.Length <= longestText ? text : text[..longestText].TrimEnd(',', ' ') + " …";
    }

    private static bool Same(List<Piece> pieces, int first, int second, int length)
    {
        for (var i = 0; i < length; i++)
            if (pieces[first + i].Key != pieces[second + i].Key) return false;
        return true;
    }

    private static string Render(IReadOnlyList<Piece> pieces)
    {
        var parts = new List<string>();

        for (var i = 0; i < pieces.Count; i++)
        {
            if (pieces[i] is Repeat repeat)
            {
                parts.Add($"({Render(repeat.Body)})×{repeat.Times}");
                continue;
            }

            var start = ((Line)pieces[i]).Number;
            var end = start;
            while (i + 1 < pieces.Count && pieces[i + 1] is Line next && next.Number == end + 1)
            {
                end = next.Number;
                i++;
            }

            parts.Add(end > start ? $"{start}-{end}" : $"{start}");
        }

        return string.Join(", ", parts);
    }
}
