namespace FixFinder.Core.LocalFixes;

/// <summary>One change to one file, worked out from an error and the code the error names.</summary>
public sealed record LocalFix
{
    public required string RuleId { get; init; }

    public required string Title { get; init; }

    public required string Explanation { get; init; }

    public required string File { get; init; }

    public required int StartLine { get; init; }

    public required int RemoveCount { get; init; }

    public required IReadOnlyList<string> NewLines { get; init; }

    public string? ResolvesWarning { get; init; }

    public static LocalFix ReplaceLine(
        string ruleId, string title, string explanation, string file, int line, string newText) => new()
    {
        RuleId = ruleId, Title = title, Explanation = explanation, File = file,
        StartLine = line, RemoveCount = 1, NewLines = [newText],
    };

    public static LocalFix Insert(
        string ruleId, string title, string explanation, string file, int beforeLine,
        IReadOnlyList<string> lines) => new()
    {
        RuleId = ruleId, Title = title, Explanation = explanation, File = file,
        StartLine = beforeLine, RemoveCount = 0, NewLines = lines,
    };

    public LocalFix Unambiguous(SourceFile source)
    {
        var fix = this;
        var lines = source.Lines;

        for (var step = 0; step < 12 && Occurrences(fix, lines) > 1; step++)
        {
            var canGrowUp = fix.StartLine > 2;
            var canGrowDown = fix.StartLine - 1 + fix.RemoveCount + 1 < lines.Count;

            if (step % 2 == 0 && canGrowUp || !canGrowDown && canGrowUp)
            {
                fix = fix with
                {
                    StartLine = fix.StartLine - 1, RemoveCount = fix.RemoveCount + 1,
                    NewLines = [lines[fix.StartLine - 2], .. fix.NewLines],
                };
            }
            else if (canGrowDown)
            {
                fix = fix with
                {
                    RemoveCount = fix.RemoveCount + 1,
                    NewLines = [.. fix.NewLines, lines[fix.StartLine - 1 + fix.RemoveCount]],
                };
            }
            else
            {
                break;
            }
        }

        return fix;
    }

    private static int Occurrences(LocalFix fix, IReadOnlyList<string> lines)
    {
        var from = Math.Max(0, fix.StartLine - 2);
        var to = Math.Min(lines.Count, fix.StartLine - 1 + fix.RemoveCount + 1);
        var length = to - from;
        if (length <= 0) return 0;

        var count = 0;

        for (var at = 0; at + length <= lines.Count; at++)
        {
            var same = true;
            for (var k = 0; k < length && same; k++) same = lines[at + k] == lines[from + k];
            if (same) count++;
        }

        return count;
    }

    public IReadOnlyList<string>? ApplyTo(SourceFile source)
    {
        if (StartLine < 1 || RemoveCount < 0 || StartLine - 1 + RemoveCount > source.Count) return null;

        return
        [
            .. source.Lines.Take(StartLine - 1),
            .. NewLines,
            .. source.Lines.Skip(StartLine - 1 + RemoveCount),
        ];
    }
}
