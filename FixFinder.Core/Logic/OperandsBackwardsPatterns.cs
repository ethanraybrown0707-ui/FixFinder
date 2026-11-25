using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A reverse Polish evaluator using the two values it takes off the stack the wrong way round. The value taken first was
/// pushed last: it is the right-hand operand, and the one taken after it the left-hand one. Used as a - b with a taken
/// first, subtraction and division come out turned round: 10 2 - 3 - gave 11, where 5 is right. QuixBugs' rpn_eval is this
/// mistake, in Python and in Java.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: two values taken one after the other off the same stack - a = stack.pop(),
/// Double a = (Double) stack.pop(), $a = array_pop($stack), local a = table.remove(stack), int a = stack[--top], a =
/// stack.back() then stack.pop_back(), a := stack[len(stack)-1] then the stack cut, a = stack(top) then top = top - 1 - and
/// the first used on the left of a subtraction or division with the second on its right, a - b; or given first to a call
/// of an operation the token picks, op(token, a, b), bin_op.apply(a, b), in a file writing an operation that subtracts
/// or divides its first by its second, (a, b) -&gt; a - b. For F#, OCaml, Elixir and Erlang, the two taken apart in a
/// pattern - a :: b :: rest, [a, b | rest], the first the top - and the line giving back a - b. The change takes them the
/// right way round: the call given them in the other order, as QuixBugs' correct programs have it, or the names the
/// values are given swapped, or the pattern's two swapped.
/// </remarks>
public static partial class OperandsBackwardsPatterns
{
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, bool Patterns = false, bool IgnoreCase = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python),
        new("java", CodePattern.Files(".java"), Syntax.CLike),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike),
        new("c", CodePattern.Files(".c"), Syntax.CLike),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike),
        new("go", CodePattern.Files(".go"), Syntax.CLike),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua),
        new("php", CodePattern.Files(".php"), Syntax.CLike),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, IgnoreCase: true),
        new("fsharp", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp, Patterns: true),
        new("ocaml", CodePattern.Files(".ml"), Syntax.OCaml, Patterns: true),
        new("elixir", CodePattern.Files(".ex", ".exs"), Syntax.Python, Patterns: true),
        new("erlang", CodePattern.Files(".erl"), Syntax.Erlang, Patterns: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-operands-taken-backwards", dialect.Files, dialect.Syntax,
            (id, source, masked) => OperandsBackwards(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>
    /// A value given a name, up to what it is given: a = , var a = , Integer a = , let a = , local a = , my $a = , a := - the
    /// value is the rest of the line, read without a regex: a lazy one with the line's end after it is slow on long lines.
    /// </summary>
    [GeneratedRegex(@"^\s*(?:(?:var|let|mut|const|final|val|local|my|auto|int|long|double|float|Integer|Double|Object|number|BigDecimal)\s+)*(?<name>[$]?[A-Za-z_]\w*)\s*(?::\s*[\w<>\[\]]+\s*)?(?::=|=)(?!=)\s*")]
    private static partial Regex Named();

    /// <summary>
    /// A value taken off a stack: stack.pop(), (Double) stack.pop(), stack.Pop(), stack.removeLast(), stack.pop().unwrap(),
    /// stack.pop()!, stack.pop, table.remove(stack), array_pop($stack), pop @stack, stack[--top] - the stack in the group named
    /// stack.
    /// </summary>
    [GeneratedRegex(@"^(?:\(\s*[\w<>]+\s*\)\s*)?[$]?(?<stack>[A-Za-z_]\w*)\s*\.\s*(?:pop|Pop|removeLast)\s*(?:\(\s*\))?\s*(?:\.\s*(?:unwrap|expect)\s*\([^()]*\)|!|\?)?$|^table\.remove\(\s*(?<stack>[A-Za-z_]\w*)\s*\)$|^array_pop\(\s*\$(?<stack>[A-Za-z_]\w*)\s*\)$|^pop\s*\(?\s*@(?<stack>[A-Za-z_]\w*)\s*\)?$|^(?<stack>[A-Za-z_]\w*)\s*\[\s*--\s*[A-Za-z_]\w*\s*\]$")]
    private static partial Regex Taken();

    /// <summary>
    /// A value read from a stack's top, taken off by the next statement: stack.back(), stack.top(), stack[len(stack)-1],
    /// stack(top) - the stack in the group named stack, and the top's name, for Fortran, in the group named top.
    /// </summary>
    [GeneratedRegex(@"^(?<stack>[A-Za-z_]\w*)\s*\.\s*(?:back|top|peek|Peek)\s*\(\s*\)$|^(?<stack>[A-Za-z_]\w*)\s*\[\s*len\(\s*\k<stack>\s*\)\s*-\s*1\s*\]$|^(?<stack>[A-Za-z_]\w*)\s*\(\s*(?<top>[A-Za-z_]\w*)\s*\)$")]
    private static partial Regex ReadFromTheTop();

    /// <summary>What every way of taking a value off a stack has in it: pop, Pop, removeLast, remove, back, top, peek, len, --.</summary>
    private static bool MayTake(string line) =>
        line.Contains("pop", StringComparison.Ordinal) || line.Contains("Pop", StringComparison.Ordinal) || line.Contains("removeLast", StringComparison.Ordinal) ||
        line.Contains("remove(", StringComparison.Ordinal) || line.Contains("--", StringComparison.Ordinal) || line.Contains("back(", StringComparison.Ordinal) ||
        line.Contains("top", StringComparison.OrdinalIgnoreCase) || line.Contains("peek", StringComparison.OrdinalIgnoreCase) || line.Contains("len(", StringComparison.Ordinal);

    /// <summary>What a pattern taking two values apart has in it: a :: b :: rest, [a, b | rest].</summary>
    private static bool MayTakeApart(string line) => line.Contains("::", StringComparison.Ordinal) || line.Contains('|');

    private static IEnumerable<LogicFinding> OperandsBackwards(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        if (!masked.Any(dialect.Patterns ? MayTakeApart : MayTake)) yield break;
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var findings = dialect.Patterns ? PatternsBackwards(id, source, masked, options) : TakenBackwards(id, source, masked, options);
        foreach (var finding in findings) yield return finding;
    }

    /// <summary>A value taken off a stack on this line, with the line after it that takes it off when the line only reads the top: the name it is given, the stack, and the last line of the taking.</summary>
    private static (string Name, string Stack, int Last)? TakeAt(IReadOnlyList<string> masked, int line, RegexOptions options)
    {
        if (!MayTake(masked[line]) || Named().Match(masked[line]) is not { Success: true } named) return null;
        var value = masked[line][named.Length..].Trim();
        if (value.EndsWith(';')) value = value[..^1].TrimEnd();
        var name = named.Groups["name"].Value;
        if (Taken().Match(value) is { Success: true } taken) return (name, taken.Groups["stack"].Value, line);
        if (ReadFromTheTop().Match(value) is not { Success: true } read || line + 1 >= masked.Count) return null;

        // The next statement takes it off: stack.pop_back(), stack.pop(), stack = stack[:len(stack)-1], top = top - 1.
        var stack = Regex.Escape(read.Groups["stack"].Value);
        var next = masked[line + 1];
        var takenOff = read.Groups["top"].Success
            ? Regex.IsMatch(next, $@"^\s*{Regex.Escape(read.Groups["top"].Value)}\s*=\s*{Regex.Escape(read.Groups["top"].Value)}\s*-\s*1\s*$", options)
            : Regex.IsMatch(next, $@"^\s*{stack}\s*\.\s*(?:pop_back|pop|Pop)\s*\(\s*\)\s*;?\s*$|^\s*{stack}\s*=\s*{stack}\s*\[\s*:\s*len\(\s*{stack}\s*\)\s*-\s*1\s*\]\s*;?\s*$", options);
        return takenOff ? (name, read.Groups["stack"].Value, line + 1) : null;
    }

    private static IEnumerable<LogicFinding> TakenBackwards(string id, SourceFile source, IReadOnlyList<string> masked, RegexOptions options)
    {
        for (var line = 0; line < masked.Count; line++)
        {
            if (TakeAt(masked, line, options) is not { } first) continue;
            if (first.Last + 1 >= masked.Count || TakeAt(masked, first.Last + 1, options) is not { } second) continue;
            if (second.Stack != first.Stack || Same(first.Name, second.Name, options)) continue;

            var used = Regex.Escape(first.Name.TrimStart('$'));
            var after = Regex.Escape(second.Name.TrimStart('$'));
            var take = $"`{first.Name}` and `{second.Name}`";

            // What is done with them, on the lines after - up to the next taking off the same stack.
            for (var at = second.Last + 1; at < Math.Min(masked.Count, second.Last + 13); at++)
            {
                if (TakeAt(masked, at, options) is { } again && again.Stack == first.Stack) break;
                var text = masked[at];

                // a - b, a / b: the first taken on the left.
                var direct = Regex.Match(text, $@"(?<![\w$@.])[$]?{used}\s*(?<sign>-(?![-=>])|/(?![/=]))\s*[$]?{after}(?!\w)", options);
                if (direct.Success)
                {
                    var swapped = Swapped(source, line, first.Last + 1, second.Last, first.Name, second.Name);
                    yield return new LogicFinding(id, line + 1, Told(line, take, first.Name, second.Name, $"{first.Name} {direct.Groups["sign"].Value} {second.Name}"),
                        new LocalFix
                        {
                            RuleId = id,
                            Title = $"Take them the right way round: {swapped[0].Trim()}",
                            Explanation = $"The value taken first is the right-hand operand; named so, `{first.Name} {direct.Groups["sign"].Value} {second.Name}` takes the left-hand one from it.",
                            File = source.Path,
                            StartLine = line + 1,
                            RemoveCount = second.Last - line + 1,
                            NewLines = swapped,
                        });
                    line = second.Last;
                    break;
                }

                // op(token, a, b), bin_op.apply(a,b), operations[token](a, b): the first given first to an operation the token picks.
                var call = Regex.Match(text, $@"\((?<before>\s*(?:[^()]*,\s*)?)(?<first>[$]?{used})(?<between>\s*,\s*)(?<second>[$]?{after})\s*\)", options);
                if (call.Success && SubtractsOrDivides(masked) && TokenPicks(masked, line, at, text))
                {
                    var written = source.Lines[at];
                    var changed = written[..call.Groups["first"].Index] + call.Groups["second"].Value + call.Groups["between"].Value + call.Groups["first"].Value +
                                  written[(call.Groups["second"].Index + call.Groups["second"].Length)..];
                    yield return new LogicFinding(id, at + 1, Told(line, take, first.Name, second.Name, null),
                        LocalFix.ReplaceLine(id, $"Give them the other way round: {call.Groups["second"].Value}{call.Groups["between"].Value}{call.Groups["first"].Value}",
                            $"The value taken first, `{first.Name}`, is the right-hand operand, so it goes second.",
                            source.Path, at + 1, changed));
                    line = second.Last;
                    break;
                }
            }
        }
    }

    private static string Told(int line, string take, string first, string second, string? written) =>
        $"Line {line + 1} takes {take} off the stack in that order - so `{first}` is the value pushed last, the right-hand operand, and " +
        $"`{second}` the left-hand one - but " + (written is null ? $"`{first}` is given first, as the left-hand one" : $"`{written}` uses them the other way round") +
        ": subtraction and division come out turned round, and 10 2 - 3 - gives 11 where 5 is right.";

    /// <summary>The taking lines, from the first value's to the last line taking the second off, with the two names swapped on the lines naming them and the others as they are.</summary>
    private static List<string> Swapped(SourceFile source, int first, int second, int last, string firstName, string secondName)
    {
        var lines = new List<string>();
        for (var at = first; at <= last; at++)
        {
            var text = source.Lines[at];
            var name = at == first ? firstName : at == second ? secondName : null;
            var other = at == first ? secondName : firstName;
            if (name is not null && Regex.Match(text, $@"(?<![\w$@.]){Regex.Escape(name)}(?!\w)") is { Success: true } found)
                text = text[..found.Index] + other + text[(found.Index + found.Length)..];
            lines.Add(text);
        }

        return lines;
    }

    private static bool Same(string first, string second, RegexOptions options) =>
        string.Equals(first, second, options.HasFlag(RegexOptions.IgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Whether the file writes an operation subtracting or dividing its first by its second: (a, b) -&gt; a - b, lambda a, b: a - b, (x, y) =&gt; x / y.</summary>
    private static bool SubtractsOrDivides(IReadOnlyList<string> masked) =>
        masked.Any(text => Regex.IsMatch(text, @"(?<![\w$])(?<first>[A-Za-z_]\w*)\s*,\s*(?<second>[A-Za-z_]\w*)\s*\)?\s*(?:->|=>|:)\s*\k<first>\s*[-/]\s*\k<second>(?!\w)"));

    /// <summary>Whether the operation called is picked by the token: the token named on the call's line or a line before it since the taking - op(token, ...), op.get(token).</summary>
    private static bool TokenPicks(IReadOnlyList<string> masked, int taking, int call, string text)
    {
        // The token: the name the loop around the taking goes through, for token in tokens.
        if (Reaching.InnermostLoop(masked, 0, taking) is not { } loop) return false;
        var heading = masked[loop.Heading];
        var walked = Regex.Match(heading, @"\b(?:for|foreach)\s*\(?\s*(?:(?:var|let|const|final|Object|String|auto|my)\s+)?[$]?(?<item>[A-Za-z_]\w*)\s*(?:\bin\b|:|\bof\b)");
        if (!walked.Success) return false;
        var item = new Regex($@"(?<![\w$@.])[$]?{Regex.Escape(walked.Groups["item"].Value)}(?!\w)");
        return Enumerable.Range(taking, call - taking + 1).Any(at => item.IsMatch(masked[at]));
    }

    /// <summary>
    /// The functional languages' pattern taking the two values apart, the first the top of the stack, on a line giving back the
    /// first minus or over the second: "-", a :: b :: rest -&gt; (a - b) :: rest; step("-", [A, B | Rest]) -&gt; [A - B | Rest].
    /// </summary>
    private static IEnumerable<LogicFinding> PatternsBackwards(string id, SourceFile source, IReadOnlyList<string> masked, RegexOptions options)
    {
        for (var line = 0; line < masked.Count; line++)
        {
            var text = masked[line];
            if (!MayTakeApart(text)) continue;
            var pattern = Regex.Match(text, @"(?<first>[A-Za-z_]\w*)\s*::\s*(?<second>[A-Za-z_]\w*)\s*::\s*[A-Za-z_]\w*|\[\s*(?<first>[A-Za-z_]\w*)\s*,\s*(?<second>[A-Za-z_]\w*)\s*\|\s*[A-Za-z_]\w*\s*\]");
            if (!pattern.Success) continue;
            var first = pattern.Groups["first"];
            var second = pattern.Groups["second"];
            var rest = text[(pattern.Index + pattern.Length)..];
            var used = Regex.Match(rest, $@"(?<![\w$@.]){Regex.Escape(first.Value)}\s*(?<sign>-(?![->])|/(?![/=]))\s*{Regex.Escape(second.Value)}(?!\w)");
            if (!used.Success) continue;

            var written = source.Lines[line];
            var changed = written[..first.Index] + second.Value + written[(first.Index + first.Length)..second.Index] + first.Value + written[(second.Index + second.Length)..];
            var told = $"Line {line + 1} takes `{first.Value}` and then `{second.Value}` off the stack - so `{first.Value}`, the top, is the value pushed last, " +
                       $"the right-hand operand - but `{first.Value} {used.Groups["sign"].Value} {second.Value}` uses them the other way round: subtraction and " +
                       "division come out turned round, and 10 2 - 3 - gives 11 where 5 is right.";
            yield return new LogicFinding(id, line + 1, told,
                LocalFix.ReplaceLine(id, $"Take them the right way round: {second.Value}, then {first.Value}",
                    $"The top of the stack is the right-hand operand; named `{second.Value}`, `{first.Value} {used.Groups["sign"].Value} {second.Value}` takes the left-hand one from it.",
                    source.Path, line + 1, changed));
        }
    }
}
