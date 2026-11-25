using System.Text.RegularExpressions;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// A count left out of step with what it counts: a class keeps a list and a number saying how many it holds, one method
/// changes both - <c>push</c> appends to <c>items</c> and adds 1 to <c>size</c> - and another changes only one -
/// <c>pop</c> takes an item out of <c>items</c> and leaves <c>size</c> as it was - so after it the number no longer says
/// how many there are. The rule the class keeps, that the two move together, is worked out from the method that keeps it.
/// </summary>
/// <remarks>
/// A list is changed by a call that adds an item - append, add, push, push_back, Add, Enqueue, offer, insert - or takes
/// one out - pop, remove, RemoveAt, Dequeue, poll, pop_back, shift - or, in Go, by giving it append's result or a slice of
/// itself; a number by adding or taking 1. The two are kept together when one method adds an item and 1, or takes one out
/// and 1. The number's name has to say it counts - size, count, length, len, num, top, or n - so a running total of pushes
/// is not taken for one. A method that calls another of the class's methods, which may keep the rule for it, a method
/// giving either a value of another kind - size = len(items), items = [] - and a constructor are left alone.
/// </remarks>
internal static partial class CountOutOfStep
{
    public const string CheckId = "analysis-count-out-of-step";

    private static readonly HashSet<string> Adding = new(StringComparer.Ordinal)
    {
        "append", "add", "push", "push_back", "emplace_back", "push_front", "emplace_front", "insert", "unshift", "appendleft",
        "offer", "offerFirst", "offerLast", "addFirst", "addLast", "enqueue", "Add", "Push", "Enqueue", "Insert", "AddFirst", "AddLast",
    };

    private static readonly HashSet<string> TakingOut = new(StringComparer.Ordinal)
    {
        "pop", "popleft", "pop_back", "pop_front", "remove", "discard", "erase", "shift", "poll", "pollFirst", "pollLast", "removeFirst",
        "removeLast", "dequeue", "Remove", "RemoveAt", "Pop", "Dequeue", "RemoveFirst", "RemoveLast",
    };

    private enum Kind { Added, TakenOut, CountedUp, CountedDown, GivenAnotherValue }

    /// <summary>One change a method makes to one of its object's fields, and where.</summary>
    private sealed record Change(string Field, Kind Kind, SourceSpan Span);

    public static IEnumerable<AnalysisFinding> Find(IrProgram program, string foundBy)
    {
        foreach (var type in program.Classes)
        {
            var fields = type.Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
            var methodNames = type.Methods.Select(method => method.Name).ToHashSet(StringComparer.Ordinal);
            var methods = type.Methods
                .Where(method => !method.IsConstructor && method.Name is not ("__init__" or "constructor") && method.Name != type.Name)
                .Select(method => (Method: method, Changes: ChangesOf(method, fields), Delegates: CallsItsOwn(method, methodNames)))
                .ToList();

            // The rule the class keeps: a list and a count one method changes together, the same way.
            var keptTogether = new List<(string Items, string Count, IrFunction KeptBy)>();
            foreach (var (method, changes, _) in methods)
                foreach (var items in changes.Where(change => change.Kind is Kind.Added or Kind.TakenOut).Select(change => change.Field).Distinct())
                    foreach (var count in changes.Where(change => change.Kind is Kind.CountedUp or Kind.CountedDown && Counts().IsMatch(change.Field)).Select(change => change.Field).Distinct())
                        if ((Has(changes, items, Kind.Added) && Has(changes, count, Kind.CountedUp)) || (Has(changes, items, Kind.TakenOut) && Has(changes, count, Kind.CountedDown)))
                            if (!keptTogether.Any(kept => kept.Items == items && kept.Count == count)) keptTogether.Add((items, count, method));

            foreach (var (items, count, keptBy) in keptTogether)
            {
                foreach (var (method, changes, delegates) in methods)
                {
                    if (ReferenceEquals(method, keptBy) || delegates) continue;
                    if (Has(changes, items, Kind.GivenAnotherValue) || Has(changes, count, Kind.GivenAnotherValue)) continue;

                    var added = Has(changes, items, Kind.Added);
                    var takenOut = Has(changes, items, Kind.TakenOut);
                    var countedUp = Has(changes, count, Kind.CountedUp);
                    var countedDown = Has(changes, count, Kind.CountedDown);

                    (Change? change, string what)? oneSided =
                        added && !countedUp ? (First(changes, items, Kind.Added), $"puts an item in `{items}` but leaves `{count}` as it was") :
                        takenOut && !countedDown ? (First(changes, items, Kind.TakenOut), $"takes an item out of `{items}` but leaves `{count}` as it was") :
                        countedUp && !added ? (First(changes, count, Kind.CountedUp), $"adds 1 to `{count}` but puts nothing in `{items}`") :
                        countedDown && !takenOut ? (First(changes, count, Kind.CountedDown), $"takes 1 off `{count}` but takes nothing out of `{items}`") :
                        null;
                    if (oneSided is not ({ } where, var said)) continue;

                    yield return new AnalysisFinding(CheckId, where.Span,
                        $"`{method.Name}` {said}, while `{keptBy.Name}` changes both - after it, `{count}` no longer says how many `{items}` holds",
                        Severity.Warning, Confidence.Likely, FindingKind.Logic, foundBy)
                    {
                        Function = method.FullName,
                    };
                }
            }
        }
    }

