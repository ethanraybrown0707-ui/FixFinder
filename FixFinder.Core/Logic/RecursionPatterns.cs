using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Recursion whose stopping case is there but is never reached, and recursion that stops with the wrong value - in a
/// function of one parameter, n, that tests n against a number once and calls itself with n changed by a number:
/// <list type="bullet">
/// <item>the call moves n away from where the test stops it - if n &lt;= 0 with f(n + 1) - or does not move it - f(n) - so
/// from any other n each call makes another, until the language runs out of room for them;</item>
/// <item>the call takes 2 or more off n each time, and the test is n == 0: an odd n goes past 0 and never equals it;</item>
/// <item>the function gives back n times what the next call gives back, and the stopping case gives back 0 - so every
/// answer has 0 in its product, and is 0.</item>
/// </list>
/// A function with no way to stop at all is a different mistake, found by analysis-recursion-never-ends.
/// <para>
/// And, as a check of its own, a tree's or a list's insert that throws away what it gives back: insert gives back a new node
/// when the place it is given is empty - if node is None: return Node(value) - and calls itself on a field of that place as a
/// statement of its own, insert(node.left, value). When node.left is empty, the new node is made and lost, and nothing is
/// added. The change keeps it: node.left = insert(node.left, value).
/// </para>
/// <para>
/// And a memo that is asked and never given anything: fib asks memo for n - if n in memo: return memo[n] - but nothing in the
/// file ever puts an answer into memo, so it is always empty and every call works its answer out again, as if there were no
/// memo: fib(60) does not finish. The change keeps the answer where fib works it out - memo[n] = fib(n - 1) + fib(n - 2),
/// then return memo[n].
/// </para>
/// </summary>
/// <remarks>
/// Only what the code shows is said: one parameter, one test of it against a number in the function, and every call the
/// function makes to itself giving it n, or n plus or minus a number. A call written any other way - f(n // 2), f(n - k) -
/// leaves the function alone.
/// </remarks>
public static partial class RecursionPatterns
{
    /// <summary>How a function's body is found: by indentation, between braces, or up to the end that closes it.</summary>
    private enum Body { Indented, Braces, Ends, BracesOrIndented }

    /// <summary>How one language writes a function of one parameter, and how its comparisons are written.</summary>
    /// <param name="Parameter">The parameter's name in what the brackets hold: the last word (int n), the first (n: u32), or the $ name.</param>
    /// <param name="Copied">Perl's: the line that copies the parameter out of @_, my ($n) = @_; - where the brackets hold none.</param>
    /// <param name="Words">Whether comparisons are written as words - -eq, -le - as PowerShell's are.</param>
    /// <param name="FieldOf">A field of the parameter {p} - node.left, root-&gt;left, $node-&gt;{left} - or null where the new-node check is not made.</param>
    /// <param name="Memo">How the language asks a memo and puts into one, or null where the memo check is not made.</param>
    private sealed record Dialect(string Id, IReadOnlySet<string> Files, Syntax Syntax, Regex Header, Body Body, string Parameter,
        Regex? Copied = null, bool Words = false, string? FieldOf = Dotted, Memo? Memo = null);

    /// <summary>
    /// How one language asks a memo whether it has a key - with the memo as "map" and the key as "key", and "set" when it is a
    /// Map asked with has - what puts a value into the memo named {m}, and the lines that keep an answer and give it back.
    /// </summary>
    /// <param name="Kept">The lines of the change, from the indentation, the memo, the key, the answer, the ; the line ended
    /// with, and whether the line said return - Ruby's and Scala's last line gives back its value without one.</param>
    private sealed record Memo(Regex Asked, string Put, Func<string, string, string, string, string, bool, Match, IReadOnlyList<string>> Kept);

    private static IReadOnlyList<string> KeptByIndex(string indent, string map, string key, string answer, string end, bool said) =>
        [$"{indent}{map}[{key}] = {answer}{end}", $"{indent}return {map}[{key}]{end}"];

    private const string Dotted = @"{p}\.[A-Za-z_]\w*";

    private const string LastWord = @"(?<name>[A-Za-z_]\w*)\s*(?:=.*)?$";
    private const string FirstWord = @"^\s*(?:mut\s+)?(?<name>[A-Za-z_]\w*)";
    private const string DollarName = @"(?<name>\$[A-Za-z_]\w*)";

