using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Something threads can share - a lock, a field, a variable - named the same wherever the program means the same thing.
/// The identity is what matches two places; Shown is how the code at hand writes it. Something reached through one of a
/// function's parameters has no identity of its own until a call says what that parameter is.
/// </summary>
internal sealed record SharedName(string Identity, string Shown, int Parameter = SharedName.NoParameter, string Path = "")
{
    public const int NoParameter = -1;

    /// <summary>Something whose value cannot be named - synchronized (next()), lock (locks[i]) - so it could be anything.</summary>
    public static SharedName Unnamed { get; } = new("?", "?");

    public bool IsKnown => Identity != "?";

    public bool IsConcrete => Parameter == NoParameter;
}

/// <summary>
/// Data threads can share, named program-wide: a field of one object, a static field, a module's variable, a variable a
/// lambda captures. <paramref name="Field"/> is the field's declaration, when there is one, which says whether it is
/// volatile or an atomic. Data reached through a parameter waits, like a <see cref="SharedName"/>, for a call to name it.
/// </summary>
internal sealed record DataPlace(string Identity, IrField? Field, int Parameter = SharedName.NoParameter, string Path = "")
{
    public bool IsConcrete => Parameter == SharedName.NoParameter;

    /// <summary>Safe to use from several threads without a lock: volatile, or an atomic or concurrent type that synchronises itself.</summary>
    public bool Synchronises => Field is { } declared && (declared.IsVolatile ||
        declared.Type.Name.StartsWith("Atomic", StringComparison.Ordinal) || declared.Type.Name.StartsWith("Concurrent", StringComparison.Ordinal));
}

/// <summary>
/// Names that mean the same thing across the whole program, which is what matching two threads' use of one variable, or
/// two places taking one lock, needs. A field belongs to its class, a variable to the function that declares it - reached
/// from a lambda or a nested function through the functions it is written in - and a module's variable to the module.
/// </summary>
/// <remarks>
/// A variable no other code can reach - never captured, passed on, stored or returned - is new on every call, so no two
/// threads can share it, and it has no program-wide name at all.
/// </remarks>
internal sealed class ProgramNames(IrProgram program, SourceText source)
{
    /// <summary>How far a name is looked up through the functions it is written inside.</summary>
    public const int MostNesting = 32;