    private static bool Has(IReadOnlyList<Change> changes, string field, Kind kind) => changes.Any(change => change.Field == field && change.Kind == kind);

    private static Change First(IReadOnlyList<Change> changes, string field, Kind kind) =>
        changes.Where(change => change.Field == field && change.Kind == kind).OrderBy(change => change.Span.Line).First();

    /// <summary>Every change the method makes to its object's fields, in the order the code is written.</summary>
    private static List<Change> ChangesOf(IrFunction method, HashSet<string> fields)
    {
        var locals = IrWalk.LocalNames(method, assigningDeclares: false);
        var self = method.Parameters.FirstOrDefault()?.Name;
        string? FieldOf(Expr expression) => expression switch
        {
            Member { Target: Name { Identifier: var owner }, MemberName: var field } when owner is "this" or "self" || owner == self => field,
            Name { Identifier: var name } when fields.Contains(name) && !locals.Contains(name) => name,
            _ => null,
        };

        var changes = new List<Change>();
        foreach (var expression in IrWalk.Statements(method.Body).SelectMany(statement => StatementParts(statement)).SelectMany(IrWalk.Within))
        {
            switch (expression)
            {
                case Call { Callee: Member { Target: var target, MemberName: var called } } call when FieldOf(target) is { } field:
                    if (Adding.Contains(called)) changes.Add(new Change(field, Kind.Added, call.Span));
                    else if (TakingOut.Contains(called)) changes.Add(new Change(field, Kind.TakenOut, call.Span));
                    break;

                case AssignValue { Target: var target, Value: var value } assigned when FieldOf(target) is { } field:
                    changes.Add(new Change(field, KindOf(field, value, null, FieldOf), assigned.Span));
                    break;
            }
        }

        foreach (var statement in IrWalk.Statements(method.Body))
            if (statement is Assign { Target: var target, Value: var value, Compound: var compound } && FieldOf(target) is { } field)
                changes.Add(new Change(field, KindOf(field, value, compound, FieldOf), statement.Span));

        return [.. changes.OrderBy(change => change.Span.Line)];
    }

    /// <summary>The expressions a statement holds - its own, and an assignment's value, which IrWalk keeps as its own.</summary>
    private static IEnumerable<Expr> StatementParts(Stmt statement) => IrWalk.Expressions(statement);

    /// <summary>What giving a field a value does to it: 1 added or taken off, an item added or taken out, or another value.</summary>
    private static Kind KindOf(string field, Expr value, BinaryOperator? compound, Func<Expr, string?> fieldOf) => (compound, value) switch
    {
        (BinaryOperator.Add, Literal { Value: 1L or 1 }) => Kind.CountedUp,
        (BinaryOperator.Subtract, Literal { Value: 1L or 1 }) => Kind.CountedDown,
        (null, Binary { Operator: BinaryOperator.Add, Left: var left, Right: Literal { Value: 1L or 1 } }) when fieldOf(left) == field => Kind.CountedUp,
        (null, Binary { Operator: BinaryOperator.Subtract, Left: var left, Right: Literal { Value: 1L or 1 } }) when fieldOf(left) == field => Kind.CountedDown,
        (null, Binary { Operator: BinaryOperator.Add, Left: var left, Right: CollectionLiteral }) when fieldOf(left) == field => Kind.Added,
        (null, Slice { Target: var sliced }) when fieldOf(sliced) == field => Kind.TakenOut,
        _ => Kind.GivenAnotherValue,
    };

    /// <summary>Whether the method calls another of its class's methods, which may keep the rule for it.</summary>
    private static bool CallsItsOwn(IrFunction method, HashSet<string> methodNames) =>
        IrWalk.Statements(method.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Within)
            .Any(expression => expression is Call call && CalledName(call) is { } called && methodNames.Contains(called) && called != method.Name);

    /// <summary>The method a call names on its own object - this.name(), self.name(), or a bare name() - or null.</summary>
    private static string? CalledName(Call call) => call.Callee switch
    {
        Member { Target: Name { Identifier: "this" or "self" }, MemberName: var called } => called,
        Name { Identifier: var called } => called,
        _ => null,
    };

    /// <summary>A name that says it counts: size, count, length, len, num, top, n.</summary>
    [GeneratedRegex(@"(?i)size|count|length|len|num|top|^n$")]
    private static partial Regex Counts();
}
