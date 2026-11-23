using System.Globalization;
using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// A branch of an if - else if chain that can never run, because a branch before it takes every value it would: if score
/// &gt;= 50 ... else if score &gt;= 70 never reaches its second branch, as every score of 70 or more is 50 or more. No input can
/// test that branch. Run with a score of 75 in Python, Java, C, C++, C#, JavaScript, Go, Rust, Scala, Ruby, PHP, Lua, Perl, Dart,
/// Fortran, PowerShell, Elixir's cond, Erlang's if, F# and OCaml, each printed C, not B, with nothing said by any compiler, linter
/// or warning flag. A condition read here is one comparison of a name with a number - score &gt;= 70 - or two joined by and,
/// 50 &lt;= score and score &lt; 70: the values each lets through are worked out, and a branch whose values all pass an earlier
/// branch's test is found. When every branch compares the same name the same way - a ladder of &gt;= or of &lt; - the change puts
/// the branches in order, the strictest first, so each takes the values meant for it.
/// </summary>
public static partial class UnreachableBranchPatterns
{
    /// <summary>
    /// One branch of a chain: its header line, where its condition is on that line, the lines of its body after it, and - in a
    /// chain with braces - the line and column of the } that closes its block.
    /// </summary>
    private sealed record Branch(int Header, int ConditionStart, int ConditionEnd, int BodyFirst, int BodyEnd, (int Line, int Column)? Close = null);

    /// <summary>A chain of branches, and whether they can be put in another order.</summary>
    private sealed record Chain(IReadOnlyList<Branch> Branches, bool Reorderable);

    private delegate IEnumerable<Chain> ChainReader(IReadOnlyList<string> masked);

    /// <summary>
    /// How one language writes a chain of branches: the word its checks' names carry - none for the languages with braces read
    /// alike - its files, how its code is read, and how its chains are found.
    /// </summary>
    private sealed record Dialect(string Named, IReadOnlySet<string> Files, Syntax Syntax, ChainReader Reader);

    private static readonly Dialect[] Dialects =
    [
        new("", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++", ".java", ".cs", ".js", ".mjs", ".cjs", ".dart", ".php"), Syntax.CLike, BraceChains),
        new("go-", CodePattern.Files(".go"), Syntax.CLike, BareBraceChains),
        new("rust-", CodePattern.Files(".rs"), Syntax.CLike, BareBraceChains),
        new("scala-", CodePattern.Files(".scala", ".sc"), Syntax.Scala, ScalaChains),
        new("python-", CodePattern.Files(".py", ".pyw"), Syntax.Python, PythonChains),
        new("ruby-", CodePattern.Files(".rb"), Syntax.Python, KeywordChains(@"if\b", @"elsif\b", @"^\s*(?:else|end)\b", @"(?:\s+then\b.*)?$")),
        new("lua-", CodePattern.Files(".lua"), Syntax.Lua, KeywordChains(@"if\b", @"elseif\b", @"^\s*(?:else|end)\b", @"\s+then\b.*$")),
        new("perl-", CodePattern.Files(".pl", ".pm"), Syntax.Perl, BraceChains),
        new("powershell-", CodePattern.Files(".ps1"), Syntax.Python, BraceChains),
        new("fortran-", CodePattern.Files(".f90", ".f95", ".f03", ".f08"), Syntax.Fortran,
            KeywordChains(@"if\s*\(", @"else\s*if\s*\(", @"^\s*(?:else\s*$|else\s*!|end\s*if\b|endif\b)", @"\)\s*then\s*$", RegexOptions.IgnoreCase)),
        new("fsharp-", CodePattern.Files(".fsx"), Syntax.FSharp, KeywordChains(@"if\b", @"elif\b", @"^\s*else\b", @"\s+then\b.*$")),
        new("ocaml-", CodePattern.Files(".ml"), Syntax.OCaml, KeywordChains(@"if\b", @"else\s+if\b", @"^\s*else\b", @"\s+then\b.*$")),
        new("elixir-", CodePattern.Files(".exs", ".ex"), Syntax.Python, ClauseChains(@"\bcond\s+do\s*$", @"^\s*end\b")),
        new("erlang-", CodePattern.Files(".erl"), Syntax.Erlang, ClauseChains(@"^\s*if\s*$", @"^\s*end\b")),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => Unreachable($"logic-{dialect.Named}branch-never-reached", dialect.Files, dialect.Syntax, dialect.Reader)),
        .. Dialects.Select(dialect => OddBoundary($"logic-{dialect.Named}boundary-odd-one-out", dialect.Files, dialect.Syntax, dialect.Reader)),
    ];