    private static readonly Dialect[] Dialects =
    [
        new("logic-python-recursion-never-stops", CodePattern.Files(".py", ".pyw"), Syntax.Python,
            new(@"^(?<indent>\s*)def\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), Body.Indented, FirstWord,
            Memo: new(new(@"\bif\s+(?<key>[A-Za-z_]\w*)\s+in\s+(?<map>[A-Za-z_]\w*)\s*:"), @"\b{m}\s*\[[^\]]+\]\s*=(?!=)|\b{m}\s*\|=|\b{m}\.(?:update|setdefault|add|append|extend|insert)\(",
                (indent, map, key, answer, end, said, asked) => KeptByIndex(indent, map, key, answer, end, said))),
        new("logic-ruby-recursion-never-stops", CodePattern.Files(".rb"), Syntax.Python,
            new(@"^(?<indent>\s*)def\s+(?:self\.)?(?<function>[a-z_]\w*[?!]?)\s*\((?<parameters>[^)]*)\)"), Body.Ends, FirstWord,
            Memo: new(new(@"\b(?<map>[a-z_]\w*)\.(?:key\?|has_key\?|include\?)\(\s*(?<key>[a-z_]\w*)\s*\)"), @"\b{m}\s*\[[^\]]+\]\s*(?:\|\|)?=(?![=~>])|\b{m}\.store\(",
                (indent, map, key, answer, end, said, asked) => [$"{indent}{(said ? "return " : "")}{map}[{key}] = {answer}"])),
        new("logic-lua-recursion-never-stops", CodePattern.Files(".lua"), Syntax.Lua,
            new(@"^(?<indent>\s*)(?:local\s+)?function\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), Body.Ends, FirstWord,
            Memo: new(new(@"\bif\s+(?<map>[A-Za-z_]\w*)\[\s*(?<key>[A-Za-z_]\w*)\s*\]\s*(?:~=\s*nil\s*)?then\b"), @"\b{m}\s*\[[^\]]+\]\s*=(?!=)",
                (indent, map, key, answer, end, said, asked) => KeptByIndex(indent, map, key, answer, end, said))),
        new("logic-go-recursion-never-stops", CodePattern.Files(".go"), Syntax.CLike,
            new(@"^(?<indent>\s*)func\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), Body.Braces, FirstWord,
            Memo: new(new(@",\s*(?:ok|found|exists|has)\s*:?=\s*(?<map>[A-Za-z_]\w*)\[\s*(?<key>[A-Za-z_]\w*)\s*\]"), @"\b{m}\s*\[[^\]]+\]\s*=(?!=)",
                (indent, map, key, answer, end, said, asked) => KeptByIndex(indent, map, key, answer, end, said))),
        new("logic-rust-recursion-never-stops", CodePattern.Files(".rs"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:pub\s+)?fn\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), Body.Braces, FirstWord, FieldOf: null),
        new("logic-js-recursion-never-stops", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:export\s+)?function\s+(?<function>[A-Za-z_$][\w$]*)\s*\((?<parameters>[^)]*)\)"), Body.Braces, FirstWord,
            Memo: new(new(@"\b(?<map>[A-Za-z_$][\w$]*)\.has\(\s*(?<key>[A-Za-z_$][\w$]*)\s*\)(?<set>)|\b(?<key>[A-Za-z_$][\w$]*)\s+in\s+(?<map>[A-Za-z_$][\w$]*)\b"),
                @"\b{m}\s*\[[^\]]+\]\s*=(?!=)|\b{m}\.set\(",
                (indent, map, key, answer, end, said, asked) => asked.Groups["set"].Success
                    ? [$"{indent}{map}.set({key}, {answer});", $"{indent}return {map}.get({key});"]
                    : KeptByIndex(indent, map, key, answer, end, said))),
        new("logic-java-recursion-never-stops", CodePattern.Files(".java"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:(?:public|private|protected|static|final)\s+)*[\w<>\[\]]+\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*\{?\s*$"), Body.Braces, LastWord,
            Memo: new(new(@"\b(?<map>[A-Za-z_]\w*)\.containsKey\(\s*(?<key>[A-Za-z_]\w*)\s*\)"), @"\b{m}\.(?:put|putIfAbsent|computeIfAbsent|compute|merge|putAll)\(",
                (indent, map, key, answer, end, said, asked) => [$"{indent}{map}.put({key}, {answer});", $"{indent}return {map}.get({key});"])),
        new("logic-csharp-recursion-never-stops", CodePattern.Files(".cs"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:(?:public|private|protected|internal|static)\s+)*[\w<>\[\]?]+\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*\{?\s*$"), Body.Braces, LastWord,
            Memo: new(new(@"\b(?<map>[A-Za-z_]\w*)\.(?:ContainsKey|TryGetValue)\(\s*(?<key>[A-Za-z_]\w*)\s*[,)]"), @"\b{m}\s*\[[^\]]+\]\s*=(?!=)|\b{m}\.(?:Add|TryAdd)\(",
                (indent, map, key, answer, end, said, asked) => KeptByIndex(indent, map, key, answer, end, said))),
        new("logic-c-recursion-never-stops", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:(?:static|inline|unsigned|long|const)\s+)*(?:(?:struct|enum|union)\s+)?[A-Za-z_]\w*[\s*&]+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*\{?\s*$"), Body.Braces, LastWord, FieldOf: @"{p}\s*(?:->|\.)\s*[A-Za-z_]\w*",
            Memo: new(new(@"\b(?<map>[A-Za-z_]\w*)\.(?:count|contains|find)\(\s*(?<key>[A-Za-z_]\w*)\s*\)"), @"\b{m}\s*\[[^\]]+\]\s*=(?!=)|\b{m}\.(?:insert|emplace|try_emplace|insert_or_assign)\(",
                (indent, map, key, answer, end, said, asked) => KeptByIndex(indent, map, key, answer, end, said))),
        new("logic-dart-recursion-never-stops", CodePattern.Files(".dart"), Syntax.CLike,
            new(@"^(?<indent>\s*)[\w<>?]+\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*\{?\s*$"), Body.Braces, LastWord,
            Memo: new(new(@"\b(?<map>[A-Za-z_]\w*)\.containsKey\(\s*(?<key>[A-Za-z_]\w*)\s*\)"), @"\b{m}\s*\[[^\]]+\]\s*=(?!=)|\b{m}\.(?:putIfAbsent|update|addAll)\(",
                (indent, map, key, answer, end, said, asked) => [$"{indent}{map}[{key}] = {answer};", $"{indent}return {map}[{key}]!;"])),
        new("logic-php-recursion-never-stops", CodePattern.Files(".php"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:(?:public|private|protected|static)\s+)*function\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), Body.Braces, DollarName,
            FieldOf: @"{p}->[A-Za-z_]\w*"),
        new("logic-perl-recursion-never-stops", CodePattern.Files(".pl", ".pm"), Syntax.Perl,
            new(@"^(?<indent>\s*)sub\s+(?<function>[A-Za-z_]\w*)\s*(?<parameters>)\{?\s*$"), Body.Braces, DollarName,
            Copied: new(@"^\s*my\s*(?:\(\s*(?<name>\$[A-Za-z_]\w*)\s*\)\s*=\s*@_|(?<name>\$[A-Za-z_]\w*)\s*=\s*shift\b)"), FieldOf: @"{p}->\{\s*\w+\s*\}",
            Memo: new(new(@"\bexists\s+\$(?<map>[A-Za-z_]\w*)\{\s*(?<key>\$[A-Za-z_]\w*)\s*\}"), @"\${m}\{[^}]+\}\s*(?:\|\||//)?=(?![=~>])",
                (indent, map, key, answer, end, said, asked) => [$"{indent}${map}{{{key}}} = {answer};", $"{indent}return ${map}{{{key}}};"])),
        new("logic-powershell-recursion-never-stops", CodePattern.Files(".ps1"), Syntax.Python,
            new(@"^(?<indent>\s*)function\s+(?<function>[A-Za-z_][\w-]*)\s*\((?<parameters>[^)]*)\)", RegexOptions.IgnoreCase), Body.Braces, DollarName, Words: true, FieldOf: null,
            Memo: new(new(@"\$(?<map>[A-Za-z_]\w*)\.(?:ContainsKey|Contains)\(\s*(?<key>\$[A-Za-z_]\w*)\s*\)", RegexOptions.IgnoreCase), @"\${m}\s*\[[^\]]+\]\s*=(?!=)|\${m}\.Add\(",
                (indent, map, key, answer, end, said, asked) => [$"{indent}${map}[{key}] = {answer}", $"{indent}return ${map}[{key}]"])),
        new("logic-scala-recursion-never-stops", CodePattern.Files(".scala", ".sc"), Syntax.Scala,
            new(@"^(?<indent>\s*)def\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), Body.BracesOrIndented, FirstWord,
            Memo: new(new(@"\b(?<map>[A-Za-z_]\w*)\.contains\(\s*(?<key>[A-Za-z_]\w*)\s*\)"), @"\b{m}\s*\([^()]+\)\s*=(?!=)|\b{m}\s*\+=|\b{m}\.(?:put|update|getOrElseUpdate|addOne)\(",
                (indent, map, key, answer, end, said, asked) => [$"{indent}{map}({key}) = {answer}", $"{indent}{(said ? "return " : "")}{map}({key})"])),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern(dialect.Id, dialect.Files, dialect.Syntax,
            (id, source, masked) => Recursions(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
        .. Dialects.Where(dialect => dialect.FieldOf is not null).Select(dialect => new CodePattern(
            dialect.Id.Replace("recursion-never-stops", "new-node-lost", StringComparison.Ordinal), dialect.Files, dialect.Syntax,
            (id, source, masked) => LostNodes(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
        .. Dialects.Where(dialect => dialect.Memo is not null).Select(dialect => new CodePattern(
            dialect.Id.Replace("recursion-never-stops", "memo-never-kept", StringComparison.Ordinal), dialect.Files, dialect.Syntax,
            (id, source, masked) => EmptyMemos(id, source, masked, dialect), Severity.Warning, Confidence.Likely)),
    ];

    /// <summary>
    /// Each recursive function that asks a memo for an answer the file never puts into it - said once for the function, with
    /// the change that keeps the answer on the last line that works it out by calling the function.
    /// </summary>
    private static IEnumerable<LogicFinding> EmptyMemos(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var memo = dialect.Memo!;
        for (var header = 0; header < masked.Count; header++)
        {
            if (dialect.Header.Match(masked[header]) is not { Success: true } function) continue;
            if (Lines(masked, header, dialect.Body, function.Groups["indent"].Value) is not { Count: > 0 } body) continue;

            var name = function.Groups["function"].Value;
            var called = new Regex($@"(?<![\w.$:-]){Regex.Escape(name)}\s*\(");
            var working = body.LastOrDefault(line => called.IsMatch(masked[line]), -1);
            if (working < 0) continue;

            if (body.Select(line => (Line: line, Asked: memo.Asked.Match(masked[line]))).FirstOrDefault(each => each.Asked.Success) is not { Asked.Success: true } ask) continue;

            // Asked, and given nothing anywhere in the file: no line puts a value into it.
            var map = ask.Asked.Groups["map"].Value;
            var put = new Regex(memo.Put.Replace("{m}", Regex.Escape(map), StringComparison.Ordinal));
            if (masked.Any(line => put.IsMatch(line))) continue;

            if (!OnlyAMemo(masked, header, body, map, memo, called)) continue;

            var key = source.Lines[ask.Line].Substring(ask.Asked.Groups["key"].Index, ask.Asked.Groups["key"].Length);
            var said = $"Line {ask.Line + 1} asks `{map}` for an answer for `{key}`, but no line of the file ever puts one into `{map}`: it stays empty, " +
                       $"so every call to {name} works its answer out again, as many times as it would with no memo at all.";

            // The answer on the last line that calls the function: return fib(n - 1) + fib(n - 2), or Ruby's and Scala's last line.
            var line = source.Lines[working];
            var returned = Regex.Match(masked[working], @"^(?<indent>\s*)(?<said>return\s+)?(?<answer>.*?\S)\s*(?<end>;?)\s*$");
            var tail = !returned.Groups["said"].Success && dialect.Id is "logic-ruby-recursion-never-stops" or "logic-scala-recursion-never-stops" &&
                       working == body.Where(each => masked[each].Trim() is not ("" or "}" or "end")).Max();
            if (!returned.Success || !returned.Groups["said"].Success && !tail)
            {
                yield return new LogicFinding(id, ask.Line + 1, said, null) { Severity = Severity.Warning, Confidence = Confidence.Likely };
                continue;
            }

            var answer = line.Substring(returned.Groups["answer"].Index, returned.Groups["answer"].Length);
            var lines = memo.Kept(returned.Groups["indent"].Value, map, key, answer, returned.Groups["end"].Value, returned.Groups["said"].Success, ask.Asked);

            // TypeScript's Map.get may give undefined for all its types say, so a function giving back a number says it does not: !.
            if (Path.GetExtension(source.Path) is ".ts" or ".mts" or ".cts")
                lines = [.. lines.Select(kept => Regex.Replace(kept, @"(\breturn\s+[^;]*\.get\([^()]*\))(;?)\s*$", "$1!$2"))];
            yield return new LogicFinding(id, ask.Line + 1, said,
                new LocalFix
                {
                    RuleId = id,
                    Title = $"Keep each answer in {map}: {map} gets what line {working + 1} works out",
                    Explanation = $"Each answer is then in {map} the next time it is asked for, so it is worked out once.",
                    File = source.Path,
                    StartLine = working + 1,
                    RemoveCount = 1,
                    NewLines = lines,
                })
            {
                Severity = Severity.Warning,
                Confidence = Confidence.Likely,
            };
        }
    }

    /// <summary>Perl's first parameter, copied out of @_ by the sub's first line: my ($node, $value) = @_; or my $node = shift;.</summary>
    [GeneratedRegex(@"^\s*my\s*(?:\(\s*(?<name>\$[A-Za-z_]\w*)\s*[,)].*=\s*@_|(?<name>\$[A-Za-z_]\w*)\s*=\s*shift\b)")]
    private static partial Regex FirstCopied();

    /// <summary>
    /// What a return gives back, with an if or an unless written after it, or Lua's end, taken off - and nothing for Ruby's
    /// return if node.nil?, which gives back nothing.
    /// </summary>
    [GeneratedRegex(@"\breturn\s+(?!(?:if|unless)\b)(?<value>.*?\S)\s*;?\s*(?:\s(?:if|unless)\s.*|\s+end)?\s*$")]
    private static partial Regex Returned();

    /// <summary>
    /// Each statement that calls a function giving back something new for an empty first parameter on a field of that
    /// parameter, and keeps nothing it gives back.
    /// </summary>
    private static IEnumerable<LogicFinding> LostNodes(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        for (var header = 0; header < masked.Count; header++)
        {
            if (dialect.Header.Match(masked[header]) is not { Success: true } function) continue;
            if (Lines(masked, header, dialect.Body, function.Groups["indent"].Value) is not { Count: > 0 } body) continue;
            if (FirstParameter(dialect, function.Groups["parameters"].Value, masked, body) is not { } first) continue;

            var name = function.Groups["function"].Value;
            var place = Regex.Escape(first);

            // The test that the first parameter is empty, and the new value given back there - the first return on its line or
            // the eight after it, as C's comes after the lines that make the node.
            var empty = new Regex($@"(?<![\w$]){place}\s+is\s+None\b|\bnot\s+{place}(?![\w$])|(?<![\w$]){place}\s*={{2,3}}\s*(?:None|null|NULL|nullptr|nil|undefined)\b|" +
                                  $@"(?<![\w$]){place}\s+is\s+null\b|!\s*{place}(?![\w$])|(?<![\w$]){place}\.nil\?|is_null\(\s*{place}\s*\)|!\s*defined\s*\(?\s*{place}(?![\w$])|" +
                                  $@"\bunless\s*\(?\s*(?:defined\s*\(?\s*)?{place}(?![\w$])");
            if (body.FirstOrDefault(line => empty.IsMatch(masked[line]), -1) is not (>= 0 and var testLine)) continue;

            var given = body.Where(line => line >= testLine && line <= testLine + 8)
                .Select(line => (Line: line, Return: Returned().Match(source.Lines[line])))
                .FirstOrDefault(each => each.Return.Success);
            if (given.Return is not { Success: true } returned) continue;

            var value = returned.Groups["value"].Value.Trim();
            if (value == first || value is "None" or "null" or "NULL" or "nullptr" or "nil" or "undefined" or "0" or "false") continue;

            var call = new Regex($@"^(?<indent>\s*)(?<call>(?:(?:self|this)\s*(?:\.|->)\s*)?{Regex.Escape(name)}\s*\(\s*(?<field>{dialect.FieldOf!.Replace("{p}", place, StringComparison.Ordinal)})\s*(?:,[^;]*)?\))\s*(?<end>;?)\s*$");
            var lost = body.Select(line => (Line: line, Call: call.Match(masked[line]))).Where(each => each.Call.Success).ToList();
            if (lost.Count == 0) continue;

            // One change for them all, from the first lost call's line to the last's: the tree is right only when each is kept.
            var changed = Enumerable.Range(lost[0].Line, lost[^1].Line - lost[0].Line + 1).Select(line => source.Lines[line]).ToList();
            var fields = new List<string>();
            foreach (var (line, match) in lost)
            {
                var written = source.Lines[line].Substring(match.Groups["call"].Index, match.Groups["call"].Length);
                var field = source.Lines[line].Substring(match.Groups["field"].Index, match.Groups["field"].Length);
                fields.Add(field);
                changed[line - lost[0].Line] = $"{match.Groups["indent"].Value}{field} = {written}{match.Groups["end"].Value}";
            }

            var places = string.Join(" and ", fields.Select(field => $"`{field}`"));
            yield return new LogicFinding(id, lost[0].Line + 1,
                $"{(lost.Count == 1 ? "Line" : "Lines")} {string.Join(" and ", lost.Select(each => (each.Line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)))} " +
                $"{(lost.Count == 1 ? "calls" : "call")} {name} on {places} and {(lost.Count == 1 ? "keeps" : "keep")} nothing it gives back. {name} gives back " +
                $"`{value}` when it is given an empty {first} - line {given.Line + 1} - so when {places} {(lost.Count == 1 ? "is" : "are")} empty, the new one is " +
                "made and lost, and nothing is added there.",
                new LocalFix
                {
                    RuleId = id,
                    Title = $"Keep what {name} gives back: {string.Join(", ", fields.Select(field => $"{field} = {name}(...)"))}",
                    Explanation = $"Each field then holds what {name} gives back - the new one, when it was empty - so what was added stays in it.",
                    File = source.Path,
                    StartLine = lost[0].Line + 1,
                    RemoveCount = changed.Count,
                    NewLines = changed,
                })
            {
                Severity = Severity.Error,
                Confidence = Confidence.Likely,
            };
        }
    }

    /// <summary>
    /// Whether the file shows the map is a memo of this function's alone: its answers are read back - memo[n], memo.get(n),
    /// $memo{$n} - as a set of places visited never is; outside the function nothing touches it but lines making it empty, and
    /// one does unless it is the function's own parameter; and inside, each line naming it asks it, reads it, or passes it to
    /// the function itself. A map named anywhere else may be filled there, and is left alone.
    /// </summary>
    /// <remarks>
    /// The asking is no reading back, where it is written as a read - Lua's if visited[node] then, Perl's exists $seen{$n} - but
    /// an ask handing over the answer is: C#'s TryGetValue(n, out var answer), and Go's answer, ok := memo[n].
    /// </remarks>
    private static bool OnlyAMemo(IReadOnlyList<string> masked, int header, List<int> body, string map, Memo memo, Regex called)
    {
        var name = Regex.Escape(map);
        var read = new Regex($@"(?<![\w$])[$%]?{name}\s*(?:\[|\.(?:get|at|TryGetValue|fetch|getOrElse)\(|\{{|\()");
        if (!body.Any(line => ReadsAnAnswer(masked[line], read, memo.Asked, map))) return false;

        var named = new Regex($@"(?<![\w$])[$%]?{name}(?![\w$])");
        var madeEmpty = new Regex($@"^\s*(?:(?:static|final|private|public|readonly|const|let|var|val|local|my|our)\s+)*(?:[\w.:<>\[\], ?]+\s+)?[$%]?{name}\s*(?::[^=]*)?" +
                                  $@"(?:=\s*(?:\{{\s*\}}|dict\(\s*\)|new\s*[\w.<>\[\], ]*\(\s*\)|map\[[^\]]+\][\w\[\]*.]*\{{\s*\}}|mutable\.\w+(?:\[[^\]]*\])?\(\s*\)|@\{{\s*\}}|<[^>]*>\s*\{{\s*\}}|\(\s*\)))?\s*;?\s*$");

        var inside = new HashSet<int>(body) { header };
        var outside = Enumerable.Range(0, masked.Count).Where(line => !inside.Contains(line) && named.IsMatch(masked[line])).ToList();
        if (outside.Any(line => !madeEmpty.IsMatch(masked[line]))) return false;
        if (outside.Count == 0 && !named.IsMatch(masked[header])) return false;

        return body.Where(line => named.IsMatch(masked[line]))
            .All(line => memo.Asked.IsMatch(masked[line]) || read.IsMatch(masked[line]) || called.IsMatch(masked[line]));
    }

    /// <summary>Whether the line reads an answer out of the map: a read outside its asking, or an ask that hands the answer over.</summary>
    private static bool ReadsAnAnswer(string text, Regex read, Regex asked, string map)
    {
        var asks = asked.Matches(text).Where(ask => ask.Groups["map"].Value == map).ToList();
        if (asks.Any(ask => ask.Value.Contains("TryGetValue", StringComparison.Ordinal) ||
                            ask.Value.StartsWith(',') && Regex.IsMatch(text[..ask.Index], @"(?<![\w$])(?!_\b)[A-Za-z_]\w*\s*$")))
            return true;

        return read.Matches(text).Any(each => !asks.Any(ask => each.Index >= ask.Index && each.Index < ask.Index + ask.Length));
    }

    /// <summary>The function's first parameter's name - after self or this - or Perl's first copied out of @_; null for none.</summary>
    private static string? FirstParameter(Dialect dialect, string parameters, IReadOnlyList<string> masked, List<int> body)
    {
        if (dialect.Copied is not null)
            return body.Count > 0 && FirstCopied().Match(masked[body[0]]) is { Success: true } line ? line.Groups["name"].Value : null;

        var pieces = parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SkipWhile(piece => piece is "self" or "this" or "&self" or "&mut self").ToList();
        if (pieces.Count == 0) return null;

        var name = Regex.Match(pieces[0], dialect.Parameter);
        return name.Success && name.Groups["name"].Value is not ("self" or "mut" or "const" or "final") ? name.Groups["name"].Value : null;
    }

    /// <summary>A line starting with a word that opens a block closed by end, in Ruby and Lua - an if after a statement opens none.</summary>
    [GeneratedRegex(@"^\s*(?:[a-z_]\w*\s*=\s*|local\s+)?(?<word>if|unless|while|until|case|begin|def|class|module|for|function|repeat)\b")]
    private static partial Regex OpeningWord();

    /// <summary>A do, or a function written inside a line - each opens a block of its own.</summary>
    [GeneratedRegex(@"\bdo\b|\bfunction\s*\(")]
    private static partial Regex OpensInside();

    [GeneratedRegex(@"\b(?:end|until)\b")]
    private static partial Regex Closes();

    private static readonly Dictionary<string, string> WordComparisons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["-eq"] = "==", ["-le"] = "<=", ["-lt"] = "<", ["-ge"] = ">=", ["-gt"] = ">",
    };

    private static IEnumerable<LogicFinding> Recursions(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        for (var header = 0; header < masked.Count; header++)
        {
            if (dialect.Header.Match(masked[header]) is not { Success: true } function) continue;
            if (Lines(masked, header, dialect.Body, function.Groups["indent"].Value) is not { Count: > 0 } body) continue;
            if (Parameter(dialect, function.Groups["parameters"].Value, masked, body) is not { } parameter) continue;
            if (Read(id, source, masked, dialect, function.Groups["function"].Value, parameter, body) is { } finding) yield return finding;
        }
    }

    /// <summary>The function's lines after its header - or, for one written on a single line, its header's line.</summary>
    private static List<int>? Lines(IReadOnlyList<string> masked, int header, Body body, string indent)
    {
        switch (body)
        {
            case Body.Indented:
                return Indented(masked, header, indent);
            case Body.Braces:
            case Body.BracesOrIndented when masked[header].Contains('{') || BraceBlocks.NextCode(masked, header + 1) is var next && next >= 0 && masked[next].TrimStart().StartsWith('{'):
                if (BraceBlocks.Body(masked, header) is { } lines) return [.. Enumerable.Range(lines.First, lines.End - lines.First)];
                return masked[header].Contains('{') && masked[header].Contains('}') ? [header] : null;
            case Body.BracesOrIndented:
                return Indented(masked, header, indent);
            default:
                var depth = 0;
                var inside = new List<int>();
                for (var line = header; line < masked.Count; line++)
                {
                    // A while's, an until's or a for's do is the one block its word opens.
                    var opening = OpeningWord().Match(masked[line]);
                    var opensInside = opening.Success && opening.Groups["word"].Value is "while" or "until" or "for"
                        ? 0
                        : OpensInside().Matches(masked[line]).Count;
                    depth += (opening.Success ? 1 : 0) + opensInside - Closes().Matches(masked[line]).Count;
                    if (line > header) inside.Add(line);
                    if (depth <= 0) return line == header ? [header] : inside.Take(inside.Count - 1).ToList();
                }

                return null;
        }
    }

    private static List<int> Indented(IReadOnlyList<string> masked, int header, string indent)
    {
        var lines = new List<int>();
        for (var line = header + 1; line < masked.Count; line++)
        {
            if (masked[line].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[line]).Length <= indent.Length) break;
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>The function's one parameter's name, or null for a function of none or of more than one.</summary>
    private static string? Parameter(Dialect dialect, string parameters, IReadOnlyList<string> masked, List<int> body)
    {
        if (dialect.Copied is { } copied)
            return body.Count > 0 && copied.Match(masked[body[0]]) is { Success: true } line ? line.Groups["name"].Value : null;

        var pieces = parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pieces is not [var only] || only is "self" or "this") return null;

        var name = Regex.Match(only.Replace("[int]", "", StringComparison.OrdinalIgnoreCase).Trim(), dialect.Parameter);
        return name.Success && name.Groups["name"].Value is not ("self" or "mut" or "const" or "final") ? name.Groups["name"].Value : null;
    }

    /// <summary>One self-call: the line it is on, where its argument starts and ends there, and how it changes n: +2, -1, 0.</summary>
    private sealed record SelfCall(int Line, int Start, int Length, int Step, string Written);

    private static LogicFinding? Read(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect, string function, string parameter, List<int> body)
    {
        var name = Regex.Escape(parameter);
        var called = new Regex($@"(?<![\w.$:-]){Regex.Escape(function)}\s*\(");

        // Every call the function makes to itself, each giving it n or n plus or minus a number - or none of this is said.
        var calls = new List<SelfCall>();
        foreach (var line in body)
        {
            foreach (Match call in called.Matches(masked[line]))
            {
                var open = call.Index + call.Length - 1;
                if (Brackets.ClosingParenthesis(masked[line], open) is not { } close) return null;

                var argument = masked[line][(open + 1)..close];
                if (Regex.Match(argument, $@"^\s*{name}\s*(?:(?<sign>[-+])\s*(?<step>\d+))?\s*$") is not { Success: true } given) return null;

                var step = given.Groups["step"].Success ? int.Parse(given.Groups["step"].Value) * (given.Groups["sign"].Value == "-" ? -1 : 1) : 0;
                calls.Add(new SelfCall(line, open + 1, close - open - 1, step, source.Lines[line].Substring(open + 1, close - open - 1).Trim()));
            }
        }

        if (calls.Count == 0) return null;

        // The one test of n against a number: n <= 0, 0 == n, $n -le 1.
        var comparison = dialect.Words ? @"-(?:eq|le|lt|ge|gt)\b" : @"===|==|<=|>=|<(?!<)|>(?!>)";
        var tests = body.SelectMany(line => Regex.Matches(masked[line], $@"(?<![\w$]){name}\s*(?<op>{comparison})\s*(?<limit>-?\d+)\b|\b(?<limit>-?\d+)\s*(?<op>{comparison})\s*{name}(?![\w$])")
            .Select(test => (Line: line, Test: test))).ToList();
        if (tests is not [var only]) return null;

        var op = dialect.Words ? WordComparisons[only.Test.Groups["op"].Value] : only.Test.Groups["op"].Value.Replace("===", "==", StringComparison.Ordinal);
        if (only.Test.Value.TrimStart().StartsWith(only.Test.Groups["limit"].Value, StringComparison.Ordinal))
            op = op switch { "<=" => ">=", "<" => ">", ">=" => "<=", ">" => "<", _ => op };

        var limit = int.Parse(only.Test.Groups["limit"].Value);
        var testLine = only.Line;
        var test = source.Lines[testLine].Substring(only.Test.Index, only.Test.Length);
        var stopsLow = op is "<=" or "<";
        var stopsHigh = op is ">=" or ">";

        // The call moves n away from the stop, or keeps it where it is.
        if (calls.All(call => call.Step == 0) && (stopsLow || stopsHigh))
        {
            var callLine = calls[0].Line;
            var towards = stopsLow ? $"{parameter} - 1" : $"{parameter} + 1";
            return Finding(id, source, callLine,
                $"Line {callLine + 1} calls {function} with {parameter} as it is, and line {testLine + 1} stops only when `{test}`: from any other " +
                $"{parameter} each call makes the same call again, until the language runs out of room for them.",
                calls.All(call => call.Line == callLine)
                    ? Changed(id, source, callLine, calls, towards, $"Call {function} with {towards}", $"Each call then moves {parameter} a step towards {limit}, where line {testLine + 1} stops it.")
                    : null);
        }

        if (calls.All(call => stopsLow ? call.Step > 0 : stopsHigh && call.Step < 0))
        {
            var callLine = calls[0].Line;
            var away = stopsLow ? "up" : "down";
            return Finding(id, source, callLine,
                $"Line {callLine + 1} calls {function} with `{calls[0].Written}`, which moves {parameter} {away}, away from where line {testLine + 1} " +
                $"stops it, `{test}`: from any {parameter} not already stopped there each call makes another, until the language runs out of room for them.",
                calls.All(call => call.Line == callLine)
                    ? Changed(id, source, callLine, calls, null, $"Move {parameter} towards {limit}: {(stopsLow ? "-" : "+")} in each call",
                        $"Each call then moves {parameter} towards {limit}, where line {testLine + 1} stops it.")
                    : null);
        }

        // n == 0 with 2 or more taken off each time: an odd n passes 0.
        if (op == "==" && calls.All(call => call.Step <= -2))
        {
            var instead = dialect.Words ? "-le" : "<=";
            var written = only.Test.Groups["op"];
            var fixedLine = source.Lines[testLine][..written.Index] + instead + source.Lines[testLine][(written.Index + written.Length)..];
            return Finding(id, source, testLine,
                $"Line {testLine + 1} stops only when `{test}`, and line {calls[0].Line + 1} takes {-calls[0].Step} off {parameter} each call: a {parameter} " +
                $"that is not {limit} plus a number of steps of {-calls[0].Step} goes past {limit}, never equals it, and the calls never stop.",
                LocalFix.ReplaceLine(id, $"Stop at {limit} or below it: {instead}",
                    $"With {instead}, a {parameter} that steps past {limit} stops too.", source.Path, testLine + 1, fixedLine), Confidence.Possible, Severity.Warning);
        }

        // n times the next call's answer, with the stop giving back 0.
        // PowerShell's call is in brackets of its own: $n * (Get-Factorial ($n - 1)).
        if (calls.All(call => call.Step < 0) && body.Any(line => Regex.IsMatch(masked[line], $@"(?<![\w$]){name}\s*\*\s*\(?\s*{Regex.Escape(function)}\s*\(|{Regex.Escape(function)}\s*\([^()]*\)\s*\)?\s*\*\s*{name}(?![\w$])")) &&
            ReturnsZero(masked, testLine, body) is { } zeroLine)
        {
            var zero = Regex.Match(masked[zeroLine], @"\breturn\s+(?<zero>0)\b");
            var fixedLine = source.Lines[zeroLine][..zero.Groups["zero"].Index] + "1" + source.Lines[zeroLine][(zero.Groups["zero"].Index + 1)..];
            return Finding(id, source, zeroLine,
                $"{function} gives back {parameter} times what the next call gives back, and line {zeroLine + 1} gives back 0 when it stops: every " +
                "answer then has 0 in its product, and is 0.",
                LocalFix.ReplaceLine(id, "Give back 1 when it stops", "1 leaves a product as it is, where 0 makes it 0.", source.Path, zeroLine + 1, fixedLine));
        }

        return null;
    }

    /// <summary>The line of the stop's return 0 - on the test's line, or the two after it - or null.</summary>
    private static int? ReturnsZero(IReadOnlyList<string> masked, int testLine, List<int> body) =>
        body.Where(line => line >= testLine && line <= testLine + 2).Cast<int?>()
            .FirstOrDefault(line => Regex.IsMatch(masked[line!.Value], @"\breturn\s+0\s*;?\s*(?:$|\bif\b|\bend\b|\})"));

    /// <summary>The self-calls' line with each argument changed: to <paramref name="to"/>, or with its + and - turned round.</summary>
    private static LocalFix Changed(string id, SourceFile source, int line, List<SelfCall> calls, string? to, string title, string explanation)
    {
        var text = source.Lines[line];
        foreach (var call in calls.OrderByDescending(call => call.Start))
        {
            var argument = text.Substring(call.Start, call.Length);
            var changed = to ?? Regex.Replace(argument, @"(?<sign>[-+])(?=\s*\d+\s*$)", sign => sign.Value == "+" ? "-" : "+");
            text = text[..call.Start] + changed + text[(call.Start + call.Length)..];
        }

        return LocalFix.ReplaceLine(id, title, explanation, source.Path, line + 1, text);
    }

    private static LogicFinding Finding(string id, SourceFile source, int line, string said, LocalFix? fix, Confidence confidence = Confidence.Likely, Severity severity = Severity.Error) =>
        new(id, line + 1, said, fix) { Severity = severity, Confidence = confidence };
}
