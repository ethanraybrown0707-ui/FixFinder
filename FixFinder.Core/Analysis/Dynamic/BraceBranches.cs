using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// Why the line a program stopped on ran, in the languages a copy of the program follows - Java, JavaScript, TypeScript, C#,
/// Go, Rust, PHP, Dart, Lua, Ruby and Perl: the ifs, else ifs, elses and whiles around that line, and what the names their
/// conditions read held, printed by a copy of the program as it ran.
/// </summary>
/// <remarks>
/// An if chain's names are printed just before its if, which runs each time the chain is worked out, so they are what every
/// condition in the chain read; a while's at the start of its body, which runs each time its condition was true. A copy's
/// print can only be of a name the program has there, so only a function's parameters and the variables it gave a value
/// on an earlier line are printed - and a while whose condition changes what it reads, i++ &lt; n, is left out, as its body
/// sees the names after that. What a function that calls itself printed cannot be told apart from call to call, so nothing
/// is said of one.
/// </remarks>
internal static partial class BraceBranches
{
    /// <summary>The language a file is written in, of those a copy of the program follows - or null for any other.</summary>
    public static BraceDialect? DialectFor(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".java" => JavaNullOrigin.Dialect,
        ".cs" => CSharpNullOrigin.Dialect,
        ".js" or ".mjs" or ".cjs" or ".ts" or ".mts" or ".cts" => JavaScriptMissingOrigin.Dialect,
        ".go" => GoNilOrigin.Dialect,
        ".rs" => RustNoneOrigin.Dialect,
        ".php" => PhpNullOrigin.Dialect,
        ".dart" => DartNullOrigin.Dialect,
        ".lua" => LuaNilOrigin.Dialect,
        ".rb" => RubyNilOrigin.Dialect,
        ".pl" or ".pm" => PerlUndefOrigin.Dialect,
        _ => null,
    };

    /// <summary>One branch of an if chain, or a while's body: the line it is written on and its body's first and last line, all 1-based.</summary>
    private sealed record Branch(int Header, BranchKind Kind, int First, int Last);

    /// <summary>A decision around the line, and the line its names are printed with: its chain's if, or its own while.</summary>
    internal sealed record Around(int Line, string Code, BranchKind Kind, bool WasTrue, int PrintedFor);

    /// <summary>
    /// Runs a copy of <paramref name="program"/> that prints what each condition around <paramref name="crashLine"/> read,
    /// and gives back the decisions with those values - or null when there are none, when the copy did not stop the way the
    /// program did on the same line, or when it did not print every one of them in the call that stopped.
    /// </summary>
    public static async Task<IReadOnlyList<BranchTaken>?> TraceAsync(BraceDialect dialect, string program, SourceFile source, string? typed, int crashLine,
        Func<ParsedError, bool> stoppedTheSameWay, CancellationToken cancellationToken)
    {
        if (source.Line(crashLine) is null) return null;

        var code = new BraceCode(dialect, source.Lines, dialect.Blanked(source.Lines));
        var stoppedIn = code.FunctionAround(crashLine);
        if (stoppedIn is not null && code.CallsItself(stoppedIn)) return null;

        var around = Of(dialect, code, source.Lines, crashLine, stoppedIn);
        if (around.Count == 0) return null;

        var marks = new BraceMissingValue.Marks(dialect, source.Lines, code.Masked);
        foreach (var (line, added) in dialect.Additions(code.Masked)) marks.AtTheEnd(line, added);

        // Which call printed what: the call that began the function it stopped in, printed as the function starts.
        if (stoppedIn is not null)
            marks.AfterOpening(stoppedIn.Header, dialect.Print(dialect.Quoted($"{BraceMissingValue.Marker} call {stoppedIn.Name}")));

        var printed = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var point in around.Select(decision => decision.PrintedFor).Distinct())
        {
            var names = NamesKnownAt(code, stoppedIn, point)
                .Where(name => around.Where(decision => decision.PrintedFor == point && decision.Kind != BranchKind.Else).Any(decision => Reads(code.Masked[decision.Line - 1], name.Name)))
                .ToList();
            printed[point] = [.. names.Select(name => name.Name)];

            var shown = names.Count == 0
                ? dialect.Quoted($"{BraceMissingValue.Marker} decide {point}")
                : dialect.Quoted($"{BraceMissingValue.Marker} decide {point}{BraceMissingValue.Between}") + dialect.Joining +
                  string.Join(dialect.Joining + dialect.Quoted(BraceMissingValue.Between) + dialect.Joining,
                      names.Select(name => dialect.Quoted($"{name.Name} = ") + dialect.Joining + dialect.Shown(code.TypeOf(name.Name, name.Type, stoppedIn), name.Name)));

            // Before an if chain; at the start of a while's body.
            var at = dialect.WhileLine.IsMatch(code.Masked[point - 1]) ? FirstLineOfBody(dialect, code.Masked, point) : point;
            if (at is not { } line) return null;
            marks.Before(line, dialect.Print(shown));
        }