    private static CodePattern Unreachable(string id, IReadOnlySet<string> extensions, Syntax syntax, ChainReader reader) =>
        new(id, extensions, syntax, (patternId, source, masked) => NeverReached(patternId, source, masked, reader), Severity.Error, Confidence.Certain);

    private static CodePattern OddBoundary(string id, IReadOnlySet<string> extensions, Syntax syntax, ChainReader reader) =>
        new(id, extensions, syntax, (patternId, source, masked) => BoundaryOddOneOut(patternId, source, masked, reader), Severity.Warning, Confidence.Possible);

    private static int IndentOf(string line) => line.Length - line.TrimStart().Length;

    /// <summary>The next line from <paramref name="from"/> that holds code, or the count of lines when none does.</summary>
    private static int NextCode(IReadOnlyList<string> masked, int from)
    {
        while (from < masked.Count && masked[from].Trim().Length == 0) from++;
        return from;
    }

    /// <summary>
    /// Where the block that opens from <paramref name="column"/> of <paramref name="line"/> on closes: the line and column of its
    /// }, or null when it never does. A } before the block's { closes something else, and is passed over.
    /// </summary>
    private static (int Line, int Column)? BlockClose(IReadOnlyList<string> masked, int line, int column)
    {
        var depth = 0;
        for (var at = line; at < masked.Count; at++)
        {
            for (var i = at == line ? column : 0; i < masked[at].Length; i++)
            {
                if (masked[at][i] == '{') depth++;
                else if (masked[at][i] == '}' && depth > 0 && --depth == 0) return (at, i);
            }
        }
        return null;
    }

    // ---- Reading the chains ----

    /// <summary>
    /// if (...) and else if (...) - elseif in PHP and PowerShell, elsif in Perl - with braces: written } else if (...) { with
    /// the brace that closes the branch before, or else if (...) on a line of its own under that brace, each header at the
    /// first's indentation.
    /// </summary>
    private static IEnumerable<Chain> BraceChains(IReadOnlyList<string> masked)
    {
        var start = new Regex(@"^(?<indent>\s*)if\s*\(");
        var next = new Regex(@"^(?<indent>\s*)(?:\}\s*)?(?:else\s+if|elseif|elsif)\s*\(");
        var otherwise = new Regex(@"^\s*(?:\}\s*)?else\b(?!\s*if)");

        for (var index = 0; index < masked.Count; index++)
        {
            if (start.Match(masked[index]) is not { Success: true } opened) continue;
            var indent = opened.Groups["indent"].Length;

            var headers = new List<(int Line, int Open)> { (index, opened.Index + opened.Length - 1) };
            // Where each header's block closes, for the change that puts the branches in order.
            var closes = new List<(int Line, int Column)>();
            var line = index;
            var elseLine = -1;
            int? lastClose = null;
            var reorderable = true;
            while (true)
            {
                var (header, open) = headers[^1];
                if (Brackets.ClosingParenthesis(masked[header], open) is not { } close) { headers.Clear(); break; }
                // The branch's block: its { on the header line, or alone on the next line.
                var braceLine = masked[header][(close + 1)..].Contains('{') ? header : NextCode(masked, header + 1);
                if (braceLine >= masked.Count || !masked[braceLine].Contains('{')) { reorderable = false; break; }
                if (Brackets.FirstBlockEnd(masked, braceLine) is not { } blockEnd) { headers.Clear(); break; }
                lastClose = blockEnd;
                // The } of the block that opens after the condition - which a { inside the condition, as a lambda's, would
                // put somewhere else than the line read above: such a chain is not put in order.
                if (BlockClose(masked, braceLine, braceLine == header ? close + 1 : 0) is { } closing && closing.Line == blockEnd) closes.Add(closing);
                else reorderable = false;

                // A block that opens and closes on its own header line - } else if (...) { ...; } - ends the branch on that line,
                // and the } the line starts with closed the branch before: what follows is on the next line with code. Read as
                // the } that closes this branch, it gave this same header again, and again, so the chain never ended.
                var closedOnHeader = blockEnd == header;
                var after = !closedOnHeader && masked[blockEnd].TrimStart().StartsWith('}') && masked[blockEnd].Trim().Length > 1 ? blockEnd : NextCode(masked, blockEnd + 1);
                if (after >= masked.Count) break;
                // Each header after the one before it, so the chain comes to an end however its lines are read.
                if (after <= header) { headers.Clear(); break; }
                if (next.Match(masked[after]) is { Success: true } continued && continued.Groups["indent"].Length == indent)
                {
                    headers.Add((after, continued.Index + continued.Length - 1));
                    continue;
                }
                if (otherwise.IsMatch(masked[after]) && IndentOf(masked[after]) == indent) elseLine = after;
                break;
            }

            if (headers.Count < 2) continue;

            var branches = new List<Branch>();
            for (var number = 0; number < headers.Count; number++)
            {
                var (header, open) = headers[number];
                var close = Brackets.ClosingParenthesis(masked[header], open)!.Value;
                var bodyEnd = number + 1 < headers.Count ? headers[number + 1].Line
                    : elseLine >= 0 ? elseLine
                    : masked[lastClose!.Value].Trim() == "}" && !masked[header][(close + 1)..].Contains('{') ? lastClose.Value + 1 : lastClose!.Value;
                branches.Add(new Branch(header, open + 1, close, header + 1, bodyEnd, reorderable ? closes[number] : null));
            }

            yield return new Chain(branches, reorderable);
            index = headers[^1].Line;
        }
    }

