using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Abstract;

/// <summary>What each expression could be worth, how each step changes the variables, and what a condition tells us.</summary>
public sealed class Evaluator(SourceLanguage language)
{
    private static readonly HashSet<string> Builtins = new(StringComparer.Ordinal)
    {
        "len", "print", "range", "int", "float", "str", "bool", "abs", "min", "max", "round", "sum", "input", "list", "tuple",
        "set", "dict", "sorted", "reversed", "enumerate", "zip", "map", "filter", "open", "isinstance", "type", "repr", "ord", "chr",
        "any", "all", "iter", "next", "super", "object", "id", "hash", "format", "divmod", "pow",
    };

    private static readonly HashSet<string> Growing = new(StringComparer.Ordinal)
    {
        "append", "insert", "appendleft", "add", "addFirst", "addLast", "push", "unshift", "offer", "Add", "Insert", "Push", "Enqueue", "AddFirst", "AddLast",
    };

    private static readonly HashSet<string> CertainlyShrinking = new(StringComparer.Ordinal)
    {
        "pop", "popleft", "shift", "RemoveAt", "removeFirst", "removeLast", "Pop", "Dequeue", "RemoveFirst", "RemoveLast",
    };

    private static readonly HashSet<string> MaybeShrinking = new(StringComparer.Ordinal) { "remove", "discard", "Remove", "poll", "splice" };


    public SourceLanguage Language => language;

    /// <summary>Whether a name is one of the Python builtins the evaluator knows.</summary>
    public static bool IsBuiltin(string name) => Builtins.Contains(name);

    /// <summary>Variables other code can change at any call; they are never treated as known.</summary>
    public IReadOnlySet<string> Volatile { get; init; } = new HashSet<string>();

    /// <summary>Collections that are aliased or handed on, so their length can change out of sight.</summary>
    public IReadOnlySet<string> Escaping { get; init; } = new HashSet<string>();

    /// <summary>The function's own variables; anything else belongs to other code, which any call may change.</summary>
    public IReadOnlySet<string>? Locals { get; init; }

    /// <summary>What a call to one of the program's own functions can return, from that function's summary; null when not known.</summary>
    public Func<Call, AbstractValue?>? CallReturns { get; init; }

    /// <summary>Types the program declared for its variables; in Java and C# a variable can only ever hold its declared type.</summary>
    public IReadOnlyDictionary<string, IrType> DeclaredTypes { get; init; } = new Dictionary<string, IrType>();

    /// <summary>
    /// The classes and structs the program defines itself. A Stack the program writes is its own object, whatever it
    /// is called: its push and pop do what its code says, not what java.util.Stack's do.
    /// </summary>
    public IReadOnlySet<string> OwnTypes { get; init; } = new HashSet<string>();

    /// <summary>
    /// In Java and C# a field can be written this.count or just count; both mean the same field unless a local variable
    /// of that name hides it, so both are followed under the one name.
    /// </summary>
    private string? FieldName(Expr expression) =>
        !IsPython && expression is Member { Target: Name { Identifier: "this" }, MemberName: var field } && Locals is not null && !Locals.Contains(field)
            ? field
            : null;

    /// <summary>
    /// What is now known about the object a name holds - after a change to it, or a test of it - which is as true of every
    /// other name holding the same object. Giving a name a new object is <see cref="Rebind"/>.
    /// </summary>
    public AbstractState Store(AbstractState state, string name, AbstractValue value)
    {
        var sharing = state.AliasesOf(name).ToList();

        state = StoreOne(state, name, value);
        foreach (var alias in sharing) state = StoreOne(state, alias, value);
        return state;
    }

    /// <summary>A name given a new value: whatever object it shared before, it shares no longer.</summary>
    public AbstractState Rebind(AbstractState state, string name, AbstractValue value) => StoreOne(state.Unaliased(name), name, value);

    private AbstractState StoreOne(AbstractState state, string name, AbstractValue value)
    {
        if (Volatile.Contains(name)) return state.Without(name);
        if (Escaping.Contains(name) && value.MayBe(Shared)) value = value with { Length = value.Length.Join(Interval.NonNegative) };
        return state.With(name, value);
    }

    /// <summary>The kinds of collection whose length other code can change once it has been handed one.</summary>
    private ValueKind Shared => Failures.ListsKeepTheirLength(language)
        ? ValueKind.Set | ValueKind.Dictionary
        : ValueKind.List | ValueKind.Set | ValueKind.Dictionary;

    private bool IsPython => language == SourceLanguage.Python;

    public AbstractValue Evaluate(Expr expression, AbstractState state) => expression switch
    {
        Literal literal => Constant(literal),
        Cast cast => CastValue(cast, state),
        Member field when FieldName(field) is { } named && state.Knows(named) => state[named],
        Member { MemberName: "length" or "Length" or "Count" } measured when Evaluate(measured.Target, state).IsOnly(ValueKind.Sized) =>
            AbstractValue.Integer(Evaluate(measured.Target, state).Length.Meet(Interval.NonNegative)),
        Name name => state.Knows(name.Identifier) ? state[name.Identifier]
            : IsPython && Builtins.Contains(name.Identifier) ? AbstractValue.Of(ValueKind.Function)
            : AbstractValue.Unknown,
        Unary unary => UnaryValue(unary, state),
        Binary binary => BinaryValue(binary, state),
        Conditional { Test: Binary { Operator: BinaryOperator.NotEqual, Left: var tested, Right: Literal { Kind: LiteralKind.Null } }, WhenTrue: var kept } coalesced
            when ReferenceEquals(tested, kept) =>
            Evaluate(kept, Assume(state, coalesced.Test, true)).WithoutNull().Join(Evaluate(coalesced.WhenFalse, Assume(state, coalesced.Test, false))),
        Conditional choice => Evaluate(choice.WhenTrue, Assume(state, choice.Test, true))
            .Join(Evaluate(choice.WhenFalse, Assume(state, choice.Test, false))),
        Call call => CallValue(call, state),
        ElementAccess element => ElementValue(Evaluate(element.Target, state)),
        Slice slice => Evaluate(slice.Target, state) is var sliced && sliced.IsOnly(ValueKind.Text | ValueKind.List | ValueKind.Tuple)
            ? sliced with { Length = new Interval(0, sliced.Length.High) }
            : AbstractValue.Unknown,
        NewObject created => NewValue(created, state),
        CollectionLiteral collection => AbstractValue.Sized(collection.Kind switch
        {
            CollectionKind.List or CollectionKind.Array => ValueKind.List,
            CollectionKind.Tuple => ValueKind.Tuple,
            CollectionKind.Set => ValueKind.Set,
            _ => ValueKind.Dictionary,
        }, Interval.Exactly(collection.Items.Count)),
        AssignValue { ValueBeforeAssigning: true } assigned => Evaluate(assigned.Target, state).Join(Evaluate(assigned.Value, state)),
        AssignValue assigned => Evaluate(assigned.Value, state),
        MoreItems => AbstractValue.Boolean,
        NextItem next => ItemOf(Evaluate(next.Items, state)),
        Opaque opaque => OpaqueValue(opaque),
        _ => AbstractValue.Unknown,
    };

