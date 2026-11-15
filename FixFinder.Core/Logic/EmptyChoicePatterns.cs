using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A function making every choice of k items by calling itself for the rest of each choice and putting an item in front of
/// each one it gives back - whose base case, with nothing left to choose, gives back no choices. There is one: choosing
/// nothing. With none at the bottom there is nothing to put an item in front of, so every call gives back none and the
/// function finds no choices at all. QuixBugs' subsequences is this mistake, in Python and in Java. The change gives back the
/// one empty choice - [[]] - as QuixBugs' correct programs do.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: a function's base case gives back an empty list when the count left to choose
/// is 0 - if k == 0: return [], return [] if count.zero?, if count = 0 then [] else, choices(_From, _To, 0) -&gt; [] - and the
/// function goes through what a call of itself gives back: for rest in subsequences(...), for (ArrayList rest :
/// subsequences(...)), choices(...).map(...), rest &lt;- choices(...), List.map (fun rest -&gt; ...) (choices ...).
/// </remarks>
public static partial class EmptyChoicePatterns
{
    /// <summary>How one language gives back an empty list for the count of 0, and the list holding the empty choice in its place.</summary>
    /// <param name="Empty">The base case giving back an empty list - the list in the group named empty - with its test on the line or the line above.</param>
    /// <param name="WithTheEmptyChoice">The list holding the empty choice, given the empty list as written.</param>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, Regex Empty, Func<string, string> WithTheEmptyChoice, bool IgnoreCase = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, new(@"^\s*return\s+(?<empty>\[\s*\])\s*$"), _ => "[[]]"),
        new("java", CodePattern.Files(".java"), Syntax.CLike, new(@"^\s*return\s+(?<empty>new\s+(?:Array|Linked)List\s*(?:<[^()]*>)?\s*\(\s*\))\s*;\s*$"), empty => empty),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, new(@"^\s*return\s+(?<empty>new\s+List\s*<\s*List\s*<\s*(?<item>\w+)\s*>\s*>\s*\(\s*\))\s*;\s*$"),
            empty => empty[..empty.LastIndexOf('(')].TrimEnd() + " { new List<" + Regex.Match(empty, @"List\s*<\s*List\s*<\s*(\w+)").Groups[1].Value + ">() }"),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, new(@"\breturn\s+(?<empty>\[\s*\])\s*;?\s*$"), _ => "[[]]"),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, new(@"^\s*return\s+(?<empty>\{\s*\})\s*;\s*$"), _ => "{{}}"),
        new("go", CodePattern.Files(".go"), Syntax.CLike, new(@"^\s*return\s+(?<empty>\[\]\[\]\w+\{\s*\})\s*$"), empty => empty[..^1].TrimEnd() + "{}}"),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, new(@"^\s*(?:return\s+)?(?<empty>vec!\[\s*\]|Vec::new\(\))\s*;?\s*$"), _ => "vec![vec![]]"),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, new(@"^\s*return\s+(?<empty>\[\s*\])\s+if\b"), _ => "[[]]"),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, new(@"^\s*return\s+(?<empty>\{\s*\})\s*$"), _ => "{ {} }"),
        new("php", CodePattern.Files(".php"), Syntax.CLike, new(@"^\s*return\s+(?<empty>\[\s*\]|array\(\s*\))\s*;\s*$"), _ => "[[]]"),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, new(@"^\s*return\s+(?<empty>\(\s*\))\s*(?:if\b|;)"), _ => "([])"),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, new(@"^\s*return\s+(?<empty>,?\s*@\(\s*\))\s*$"), _ => ",@(,@())", IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, new(@"^\s*return\s+(?<empty>(?:<[^<>]*>)?\[\s*\])\s*;\s*$"), _ => "[[]]"),
        // The else after the empty list on the same line, or on the next.
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, new(@"\bif\s*\([^()]*\)\s*(?<empty>List\(\s*\)|Nil)(?:\s+else\b|\s*$)"), empty => empty == "Nil" ? "List(Nil)" : "List(List())"),
        new("fsharp", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp, new(@"\bif\s+[^\n]+?\s+then\s+(?<empty>\[\s*\])(?:\s+else\b|\s*$)"), _ => "[ [] ]"),
        new("ocaml", CodePattern.Files(".ml"), Syntax.OCaml, new(@"\bif\s+[^\n]+?\s+then\s+(?<empty>\[\s*\])(?:\s+else\b|\s*$)"), _ => "[ [] ]"),
        new("elixir", CodePattern.Files(".ex", ".exs"), Syntax.Python, new(@"^\s*defp?\s+[a-z_]\w*[?!]?\s*\([^()]*\b0\s*\)\s*,\s*do:\s*(?<empty>\[\s*\])\s*$"), _ => "[[]]"),
        new("erlang", CodePattern.Files(".erl"), Syntax.Erlang, new(@"^\s*[a-z_]\w*\s*\([^()]*\b0\s*\)\s*->\s*(?<empty>\[\s*\])\s*;\s*$"), _ => "[[]]"),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-empty-choice-missing", dialect.Files, dialect.Syntax,
            (id, source, masked) => EmptyChoicesMissing(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>The count left to choose tested to be 0: k == 0, count === 0, count.zero?, $count -eq 0, count = 0 - or a clause with 0 in a parameter's place.</summary>
    [GeneratedRegex(@"(?<![\w$@.])[$]?[A-Za-z_]\w*\s*(?:===?|-eq|=)\s*0(?![\w.])|\.zero\?|\(\s*(?:[^()]*,\s*)?0\s*\)\s*(?:,\s*do:|->)", RegexOptions.IgnoreCase)]
    private static partial Regex NothingLeft();

    private static IEnumerable<LogicFinding> EmptyChoicesMissing(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        for (var line = 0; line < masked.Count; line++)
        {
            if (dialect.Empty.Match(masked[line]) is not { Success: true } given) continue;

            // Its test of the count, on the line itself or the line above - past a { on a line of its own.
            var above = line - 1;
            while (above >= 0 && masked[above].Trim() is "" or "{") above--;
            if (!NothingLeft().IsMatch(masked[line]) && !(above >= 0 && NothingLeft().IsMatch(masked[above]))) continue;

            // The function it is in, and that function going through what a call of itself gives back.
            if (FunctionAt(masked, line) is not { } function) continue;
            if (!GoesThroughItsOwnResults(masked, line, function.Name, options)) continue;

            // Another base case giving a choice - return [[n]] when the target is found - makes this empty one right.
            if (Enumerable.Range(function.Heading, Math.Min(masked.Count, line + 20) - function.Heading)
                .Any(at => at != line && GivesAChoice().IsMatch(masked[at]))) continue;

            var empty = given.Groups["empty"];
            var written = source.Lines[line];
            var emptyWritten = written.Substring(empty.Index, empty.Length);
            LocalFix fix;
            if (dialect.Language == "java")
            {
                // Java's list of lists made and given the empty one, then given back: its type the function's own.
                var indent = CodeText.Indentation(written);
                fix = new LocalFix
                {
                    RuleId = id,
                    Title = "Give back the one empty choice: nothingChosen.add(new ArrayList<>())",
                    Explanation = "With nothing left to choose there is one choice - choosing nothing - and putting an item in front of it gives each choice of one.",
                    File = source.Path,
                    StartLine = line + 1,
                    RemoveCount = 1,
                    NewLines = [$"{indent}{function.Returns} nothingChosen = {emptyWritten};", $"{indent}nothingChosen.add(new ArrayList<>());", $"{indent}return nothingChosen;"],
                };
            }
            else
            {
                var holding = dialect.WithTheEmptyChoice(emptyWritten);
                fix = LocalFix.ReplaceLine(id, $"Give back the one empty choice: {holding}",
                    "With nothing left to choose there is one choice - choosing nothing - and putting an item in front of it gives each choice of one.",
                    source.Path, line + 1, written[..empty.Index] + holding + written[(empty.Index + empty.Length)..]);
            }

            var told = $"Line {line + 1} gives back no choices when there is nothing left to choose - but there is one: choosing nothing. `{function.Name}` puts an item " +
                       "in front of each choice a call of itself gives back, so with none at the bottom every call gives back none, and it finds no choices at all.";
            yield return new LogicFinding(id, line + 1, told, fix);
        }
    }

    /// <summary>
    /// A choice given back as written - a list holding a list: return [[n]], [[value]], {{n}}, vec![vec![n]], List(List(n)),
    /// ([n]) - which a base case of its own gives, making an empty one beside it right.
    /// </summary>
    [GeneratedRegex(@"\breturn\b[^;\n]*(?:\[\s*\[|\{\s*\{|vec!\[\s*vec!|List\(\s*List\(|\(\s*\[)|(?:->|do:|then)\s*(?:\[\s*\[|\[\s*\[)")]
    private static partial Regex GivesAChoice();

    /// <summary>The function a line is in - its name, its heading's line and, for Java, what it gives back - from the nearest heading at or above it.</summary>
    private static (string Name, int Heading, string Returns)? FunctionAt(IReadOnlyList<string> masked, int line)
    {
        for (var above = line; above >= 0; above--)
        {
            var text = masked[above];
            var heading = Regex.Match(text, @"\b(?:def|defp|function|func|fn|sub|let\s+rec)\s+(?<name>[A-Za-z_][\w-]*[?!]?)|^\s*(?:(?:public|private|protected|static)\s+)*(?<returns>[\w<>\[\]:,\s]+?)\s+(?<name>[A-Za-z_]\w*)\s*\([^)]*\)\s*\{?\s*$|^(?<name>[a-z_]\w*)\s*\([^()]*\)\s*->");
            if (!heading.Success || Regex.IsMatch(heading.Groups["name"].Value, @"^(?:if|for|while|switch|return|catch|foreach)$")) continue;
            return (heading.Groups["name"].Value, above, heading.Groups["returns"].Success ? heading.Groups["returns"].Value.Trim() : "");
        }

        return null;
    }

    /// <summary>
    /// Whether the function goes through what a call of itself gives back, within the lines after the base case: for rest in
    /// f(...), for (T rest : f(...)), foreach (var rest in F(...)), for (const rest of f(...)), range f(...), f(...).map,
    /// ipairs(f(...)), foreach (f(...) as $rest), for my $rest (f(...)), rest &lt;- f(...), List.map (...) (f ...).
    /// </summary>
    private static bool GoesThroughItsOwnResults(IReadOnlyList<string> masked, int line, string function, RegexOptions options)
    {
        var name = Regex.Escape(function);
        var through = new Regex(
            $@"(?:\bin\b|:|\bof\b|<-|\brange\b|ipairs\s*\(|\bforeach\s*\(|\bfor\s+my\s+\$\w+\s*\()\s*\(?\s*{name}\s*[\s(]|(?<![\w$-])(?<![^.]\.){name}\s*\([^()]*(?:\([^()]*\)[^()]*)*\)\s*\.\s*(?:map|flat_map|flatMap|Select|SelectMany|each|forEach)\b|\bList\.map\b.*\(\s*{name}\b|\bforeach\s*\(\s*{name}\s*\(",
            options);
        // From the base case's own line on: F#'s if count = 0 then [] else [ for ... in choices ... ] goes on beside it.
        return Enumerable.Range(line, Math.Min(masked.Count, line + 20) - line).Any(at => through.IsMatch(masked[at]));
    }
}
