using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A line copied from the one above it, with its own name changed and the name it reads from left as it was:
/// <c>width = box.width</c> then <c>height = box.width</c>. The line above reads the part its name says - width from
/// box.width - and the copy's name says height while it still reads width. The change reads the copy's own part:
/// <c>box.height</c>. One check, read in each language's own way of giving a name a value, from the table below.
/// </summary>
/// <remarks>
/// A possible mistake, not a likely one: the two names may hold the same value on purpose. It is said only when the line
/// above reads the very word its own name differs by - width in box.width for width - and the copy reads it unchanged, so a
/// pair that was written to match, such as low = min(values) then high = max(values), is never taken for one.
/// </remarks>
public static partial class CopyPastePatterns
{
    /// <summary>How one language gives a name a value: what is before the name, the name, and what it is given.</summary>
    private sealed record Dialect(string Id, IReadOnlySet<string> Files, Syntax Syntax, Regex Assignment);

    /// <summary>
    /// Declarations and types before a name in the brace languages, and the name with any this. or fields. A line ending in a
    /// comma is left out: it is one of an object's members being set - CrashLine = crashLine, - where the same value for two
    /// of them is often meant, not a statement of its own.
    /// </summary>
    private const string BraceAssignment =
        @"^(?<lead>\s*(?:(?:final|const|let|var|val|auto|static|readonly|mut)\s+)*(?:[A-Za-z_][\w.<>\[\],?*&]*\s+(?=[A-Za-z_$][\w.$]*\s*=))?)" +
        @"(?<target>[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*)\s*=(?![=>])\s*(?<right>[^;]*[^;\s])\s*(?:;\s*)?$(?<!,\s*)";