        if (dialect.BeforeTheFirstLine is { } first && Enumerable.Range(1, source.Lines.Count).FirstOrDefault(line => !source.Lines[line - 1].StartsWith("#!", StringComparison.Ordinal)) is > 0 and var firstLine)
            marks.AtTheStart(firstLine, first);

        if (await FixRun.RunChangedAsync(source, marks.Lines, program, typed, cancellationToken) is not { } run) return null;
        if (run.Error is not { } error || !stoppedTheSameWay(error) || LocalFixContext.OwnFrame(error)?.Line != crashLine) return null;

        var events = run.Lines.Select(line => line.Text.TrimEnd('\r', '\n'))
            .Where(text => text.StartsWith(BraceMissingValue.Marker + " ", StringComparison.Ordinal))
            .Select(text => text[(BraceMissingValue.Marker.Length + 1)..])
            .ToList();

        // The call that stopped is the last to begin, as the function does not call itself.
        var start = stoppedIn is null ? 0 : events.FindLastIndex(said => said == $"call {stoppedIn.Name}");
        if (start < 0) return null;

        var taken = new List<BranchTaken>();
        foreach (var decision in around)
        {
            if (decision.Kind == BranchKind.Else)
            {
                taken.Add(new BranchTaken(decision.Line, decision.Code, decision.Kind, decision.WasTrue, []));
                continue;
            }

            var said = events.FindLastIndex(said => said == $"decide {decision.PrintedFor}" || said.StartsWith($"decide {decision.PrintedFor}{BraceMissingValue.Between}", StringComparison.Ordinal));
            if (said < start) return null;

            var held = events[said].Split(BraceMissingValue.Between).Skip(1)
                .Select(pair => pair.IndexOf(" = ", StringComparison.Ordinal) is var equals and > 0 ? new HeldValue(pair[..equals], pair[(equals + 3)..]) : null)
                .OfType<HeldValue>()
                .ToList();
            var condition = code.Masked[decision.Line - 1];
            var values = held.Where(value => Reads(condition, value.Name)).OrderBy(value => FirstRead(condition, value.Name)).ToList();
            taken.Add(new BranchTaken(decision.Line, decision.Code, decision.Kind, decision.WasTrue, values));
        }