    /// <summary>Go's and Rust's: if cond { and } else if cond {, the condition between the keyword and the brace.</summary>
    private static IEnumerable<Chain> BareBraceChains(IReadOnlyList<string> masked)
    {
        var start = new Regex(@"^(?<indent>\s*)if\s+(?<condition>.+?)\s*\{\s*$");
        var next = new Regex(@"^(?<indent>\s*)\}\s*else\s+if\s+(?<condition>.+?)\s*\{\s*$");

        for (var index = 0; index < masked.Count; index++)
        {
            if (start.Match(masked[index]) is not { Success: true } opened) continue;
            var branches = new List<Branch>();
            var header = index;
            var condition = opened.Groups["condition"];
            while (true)
            {
                if (Brackets.FirstBlockEnd(masked, header) is not { } end) { branches.Clear(); break; }
                branches.Add(new Branch(header, condition.Index, condition.Index + condition.Length, header + 1, end));
                // A block closed on its own header line - } else if n > 5 { return 2 } else if n > 3 { - ends the chain there: the
                // else if after it on that line was read as this same header again, and again, so the chain never ended. That
                // header's condition runs on into the block, so it is not read as values, and the chain is not put in order.
                if (end == header) break;
                if (next.Match(masked[end]) is not { Success: true } continued || continued.Groups["indent"].Length != opened.Groups["indent"].Length) break;
                header = end;
                condition = continued.Groups["condition"];
            }

            if (branches.Count < 2) continue;
            yield return new Chain(branches, true);
            index = branches[^1].Header;
        }
    }

    /// <summary>Scala's: if (...) with braces, as Java's, or Scala 3's if cond then, else if cond then, at one indentation.</summary>
    private static IEnumerable<Chain> ScalaChains(IReadOnlyList<string> masked) =>
        BraceChains(masked).Concat(KeywordChains(@"if\b", @"else\s+if\b", @"^\s*else\b(?!\s+if)", @"\s+then\b.*$")(masked));

    /// <summary>
    /// Chains written with keywords and indentation: a first header, the headers after it at its indentation, and a line that
    /// ends the conditions - else, end. The condition is what is between the keyword and <paramref name="after"/>.
    /// </summary>
    private static ChainReader KeywordChains(string first, string then, string ends, string after, RegexOptions options = RegexOptions.None) => masked =>
    {
        var start = new Regex($@"^(?<indent>\s*){first}\s*(?<condition>.+?){after}", options);
        var next = new Regex($@"^(?<indent>\s*){then}\s*(?<condition>.+?){after}", options);
        var stop = new Regex(ends, options);
        return KeywordChainsOf(masked, start, next, stop);
    };

    private static IEnumerable<Chain> KeywordChainsOf(IReadOnlyList<string> masked, Regex start, Regex next, Regex stop)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (start.Match(masked[index]) is not { Success: true } opened) continue;
            var indent = opened.Groups["indent"].Length;
            var headers = new List<(int Line, Group Condition)> { (index, opened.Groups["condition"]) };
            var end = -1;
            for (var line = index + 1; line < masked.Count; line++)
            {
                if (masked[line].Trim().Length == 0 || IndentOf(masked[line]) > indent) continue;
                if (IndentOf(masked[line]) < indent) { end = line; break; }
                if (next.Match(masked[line]) is { Success: true } continued) { headers.Add((line, continued.Groups["condition"])); continue; }
                end = stop.IsMatch(masked[line]) ? line : -1;
                break;
            }

