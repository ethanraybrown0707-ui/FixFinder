using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Abstract;

/// <summary>
/// The variables whose address a C or C++ function hands out - &amp;count to append, &amp;end to strtoll - and what can
/// change them from then on: a call given the address, and a write through a pointer that holds it. An address that may
/// be kept - stored, returned, or handed to code that could keep it - can be used by any later call, and written
/// through any pointer, so a variable whose address may be kept is changed by every call and every write through a
/// pointer.
/// </summary>
/// <remarks>
/// An address is only handed on, not kept, when it goes to one of C's own functions that write through what they are
/// given while they run and keep none of it, or to one of the program's own functions that only reads and writes through
/// the matching parameter, compares it, or hands it on in the same way. Anything else counts as keeping it.
/// </remarks>
public sealed class HandedAddresses
{
    /// <summary>
    /// C's own functions that read or write through the addresses they are given while they run, keep none of them, and
    /// give back no pointer into them.
    /// </summary>
    private static readonly HashSet<string> KeepNothing = new(StringComparer.Ordinal)
    {
        "scanf", "sscanf", "fscanf", "strtol", "strtoll", "strtoul", "strtoull", "strtod", "strtof", "strtold",
        "printf", "fprintf", "sprintf", "snprintf", "memcmp", "strcmp", "strncmp", "strlen", "qsort", "time",
    };

    private readonly Dictionary<string, HashSet<string>> _holders;

    private HandedAddresses(IReadOnlySet<string> variables, IReadOnlySet<string> kept, Dictionary<string, HashSet<string>> holders)
    {
        Variables = variables;
        Kept = kept;
        _holders = holders;
    }

    /// <summary>For a function that hands out no address, or a language this does not apply to.</summary>
    public static HandedAddresses None { get; } = new(new HashSet<string>(), new HashSet<string>(), []);

    /// <summary>Every variable whose address the function hands out.</summary>
    public IReadOnlySet<string> Variables { get; }

    /// <summary>The variables whose address may be kept somewhere the function cannot see, so any code at all may change them.</summary>
    public IReadOnlySet<string> Kept { get; }

    public static HandedAddresses Of(IrFunction function, IrProgram program)
    {
        if (function.AddressTaken.Count == 0 || program.Language is not (SourceLanguage.C or SourceLanguage.Cpp)) return None;

        var locals = IrWalk.LocalNames(function);
        var holders = function.AddressTaken.ToDictionary(variable => variable, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var escape = new Escape(program);

        foreach (var (variable, holding) in holders)
            holding.UnionWith(Holders(function.Body, locals, expression => AddressOf(expression) == variable));

        var kept = holders
            .Where(pair => escape.Leaks(function.Body, locals, expression => AddressOf(expression) == pair.Key || Unwrapped(expression) is Name { Identifier: var name } && pair.Value.Contains(name)))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);

        return new HandedAddresses(function.AddressTaken.ToHashSet(StringComparer.Ordinal), kept, holders);
    }

    /// <summary>What can have changed once the calls in an expression have run: whatever may be kept, and whatever a call is given the address of.</summary>
    public IEnumerable<string> ChangedByCallsIn(Expr expression) =>
        CollectionsOfCalls(expression).SelectMany(ChangedByCall).Distinct(StringComparer.Ordinal);

    /// <summary>What a call can change: whatever may be kept, and whatever it is given the address of.</summary>
    public IEnumerable<string> ChangedByCall(Call call) =>
        Kept.Concat(Variables.Where(variable => call.Arguments.Any(argument => Points(argument.Value, variable))));

    /// <summary>What a write can change: whatever may be kept, and whatever the pointer written through holds the address of.</summary>
    public IEnumerable<string> ChangedByWriteTo(Expr target) =>
        Kept.Concat(Variables.Where(variable => Through(target) is { } pointer && Points(pointer, variable))).Distinct(StringComparer.Ordinal);

    private static IEnumerable<Call> CollectionsOfCalls(Expr expression) => IrWalk.Within(expression).OfType<Call>();

    /// <summary>Whether an expression is the variable's address, or a pointer that holds it.</summary>
    private bool Points(Expr expression, string variable) =>
        AddressOf(expression) == variable || Unwrapped(expression) is Name { Identifier: var name } && _holders.TryGetValue(variable, out var holding) && holding.Contains(name);

    /// <summary>The pointer a write goes through: p for *p, p->next and p[i].</summary>
    private static Expr? Through(Expr target) => target switch
    {
        Member { Target: var pointer } => pointer,
        ElementAccess { Target: var pointer } => pointer,
        _ => null,
    };

    /// <summary>The variable an expression takes the address of: x for &amp;x, &amp;x.count and &amp;x[i].</summary>
    private static string? AddressOf(Expr expression) =>
        Unwrapped(expression) is Opaque { What: "address of", Parts: [var addressed] } ? Addressed(addressed) : null;

    private static string? Addressed(Expr addressed) => addressed switch
    {
        Name name => name.Identifier,
        Member member => Addressed(member.Target),
        ElementAccess element => Addressed(element.Target),
        _ => null,
    };

    private static Expr Unwrapped(Expr expression) => expression is Cast cast ? Unwrapped(cast.Value) : expression;