    private AbstractValue Constant(Literal literal) => literal.Kind switch
    {
        LiteralKind.Character when !IsPython && literal.Value is string { Length: 1 } character =>
            AbstractValue.Integer(Interval.Exactly(character[0])),
        LiteralKind.Integer => AbstractValue.Integer(Interval.Exactly(Convert.ToDouble(literal.Value))),
        LiteralKind.Real => AbstractValue.Real(Interval.Exactly(Convert.ToDouble(literal.Value))),
        LiteralKind.Boolean => new AbstractValue(ValueKind.Boolean, Interval.Exactly(literal.Value is true ? 1 : 0), Interval.Empty),
        LiteralKind.Text => AbstractValue.Text(Interval.Exactly(((string?)literal.Value ?? "").Length)),
        LiteralKind.Character => AbstractValue.Text(Interval.Exactly(1)),
        _ => AbstractValue.Null,
    };

    private static AbstractValue OpaqueValue(Opaque opaque) => opaque.What switch
    {
        "formatted text" => AbstractValue.Text(Interval.NonNegative),
        "list comprehension" or "a list" => AbstractValue.Of(ValueKind.List),
        "an object" => AbstractValue.Of(ValueKind.Dictionary),
        "set comprehension" => AbstractValue.Of(ValueKind.Set),
        "dictionary comprehension" => AbstractValue.Of(ValueKind.Dictionary),
        "lambda" or "function" => AbstractValue.Of(ValueKind.Function),
        "class" or "generator" or "AssertionError" or "lambda expression" or "anonymous class" => AbstractValue.Of(ValueKind.Object),
        "instanceof" or "is pattern" => AbstractValue.Boolean,
        "typed whole number" or "position" => AbstractValue.Integer(Interval.Top),
        "typed number" => AbstractValue.Real(Interval.Top),
        "typed text" => AbstractValue.Text(Interval.NonNegative),
        var what when what.StartsWith("module ", StringComparison.Ordinal) => AbstractValue.Of(ValueKind.Module),
        var what when what.StartsWith("caught ", StringComparison.Ordinal) => AbstractValue.Of(ValueKind.Object),
        _ => AbstractValue.Unknown,
    };

    private AbstractValue UnaryValue(Unary unary, AbstractState state)
    {
        var operand = Evaluate(unary.Operand, state);

        return unary.Operator switch
        {
            UnaryOperator.Not => AbstractValue.Boolean,
            UnaryOperator.Negate when operand.IsNumber => Numeric(operand.Kinds, operand.Number.Negate()),
            UnaryOperator.Plus when operand.IsNumber => operand,
            UnaryOperator.BitNot when operand.IsNumber => AbstractValue.Integer(Interval.Top),
            _ => AbstractValue.Unknown,
        };
    }

    private AbstractValue CastValue(Cast cast, AbstractState state)
    {
        var value = Evaluate(cast.Value, state);
        var target = FromType(cast.Type);

        if (target.IsOnly(ValueKind.Integer) && value.IsNumber) return AbstractValue.Integer(value.Number.Truncate());
        if (target.IsOnly(ValueKind.Real) && value.IsNumber) return AbstractValue.Real(value.Number);
        return target.IsUnknown ? value : target with { NullnessKnown = value.NullnessKnown && !value.MayBeNull, Kinds = target.Kinds | (value.Kinds & ValueKind.Null) };
    }

    private AbstractValue NewValue(NewObject created, AbstractState state)
    {
        var name = created.Type.Name;
        var arguments = created.Arguments.Select(a => Evaluate(a.Value, state)).ToList();

        if (name == "array")
            return AbstractValue.Sized(ValueKind.List, arguments is [{ IsNumber: true } size] ? size.Number.Meet(Interval.NonNegative) : Interval.NonNegative);

        var kind = OwnTypes.Contains(name) ? ValueKind.Nothing : name switch
        {
            "ArrayList" or "LinkedList" or "List" or "Vector" or "Stack" or "ArrayDeque" or "Queue" or "LinkedHashSet" => ValueKind.List,
            "HashMap" or "TreeMap" or "LinkedHashMap" or "Dictionary" or "SortedDictionary" or "Hashtable" => ValueKind.Dictionary,
            "HashSet" or "TreeSet" or "SortedSet" => ValueKind.Set,
            "String" or "string" => ValueKind.Text,
            _ => ValueKind.Nothing,
        };

        if (kind == ValueKind.Nothing) return AbstractValue.Of(ValueKind.Object);
        if (kind == ValueKind.Text) return AbstractValue.Text(Interval.NonNegative);

        var copiesAnother = arguments.Any(a => a.MayBe(ValueKind.Sized));
        return AbstractValue.Sized(kind, copiesAnother ? Interval.NonNegative : Interval.Exactly(0));
    }

    /// <summary>
    /// Outside Python a whole number that grows past what an int holds wraps round to a negative one - which is exactly
    /// what overflow checks like (offset + length) &lt; 0 look for. A result could wrap when a finite end passes an int's
    /// limit, or when two values with no limit are combined; a counter with no limit going up by one is not evidence.
    /// </summary>
    private Interval Wrapped(ValueKind kinds, Interval left, Interval right, Interval result, bool multiplying = false)
    {
        if (!Failures.NumbersWrapRound(language) || (kinds & ValueKind.Real) != 0 || result.IsEmpty) return result;

        var passesALimit = !double.IsInfinity(result.Low) && result.Low < int.MinValue || !double.IsInfinity(result.High) && result.High > int.MaxValue;
        var bothUnlimited = multiplying
            ? (Unlimited(left) && Grows(right)) || (Unlimited(right) && Grows(left))
            : double.IsPositiveInfinity(left.High) && double.IsPositiveInfinity(right.High) || double.IsNegativeInfinity(left.Low) && double.IsNegativeInfinity(right.Low);

        return passesALimit || bothUnlimited ? Interval.Top : result;

        static bool Unlimited(Interval range) => double.IsInfinity(range.Low) || double.IsInfinity(range.High);
        static bool Grows(Interval factor) => !(factor.IsExact && Math.Abs(factor.Low) <= 1);
    }

    private static AbstractValue Numeric(ValueKind kinds, Interval range) =>
        (kinds & ValueKind.Real) != 0 ? AbstractValue.Real(range) : AbstractValue.Integer(range);

