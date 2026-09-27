using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Effect summaries: what calling a function does to the collections it can reach, beyond the value it returns - which
/// of its parameters' lists, dictionaries and sets it adds to or removes from, which fields of the object it runs on,
/// which module variables and static fields - each with the line that does it, in the function or in one it calls.
/// </summary>
/// <remarks>
/// A summary records only changes the code plainly makes: a method that adds or removes called on the collection itself,
/// or a call whose own summary says so. It is evidence of what a function does, not a bound on what it might do - what
/// happens in code the collection is handed to that FixFinder cannot see is not guessed at.
/// </remarks>
internal sealed class Effects(IrProgram program, CallTargets targets)
{
    /// <summary>How many times summaries are worked out again from each other's; enough for a chain of helpers.</summary>
    private const int Rounds = 4;

    /// <summary>Methods that put more in a collection. Only a list certainly grows: adding what a set already holds changes nothing.</summary>
    public static readonly IReadOnlySet<string> Adding = new HashSet<string>(StringComparer.Ordinal)
    {
        "append", "extend", "insert", "appendleft", "extendleft", "add", "addAll", "addFirst", "addLast", "offer", "push",
        "Add", "AddRange", "Insert", "InsertRange", "Push", "Enqueue", "AddFirst", "AddLast",
    };

    /// <summary>Methods that take out of a collection.</summary>
    public static readonly IReadOnlySet<string> Removing = new HashSet<string>(StringComparer.Ordinal)
    {
        "remove", "pop", "popleft", "popitem", "discard", "clear", "removeAll", "removeIf", "retainAll", "poll", "removeFirst", "removeLast",
        "Remove", "RemoveAt", "RemoveAll", "RemoveRange", "Clear", "Pop", "Dequeue", "RemoveFirst", "RemoveLast",
    };

    public enum Reach { Parameter, Field, Module }

    /// <summary>
    /// Something a function can reach: one of its parameters (by position), a field of the object it runs on, or a module
    /// variable or static field (by name).
    /// </summary>
    public sealed record Reached(Reach Kind, string Name, int Index = -1);

    /// <summary>A change a function makes to what it reaches: the call that makes it, and where.</summary>
    public sealed record Change(SourceSpan At, string Method)
    {
        public bool Adds => Adding.Contains(Method);
    }

    private readonly Dictionary<IrFunction, Dictionary<Reached, Change>> _summaries = new(ReferenceEqualityComparer.Instance);

    public IrProgram Program => program;

    private bool IsPython => program.Language == SourceLanguage.Python;

    /// <summary>What calling the function changes, in the function's own terms.</summary>
    public IReadOnlyDictionary<Reached, Change> Of(IrFunction function)
    {
        if (_summaries.Count == 0) Compute();
        return _summaries.GetValueOrDefault(function) ?? [];
    }

    private void Compute()
    {
        foreach (var function in program.AllFunctions) _summaries[function] = Direct(function);

        for (var round = 0; round < Rounds; round++)
        {
            var grew = false;

            foreach (var function in program.AllFunctions)
            {
                var mine = _summaries[function];

                foreach (var (call, target) in Calls(function))
                {
                    foreach (var (reached, change) in _summaries.GetValueOrDefault(target.Function) ?? [])
                    {
                        if (InCaller(reached, call, target, function) is not { } translated || mine.ContainsKey(translated)) continue;
                        mine[translated] = change;
                        grew = true;
                    }
                }
            }

            if (!grew) break;
        }
    }

    /// <summary>The changes a function makes itself: a method that adds or removes, called on something it can reach.</summary>
    private Dictionary<Reached, Change> Direct(IrFunction function)
    {
        var changes = new Dictionary<Reached, Change>();

        foreach (var call in IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(CallsIn))
        {
            if (call.Callee is not Member { Target: var changed, MemberName: var method } || !Adding.Contains(method) && !Removing.Contains(method)) continue;
            if (ReachedBy(changed, function) is { } reached) changes.TryAdd(reached, new Change(call.Span, method));
        }

        return changes;
    }

    /// <summary>The calls a function makes to the program's own functions.</summary>
    private IEnumerable<(Call Call, CallTarget Target)> Calls(IrFunction function)
    {
        var locals = IrWalk.LocalNames(function, assigningDeclares: IsPython);

        foreach (var call in IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(CallsIn))
            if (targets.Resolve(call, function, locals) is { } target) yield return (call, target);
    }

