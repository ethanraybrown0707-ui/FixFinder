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
/// method called on self or this, a class's static method, a Python class called to make an object - in the caller's
/// file or, through what its module imports, in another of the program's files. Anything ambiguous - two functions with
/// the name, overloads with the same number of parameters, a name set again - is left unresolved.
/// </summary>
public sealed class CallTargets(IrProgram program)
{
    private JavaScriptModules? _javaScript;
    private PythonModules? _python;
    private readonly Dictionary<IrFunction, HashSet<string>> _localsOf = new(ReferenceEqualityComparer.Instance);

    private JavaScriptModules JavaScript => _javaScript ??= new JavaScriptModules(program);

    private PythonModules Python => _python ??= new PythonModules(program);

    private readonly ILookup<string, IrFunction> _topLevel = program.Functions
        .Where(f => f.Owner is null && f.Name != IrFunction.ModuleBody && f.EnclosedBy is null or IrFunction.ModuleBody)
        .ToLookup(f => f.Name, StringComparer.Ordinal);

    private readonly ILookup<string, IrFunction> _nested = program.Functions
        .Where(f => f.EnclosedBy is { } outer && outer != IrFunction.ModuleBody)
        .ToLookup(f => $"{f.EnclosedBy}/{f.Name}", StringComparer.Ordinal);

    private readonly ILookup<string, IrClass> _classes = program.Classes.ToLookup(c => c.Name, StringComparer.Ordinal);

    private readonly ILookup<string, IrFunction> _byFullName = program.AllFunctions.ToLookup(f => f.FullName, StringComparer.Ordinal);

    private bool IsPython => program.Language == SourceLanguage.Python;

    /// <summary>
    /// The function a lambda or nested function is written in. Overloads share a name, so it is the one whose code
    /// contains this one's.
    /// </summary>
    public IrFunction? Enclosing(IrFunction function)
    {
        if (function.EnclosedBy is not { } outer) return null;

        var named = _byFullName[outer].Where(f => !ReferenceEquals(f, function)).ToList();
        return named.FirstOrDefault(f => Contains(f.Span, function.Span)) ?? (named.Count == 1 ? named[0] : null);
    }

    private static bool Contains(SourceSpan outer, SourceSpan inner) =>
        outer.File == inner.File && (outer.Line, outer.Column).CompareTo((inner.Line, inner.Column)) <= 0 &&
        (outer.EndLine, outer.EndColumn).CompareTo((inner.EndLine, inner.EndColumn)) >= 0;

    /// <summary>
    /// The class whose methods a function calls by name or on this and self: its own, or - for a lambda or a function
    /// written inside a method - the class of that method, whose object it still has.
    /// </summary>
    public string? ClassOf(IrFunction function)
    {
        if (_classOf.TryGetValue(function, out var known)) return known;

        string? found = null;
        var current = function;

        for (var depth = 0; current is not null && depth < MostNesting; depth++)
        {
            if (current.Owner is { } owner && _classes[owner].Any())
            {
                found = owner;
                break;
            }

            current = Enclosing(current);
        }

        return _classOf[function] = found;
    }

    /// <summary>Worked out once per function: every call a function makes asks, and abstract interpretation asks on every pass.</summary>
    private readonly Dictionary<IrFunction, string?> _classOf = new(ReferenceEqualityComparer.Instance);

    /// <summary>Lambdas inside lambdas are rarely more than a few deep; this only stops a malformed program looping.</summary>
    private const int MostNesting = 32;

