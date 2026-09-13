using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// A fix gcc or clang worked out itself and printed as <c>fix-it:"app.c":{4:20-4:27}:"average"</c>.
/// </summary>
/// <remarks>
/// gcc and clang already know a great many answers - the header that declares <c>bool</c>, the
/// member a misspelt one was nearest to, the function a misspelt call meant - and print them for a
/// person to read. FixFinder builds with <c>-fdiagnostics-parseable-fixits</c>, which prints the same
/// answers a second time in a form a program can apply exactly: a file, a range and the text to put
/// there. Nothing here is inferred. The edit is the compiler's, and it is still compiled before it
/// is offered.
/// <para>
/// <b>Columns in a fix-it are bytes, not characters</b>, so a line with anything outside ASCII in it
/// is converted rather than indexed directly - indexing a UTF-16 string by a UTF-8 byte count would
/// put the change in the wrong place on exactly the lines where nobody would spot it.
/// </para>
/// </remarks>
public sealed partial class CompilerFixIt : ILocalFixRule
{
    public string Id => "c-compiler-fix-it";

    [GeneratedRegex(@"^fix-it:""(?<file>(?:[^""\\]|\\.)*)"":\{(?<l1>\d+):(?<c1>\d+)-(?<l2>\d+):(?<c2>\d+)\}:""(?<text>(?:[^""\\]|\\.)*)""\s*$")]
    private static partial Regex FixItLine();

    /// <summary>The next error or warning, which is where this diagnostic's fix-its stop. Notes belong to it.</summary>
    [GeneratedRegex(@"^(?:[A-Za-z]:)?[^\s:][^:]*?:\d+:\d+:\s*(?:fatal error|error|warning):")]
    private static partial Regex NextDiagnostic();

    [GeneratedRegex(@"^undefined reference to '(?<symbol>[^']+)'$")]
    private static partial Regex UndefinedReference();

