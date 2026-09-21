using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Solver;

namespace FixFinder.Core.Analysis.Symbolic;

/// <summary>A loop that moves one counter by a fixed step towards a limit the loop does not change.</summary>
public sealed record CountedLoop(string Counter, Rational Step, BinaryOperator Comparison, Expr Limit);

/// <summary>
/// Works out how loops are bounded from the code alone: which count a fixed number of times, and which can never end
/// once they start, because nothing the condition reads can change inside them and nothing inside leaves them.
/// </summary>
public static class LoopBounds
{
    /// <summary>The counted while and for loops of a function, by their condition.</summary>
    public static Dictionary<Expr, CountedLoop> Counted(IrFunction function)
    {
        var counted = new Dictionary<Expr, CountedLoop>(ReferenceEqualityComparer.Instance);

        foreach (var statement in IrWalk.Statements(function.Body))
        {
            var (condition, steps) = statement switch
            {
                While { TestsFirst: true } loop => (loop.Condition, loop.Body),
                For { Condition: { } test } loop => (test, loop.Step),
                _ => (null, []),
            };

            if (condition is Binary { Left: Name { Identifier: var counter } } comparison && Comparing(comparison.Operator) &&
                !IrWalk.Names(comparison.Right).Contains(counter) && StepOf(counter, steps, statement) is { } step &&
                Unchanged(IrWalk.Names(comparison.Right), statement))
            {
                counted[condition] = new CountedLoop(counter, step, comparison.Operator, comparison.Right);
            }
        }

        return counted;
    }

    /// <summary>The statements that run each time round a loop: its body, and a for loop's step - not its setup or else.</summary>
    private static List<Stmt> Inside(Stmt loop) => loop switch
    {
        While whileLoop => IrWalk.Statements(whileLoop.Body).ToList(),
        For forLoop => IrWalk.Statements(forLoop.Body.Concat(forLoop.Step)).ToList(),
        _ => [],
    };

    private static bool Comparing(BinaryOperator op) =>
        op is BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual or BinaryOperator.NotEqual;

    /// <summary>
    /// The one fixed step the counter takes each time round: exactly one assignment to it in the whole loop, written at
    /// the top level of the step (or of a while loop's body, with no continue that could skip it).
    /// </summary>
    private static Rational? StepOf(string counter, IReadOnlyList<Stmt> steps, Stmt loop)
    {
        var inside = Inside(loop);
        var stepped = steps.Select(s => Step(counter, s)).OfType<Rational>().ToList();

        if (stepped.Count != 1 || stepped[0].IsZero || inside.Count(s => Assigns(s, counter)) != 1) return null;
        if (loop is While && inside.Any(s => s is Continue)) return null;

        return stepped[0];
    }

    private static Rational? Step(string counter, Stmt statement) => statement switch
    {
        Assign { Target: Name { Identifier: var name }, Compound: { } op, Value: Literal { Kind: LiteralKind.Integer } step }
            when name == counter && op is BinaryOperator.Add or BinaryOperator.Subtract => Signed(op, step),
        Assign { Target: Name { Identifier: var name }, Compound: null, Value: Binary { Left: Name { Identifier: var read }, Right: Literal { Kind: LiteralKind.Integer } step } sum }
            when name == counter && read == counter && sum.Operator is BinaryOperator.Add or BinaryOperator.Subtract => Signed(sum.Operator, step),
        Evaluate { Value: AssignValue { Target: Name { Identifier: var name }, Value: Binary { Left: Name { Identifier: var read }, Right: Literal { Kind: LiteralKind.Integer } step } sum } }
            when name == counter && read == counter && sum.Operator is BinaryOperator.Add or BinaryOperator.Subtract => Signed(sum.Operator, step),
        _ => null,
    };

    private static Rational Signed(BinaryOperator op, Literal step) =>
        op == BinaryOperator.Add ? Convert.ToInt64(step.Value) : -Convert.ToInt64(step.Value);

    private static bool Assigns(Stmt statement, string name) =>
        Bound(statement).Contains(name) || IrWalk.Expressions(statement).SelectMany(AssignedInside).Contains(name);

    private static IEnumerable<string> Bound(Stmt statement) => statement switch
    {
        Assign { Target: var target } => Targets(target),
        Declare declare => [declare.Variable],
        ForEach loop => Targets(loop.Target),
        Using { Variable: { } variable } => Targets(variable),
        OpaqueStmt opaque => opaque.MayAssign,
        Try attempt => attempt.Handlers.Select(h => h.Variable).OfType<string>(),
        _ => [],
    };

    private static IEnumerable<string> Targets(Expr target) => target switch
    {
        Name name => [name.Identifier],
        Member { Target: Name { Identifier: "this" }, MemberName: var field } => [field],
        CollectionLiteral unpacked => unpacked.Items.SelectMany(Targets),
        _ => [],
    };

    private static IEnumerable<string> AssignedInside(Expr expression) =>
        expression is AssignValue assigned
            ? [.. Targets(assigned.Target), .. AssignedInside(assigned.Value)]
            : IrWalk.Children(expression).SelectMany(AssignedInside);

