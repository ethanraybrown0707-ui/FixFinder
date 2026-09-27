using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>
/// What a JavaScript file gives other files: its ES exports, and what it assigns to CommonJS's module.exports and
/// exports. Only what the code plainly and always gives is kept. An export made on some ways through the code, inside a
/// function, or by a call that is handed the exports object may be something else by the time another file asks for
/// it, so it is not kept as any one value.
/// </summary>
internal static class JsModuleExports
{
    /// <summary>
    /// The exports of one file, from the ES exports its reader found and the statements of its top level. A name given two
    /// different values maps to null.
    /// </summary>
    public static IReadOnlyDictionary<string, Expr?> Of(IReadOnlyList<(string Key, Expr? Value)> declared, IReadOnlyList<Stmt> topLevel,
        IReadOnlyList<IrFunction> functions)
    {
        var exports = new Dictionary<string, Expr?>(StringComparer.Ordinal);
        foreach (var (key, value) in declared) Give(exports, key, value);
        foreach (var (key, value) in CommonJs(topLevel, functions)) Give(exports, key, value);
        return exports;
    }

    private static void Give(Dictionary<string, Expr?> exports, string key, Expr? value) =>
        exports[key] = exports.TryGetValue(key, out var given) && !Same(given, value) ? null : value;

    private static bool Same(Expr? first, Expr? second) => (first, second) switch
    {
        (Name one, Name other) => one.Identifier == other.Identifier,
        (Opaque one, Opaque other) => one.What == other.What && one.Span == other.Span,
        _ => false,
    };

    /// <summary>How an assignment reaches what the module exports, if it does.</summary>
    private enum Exporting { Nothing, WholeModule, OneName, ExportsReplaced, Unknown }

    /// <summary>
    /// The CommonJS exports: module.exports = ..., module.exports.name = ... and exports.name = ..., followed in the order
    /// the top level makes them. A new module.exports leaves behind what was given before it, and what exports.name gives
    /// after it, since exports still names the old object.
    /// </summary>
    private static Dictionary<string, Expr?> CommonJs(IReadOnlyList<Stmt> topLevel, IReadOnlyList<IrFunction> functions)
    {
        var exports = new Dictionary<string, Expr?>(StringComparer.Ordinal);
        var madePlainly = new HashSet<AssignValue>(ReferenceEqualityComparer.Instance);
        var exportsStillGives = true;

        foreach (var statement in topLevel)
        {
            if (statement is not Evaluate { Value: AssignValue assigned }) continue;

            var (how, key) = Reached(assigned.Target);
            if (how == Exporting.Nothing) continue;
            madePlainly.Add(assigned);

            switch (how)
            {
                case Exporting.WholeModule:
                    exports.Clear();
                    exportsStillGives = false;
                    GiveWholeModule(exports, assigned.Value);
                    break;

                case Exporting.OneName when ThroughExports(assigned.Target) && !exportsStillGives:
                    break;

                case Exporting.OneName:
                    Give(exports, key!, assigned.Value);
                    break;

                case Exporting.ExportsReplaced:
                    exportsStillGives = false;
                    break;

                default:
                    return [];
            }
        }

        // What is exported anywhere else - in a branch, a loop, a function - may or may not have happened.
        var elsewhere = functions.Select(function => function.Body).Append(topLevel)
            .SelectMany(IrWalk.Statements)
            .SelectMany(IrWalk.Expressions)
            .SelectMany(IrWalk.Within);

        foreach (var expression in elsewhere)
        {
            if (expression is Call call && call.Arguments.Any(argument => IsExportsObject(argument.Value))) return [];
            if (expression is not AssignValue assigned || madePlainly.Contains(assigned)) continue;

            var (how, key) = Reached(assigned.Target);
            if (how is Exporting.WholeModule or Exporting.ExportsReplaced or Exporting.Unknown) return [];
            if (how == Exporting.OneName) exports[key!] = null;
        }

        return exports;
    }

    private static void GiveWholeModule(Dictionary<string, Expr?> exports, Expr value)
    {
        if (value is not CollectionLiteral { Kind: CollectionKind.Dictionary, Keys: { } keys } literal)
        {
            Give(exports, ModuleImport.WholeModule, value);
            return;
        }

        // A name worked out as the program runs could be any of the others, so an object with one gives nothing certain.
        if (keys.Any(key => key is not Literal { Value: string })) return;

        for (var index = 0; index < keys.Count && index < literal.Items.Count; index++)
            Give(exports, (string)((Literal)keys[index]).Value!, literal.Items[index]);
    }

    /// <summary>What an assignment's target is, as far as the exports go, and the name it gives when it gives one.</summary>
    private static (Exporting How, string? Key) Reached(Expr target) => target switch
    {
        _ when IsModuleExports(target) => (Exporting.WholeModule, null),
        Name { Identifier: "exports" } => (Exporting.ExportsReplaced, null),
        Member { Target: var holder, MemberName: var key } when IsExportsObject(holder) => (Exporting.OneName, key),
        ElementAccess { Target: var holder, Key: Literal { Value: string key } } when IsExportsObject(holder) => (Exporting.OneName, key),
        ElementAccess { Target: var holder } when IsExportsObject(holder) => (Exporting.Unknown, null),
        _ => (Exporting.Nothing, null),
    };

    private static bool IsModuleExports(Expr expression) =>
        expression is Member { Target: Name { Identifier: "module" }, MemberName: "exports" };

    private static bool IsExportsObject(Expr expression) => IsModuleExports(expression) || expression is Name { Identifier: "exports" };

    /// <summary>exports.name = ..., rather than module.exports.name = ....</summary>
    private static bool ThroughExports(Expr target) => target switch
    {
        Member { Target: Name { Identifier: "exports" } } => true,
        ElementAccess { Target: Name { Identifier: "exports" } } => true,
        _ => false,
    };
}