    private static readonly Dialect[] Dialects =
    [
        new("logic-python-copied-line", CodePattern.Files(".py", ".pyw"), Syntax.Python,
            new(@"^(?<lead>\s*)(?<target>[A-Za-z_]\w*(?:\.\w+)*)\s*=(?!=)\s*(?<right>.*\S)\s*$")),
        new("logic-java-copied-line", CodePattern.Files(".java"), Syntax.CLike, new(BraceAssignment)),
        new("logic-copied-line", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++", ".cs", ".js", ".mjs", ".cjs"), Syntax.CLike, new(BraceAssignment)),
        new("logic-go-copied-line", CodePattern.Files(".go"), Syntax.CLike,
            new(@"^(?<lead>\s*(?:var\s+)?)(?<target>[A-Za-z_]\w*(?:\.\w+)*)\s*(?<op>:=|=)(?!=)\s*(?<right>.*\S)\s*$")),
        new("logic-rust-copied-line", CodePattern.Files(".rs"), Syntax.CLike,
            new(@"^(?<lead>\s*(?:let\s+(?:mut\s+)?)?)(?<target>[A-Za-z_]\w*(?:\.\w+)*)\s*(?::\s*[^=]+)?=(?![=>])\s*(?<right>[^;]*[^;\s])\s*;\s*$")),
        new("logic-ruby-copied-line", CodePattern.Files(".rb"), Syntax.Python,
            new(@"^(?<lead>\s*)(?<target>@{0,2}[A-Za-z_]\w*(?:\.\w+)*)\s*=(?![=~>])\s*(?<right>.*\S)\s*$")),
        new("logic-php-copied-line", CodePattern.Files(".php"), Syntax.CLike,
            new(@"^(?<lead>\s*)(?<target>\$[A-Za-z_]\w*(?:->\w+)*)\s*=(?![=>])\s*(?<right>[^;]*[^;\s])\s*;\s*$")),
        new("logic-lua-copied-line", CodePattern.Files(".lua"), Syntax.Lua,
            new(@"^(?<lead>\s*(?:local\s+)?)(?<target>[A-Za-z_]\w*(?:\.\w+)*)\s*=(?!=)\s*(?<right>.*\S)\s*$")),
        new("logic-perl-copied-line", CodePattern.Files(".pl", ".pm"), Syntax.Perl,
            new(@"^(?<lead>\s*(?:my\s+)?)(?<target>\$[A-Za-z_]\w*)\s*=(?![=~>])\s*(?<right>[^;]*[^;\s])\s*;\s*$")),
        new("logic-dart-copied-line", CodePattern.Files(".dart"), Syntax.CLike, new(BraceAssignment)),
        new("logic-fortran-copied-line", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran,
            new(@"^(?<lead>\s*)(?<target>[A-Za-z_]\w*(?:%\w+)*)\s*=(?![=>])\s*(?<right>.*\S)\s*$")),
        new("logic-powershell-copied-line", CodePattern.Files(".ps1"), Syntax.Python,
            new(@"^(?<lead>\s*)(?<target>\$[A-Za-z_]\w*(?:\.\w+)*)\s*=(?!=)\s*(?<right>.*\S)\s*$")),
        new("logic-fsharp-copied-line", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp,
            new(@"^(?<lead>\s*let\s+(?:mutable\s+)?)(?<target>[A-Za-z_]\w*)\s*=(?!=)\s*(?<right>.*\S)\s*$")),
        new("logic-scala-copied-line", CodePattern.Files(".scala", ".sc"), Syntax.Scala, new(BraceAssignment)),
        new("logic-elixir-copied-line", CodePattern.Files(".ex", ".exs"), Syntax.Python,
            new(@"^(?<lead>\s*)(?<target>[a-z_]\w*)\s*=(?![=~>])\s*(?<right>.*\S)\s*$")),
        new("logic-erlang-copied-line", CodePattern.Files(".erl"), Syntax.Erlang,
            new(@"^(?<lead>\s*)(?<target>[A-Z]\w*)\s*=(?![=:<])\s*(?<right>.*[^,\s])\s*(?<end>,)\s*$")),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
        [.. Dialects.Select(dialect => new CodePattern(dialect.Id, dialect.Files, dialect.Syntax,
            (id, source, masked) => CopiedLines(id, source, masked, dialect.Assignment), Severity.Warning, Confidence.Possible))];

    /// <summary>A name, a field or a function in the code: what is looked through for the word the two names differ by.</summary>
    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Word();

    /// <summary>Where a name's parts meet: an underscore, a capital after a small letter, a letter and a figure.</summary>
    [GeneratedRegex(@"_+|(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Za-z])(?=[0-9])|(?<=[0-9])(?=[A-Za-z])")]
    private static partial Regex PartBreak();

    private static IEnumerable<LogicFinding> CopiedLines(string id, SourceFile source, IReadOnlyList<string> masked, Regex assignment)
    {
        for (var line = 0; line + 1 < masked.Count; line++)
        {
            if (assignment.Match(masked[line]) is not { Success: true } above) continue;

            // The line just below, with nothing but blank lines between.
            var next = line + 1;
            while (next < masked.Count && masked[next].Trim().Length == 0) next++;
            if (next >= masked.Count || assignment.Match(masked[next]) is not { Success: true } copied) continue;

            if (above.Groups["lead"].Value != copied.Groups["lead"].Value || above.Groups["op"].Value != copied.Groups["op"].Value) continue;
            if (Differ(above.Groups["target"].Value, copied.Groups["target"].Value) is not var (original, renamed)) continue;

            // Read from the original's own lines, so what is said and changed is the code as written, not as masked.
            var right = source.Lines[line].Substring(above.Groups["right"].Index, above.Groups["right"].Length);
            var copiedRight = source.Lines[next].Substring(copied.Groups["right"].Index, copied.Groups["right"].Length);
            if (right != copiedRight) continue;

            // The line above reads the word its name is told apart by - and only that one, not the copy's word too. Read as
            // written, so a key in quotes counts: box["width"] reads width as much as box.width does.
            if (!Uses(right, original) || Uses(right, renamed)) continue;

            var corrected = Replaced(right, original, renamed);
            if (corrected == right) continue;

            var target = source.Lines[next].Substring(copied.Groups["target"].Index, copied.Groups["target"].Length);
            var aboveTarget = source.Lines[line].Substring(above.Groups["target"].Index, above.Groups["target"].Length);
            var start = copied.Groups["right"].Index;
            var fixedLine = source.Lines[next][..start] + corrected + source.Lines[next][(start + copied.Groups["right"].Length)..];

            yield return new LogicFinding(id, next + 1,
                $"Line {next + 1} gives `{target}` the same `{right}` that line {line + 1} gives `{aboveTarget}` - it reads {original}, " +
                $"as the line it looks copied from does, though its own name says {renamed}. If it should read {renamed}, it is `{corrected}`.",
                LocalFix.ReplaceLine(id, $"Read {renamed}, as {target}'s name says: {corrected}",
                    $"Line {line + 1} reads {original} for {aboveTarget}; line {next + 1}, the copy of it for {target}, still reads {original}. " +
                    $"With {renamed} in its place, each name gets its own value. If the two are meant to be the same, leave it as it is.",
                    source.Path, next + 1, fixedLine))
            {
                Severity = Severity.Warning,
                Confidence = Confidence.Possible,
            };
        }
    }

    /// <summary>
    /// The one word two names are told apart by - width and height in box_width and box_height, x and y in dx and dy - or null
    /// when they differ by more than one, or not at all.
    /// </summary>
    private static (string Original, string Renamed)? Differ(string above, string copied)
    {
        var aboveParts = Parts(above);
        var copiedParts = Parts(copied);
        if (aboveParts.Count != copiedParts.Count) return null;

        var differing = Enumerable.Range(0, aboveParts.Count).Where(index => !string.Equals(aboveParts[index], copiedParts[index], StringComparison.OrdinalIgnoreCase)).ToList();
        if (differing is not [var at]) return null;

        var (original, renamed) = (aboveParts[at].ToLowerInvariant(), copiedParts[at].ToLowerInvariant());

        // dx and dy, x1 and y1: names one letter apart at their end are told apart by that letter.
        if (original.Length == renamed.Length && original.Length > 1 && original[..^1] == renamed[..^1] && char.IsLetter(original[^1]) && char.IsLetter(renamed[^1]))
            return (original[^1..], renamed[^1..]);

        return original.Any(char.IsLetter) && renamed.Any(char.IsLetter) ? (original, renamed) : null;
    }

    private static List<string> Parts(string name) =>
        [.. Regex.Split(name.TrimStart('$', '@'), @"\.|->|%")
            .SelectMany(piece => PartBreak().Split(piece))
            .Where(part => part.Length > 0)];

    /// <summary>Whether the code uses the word as a part of one of its names - or as the whole of one, with an s for more than one.</summary>
    private static bool Uses(string code, string word) =>
        Word().Matches(code).Any(name => Parts(name.Value).Any(part => IsTheWord(part, word)));

    private static bool IsTheWord(string part, string word) =>
        string.Equals(part, word, StringComparison.OrdinalIgnoreCase) || string.Equals(part, word + "s", StringComparison.OrdinalIgnoreCase);

    /// <summary>The code with each part that is the word given the other word instead, written in the same case: Width, WIDTH, widths.</summary>
    private static string Replaced(string code, string original, string renamed)
    {
        var changes = new List<(int Start, int Length, string With)>();

        foreach (Match name in Word().Matches(code))
        {
            var offset = 0;
            foreach (var part in PartBreak().Split(name.Value))
            {
                var at = name.Value.IndexOf(part, offset, StringComparison.Ordinal);
                if (part.Length > 0 && IsTheWord(part, original))
                {
                    var plural = part.Length == original.Length + 1 ? part[^1..] : "";
                    changes.Add((name.Index + at, part.Length, InCaseOf(part, renamed) + plural));
                }

                offset = at + part.Length;
            }
        }

        var result = code;
        foreach (var (start, length, with) in changes.OrderByDescending(change => change.Start))
            result = result[..start] + with + result[(start + length)..];
        return result;
    }

    private static string InCaseOf(string part, string word) =>
        part.All(c => !char.IsLetter(c) || char.IsUpper(c)) && part.Length > 1 ? word.ToUpperInvariant()
        : char.IsUpper(part[0]) ? char.ToUpperInvariant(word[0]) + word[1..]
        : word;
}