    /// <summary>
    /// Whether none of the names can change while the loop runs: nothing in it assigns them, and - for the ones whose
    /// contents matter, like a list whose length is tested - nothing hands them to a call or calls a method on them.
    /// </summary>
    private static bool Unchanged(IEnumerable<string> names, Stmt loop, IReadOnlySet<string>? contents = null)
    {
        var watched = names.ToHashSet(StringComparer.Ordinal);
        if (watched.Count == 0) return true;

        var inside = Inside(loop);
        if (inside.Any(s => Bound(s).Any(watched.Contains))) return false;

        var held = contents ?? watched;
        return !inside.SelectMany(IrWalk.Expressions).Any(e => ChangesAny(e, watched, held));
    }

    private static bool ChangesAny(Expr expression, HashSet<string> watched, IReadOnlySet<string> contents) => expression switch
    {
        AssignValue assigned when Targets(assigned.Target).Any(watched.Contains) => true,
        Call { Callee: Member { Target: Name { Identifier: var receiver } } } when contents.Contains(receiver) => true,
        Call call when call.Arguments.Any(a => a.Value is Name { Identifier: var passed } && contents.Contains(passed)) => true,
        _ => IrWalk.Children(expression).Any(child => ChangesAny(child, watched, contents)),
    };

    /// <summary>
    /// The names a condition reads for what they hold rather than as a value compared: len(x), x.Length, or x tested for
    /// truth on its own. Only those can change when x is handed to a call.
    /// </summary>
    private static HashSet<string> ContentsReadBy(Expr condition)
    {
        var contents = new HashSet<string>(StringComparer.Ordinal);

        void Visit(Expr expression, bool compared)
        {
            switch (expression)
            {
                case Name { Identifier: var name } when !compared:
                    contents.Add(name);
                    break;
                case Member { Target: Name { Identifier: var sized } }:
                    contents.Add(sized);
                    break;
                case Call { Arguments: [{ Value: Name { Identifier: var measured } }] }:
                    contents.Add(measured);
                    break;
                case Binary { Operator: BinaryOperator.And or BinaryOperator.Or } both:
                    Visit(both.Left, false);
                    Visit(both.Right, false);
                    break;
                case Unary { Operator: UnaryOperator.Not } negation:
                    Visit(negation.Operand, false);
                    break;
                default:
                    foreach (var child in IrWalk.Children(expression)) Visit(child, true);
                    break;
            }
        }

        Visit(condition, false);
        return contents;
    }

    /// <summary>
    /// While and for loops that, once their condition is true, stay in the loop for ever: the condition reads only the
    /// function's own variables and no calls, nothing in the loop changes those variables, and nothing leaves the loop.
    /// </summary>
    public static IEnumerable<(Stmt Loop, Expr Condition)> NeverEnding(IrFunction function, IReadOnlySet<string> locals)
    {
        foreach (var statement in IrWalk.Statements(function.Body))
        {
            var condition = statement switch
            {
                While loop => loop.Condition,
                For { Condition: { } test } => test,
                _ => null,
            };

            if (condition is null || IsDeliberate(condition) || !OnlyReads(condition, locals)) continue;

            var names = IrWalk.Names(condition).ToHashSet(StringComparer.Ordinal);
            if (names.Count == 0 || !Unchanged(names, statement, ContentsReadBy(condition))) continue;

            var inside = Inside(statement);
            if (inside.Any(s => s is Break or Return or Throw) || inside.SelectMany(IrWalk.Expressions).Any(Leaves)) continue;

            yield return (statement, condition);
        }
    }

    private static bool IsDeliberate(Expr condition) => condition switch
    {
        Literal => true,
        Name { Identifier: var name } => name.Length > 1 && name.All(c => char.IsUpper(c) || c == '_' || char.IsDigit(c)),
        Unary { Operand: var inner } => IsDeliberate(inner),
        _ => false,
    };

    /// <summary>A condition built only from the function's own variables, literals, operators and their sizes.</summary>
    private static bool OnlyReads(Expr condition, IReadOnlySet<string> locals) => condition switch
    {
        Literal => true,
        Name { Identifier: var name } => locals.Contains(name),
        Unary unary => OnlyReads(unary.Operand, locals),
        Binary { Operator: not (BinaryOperator.In or BinaryOperator.NotIn) } binary => OnlyReads(binary.Left, locals) && OnlyReads(binary.Right, locals),
        Member { Target: Name { Identifier: var sized }, MemberName: "length" or "Length" or "Count" } => locals.Contains(sized),
        Call { Callee: Name { Identifier: "len" }, Arguments: [{ Value: Name { Identifier: var sized } }] } => locals.Contains(sized),
        _ => false,
    };

    private static bool Leaves(Expr expression) => expression switch
    {
        Call { CalleeName: "exit" or "quit" or "_exit" or "Exit" or "FailFast" } => true,
        Opaque { What: "yield" or "await" } => true,
        _ => IrWalk.Children(expression).Any(Leaves),
    };
}
