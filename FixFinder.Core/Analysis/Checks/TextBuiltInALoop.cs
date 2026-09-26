using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Text built a piece at a time inside a loop - text += piece - in Java and C#, whose strings never change once made:
/// each += makes a new string and copies all the text built so far into it, so building n pieces copies the growing
/// text n times over and the work grows with the square of n. A StringBuilder adds each piece to the end of the text it
/// already holds.
/// </summary>
/// <remarks>
/// Only Java and C#. CPython usually grows a string in place when nothing else holds it, and JavaScript engines join
/// strings lazily, so there the same code is not certainly slow and nothing is said.
/// <para>
/// The change to a StringBuilder is only offered where the code shows it gives the same text: the text is this
/// function's own local, starting as written text and only ever set to written text or added to, so it is never null;
/// every piece added is of a kind a StringBuilder writes as + would - text, a character, a number, a truth value; the
/// text is not read inside the loop, nor by a function written inside this one; the loop is not inside a try, where an
/// error could leave the text half built for a catch to read; and each piece is added on a line of its own.
/// </para>
/// </remarks>
internal static class TextBuiltInALoop
{
    public const string Rule = "analysis-text-built-in-loop";

    /// <summary>The types whose values a StringBuilder writes exactly as string concatenation does.</summary>
    private static readonly HashSet<string> WrittenAlike = new(StringComparer.Ordinal)
    {
        "String", "string", "char", "Character", "int", "Integer", "long", "Long", "short", "Short", "byte", "Byte", "double", "Double",
        "float", "Float", "boolean", "Boolean", "bool", "decimal", "uint", "ulong", "ushort", "sbyte",
    };

    /// <summary>Methods that give back text, so what they give is written the same either way.</summary>
    private static readonly HashSet<string> TextMethods = new(StringComparer.Ordinal)
    {
        "toString", "ToString", "substring", "Substring", "trim", "Trim", "strip", "toUpperCase", "toLowerCase", "ToUpper", "ToLower",
        "ToUpperInvariant", "ToLowerInvariant", "repeat", "replace", "Replace", "valueOf", "format", "Format", "join", "Join",
    };

    public static IEnumerable<AnalysisFinding> In(IrProgram program, IrFunction function, SourceText source)
    {
        if (program.Language is not (SourceLanguage.Java or SourceLanguage.CSharp)) yield break;

        var declared = DeclaredTypes(function);
        var texts = declared.Where(pair => pair.Value.Name is "String" or "string").Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);

