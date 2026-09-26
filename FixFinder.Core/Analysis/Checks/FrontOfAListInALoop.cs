using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Taking the first item out of a list, or putting one in front of it, on every pass of a loop. A list keeps its items
/// side by side from the start of one block of memory, so every other item has to move one place - to close the gap,
/// or to make room: n moves for a list of n. Done on every pass, the work grows with the passes times the length, and
/// with the square of the length when the loop empties the list from the front, as a queue does.
/// </summary>
/// <remarks>
/// Only where the code shows the list is one that moves its items, as each language's own documentation says it does:
/// a Python list, whose pop(0) and insert(0, v) "incur O(n) memory movement costs"; a Java ArrayList, whose remove and
/// add at a position shift every later element; a C# List, whose RemoveAt and Insert are O(n). Java's LinkedList takes
/// from its front without moving anything, so a Java list is only reported when every value it can hold is made as an
/// ArrayList. JavaScript is left out: how long shift() takes is up to the engine, not the language. Go and C/C++ are
/// left out too.
/// <para>
/// A loop that runs a number of times written into the code - over range(3), over a list written out in full, or
/// counting from one written number to another - does the moving a fixed number of times, and is left alone. A loop
/// over the same list is left to the check for a collection changed while a loop walks over it.
/// </para>
/// <para>
/// No change is offered. What takes from the front without moving anything - Python's deque, Java's ArrayDeque, C#'s
/// Queue and LinkedList - does not do everything a list does: a deque has no slices and prints as deque([...]), an
/// ArrayDeque holds no nulls, and none of the others can be read by position. Whether one will do depends on everything
/// else the program does with the list, which is for whoever knows the program to decide.
/// </para>
/// </remarks>
internal static class FrontOfAListInALoop
{
    public const string Rule = "analysis-list-front-in-loop";

    /// <summary>A call that takes the first item of a list, or puts one in front of it.</summary>
    private sealed record FrontCall(Call Call, string List, string Method, bool Takes);

    /// <summary>Each language's methods for taking the first item of its list and for putting one at a position.</summary>
    private static readonly Dictionary<SourceLanguage, (string Take, string Put)> FrontMethods = new()
    {
        [SourceLanguage.Python] = ("pop", "insert"),
        [SourceLanguage.Java] = ("remove", "add"),
        [SourceLanguage.CSharp] = ("RemoveAt", "Insert"),
    };

    public static IEnumerable<AnalysisFinding> In(IrProgram program, IrFunction function, SourceText source)
    {
        if (!FrontMethods.TryGetValue(program.Language, out var methods)) yield break;

        var loopsAround = new Dictionary<Call, List<Stmt>>(ReferenceEqualityComparer.Instance);
        var calls = new List<FrontCall>();

        // Outer loops come first, so each call's loops are listed from the outermost in.
        foreach (var loop in IrWalk.Statements(function.Body).Where(s => s is For or ForEach or While))
        {
            foreach (var inner in IrWalk.Statements(PerformanceChecks.BodyOf(loop)).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Within))
            {
                if (Front(inner, methods, source) is not { } front) continue;

                if (!loopsAround.TryGetValue(front.Call, out var loops))
                {
                    loopsAround[front.Call] = loops = [];
                    calls.Add(front);
                }

                loops.Add(loop);
            }
        }

        var evidence = new CollectionEvidence(program, function);
        var reported = new HashSet<(string List, SourceSpan Loop)>();