    private AbstractValue BinaryValue(Binary binary, AbstractState state)
    {
        if (binary.Operator is BinaryOperator.And or BinaryOperator.Or)
        {
            if (!IsPython) return AbstractValue.Boolean;
            var kept = TruthOf(Evaluate(binary.Left, state), holds: binary.Operator == BinaryOperator.Or);
            var otherwise = Evaluate(binary.Right, Assume(state, binary.Left, binary.Operator == BinaryOperator.And));
            return kept.IsImpossible ? otherwise : kept.Join(otherwise);
        }

        if (binary.Operator is BinaryOperator.Equal or BinaryOperator.NotEqual or BinaryOperator.Less or BinaryOperator.LessOrEqual
            or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual or BinaryOperator.In or BinaryOperator.NotIn
            or BinaryOperator.Is or BinaryOperator.IsNot)
            return AbstractValue.Boolean;

        var left = Evaluate(binary.Left, state);
        var right = Evaluate(binary.Right, state);

        if (left.IsNumber && right.IsNumber)
        {
            var real = ((left.Kinds | right.Kinds) & ValueKind.Real) != 0;
            var kinds = real ? ValueKind.Real : ValueKind.Integer;

            return binary.Operator switch
            {
                BinaryOperator.Add => Numeric(kinds, Wrapped(kinds, left.Number, right.Number, left.Number.Add(right.Number))),
                BinaryOperator.Subtract => Numeric(kinds, Wrapped(kinds, left.Number, right.Number.Negate(), left.Number.Subtract(right.Number))),
                BinaryOperator.Multiply => Numeric(kinds, Wrapped(kinds, left.Number, right.Number, left.Number.Multiply(right.Number), multiplying: true)),
                BinaryOperator.Divide when Failures.DivisionGivesReal(language) || real => AbstractValue.Real(left.Number.Divide(right.Number)),
                BinaryOperator.Divide => AbstractValue.Integer(left.Number.Divide(right.Number).Truncate()),
                BinaryOperator.FloorDivide => Numeric(kinds, left.Number.Divide(right.Number).Floor()),
                BinaryOperator.Modulo => Numeric(kinds, left.Number.Modulo(right.Number, signFollowsDivisor: IsPython)),
                _ => Numeric(kinds, Interval.Top),
            };
        }

        if (binary.Operator == BinaryOperator.Add)
        {
            if (left.IsOnly(ValueKind.Text) && right.IsOnly(ValueKind.Text)) return AbstractValue.Text(left.Length.Add(right.Length));
            if (!IsPython && (left.IsOnly(ValueKind.Text) || right.IsOnly(ValueKind.Text))) return AbstractValue.Text(Interval.NonNegative);
            if (left.IsOnly(ValueKind.List) && right.IsOnly(ValueKind.List)) return AbstractValue.Sized(ValueKind.List, left.Length.Add(right.Length));
            if (left.IsOnly(ValueKind.Tuple) && right.IsOnly(ValueKind.Tuple)) return AbstractValue.Sized(ValueKind.Tuple, left.Length.Add(right.Length));
        }

        if (binary.Operator == BinaryOperator.Multiply && IsPython)
        {
            var (sized, count) = left.IsOnly(ValueKind.Text | ValueKind.List) && right.IsOnly(ValueKind.Integer | ValueKind.Boolean) ? (left, right)
                : right.IsOnly(ValueKind.Text | ValueKind.List) && left.IsOnly(ValueKind.Integer | ValueKind.Boolean) ? (right, left)
                : (null, null);

            if (sized is not null)
                return AbstractValue.Sized(sized.Kinds, sized.Length.Multiply(count!.Number.Meet(Interval.NonNegative)).Meet(Interval.NonNegative));
        }

        return AbstractValue.Unknown;
    }

    private AbstractValue CallValue(Call call, AbstractState state)
    {
        if (CallReturns?.Invoke(call) is { } summarised) return summarised;

        var arguments = call.Arguments.Where(a => a.Name is null).Select(a => Evaluate(a.Value, state)).ToList();

        if (call.Callee is Name { Identifier: var function } && IsPython && !state.Knows(function))
            return BuiltinCall(function, arguments, call);

        // Asking for memory can come back with nothing, which is why C code checks what it was given.
        if (language is SourceLanguage.C or SourceLanguage.Cpp && call.Callee is Name { Identifier: var asked } &&
            asked is "malloc" or "calloc" or "realloc" or "strdup" or "strndup")
            return AbstractValue.Of(ValueKind.Object).Join(AbstractValue.Null);

        if (!IsPython && call.Callee is Member { Target: Name { Identifier: var type }, MemberName: var helper } && !state.Knows(type) &&
            StaticHelper(type, helper, arguments, call) is { } helped)
            return helped;

        if (call.Callee is Member { Target: var receiverExpression, MemberName: var method })
        {
            var receiver = Evaluate(receiverExpression, state);

            if (receiverExpression is Name { Identifier: "re" } && (receiver.IsOnly(ValueKind.Module) || !state.Knows("re")) &&
                method is "match" or "search" or "fullmatch")
                return AbstractValue.Of(ValueKind.Object).Join(AbstractValue.Null);

            if (receiver.IsOnly(ValueKind.Text))
                return IsPython ? TextMethod(method) : ManagedTextMethod(method, receiver);

            if (!IsPython && receiver.IsOnly(ValueKind.List | ValueKind.Set | ValueKind.Dictionary))
            {
                if (method is "size" or "Count") return AbstractValue.Integer(receiver.Length.Meet(Interval.NonNegative));
                if (method is "isEmpty" or "contains" or "containsKey" or "Contains" or "ContainsKey" or "Any") return AbstractValue.Boolean;
                if (method is "indexOf" or "IndexOf") return AbstractValue.Integer(new Interval(-1, double.PositiveInfinity));
            }

            if (receiver.IsOnly(ValueKind.Dictionary) && method == "get")
                return call.Arguments.Count >= 2 ? AbstractValue.Unknown.Join(Evaluate(call.Arguments[1].Value, state)) : AbstractValue.Unknown.Join(AbstractValue.Null);

            if (receiver.IsOnly(ValueKind.List | ValueKind.Set | ValueKind.Dictionary))
            {
                if (IsPython && method is "append" or "extend" or "insert" or "remove" or "sort" or "reverse" or "clear" or "add" or "discard" or "update")
                    return AbstractValue.Null;
                if (!IsPython && method is "add" or "remove" or "addAll" or "removeAll" or "Remove" or "offer")
                    return AbstractValue.Boolean;
                if (method is "index" or "count") return AbstractValue.Integer(Interval.NonNegative);
                if (method == "copy") return receiver;
            }
        }

        return AbstractValue.Unknown;
    }

    /// <summary>Java and C# library calls on a type rather than a value: Math.max, int.Parse, String.valueOf.</summary>
    private static AbstractValue? StaticHelper(string type, string method, IReadOnlyList<AbstractValue> arguments, Call call) => (type, method) switch
    {
        ("Math", "Abs" or "abs") => BuiltinCall("abs", arguments, call),
        ("Math", "Max" or "max") => BuiltinCall("max", arguments, call),
        ("Math", "Min" or "min") => BuiltinCall("min", arguments, call),
        ("int" or "long" or "short" or "Int32" or "Int64" or "Integer" or "Long", "Parse" or "parseInt" or "parseLong" or "valueOf") =>
            AbstractValue.Integer(Interval.Top),
        ("double" or "float" or "decimal" or "Double" or "Float" or "Decimal", "Parse" or "parseDouble" or "parseFloat" or "valueOf") =>
            AbstractValue.Real(Interval.Top),
        (_, "TryParse" or "IsNullOrEmpty" or "IsNullOrWhiteSpace") => AbstractValue.Boolean,
        ("Math", "floor" or "ceil" or "round" or "trunc") => AbstractValue.Integer(Interval.Top),
        ("Math", "random") => AbstractValue.Real(new Interval(0, 1)),
        ("Array", "isArray") => AbstractValue.Boolean,
        ("Object", "keys" or "values" or "entries") => AbstractValue.Sized(ValueKind.List, Interval.NonNegative),
        ("JSON", "stringify") => AbstractValue.Text(Interval.NonNegative),
        ("string" or "String", "Join" or "Format" or "Concat" or "join" or "format" or "valueOf") => AbstractValue.Text(Interval.NonNegative),
        _ => null,
    };