    private readonly Dictionary<string, IrClass> _classes = program.Classes.GroupBy(c => c.Name, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    private readonly Dictionary<IrFunction, HashSet<string>> _locals = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IrFunction, HashSet<string>> _sharedLocals = new(ReferenceEqualityComparer.Instance);
    private Dictionary<IrFunction, List<IrFunction>>? _nested;

    public CallTargets Targets { get; } = new(program);

    public IrProgram Program => program;

    public bool IsClass(string name) => _classes.ContainsKey(name);

    /// <summary>The class a function belongs to, or - for a lambda or nested function - the class of the method it is in.</summary>
    public IrClass? ClassOf(IrFunction function) => Targets.ClassOf(function) is { } owner ? _classes.GetValueOrDefault(owner) : null;

    /// <summary>What an expression names program-wide; a field of this class is the class's, whichever object it is on.</summary>
    public SharedName? Named(Expr expression, IrFunction function) => expression switch
    {
        Name { Identifier: "this" or "self" } when Targets.ClassOf(function) is { } owner => new SharedName($"{owner}.this", Shown(expression)),
        Name { Identifier: var name } => Variable(name, function, Shown(expression)),
        Member { Target: Name { Identifier: "this" or "self" }, MemberName: var field } when Targets.ClassOf(function) is { } owner =>
            new SharedName($"{owner}.{field}", Shown(expression)),
        Member { Target: Name { Identifier: var type }, MemberName: var field } when IsClass(type) && !LocalsOf(function).Contains(type) =>
            new SharedName($"{type}.{field}", Shown(expression)),
        Member { Target: var target, MemberName: var field } => Named(target, function) is { IsKnown: true } inner
            ? new SharedName($"{inner.Identity}.{field}", Shown(expression), inner.Parameter, $"{inner.Path}.{field}")
            : SharedName.Unnamed,
        _ => SharedName.Unnamed,
    };

    /// <summary>
    /// What a name means in a function: one of its own parameters, a variable of its own or of the code it is written
    /// in, a field of its class, or a global. A local that never leaves its function is null - each call makes its own.
    /// </summary>
    public SharedName? Variable(string name, IrFunction function, string shown)
    {
        var scope = function;

        for (var depth = 0; scope is not null && depth < MostNesting; depth++)
        {
            if (scope.Name == IrFunction.ModuleBody) return new SharedName(name, shown);

            var parameterIndex = IndexOf(scope.Parameters, name);
            if (parameterIndex >= 0)
            {
                // A lambda's use of the parameter of the method around it is fixed for the lambda's whole life, like a local.
                return ReferenceEquals(scope, function)
                    ? new SharedName($"{KeyOf(scope)}({name})", shown, parameterIndex)
                    : new SharedName($"{KeyOf(scope)}:{name}", shown);
            }

            if (!scope.OuterNames.Contains(name) && LocalsOf(scope).Contains(name))
                return SharedLocalsOf(scope).Contains(name) ? new SharedName($"{KeyOf(scope)}:{name}", shown) : null;

            scope = Targets.Enclosing(scope);
        }

        return Targets.ClassOf(function) is { } owner && program.Language != SourceLanguage.Python
            ? new SharedName($"{owner}.{name}", shown)
            : new SharedName(name, shown);
    }

    private static int IndexOf(IReadOnlyList<IrParameter> parameters, string name)
    {
        for (var index = 0; index < parameters.Count; index++)
            if (parameters[index].Name == name) return index;

        return -1;
    }

    /// <summary>Overloads and lambdas on the same line can share a full name; where the function starts tells them apart.</summary>
    public static string KeyOf(IrFunction function) => $"{function.FullName}@{function.Span.Line}";

    public HashSet<string> LocalsOf(IrFunction function)
    {
        if (_locals.TryGetValue(function, out var known)) return known;
        return _locals[function] = IrWalk.LocalNames(function, assigningDeclares: program.Language == SourceLanguage.Python);
    }

    /// <summary>
    /// A function's variables that other code, and so other threads, can reach: captured by a lambda or a function
    /// written inside it, passed to a call, stored in an object, or returned.
    /// </summary>
    public HashSet<string> SharedLocalsOf(IrFunction function)
    {
        if (_sharedLocals.TryGetValue(function, out var known)) return known;

        _nested ??= NestedFunctions();
        var locals = LocalsOf(function);
        var shared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var nested in _nested.GetValueOrDefault(function) ?? [])
            shared.UnionWith(IrWalk.FreeNames(nested));

        foreach (var statement in IrWalk.Statements(function.Body))
        {
            switch (statement)
            {
                case Assign { Target: Member or ElementAccess, Value: Name stored }:
                    shared.Add(stored.Identifier);
                    break;
                case Return { Value: Name returned }:
                    shared.Add(returned.Identifier);
                    break;
            }

            foreach (var expression in IrWalk.Expressions(statement))
            {
                shared.UnionWith(Passed(expression));
                shared.UnionWith(Started(expression));
            }
        }

        shared.IntersectWith(locals);
        return _sharedLocals[function] = shared;
    }

    /// <summary>The lambdas and functions written directly inside each function.</summary>
    public IReadOnlyList<IrFunction> NestedIn(IrFunction function)
    {
        _nested ??= NestedFunctions();
        return _nested.GetValueOrDefault(function) ?? [];
    }

    private Dictionary<IrFunction, List<IrFunction>> NestedFunctions()
    {
        var nested = new Dictionary<IrFunction, List<IrFunction>>(ReferenceEqualityComparer.Instance);

        foreach (var function in program.AllFunctions)
        {
            if (Targets.Enclosing(function) is not { } outer) continue;
            if (!nested.TryGetValue(outer, out var inside)) nested[outer] = inside = [];
            inside.Add(function);
        }

        return nested;
    }

    /// <summary>Names handed to a call or a new object - directly, or inside a tuple or list of arguments.</summary>
    private static IEnumerable<string> Passed(Expr expression)
    {
        var handed = expression switch
        {
            Call call => call.Arguments.SelectMany(a => Handed(a.Value)),
            NewObject made => made.Arguments.SelectMany(a => Handed(a.Value)),
            _ => [],
        };

        return handed.Concat(IrWalk.Children(expression).SelectMany(Passed));
    }

    /// <summary>Variables start() is called on: threads, whose fields the code they run shares with whoever made them.</summary>
    private static IEnumerable<string> Started(Expr expression)
    {
        var started = expression is Call { Callee: Member { Target: Name thread, MemberName: "start" or "Start" }, Arguments.Count: 0 } ? [thread.Identifier] : Enumerable.Empty<string>();
        return started.Concat(IrWalk.Children(expression).SelectMany(Started));
    }

    private static IEnumerable<string> Handed(Expr value) => value switch
    {
        Name name => [name.Identifier],
        CollectionLiteral items => items.Items.SelectMany(Handed),
        _ => [],
    };

    /// <summary>How the source writes an expression, or the IR's own rendering where the source is not to hand.</summary>
    public string Shown(Expr expression) => source.Of(expression.Span) is { Length: > 0 } text ? text : IrText.Of(expression);

