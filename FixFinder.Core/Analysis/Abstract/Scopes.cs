using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Abstract;

/// <summary>Which variables a function cannot follow on its own, because other code can see and change them.</summary>
public static class Scopes
{
    private static readonly HashSet<string> HarmlessBuiltins = new(StringComparer.Ordinal)
    {
        "len", "sum", "sorted", "list", "tuple", "set", "min", "max", "any", "all", "enumerate", "zip", "reversed", "print",
        "str", "repr", "bool", "iter", "isinstance", "type", "id", "hash", "dict", "frozenset", "format",
    };

    /// <summary>
    /// Variables of <paramref name="function"/> that a function written inside it uses - or, for the module, that any function uses.
    /// Those can change during any call, so they are never treated as known.
    /// </summary>
    public static HashSet<string> Volatile(IrProgram program, IrFunction function)
    {
        var shared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var other in program.AllFunctions.Where(f => !ReferenceEquals(f, function) && IsInside(program, f, function)))
            shared.UnionWith(IrWalk.FreeNames(other));

        // A variable whose address was taken can be changed through that pointer by any call it was handed to.
        shared.UnionWith(function.AddressTaken);
        shared.IntersectWith(IrWalk.LocalNames(function));
        return shared;
    }

    private static bool IsInside(IrProgram program, IrFunction inner, IrFunction outer)
    {
        if (outer.Name == IrFunction.ModuleBody) return inner.Name != IrFunction.ModuleBody && SameFile(inner, outer);

        var byName = program.AllFunctions.GroupBy(f => f.FullName).ToDictionary(g => g.Key, g => g.First());
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var parent = inner.EnclosedBy; parent is not null && parent != IrFunction.ModuleBody; parent = byName.GetValueOrDefault(parent)?.EnclosedBy)
        {
            if (parent == outer.FullName) return true;
            if (!seen.Add(parent)) break;
        }

        return false;
    }

    private static bool SameFile(IrFunction a, IrFunction b) => string.Equals(a.Span.File, b.Span.File, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Collections that are aliased, stored, returned or handed to code FixFinder cannot see - after which their length
    /// can change without this function saying so.
    /// </summary>
    public static HashSet<string> Escaping(IrFunction function)
    {
        var escaping = new HashSet<string>(StringComparer.Ordinal);

        void Visit(Expr expression, bool safe)
        {
            switch (expression)
            {
                case Name name:
                    if (!safe) escaping.Add(name.Identifier);
                    break;
                case Member { MemberName: "Length" or "Count" or "length" } measured:
                    Visit(measured.Target, true);
                    break;
                case Call { Callee: Member { Target: var receiver } } method:
                    Visit(receiver, true);
                    foreach (var argument in method.Arguments) Visit(argument.Value, false);
                    break;
                case Call { Callee: Name { Identifier: var builtin } } call when HarmlessBuiltins.Contains(builtin):
                    foreach (var argument in call.Arguments) Visit(argument.Value, true);
                    break;
                case ElementAccess element:
                    Visit(element.Target, true);
                    Visit(element.Key, false);
                    break;
                case Slice slice:
                    Visit(slice.Target, true);
                    foreach (var part in new[] { slice.Lower, slice.Upper, slice.Step }.OfType<Expr>()) Visit(part, false);
                    break;
                case Binary or Unary or MoreItems or NextItem:
                    foreach (var child in IrWalk.Children(expression)) Visit(child, true);
                    break;
                case Conditional choice:
                    Visit(choice.Test, true);
                    Visit(choice.WhenTrue, safe);
                    Visit(choice.WhenFalse, safe);
                    break;
                default:
                    foreach (var child in IrWalk.Children(expression)) Visit(child, false);
                    break;
            }
        }

        foreach (var statement in IrWalk.Statements(function.Body))
        {
            switch (statement)
            {
                case ForEach loop:
                    Visit(loop.Items, true);
                    break;
                case If or While or AssertThat or For:
                    foreach (var expression in IrWalk.Expressions(statement)) Visit(expression, true);
                    break;
                case Assign { Target: Name } assign:
                    Visit(assign.Value, false);
                    break;
                case Assign assign:
                    Visit(assign.Target, true);
                    Visit(assign.Value, false);
                    break;
                default:
                    foreach (var expression in IrWalk.Expressions(statement)) Visit(expression, false);
                    break;
            }
        }

        return escaping;
    }
}