    private static AbstractValue ManagedTextMethod(string method, AbstractValue text) => method switch
    {
        "length" => AbstractValue.Integer(text.Length.Meet(Interval.NonNegative)),
        "charAt" => AbstractValue.Integer(new Interval(0, 65535)),
        "indexOf" or "lastIndexOf" or "IndexOf" or "LastIndexOf" => AbstractValue.Integer(new Interval(-1, double.PositiveInfinity)),
        "compareTo" or "CompareTo" or "compareToIgnoreCase" => AbstractValue.Integer(Interval.Top),
        "equals" or "equalsIgnoreCase" or "Equals" or "isEmpty" or "isBlank" or "contains" or "Contains" or "startsWith" or "StartsWith"
            or "endsWith" or "EndsWith" or "matches" or "includes" => AbstractValue.Boolean,
        "split" or "Split" or "toCharArray" or "ToCharArray" => AbstractValue.Sized(ValueKind.List, Interval.NonNegative),
        _ => AbstractValue.Text(Interval.NonNegative),
    };

    private static AbstractValue TextMethod(string method) => method switch
    {
        "split" or "splitlines" or "rsplit" => AbstractValue.Sized(ValueKind.List, new Interval(0, double.PositiveInfinity)),
        "find" or "rfind" => AbstractValue.Integer(new Interval(-1, double.PositiveInfinity)),
        "index" or "rindex" or "count" => AbstractValue.Integer(Interval.NonNegative),
        "startswith" or "endswith" or "isdigit" or "isalpha" or "isalnum" or "isspace" or "isupper" or "islower" or "isnumeric"
            or "isdecimal" or "istitle" => AbstractValue.Boolean,
        "encode" => AbstractValue.Of(ValueKind.Object),
        _ => AbstractValue.Text(Interval.NonNegative),
    };

    private static AbstractValue BuiltinCall(string function, IReadOnlyList<AbstractValue> arguments, Call call)
    {
        var first = arguments.Count > 0 ? arguments[0] : AbstractValue.Unknown;

        switch (function)
        {
            case "len":
                return AbstractValue.Integer(first.MayBe(ValueKind.Sized) ? first.Length.Meet(Interval.NonNegative) : Interval.NonNegative);
            case "int":
                return AbstractValue.Integer(first.IsNumber ? first.Number.Truncate() : Interval.Top);
            case "float":
                return AbstractValue.Real(first.IsNumber ? first.Number : Interval.Top);
            case "round":
                return AbstractValue.Integer(first.IsNumber ? first.Number.Floor().Join(first.Number.Floor().Add(Interval.Exactly(1))) : Interval.Top);
            case "abs" when first.IsNumber:
                var magnitude = first.Number.Low >= 0 ? first.Number : first.Number.High <= 0 ? first.Number.Negate()
                    : new Interval(0, Math.Max(-first.Number.Low, first.Number.High));
                return Numeric(first.Kinds, magnitude);
            case "min" or "max" when arguments.Count >= 2 && arguments.All(a => a.IsNumber):
                var low = function == "min" ? arguments.Min(a => a.Number.Low) : arguments.Max(a => a.Number.Low);
                var high = function == "min" ? arguments.Min(a => a.Number.High) : arguments.Max(a => a.Number.High);
                return Numeric(arguments.Aggregate(ValueKind.Nothing, (k, a) => k | a.Kinds), new Interval(low, high));
            case "str" or "repr" or "input" or "chr" or "format":
                return AbstractValue.Text(function == "chr" ? Interval.Exactly(1) : Interval.NonNegative);
            case "bool" or "isinstance" or "any" or "all":
                return AbstractValue.Boolean;
            case "ord" or "hash" or "id":
                return AbstractValue.Integer(Interval.Top);
            case "print":
                return AbstractValue.Null;
            case "range":
                return RangeOf(arguments);
            case "list" or "sorted" or "tuple" or "set":
                var kind = function switch { "tuple" => ValueKind.Tuple, "set" => ValueKind.Set, _ => ValueKind.List };
                return AbstractValue.Sized(kind, first.MayBe(ValueKind.Sized) && call.Arguments.Count > 0 ? first.Length.Meet(Interval.NonNegative) : Interval.NonNegative);
            case "dict":
                return AbstractValue.Of(ValueKind.Dictionary);
            case "open":
                return AbstractValue.Of(ValueKind.File);
            default:
                return AbstractValue.Unknown;
        }
    }

    private static AbstractValue RangeOf(IReadOnlyList<AbstractValue> arguments)
    {
        if (arguments.Count == 0 || arguments.Any(a => !a.IsNumber)) return AbstractValue.Of(ValueKind.Range);

        var (start, stop) = arguments.Count == 1 ? (Interval.Exactly(0), arguments[0].Number) : (arguments[0].Number, arguments[1].Number);
        var stepsUp = arguments.Count < 3 || arguments[2].Number.Low > 0;

        if (!stepsUp) return AbstractValue.Of(ValueKind.Range);

        var items = new Interval(start.Low, stop.High - 1);
        var count = stop.Subtract(start).Meet(Interval.NonNegative);
        if (count.IsEmpty) count = Interval.Exactly(0);

        return new AbstractValue(ValueKind.Range, arguments.Count < 3 ? items : new Interval(start.Low, stop.High - 1), arguments.Count < 3 ? count : Interval.NonNegative);
    }

    private static AbstractValue ElementValue(AbstractValue target) =>
        target.IsOnly(ValueKind.Text) ? AbstractValue.Text(Interval.Exactly(1))
        : target.IsOnly(ValueKind.Range) ? AbstractValue.Integer(target.Number)
        : AbstractValue.Unknown;

    private static AbstractValue ItemOf(AbstractValue items) =>
        items.IsOnly(ValueKind.Range) ? AbstractValue.Integer(items.Number.IsEmpty ? Interval.Top : items.Number)
        : items.IsOnly(ValueKind.Text) ? AbstractValue.Text(Interval.Exactly(1))
        : items.IsOnly(ValueKind.File) ? AbstractValue.Text(Interval.NonNegative)
        : AbstractValue.Unknown;

