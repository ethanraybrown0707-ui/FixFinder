using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A search taking its next item from a queue - or a stack - in a loop that never asks whether it is empty: while True, with
/// node = queue.popleft() each time round, left only by a return when what is looked for turns up. When it is not there, the
/// queue runs out and taking from it fails: Python's pop from an empty deque, Java's NoSuchElementException, Go's index out of
/// range, Ruby's and Lua's nil - or, where a missing item reads as nothing, the loop goes round for ever. QuixBugs'
/// breadth_first_search is this mistake, in Python and in Java. The change makes the loop ask: while queue:, while
/// (!queue.isEmpty()) - and in Java, where nothing could follow the loop that was never left, returns false after it.
/// </summary>
/// <remarks>
/// Said only for a loop written to go round for ever - while True, while (true), for (;;), Go's for {, Rust's loop, Ruby's
/// loop do, Fortran's do - that takes an item from a queue or stack each time round, that a return, break or exit inside
/// leaves, and in which no test - nor the loop's own - names the queue, or for a queue kept in an array, where its next item
/// is. A loop asking if not queue: break, or one waiting on a queue that blocks until an item comes, is left alone.
/// </remarks>
public static partial class EmptyQueuePatterns
{
    /// <summary>How one language is read, writes a loop that asks whether its queue has items, and gives false.</summary>
    /// <param name="Endless">A loop's first line written to go round for ever.</param>
    /// <param name="Takes">Ways of taking the next item from the queue named {Q} - or, for one kept in an array, {Q}[{H}++].</param>
    /// <param name="Asking">The loop's first line asking that the queue has items - {Q}, or {H} and {T} for one in an array; {indent} and {open} kept.</param>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, Regex Endless, string[] Takes, string Asking,
        string? AskingInArray = null, string? ReturnsFalse = null, bool IgnoreCase = false);

    private const string Name = @"[$@]?[A-Za-z_]\w*";

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, new(@"^(?<indent>\s*)while\s+(?:True|1)\s*:\s*$"),
            [@"(?<q>NAME)\s*\.\s*(?:popleft|pop)\s*\(\s*0?\s*\)", @"heapq\s*\.\s*heappop\s*\(\s*(?<q>NAME)\s*\)"], "{indent}while {Q}:"),
        new("java", CodePattern.Files(".java"), Syntax.CLike, new(@"^(?<indent>\s*)(?:while\s*\(\s*true\s*\)|for\s*\(\s*;\s*;\s*\))\s*(?<open>\{?)\s*$"),
            [@"(?<q>NAME)\s*\.\s*(?:removeFirst|removeLast|remove|poll|pollFirst|pollLast|pop)\s*\(\s*\)", @"(?<q>NAME)\s*\.\s*remove\s*\(\s*0\s*\)"],
            "{indent}while (!{Q}.isEmpty()) {open}", ReturnsFalse: "return false;"),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, new(@"^(?<indent>\s*)(?:while\s*\(\s*true\s*\)|for\s*\(\s*;\s*;\s*\))\s*(?<open>\{?)\s*$"),
            [@"(?<q>NAME)\s*\.\s*(?:Dequeue|Pop)\s*\(\s*\)"], "{indent}while ({Q}.Count > 0){open}"),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, new(@"^(?<indent>\s*)(?:while\s*\(\s*(?:true|1)\s*\)|for\s*\(\s*;\s*;\s*\))\s*(?<open>\{?)\s*$"),
            [@"(?<q>NAME)\s*\.\s*(?:shift|pop)\s*\(\s*\)"], "{indent}while ({Q}.length > 0) {open}"),
        new("c", CodePattern.Files(".c"), Syntax.CLike, new(@"^(?<indent>\s*)(?:while\s*\(\s*(?:1|true)\s*\)|for\s*\(\s*;\s*;\s*\))\s*(?<open>\{?)\s*$"),
            [@"(?<q>NAME)\s*\[\s*(?<h>NAME)\s*\+\+\s*\]"], "", AskingInArray: "{indent}while ({H} < {T}) {open}"),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, new(@"^(?<indent>\s*)(?:while\s*\(\s*(?:true|1)\s*\)|for\s*\(\s*;\s*;\s*\))\s*(?<open>\{?)\s*$"),
            [@"(?<q>NAME)\s*\.\s*(?:front|top)\s*\(\s*\)", @"(?<q>NAME)\s*\.\s*pop_front\s*\(\s*\)"], "{indent}while (!{Q}.empty()) {open}"),
        new("go", CodePattern.Files(".go"), Syntax.CLike, new(@"^(?<indent>\s*)for\s*(?<open>\{)\s*$"),
            [@"(?<q>NAME)\s*=\s*\k<q>\s*\[\s*1\s*:\s*\]", @"(?<q>NAME)\s*=\s*\k<q>\s*\[\s*:\s*len\s*\(\s*\k<q>\s*\)\s*-\s*1\s*\]"], "{indent}for len({Q}) > 0 {open}"),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, new(@"^(?<indent>\s*)loop\s*(?<open>\{)\s*$"),
            [@"(?<q>NAME)\s*\.\s*(?:pop_front|pop_back|pop)\s*\(\s*\)\s*\.\s*(?:unwrap|expect)\s*\("], "{indent}while !{Q}.is_empty() {open}"),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, new(@"^(?<indent>\s*)(?:loop\s+do|while\s+true)\s*$"),
            [@"(?<q>NAME)\s*\.\s*(?:shift|pop)\b"], "{indent}until {Q}.empty?"),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, new(@"^(?<indent>\s*)while\s+true\s+do\s*$"),
            [@"table\s*\.\s*remove\s*\(\s*(?<q>NAME)\s*(?:,\s*1\s*)?\)"], "{indent}while #{Q} > 0 do"),
        new("php", CodePattern.Files(".php"), Syntax.CLike, new(@"^(?<indent>\s*)(?:while\s*\(\s*(?:true|1)\s*\)|for\s*\(\s*;\s*;\s*\))\s*(?<open>\{?)\s*$", RegexOptions.IgnoreCase),
            [@"array_(?:shift|pop)\s*\(\s*(?<q>NAME)\s*\)", @"(?<q>NAME)\s*->\s*(?:dequeue|shift|pop)\s*\(\s*\)"], "{indent}while (count({Q}) > 0) {open}"),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, new(@"^(?<indent>\s*)while\s*\(\s*1\s*\)\s*(?<open>\{?)\s*$"),
            [@"\b(?:shift|pop)\s*\(?\s*(?<q>@[A-Za-z_]\w*)"], "{indent}while ({Q}) {open}"),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, new(@"^(?<indent>\s*)while\s*\(\s*\$true\s*\)\s*(?<open>\{?)\s*$", RegexOptions.IgnoreCase),
            [@"(?<q>NAME)\s*\.\s*(?:Dequeue|Pop)\s*\(\s*\)"], "{indent}while ({Q}.Count -gt 0) {open}", IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, new(@"^(?<indent>\s*)(?:while\s*\(\s*true\s*\)|for\s*\(\s*;\s*;\s*\))\s*(?<open>\{?)\s*$"),
            [@"(?<q>NAME)\s*\.\s*(?:removeFirst|removeLast)\s*\(\s*\)", @"(?<q>NAME)\s*\.\s*removeAt\s*\(\s*0\s*\)"], "{indent}while ({Q}.isNotEmpty) {open}"),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, new(@"^(?<indent>\s*)while\s*\(\s*true\s*\)\s*(?<open>\{?)\s*$"),
            [@"(?<q>NAME)\s*\.\s*(?:dequeue|pop)\s*\(\s*\)"], "{indent}while ({Q}.nonEmpty) {open}"),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, new(@"^(?<indent>\s*)do\s*(?:while\s*\(\s*\.true\.\s*\))?\s*$", RegexOptions.IgnoreCase),
            [@"(?<q>NAME)\s*\(\s*(?<h>NAME)\s*\)"], "", AskingInArray: "{indent}do while ({H} <= {T})", IgnoreCase: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-taken-from-empty", dialect.Files, dialect.Syntax,
            (id, source, masked) => TakenFromEmpty(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>A test's word: if, elif, else if, elsif, elseif, unless, while, until, when - or a ? choosing.</summary>
    [GeneratedRegex(@"(?<![\w$.@-])(?:else\s+if|elseif|elsif|elif|if|unless|while|until|when)\b|\?", RegexOptions.IgnoreCase)]
    private static partial Regex TestWord();

    /// <summary>A way out of the loop: return, break, exit, last, raise, throw.</summary>
    [GeneratedRegex(@"(?<![\w$.@-])(?:return|break|exit|last|raise|throw)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WayOut();

    private static IEnumerable<LogicFinding> TakenFromEmpty(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var takes = dialect.Takes.Select(take => new Regex(take.Replace("NAME", Name), options)).ToList();
        for (var header = 0; header < masked.Count; header++)
        {
            if (dialect.Endless.Match(masked[header]) is not { Success: true } endless) continue;
            var body = Body(masked, header, endless.Groups["indent"].Value.Length);
            if (body.Count == 0 || !body.Any(line => WayOut().IsMatch(masked[line]))) continue;

            // The next item taken from a queue, each time round. A queue kept in an array is one whose next item's place goes
            // on with it - queue[head++], head = head + 1 - and whose last went in at a place it is asked against: queue[tail++]
            // = item, queue(tail) = item; the first take that is one of these is the queue's.
            string? queue = null, next = null, last = null;
            var takenLine = -1;
            foreach (var line in body)
            {
                foreach (var taken in takes.Select(take => take.Match(masked[line])).Where(match => match.Success))
                {
                    var arrayNext = taken.Groups["h"].Success ? taken.Groups["h"].Value : null;
                    var arrayLast = arrayNext is null ? null : LastIn(masked, body, taken.Groups["q"].Value, arrayNext, options);
                    if (arrayNext is not null && arrayLast is null) continue;
                    (queue, next, last, takenLine) = (taken.Groups["q"].Value, arrayNext, arrayLast, line);
                    break;
                }

                if (queue is not null) break;
            }

            if (queue is null) continue;

            // No test - nor the loop's own - names the queue, or where its next item is.
            var named = new Regex($@"(?<![\w$@]){Regex.Escape(next ?? queue)}(?!\w)", options);
            if (body.Any(line => Asks(masked[line], named))) continue;

            // What a take gives on an empty queue - Java's poll, JavaScript's shift - asked about: if (item == null) break.
            if (Regex.Match(masked[takenLine], $@"(?<item>{Name})\s*(?::=|=(?!=))[^=]*$", options) is { Success: true } given)
            {
                var item = Regex.Escape(given.Groups["item"].Value);
                var nothing = new Regex($@"(?<![\w$@]){item}\s*(?:===?|!==?|\bis\b|-eq|-ne)\s*(?:null|None|nil|undefined|NULL|nullptr|\$null)\b|(?:null|None|undefined|nil)\s*===?\s*{item}(?!\w)|(?:!|\bnot\s+){item}(?!\w)|{item}\s*\.\s*nil\?", options);
                if (body.Any(line => TestWord().IsMatch(masked[line]) && nothing.IsMatch(masked[line]))) continue;

                // A queue that each item is put back on - taking turns round it - never runs out.
                var putBack = new Regex($@"(?<![\w$@]){Regex.Escape(queue)}\s*\.\s*(?:append|appendleft|push|push_back|add|addLast|addFirst|offer|Enqueue|enqueue|unshift)\s*\(\s*{item}\s*\)|(?:push|unshift)\s*\(?\s*{Regex.Escape(queue)}\s*,\s*{item}\s*\)?|{Regex.Escape(queue)}\s*<<\s*{item}(?!\w)|table\s*\.\s*insert\s*\(\s*{Regex.Escape(queue)}\s*,\s*{item}\s*\)", options);
                if (body.Any(line => putBack.IsMatch(masked[line]))) continue;
            }

            var told = $"Line {takenLine + 1} takes the next item from `{queue}` each time round the loop on line {header + 1}, which goes round " +
                       $"until something inside leaves it - but nothing asks whether `{queue}` is empty. When what is looked for is not there, " +
                       $"`{queue}` runs out, and taking from it fails{(dialect.Language is "perl" or "php" ? ", or the loop goes on with nothing" : "")}.";
            yield return new LogicFinding(id, takenLine + 1, told, AskFirst(id, source, masked, header, endless, body, queue, next, last, dialect))
            {
                Severity = Severity.Error,
                Confidence = Confidence.Likely,
            };
        }
    }

    /// <summary>The lines of the loop's body: those after its first line, indented further - a { on a line of its own aside.</summary>
    private static List<int> Body(IReadOnlyList<string> masked, int header, int indent)
    {
        var body = new List<int>();
        for (var below = header + 1; below < masked.Count; below++)
        {
            var text = masked[below];
            if (text.Trim().Length == 0) continue;
            var width = CodeText.Indentation(text).Length;
            if (width == indent && body.Count == 0 && text.Trim() == "{") continue;
            if (width <= indent) break;
            body.Add(below);
        }

        return body;
    }

    /// <summary>
    /// For a queue kept in an array, the name of where its last item went in: queue[tail++] = item, or queue(tail) = item after
    /// tail = tail + 1 - and the next item's place moving on: queue[head++], head = head + 1. Null when either is not seen.
    /// </summary>
    private static string? LastIn(IReadOnlyList<string> masked, List<int> body, string queue, string next, RegexOptions options)
    {
        var name = Regex.Escape(queue);
        var nextName = Regex.Escape(next);
        var movesOn = body.Any(line => Regex.IsMatch(masked[line], $@"\[\s*{nextName}\s*\+\+\s*\]|^\s*{nextName}\s*=\s*{nextName}\s*\+\s*1\s*;?\s*$", options));
        if (!movesOn) return null;
        for (var line = 0; line < masked.Count; line++)
        {
            if (Regex.Match(masked[line], $@"(?<![\w$@]){name}\s*[\[(]\s*(?<last>{Name})\s*(?:\+\+)?\s*[\])]\s*=(?!=)", options) is { Success: true } putIn &&
                !string.Equals(putIn.Groups["last"].Value, next, StringComparison.OrdinalIgnoreCase))
                return putIn.Groups["last"].Value;
        }

        return null;
    }

    /// <summary>Whether a line's test - after if, while, unless, until, when or before a ? - names the queue.</summary>
    private static bool Asks(string text, Regex named)
    {
        foreach (Match word in TestWord().Matches(text))
        {
            var asked = word.Value == "?" ? text[..word.Index] : text[(word.Index + word.Length)..];
            if (named.IsMatch(asked)) return true;
        }

        return false;
    }

    /// <summary>
    /// The change: the loop asks that the queue has items - while queue:, while (!queue.isEmpty()) {, for len(queue) &gt; 0 {,
    /// until queue.empty? - and in Java, where the loop was the function's last statement, false is returned after it.
    /// </summary>
    private static LocalFix? AskFirst(string id, SourceFile source, IReadOnlyList<string> masked, int header, Match endless, List<int> body,
        string queue, string? next, string? last, Dialect dialect)
    {
        var template = next is null ? dialect.Asking : dialect.AskingInArray;
        if (string.IsNullOrEmpty(template)) return null;
        var asking = template.Replace("{indent}", CodeText.Indentation(source.Lines[header])).Replace("{Q}", queue)
            .Replace("{H}", next ?? "").Replace("{T}", last ?? "").Replace("{open}", endless.Groups["open"].Value).TrimEnd();
        var title = $"Ask whether {queue} has items: {asking.Trim()}";
        var explanation = $"With `{queue}` empty there is nothing left to look at: the loop ends there, and what was looked for is not to be found.";

        // Java will not compile a line after a loop that is never left, so a function ending with one has nothing after it -
        // and once the loop can end, it needs its false.
        var closing = body[^1] + 1;
        while (closing < masked.Count && masked[closing].Trim().Length == 0) closing++;
        var afterLoop = closing + 1;
        while (afterLoop < masked.Count && masked[afterLoop].Trim().Length == 0) afterLoop++;
        var endsFunction = closing < masked.Count && masked[closing].Trim() == "}" && afterLoop < masked.Count && masked[afterLoop].Trim() == "}";
        if (dialect.ReturnsFalse is { } returnsFalse && endsFunction && body.Any(line => Regex.IsMatch(masked[line], @"\breturn\s+true\s*;")))
        {
            var lines = new List<string> { asking };
            for (var line = header + 1; line <= closing; line++) lines.Add(source.Lines[line]);
            lines.Add(CodeText.Indentation(source.Lines[closing]) + returnsFalse);
            return new LocalFix
            {
                RuleId = id,
                Title = title,
                Explanation = explanation + " A loop that can end needs what the function gives when it does: false.",
                File = source.Path,
                StartLine = header + 1,
                RemoveCount = closing - header + 1,
                NewLines = lines,
            };
        }

        return LocalFix.ReplaceLine(id, title, explanation, source.Path, header + 1, asking);
    }
}
