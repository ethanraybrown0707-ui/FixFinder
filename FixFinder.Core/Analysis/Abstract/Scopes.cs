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
    public static HashSet<string> Volatile(IrProgram program, IrFunction function) => new Nesting(program).Volatile(function);

    private static bool SameFile(IrFunction a, IrFunction b) => string.Equals(a.Span.File, b.Span.File, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Collections that are stored, returned or handed to code FixFinder cannot see - after which their length can change
    /// without this function saying so.
    /// </summary>
    /// <remarks>
    /// b = a between two of the function's own variables is not a way out: the evaluator follows both names as one
    /// object. But it is one object, so if either name escapes, both do.
    /// </remarks>
    public static HashSet<string> Escaping(IrFunction function)
    {
        var escaping = new HashSet<string>(StringComparer.Ordinal);
        var sharing = new List<(string Name, string Of)>();

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

                // A comprehension walks over what it is given, as a for loop does, and what it makes is a new collection.
                case Opaque { What: "generator" or "list comprehension" or "set comprehension" or "dictionary comprehension" } comprehension:
                    foreach (var walked in comprehension.Parts) Visit(walked, true);
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
                case Assign { Target: Name { Identifier: var name }, Value: Name { Identifier: var of } }:
                    sharing.Add((name, of));
                    break;
                case Declare { Variable: var name, Initial: Name { Identifier: var of } }:
                    sharing.Add((name, of));
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

        // An object escapes through any of its names.
        for (var spreading = true; spreading;)
        {
            spreading = false;
            foreach (var (name, of) in sharing)
            {
                if (escaping.Contains(name) == escaping.Contains(of)) continue;
                escaping.Add(name);
                escaping.Add(of);
                spreading = true;
            }
        }

        return escaping;
    }
}