            if (headers.Count < 2 || end < 0) continue;
            var branches = headers.Select((header, number) => new Branch(header.Line, header.Condition.Index, header.Condition.Index + header.Condition.Length,
                header.Line + 1, number + 1 < headers.Count ? headers[number + 1].Line : end)).ToList();
            yield return new Chain(branches, true);
            index = headers[^1].Line;
        }
    }

    /// <summary>Python's: if cond: and elif cond: at one indentation, the chain ending at else:, or where the indentation goes back.</summary>
    private static IEnumerable<Chain> PythonChains(IReadOnlyList<string> masked)
    {
        var start = new Regex(@"^(?<indent>\s*)if\s+(?<condition>[^:]+?)\s*:");
        var next = new Regex(@"^(?<indent>\s*)elif\s+(?<condition>[^:]+?)\s*:");
        var otherwise = new Regex(@"^\s*else\s*:");

        for (var index = 0; index < masked.Count; index++)
        {
            if (start.Match(masked[index]) is not { Success: true } opened) continue;
            var indent = opened.Groups["indent"].Length;
            var headers = new List<(int Line, Group Condition)> { (index, opened.Groups["condition"]) };
            var end = masked.Count;
            for (var line = index + 1; line < masked.Count; line++)
            {
                if (masked[line].Trim().Length == 0 || IndentOf(masked[line]) > indent) continue;
                if (IndentOf(masked[line]) == indent && next.Match(masked[line]) is { Success: true } continued) { headers.Add((line, continued.Groups["condition"])); continue; }
                end = line;
                if (IndentOf(masked[line]) == indent && otherwise.IsMatch(masked[line])) end = line;
                break;
            }

            // The last branch's body ends at the last line with code before the chain ends.
            while (end - 1 > headers[^1].Line && masked[end - 1].Trim().Length == 0) end--;
            if (headers.Count < 2) continue;
            var branches = headers.Select((header, number) => new Branch(header.Line, header.Condition.Index, header.Condition.Index + header.Condition.Length,
                header.Line + 1, number + 1 < headers.Count ? headers[number + 1].Line : end)).ToList();
            yield return new Chain(branches, true);
            index = headers[^1].Line;
        }
    }

    /// <summary>
    /// Elixir's cond and Erlang's if: a line that opens the clauses, then one clause a line - a condition, then -&gt; - until the
    /// end. A clause whose condition is true alone is the else, and ends the conditions.
    /// </summary>
    private static ChainReader ClauseChains(string opens, string ends) => masked =>
    {
        var open = new Regex(opens);
        var close = new Regex(ends);
        var clause = new Regex(@"^(?<indent>\s*)(?<condition>.+?)\s*->");
        var chains = new List<Chain>();
        for (var index = 0; index < masked.Count; index++)
        {
            if (!open.IsMatch(masked[index])) continue;
            var headers = new List<(int Line, Group Condition)>();
            var clauseIndent = -1;
            var end = -1;
            var endedByTrue = false;
            for (var line = index + 1; line < masked.Count; line++)
            {
                if (masked[line].Trim().Length == 0) continue;
                if (close.IsMatch(masked[line]) && IndentOf(masked[line]) <= IndentOf(masked[index])) { end = line; break; }
                if (clause.Match(masked[line]) is { Success: true } found && (clauseIndent < 0 || found.Groups["indent"].Length == clauseIndent))
                {
                    clauseIndent = found.Groups["indent"].Length;
                    if (found.Groups["condition"].Value.Trim() == "true") { end = line; endedByTrue = true; break; }
                    headers.Add((line, found.Groups["condition"]));
                }
            }

            if (headers.Count < 2 || end < 0) continue;
            chains.Add(new Chain(headers.Select((header, number) => new Branch(header.Line, header.Condition.Index, header.Condition.Index + header.Condition.Length,
                header.Line + 1, number + 1 < headers.Count ? headers[number + 1].Line : end)).ToList(),
                // Only with a true clause after them are the clauses shaped alike: in Erlang the last before end has no ;.
                endedByTrue));
        }
        return chains;
    };

    // ---- Reading a condition as the values it lets through ----

    /// <summary>The values a test lets through: from one bound to another, each either taken in or not, or open to infinity.</summary>
    internal sealed record Range(string Name, double Low, bool LowIn, double High, bool HighIn)
    {
        /// <summary>Whether every value this range lets through, <paramref name="wider"/> lets through too.</summary>
        public bool Within(Range wider) =>
            Name == wider.Name &&
            (Low > wider.Low || (Low == wider.Low && (wider.LowIn || !LowIn))) &&
            (High < wider.High || (High == wider.High && (wider.HighIn || !HighIn)));

        /// <summary>A value this range lets through, to show in the message.</summary>
        public string Example() =>
            double.IsNegativeInfinity(Low) ? Number(HighIn ? High : High - 1) : Number(LowIn ? Low : Low + 1);

        private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"^(?<name>[A-Za-z_$@][\w.$]*)\s*(?<operator><=|>=|=<|<|>|===|==|=:=|-le|-ge|-lt|-gt|-eq|\.le\.|\.ge\.|\.lt\.|\.gt\.|\.eq\.)\s*(?<number>-?\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex NameThenNumber();

    [GeneratedRegex(@"^(?<number>-?\d+(?:\.\d+)?)\s*(?<operator><=|>=|=<|<|>|===|==|=:=)\s*(?<name>[A-Za-z_$@][\w.$]*)$")]
    private static partial Regex NumberThenName();

    /// <summary>A comparison's operator turned round, for a number written first: 50 &lt;= score is score &gt;= 50.</summary>
    private static string TurnedRound(string op) => op switch { "<=" or "=<" => ">=", ">=" => "<=", "<" => ">", ">" => "<", _ => op };

    private static Range? OneComparison(string text)
    {
        text = text.Trim();
        while (text.StartsWith('(') && text.EndsWith(')') && Brackets.Closing(text, 0) == text.Length - 1) text = text[1..^1].Trim();

        string name, op, number;
        if (NameThenNumber().Match(text) is { Success: true } named)
            (name, op, number) = (named.Groups["name"].Value, named.Groups["operator"].Value.ToLowerInvariant(), named.Groups["number"].Value);
        else if (NumberThenName().Match(text) is { Success: true } numbered)
            (name, op, number) = (numbered.Groups["name"].Value, TurnedRound(numbered.Groups["operator"].Value), numbered.Groups["number"].Value);
        else return null;

        var value = double.Parse(number, CultureInfo.InvariantCulture);
        return op switch
        {
            ">=" or "-ge" or ".ge." => new Range(name, value, true, double.PositiveInfinity, false),
            ">" or "-gt" or ".gt." => new Range(name, value, false, double.PositiveInfinity, false),
            "<=" or "=<" or "-le" or ".le." => new Range(name, double.NegativeInfinity, false, value, true),
            "<" or "-lt" or ".lt." => new Range(name, double.NegativeInfinity, false, value, false),
            _ => new Range(name, value, true, value, true),
        };
    }

    [GeneratedRegex(@"\s*(?:&&|\band\b|-and\b|\.and\.|\bandalso\b)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex And();

    /// <summary>
    /// The values a condition lets through - one comparison of a name with a number, two joined by and, or Python's
    /// 50 &lt;= score &lt; 70 - or null for any other condition.
    /// </summary>
    internal static Range? ValuesLetThrough(string condition)
    {
        condition = condition.Trim();
        while (condition.StartsWith('(') && condition.EndsWith(')') && Brackets.Closing(condition, 0) == condition.Length - 1) condition = condition[1..^1].Trim();

        var chained = Regex.Match(condition, @"^(?<low>-?\d+(?:\.\d+)?)\s*(?<first><=|<)\s*(?<name>[A-Za-z_]\w*)\s*(?<second><=|<)\s*(?<high>-?\d+(?:\.\d+)?)$");
        if (chained.Success)
            return new Range(chained.Groups["name"].Value, double.Parse(chained.Groups["low"].Value, CultureInfo.InvariantCulture), chained.Groups["first"].Value == "<=",
                double.Parse(chained.Groups["high"].Value, CultureInfo.InvariantCulture), chained.Groups["second"].Value == "<=");

        var parts = And().Split(condition);
        if (parts.Length > 2 || parts.Any(part => part.Contains("||") || Regex.IsMatch(part, @"\bor\b|-or\b|\.or\.|\borelse\b", RegexOptions.IgnoreCase))) return null;
        var ranges = parts.Select(OneComparison).ToList();
        if (ranges.Any(range => range is null)) return null;
        if (ranges.Count == 1) return ranges[0];

        var (first, second) = (ranges[0]!, ranges[1]!);
        if (first.Name != second.Name) return null;
        var (low, lowIn) = first.Low > second.Low || (first.Low == second.Low && !first.LowIn) ? (first.Low, first.LowIn) : (second.Low, second.LowIn);
        var (high, highIn) = first.High < second.High || (first.High == second.High && !first.HighIn) ? (first.High, first.HighIn) : (second.High, second.HighIn);
        return new Range(first.Name, low, lowIn, high, highIn);
    }

    // ---- Finding, and the change ----

    private static IEnumerable<LogicFinding> NeverReached(string id, SourceFile source, IReadOnlyList<string> masked, ChainReader reader)
    {
        foreach (var chain in reader(masked))
        {
            var ranges = chain.Branches.Select(branch => ValuesLetThrough(masked[branch.Header][branch.ConditionStart..branch.ConditionEnd])).ToList();
            var change = chain.Reorderable ? Reordered(id, source, chain, ranges) : null;

            for (var later = 1; later < chain.Branches.Count; later++)
            {
                if (ranges[later] is not { } values) continue;
                var earlier = Enumerable.Range(0, later).FirstOrDefault(before => ranges[before] is { } taken && values.Within(taken), -1);
                if (earlier < 0) continue;

                var laterBranch = chain.Branches[later];
                var earlierBranch = chain.Branches[earlier];
                var laterCondition = source.Lines[laterBranch.Header][laterBranch.ConditionStart..laterBranch.ConditionEnd].Trim();
                var earlierCondition = source.Lines[earlierBranch.Header][earlierBranch.ConditionStart..earlierBranch.ConditionEnd].Trim();

                yield return new LogicFinding(id, laterBranch.Header + 1,
                    $"Every value {laterCondition} lets through, {earlierCondition} on line {earlierBranch.Header + 1} lets through too, and that branch comes " +
                    $"first - so this branch can never run, and no input can test it: {values.Name} = {values.Example()} takes line {earlierBranch.Header + 1}'s branch",
                    change);
            }
        }
    }

    /// <summary>
    /// A ladder of three branches or more comparing one name with a number each, the same way round and in order - mark &gt;= 70,
    /// mark &gt;= 60, mark &gt;= 50, mark &gt; 40 - where one branch alone differs from the others in whether its own number is let
    /// through: every other one takes in the number it names, and this one leaves it to the branch after. A mark of exactly 40
    /// is not given this branch's answer, as 70, 60 and 50 are given theirs.
    /// </summary>
    /// <remarks>
    /// It may be meant, so it is a warning that it may be a mistake, with the change that makes the branch like the others.
    /// The numbers have to step alike: the odd one is said only when it is a whole number that the others' common step - their
    /// greatest common divisor, above 1 - divides, as 40 is for 70, 60 and 50. mark &gt; 39 among them is mark &gt;= 40 for whole
    /// marks, written another way, and is left alone.
    /// </remarks>
    private static IEnumerable<LogicFinding> BoundaryOddOneOut(string id, SourceFile source, IReadOnlyList<string> masked, ChainReader reader)
    {
        foreach (var whole in reader(masked))
        {
            // A last clause of true - Elixir's cond and Erlang's if end so - is the chain's else, and is no rung of the ladder.
            var branches = whole.Branches.ToList();
            if (branches.Count > 0 && masked[branches[^1].Header][branches[^1].ConditionStart..branches[^1].ConditionEnd].Trim() == "true") branches.RemoveAt(branches.Count - 1);
            var chain = whole with { Branches = branches };

            if (chain.Branches.Count < 3) continue;
            var ranges = chain.Branches.Select(branch => ValuesLetThrough(masked[branch.Header][branch.ConditionStart..branch.ConditionEnd])).ToList();
            if (ranges.Any(range => range is null) || ranges.Select(range => range!.Name).Distinct().Count() != 1) continue;
            var all = ranges.Select(range => range!).ToList();

            // A ladder from the top - >= 70, >= 60 - each with a lowest value, or from the bottom - < 40, < 50 - each with a highest.
            var fromTheTop = all.All(range => double.IsPositiveInfinity(range.High) && !double.IsNegativeInfinity(range.Low));
            var fromTheBottom = all.All(range => double.IsNegativeInfinity(range.Low) && !double.IsPositiveInfinity(range.High));
            if (!fromTheTop && !fromTheBottom) continue;
            var bounds = all.Select(range => fromTheTop ? range.Low : range.High).ToList();
            var takesItsOwn = all.Select(range => fromTheTop ? range.LowIn : range.HighIn).ToList();
            if (!bounds.Zip(bounds.Skip(1)).All(pair => fromTheTop ? pair.First > pair.Second : pair.First < pair.Second)) continue;

            var taking = takesItsOwn.Count(takes => takes);
            var odd = taking == 1 && takesItsOwn.Count >= 3 ? takesItsOwn.IndexOf(true)
                : taking == takesItsOwn.Count - 1 && takesItsOwn.Count >= 3 ? takesItsOwn.IndexOf(false)
                : -1;
            if (odd < 0) continue;

            // The numbers step alike - 70, 60, 50 by 10 - and the odd one is on that step: 40, not 39.
            if (bounds.Any(bound => bound != Math.Floor(bound))) continue;
            var step = bounds.Where((_, index) => index != odd).Select(bound => (long)Math.Abs(bound)).Aggregate(0L, GreatestCommonDivisor);
            if (step <= 1 || (long)Math.Abs(bounds[odd]) % step != 0) continue;

            var branch = chain.Branches[odd];
            var written = source.Lines[branch.Header][branch.ConditionStart..branch.ConditionEnd].Trim();
            var others = chain.Branches.Where((_, index) => index != odd)
                .Select(other => source.Lines[other.Header][other.ConditionStart..other.ConditionEnd].Trim()).ToList();
            var number = bounds[odd].ToString(CultureInfo.InvariantCulture);
            var name = all[odd].Name;
            var where = odd + 1 < chain.Branches.Count ? $"the branch on line {chain.Branches[odd + 1].Header + 1}" : "what comes after the last branch";
            var change = OperatorTurned(id, source, branch, takesItsOwn[odd]);
            // The condition as the change writes it: the same place on the changed line, longer or shorter by the operator.
            var rewritten = change is null ? null
                : change.NewLines[0].Substring(branch.ConditionStart, branch.ConditionEnd - branch.ConditionStart + change.NewLines[0].Length - source.Lines[branch.Header].Length).Trim();

            var otherNumbers = bounds.Where((_, index) => index != odd).Select(bound => bound.ToString(CultureInfo.InvariantCulture)).ToList();
            var theirNumbers = $"{string.Join(", ", otherNumbers.SkipLast(1))} and {otherNumbers[^1]}";
            var what = takesItsOwn[odd]
                ? $"Every other branch of this chain leaves its own number to the branch after it - {string.Join(", ", others)} - but {written} takes " +
                  $"{number} in: {name} = {number} is given this branch's answer, where {theirNumbers} are each given the next branch's"
                : $"Every other branch of this chain takes in its own number - {string.Join(", ", others)} - but {written} does not: " +
                  $"{name} = {number} is not given this branch's answer, as {theirNumbers} are given theirs, and goes on to {where}";
            yield return new LogicFinding(id, branch.Header + 1,
                what + (rewritten is null ? "" : $". If {number} belongs as the others' numbers do, write {rewritten}"), change);
        }

        static long GreatestCommonDivisor(long one, long other)
        {
            while (other != 0) (one, other) = (other, one % other);
            return one;
        }
    }

    /// <summary>
    /// The branch's comparison made like the others: one that leaves its number out made to take it in - &gt; to &gt;=, &lt; to &lt;=,
    /// -gt to -ge, .gt. to .ge. - or one that takes it in made to leave it out, the other way. Null when its line does not
    /// hold just one comparison to change.
    /// </summary>
    private static LocalFix? OperatorTurned(string id, SourceFile source, Branch branch, bool takesItsOwn)
    {
        var line = source.Lines[branch.Header];
        var condition = line[branch.ConditionStart..branch.ConditionEnd];
        var operators = OddOperator().Matches(condition);
        if (operators.Count != 1) return null;

        var found = operators[0].Value;
        var turned = (found.ToLowerInvariant(), takesItsOwn) switch
        {
            (">", false) => ">=", ("<", false) => found == "<" && IsErlang(source) ? "=<" : "<=",
            ("-gt", false) => "-ge", ("-lt", false) => "-le", (".gt.", false) => ".ge.", (".lt.", false) => ".le.",
            (">=", true) => ">", ("<=" or "=<", true) => "<", ("-ge", true) => "-gt", ("-le", true) => "-lt", (".ge.", true) => ".gt.", (".le.", true) => ".lt.",
            _ => null,
        };
        if (turned is null) return null;

        var at = branch.ConditionStart + operators[0].Index;
        return new LocalFix
        {
            RuleId = id, Title = takesItsOwn ? $"Leave the number out, as the other branches do: {turned}" : $"Take the number in, as the other branches do: {turned}",
            Explanation = "With the same comparison in every branch, each number of the ladder is given the same branch as the others are - its own, or the next.",
            File = source.Path, StartLine = branch.Header + 1, RemoveCount = 1,
            NewLines = [line[..at] + turned + line[(at + found.Length)..]],
        };

        static bool IsErlang(SourceFile file) => Path.GetExtension(file.Path).Equals(".erl", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A comparison's operator, of those a ladder is written with: &gt;=, &gt;, &lt;=, =&lt;, &lt;, and the words for them.</summary>
    [GeneratedRegex(@"(?<![<>=!])(?:>=|<=|=<|>(?!=)|<(?!=)|-ge\b|-gt\b|-le\b|-lt\b|\.ge\.|\.gt\.|\.le\.|\.lt\.)", RegexOptions.IgnoreCase)]
    private static partial Regex OddOperator();

    /// <summary>
    /// When every branch compares one name the same way - all &gt;= and &gt;, or all &lt;= and &lt; - the chain with its branches put in
    /// order: the highest bound first for &gt;=, the lowest first for &lt;. Each place keeps its header's own words - if, else if,
    /// the brace before them - and is given the condition and the rest of the branch that belongs there: in a chain with braces,
    /// its text through the } of its block; in the others, the rest of its header line and the lines of its body.
    /// </summary>
    private static LocalFix? Reordered(string id, SourceFile source, Chain chain, IReadOnlyList<Range?> ranges)
    {
        if (ranges.Any(range => range is null)) return null;
        var all = ranges.Select(range => range!).ToList();
        if (all.Select(range => range.Name).Distinct().Count() != 1) return null;

        var rising = all.All(range => double.IsPositiveInfinity(range.High) && !double.IsNegativeInfinity(range.Low));
        var falling = all.All(range => double.IsNegativeInfinity(range.Low) && !double.IsPositiveInfinity(range.High));
        if (!rising && !falling) return null;

        var order = Enumerable.Range(0, all.Count).OrderBy(number => rising ? -all[number].Low : all[number].High).ToList();
        if (order.SequenceEqual(Enumerable.Range(0, all.Count))) return null;

        var first = chain.Branches[0].Header;
        int last;
        List<string> lines;
        if (chain.Branches.All(branch => branch.Close is not null))
        {
            lines = BlocksReordered(source.Lines, chain.Branches, order);
            last = chain.Branches[^1].Close!.Value.Line + 1;
            // The lines at the end that stay as they were - the last block's } - are left out of the change.
            while (lines.Count > 1 && last - 1 > first && lines[^1] == source.Lines[last - 1])
            {
                lines.RemoveAt(lines.Count - 1);
                last--;
            }
        }
        else
        {
            last = chain.Branches[^1].BodyEnd;
            lines = [];
            for (var place = 0; place < chain.Branches.Count; place++)
            {
                var here = chain.Branches[place];
                var moved = chain.Branches[order[place]];
                lines.Add(source.Lines[here.Header][..here.ConditionStart] + source.Lines[moved.Header][moved.ConditionStart..]);
                lines.AddRange(source.Lines.Skip(moved.BodyFirst).Take(moved.BodyEnd - moved.BodyFirst));
            }
        }

        var name = all[0].Name;
        return new LocalFix
        {
            RuleId = id, Title = $"Put the branches in order, the {(rising ? "highest" : "lowest")} first",
            Explanation = $"With the strictest test first, each value of {name} reaches the first branch whose test it passes - so each branch takes " +
                          "the values meant for it.",
            File = source.Path, StartLine = first + 1, RemoveCount = last - first, NewLines = lines,
        };
    }

    /// <summary>
    /// A chain with braces with its branches put in <paramref name="order"/>. A branch's own text runs from its condition to the }
    /// that closes its block - over many lines, or on its header line alone - and what is between one block and the next
    /// condition - } else if (, or a line break and else if ( - stays in its place, so blocks of every shape, mixed in one chain,
    /// each still close where they should. Taking a header line and the lines under it instead moved a one-line block's } with
    /// its header, and left the } line after the chain to close another branch's block.
    /// </summary>
    private static List<string> BlocksReordered(IReadOnlyList<string> lines, IReadOnlyList<Branch> branches, IReadOnlyList<int> order)
    {
        static (int Line, int Column) ConditionOf(Branch branch) => (branch.Header, branch.ConditionStart);
        static (int Line, int Column) AfterBlock(Branch branch) => (branch.Close!.Value.Line, branch.Close.Value.Column + 1);

        var text = new System.Text.StringBuilder(Between(lines, (branches[0].Header, 0), ConditionOf(branches[0])));
        for (var place = 0; place < branches.Count; place++)
        {
            if (place > 0) text.Append(Between(lines, AfterBlock(branches[place - 1]), ConditionOf(branches[place])));
            var moved = branches[order[place]];
            text.Append(Between(lines, ConditionOf(moved), AfterBlock(moved)));
        }
        var (lastLine, afterLast) = AfterBlock(branches[^1]);
        text.Append(lines[lastLine][afterLast..]);
        return [.. text.ToString().Split('\n')];
    }

    /// <summary>The text from one place in the lines to another, the lines between joined by line breaks.</summary>
    private static string Between(IReadOnlyList<string> lines, (int Line, int Column) from, (int Line, int Column) to)
    {
        if (from.Line == to.Line) return lines[from.Line][from.Column..to.Column];
        var text = new System.Text.StringBuilder(lines[from.Line][from.Column..]);
        for (var line = from.Line + 1; line < to.Line; line++) text.Append('\n').Append(lines[line]);
        return text.Append('\n').Append(lines[to.Line][..to.Column]).ToString();
    }
}