    /// <summary>The name, inside a method, of the object it runs on - its this or self - or null for code that has none.</summary>
    public string? ThisOf(IrFunction function)
    {
        var current = function;

        for (var depth = 0; current is not null && depth < MostNesting; depth++)
        {
            if (current.Owner is { } owner && IsClass(owner)) return current.IsStatic ? null : $"{KeyOf(current)}:this";
            current = Targets.Enclosing(current);
        }

        return null;
    }

    /// <summary>The method a thread runs for an object of one of the program's classes: run() of a Runnable or a Thread, call() of a Callable.</summary>
    public IrFunction? RunOf(string type) =>
        _classes.GetValueOrDefault(type) is { } found && found.Bases.Any(b => b is "Runnable" or "Thread" or "Callable" or "threading.Thread")
            ? found.Methods.FirstOrDefault(m => m.Name is "run" or "call" && m.Parameters.All(p => p.Name == "self"))
            : null;

    /// <summary>Which of the program's classes the object an expression holds belongs to, where the code says so plainly.</summary>
    public IrClass? ClassOfValue(Expr expression, IrFunction function) => expression switch
    {
        NewObject made => _classes.GetValueOrDefault(made.Type.Name),
        Call { Callee: Name { Identifier: var type } } when program.Language == SourceLanguage.Python => _classes.GetValueOrDefault(type),
        Name { Identifier: "this" or "self" } => ClassOf(function),
        Name { Identifier: var name } => ClassOfVariable(name, function),
        Member { Target: Name { Identifier: "this" or "self" }, MemberName: var field } => ClassOfField(ClassOf(function), field),
        _ => null,
    };

    private IrClass? ClassOfVariable(string name, IrFunction function)
    {
        var scope = function;

        for (var depth = 0; scope is not null && depth < MostNesting; depth++)
        {
            if (scope.Parameters.FirstOrDefault(p => p.Name == name) is { } parameter) return _classes.GetValueOrDefault(parameter.Type.Name);

            foreach (var statement in IrWalk.Statements(scope.Body))
            {
                var made = statement switch
                {
                    Declare declare when declare.Variable == name =>
                        _classes.GetValueOrDefault(declare.Type.Name) ?? (declare.Initial is NewObject or Call ? ClassOfValue(declare.Initial, scope) : null),
                    Assign { Target: Name { Identifier: var assigned }, Value: NewObject or Call } assign when assigned == name => ClassOfValue(assign.Value, scope),
                    _ => null,
                };

                if (made is not null) return made;
            }

            if (!scope.OuterNames.Contains(name) && LocalsOf(scope).Contains(name)) return null;
            scope = Targets.Enclosing(scope);
        }

        return ClassOfField(ClassOf(function), name);
    }

