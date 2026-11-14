using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// A function taking a list apart - its first item, then itself again on the rest - with no stop for when the list is
/// empty. Each call passes one item over, so one call comes with none left, and reading its first item fails: Python's
/// unpacking, Java's index out of bounds, Ruby's and Lua's arithmetic on nil - or, where a missing item reads as nothing,
/// calls that go on and on. QuixBugs' possible_change is this mistake, in Python and in Java. The change adds the list being empty
/// to the stop that gives nothing - total &lt; 0 or not coins - as QuixBugs' correct program does.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: the function reads its list's first item - coins[0], coins.head, coins(1),
/// first, *rest = coins - and calls itself with the rest of it in its place: coins[1:], coins.slice(1), coins.tail,
/// &amp;coins[1..], array_slice($coins, 1), coins(2:), or a name given the rest; and no test in the function names the list,
/// nor a count handed over one smaller with it - C's coins + 1, count - 1 - which would be how it knows how many are left.
/// A function counting down a number it is given beside the list, stopping at 0, names that number in its test, and is left
/// alone.
/// </remarks>
public static partial class EmptyListPatterns
{
    /// <summary>How one language is read, asks whether a list is empty, and joins two tests.</summary>
    /// <param name="Empty">The test that the list {L} is empty; for C, that the count {L} handed over with it is 0.</param>
    /// <param name="EmptyArray">The test for a list declared with [], where it differs: Java's .length, C#'s .Length.</param>
    /// <param name="Or">What joins two tests: or, ||, -or, .or.</param>
    /// <param name="Commands">Whether a function is called as a command - Get-PossibleChange $rest $total - as PowerShell's are.</param>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, string Empty, string Or, bool ColonEnds = false,
        bool IgnoreCase = false, string? EmptyArray = null, bool Commands = false, bool CountsBeside = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, "not {L}", " or ", ColonEnds: true),
        new("java", CodePattern.Files(".java"), Syntax.CLike, "{L}.isEmpty()", " || ", EmptyArray: "{L}.length == 0"),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, "{L}.Count == 0", " || ", EmptyArray: "{L}.Length == 0"),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, "{L}.length === 0", " || "),
        new("c", CodePattern.Files(".c"), Syntax.CLike, "{L} == 0", " || ", CountsBeside: true),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, "{L}.empty()", " || "),
        new("go", CodePattern.Files(".go"), Syntax.CLike, "len({L}) == 0", " || "),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, "{L}.is_empty()", " || "),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, "{L}.empty?", " || "),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, "#{L} == 0", " or "),
        new("php", CodePattern.Files(".php"), Syntax.CLike, "count({L}) === 0", " || "),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, "!@{L}", " || "),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, "{L}.Count -eq 0", " -or ", IgnoreCase: true, Commands: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, "{L}.isEmpty", " || "),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, "{L}.isEmpty", " || "),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, "size({L}) == 0", " .or. ", IgnoreCase: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-no-stop-when-empty", dialect.Files, dialect.Syntax,
            (id, source, masked) => ListsTakenApart(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>A test's word: if, elif, else if, elsif, elseif, unless, while, until, when.</summary>
    [GeneratedRegex(@"(?<![\w$.@-])(?:else\s+if|elseif|elsif|elif|if|unless|while|until|when)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TestWord();

    /// <summary>A value that is nothing: 0, false, an empty text or list, None, null, nil, undef - given back, or given to the function's result.</summary>
    [GeneratedRegex(@"^\s*(?:return\s+|[A-Za-z_]\w*\s*=\s*)?(?:0|false|False|\.false\.|""""|''|\[\s*\]|None|null|nil|undef|\$null|\$false)\s*;?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Nothing();

    /// <summary>
    /// The ways of writing the rest of the list {L}, past its first item: {L}[1:], {L}[1..], &amp;{L}[1..], {L}.slice(1),
    /// {L}.sublist(1), {L}.Skip(1), {L}.subList(1, ...), {L}.tail, {L}.drop(1), Arrays.copyOfRange({L}, 1, ...),
    /// array_slice({L}, 1), {L}(2:), {L} + 1, table.unpack({L}, 2), {L}.begin() + 1, Select-Object -Skip 1.
    /// </summary>
    private static readonly string[] RestForms =
    [
        @"{L}\s*\[\s*1\s*:\s*\]", @"&?\s*{L}\s*\[\s*1\s*\.\.\s*(?:\]|\()", @"{L}\s*\.\s*(?:slice|sublist|drop|Skip)\s*\(\s*1\s*\)",
        @"{L}\s*\.\s*subList\s*\(\s*1\s*,", @"{L}\s*\.\s*tail\b", @"Arrays\s*\.\s*copyOfRange\s*\(\s*{L}\s*,\s*1\s*,",
        @"array_slice\s*\(\s*{L}\s*,\s*1\s*\)", @"{L}\s*\(\s*2\s*:\s*\)", @"(?<![\w.])\(?\s*{L}\s*\+\s*1\b", @"table\s*\.\s*unpack\s*\(\s*{L}\s*,\s*2\s*\)",
        @"{L}\s*\.\s*begin\s*\(\s*\)\s*\+\s*1", @"{L}\s*\|\s*Select-Object\s+-Skip\s+1",
    ];

    /// <summary>
    /// The ways of reading the list {L}'s first item: {L}[0], {L}[1] (Lua), {L}(1) (Fortran), {L}.head, {L}.first, {L}.get(0),
    /// {L}.First(), {L}.front(), {L}->[0], $$L[0], *{L}.
    /// </summary>
    private static readonly string[] FirstForms =
    [
        @"{L}\s*\[\s*0\s*\]", @"{L}\s*\(\s*1\s*\)", @"{L}\s*\.\s*(?:head|first|First\(\)|front\(\)|get\(\s*0\s*\))", @"{L}\s*->\s*\[\s*0\s*\]",
    ];

    /// <summary>
    /// What every way of writing a list's rest has in it - [1:], [1.., slice(1), .tail, copyOfRange, first, *rest =, [first,
    /// ...rest] =, my ($first, @rest) - so a file without one is passed over; C's pointer one further on, + 1, is asked for
    /// in C alone.
    /// </summary>
    [GeneratedRegex(@"\[\s*1\s*:\s*\]|\[\s*1\s*\.\.|\.\s*(?:slice|sublist|drop|Skip)\s*\(\s*1\s*\)|\.\s*subList\s*\(\s*1|\.\s*tail\b|copyOfRange|array_slice|\(\s*2\s*:\s*\)|table\s*\.\s*unpack|\.\s*begin\s*\(\s*\)\s*\+\s*1|-Skip\s+1|,\s*\*\s*[A-Za-z_]\w*\s*=|\.\.\.\s*[A-Za-z_$][\w$]*\s*\]\s*=|my\s*\(\s*\$\w+\s*,\s*@", RegexOptions.IgnoreCase)]
    private static partial Regex RestWritten();

    private static IEnumerable<LogicFinding> ListsTakenApart(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        if (!masked.Any(line => RestWritten().IsMatch(line) || dialect.CountsBeside && line.Contains('+'))) yield break;
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var names = dialect.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (var function in SortingPatterns.FunctionsIn(masked, Path.GetExtension(source.Path).ToLowerInvariant()))
        {
            // Only a function calling itself takes a list apart this way: its calls, read once.
            var calls = function.Body
                .Where(line => masked[line].Contains(function.Name, dialect.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                .SelectMany(line => SelfCalls(masked[line], function.Name, dialect).Select(call => (Line: line, Call: call)))
                .Where(each => each.Call.Arguments.Count == function.Parameters.Count).ToList();
            if (calls.Count == 0) continue;

            for (var listAt = 0; listAt < function.Parameters.Count; listAt++)
            {
                var list = function.Parameters[listAt];
                if (calls.All(each => names.Equals(each.Call.Arguments[listAt].Trim(), list))) continue;

                var restForm = RestForm(list, options);
                var restNames = RestNames(masked, function.Body, list, restForm, options);
                if (FirstRead(masked, function.Body, list, dialect, options, restNames.Count > 0) is not { } firstRead) continue;

                // A call of itself with the rest of the list in its place - and any count handed over one smaller beside it.
                foreach (var (line, call) in calls)
                {
                    var given = call.Arguments[listAt].Trim();
                    if (!IsRest(given, restForm, restNames, options)) continue;

                    var countsBeside = new List<string>();
                    for (var at = 0; at < function.Parameters.Count; at++)
                    {
                        var parameter = Regex.Escape(function.Parameters[at]);
                        if (at != listAt && Regex.IsMatch(call.Arguments[at].Trim(), $@"^{parameter}\s*-\s*1$", options)) countsBeside.Add(function.Parameters[at]);
                    }

                    if (dialect.CountsBeside && countsBeside.Count == 0) continue;
                    if (TestsName(masked, function.Body, [list, .. countsBeside], options)) continue;

                    var emptyName = dialect.CountsBeside ? countsBeside[0] : list;
                    var told = $"Line {firstRead + 1} reads `{list}`'s first item, and line {line + 1} calls `{function.Name}` again with the rest of " +
                               $"`{list}`, but nothing in `{function.Name}` asks whether {(dialect.CountsBeside ? $"`{emptyName}`, how many items `{list}` has, is 0" : $"`{list}` is empty")}. " +
                               "Each call passes one item over, so a call comes with none left, and its first item is not there to be read.";
                    yield return new LogicFinding(id, firstRead + 1, told, StopWhenEmpty(id, source, masked, function, firstRead, list, emptyName, dialect))
                    {
                        Severity = Severity.Error,
                        Confidence = Confidence.Likely,
                    };
                    break;
                }
            }
        }
    }

    /// <summary>Every way of writing the rest of the list, as one pattern.</summary>
    private static Regex RestForm(string list, RegexOptions options) =>
        new(string.Join("|", RestForms.Select(form => $"(?:{form.Replace("{L}", Regex.Escape(list))})")), options);

    /// <summary>
    /// The names the function gives the rest of the list: first, *rest = coins; const [first, ...rest] = coins; my ($first,
    /// @rest) = @$coins; or a name given one of its ways of writing the rest - int[] rest = Arrays.copyOfRange(coins, 1, ...),
    /// std::vector&lt;int&gt; rest(coins.begin() + 1, coins.end()), local rest = { table.unpack(coins, 2) }.
    /// </summary>
    private static List<string> RestNames(IReadOnlyList<string> masked, List<int> body, string list, Regex restForm, RegexOptions options)
    {
        var name = Regex.Escape(list);
        var bare = Regex.Escape(list.TrimStart('$', '@'));
        var takenApart = new Regex(
            $@"^\s*[A-Za-z_]\w*\s*,\s*\*\s*(?<rest>[A-Za-z_]\w*)\s*=\s*{name}\s*$" +
            $@"|\[\s*[A-Za-z_$][\w$]*\s*,\s*\.\.\.\s*(?<rest>[A-Za-z_$][\w$]*)\s*\]\s*=\s*{name}\s*;?\s*$" +
            $@"|\bmy\s*\(\s*\$\w+\s*,\s*@(?<rest>\w+)\s*\)\s*=\s*@\{{?\s*\${bare}\s*\}}?\s*;", options);
        var rests = new List<string>();
        foreach (var line in body)
        {
            if (!masked[line].Contains(list.TrimStart('$', '@'), options.HasFlag(RegexOptions.IgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
            if (takenApart.Match(masked[line]) is { Success: true } apart) rests.Add(apart.Groups["rest"].Value);

            // A name given the rest: rest = <the rest>, or std::vector<int> rest(<the rest>, ...).
            if (Regex.Match(masked[line], @"(?<rest>[$@]?[A-Za-z_]\w*)\s*(?:=\s*|\()(?<value>.+)$") is { Success: true } given &&
                restForm.IsMatch(given.Groups["value"].Value))
            {
                rests.Add(given.Groups["rest"].Value);
            }
        }

        return rests;
    }

    /// <summary>The line reading the list's first item - or taking it apart into its first item and the rest - or null.</summary>
    private static int? FirstRead(IReadOnlyList<string> masked, List<int> body, string list, Dialect dialect, RegexOptions options, bool takenApart)
    {
        var name = Regex.Escape(list);
        var bare = Regex.Escape(list.TrimStart('$', '@'));
        foreach (var line in body)
        {
            var text = masked[line];
            if (FirstForms.Any(form => Regex.IsMatch(text, $@"(?<![\w$@.]){form.Replace("{L}", name)}", options))) return line;

            // Lua counts a table's items from 1.
            if (dialect.Language == "lua" && Regex.IsMatch(text, $@"(?<![\w.]){name}\s*\[\s*1\s*\]")) return line;
            if (dialect.Language == "perl" && Regex.IsMatch(text, $@"\$\${bare}\s*\[\s*0\s*\]|\${bare}\s*->\s*\[\s*0\s*\]|\bmy\s*\(\s*\$\w+\s*,\s*@\w+\s*\)\s*=\s*@\{{?\s*\${bare}")) return line;
            if (takenApart && Regex.IsMatch(text, $@"^\s*[A-Za-z_]\w*\s*,\s*\*\s*[A-Za-z_]\w*\s*=\s*{name}\s*$|\[\s*[A-Za-z_$][\w$]*\s*,\s*\.\.\.\s*[A-Za-z_$][\w$]*\s*\]\s*=\s*{name}\b", options)) return line;
        }

        return null;
    }

    /// <summary>Whether an argument is the rest of the list: one of its ways of writing it, or a name given it - \@rest in Perl.</summary>
    private static bool IsRest(string argument, Regex restForm, List<string> restNames, RegexOptions options)
    {
        if (restForm.Match(argument) is { Success: true, Index: 0 }) return true;
        var given = argument.TrimStart('\\', '&', '@', '$', '(').TrimEnd(')').Trim();
        return restNames.Any(rest => string.Equals(rest.TrimStart('$', '@'), given, options.HasFlag(RegexOptions.IgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    }

    /// <summary>Whether any test in the function - if, while, unless, a ? - names one of these.</summary>
    private static bool TestsName(IReadOnlyList<string> masked, List<int> body, IEnumerable<string> asked, RegexOptions options)
    {
        // A name with its sigils, however many: coins, $coins, @$coins.
        var named = new Regex($@"(?<![\w$@])[$@]*(?:{string.Join("|", asked.Select(each => Regex.Escape(each.TrimStart('$', '@'))))})(?!\w)", options);
        foreach (var line in body)
        {
            var text = masked[line];
            foreach (Match word in TestWord().Matches(text))
            {
                if (named.IsMatch(text[(word.Index + word.Length)..]) || named.IsMatch(text[..word.Index])) return true;
            }

            var question = text.IndexOf('?');
            if (question > 0 && named.IsMatch(text[..question])) return true;
        }

        return false;
    }

    /// <summary>A call the function makes of itself: its arguments, from name(a, b) - or, for a command, from Name a b.</summary>
    private sealed record SelfCall(List<string> Arguments);

    private static IEnumerable<SelfCall> SelfCalls(string text, string name, Dialect dialect)
    {
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        foreach (Match call in Regex.Matches(text, $@"(?<![\w$.@-])(?:(?:self|this)\s*\.\s*)?{Regex.Escape(name)}(?<open>\s*\(|\s+(?=[$(@\w-]))", options))
        {
            var start = call.Index + call.Length;
            if (call.Groups["open"].Value.Trim() == "(")
            {
                if (Brackets.ClosingParenthesis(text, start - 1) is not { } close) continue;
                yield return new SelfCall(TopLevel(text[start..close]));
            }
            else if (dialect.Commands)
            {
                yield return new SelfCall(CommandArguments(text, start));
            }
        }
    }

    /// <summary>A command's arguments, one after another: each a word or a bracketed piece, up to a ) it did not open, a |, a ; or the end.</summary>
    private static List<string> CommandArguments(string text, int from)
    {
        var arguments = new List<string>();
        var at = from;
        while (at < text.Length)
        {
            while (at < text.Length && text[at] == ' ') at++;
            if (at >= text.Length || text[at] is ')' or '|' or ';' or '}') break;
            var start = at;
            var depth = 0;
            while (at < text.Length && (depth > 0 || text[at] != ' ' && text[at] is not (')' or '|' or ';' or '}')))
            {
                if (text[at] is '(' or '[' or '{') depth++;
                else if (text[at] is ')' or ']' or '}') depth--;
                at++;
            }

            arguments.Add(text[start..at]);
        }

        return arguments;
    }

    /// <summary>The pieces of a list between its commas at the top level.</summary>
    private static List<string> TopLevel(string text)
    {
        var pieces = new List<string>();
        var depth = 0;
        var from = 0;
        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] is '(' or '[' or '{') depth++;
            else if (text[at] is ')' or ']' or '}') depth--;
            else if (text[at] == ',' && depth == 0)
            {
                pieces.Add(text[from..at]);
                from = at + 1;
            }
        }

        if (text[from..].Trim().Length > 0 || pieces.Count > 0) pieces.Add(text[from..]);
        return pieces;
    }

    /// <summary>
    /// The change: the list being empty added to the stop before the first item is read that gives nothing - return 0, ways =
    /// 0, return false - as QuixBugs' correct program has it: total &lt; 0 or not coins. Null when there is no such stop.
    /// </summary>
    private static LocalFix? StopWhenEmpty(string id, SourceFile source, IReadOnlyList<string> masked,
        (int Header, string Name, List<string> Parameters, List<int> Body) function, int firstRead, string list, string emptyName, Dialect dialect)
    {
        foreach (var line in function.Body.Where(each => each < firstRead).Reverse())
        {
            var text = masked[line];
            foreach (Match word in TestWord().Matches(text))
            {
                if (word.Value.ToLowerInvariant() is "while" or "until" or "unless" or "when") continue;
                if (Reaching.Written(text, word.Index + word.Length, dialect.ColonEnds) is not { } wording) continue;

                // What the stop gives: on its line, or alone on the line below - or, written after it as Ruby's return 0 if, before it.
                var givenBefore = text[..word.Index];
                var gives = wording.Rest.Length > 0 ? wording.Rest : NextLine(masked, line);
                var nothing = Nothing().IsMatch(wording.Rest.Length > 0 ? wording.Rest.TrimEnd('}', ' ') : gives) ||
                              Regex.IsMatch(givenBefore, @"^\s*return\s+(?:0|false|nil|None|null)\s*$");
                if (!nothing) continue;

                var conditionStart = text.IndexOf(wording.Condition, wording.Start, StringComparison.Ordinal);
                if (conditionStart < 0) return null;
                var conditionEnd = conditionStart + wording.Condition.Length;
                var original = source.Lines[line];
                if (conditionEnd > original.Length) return null;

                var arrayDeclared = dialect.EmptyArray is not null && Regex.IsMatch(masked[function.Header], $@"\[\s*\]\s*{Regex.Escape(list)}\b|\b{Regex.Escape(list)}\s*\[\s*\]");
                var empty = (arrayDeclared ? dialect.EmptyArray! : dialect.Empty).Replace("{L}", emptyName);
                var asked = $"{wording.Condition}{dialect.Or}{empty}";
                var changed = original[..conditionStart] + asked + original[conditionEnd..];
                return LocalFix.ReplaceLine(id, $"Stop when no items are left: {asked}",
                    $"With `{list}` empty there is no first item to take: the function stops there, giving what it gives when there is no way left.",
                    source.Path, line + 1, changed);
            }
        }

        return null;
    }

    /// <summary>The next line with anything on it, trimmed - the one a test's block starts with.</summary>
    private static string NextLine(IReadOnlyList<string> masked, int line)
    {
        for (var below = line + 1; below < masked.Count; below++)
        {
            if (masked[below].Trim().Length > 0 && masked[below].Trim() != "{") return masked[below].Trim();
        }

        return "";
    }
}