    public AbstractValue FromType(IrType type)
    {
        var value = OwnTypes.Contains(type.Name) ? AbstractValue.Of(ValueKind.Object) : type.Name switch
        {
            "byte" when language is SourceLanguage.CSharp or SourceLanguage.Go or SourceLanguage.C or SourceLanguage.Cpp =>
                AbstractValue.Integer(new Interval(0, 255)),
            "byte" or "Byte" or "sbyte" => AbstractValue.Integer(new Interval(-128, 127)),
            "short" or "Short" or "Int16" => AbstractValue.Integer(new Interval(short.MinValue, short.MaxValue)),
            "ushort" or "UInt16" => AbstractValue.Integer(new Interval(0, ushort.MaxValue)),
            "uint" or "UInt32" => AbstractValue.Integer(new Interval(0, uint.MaxValue)),
            "ulong" or "UInt64" => AbstractValue.Integer(new Interval(0, double.PositiveInfinity)),
            "int" or "long" or "Integer" or "Long" or "Int32" or "Int64" => AbstractValue.Integer(Interval.Top),
            "char" or "Character" => AbstractValue.Integer(new Interval(0, 65535)),
            "array" => AbstractValue.Of(ValueKind.List),
            "float" or "double" or "Double" or "Float" or "decimal" => AbstractValue.Real(Interval.Top),
            "bool" or "boolean" or "Boolean" => AbstractValue.Boolean,
            "str" or "string" or "String" => AbstractValue.Text(Interval.NonNegative),
            "list" or "List" or "ArrayList" => AbstractValue.Of(ValueKind.List),
            "dict" or "Dictionary" or "Map" or "HashMap" => AbstractValue.Of(ValueKind.Dictionary),
            "set" or "HashSet" or "Set" => AbstractValue.Of(ValueKind.Set),
            "tuple" => AbstractValue.Of(ValueKind.Tuple),
            "None" => AbstractValue.Null,
            _ => AbstractValue.Unknown,
        };

        if (IsPython || !IsPrimitive(type.Name)) value = value with { NullnessKnown = false };
        return type.Nullable ? value.Join(AbstractValue.Null) : value;
    }

    /// <summary>
    /// A number as the variable or function holding it declares it. A whole number kept in a double is a fraction from
    /// then on - 10 / d gives infinity rather than failing when d is 0 - and a fraction kept in a C int loses what is
    /// after the point. Python's hints convert nothing, so there a number stays as it was written.
    /// </summary>
    public AbstractValue AsDeclared(AbstractValue value, IrType declared)
    {
        if (IsPython || !value.IsNumber) return value;
        if (declared.IsFloatingPoint) return AbstractValue.Real(value.Number);

        var truncates = language is SourceLanguage.C or SourceLanguage.Cpp && value.IsOnly(ValueKind.Real) && FromType(declared).IsOnly(ValueKind.Integer);
        return truncates ? AbstractValue.Integer(value.Number.Truncate()) : value;
    }

    /// <summary>Java and C# value types, which can never hold null.</summary>
    private static bool IsPrimitive(string name) => name is "int" or "long" or "short" or "byte" or "double" or "float" or "boolean" or "bool"
        or "char" or "decimal" or "uint" or "ulong" or "sbyte" or "ushort";

    public AbstractState Apply(AbstractState state, Instruction instruction)
    {
        if (!state.IsReachable) return state;

        switch (instruction)
        {
            case AssignInstruction assign:
                state = Embedded(state, assign.Value);
                if (assign.Target is not Name) state = Embedded(state, assign.Target);
                var assigned = Evaluate(Settled(assign.Value), state);
                return AssignTo(ForgetOthersAfterCalls(state, assign.Value), assign.Target, assigned, assign.Value);

            case EvaluateInstruction { Value: Name or Member }:
                return ForgetOthers(state);

            case EvaluateInstruction evaluate:
                state = Embedded(state, evaluate.Value);
                return ForgetOthersAfterCalls(AfterCall(state, evaluate.Value), evaluate.Value);

            case ReleaseInstruction:
                return ForgetOthers(state);

            case DeclareInstruction declare:
                return Rebind(state, declare.Variable, FromType(declare.Type));

            case ForgetInstruction forget:
                return forget.Names.Aggregate(state, (s, name) => s.Without(name));

            default:
                return state;
        }
    }

    private AbstractState AssignTo(AbstractState state, Expr target, AbstractValue value, Expr? valueExpression)
    {
        switch (target)
        {
            case Name name:
                if (!IsPython && DeclaredTypes.TryGetValue(name.Identifier, out var declared))
                    value = value.IsUnknown ? FromType(declared) : AsDeclared(value, declared);

                // b = a: both names now hold the one object, so a change made through either is a change to both.
                if (valueExpression is Name { Identifier: var source } && source != name.Identifier && Tracked(source) && Tracked(name.Identifier))
                    return Rebind(state, name.Identifier, value).Aliasing(name.Identifier, source);

                // items += more extends a Python list in place, where items = items + more makes a new one - and once
                // lowered the two look alike. For a list that other names share, neither is guessed: their lengths are forgotten.
                if (IsPython && valueExpression is Binary { Left: Name { Identifier: var extended } } && extended == name.Identifier &&
                    state.AliasesOf(name.Identifier).Any() && state[name.Identifier].MayBe(ValueKind.List))
                {
                    state = Store(state, name.Identifier, state[name.Identifier] with { Length = Interval.NonNegative });
                }

                return Rebind(state, name.Identifier, value);

            case CollectionLiteral { Kind: CollectionKind.Tuple or CollectionKind.List } unpacked:
                if (valueExpression is CollectionLiteral { Items.Count: var count } packed && count == unpacked.Items.Count)
                {
                    var values = packed.Items.Select(item => Evaluate(item, state)).ToList();
                    for (var i = 0; i < count; i++) state = AssignTo(state, unpacked.Items[i], values[i], null);
                    return state;
                }
                return unpacked.Items.Aggregate(state, (s, item) => AssignTo(s, item, AbstractValue.Unknown, null));

            case Member field when FieldName(field) is { } named:
                return Rebind(state, named, value);

            case ElementAccess { Target: Name owner } when state[owner.Identifier].IsOnly(ValueKind.Dictionary):
                var dictionary = state[owner.Identifier];
                return Store(state, owner.Identifier, dictionary with { Length = new Interval(dictionary.Length.Low, dictionary.Length.High + 1) });

            default:
                return state;
        }
    }

    /// <summary>Whether a name can be followed as sharing an object: not one that other code can change at any call.</summary>
    private bool Tracked(string name) => !Volatile.Contains(name);

    /// <summary>What was learned about variables that are not the function's own is lost once other code has run.</summary>
    private AbstractState ForgetOthersAfterCalls(AbstractState state, Expr expression) =>
        CallsOtherCode(expression) ? ForgetOthers(state) : state;

    /// <summary>
    /// Entering or leaving a with block runs the resource's own code too - and a lock is where other threads come in. The
    /// hidden copies the code itself makes - the collection a loop walks - are the function's own and no other code can
    /// change them, unless they share their object with something that is forgotten.
    /// </summary>
    private AbstractState ForgetOthers(AbstractState state)
    {
        if (Locals is null) return state;

        foreach (var name in state.Names.Where(name => !Locals.Contains(name) && !IsHidden(name)).ToList())
        {
            var hiddenCopies = state.AliasesOf(name).Where(IsHidden).ToList();
            state = hiddenCopies.Aggregate(state.Without(name), (s, copy) => s.Without(copy));
        }

        return state;
    }