    public CallTarget? Resolve(Call call, IrFunction caller, IReadOnlySet<string> callerLocals)
    {
        var arguments = call.Arguments.Count;

        switch (call.Callee)
        {
            case Name { Identifier: var name }:
                if (Single(_nested[$"{caller.FullName}/{name}"]) is { } nested) return new CallTarget(nested, false);
                if (callerLocals.Contains(name) && caller.Name != IrFunction.ModuleBody) return null;

                return program.Language switch
                {
                    SourceLanguage.Python => Around(caller, name) is { Bound: true } around ? around.Inside : Python.Named(name, caller, call.Span),
                    SourceLanguage.JavaScript => Around(caller, name) is { Bound: true } around ? around.Inside : JavaScript.Named(name, caller, call.Span),

                    // Go calls the package's own functions by name, whichever file they are written in.
                    SourceLanguage.Go => Single(_topLevel[name].Where(f => f.Parameters.Count == arguments)) is { } packaged ? new CallTarget(packaged, false) : null,

                    SourceLanguage.C or SourceLanguage.Cpp => Native(name, arguments, caller),
                    _ => ClassOf(caller) is { } within ? Method(within, name, arguments, bound: false) : null,
                };

            case Member { Target: Name { Identifier: "self" or "cls" or "this" }, MemberName: var method } when ClassOf(caller) is { } owner:
                return Method(owner, method, arguments, bound: IsPython);

            case Member { Target: Name { Identifier: var type }, MemberName: var method } when !callerLocals.Contains(type) && _classes[type].Any():
                return Method(type, method, arguments, bound: false) is { Function.IsStatic: true } found ? found : null;

            // helpers.total(): a function of another module, reached through the module.
            case Member { Target: var holder, MemberName: var key } when program.Language is SourceLanguage.Python or SourceLanguage.JavaScript &&
                                                                         !HeldLocally(holder, caller, callerLocals):
                return IsPython ? Python.Member(holder, key, caller, call.Span) : JavaScript.Member(holder, key, caller, call.Span);

            default:
                return null;
        }
    }

    /// <summary>
    /// What a name means to a function written inside others, before the module is asked: Bound when one of the
    /// functions around it binds the name - with the function it defines under that name, when that is what it binds.
    /// </summary>
    private (bool Bound, CallTarget? Inside) Around(IrFunction caller, string name)
    {
        var current = Enclosing(caller);
        for (var depth = 0; current is not null && current.Name != IrFunction.ModuleBody && depth < MostNesting; depth++)
        {
            if (Single(_nested[$"{current.FullName}/{name}"]) is { } defined) return (true, new CallTarget(defined, false));
            if (LocalsOf(current).Contains(name)) return (true, null);
            current = Enclosing(current);
        }

        return (false, null);
    }

    /// <summary>Whether the name an expression starts from belongs to the caller, or to a function around it, rather than to the module.</summary>
    private bool HeldLocally(Expr holder, IrFunction caller, IReadOnlySet<string> callerLocals) => holder switch
    {
        Name { Identifier: var name } => callerLocals.Contains(name) && caller.Name != IrFunction.ModuleBody || Around(caller, name).Bound,
        Member { Target: var outer } => HeldLocally(outer, caller, callerLocals),
        _ => false,
    };

    private HashSet<string> LocalsOf(IrFunction function) =>
        _localsOf.TryGetValue(function, out var known) ? known : _localsOf[function] = IrWalk.LocalNames(function, assigningDeclares: IsPython);

    /// <summary>
    /// A C or C++ function called by name. In a C++ method the class's own methods come first, as C++ looks there before
    /// anywhere else. Otherwise it is the one free function with that name that takes that many arguments, among those
    /// in the caller's own file - where a static function hides any other - or, when that file has none of the name, in
    /// the rest of the program. Two that could both be meant are overloads or rivals, and neither is chosen.
    /// </summary>
    private CallTarget? Native(string name, int arguments, IrFunction caller)
    {
        if (ClassOf(caller) is { } within)
        {
            if (_classes[within].Any(type => type.Methods.Any(method => method.Name == name))) return Method(within, name, arguments, bound: false);

            // A method of that name in any class may be one this class inherits, which C++ would call instead.
            if (program.Classes.Any(type => type.Methods.Any(method => method.Name == name))) return null;
        }

        var named = _topLevel[name].ToList();
        var ownFile = named.Where(function => Places.SameFile(function.Span, caller.Span)).ToList();
        return Single((ownFile.Count > 0 ? ownFile : named).Where(function => Takes(function, arguments))) is { } chosen ? new CallTarget(chosen, false) : null;
    }

    /// <summary>Whether a function takes that many arguments: exactly its parameters, or at least those before a C ... .</summary>
    private static bool Takes(IrFunction function, int arguments) =>
        function.Parameters is [.., { Kind: ParameterKind.Rest }] ? arguments >= function.Parameters.Count - 1 : arguments == function.Parameters.Count;

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