    /// <summary>Every call in an expression, but none inside a lambda, which is a function of its own.</summary>
    public static IEnumerable<Call> CallsIn(Expr expression)
    {
        if (expression is Opaque { What: "lambda expression" or "lambda" }) yield break;
        if (expression is Call call) yield return call;

        foreach (var child in IrWalk.Children(expression))
            foreach (var inner in CallsIn(child)) yield return inner;
    }

    /// <summary>What an expression in a function reaches, when it is something a caller could see change: a parameter, a field, a module variable.</summary>
    public Reached? ReachedBy(Expr expression, IrFunction function)
    {
        switch (expression)
        {
            case Member { Target: Name { Identifier: "self" or "this" }, MemberName: var field }:
                return StaticField(function, field) ?? new Reached(Reach.Field, field);

            case Member { Target: Name { Identifier: var type }, MemberName: var field } when program.Classes.Any(c => c.Name == type):
                return new Reached(Reach.Module, $"{type}.{field}");

            case Name { Identifier: var name }:
                var index = function.Parameters.ToList().FindIndex(p => p.Name == name);
                if (index >= 0) return IsPython && index == 0 && function.Owner is not null && name is "self" or "cls" ? null : new Reached(Reach.Parameter, name, index);

                var locals = IrWalk.LocalNames(function, assigningDeclares: IsPython);
                if (locals.Contains(name)) return null;
                if (IsPython) return new Reached(Reach.Module, name);

                return StaticField(function, name) ?? (FieldOf(function, name) is not null ? new Reached(Reach.Field, name) : null);

            default:
                return null;
        }
    }

    private IrField? FieldOf(IrFunction function, string name) =>
        targets.ClassOf(function) is { } owner ? program.Classes.FirstOrDefault(c => c.Name == owner)?.Fields.FirstOrDefault(f => f.Name == name) : null;

    private Reached? StaticField(IrFunction function, string name) =>
        FieldOf(function, name) is { IsStatic: true } && targets.ClassOf(function) is { } owner ? new Reached(Reach.Module, $"{owner}.{name}") : null;

    /// <summary>
    /// A change a callee makes, in the caller's terms: to the argument the caller passed, to the caller's own object when
    /// the call is made on it, to the same module variable. Null when the caller cannot see it.
    /// </summary>
    private Reached? InCaller(Reached reached, Call call, CallTarget target, IrFunction caller) => reached.Kind switch
    {
        Reach.Parameter => ArgumentFor(call, target, reached.Index) is { } argument ? ReachedBy(argument, caller) : null,
        Reach.Field => OnSameObject(call) ? reached : null,
        Reach.Module when !SharedWithCaller(reached, target, call.Span) => null,
        _ => reached,
    };

    /// <summary>
    /// Whether a module variable a callee changes is one the caller has too. Every Python module has variables of its own,
    /// so a function in another file that changes its total changes that file's total, not the caller's.
    /// </summary>
    public bool SharedWithCaller(Reached reached, CallTarget target, SourceSpan call) =>
        !(IsPython && reached.Kind == Reach.Module && !reached.Name.Contains('.') && !Places.SameFile(target.Function.Span, call));

    /// <summary>
    /// Whether a call runs on the caller's own object: self.helper(), this.helper(), or plain helper() in the languages
    /// where a method calls another of its object's methods by name alone - Java, C# and C++.
    /// </summary>
    public bool OnSameObject(Call call) => call.Callee switch
    {
        Member { Target: Name { Identifier: "self" or "this" } } => true,
        Name => program.Language is SourceLanguage.Java or SourceLanguage.CSharp or SourceLanguage.Cpp,
        _ => false,
    };

    /// <summary>The argument a call gives one of the callee's parameters: by name, or by its position among the others.</summary>
    public static Expr? ArgumentFor(Call call, CallTarget target, int parameterIndex)
    {
        if (parameterIndex < 0 || parameterIndex >= target.Function.Parameters.Count) return null;

        var parameter = target.Function.Parameters[parameterIndex];
        if (call.Arguments.FirstOrDefault(a => a.Name == parameter.Name) is { } named) return named.Value;
        if (parameter.Kind != ParameterKind.Normal) return null;

        var position = target.Bound ? parameterIndex - 1 : parameterIndex;
        var positional = call.Arguments.Where(a => a.Name is null).ToList();
        return position >= 0 && position < positional.Count ? positional[position].Value : null;
    }
}