        return taken;
    }

    /// <summary>
    /// The decisions around the line in the function it is in, outermost first: each if chain one of whose branches holds the
    /// line - its branches up to that one, the ones above it false - and each while whose body holds it.
    /// </summary>
    internal static List<Around> Of(BraceDialect dialect, BraceCode code, IReadOnlyList<string> lines, int crashLine, BraceFunction? stoppedIn)
    {
        var masked = code.Masked;
        var found = new List<Around>();
        var ifsAtAStatement = code.Ifs(stoppedIn).ToHashSet();

        for (var head = stoppedIn is null ? 1 : stoppedIn.Header + 1; head < crashLine; head++)
        {
            if (code.FunctionAround(head) != stoppedIn) continue;
            var text = masked[head - 1];

            if (dialect.IfLine.IsMatch(text) && !dialect.ElseIfLine.IsMatch(text))
            {
                if (!ifsAtAStatement.Contains(head) || Chain(dialect, masked, head) is not { } chain) continue;
                var holding = chain.FindIndex(branch => branch.First <= crashLine && crashLine <= branch.Last);
                if (holding < 0) continue;

                found.AddRange(chain.Take(holding + 1).Select((branch, index) =>
                    new Around(branch.Header, Written(lines[branch.Header - 1], dialect), branch.Kind, WasTrue: index == holding, PrintedFor: head)));
            }
            else if (dialect.WhileLine.IsMatch(text) && !ChangesWhatItReads().IsMatch(text) &&
                     Body(dialect, masked, head) is { } body && body.First <= crashLine && crashLine <= body.Last)
            {
                found.Add(new Around(head, Written(lines[head - 1], dialect), BranchKind.While, WasTrue: true, PrintedFor: head));
            }
        }

        return found;
    }

    /// <summary>The branches of the if chain that starts on <paramref name="head"/>, or null when they are not all blocks this reads.</summary>
    private static List<Branch>? Chain(BraceDialect dialect, IReadOnlyList<string> masked, int head) =>
        dialect.BlocksEndWithAWord ? ChainEndingWithAWord(dialect, masked, head) : ChainInBraces(masked, head);

    /// <summary>if (x) { ... } else if (y) { ... } else { ... }, each brace on its header's line or the next line on its own.</summary>
    private static List<Branch>? ChainInBraces(IReadOnlyList<string> masked, int head)
    {
        var branches = new List<Branch>();
        var (header, kind) = (head - 1, BranchKind.If);

        while (true)
        {
            if (OpeningBrace(masked, header) is not var (openLine, openColumn) || ClosingBrace(masked, openLine, openColumn) is not var (closeLine, closeColumn)) return null;
            branches.Add(new Branch(header + 1, kind, openLine + 2, closeLine));
            if (kind == BranchKind.Else) return branches;

            // What follows the closing brace - on its line, or the next line with code - may be the chain's next branch.
            var after = masked[closeLine][(closeColumn + 1)..].Trim();
            var next = closeLine;
            if (after.Length == 0)
            {
                next = closeLine + 1;
                while (next < masked.Count && masked[next].Trim().Length == 0) next++;
                if (next >= masked.Count) return branches;
                after = masked[next].Trim();
            }

            if (ElseIfStarts().IsMatch(after)) (header, kind) = (next, BranchKind.ElseIf);
            else if (ElseStarts().IsMatch(after)) (header, kind) = (next, BranchKind.Else);
            else return branches;
        }
    }

    /// <summary>Ruby's and Lua's if ... elsif ... else ... end: one block to its end, its branches split where an elsif or else of its own starts.</summary>
    private static List<Branch>? ChainEndingWithAWord(BraceDialect dialect, IReadOnlyList<string> masked, int head)
    {
        if (dialect.BlockEnd(masked, head - 1) is not { } end || end <= head - 1) return null;

        // An elsif or else of a block inside a branch is that block's, so only those outside every inner block are counted.
        var headers = new List<(int Line, BranchKind Kind)> { (head - 1, BranchKind.If) };
        var inside = 0;
        for (var index = head; index < end; index++)
        {
            var text = masked[index];
            if (inside == 0 && dialect.ElseIfLine.IsMatch(text)) headers.Add((index, BranchKind.ElseIf));
            else if (inside == 0 && ElseAlone().IsMatch(text)) headers.Add((index, BranchKind.Else));
            inside += dialect.BlocksOpened(text);
        }

        return headers.Select((each, position) =>
            new Branch(each.Line + 1, each.Kind, each.Line + 2, position + 1 < headers.Count ? headers[position + 1].Line : end)).ToList();
    }

    /// <summary>A while's body, 1-based: from the line after its opening to the line before its closing.</summary>
    private static (int First, int Last)? Body(BraceDialect dialect, IReadOnlyList<string> masked, int header)
    {
        if (dialect.BlocksEndWithAWord)
            return dialect.BlockEnd(masked, header - 1) is { } end && end > header - 1 ? (header + 1, end) : null;

        return OpeningBrace(masked, header - 1) is var (openLine, openColumn) && ClosingBrace(masked, openLine, openColumn) is var (closeLine, _)
            ? (openLine + 2, closeLine)
            : null;
    }

    /// <summary>The first line of a while's body with code on it, 1-based - where what its condition read is printed.</summary>
    private static int? FirstLineOfBody(BraceDialect dialect, IReadOnlyList<string> masked, int header)
    {
        if (Body(dialect, masked, header) is not var (first, last)) return null;
        for (var line = first; line <= last; line++)
            if (masked[line - 1].Trim().Length > 0) return line;
        return null;
    }

    /// <summary>The brace that opens a block on line <paramref name="line"/> (0-based): the last thing on it, or alone on the next line.</summary>
    private static (int Line, int Column)? OpeningBrace(IReadOnlyList<string> masked, int line)
    {
        var text = masked[line].TrimEnd();
        if (text.EndsWith('{')) return (line, text.Length - 1);
        if (line + 1 < masked.Count && masked[line + 1].Trim() == "{") return (line + 1, masked[line + 1].IndexOf('{'));
        return null;
    }

    private static (int Line, int Column)? ClosingBrace(IReadOnlyList<string> masked, int line, int column)
    {
        var depth = 0;
        for (var index = line; index < masked.Count; index++)
        {
            for (var at = index == line ? column : 0; at < masked[index].Length; at++)
            {
                if (masked[index][at] == '{') depth++;
                else if (masked[index][at] == '}' && --depth == 0) return (index, at);
            }
        }

        return null;
    }

    /// <summary>
    /// The names the program has a value for when line <paramref name="line"/> runs, which a print there can name: the
    /// function's parameters and each variable a line of it above that one gave a value - with the type it is declared as.
    /// </summary>
    private static IEnumerable<(string Name, string Type)> NamesKnownAt(BraceCode code, BraceFunction? function, int line)
    {
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (type, name) in function?.Parameters ?? []) known.TryAdd(name, type);
        foreach (var (setOn, name, type) in code.LinesSettingAny(function).Where(set => set.Line < line)) known.TryAdd(name, type);
        return known.Select(each => (each.Key, each.Value));
    }

    /// <summary>Whether a line reads the name as a variable: not as a function it calls, nor as a part of something after a dot.</summary>
    private static bool Reads(string masked, string name) => FirstRead(masked, name) >= 0;

    /// <summary>Where on the line it first reads the name as a variable, or -1: the values are shown in the order the condition reads them.</summary>
    private static int FirstRead(string masked, string name) =>
        Regex.Match(masked, $@"(?<![\w$@.>]){Regex.Escape(name)}(?![\w$(])") is { Success: true } read ? read.Index : -1;

    /// <summary>The header as it is written, without the braces around it: <c>} else if (x &gt; 2) {</c> is <c>else if (x &gt; 2)</c>.</summary>
    private static string Written(string line, BraceDialect dialect) =>
        dialect.BlocksEndWithAWord ? line.Trim() : line.Trim().TrimStart('}').TrimEnd('{').Trim();

    [GeneratedRegex(@"^(?:else\s*if|elsif|elseif)\b")]
    private static partial Regex ElseIfStarts();

    [GeneratedRegex(@"^else\b(?!\s*if\b)")]
    private static partial Regex ElseStarts();

    [GeneratedRegex(@"^\s*else\s*$")]
    private static partial Regex ElseAlone();

    /// <summary>A condition that changes what it reads - i++, --left, (line = next()) - whose body sees its names after the change.</summary>
    [GeneratedRegex(@"\+\+|--|(?<![=!<>:])=(?![=~>])")]
    private static partial Regex ChangesWhatItReads();
}