    private sealed record FixIt(string File, int StartLine, int StartColumn, int EndLine, int EndColumn, string Text);

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error.LanguageId != "gcc") return null;

        var output = context.Output;
        var at = IndexOf(output, error.FirstLineSequence);
        if (at < 0) return null;

        var fixIts = After(output, at);
        var explanation = $"The compiler worked this out itself and printed it as a fix-it: {error.Message}.";

        // A misspelt function is only a warning to the compiler. The error is the linker's, which
        // knows nothing about source, and the fix-it hangs off the warning.
        if (fixIts.Count == 0 && UndefinedReference().Match(error.Message ?? "") is { Success: true } undefined)
        {
            var symbol = undefined.Groups["symbol"].Value;
            var warning = -1;

            for (var i = 0; i < output.Count && warning < 0; i++)
                if (output[i].Text.Contains($"implicit declaration of function '{symbol}'", StringComparison.Ordinal)) warning = i;

            if (warning >= 0)
            {
                fixIts = After(output, warning);
                explanation =
                    $"`{symbol}` was never declared, so the compiler only warned - naming the function it was nearest to - " +
                    $"and then the linker could not find `{symbol}` anywhere.";
            }
        }

        fixIts = fixIts.Distinct().ToList();
        if (fixIts.Count == 0) return null;

        var files = fixIts.Select(f => context.Resolve(f.File)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files is not [{ } path] || context.Read(path) is not { } source) return null;

        var first = fixIts.Min(f => f.StartLine);
        var last = fixIts.Max(f => f.EndLine);
        if (first < 1 || last > source.Count) return null;

        var block = string.Join("\n", source.Lines.Skip(first - 1).Take(last - first + 1));
        var edits = new List<(int Start, int End, string Text)>();

        foreach (var fixIt in fixIts)
        {
            if (Offset(source, first, fixIt.StartLine, fixIt.StartColumn) is not { } start ||
                Offset(source, first, fixIt.EndLine, fixIt.EndColumn) is not { } end ||
                end < start)
                return null;

            edits.Add((start, end, fixIt.Text));
        }

        edits.Sort((a, b) => a.Start.CompareTo(b.Start));

        // Two fix-its over the same text disagree about it, and neither can be applied after the other.
        for (var i = 1; i < edits.Count; i++)
            if (edits[i].Start < edits[i - 1].End) return null;

        var edited = block;
        foreach (var edit in Enumerable.Reverse(edits)) edited = edited[..edit.Start] + edit.Text + edited[edit.End..];

        var replaced = edits.Select(e => block[e.Start..e.End]).ToList();

        var title = edits.Count == 1 && replaced[0].Length > 0 && !edits[0].Text.Contains('\n')
            ? $"Change {replaced[0]} to {edits[0].Text}"
            : edits.All(e => e.Start == e.End && e.Text.TrimStart().StartsWith("#include", StringComparison.Ordinal))
                ? $"Add {string.Join(", ", edits.Select(e => e.Text.Trim()).Distinct())}"
                : "Make the change the compiler suggested";

        return new LocalFix
        {
            RuleId = Id,
            Title = title,
            Explanation = explanation,
            File = source.Path,
            StartLine = first,
            RemoveCount = last - first + 1,
            NewLines = edited.Split('\n'),
            ResolvesWarning = error.ExceptionType == "compile warning" ? error.Message : null,
        };
    }

    private static int IndexOf(IReadOnlyList<CapturedLine> output, int sequence)
    {
        for (var i = 0; i < output.Count; i++)
            if (output[i].Sequence == sequence) return i;

        return -1;
    }

    private static List<FixIt> After(IReadOnlyList<CapturedLine> output, int index)
    {
        var found = new List<FixIt>();

        for (var i = index + 1; i < output.Count; i++)
        {
            var text = output[i].Text;

            if (FixItLine().Match(text) is { Success: true } m)
            {
                found.Add(new FixIt(
                    Unescape(m.Groups["file"].Value),
                    int.Parse(m.Groups["l1"].Value), int.Parse(m.Groups["c1"].Value),
                    int.Parse(m.Groups["l2"].Value), int.Parse(m.Groups["c2"].Value),
                    Unescape(m.Groups["text"].Value)));

                continue;
            }

            if (NextDiagnostic().IsMatch(text)) break;
        }

        return found;
    }

    /// <summary>Where a line and byte column land in the block of lines starting at <paramref name="first"/>.</summary>
    private static int? Offset(SourceFile source, int first, int line, int byteColumn)
    {
        if (source.Line(line) is not { } text || CharIndex(text, byteColumn) is not { } column) return null;

        var offset = 0;
        for (var k = first; k < line; k++) offset += source.Lines[k - 1].Length + 1;

        return offset + column;
    }

    private static int? CharIndex(string line, int byteColumn)
    {
        var target = byteColumn - 1;
        var bytes = 0;

        for (var i = 0; i < line.Length;)
        {
            if (bytes == target) return i;
            if (bytes > target) return null;

            var width = char.IsHighSurrogate(line[i]) && i + 1 < line.Length ? 2 : 1;
            bytes += Encoding.UTF8.GetByteCount(line.AsSpan(i, width));
            i += width;
        }

        return bytes == target ? line.Length : null;
    }

    /// <summary>The compiler's escaping undone: backslash sequences, and octal bytes for anything non-ASCII.</summary>
    private static string Unescape(string text)
    {
        var result = new StringBuilder();
        var pending = new List<byte>();

        void Flush()
        {
            if (pending.Count == 0) return;
            result.Append(Encoding.UTF8.GetString(pending.ToArray()));
            pending.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length)
            {
                Flush();
                result.Append(text[i]);
                continue;
            }

            var c = text[++i];

            if (c is >= '0' and <= '7')
            {
                var value = c - '0';

                for (var digits = 1; digits < 3 && i + 1 < text.Length && text[i + 1] is >= '0' and <= '7'; digits++)
                    value = value * 8 + (text[++i] - '0');

                pending.Add((byte)value);
                continue;
            }

            Flush();

            result.Append(c switch
            {
                'n' => '\n',
                't' => '\t',
                _ => c,
            });
        }

        Flush();
        return result.ToString();
    }
}