    /// <summary>The function's own pointers that are given the address, directly or from another that holds it.</summary>
    private static HashSet<string> Holders(IReadOnlyList<Stmt> body, IReadOnlySet<string> locals, Func<Expr, bool> isAddress)
    {
        var holders = new HashSet<string>(StringComparer.Ordinal);
        var assignments = IrWalk.Statements(body).SelectMany(Assignments).ToList();

        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var (holder, value) in assignments)
            {
                if (!locals.Contains(holder) || holders.Contains(holder)) continue;
                if (isAddress(value) || Unwrapped(value) is Name { Identifier: var from } && holders.Contains(from)) grew |= holders.Add(holder);
            }
        }

        return holders;
    }

    /// <summary>Every name a statement gives a plain value to, with the value.</summary>
    private static IEnumerable<(string Name, Expr Value)> Assignments(Stmt statement)
    {
        switch (statement)
        {
            case Declare { Initial: { } initial } declared:
                yield return (declared.Variable, initial);
                break;
            case Assign { Target: Name target, Compound: null } assign:
                yield return (target.Identifier, assign.Value);
                break;
        }

        foreach (var embedded in IrWalk.Expressions(statement).SelectMany(IrWalk.Within).OfType<AssignValue>())
            if (embedded.Target is Name { Identifier: var named }) yield return (named, embedded.Value);
    }

    /// <summary>Whether a pointer may be kept by a function body, worked out once for each of the program's functions and parameters.</summary>
    private sealed class Escape(IrProgram program)
    {
        private readonly Dictionary<(IrFunction Function, int Parameter), bool> _known = [];

        /// <summary>
        /// Whether the pointer shows up anywhere in the body where it could be kept: stored anywhere but in one of the
        /// function's own pointers, returned, or handed to code that could keep it.
        /// </summary>
        public bool Leaks(IReadOnlyList<Stmt> body, IReadOnlySet<string> locals, Func<Expr, bool> isPointer)
        {
            var holders = Holders(body, locals, isPointer);
            bool Pointer(Expr expression) => isPointer(expression) || Unwrapped(expression) is Name { Identifier: var name } && holders.Contains(name);

            return IrWalk.Statements(body).Any(statement => StatementLeaks(statement, locals, Pointer));
        }

        private bool StatementLeaks(Stmt statement, IReadOnlySet<string> locals, Func<Expr, bool> pointer) => statement switch
        {
            Declare { Initial: { } initial } declared => Stored(initial, locals.Contains(declared.Variable), pointer),
            Assign assign => Stored(assign.Value, assign is { Target: Name { Identifier: var holder }, Compound: null } && locals.Contains(holder), pointer) ||
                             TargetLeaks(assign.Target, pointer),
            Return { Value: { } returned } => pointer(returned) || Leaks(returned, pointer),
            _ => IrWalk.Expressions(statement).Any(expression => Leaks(expression, pointer)),
        };

        /// <summary>A value given to something: the pointer may go into one of the function's own pointers, and nowhere else.</summary>
        private bool Stored(Expr value, bool intoOwnPointer, Func<Expr, bool> pointer) =>
            pointer(value) ? !intoOwnPointer : Leaks(value, pointer);

        private bool TargetLeaks(Expr target, Func<Expr, bool> pointer) => target switch
        {
            Name => false,
            Member { Target: var through } => !pointer(through) && Leaks(through, pointer),
            ElementAccess { Target: var through, Key: var key } => !pointer(through) && Leaks(through, pointer) || Leaks(key, pointer),
            _ => Leaks(target, pointer),
        };

        /// <summary>
        /// Whether the pointer appears in the expression anywhere except where it is only read or written through,
        /// compared, tested, or handed to code that keeps nothing.
        /// </summary>
        private bool Leaks(Expr expression, Func<Expr, bool> pointer)
        {
            if (pointer(expression)) return true;

            switch (Unwrapped(expression))
            {
                case Member { Target: var through }:
                    return !pointer(through) && Leaks(through, pointer);

                case ElementAccess { Target: var through, Key: var key }:
                    return !pointer(through) && Leaks(through, pointer) || Leaks(key, pointer);

                case Binary { Operator: BinaryOperator.Equal or BinaryOperator.NotEqual or BinaryOperator.Less or BinaryOperator.LessOrEqual
                              or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual or BinaryOperator.And or BinaryOperator.Or } tested:
                    return !pointer(tested.Left) && Leaks(tested.Left, pointer) || !pointer(tested.Right) && Leaks(tested.Right, pointer);

                case Unary { Operator: UnaryOperator.Not, Operand: var tested }:
                    return !pointer(tested) && Leaks(tested, pointer);

                case Call call:
                    return Leaks(call.Callee, pointer) ||
                           call.Arguments.Select((argument, index) => pointer(argument.Value) ? !KeepsNothing(call, index) : Leaks(argument.Value, pointer)).Any(leaks => leaks);

                case AssignValue assigned:
                    return pointer(assigned.Value) || Leaks(assigned.Value, pointer) || TargetLeaks(assigned.Target, pointer);

                default:
                    return IrWalk.Children(expression).Any(child => Leaks(child, pointer));
            }
        }

        /// <summary>Whether a call keeps nothing of the pointer it is given at this position.</summary>
        private bool KeepsNothing(Call call, int position)
        {
            if (call.Callee is not Name { Identifier: var called }) return false;
            if (KeepNothing.Contains(called)) return true;

            var functions = program.AllFunctions.Where(function => function.Owner is null && function.Name == called).ToList();
            if (functions is not [var function] || position >= function.Parameters.Count) return false;

            var key = (function, position);
            if (_known.TryGetValue(key, out var keeps)) return !keeps;

            // Assumed kept by nothing while it is being worked out, so a function that calls itself does not go round forever:
            // anything else in its body that keeps the pointer is found all the same.
            _known[key] = false;
            var parameter = function.Parameters[position].Name;
            keeps = Leaks(function.Body, IrWalk.LocalNames(function), expression => Unwrapped(expression) is Name { Identifier: var name } && name == parameter);
            _known[key] = keeps;
            return !keeps;
        }
    }
}
