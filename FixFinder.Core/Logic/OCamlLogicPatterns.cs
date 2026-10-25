using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes OCaml programs make that still compile: a for loop to a length, which OCaml counts up to and including; ==
/// and != - which ask whether two values are the very same one in memory - used for text, lists and floats, where = and
/// &lt;&gt; were meant; a whole-number division turned into a float after its fraction was already dropped; a decimal worked
/// out by arithmetic and then compared exactly; a grid made with Array.make of one row; two lines under a then, the first
/// ending with ;, of which then takes only the first; and Hashtbl.add of a key just read, which adds a second binding where
/// one was to be changed. OCamlLogicLiveTests runs each of the last four, and its change, with OCaml 5.5.2 on CI - each mistake
/// compiled with no warning: 0.1 +. 0.2 printed 0.30000000000000004, and = 0.3 was false; after grid.(0).(0) &lt;- 1 every row
/// printed 100; the total of the even numbers from 1 to 4 came to 10, not 6; and counting a, b and a, Hashtbl.find gave 2 for
/// a, but Hashtbl.length and a fold both counted 3. Each change printed what was meant: equal, 100 000 000, 6, and 2 and 2.
/// </summary>
public static partial class OCamlLogicPatterns
{
    private static readonly IReadOnlySet<string> OCamlFiles = CodePattern.Files(".ml");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely) =>
        new(id, OCamlFiles, Syntax.OCaml, find, severity, confidence);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-ocaml-for-to-length", ForToLength, Severity.Error),
        Pattern("logic-ocaml-physical-equality", PhysicalEquality, Severity.Error),
        Pattern("logic-ocaml-integer-average", IntegerAverage, Severity.Error),
        Pattern("logic-ocaml-float-equality", FloatEquality),
        Pattern("logic-ocaml-rows-shared", RowsShared, Severity.Error, Confidence.Certain),
        Pattern("logic-ocaml-then-takes-one-expression", ThenTakesOneExpression, Severity.Error),
        Pattern("logic-ocaml-hashtbl-add-for-update", HashtblAddForUpdate, Severity.Error),
    ];

    /// <summary>
    /// <c>for i = 0 to Array.length a do</c> with <c>a.(i)</c> - or a.[i], Array.get a i, List.nth a i - inside: the last pass
    /// asks for position Array.length a, one past the end.
    /// </summary>
    private static IEnumerable<LogicFinding> ForToLength(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (OCamlLoops.ForToLength().Match(masked[index]) is not { Success: true } loop) continue;

            var variable = Regex.Escape(loop.Groups["variable"].Value);
            var collection = Regex.Escape(loop.Groups["collection"].Value);
            var position = new Regex($@"(?<![\w.']){collection}\s*\.\s*[(\[]\s*{variable}\s*[)\]]|\b(?:Array|String|Bytes)\.get\s+{collection}\s+{variable}\b|\bList\.nth\s+{collection}\s+{variable}\b");

            if (!Body(masked, index).Any(line => position.IsMatch(line))) continue;

            yield return new LogicFinding(id, index + 1,
                $"`for {loop.Groups["variable"].Value} = 0 to {loop.Groups["length"].Value}` counts up to and including {loop.Groups["length"].Value}, so its last pass asks " +
                $"{loop.Groups["collection"].Value} for a position one past its end",
                OCamlLoops.UpperBoundFix(id, source, index + 1, loop));
        }
    }

    /// <summary>The loop's body: from its do to the done that closes it.</summary>
    private static IEnumerable<string> Body(IReadOnlyList<string> masked, int loopLine)
    {
        var depth = 0;

        for (var index = loopLine; index < masked.Count; index++)
        {
            var line = index == loopLine ? masked[index][(masked[index].IndexOf(" do", StringComparison.Ordinal) + 3)..] : masked[index];
            yield return line;

            depth += Regex.Matches(line, @"\bdo\b").Count - Regex.Matches(line, @"\bdone\b").Count;
            if (depth < 0 || (index > loopLine && depth <= 0 && Regex.IsMatch(line, @"\bdone\b"))) yield break;
        }
    }

    /// <summary>
    /// == or != with text, a list that is not empty, or a decimal on one side - written in the code, so certainly one of them.
    /// Those are made in memory each time, so two equal ones need not be the same one: = and &lt;&gt; compare what they hold.
    /// </summary>
    [GeneratedRegex(@"(?<left>""\s*""|\[[^\]]*[^\]\s][^\]]*\]|\b\d+\.\d*)\s*(?<operator>==|!=)(?![=])|(?<![=<>!:])(?<operator>==|!=)\s*(?<right>""\s*""|\[[^\]]*[^\]\s][^\]]*\]|\d+\.\d*)")]
    private static partial Regex ComparedInMemory();

    private static IEnumerable<LogicFinding> PhysicalEquality(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (ComparedInMemory().Match(masked[index]) is not { Success: true } comparison) continue;

            var symbol = comparison.Groups["operator"];
            var meant = symbol.Value == "==" ? "=" : "<>";
            var line = source.Lines[index];
            var corrected = line[..symbol.Index] + meant + line[(symbol.Index + symbol.Length)..];

            yield return new LogicFinding(id, index + 1,
                $"`{symbol.Value}` asks whether two values are the very same one in memory, not whether they are {(meant == "=" ? "equal" : "different")} - and text, " +
                "lists and decimals are made anew each time, so two equal ones need not be the same one",
                LocalFix.ReplaceLine(id, $"Compare what they hold with {meant}",
                    $"{meant} compares the values themselves, which is what {symbol.Value} is almost always meant to do here.",
                    source.Path, index + 1, corrected));
        }
    }

    /// <summary><c>float_of_int (total / count)</c>: the division is between ints, so its fraction is gone before it becomes a float.</summary>
    [GeneratedRegex(@"\b(?<convert>float_of_int|float)\s*\(\s*(?<dividend>[^()/]+?|[^()/]*\([^()]*\)[^()/]*?)\s*/(?!\.)\s*(?<divisor>[^()/]+?|[^()/]*\([^()]*\)[^()/]*?)\s*\)")]
    private static partial Regex ConvertedAfterDividing();

    private static IEnumerable<LogicFinding> IntegerAverage(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        static string AsFloat(string value) => Regex.IsMatch(value, @"^[\w.']+$") ? $"float_of_int {value}" : $"float_of_int ({value})";

        for (var index = 0; index < masked.Count; index++)
        {
            if (ConvertedAfterDividing().Match(masked[index]) is not { Success: true } converted) continue;

            var line = source.Lines[index];
            var dividend = line.Substring(converted.Groups["dividend"].Index, converted.Groups["dividend"].Length).Trim();
            var divisor = line.Substring(converted.Groups["divisor"].Index, converted.Groups["divisor"].Length).Trim();
            var exact = $"({AsFloat(dividend)} /. {AsFloat(divisor)})";
            var corrected = line[..converted.Index] + exact + line[(converted.Index + converted.Length)..];

            yield return new LogicFinding(id, index + 1,
                $"`{dividend} / {divisor}` divides one int by another, which gives an int: its fraction is dropped before {converted.Groups["convert"].Value} " +
                "makes it a float - 7 / 2 is 3, so this gives 3. rather than 3.5",
                LocalFix.ReplaceLine(id, $"Divide as floats: {exact}",
                    "Turning each number into a float before dividing, and dividing with /., keeps the fraction.",
                    source.Path, index + 1, corrected));
        }
    }

    /// <summary><c>let price = 0.1 +. 0.2</c>: a name given a decimal worked out by arithmetic - and <c>in</c>, when it ends with one.</summary>
    [GeneratedRegex(@"^\s*let\s+(?<name>[a-z_][\w']*)\s*=\s*(?<value>.*\d+\.\d*.*?)(?:\s+in)?\s*$")]
    private static partial Regex DecimalWorkedOut();

    /// <summary><c>if price = 0.3 then</c>: a name compared exactly with a decimal, in a condition.</summary>
    [GeneratedRegex(@"\b(?:if|while|when)\b.*?(?<![\w.'<>=!:])(?<name>[a-z_][\w']*)\s*(?<op><>|=)(?![=>])\s*(?<value>\d+\.\d*[1-9]\d*)(?![\w.])")]
    private static partial Regex ComparedWithDecimal();

    /// <summary>
    /// A decimal worked out by arithmetic - +., -., *. or /. - compared exactly with a decimal: the arithmetic leaves a tiny
    /// error, so 0.1 +. 0.2 = 0.3 is false. The change compares the difference with a tiny tolerance.
    /// </summary>
    private static IEnumerable<LogicFinding> FloatEquality(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var workedOut = masked
            .Select(line => DecimalWorkedOut().Match(line))
            .Where(given => given.Success && Regex.IsMatch(Regex.Replace(given.Groups["value"].Value, @"\d+\.\d*", "0"), @"[-+*/]\."))
            .Select(given => given.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < masked.Count; index++)
        {
            if (ComparedWithDecimal().Match(masked[index]) is not { Success: true } compared || !workedOut.Contains(compared.Groups["name"].Value)) continue;

            var name = compared.Groups["name"].Value;
            var value = compared.Groups["value"].Value;
            var equal = compared.Groups["op"].Value == "=";
            var close = equal ? $"abs_float ({name} -. {value}) < 1e-9" : $"abs_float ({name} -. {value}) >= 1e-9";
            var start = compared.Groups["name"].Index;
            var end = compared.Groups["value"].Index + compared.Groups["value"].Length;
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"the arithmetic that gives {name} its value leaves a tiny error - 0.1 +. 0.2 is 0.30000000000000004 - so comparing {name} " +
                $"exactly with {value} is {(equal ? "false" : "true")} even when the two are equal for every practical purpose",
                LocalFix.ReplaceLine(id, $"Compare within a tiny tolerance: {close}",
                    "The difference between the two is compared with a tiny tolerance, which allows for the error the arithmetic left.",
                    source.Path, index + 1, line[..start] + close + line[end..]));
        }
    }

    /// <summary><c>Array.make 3 (Array.make 3 0)</c>, <c>Array.make 3 [| 0; 0; 0 |]</c> or <c>Array.make 3 row</c> - one array given as every item.</summary>
    [GeneratedRegex(@"\bArray\.make\s+(?<count>\w+)\s+(?:\((?<inner>Array\.make\s[^()]*)\)|(?<inner>\[\|[^\]]*\|\])|(?<name>[a-z_][\w']*)(?![\w'.]))")]
    private static partial Regex ArrayMakeOfArray();

    /// <summary>
    /// A grid made with Array.make of an array: Array.make puts the one value it is given in every place, and an array is one
    /// array wherever it is put - so every row is the same row. The change makes each row with Array.init.
    /// </summary>
    private static IEnumerable<LogicFinding> RowsShared(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match made in ArrayMakeOfArray().Matches(masked[index]))
            {
                var name = made.Groups["name"].Value;
                // A name is a row only when it is given an array: let row = Array.make 3 0, or let row = [| ... |].
                if (made.Groups["name"].Success && !Regex.IsMatch(code, $@"\blet\s+{Regex.Escape(name)}\s*=\s*(?:\[\||Array\.(?:make|init|copy)\b)")) continue;

                var line = source.Lines[index];
                var count = made.Groups["count"].Value;
                var row = made.Groups["inner"].Success ? line.Substring(made.Groups["inner"].Index, made.Groups["inner"].Length) : $"Array.copy {name}";
                var changed = $"Array.init {count} (fun _ -> {row})";

                yield return new LogicFinding(id, index + 1,
                    "Array.make puts the one value it is given in every place - and an array is one array wherever it is put, so every row is " +
                    "the same row: setting grid.(0).(0) changes the first cell of every row",
                    LocalFix.ReplaceLine(id, $"Make each row its own: {changed}",
                        "Array.init runs the function once for each row, so each row is a new array.",
                        source.Path, index + 1, line[..made.Index] + changed + line[(made.Index + made.Length)..]));
            }
        }
    }

    /// <summary><c>if mark &gt;= 50 then</c> - or <c>else if</c> - with nothing after then on its line.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?:else\s+)?if\b(?<condition>.+?)\bthen\s*$")]
    private static partial Regex ThenAtLineEnd();

    /// <summary>A line that starts an expression running on past a ; - let, match, try, fun, function - or one already bracketed.</summary>
    [GeneratedRegex(@"^\s*(?:let|match|try|fun|function|begin)\b|^\s*\(")]
    private static partial Regex RunsOnPastSemicolon();

    private static int IndentOf(string line) => line.Length - line.TrimStart().Length;

    private static int Depth(string line) => line.Count(c => c is '(' or '[') - line.Count(c => c is ')' or ']');

    /// <summary>
    /// <c>if mark &gt;= 50 then</c> with two lines under it, the first ending with ; - then takes one expression, and the ;
    /// ends it, so the lines after run whether or not the condition holds, however far they are indented. The change puts
    /// the lines in begin and end.
    /// </summary>
    private static IEnumerable<LogicFinding> ThenTakesOneExpression(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (ThenAtLineEnd().Match(masked[index]) is not { Success: true } header) continue;
            var indent = header.Groups["indent"].Length;

            var body = new List<int>();
            var after = index + 1;
            for (; after < masked.Count; after++)
            {
                if (masked[after].Trim().Length == 0) continue;
                if (IndentOf(masked[after]) <= indent) break;
                body.Add(after);
            }

            if (body.Count < 2) continue;
            var first = masked[body[0]];
            var bodyIndent = IndentOf(first);
            // ;; ends the whole phrase, not only the then.
            if (!first.TrimEnd().EndsWith(';') || first.TrimEnd().EndsWith(";;") || RunsOnPastSemicolon().IsMatch(first) || Depth(first) != 0) continue;
            if (IndentOf(masked[body[1]]) != bodyIndent) continue;
            // An else after the lines would make it a syntax error, which ocamlc names itself.
            if (after < masked.Count && Regex.IsMatch(masked[after], @"^\s*else\b")) continue;

            var condition = header.Groups["condition"].Value.Trim();
            var inBrackets = body.Sum(line => Depth(masked[line])) == 0;
            LocalFix? fix = null;
            if (inBrackets)
            {
                var lines = new List<string> { source.Lines[index].TrimEnd() + " begin" };
                var last = body[^1];
                lines.AddRange(source.Lines.Skip(index + 1).Take(last - index - 1));
                var lastLine = source.Lines[last].TrimEnd();
                var endsWithSemicolon = lastLine.EndsWith(';');
                lines.Add(endsWithSemicolon ? lastLine[..^1] : lastLine);
                lines.Add(new string(' ', indent) + (endsWithSemicolon ? "end;" : "end"));
                fix = new LocalFix
                {
                    RuleId = id, Title = "Put the lines the condition is for in begin ... end",
                    Explanation = "begin and end make the lines between them one expression, so then takes them all, and they run only when the condition holds.",
                    File = source.Path, StartLine = index + 1, RemoveCount = last - index + 1, NewLines = lines,
                };
            }

            yield return new LogicFinding(id, body[1] + 1,
                $"then takes one expression, and the ; on line {body[0] + 1} ends it - so line {body[1] + 1} runs whether or not {condition} holds, " +
                "though it is indented as if it belonged to the if",
                fix);
        }
    }

    /// <summary><c>Hashtbl.add counts word (count + 1)</c> - a table, and a key that is a name.</summary>
    [GeneratedRegex(@"\bHashtbl\.add\s+(?<table>[a-z_][\w'.]*)\s+(?<key>[a-z_][\w']*)\b")]
    private static partial Regex HashtblAdd();

    /// <summary>
    /// Hashtbl.add of a key the lines just before read with Hashtbl.find or find_opt: add puts a second binding on top of the
    /// first rather than changing it, so Hashtbl.length, iter and fold count both. A table the program takes bindings off with
    /// Hashtbl.remove - keeping the old ones under the new, as a table of names in scope does - is left alone. The change is
    /// Hashtbl.replace, which changes the binding there is.
    /// </summary>
    private static IEnumerable<LogicFinding> HashtblAddForUpdate(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match added in HashtblAdd().Matches(masked[index]))
            {
                var table = Regex.Escape(added.Groups["table"].Value);
                var key = Regex.Escape(added.Groups["key"].Value);
                if (Regex.IsMatch(code, $@"\bHashtbl\.remove\s+{table}\b")) continue;

                var readFirst = new Regex($@"\bHashtbl\.(?:find|find_opt)\s+{table}\s+{key}\b");
                if (!Enumerable.Range(Math.Max(0, index - 5), index - Math.Max(0, index - 5) + 1).Any(line => readFirst.IsMatch(masked[line]))) continue;

                var line = source.Lines[index];
                var call = added.Index + "Hashtbl.".Length;

                yield return new LogicFinding(id, index + 1,
                    $"Hashtbl.add puts a second binding for {added.Groups["key"].Value} on top of the one there is, rather than changing it - so " +
                    $"Hashtbl.length counts both, and Hashtbl.iter and fold go over both",
                    LocalFix.ReplaceLine(id, "Change the binding there is: Hashtbl.replace",
                        "Hashtbl.replace changes the binding the key has - or adds one when it has none - so each key is in the table once.",
                        source.Path, index + 1, line[..call] + "replace" + line[(call + "add".Length)..]));
            }
        }
    }
}