    /// <summary>The control-flow graph's own temporaries - $items0, $result2 - which the program never names.</summary>
    private static bool IsHidden(string name) => name.StartsWith('$');

    private bool CallsOtherCode(Expr expression) =>
        expression is Call { Callee: var callee } && !(IsPython && callee is Name { Identifier: var builtin } && Builtins.Contains(builtin))
        || expression is Opaque { What: "await" or "yield" }
        || IrWalk.Children(expression).Any(CallsOtherCode);

    /// <summary>A call can change what it is called on, and anything mutable handed to an unknown function.</summary>
    private AbstractState AfterCall(AbstractState state, Expr expression)
    {
        if (expression is not Call call) return state;

        if (call.Callee is Member { Target: Name owner, MemberName: var method } && state[owner.Identifier] is var collection &&
            collection.IsOnly(ValueKind.List | ValueKind.Set | ValueKind.Dictionary))
        {
            var length = collection.Length;
            var changed = method switch
            {
                _ when Growing.Contains(method) => collection.IsOnly(ValueKind.Set) ? new Interval(length.Low, length.High + 1) : length.Add(Interval.Exactly(1)),
                _ when CertainlyShrinking.Contains(method) || IsPython && method == "remove" =>
                    length.Subtract(Interval.Exactly(1)).Meet(Interval.NonNegative) is { IsEmpty: false } fewer ? fewer : Interval.Exactly(0),
                _ when MaybeShrinking.Contains(method) => new Interval(Math.Max(0, length.Low - 1), length.High),
                "clear" => Interval.Exactly(0),
                "extend" or "update" => new Interval(length.Low, double.PositiveInfinity),
                "sort" or "reverse" or "copy" or "index" or "count" or "get" or "keys" or "values" or "items" or "size" or "isEmpty"
                    or "contains" or "containsKey" or "indexOf" or "Contains" or "ContainsKey" or "IndexOf" or "Sort" or "Reverse" => length,
                _ => Interval.NonNegative,
            };
            state = Store(state, owner.Identifier, collection with { Length = changed });
        }

        foreach (var argument in call.Arguments)
        {
            if (argument.Value is Name passed && state[passed.Identifier].IsOnly(ValueKind.List | ValueKind.Set | ValueKind.Dictionary) &&
                state[passed.Identifier].MayBe(Shared) &&
                !(IsPython && call.Callee is Name { Identifier: var builtin } && Builtins.Contains(builtin)))
                state = Store(state, passed.Identifier, state[passed.Identifier] with { Length = Interval.NonNegative });
        }

        return state;
    }

    /// <summary>Assignments made inside an expression - Python's := - happen before the rest of it is used.</summary>
    private AbstractState Embedded(AbstractState state, Expr expression)
    {
        foreach (var assigned in Assignments(expression))
            state = AssignTo(Embedded(state, assigned.Value), assigned.Target, Evaluate(assigned.Value, state), assigned.Value);
        return state;
    }

    private static IEnumerable<AssignValue> Assignments(Expr expression) =>
        expression is AssignValue assigned ? [assigned] : IrWalk.Children(expression).SelectMany(Assignments);

    /// <summary>
    /// The expression as it reads once its own assignments have happened: x = y and ++i read as their target, i++ as the
    /// target less the step - so a value is never counted twice.
    /// </summary>
    private static Expr Settled(Expr expression) => expression switch
    {
        AssignValue { ValueBeforeAssigning: false } assigned => assigned.Target,
        AssignValue { Value: Binary { Operator: BinaryOperator.Add or BinaryOperator.Subtract } step } assigned =>
            new Binary(assigned.Span, step.Operator == BinaryOperator.Add ? BinaryOperator.Subtract : BinaryOperator.Add, assigned.Target, step.Right),
        AssignValue assigned => Opaque.Of(assigned.Span, "value before assignment"),
        Binary binary => binary with { Left = Settled(binary.Left), Right = Settled(binary.Right) },
        Unary unary => unary with { Operand = Settled(unary.Operand) },
        _ => expression,
    };

    /// <summary>What must be true of the variables for <paramref name="condition"/> to come out as <paramref name="holds"/>.</summary>
    public AbstractState Assume(AbstractState state, Expr condition, bool holds)
    {
        if (!state.IsReachable) return state;
        state = Embedded(state, condition);
        condition = Settled(condition);

        switch (condition)
        {
            case Literal literal:
                return Truthy(Constant(literal), language) is { } truth && truth != holds ? AbstractState.Unreachable : state;

            case Unary { Operator: UnaryOperator.Not } negation:
                return Assume(state, negation.Operand, !holds);

            case Binary { Operator: BinaryOperator.And } both when holds:
                return Assume(Assume(state, both.Left, true), both.Right, true);

            case Binary { Operator: BinaryOperator.Or } either when !holds:
                return Assume(Assume(state, either.Left, false), either.Right, false);

            case Binary { Operator: BinaryOperator.And } both:
                return Assume(state, both.Left, false).Join(Assume(Assume(state, both.Left, true), both.Right, false));

            case Binary { Operator: BinaryOperator.Or } either:
                return Assume(state, either.Left, true).Join(Assume(Assume(state, either.Left, false), either.Right, true));

            case Binary comparison when Comparison(comparison.Operator):
                return Compare(state, comparison, holds);

            case Call { Callee: Name { Identifier: "isinstance" }, Arguments: [{ Value: Name checkedName }, { Value: var typeExpression }] }
                when IsPython && KindsNamed(typeExpression) is { } kinds:
                var current = state[checkedName.Identifier];
                return Store(state, checkedName.Identifier, holds ? current.Kept(kinds) : current.Kept(~kinds));

            case Name truthName:
                return Store(state, truthName.Identifier, TruthOf(state[truthName.Identifier], holds));

            case Opaque { What: "is pattern", Parts: [var matched] } when holds && Followed(matched) is { } matchedName:
                return Store(state, matchedName, state[matchedName].WithoutNull());

            case Call { Callee: Member { Target: Name { Identifier: "string" or "String" }, MemberName: var test },
                    Arguments: [{ Value: var tested }] } when !holds && test is "IsNullOrEmpty" or "IsNullOrWhiteSpace" && Followed(tested) is { } testedName:
                var present = state[testedName].WithoutNull();
                return Store(state, testedName, test == "IsNullOrEmpty" && present.IsOnly(ValueKind.Text) ? present.WithLength(new Interval(1, double.PositiveInfinity)) : present);

            // typeof x === 'number' says what x is, and that it is something: in JavaScript only null and undefined are neither.
            case Binary { Operator: BinaryOperator.Equal } typed when holds && TypeOfTest(typed) is { } named:
                return AssumeSomething(state, named);

            case Call { Callee: Name { Identifier: "len" }, Arguments: [{ Value: Name sized }] }:
                var measured = state[sized.Identifier];
                return Store(state, sized.Identifier, measured.WithLength(holds ? new Interval(1, double.PositiveInfinity) : Interval.Exactly(0)));

            default:
                return Truthy(Evaluate(condition, state), language) is { } known && known != holds ? AbstractState.Unreachable : state;
        }
    }

