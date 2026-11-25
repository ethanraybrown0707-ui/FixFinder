using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A pointer moved two nodes on each time round - the hare of Floyd's cycle search, hare = hare.successor.successor - but asked
/// only whether the next node is there: if hare.successor is None. When the next node is there but the one after it is not,
/// the hare lands on nothing, and the next time round asking for its next node fails: Python's AttributeError, Java's
/// NullPointerException, Go's nil pointer dereference, Ruby's and Lua's nil - PHP warns of reading a property of null. QuixBugs'
/// detect_cycle is this mistake, in Python and in Java. The change asks about the hare itself first: hare is None or
/// hare.successor is None.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: inside a loop a name is moved two on along the same link - hare.next.next,
/// hare-&gt;next-&gt;next, hare.getSuccessor().getSuccessor(), next[next[hare]] - and a test in that loop reads its next one -
/// hare.next == null, hare.successor is not None, next[hare] == NONE - with no test of the name alone. Not said for Perl, where
/// reading $hare-&gt;{next} of an undefined $hare makes it an empty hash and the search gives the right answer; nor for
/// PowerShell unless the file sets strict mode, since a property of $null is otherwise $null; nor for Fortran, whose .or. may
/// read both sides, so the asking could not be written in one test.
/// </remarks>
public static partial class TwoStepPatterns
{
    /// <summary>How one language is read, and joins two tests: or, ||, -or - and and, &amp;&amp;, -and.</summary>
    /// <param name="Strict">Whether the language reads a missing node's link as nothing unless strict mode is set, as PowerShell does.</param>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, string Or, string And, bool ColonEnds = false,
        bool IgnoreCase = false, bool Strict = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, " or ", " and ", ColonEnds: true),
        new("java", CodePattern.Files(".java"), Syntax.CLike, " || ", " && "),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, " || ", " && "),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, " || ", " && "),
        new("c", CodePattern.Files(".c"), Syntax.CLike, " || ", " && "),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, " || ", " && "),
        new("go", CodePattern.Files(".go"), Syntax.CLike, " || ", " && "),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, " || ", " && "),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, " || ", " && "),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, " or ", " and "),
        new("php", CodePattern.Files(".php"), Syntax.CLike, " || ", " && "),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, " -or ", " -and ", IgnoreCase: true, Strict: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, " || ", " && "),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, " || ", " && "),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-two-steps-one-asked", dialect.Files, dialect.Syntax,
            (id, source, masked) => TwoStepsOneAsked(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>A name moved two on along one link: hare = hare.next.next, hare!.Next!.Next, hare-&gt;next-&gt;next, hare.getSuccessor().getSuccessor(), next[next[hare]].</summary>
    [GeneratedRegex(@"^\s*(?<h>[$@]?[A-Za-z_]\w*)\s*(?::=|=(?!=))\s*(?:\k<h>\s*!?\s*(?:\.|->)\s*(?<link>[A-Za-z_]\w*)\s*!?\s*(?:\.|->)\s*\k<link>|\k<h>\s*\.\s*(?<getter>get[A-Za-z_]\w*)\s*\(\s*\)\s*\.\s*\k<getter>\s*\(\s*\)|(?<array>[A-Za-z_]\w*)\s*\[\s*\k<array>\s*\[\s*\k<h>\s*\]\s*\])\s*;?\s*$")]
    private static partial Regex TwoOn();

    /// <summary>A test's word: if, elif, else if, elsif, elseif, unless, while, until.</summary>
    [GeneratedRegex(@"(?<![\w$.@-])(?:else\s+if|elseif|elsif|elif|if|unless|while|until)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TestWord();

    private static IEnumerable<LogicFinding> TwoStepsOneAsked(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        if (dialect.Strict && !masked.Any(line => Regex.IsMatch(line, @"^\s*Set-StrictMode\b", RegexOptions.IgnoreCase))) yield break;

        for (var line = 0; line < masked.Count; line++)
        {
            if (TwoOn().Match(masked[line]) is not { Success: true } moved || !Reaching.InALoop(masked, 0, line)) continue;
            var hare = moved.Groups["h"].Value;
            var name = Regex.Escape(hare);

            // Its next one read once - not twice - by the link it moves along.
            var oneOn = moved.Groups["link"].Success
                ? new Regex($@"(?<![\w$@.]){name}\s*!?\s*(?:\.|->)\s*{Regex.Escape(moved.Groups["link"].Value)}(?!\w)(?!\s*!?\s*(?:\.|->)\s*{Regex.Escape(moved.Groups["link"].Value)}(?!\w))", options)
                : moved.Groups["getter"].Success
                    ? new Regex($@"(?<![\w$@.]){name}\s*\.\s*{Regex.Escape(moved.Groups["getter"].Value)}\s*\(\s*\)(?!\s*\.\s*{Regex.Escape(moved.Groups["getter"].Value)})", options)
                    : new Regex($@"(?<![\w$@.]){Regex.Escape(moved.Groups["array"].Value)}\s*\[\s*{name}\s*\]", options);
            var alone = new Regex($@"(?<![\w$@.]){name}(?!\w)", options);

            foreach (var test in LoopTests(masked, line, dialect))
            {
                var condition = test.Wording.Condition;
                if (!oneOn.IsMatch(condition)) continue;

                // The name asked about alone - hare is None, hare == null, !defined $hare - with its reads taken out.
                if (alone.IsMatch(oneOn.Replace(condition, " "))) continue;

                // The test as written: TypeScript's lines are read with their types - and hare!'s ! - turned to spaces.
                var at = masked[test.Line].IndexOf(condition, test.Wording.Start, StringComparison.Ordinal);
                var written = at >= 0 && at + condition.Length <= source.Lines[test.Line].Length ? source.Lines[test.Line].Substring(at, condition.Length) : condition;
                var told = $"Line {line + 1} moves `{hare}` two nodes on each time round, but line {test.Line + 1} asks only whether the next one is " +
                           $"there: `{written}`. When it is but the one after is not, `{hare}` lands on nothing, and asking for its next node then " +
                           "fails.";
                yield return new LogicFinding(id, test.Line + 1, told, AskAboutItFirst(id, source, masked, test.Line, test.Wording, oneOn, hare, dialect))
                {
                    Severity = Severity.Error,
                    Confidence = Confidence.Likely,
                };
                break;
            }
        }
    }

    /// <summary>The tests of the loop the line is in - its own first line among them, if it asks something - nearest first.</summary>
    private static IEnumerable<(int Line, Reaching.Wording Wording)> LoopTests(IReadOnlyList<string> masked, int line, Dialect dialect)
    {
        var indent = CodeText.Indentation(masked[line]).Length;
        for (var above = line - 1; above >= 0 && indent > 0; above--)
        {
            if (masked[above].Trim().Length == 0 || masked[above].Trim() == "{") continue;
            var width = CodeText.Indentation(masked[above]).Length;
            if (width >= indent) continue;
            indent = width;
            if (!Regex.IsMatch(masked[above], @"^\s*(?:\}\s*)?(?:while|for|do|loop|until)\b", RegexOptions.IgnoreCase)) continue;

            var lines = new List<int> { above };
            for (var below = above + 1; below < masked.Count; below++)
            {
                if (masked[below].Trim().Length == 0) continue;
                if (CodeText.Indentation(masked[below]).Length <= width && masked[below].Trim() != "{") break;
                lines.Add(below);
            }

            foreach (var each in lines)
            {
                foreach (Match word in TestWord().Matches(masked[each]))
                {
                    if (Reaching.Written(masked[each], word.Index + word.Length, dialect.ColonEnds) is { } wording) yield return (each, wording);
                }
            }

            yield break;
        }
    }

    /// <summary>
    /// The change: the name asked about first, the way the test asks about its next one - hare is None or hare.successor is
    /// None; fast is not None and fast.successor is not None; hare == NONE || next[hare] == NONE.
    /// </summary>
    private static LocalFix? AskAboutItFirst(string id, SourceFile source, IReadOnlyList<string> masked, int line, Reaching.Wording wording, Regex oneOn, string hare, Dialect dialect)
    {
        // The test as written, not as read: TypeScript's ! is a space in the lines read.
        var text = source.Lines[line];
        var at = masked[line].IndexOf(wording.Condition, wording.Start, StringComparison.Ordinal);
        if (at < 0 || at + wording.Condition.Length > text.Length) return null;
        var condition = text.Substring(at, wording.Condition.Length);

        var asked = Pieces(condition).FirstOrDefault(piece => oneOn.IsMatch(piece));
        if (asked is null) return null;
        var aboutIt = oneOn.Replace(asked.Trim(), _ => hare, 1);
        // A test going on while the next one is there - != null, is not None - is joined with and; one stopping when it is not, with or.
        var whilePresent = Regex.IsMatch(asked, @"!==?|\bis\s+not\b|-ne\b|~=", RegexOptions.IgnoreCase);
        var both = $"{aboutIt}{(whilePresent ? dialect.And : dialect.Or)}{condition}";
        var changed = text[..at] + both + text[(at + condition.Length)..];
        return LocalFix.ReplaceLine(id, $"Ask whether {hare} is there first: {both}",
            $"Moved two on, `{hare}` itself may be nothing: asking that first stops there, before its next node is asked for.",
            source.Path, line + 1, changed);
    }

    /// <summary>The pieces of a test between its or's and and's, at the top level of its brackets.</summary>
    private static List<string> Pieces(string condition)
    {
        var pieces = new List<string>();
        var depth = 0;
        var from = 0;
        for (var at = 0; at < condition.Length; at++)
        {
            if (condition[at] is '(' or '[' or '{') depth++;
            else if (condition[at] is ')' or ']' or '}') depth--;
            else if (depth == 0 && Regex.Match(condition[at..], @"^(?:\|\||&&|-or\b|-and\b|\bor\b|\band\b)", RegexOptions.IgnoreCase) is { Success: true } join &&
                     (at == 0 || !char.IsLetterOrDigit(condition[at - 1])))
            {
                pieces.Add(condition[from..at]);
                at += join.Length - 1;
                from = at + 1;
            }
        }

        pieces.Add(condition[from..]);
        return pieces;
    }
}
