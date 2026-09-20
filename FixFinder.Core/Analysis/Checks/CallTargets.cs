using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>A function of the program that a call runs; <paramref name="Bound"/> when the call supplies its first parameter (self).</summary>
public sealed record CallTarget(IrFunction Function, bool Bound)
{
    /// <summary>
    /// A method a subclass can replace, so what this one returns says nothing certain about what the call returns. Only
    /// top-level, nested and static functions are certainly the code that runs.
    /// </summary>
    public bool Overridable => Function.Owner is not null && !Function.IsStatic && Function.EnclosedBy is null or IrFunction.ModuleBody;

    /// <summary>The parameters the call's own arguments fill.</summary>
    public IReadOnlyList<IrParameter> Parameters => Bound ? Function.Parameters.Skip(1).ToList() : Function.Parameters;
}

/// <summary>
/// Which of the program's own functions a call runs, where the code says so plainly: a function called by name, a
/// method called on self or this, a class's static method, a Python class called to make an object. Anything
/// ambiguous - two functions with the name, overloads with the same number of parameters - is left unresolved.
/// </summary>
public sealed class CallTargets(IrProgram program)
{
    private readonly ILookup<string, IrFunction> _topLevel = program.Functions
        .Where(f => f.Owner is null && f.Name != IrFunction.ModuleBody && f.EnclosedBy is null or IrFunction.ModuleBody)
        .ToLookup(f => f.Name, StringComparer.Ordinal);

    private readonly ILookup<string, IrFunction> _nested = program.Functions
        .Where(f => f.EnclosedBy is { } outer && outer != IrFunction.ModuleBody)
        .ToLookup(f => $"{f.EnclosedBy}/{f.Name}", StringComparer.Ordinal);

    private readonly ILookup<string, IrClass> _classes = program.Classes.ToLookup(c => c.Name, StringComparer.Ordinal);

    private bool IsPython => program.Language == SourceLanguage.Python;

    public CallTarget? Resolve(Call call, IrFunction caller, IReadOnlySet<string> callerLocals)
    {
        var arguments = call.Arguments.Count;

        switch (call.Callee)
        {
            case Name { Identifier: var name }:
                if (Single(_nested[$"{caller.FullName}/{name}"]) is { } nested) return new CallTarget(nested, false);
                if (callerLocals.Contains(name) && caller.Name != IrFunction.ModuleBody) return null;

                if (IsPython)
                {
                    if (Single(_topLevel[name]) is { } function) return new CallTarget(function, false);
                    if (Single(_classes[name]) is { } made && Single(made.Methods.Where(m => m.Name == "__init__")) is { } init) return new CallTarget(init, true);
                    return null;
                }

                // Go calls the package's own functions by name, whichever file they are written in.
                if (program.Language == SourceLanguage.Go)
                    return Single(_topLevel[name].Where(f => f.Parameters.Count == arguments)) is { } packaged ? new CallTarget(packaged, false) : null;

                return caller.Owner is { } within ? Method(within, name, arguments, bound: false) : null;

            case Member { Target: Name { Identifier: "self" or "cls" or "this" }, MemberName: var method } when caller.Owner is { } owner:
                return Method(owner, method, arguments, bound: IsPython);

            case Member { Target: Name { Identifier: var type }, MemberName: var method } when !callerLocals.Contains(type) && _classes[type].Any():
                return Method(type, method, arguments, bound: false) is { Function.IsStatic: true } found ? found : null;

            default:
                return null;
        }
    }

    private CallTarget? Method(string owner, string name, int arguments, bool bound)
    {
        if (Single(_classes[owner]) is not { } type) return null;

        var named = type.Methods.Where(m => m.Name == name).ToList();
        if (IsPython) return Single(named) is { } method ? new CallTarget(method, bound && !method.IsStatic) : null;

        return Single(named.Where(m => m.Parameters.Count == arguments)) is { } overload ? new CallTarget(overload, false) : null;
    }

    private static T? Single<T>(IEnumerable<T> candidates) where T : class
    {
        using var each = candidates.GetEnumerator();
        if (!each.MoveNext()) return null;
        var first = each.Current;
        return each.MoveNext() ? null : first;
    }
}