    /// <summary>What a typeof test asks about, when what it is compared with rules out nothing at all.</summary>
    private static Expr? TypeOfTest(Binary comparison)
    {
        var (tested, named) = (comparison.Left, comparison.Right) switch
        {
            (Opaque { What: "typeof", Parts: [var left] }, Literal { Kind: LiteralKind.Text, Value: string what }) => (left, what),
            (Literal { Kind: LiteralKind.Text, Value: string what }, Opaque { What: "typeof", Parts: [var right] }) => (right, what),
            _ => (null, null),
        };

        // typeof null is "object", and an undefined value answers "undefined", so neither says the value is there.
        return tested is not null && named is not ("undefined" or "object") ? tested : null;
    }

    /// <summary>What must hold for a value to be something: a chain that gives nothing when its start is nothing must have got past it.</summary>
    private AbstractState AssumeSomething(AbstractState state, Expr expression)
    {
        switch (expression)
        {
            case Conditional { WhenFalse: Literal { Kind: LiteralKind.Null } } choice:
                return AssumeSomething(Assume(state, choice.Test, true), choice.WhenTrue);

            case Member member when Followed(expression) is null:
                return AssumeSomething(state, member.Target);

            default:
                return Followed(expression) is { } name ? Store(state, name, state[name].WithoutNull()) : state;
        }
    }

    private static bool Comparison(BinaryOperator op) =>
        op is BinaryOperator.Equal or BinaryOperator.NotEqual or BinaryOperator.Less or BinaryOperator.LessOrEqual
            or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual or BinaryOperator.Is or BinaryOperator.IsNot;

    private static ValueKind? KindsNamed(Expr typeExpression) => typeExpression switch
    {
        Name { Identifier: "int" } => ValueKind.Integer | ValueKind.Boolean,
        Name { Identifier: "float" } => ValueKind.Real,
        Name { Identifier: "str" } => ValueKind.Text,
        Name { Identifier: "bool" } => ValueKind.Boolean,
        Name { Identifier: "list" } => ValueKind.List,
        Name { Identifier: "dict" } => ValueKind.Dictionary,
        Name { Identifier: "tuple" } => ValueKind.Tuple,
        Name { Identifier: "set" } => ValueKind.Set,
        CollectionLiteral { Kind: CollectionKind.Tuple } several => several.Items.Select(KindsNamed).Aggregate(
            (ValueKind?)ValueKind.Nothing, (all, one) => all is null || one is null ? null : all | one),
        _ => null,
    };

    /// <summary>Whether the value is certainly truthy (true), certainly falsy (false), or could be either (null).</summary>
    public static bool? Truthy(AbstractValue value, SourceLanguage language = SourceLanguage.Python)
    {
        var emptyIsFalse = Failures.EmptyIsFalse(language);

        if (value.IsImpossible) return null;
        if (value.IsNull) return false;
        if (value.IsOnly(ValueKind.Numeric) && value.Number.IsExact) return value.Number.Low != 0;
        if (value.IsOnly(ValueKind.Numeric) && !value.Number.Contains(0)) return true;
        if (!emptyIsFalse && value.IsOnly(ValueKind.List | ValueKind.Tuple | ValueKind.Set | ValueKind.Dictionary | ValueKind.Object)) return true;
        if (value.IsOnly(ValueKind.Sized) && value.Length.IsExact && value.Length.Low == 0) return emptyIsFalse ? false : null;
        if (value.IsOnly(ValueKind.Sized) && value.Length.Low > 0) return true;
        if (value.IsOnly(ValueKind.Function | ValueKind.Module | ValueKind.File)) return true;
        return null;
    }

    /// <summary>
    /// Narrows each kind separately: a truthy number is not 0 and a truthy collection is not empty, while a falsy value
    /// can only be 0, empty or null.
    /// </summary>
    private AbstractValue TruthOf(AbstractValue value, bool holds)
    {
        if (!Failures.EmptyIsFalse(language))
        {
            if (!holds) return value.Kept(ValueKind.Numeric | ValueKind.Text | ValueKind.Null) is var falsy && falsy.IsImpossible ? value : TruthOfFalsy(falsy);

            // Anything that is not a number, a text or nothing is true in JavaScript, however empty it is.
            var certain = value.Kept(~(ValueKind.Numeric | ValueKind.Text | ValueKind.Null));
            var narrowed = value.Kept(ValueKind.Numeric | ValueKind.Text);
            if (!narrowed.IsImpossible) narrowed = TruthOfTruthy(narrowed);
            return certain.IsImpossible ? narrowed : narrowed.IsImpossible ? certain : certain.Join(narrowed);
        }

        var kinds = holds ? value.Kinds & ~ValueKind.Null : value.Kinds & (ValueKind.Numeric | ValueKind.Sized | ValueKind.Null | ValueKind.Object);
        var number = value.Number;
        var length = value.Length;

        if ((kinds & ValueKind.Numeric) != 0)
        {
            number = holds ? WithoutZero(number, (kinds & ValueKind.Real) == 0) : number.Meet(Interval.Exactly(0));
            if (number.IsEmpty) kinds &= ~ValueKind.Numeric;
        }

        if ((kinds & ValueKind.Sized) != 0)
        {
            length = length.Meet(holds ? new Interval(1, double.PositiveInfinity) : Interval.Exactly(0));
            if (length.IsEmpty) kinds &= ~ValueKind.Sized;
        }

        return new AbstractValue(kinds, number, length) { NullnessKnown = value.NullnessKnown };
    }

    /// <summary>A number that is not 0, or a text that is not empty.</summary>
    private static AbstractValue TruthOfTruthy(AbstractValue value)
    {
        var kinds = value.Kinds;
        var number = (kinds & ValueKind.Numeric) != 0 ? WithoutZero(value.Number, (kinds & ValueKind.Real) == 0) : value.Number;
        var length = (kinds & ValueKind.Text) != 0 ? value.Length.Meet(new Interval(1, double.PositiveInfinity)) : value.Length;
        if ((kinds & ValueKind.Numeric) != 0 && number.IsEmpty) kinds &= ~ValueKind.Numeric;
        if ((kinds & ValueKind.Text) != 0 && length.IsEmpty) kinds &= ~ValueKind.Text;
        return new AbstractValue(kinds, number, length) { NullnessKnown = value.NullnessKnown };
    }

    /// <summary>In JavaScript only a number, a text or nothing can be false, so a falsy value is one of those.</summary>
    private static AbstractValue TruthOfFalsy(AbstractValue value)
    {
        var number = value.Number.Meet(Interval.Exactly(0));
        var length = value.Length.Meet(Interval.Exactly(0));
        var kinds = value.Kinds;
        if ((kinds & ValueKind.Numeric) != 0 && number.IsEmpty) kinds &= ~ValueKind.Numeric;
        if ((kinds & ValueKind.Text) != 0 && length.IsEmpty) kinds &= ~ValueKind.Text;
        return new AbstractValue(kinds, number, length) { NullnessKnown = value.NullnessKnown };
    }

