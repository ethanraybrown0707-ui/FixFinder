namespace FixFinder.Core.Analysis.Ir;

/// <summary>Walks the IR: every statement inside a block, every expression inside a statement, every name inside an expression.</summary>
public static class IrWalk
{
    public static IEnumerable<Stmt> Statements(IEnumerable<Stmt> block)
    {
        foreach (var statement in block)
        {
            yield return statement;

            foreach (var inner in Statements(Children(statement)))
                yield return inner;
        }
    }

    private static IEnumerable<Stmt> Children(Stmt statement) => statement switch
    {
        If branch => branch.Then.Concat(branch.Else),
        While loop => loop.Body.Concat(loop.Else),
        For loop => loop.Setup.Concat(loop.Body).Concat(loop.Step),
        ForEach loop => loop.Body.Concat(loop.Else),
        Try attempt => attempt.Body.Concat(attempt.Handlers.SelectMany(h => h.Body)).Concat(attempt.Else).Concat(attempt.Finally),
        Switch choice => choice.Cases.SelectMany(c => c.Body),
        Using used => used.Body,
        _ => [],
    };

    /// <summary>The expressions a statement itself holds, not those of statements nested in it.</summary>
    public static IEnumerable<Expr> Expressions(Stmt statement)
    {
        Expr?[] held = statement switch
        {
            Evaluate evaluate => [evaluate.Value],
            Assign assign => [assign.Target, assign.Value],
            Declare declare => [declare.Initial],
            If branch => [branch.Condition],
            While loop => [loop.Condition],
            For loop => [loop.Condition],
            ForEach loop => [loop.Target, loop.Items],
            Return leave => [leave.Value],
            Throw raise => [raise.Exception],
            AssertThat check => [check.Condition, check.Message],
            Switch choice => [choice.Subject, .. choice.Cases.SelectMany(c => c.Labels)],
            Using used => [used.Resource, used.Variable],
            OpaqueStmt opaque => [.. opaque.Parts],
            _ => [],
        };

        return held.OfType<Expr>();
    }

    public static IEnumerable<Expr> Children(Expr expression) => expression switch
    {
        Unary unary => [unary.Operand],
        Binary binary => [binary.Left, binary.Right],
        Conditional choice => [choice.Test, choice.WhenTrue, choice.WhenFalse],
        Call call => [call.Callee, .. call.Arguments.Select(a => a.Value)],
        Member member => [member.Target],
        ElementAccess element => [element.Target, element.Key],
        Slice slice => new[] { slice.Target, slice.Lower, slice.Upper, slice.Step }.OfType<Expr>(),
        NewObject created => created.Arguments.Select(a => a.Value),
        CollectionLiteral collection => collection.Items.Concat(collection.Keys ?? []),
        AssignValue assigned => [assigned.Target, assigned.Value],
        MoreItems more => [more.Items],
        NextItem next => [next.Items],
        Opaque opaque => opaque.Parts,
        _ => [],
    };

    public static IEnumerable<string> Names(Expr expression) =>
        expression is Name name ? [name.Identifier] : Children(expression).SelectMany(Names);

    /// <summary>The names a function binds for itself: its parameters and everything it assigns, less what it declares as outer.</summary>
    public static HashSet<string> LocalNames(IrFunction function)
    {
        var locals = function.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var statement in Statements(function.Body))
        {
            var bound = statement switch
            {
                Assign assign => Bound(assign.Target),
                Declare declare => [declare.Variable],
                ForEach loop => Bound(loop.Target),
                Using { Variable: { } variable } => Bound(variable),
                Try attempt => attempt.Handlers.Select(h => h.Variable).OfType<string>(),
                OpaqueStmt opaque => opaque.MayAssign,
                _ => [],
            };

            locals.UnionWith(bound);
            locals.UnionWith(Expressions(statement).SelectMany(AssignedInside).SelectMany(Bound));
        }

        locals.ExceptWith(function.OuterNames);
        return locals;
    }

    private static IEnumerable<Expr> AssignedInside(Expr expression) =>
        expression is AssignValue assigned ? [assigned.Target, .. AssignedInside(assigned.Value)] : Children(expression).SelectMany(AssignedInside);

    private static IEnumerable<string> Bound(Expr target) => target switch
    {
        Name name => [name.Identifier],
        CollectionLiteral unpacked => unpacked.Items.SelectMany(Bound),
        _ => [],
    };

    /// <summary>Names a function uses without binding them itself: they belong to an enclosing function or the module.</summary>
    public static HashSet<string> FreeNames(IrFunction function)
    {
        var used = Statements(function.Body).SelectMany(Expressions).SelectMany(Names).ToHashSet(StringComparer.Ordinal);
        used.UnionWith(function.OuterNames);
        used.ExceptWith(LocalNames(function));
        return used;
    }
}