    private IrClass? ClassOfField(IrClass? owner, string field)
    {
        if (owner is null) return null;
        if (owner.Fields.FirstOrDefault(f => f.Name == field) is { } declared && _classes.GetValueOrDefault(declared.Type.Name) is { } typed) return typed;

        // Python gives an object its attributes in its methods: self.counter = Counter().
        foreach (var method in owner.Methods)
        {
            foreach (var statement in IrWalk.Statements(method.Body))
            {
                if (statement is Assign { Target: Member { Target: Name { Identifier: "self" or "this" }, MemberName: var assigned }, Value: NewObject or Call } assign &&
                    assigned == field && ClassOfValue(assign.Value, method) is { } made)
                {
                    return made;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The data an expression reads or writes, named program-wide, with this and self standing for the given object. Null
    /// for data only this call can see - a local nothing else reaches - or data whose owner cannot be told.
    /// </summary>
    public DataPlace? PlaceOf(Expr expression, IrFunction function, string? receiver) => expression switch
    {
        Name { Identifier: "this" or "self" } => receiver is null ? null : new DataPlace(receiver, null),
        Name { Identifier: var name } => VariablePlace(name, function, receiver),
        Member { Target: Name { Identifier: "this" or "self" }, MemberName: var field } => FieldPlace(ClassOf(function), field, receiver),
        Member { Target: Name { Identifier: var type }, MemberName: var field } when IsClass(type) && !LocalsOf(function).Contains(type) =>
            new DataPlace($"{type}.{field}", _classes[type].Fields.FirstOrDefault(f => f.Name == field)),
        Member { Target: var target, MemberName: var field } when PlaceOf(target, function, receiver) is { } owner =>
            new DataPlace($"{owner.Identity}.{field}", ClassOfValue(target, function)?.Fields.FirstOrDefault(f => f.Name == field), owner.Parameter, $"{owner.Path}.{field}"),
        _ => null,
    };

    private DataPlace? VariablePlace(string name, IrFunction function, string? receiver)
    {
        var scope = function;

        for (var depth = 0; scope is not null && depth < MostNesting; depth++)
        {
            if (scope.Name == IrFunction.ModuleBody) return new DataPlace(name, null);

            var parameterIndex = IndexOf(scope.Parameters, name);
            if (parameterIndex >= 0)
            {
                return ReferenceEquals(scope, function)
                    ? new DataPlace($"{KeyOf(scope)}({name})", null, parameterIndex)
                    : new DataPlace($"{KeyOf(scope)}:{name}", null);
            }

            if (!scope.OuterNames.Contains(name) && LocalsOf(scope).Contains(name))
                return SharedLocalsOf(scope).Contains(name) ? new DataPlace($"{KeyOf(scope)}:{name}", null) : null;

            scope = Targets.Enclosing(scope);
        }

        if (program.Language == SourceLanguage.Python) return new DataPlace(name, null);
        return FieldPlace(ClassOf(function), name, receiver);
    }

    /// <summary>
    /// A field of the class: a static one belongs to the class, any other to the object it is reached through. Java and C#
    /// declare their fields, so a name that is not one is left alone; Python gives an object its attributes as it runs.
    /// </summary>
    private DataPlace? FieldPlace(IrClass? owner, string field, string? receiver)
    {
        var declared = owner?.Fields.FirstOrDefault(f => f.Name == field);
        if (declared is { IsStatic: true }) return new DataPlace($"{owner!.Name}.{field}", declared);
        if (declared is null && program.Language != SourceLanguage.Python) return null;

        return receiver is null ? null : new DataPlace($"{receiver}.{field}", declared);
    }

    /// <summary>The source text of a span, or empty where the source is not to hand.</summary>
    public string TextAt(SourceSpan span) => source.Of(span);

    /// <summary>
    /// The name of the type of the value an expression holds, where the code says so - declared, or what made it - even
    /// for a type the program did not write: CountDownLatch, threading.Event, a queue.
    /// </summary>
    public string? TypeNameOf(Expr expression, IrFunction function) => expression switch
    {
        NewObject made => made.Type.Name,
        Call { Callee: Name { Identifier: var made } } when program.Language == SourceLanguage.Python => made,
        Call { Callee: Member { MemberName: var made } } when program.Language == SourceLanguage.Python => made,
        Name { Identifier: var name } => VariableTypeName(name, function),
        Member { Target: Name { Identifier: "this" or "self" }, MemberName: var field } => FieldTypeName(ClassOf(function), field),
        Member { Target: Name { Identifier: var type }, MemberName: var field } when IsClass(type) => FieldTypeName(_classes[type], field),
        _ => null,
    };

    private string? VariableTypeName(string name, IrFunction function)
    {
        var scope = function;

        for (var depth = 0; scope is not null && depth < MostNesting; depth++)
        {
            if (scope.Parameters.FirstOrDefault(p => p.Name == name) is { Type.IsUnknown: false } parameter) return parameter.Type.Name;

            foreach (var statement in IrWalk.Statements(scope.Body))
            {
                var named = statement switch
                {
                    Declare { Type.IsUnknown: false } declare when declare.Variable == name => declare.Type.Name,
                    Declare { Initial: NewObject or Call } declare when declare.Variable == name => TypeNameOf(declare.Initial, scope),
                    Assign { Target: Name { Identifier: var assigned }, Value: NewObject or Call } assign when assigned == name => TypeNameOf(assign.Value, scope),
                    _ => null,
                };

                if (named is not null) return named;
            }

            if (!scope.OuterNames.Contains(name) && LocalsOf(scope).Contains(name)) return null;
            scope = Targets.Enclosing(scope);
        }

        return FieldTypeName(ClassOf(function), name);
    }

    private string? FieldTypeName(IrClass? owner, string field)
    {
        if (owner?.Fields.FirstOrDefault(f => f.Name == field) is { } declared)
            return !declared.Type.IsUnknown ? declared.Type.Name : declared.Initial is { } initial ? TypeNameOf(initial, owner.Methods.FirstOrDefault() ?? EmptyFunction) : null;

        foreach (var method in owner?.Methods ?? [])
        {
            foreach (var statement in IrWalk.Statements(method.Body))
            {
                if (statement is Assign { Target: Member { Target: Name { Identifier: "self" or "this" }, MemberName: var assigned }, Value: NewObject or Call } assign &&
                    assigned == field)
                {
                    return TypeNameOf(assign.Value, method);
                }
            }
        }

        return null;
    }

    private static readonly IrFunction EmptyFunction = new(SourceSpan.None, "", null, [], IrType.Unknown, []);
}