    private static Interval WithoutZero(Interval range, bool integers)
    {
        if (range.IsExact && range.Low == 0) return Interval.Empty;
        if (range.Low == 0) return new Interval(integers ? 1 : double.Epsilon, range.High);
        if (range.High == 0) return new Interval(range.Low, integers ? -1 : -double.Epsilon);
        return range;
    }

    private AbstractState Compare(AbstractState state, Binary comparison, bool holds)
    {
        var op = holds ? comparison.Operator : Opposite(comparison.Operator);

        if (IsNullLiteral(comparison.Right) || IsNullLiteral(comparison.Left))
        {
            var other = IsNullLiteral(comparison.Right) ? comparison.Left : comparison.Right;
            if (Followed(other) is not { } checkedName) return state;

            var value = state[checkedName];
            return op switch
            {
                BinaryOperator.Equal or BinaryOperator.Is => Store(state, checkedName, value.OnlyNull()),
                BinaryOperator.NotEqual or BinaryOperator.IsNot => Store(state, checkedName, value.WithoutNull()),
                _ => state,
            };
        }

        if (op is BinaryOperator.Is or BinaryOperator.IsNot) return state;

        var ordering = op is BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual;
        var left = Evaluate(comparison.Left, state);
        var right = Evaluate(comparison.Right, state);
        if (ordering)
        {
            left = AsNumberWhenComparedWithOne(left, right);
            right = AsNumberWhenComparedWithOne(right, left);
        }
        if (!left.IsNumber || !right.IsNumber) return state;

        state = Known(state, comparison.Left, left);
        state = Known(state, comparison.Right, right);

        var integers = left.IsOnly(ValueKind.Integer | ValueKind.Boolean) && right.IsOnly(ValueKind.Integer | ValueKind.Boolean);
        var leftAllowed = Allowed(op, right.Number, integers);
        var rightAllowed = Allowed(Mirror(op), left.Number, integers);

        if (left.Number.Meet(leftAllowed).IsEmpty || right.Number.Meet(rightAllowed).IsEmpty) return AbstractState.Unreachable;

        state = Refine(state, comparison.Left, leftAllowed, op == BinaryOperator.NotEqual ? right.Number : null, integers);
        state = Refine(state, comparison.Right, rightAllowed, op == BinaryOperator.NotEqual ? left.Number : null, integers);
        return state;
    }

    private static bool IsNullLiteral(Expr expression) => expression is Literal { Kind: LiteralKind.Null };

    /// <summary>The name a variable is followed under: its own, or for this.x in Java and C#, the field's.</summary>
    private string? Followed(Expr expression) => expression is Name name ? name.Identifier : FieldName(expression);

    /// <summary>Something ordered against a number must be a number itself, or the comparison would have failed.</summary>
    private static AbstractValue AsNumberWhenComparedWithOne(AbstractValue value, AbstractValue other) =>
        value.IsUnknown && other.IsNumber ? new AbstractValue(ValueKind.Integer | ValueKind.Real, Interval.Top, Interval.Empty) : value;

    private AbstractState Known(AbstractState state, Expr side, AbstractValue value) =>
        side is Name name && state[name.Identifier].IsUnknown && value.IsNumber ? Store(state, name.Identifier, value) : state;

    private AbstractState Refine(AbstractState state, Expr side, Interval allowed, Interval? excluded, bool integers)
    {
        switch (side)
        {
            case Name name:
                var value = state[name.Identifier];
                if (!value.IsNumber) return state;
                var refined = value.WithNumber(allowed);
                if (excluded is { IsExact: true } single) refined = Excluding(refined, single.Low, integers);
                return Store(state, name.Identifier, refined);

            case var measuring when LengthOf(measuring) is { } sized:
                var measured = state[sized.Identifier];
                var lengthAllowed = allowed.Meet(Interval.NonNegative);
                var kept = measured.WithLength(lengthAllowed);
                if (excluded is { IsExact: true } lengthExcluded) kept = kept with { Length = Excluding(AbstractValue.Integer(kept.Length), lengthExcluded.Low, true).Number };
                return Store(state, sized.Identifier, kept);

            default:
                return state;
        }
    }

    /// <summary>The variable whose length this expression reads: len(x), x.length, x.length(), x.size(), x.Length or x.Count.</summary>
    private Name? LengthOf(Expr expression) => expression switch
    {
        Call { Callee: Name { Identifier: "len" }, Arguments: [{ Value: Name sized }] } when IsPython => sized,
        Member { Target: Name sized, MemberName: "length" or "Length" or "Count" } when !IsPython => sized,
        Call { Callee: Member { Target: Name sized, MemberName: "length" or "size" or "Count" }, Arguments.Count: 0 } when !IsPython => sized,
        _ => null,
    };

    private static AbstractValue Excluding(AbstractValue value, double point, bool integers)
    {
        var range = value.Number;
        if (range.IsExact && range.Low == point) return value with { Kinds = ValueKind.Nothing };
        if (!integers) return value;
        if (range.Low == point) return value with { Number = new Interval(point + 1, range.High) };
        if (range.High == point) return value with { Number = new Interval(range.Low, point - 1) };
        return value;
    }

    private static Interval Allowed(BinaryOperator op, Interval other, bool integers)
    {
        return op switch
        {
            BinaryOperator.Less => new Interval(double.NegativeInfinity, integers ? other.High - 1 : Math.BitDecrement(other.High)),
            BinaryOperator.LessOrEqual => new Interval(double.NegativeInfinity, other.High),
            BinaryOperator.Greater => new Interval(integers ? other.Low + 1 : Math.BitIncrement(other.Low), double.PositiveInfinity),
            BinaryOperator.GreaterOrEqual => new Interval(other.Low, double.PositiveInfinity),
            BinaryOperator.Equal => other,
            _ => Interval.Top,
        };
    }

    private static BinaryOperator Opposite(BinaryOperator op) => op switch
    {
        BinaryOperator.Less => BinaryOperator.GreaterOrEqual,
        BinaryOperator.LessOrEqual => BinaryOperator.Greater,
        BinaryOperator.Greater => BinaryOperator.LessOrEqual,
        BinaryOperator.GreaterOrEqual => BinaryOperator.Less,
        BinaryOperator.Equal => BinaryOperator.NotEqual,
        BinaryOperator.NotEqual => BinaryOperator.Equal,
        BinaryOperator.Is => BinaryOperator.IsNot,
        BinaryOperator.IsNot => BinaryOperator.Is,
        _ => op,
    };

    private static BinaryOperator Mirror(BinaryOperator op) => op switch
    {
        BinaryOperator.Less => BinaryOperator.Greater,
        BinaryOperator.LessOrEqual => BinaryOperator.GreaterOrEqual,
        BinaryOperator.Greater => BinaryOperator.Less,
        BinaryOperator.GreaterOrEqual => BinaryOperator.LessOrEqual,
        _ => op,
    };
}