        // Outer loops first: a text that grows across the passes of the outer loop is reported against it, once.
        foreach (var loop in IrWalk.Statements(function.Body).Where(s => s is For or ForEach or While))
        {
            var body = IrWalk.Statements(PerformanceChecks.BodyOf(loop)).ToList();

            foreach (var text in texts.Where(t => !reported.Contains(t)))
            {
                var additions = body.OfType<Assign>().Where(assign => PiecesAdded(assign, text) is not null).ToList();
                if (additions.Count == 0) continue;

                // Set to something new on every pass, the text does not grow from one pass to the next of this loop.
                if (body.Any(statement => SetsOtherwise(statement, text, additions))) continue;

                reported.Add(text);
                yield return new AnalysisFinding(
                    Rule,
                    additions[0].Span,
                    Message(program.Language, text, additions[0], loop, source),
                    Severity.Suggestion,
                    Confidence.Likely,
                    FindingKind.Performance,
                    PerformanceChecks.FoundBy)
                {
                    Function = function.FullName,
                    Fix = Change(program, function, loop, text, additions, declared, source),
                };
            }
        }
    }

    private static string Message(SourceLanguage language, string text, Assign addition, Stmt loop, SourceText source)
    {
        var code = source.Of(addition.Span).TrimEnd(';');
        var doing = code.Length is > 0 and <= 80 ? $"`{code}`" : $"adding to `{text}`";
        var type = language == SourceLanguage.Java ? "String" : "string";

        return $"`{text}` is a {type}, and a {type} never changes once it is made, so {doing} makes a new one on every pass of the loop " +
               $"that begins on line {loop.Span.Line}, copying all the text built so far into it - the work grows with the square of the number of pieces";
    }

    /// <summary>
    /// What an assignment adds to the end of the text, when that is all it does: text += piece, or text = text + a + b,
    /// whose pieces are a and b. Null for any other assignment.
    /// </summary>
    private static IReadOnlyList<Expr>? PiecesAdded(Assign assign, string text)
    {
        if (assign.Target is not Name { Identifier: var target } || target != text) return null;
        if (assign.Compound == BinaryOperator.Add) return [assign.Value];
        if (assign.Compound is not null) return null;

        // text = text + a + b reads left to right: ((text + a) + b).
        var pieces = new List<Expr>();
        var value = assign.Value;
        while (value is Binary { Operator: BinaryOperator.Add } joined)
        {
            pieces.Insert(0, joined.Right);
            value = joined.Left;
        }

        return value is Name { Identifier: var first } && first == text && pieces.Count > 0 ? pieces : null;
    }

    /// <summary>Whether a statement gives the text a value other than by adding to its end.</summary>
    private static bool SetsOtherwise(Stmt statement, string text, List<Assign> additions) => statement switch
    {
        Assign assign when IrWalk.Binds(assign.Target, text) => !additions.Contains(assign),
        Declare { Variable: var variable } => variable == text,
        ForEach loop => IrWalk.Binds(loop.Target, text),
        _ => IrWalk.Expressions(statement).SelectMany(IrWalk.Within).Any(inner => inner is AssignValue assigned && IrWalk.Binds(assigned.Target, text)),
    };

    private static Dictionary<string, IrType> DeclaredTypes(IrFunction function) =>
        function.Parameters.Select(p => (p.Name, p.Type))
            .Concat(IrWalk.Statements(function.Body).OfType<Declare>().Select(d => (Name: d.Variable, d.Type)))
            .Where(pair => !pair.Type.IsUnknown)
            .GroupBy(pair => pair.Name, StringComparer.Ordinal)
            .Where(group => group.Select(pair => pair.Type.Name).Distinct().Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Type, StringComparer.Ordinal);

    /// <summary>
    /// The change to a StringBuilder: made from the text just before the loop, added to in the loop, and turned back into
    /// the text just after it - offered only where the remarks above say it gives the same text.
    /// </summary>
    private static LocalFix? Change(
        IrProgram program, IrFunction function, Stmt loop, string text, List<Assign> additions, IReadOnlyDictionary<string, IrType> declared, SourceText source)
    {
        var java = program.Language == SourceLanguage.Java;
        if (!AlwaysWrittenText(function, text, additions) || MentionedInside(loop, text, additions) || SeenByNestedFunction(program, function, text)) return null;
        if (InsideTry(function.Body, loop)) return null;

        var file = loop.Span.File;
        var lines = source.Lines(file);
        if (loop.Span.Line < 1 || loop.Span.EndLine > lines.Count || loop.Span.EndLine <= loop.Span.Line) return null;
        if (!SetBeforeTheLoop.LoopStartsItsLine(lines, loop.Span.Line, program.Language) || lines[loop.Span.EndLine - 1].Trim() != "}") return null;

        var builder = $"{text}Builder";
        if (lines.Any(line => System.Text.RegularExpressions.Regex.IsMatch(line, $@"\b{builder}\b"))) return null;

        var append = java ? "append" : "Append";
        var replaced = new Dictionary<int, string>();
        foreach (var addition in additions)
        {
            var pieces = PiecesAdded(addition, text)!;
            if (pieces.Any(piece => !WritesAlike(piece, declared) || IrWalk.Names(piece).Contains(text))) return null;
            if (AloneOnItsLine(addition, lines, source) is not { } lead) return null;
            if (pieces.Select(piece => PieceText(piece, source)).ToList() is not { } written || written.Any(w => w is null)) return null;

            replaced[addition.Span.Line] = $"{lead}{builder}" + string.Concat(written.Select(w => $".{append}({w})")) + ";";
        }

        var loopLine = lines[loop.Span.Line - 1];
        var indent = loopLine[..(loopLine.Length - loopLine.TrimStart().Length)];
        var loopLines = Enumerable.Range(loop.Span.Line, loop.Span.EndLine - loop.Span.Line + 1)
            .Select(number => replaced.GetValueOrDefault(number) ?? lines[number - 1]);

        var making = java ? $"StringBuilder {builder} = new StringBuilder().append({text});" : $"var {builder} = new System.Text.StringBuilder({text});";
        var back = java ? $"{text} = {builder}.toString();" : $"{text} = {builder}.ToString();";

        return new LocalFix
        {
            RuleId = Rule,
            Title = $"Build `{text}` in a StringBuilder, and make the {(java ? "String" : "string")} once after the loop",
            Explanation =
                $"Each piece is added to the end of `{builder}`, which grows its buffer as it needs to, instead of copying all of `{text}` on every pass. " +
                $"`{text}` is given the finished text as soon as the loop ends. Every piece is text, a character, a number or a truth value, which a " +
                $"StringBuilder writes exactly as + does, and nothing reads `{text}` while the loop runs, so the text comes out the same.",
            File = file,
            StartLine = loop.Span.Line,
            RemoveCount = loop.Span.EndLine - loop.Span.Line + 1,
            NewLines = [indent + making, .. loopLines, indent + back],
        };
    }

    /// <summary>A piece a StringBuilder writes exactly as string concatenation does - never an array, which + and append write differently.</summary>
    private static bool WritesAlike(Expr piece, IReadOnlyDictionary<string, IrType> declared) => piece switch
    {
        Literal { Kind: not LiteralKind.Null } => true,
        Name { Identifier: var name } => declared.TryGetValue(name, out var type) && WrittenAlike.Contains(type.Name) && !type.Nullable,
        Binary { Operator: BinaryOperator.Add } joined => WritesAlike(joined.Left, declared) || WritesAlike(joined.Right, declared) || IsText(joined),
        Call { Callee: Member { MemberName: var method } } => TextMethods.Contains(method),
        Conditional choice => WritesAlike(choice.WhenTrue, declared) && WritesAlike(choice.WhenFalse, declared),
        _ => false,
    };

    /// <summary>A sum with written text anywhere in it is text itself.</summary>
    private static bool IsText(Expr expression) => expression switch
    {
        Literal { Kind: LiteralKind.Text } => true,
        Binary { Operator: BinaryOperator.Add } joined => IsText(joined.Left) || IsText(joined.Right),
        _ => false,
    };

    /// <summary>Whether the text is a local that starts as written text and is only ever set to written text or added to, so it is never null.</summary>
    private static bool AlwaysWrittenText(IrFunction function, string text, List<Assign> additions)
    {
        if (function.Parameters.Any(p => p.Name == text)) return false;

        var statements = IrWalk.Statements(function.Body).ToList();
        var declarations = statements.OfType<Declare>().Where(d => d.Variable == text).ToList();
        if (declarations is not [{ Initial: Literal { Kind: LiteralKind.Text } }]) return false;

        return statements.All(statement => statement switch
        {
            Assign assign when IrWalk.Binds(assign.Target, text) =>
                additions.Contains(assign) || PiecesAdded(assign, text) is not null || assign.Value is Literal { Kind: LiteralKind.Text },
            ForEach loop => !IrWalk.Binds(loop.Target, text),
            _ => !IrWalk.Expressions(statement).SelectMany(IrWalk.Within).Any(inner => inner is AssignValue assigned && IrWalk.Binds(assigned.Target, text)),
        });
    }

    /// <summary>Whether the loop reads the text anywhere but in the additions themselves.</summary>
    private static bool MentionedInside(Stmt loop, string text, List<Assign> additions)
    {
        foreach (var statement in IrWalk.Statements(PerformanceChecks.BodyOf(loop)))
        {
            if (statement is Assign assign && additions.Contains(assign)) continue;
            if (IrWalk.Expressions(statement).SelectMany(IrWalk.Names).Contains(text)) return true;
        }

        return loop switch
        {
            For counted => new[] { counted.Condition }.Concat(counted.Setup.Concat(counted.Step).SelectMany(IrWalk.Expressions)).OfType<Expr>().SelectMany(IrWalk.Names).Contains(text),
            While repeated => IrWalk.Names(repeated.Condition).Contains(text),
            ForEach each => IrWalk.Names(each.Items).Contains(text),
            _ => false,
        };
    }

    /// <summary>Whether a lambda or local function written in this one mentions the text - it could read it while the loop runs.</summary>
    private static bool SeenByNestedFunction(IrProgram program, IrFunction function, string text) =>
        program.AllFunctions.Any(other =>
            !ReferenceEquals(other, function) && other.Span.File == function.Span.File &&
            other.Span.Line >= function.Span.Line && other.Span.EndLine <= function.Span.EndLine &&
            IrWalk.Statements(other.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Names).Contains(text));

    /// <summary>Whether the loop is written inside a try, whose catch or finally could read the text when an error leaves it half built.</summary>
    private static bool InsideTry(IReadOnlyList<Stmt> block, Stmt loop)
    {
        foreach (var statement in block)
        {
            if (ReferenceEquals(statement, loop)) return false;
            if (statement is Try attempt && IrWalk.Statements(attempt.Body).Any(inner => ReferenceEquals(inner, loop))) return true;
            if (Children(statement).Any(inner => InsideTry(inner, loop))) return true;
        }

        return false;
    }

    private static IEnumerable<IReadOnlyList<Stmt>> Children(Stmt statement) => statement switch
    {
        If branch => [branch.Then, branch.Else],
        While repeated => [repeated.Body, repeated.Else],
        For counted => [counted.Setup, counted.Body, counted.Step],
        ForEach each => [each.Body, each.Else],
        Try attempt => [attempt.Body, .. attempt.Handlers.Select(h => h.Body), attempt.Else, attempt.Finally],
        Switch choice => choice.Cases.Select(c => c.Body),
        Using used => [used.Body],
        Labeled labeled => [labeled.Body],
        _ => [],
    };

    /// <summary>The white space an addition's line starts with, when the addition is all there is on the line.</summary>
    private static string? AloneOnItsLine(Assign addition, IReadOnlyList<string> lines, SourceText source)
    {
        if (addition.Span.Line < 1 || addition.Span.Line > lines.Count || addition.Span.EndLine != addition.Span.Line) return null;

        var line = lines[addition.Span.Line - 1];
        var trimmed = line.Trim();
        var statement = source.Of(addition.Span).Trim();
        if (statement.Length == 0 || trimmed != statement && trimmed != statement + ";") return null;

        return line[..(line.Length - line.TrimStart().Length)];
    }

    /// <summary>A piece as the code writes it, when it is written on one line.</summary>
    private static string? PieceText(Expr piece, SourceText source) =>
        piece.Span.EndLine == piece.Span.Line && source.Of(piece.Span) is { Length: > 0 } written ? written : null;
}
