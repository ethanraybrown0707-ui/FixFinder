using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Go's own library used in a way that builds, passes go vet, and does something else - each run with go vet and go run:
/// time.Sleep(2) sleeps 2 nanoseconds, as a bare number is that many nanoseconds - it was over long before a second;
/// copy into a slice made with length 0 copies nothing, as copy fills only the length the slice has - [] 0; name[0] of a
/// string is a byte, a number - fmt.Println printed 104 for "hello"; and a goroutine main does not wait for never ran -
/// main ended, and the program with it, before its line was printed.
/// </summary>
public static partial class GoLibraryPatterns
{
    private static readonly IReadOnlySet<string> GoFiles = CodePattern.Files(".go");

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-go-sleep-nanoseconds", GoFiles, Syntax.CLike, SleepNanoseconds, Severity.Error),
        new CodePattern("logic-go-copy-into-empty", GoFiles, Syntax.CLike, CopyIntoEmpty, Severity.Error, Confidence.Certain),
        new CodePattern("logic-go-byte-printed", GoFiles, Syntax.CLike, BytePrinted, Severity.Error),
        new CodePattern("logic-go-goroutine-not-waited", GoFiles, Syntax.CLike, GoroutineNotWaited),
    ];

    [GeneratedRegex(@"\btime\s*\.\s*Sleep\s*\(\s*(?<count>\d+)\s*\)")]
    private static partial Regex SleepNumber();

    private static IEnumerable<LogicFinding> SleepNanoseconds(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match sleep in SleepNumber().Matches(masked[index]))
            {
                var count = sleep.Groups["count"].Value;
                if (count == "0") continue;

                // A small number reads as seconds, a large one as milliseconds.
                var unit = long.Parse(count) < 100 ? "Second" : "Millisecond";
                var line = source.Lines[index];
                var countGroup = sleep.Groups["count"];

                yield return new LogicFinding(id, index + 1,
                    $"time.Sleep takes a time.Duration, and a number on its own is that many nanoseconds - so time.Sleep({count}) waits " +
                    $"{count} billionths of a second, which is no wait at all",
                    LocalFix.ReplaceLine(id, $"Say the unit: time.Sleep({count} * time.{unit})",
                        $"time.{unit} is one {unit.ToLowerInvariant()} as a Duration, so {count} of them is the wait meant.",
                        source.Path, index + 1, line[..countGroup.Index] + $"{count} * time.{unit}" + line[(countGroup.Index + countGroup.Length)..]));
            }
        }
    }

    private static IEnumerable<LogicFinding> CopyIntoEmpty(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            var copied = Regex.Match(masked[index], @"\bcopy\s*\(\s*(?<into>[A-Za-z_]\w*)\s*,\s*(?<from>[A-Za-z_][\w.]*)\s*\)");
            if (!copied.Success) continue;

            var into = copied.Groups["into"].Value;
            var from = copied.Groups["from"].Value;

            // The slice copied into, made with length 0 - into := make([]int, 0), into = []int{}, or var into []int - and not
            // given anything since.
            var made = Enumerable.Range(0, index).Reverse().Select(line => (Line: line, Match: Regex.Match(masked[line],
                    $@"\b{Regex.Escape(into)}\s*:?=\s*(?<empty>make\s*\(\s*\[\](?<type>[\w.*]+)\s*,\s*0\s*(?:,[^)]*)?\)|\[\](?<type>[\w.*]+)\s*\{{\s*\}})|^\s*var\s+{Regex.Escape(into)}\s+(?<empty>\[\](?<type>[\w.*]+))\s*$")))
                .FirstOrDefault(each => each.Match.Success);
            if (made.Match is not { Success: true } emptySlice) continue;
            if (Enumerable.Range(made.Line + 1, index - made.Line - 1).Any(line => Regex.IsMatch(masked[line], $@"\b{Regex.Escape(into)}\s*=(?!=)|\b{Regex.Escape(into)}\s*=\s*append\b"))) continue;

            var line = source.Lines[made.Line];
            var type = emptySlice.Groups["type"].Value;
            var empty = emptySlice.Groups["empty"];
            var sized = $"make([]{type}, len({from}))";
            // var into []int becomes var into = make(...); the others keep their := or =, and only what is made changes.
            var changedLine = emptySlice.Value.TrimStart().StartsWith("var", StringComparison.Ordinal)
                ? line[..empty.Index].TrimEnd() + " = " + sized + line[(empty.Index + empty.Length)..]
                : line[..empty.Index] + sized + line[(empty.Index + empty.Length)..];

            yield return new LogicFinding(id, index + 1,
                $"copy copies only as many items as both slices have, and {into} was made on line {made.Line + 1} with none - length 0 - so " +
                $"nothing is copied, and {into} stays empty: [] 0",
                LocalFix.ReplaceLine(id, $"Make it as long as what is copied: {sized}",
                    $"make with len({from}) gives {into} room for every item of {from}, so copy fills it.",
                    source.Path, made.Line + 1, changedLine));
        }
    }

    [GeneratedRegex(@"\bfmt\s*\.\s*Print(?:ln)?\s*\(")]
    private static partial Regex PrintCall();

    private static IEnumerable<LogicFinding> BytePrinted(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        for (var index = 0; index < masked.Count; index++)
        {
            if (PrintCall().Match(masked[index]) is not { Success: true } print) continue;

            var close = Brackets.ClosingParenthesis(masked[index], print.Index + print.Length - 1);
            if (close is not { } end) continue;

            var arguments = masked[index][(print.Index + print.Length)..end];
            foreach (Match indexed in Regex.Matches(arguments, @"(?<![\w.])(?<!\bstring\s*\(\s*)(?<name>[A-Za-z_]\w*)\s*\[(?<position>[^\]:]+)\]"))
            {
                var name = indexed.Groups["name"].Value;
                // A string: given text in quotes, or declared string.
                if (!Regex.IsMatch(code, $@"\b{Regex.Escape(name)}\s*:?=\s*""|\bvar\s+{Regex.Escape(name)}\s+string\b|[(,]\s*{Regex.Escape(name)}\s+string\b")) continue;

                var line = source.Lines[index];
                var start = print.Index + print.Length + indexed.Index;
                var written = line.Substring(start, indexed.Length);

                yield return new LogicFinding(id, index + 1,
                    $"{written} is a byte - a number - not a piece of text, so fmt prints its number: \"hello\"[0] printed 104, not h",
                    LocalFix.ReplaceLine(id, $"Print it as text: string({written})",
                        $"string(...) turns the byte back into the character it stands for.",
                        source.Path, index + 1, line[..start] + $"string({written})" + line[(start + indexed.Length)..]));
            }
        }
    }

    private static IEnumerable<LogicFinding> GoroutineNotWaited(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var main = Enumerable.Range(0, masked.Count).FirstOrDefault(line => Regex.IsMatch(masked[line], @"^\s*func\s+main\s*\(\s*\)\s*\{"), -1);
        if (main < 0 || BraceBlocks.Body(masked, main) is not { } body) yield break;

        var bodyLines = Enumerable.Range(body.First, body.End - body.First).ToList();
        var started = bodyLines.FirstOrDefault(line => Regex.IsMatch(masked[line], @"^\s*go\s+"), -1);
        if (started < 0) yield break;

        // Anything after it that waits: a WaitGroup, a channel received from, a select, a sleep, reading input, or a server.
        var waits = new Regex(@"\.\s*Wait\s*\(|<-|\bselect\s*\{|\btime\s*\.\s*Sleep\b|\bfmt\s*\.\s*Scan|\bbufio\b|\.\s*ListenAndServe\b|\bfor\s*\{");
        if (bodyLines.Where(line => line > started).Any(line => waits.IsMatch(masked[line]))) yield break;

        yield return new LogicFinding(id, started + 1,
            "main does not wait for this goroutine, and the program ends when main does - so it may never run at all: its line was never printed",
            null);
    }
}
