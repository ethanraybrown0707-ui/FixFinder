using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>One thing a function refuses: when <paramref name="Failure"/> holds of its arguments, it raises <paramref name="Raises"/>.</summary>
public sealed record Precondition(Expr Failure, string Raises, SourceSpan Span);

/// <summary>
/// Contracts: the checks a function makes on its arguments as it starts - <c>if x &lt; 0: raise ValueError</c>,
/// <c>assert n &gt; 0</c>, <c>Objects.requireNonNull(p)</c>, <c>ArgumentNullException.ThrowIfNull(p)</c>. They are its
/// preconditions, and every call the program makes to it can be checked against them.
/// </summary>
public static class Contracts
{
    public static IReadOnlyList<Precondition> Of(IrFunction function)
    {
        var parameters = function.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var found = new List<Precondition>();

        foreach (var statement in function.Body)
        {
            if (statement is Evaluate { Value: Literal { Kind: LiteralKind.Text } }) continue;

            if (Guard(statement, parameters) is not { } precondition) break;
            found.Add(precondition);
        }

        return found;
    }

    private static Precondition? Guard(Stmt statement, HashSet<string> parameters) => statement switch
    {
        If { Else.Count: 0, Then: [.., Throw thrown] then } guard
            when then.Take(then.Count - 1).All(s => s is Evaluate { Value: Call }) && ReadsOnly(guard.Condition, parameters) =>
            new Precondition(guard.Condition, Raised(thrown), guard.Span),

        AssertThat check when ReadsOnly(check.Condition, parameters) =>
            new Precondition(new Unary(check.Span, UnaryOperator.Not, check.Condition), "AssertionError", check.Span),

        Evaluate { Value: Call call } => Library(call, parameters),
        Assign { Value: Call call } => Library(call, parameters),
        _ => null,
    };

    /// <summary>The standard library's own argument checks.</summary>
    private static Precondition? Library(Call call, HashSet<string> parameters)
    {
        if (call is not { Callee: Member { Target: Name { Identifier: var type }, MemberName: var method }, Arguments: [{ Value: Name { Identifier: var checkedName } }, ..] } ||
            !parameters.Contains(checkedName)) return null;

        var name = new Name(call.Span, checkedName);
        Expr Compare(BinaryOperator op) => new Binary(call.Span, op, name, new Literal(call.Span, LiteralKind.Integer, 0L));

        return (type, method) switch
        {
            ("Objects", "requireNonNull") => new Precondition(new Binary(call.Span, BinaryOperator.Equal, name, new Literal(call.Span, LiteralKind.Null, null)),
                "NullPointerException", call.Span),
            ("ArgumentNullException", "ThrowIfNull") => new Precondition(new Binary(call.Span, BinaryOperator.Equal, name, new Literal(call.Span, LiteralKind.Null, null)),
                "ArgumentNullException", call.Span),
            ("ArgumentOutOfRangeException", "ThrowIfNegative") => new Precondition(Compare(BinaryOperator.Less), "ArgumentOutOfRangeException", call.Span),
            ("ArgumentOutOfRangeException", "ThrowIfNegativeOrZero") => new Precondition(Compare(BinaryOperator.LessOrEqual), "ArgumentOutOfRangeException", call.Span),
            ("ArgumentOutOfRangeException", "ThrowIfZero") => new Precondition(Compare(BinaryOperator.Equal), "ArgumentOutOfRangeException", call.Span),
            _ => null,
        };
    }

    /// <summary>A test that reads only the parameters - the functions it calls, like len or isinstance, aside.</summary>
    private static bool ReadsOnly(Expr condition, HashSet<string> parameters)
    {
        var read = Variables(condition).ToList();
        return read.Count > 0 && read.All(parameters.Contains);
    }

    private static IEnumerable<string> Variables(Expr expression) => expression switch
    {
        Name name => [name.Identifier],
        Call { Callee: Name { Identifier: "isinstance" or "issubclass" or "hasattr" or "callable" }, Arguments: [var tested, ..] } => Variables(tested.Value),
        Call call => call.Arguments.SelectMany(a => Variables(a.Value)).Concat(call.Callee is Member { Target: var owner } ? Variables(owner) : []),
        _ => IrWalk.Children(expression).SelectMany(Variables),
    };

    private static string Raised(Throw thrown) => thrown.Exception switch
    {
        NewObject created => created.Type.Name,
        Call { CalleeName: { } name } => name,
        Name name => name.Identifier,
        _ => "an error",
    };
}