        foreach (var front in calls.OrderBy(f => f.Call.Span.Line).ThenBy(f => f.Call.Span.Column))
        {
            var loops = loopsAround[front.Call];

            // Walking the list while taking from it is a different mistake, with a check of its own.
            if (loops.Any(loop => loop is ForEach { Items: Name { Identifier: var walked } } && walked == front.List)) continue;

            var growing = loops.LastOrDefault(loop => !RunsAFixedNumberOfTimes(loop, program.Language));
            if (growing is null || !reported.Add((front.List, growing.Span))) continue;

            if (Moving(program, function, evidence, front.List) is not { } kind) continue;

            yield return new AnalysisFinding(
                Rule,
                front.Call.Span,
                Message(front, kind, growing, source),
                Severity.Suggestion,
                Confidence.Likely,
                FindingKind.Performance,
                PerformanceChecks.FoundBy)
            {
                Function = function.FullName,
            };
        }
    }

    /// <summary>The call, when it takes the first item of a named list or puts one in front of it: the position written as 0.</summary>
    private static FrontCall? Front(Expr expression, (string Take, string Put) methods, SourceText source) => expression switch
    {
        Call { Callee: Member { Target: Name list, MemberName: var method }, Arguments: [{ Name: null, Value: var position }] } call
            when method == methods.Take && WrittenAsZero(position, source) => new FrontCall(call, list.Identifier, method, Takes: true),

        Call { Callee: Member { Target: Name list, MemberName: var method }, Arguments: [{ Name: null, Value: var position }, { Name: null }] } call
            when method == methods.Put && WrittenAsZero(position, source) => new FrontCall(call, list.Identifier, method, Takes: false),

        _ => null,
    };

    /// <summary>
    /// A position written as the number 0 and nothing else. Java's remove(0L) is not one: no remove takes a long, so it
    /// removes the item equal to 0 instead.
    /// </summary>
    private static bool WrittenAsZero(Expr position, SourceText source) =>
        position is Literal { Kind: LiteralKind.Integer, Value: 0L } && source.Of(position.Span) == "0";

    /// <summary>
    /// Whether a loop runs a number of times written into the code: over a list, a tuple or a text written out in full,
    /// over Python's range of written numbers, or counting from a written number to another.
    /// </summary>
    private static bool RunsAFixedNumberOfTimes(Stmt loop, SourceLanguage language) => loop switch
    {
        ForEach { Items: CollectionLiteral or Literal { Kind: LiteralKind.Text } } => true,
        ForEach { Items: Call { Callee: Name { Identifier: "range" }, Arguments: { Count: > 0 } bounds } } when language == SourceLanguage.Python =>
            bounds.All(bound => bound.Value is Literal { Kind: LiteralKind.Integer }),
        For counted => CountsBetweenWrittenNumbers(counted),
        _ => false,
    };

    private static bool CountsBetweenWrittenNumbers(For counted)
    {
        var counters = LimitedByAWrittenNumber(counted.Condition).ToHashSet(StringComparer.Ordinal);

        return counters.Count > 0 && counted.Setup.Any(start => start switch
        {
            Declare { Initial: Literal { Kind: LiteralKind.Integer } } declared => counters.Contains(declared.Variable),
            Assign { Target: Name { Identifier: var assigned }, Compound: null, Value: Literal { Kind: LiteralKind.Integer } } => counters.Contains(assigned),
            _ => false,
        });
    }

    /// <summary>
    /// The names a loop's condition keeps below or above a written number - i &lt; 3 - including either side of an and,
    /// which ends the loop as soon as that side is false.
    /// </summary>
    private static IEnumerable<string> LimitedByAWrittenNumber(Expr? condition) => condition switch
    {
        Binary { Operator: BinaryOperator.And } both => LimitedByAWrittenNumber(both.Left).Concat(LimitedByAWrittenNumber(both.Right)),
        Binary { Operator: BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual } limit =>
            (limit.Left, limit.Right) switch
            {
                (Name name, Literal { Kind: LiteralKind.Integer }) => [name.Identifier],
                (Literal { Kind: LiteralKind.Integer }, Name name) => [name.Identifier],
                _ => [],
            },
        _ => [],
    };

    /// <summary>What the list is, in the message's words, when the code shows it is one that moves its items; null otherwise.</summary>
    private static string? Moving(IrProgram program, IrFunction function, CollectionEvidence evidence, string list) => program.Language switch
    {
        SourceLanguage.Python => evidence.Of(list) is { Lookup: Lookup.FromTheStart, Kind: "list" } ? "a list" : null,
        SourceLanguage.Java => AlwaysMadeAs(program, function, list, "ArrayList") ? "an ArrayList" : null,
        SourceLanguage.CSharp => AlwaysMadeAs(program, function, list, "List") ? "a List" : null,
        _ => null,
    };

    /// <summary>
    /// Whether every value the name can hold is of this class: every place the function gives it one declares it as the
    /// class or makes one with new - or, for a field the function never gives a value, the field is declared as the class.
    /// </summary>
    private static bool AlwaysMadeAs(IrProgram program, IrFunction function, string list, string made)
    {
        var definitions = CollectionEvidence.Definitions(function, list).ToList();
        if (definitions.Count == 0)
        {
            var field = program.Classes.Where(c => c.Name == function.Owner).SelectMany(c => c.Fields).FirstOrDefault(f => f.Name == list);
            return field?.Type.Name == made;
        }

        var giving = definitions.Where(d => d.IsParameter || d.Value is not null || !d.Understood).ToList();
        return giving.Count > 0 && giving.All(d => d.Declared?.Name == made || d.Understood && d.Value is NewObject { Type.Name: var type } && type == made);
    }

    private static string Message(FrontCall front, string kind, Stmt loop, SourceText source)
    {
        var code = source.Of(front.Call.Span);
        var shown = code.Length is > 0 and <= 80 ? code : front.Takes ? $"{front.List}.{front.Method}(0)" : $"{front.List}.{front.Method}(0, ...)";
        var moved = front.Takes
            ? $"takes the first item out of `{front.List}`, {kind}, and every item after it moves one place towards the front to close the gap"
            : $"puts an item in front of everything in `{front.List}`, {kind}, and every item already there moves one place along to make room";

        return $"`{shown}` {moved} - on every pass of the loop that begins on line {loop.Span.Line}, so the work grows with the number of " +
               $"passes times the length of `{front.List}`";
    }
}
