using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Solver;

namespace FixFinder.Core.Analysis.Symbolic;

public sealed partial class SymbolicExecutor
{
    private static readonly HashSet<string> Builtins = new(StringComparer.Ordinal)
    {
        "len", "print", "range", "int", "float", "str", "bool", "abs", "min", "max", "round", "sum", "input", "list", "tuple", "set",
        "dict", "sorted", "reversed", "enumerate", "zip", "map", "filter", "open", "isinstance", "type", "repr", "ord", "chr", "any",
        "all", "iter", "next", "id", "hash", "format", "divmod", "pow", "frozenset", "callable", "hasattr",
    };

    private static readonly HashSet<string> Growing = new(StringComparer.Ordinal)
    {
        "append", "appendleft", "add", "addFirst", "addLast", "push", "offer", "Add", "Push", "Enqueue", "AddFirst", "AddLast",
    };

    private static readonly HashSet<string> Taking = new(StringComparer.Ordinal)
    {
        "pop", "popleft", "Pop", "Dequeue", "Peek", "First", "Last", "element", "getFirst", "getLast", "removeFirst", "removeLast",
    };

    private static readonly HashSet<string> TakingWithoutRemoving = new(StringComparer.Ordinal) { "Peek", "First", "Last", "element", "getFirst", "getLast" };

    private SymbolicValue Evaluate(Expr expression, Path path)
    {
        Record(expression.Span);

        return expression switch
        {
            Literal literal => Constant(literal),
            Name name => Read(name.Identifier, path),
            Member field when FieldName(field) is { } named => Read(named, path),
            Member member => MemberValue(member, path),
            Unary unary => UnaryValue(unary, path),
            Binary binary => BinaryValue(binary, path),
            Conditional choice => Choose(choice, choice.Test, path) ? Evaluate(choice.WhenTrue, path) : Evaluate(choice.WhenFalse, path),
            Call call => CallValue(call, path),
            ElementAccess element => ElementValue(element, path),
            Slice slice => SliceValue(slice, path),
            NewObject created => NewValue(created, path),
            Cast cast => CastValue(cast, path),
            CollectionLiteral collection => CollectionValue(collection, path),
            AssignValue assigned => AssignValueOf(assigned, path),
            NextItem next => ItemValue(next, path),
            MoreItems => new SymTruth(Condition.Either),
            Opaque opaque => OpaqueValue(opaque, path),
            _ => SymUnknown.Value,
        };
    }

    private SymbolicValue Constant(Literal literal) => literal.Kind switch
    {
        LiteralKind.Integer => literal.Value switch
        {
            long whole => new SymNumber(whole, true),
            int whole => new SymNumber((long)whole, true),
            BigInteger big => new SymNumber(LinearTerm.Of(new Rational(big, 1)), true),
            double real when double.IsFinite(real) => new SymNumber(LinearTerm.Of(Rational.FromDouble(real)), true),
            _ => SymUnknown.Value,
        },
        LiteralKind.Real when literal.Value is { } real && Convert.ToDouble(real, CultureInfo.InvariantCulture) is var number && double.IsFinite(number) =>
            new SymNumber(LinearTerm.Of(Rational.FromDouble(number)), false),
        LiteralKind.Boolean => new SymTruth(literal.Value is true ? Condition.True : Condition.False),
        LiteralKind.Text => new SymText(((string?)literal.Value ?? "").Length, (string?)literal.Value ?? ""),
        LiteralKind.Character when !IsPython && literal.Value is string { Length: 1 } character => new SymNumber((long)character[0], true),
        LiteralKind.Character => new SymText(1),
        LiteralKind.Null => SymNull.Value,
        _ => SymUnknown.Value,
    };

    private static bool IsNullLiteral(Expr expression) => expression is Literal { Kind: LiteralKind.Null };

    /// <summary>In Java and C#, this.count and count are the same field unless a local variable hides it.</summary>
    private string? FieldName(Expr expression) =>
        !IsPython && expression is Member { Target: Name { Identifier: "this" }, MemberName: var field } && !_locals.Contains(field) ? field : null;

    private string? VariableOf(Expr expression) => expression is Name name ? name.Identifier : FieldName(expression);

    private bool IsParameter(string name) => _graph.Function.Parameters.Any(p => p.Name == name);

    /// <summary>The name when it is a parameter that still holds what was passed in, so a symbol for it stands for an input.</summary>
    private string? InputName(string? name, Path path) => name is not null && IsParameter(name) && !path.Reassigned.Contains(name) ? name : null;

    private bool IsOwn(string name) => _locals.Contains(name) || name.StartsWith('$') || name == "this";

    private SymbolicValue Read(string name, Path path) =>
        path.Store.TryGetValue(name, out var value) ? value
        : IsPython && !IsOwn(name) && Builtins.Contains(name) ? new SymOther("builtin")
        : SymUnknown.Value;

    private void Set(Path path, string name, SymbolicValue value)
    {
        if (_volatile.Contains(name)) return;

        path.Store = path.Store.SetItem(name, value);
        path.Truths = path.Truths.Remove(name);
    }

    /// <summary>
    /// A change to the collection a variable holds - made through it, or learned about it - which is as true of every
    /// other variable holding the same collection. Giving a variable a new value is <see cref="Assign"/>.
    /// </summary>
    private void Changed(Path path, string name, SymbolicValue value)
    {
        Set(path, name, value);
        foreach (var alias in path.Aliases.GetValueOrDefault(name) ?? []) Set(path, alias, value);
    }

    /// <summary>After b = a: b leaves whatever collection it shared before and shares a's, with every name already sharing it.</summary>
    private static void Alias(Path path, string name, string of)
    {
        Unalias(path, name);

        var group = (path.Aliases.GetValueOrDefault(of) ?? []).Add(of);
        foreach (var member in group)
            path.Aliases = path.Aliases.SetItem(member, (path.Aliases.GetValueOrDefault(member) ?? ImmutableHashSet.Create<string>(StringComparer.Ordinal)).Add(name));

        path.Aliases = path.Aliases.SetItem(name, group.WithComparer(StringComparer.Ordinal));
    }

    /// <summary>A variable given a new value shares nothing with anyone any more.</summary>
    private static void Unalias(Path path, string name)
    {
        if (!path.Aliases.TryGetValue(name, out var others)) return;

        path.Aliases = path.Aliases.Remove(name);
        foreach (var other in others)
        {
            if (!path.Aliases.TryGetValue(other, out var theirs)) continue;
            var remaining = theirs.Remove(name);
            path.Aliases = remaining.IsEmpty ? path.Aliases.Remove(other) : path.Aliases.SetItem(other, remaining);
        }
    }

    private static void Drop(Path path, IEnumerable<string> names)
    {
        var dropped = names.ToList();
        foreach (var name in dropped) Unalias(path, name);
        path.Store = path.Store.RemoveRange(dropped);
        path.Truths = path.Truths.RemoveRange(dropped);
        path.Reassigned = path.Reassigned.Union(dropped);
    }

    /// <summary>Other code has run: whatever this function does not own may have changed.</summary>
    private void ForgetOutside(Path path) => Drop(path, path.Store.Keys.Concat(path.Truths.Keys).Where(name => !IsOwn(name)).ToList());

    private SymNumber Approximate(bool whole) => new(_symbols.New("an approximation", SymbolOrigin.Approximation, whole), whole);

    /// <summary>
    /// An operation the solver cannot follow, such as the product of two unknowns: still an approximation, but the same
    /// operation on the same values is always the same symbol, so two paths - or two versions - that compute it agree.
    /// </summary>
    private SymNumber Applied(string operation, bool whole, params LinearTerm[] operands)
    {
        var key = $"{operation}|{whole}|{string.Join("|", operands.Select(o => o.ToString()))}";
        if (!_applied.TryGetValue(key, out var term))
        {
            term = _symbols.New("an approximation", SymbolOrigin.Approximation, whole, applies: new Application(operation, operands));
            _applied[key] = term;
        }

        return new SymNumber(term, whole);
    }

    private LinearTerm ApproximateLength(Path path)
    {
        var length = _symbols.New("an approximation", SymbolOrigin.Approximation);
        path.Constraints = path.Constraints.Add(Constraint.AtLeast(length, 0));
        return length;
    }

    private static string Subject(Expr expression) => expression switch
    {
        Name name => $"`{name.Identifier}`",
        Call call => $"what `{IrText.Of(call)}` returns",
        _ => $"`{IrText.Of(expression)}`",
    };

    /// <summary>A value of a declared type, with a fresh symbol for what is not known and the type's own limits.</summary>
    private SymbolicValue FromType(IrType type, string describes, SymbolOrigin origin, Path path, string? variable = null)
    {
        var lengthOrigin = origin == SymbolOrigin.Input ? SymbolOrigin.TextLength : SymbolOrigin.Length;

        switch (type.Name)
        {
            case "int" or "long" or "short" or "byte" or "sbyte" or "uint" or "ulong" or "ushort" or "char" or "nint" or "Integer" or "Long"
                or "Short" or "Byte" or "Character" or "Int32" or "Int64":
                var whole = _symbols.New(describes, origin, variable: variable);
                if (!IsPython && Limits(type.Name) is { } limits)
                    path.Constraints = path.Constraints.AddRange([Constraint.AtLeast(whole, LinearTerm.Of(limits.Low)), Constraint.AtMost(whole, LinearTerm.Of(limits.High))]);
                return new SymNumber(whole, true);

            case "float" or "double" or "decimal" or "Double" or "Float" or "Decimal":
                return new SymNumber(_symbols.New(describes, origin, isWhole: false, variable: variable), false);

            case "bool" or "boolean" or "Boolean":
                var bit = _symbols.New(describes, origin is SymbolOrigin.Parameter or SymbolOrigin.Outside or SymbolOrigin.Input ? SymbolOrigin.Flag : SymbolOrigin.Derived,
                    variable: variable);
                path.Constraints = path.Constraints.AddRange([Constraint.AtLeast(bit, 0), Constraint.AtMost(bit, 1)]);
                return new SymTruth(Condition.Of(Constraint.AtLeast(bit, 1)));

            case "str" or "string" or "String":
                var characters = _symbols.New(describes, SymbolOrigin.TextLength, variable: variable);
                path.Constraints = path.Constraints.Add(Constraint.AtLeast(characters, 0));
                return new SymText(characters);

            case "list" or "List" or "ArrayList" or "LinkedList" or "array" or "tuple" or "IList" or "ICollection" or "Collection" or "Deque"
                or "ArrayDeque" or "Stack" or "Queue" or "Vector" or "IReadOnlyList" or "IReadOnlyCollection":
                return new SymSequence(type.Name == "tuple" ? CollectionKind.Tuple : CollectionKind.List, Length(describes, lengthOrigin, path, variable));

            case "set" or "Set" or "HashSet" or "TreeSet" or "ISet" or "SortedSet":
                return new SymSequence(CollectionKind.Set, Length(describes, lengthOrigin, path, variable));

            case "dict" or "Dictionary" or "Map" or "HashMap" or "TreeMap" or "IDictionary" or "SortedDictionary":
                return new SymSequence(CollectionKind.Dictionary, Length(describes, lengthOrigin, path, variable));

            case "None":
                return SymNull.Value;

            default:
                return SymUnknown.Value;
        }
    }

    private LinearTerm Length(string describes, SymbolOrigin origin, Path path, string? variable = null)
    {
        var length = _symbols.New(describes, origin, variable: variable);
        path.Constraints = path.Constraints.Add(Constraint.AtLeast(length, 0));
        return length;
    }

    private (Rational Low, Rational High)? Limits(string type) => type switch
    {
        "int" or "Integer" or "Int32" => (int.MinValue, int.MaxValue),
        "long" or "Long" or "Int64" or "nint" => (long.MinValue, long.MaxValue),
        "short" or "Short" => (short.MinValue, short.MaxValue),
        "byte" or "Byte" when _language == SourceLanguage.Java => (sbyte.MinValue, sbyte.MaxValue),
        "byte" or "Byte" => (0, 255),
        "sbyte" => (sbyte.MinValue, sbyte.MaxValue),
        "char" or "Character" or "ushort" => (0, 65535),
        "uint" => (0, uint.MaxValue),
        "ulong" => (Rational.Zero, new Rational(ulong.MaxValue, 1)),
        _ => null,
    };

    /// <summary>Something not known that is used as a number: from now on it is one, with its own symbol.</summary>
    private SymNumber AsNumber(Expr source, SymbolicValue value, Path path, bool whole)
    {
        if (value is SymNumber number) return number;

        var name = VariableOf(source);
        var origin = InputName(name, path) is not null ? SymbolOrigin.Parameter : SymbolOrigin.Outside;
        var made = new SymNumber(_symbols.New(name is null ? Subject(source) : $"`{name}`", origin, whole, name), whole);
        if (name is not null) Changed(path, name, made);
        return made;
    }

    /// <summary>Something not known that is gone through or measured: from now on it is a collection with its own length.</summary>
    private SymSequence AsSequence(Expr source, Path path)
    {
        var name = VariableOf(source);
        var made = new SymSequence(CollectionKind.List, Length(name is null ? Subject(source) : $"`{name}`", SymbolOrigin.Length, path, InputName(name, path)));

        // Tested for truth before it was measured: a collection is true exactly when it is not empty, so the flag that
        // stood for its truth and the length it has now must agree - at least one item when true, none when false.
        if (name is not null && path.Truths.TryGetValue(name, out var flag))
            path.Constraints = path.Constraints.AddRange([Constraint.AtLeast(made.Length, flag), Constraint.AtMost(made.Length, flag * LongestCollection)]);

        if (name is not null) Changed(path, name, made);
        return made;
    }

    /// <summary>More items than any collection in any of the languages can hold: Python's own limit, sys.maxsize.</summary>
    private static readonly Rational LongestCollection = new(long.MaxValue, 1);

    private bool Choose(Expr at, Expr test, Path path) =>
        path.Choices.TryGetValue(at, out var chosen) ? chosen : Settle(at, Truth(test, path));

    private bool Choose(Expr at, Condition condition, Path path) =>
        path.Choices.TryGetValue(at, out var chosen) ? chosen : Settle(at, condition);

    private static bool Settle(Expr at, Condition condition) =>
        condition is Known known ? known.Value : throw new ChoiceNeeded(at, condition);

    private Condition Truth(Expr expression, Path path) => expression switch
    {
        Unary { Operator: UnaryOperator.Not } negation => Truth(negation.Operand, path).Not(),
        Binary { Operator: BinaryOperator.And } both => Choose(both, both.Left, path) ? Truth(both.Right, path) : Condition.False,
        Binary { Operator: BinaryOperator.Or } either => Choose(either, either.Left, path) ? Condition.True : Truth(either.Right, path),
        Binary comparison when IsComparison(comparison.Operator) => Compare(comparison, path),
        _ when VariableOf(expression) is { } name && !_volatile.Contains(name) && Evaluate(expression, path) is SymUnknown => Flag(name, path),
        _ => TruthOf(Evaluate(expression, path)),
    };

    /// <summary>
    /// Whether a variable holding something not known is true: one symbol, kept until the variable is assigned again,
    /// so two tests of the same flag always agree.
    /// </summary>
    private Condition Flag(string name, Path path)
    {
        if (!path.Truths.TryGetValue(name, out var bit))
        {
            bit = _symbols.New($"`{name}`", SymbolOrigin.Flag, variable: InputName(name, path));
            path.Constraints = path.Constraints.AddRange([Constraint.AtLeast(bit, 0), Constraint.AtMost(bit, 1)]);
            path.Truths = path.Truths.SetItem(name, bit);
        }

        return Condition.Of(Constraint.AtLeast(bit, 1));
    }

    /// <summary>What a typeof test asks about, when what it is compared with is a kind nothing can have.</summary>
    private static Expr? TypeOfTest(Binary comparison) => (comparison.Left, comparison.Right) switch
    {
        (Opaque { What: "typeof", Parts: [var left] }, Literal { Kind: LiteralKind.Text, Value: string what }) when Present(what) => left,
        (Literal { Kind: LiteralKind.Text, Value: string what }, Opaque { What: "typeof", Parts: [var right] }) when Present(what) => right,
        _ => null,
    };

    private static bool Present(string kind) => kind is "number" or "string" or "boolean" or "function" or "bigint" or "symbol";

    private static bool IsComparison(BinaryOperator op) =>
        op is BinaryOperator.Equal or BinaryOperator.NotEqual or BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater
            or BinaryOperator.GreaterOrEqual or BinaryOperator.Is or BinaryOperator.IsNot;

    private static Condition TruthOf(SymbolicValue value) => value switch
    {
        SymTruth truth => truth.Condition,
        SymNumber number => Condition.Of(Constraint.Different(number.Term, 0)),
        SymText text => Condition.Of(Constraint.AtLeast(text.Length, 1)),
        SymSequence sequence => Condition.Of(Constraint.AtLeast(sequence.Length, 1)),
        SymRange { Step.Sign: > 0 } range => Condition.Of(Constraint.Below(range.Start, range.Stop)),
        SymRange range => Condition.Of(Constraint.Above(range.Start, range.Stop)),
        SymNull => Condition.False,
        SymOther => Condition.True,
        _ => Condition.Either,
    };

    private Condition Compare(Binary comparison, Path path)
    {
        var op = comparison.Operator;

        // typeof nothing is never "number", "string", "boolean" or "function", whichever way round the test is written.
        if (op is BinaryOperator.Equal or BinaryOperator.NotEqual && TypeOfTest(comparison) is { } asked && Evaluate(asked, path) is SymNull)
            return op == BinaryOperator.Equal ? Condition.False : Condition.True;

        if (IsNullLiteral(comparison.Right) || IsNullLiteral(comparison.Left))
        {
            var tested = IsNullLiteral(comparison.Right) ? comparison.Left : comparison.Right;
            var other = Evaluate(tested, path);
            var equal = op is BinaryOperator.Equal or BinaryOperator.Is;
            var name = VariableOf(tested);

            if (other is SymUnknown && !(name is not null && _volatile.Contains(name)))
            {
                var parameter = InputName(name, path);

                if (Choose(comparison, Condition.Either, path))
                {
                    other = SymNull.Value;
                    if (name is not null) Changed(path, name, SymNull.Value);
                    if (parameter is null) path.OutsideDecided = true;
                    else path.NullParameters = path.NullParameters.Add(parameter);
                    path.Facts = path.Facts.Add($"{Subject(tested)} is {NullWord}");
                }
                else if (parameter is not null && !path.NotNullParameters.Contains(parameter))
                {
                    path.NotNullParameters = path.NotNullParameters.Add(parameter);
                }
            }

            return other is SymNull == equal ? Condition.True : Condition.False;
        }

        var left = Evaluate(comparison.Left, path);
        var right = Evaluate(comparison.Right, path);

        if ((left is SymNull || right is SymNull) && op is BinaryOperator.Equal or BinaryOperator.NotEqual or BinaryOperator.Is or BinaryOperator.IsNot)
            return (left is SymNull && right is SymNull) == (op is BinaryOperator.Equal or BinaryOperator.Is) ? Condition.True : Condition.False;

        if (left is SymUnknown && comparison.Left is Name { Identifier: var leftName } && _numeric.Contains(leftName)) left = AsNumber(comparison.Left, left, path, !IsPython);
        if (right is SymUnknown && comparison.Right is Name { Identifier: var rightName } && _numeric.Contains(rightName)) right = AsNumber(comparison.Right, right, path, !IsPython);

        if (left is SymNumber known && right is SymUnknown) right = AsNumber(comparison.Right, right, path, !IsPython && known.Whole);
        if (right is SymNumber other2 && left is SymUnknown) left = AsNumber(comparison.Left, left, path, !IsPython && other2.Whole);

        if (left is SymNumber a && right is SymNumber b)
        {
            return op switch
            {
                BinaryOperator.Less => Condition.Of(Constraint.Below(a.Term, b.Term)),
                BinaryOperator.LessOrEqual => Condition.Of(Constraint.AtMost(a.Term, b.Term)),
                BinaryOperator.Greater => Condition.Of(Constraint.Above(a.Term, b.Term)),
                BinaryOperator.GreaterOrEqual => Condition.Of(Constraint.AtLeast(a.Term, b.Term)),
                BinaryOperator.Equal => Condition.Of(Constraint.Same(a.Term, b.Term)),
                BinaryOperator.NotEqual => Condition.Of(Constraint.Different(a.Term, b.Term)),
                _ => Condition.Either,
            };
        }

        if (left is SymText { Known: { } first } && right is SymText { Known: { } second } && _language != SourceLanguage.Java &&
            op is BinaryOperator.Equal or BinaryOperator.NotEqual)
            return (first == second) == (op == BinaryOperator.Equal) ? Condition.True : Condition.False;

        return Condition.Either;
    }

    private SymbolicValue UnaryValue(Unary unary, Path path)
    {
        if (unary.Operator == UnaryOperator.Not) return new SymTruth(Truth(unary.Operand, path).Not());

        var operand = Evaluate(unary.Operand, path);
        if (operand is SymUnknown && VariableOf(unary.Operand) is not null) operand = AsNumber(unary.Operand, operand, path, !IsPython);

        return (unary.Operator, operand) switch
        {
            (UnaryOperator.Negate, SymNumber number) => number with { Term = -number.Term },
            (UnaryOperator.Plus, SymNumber number) => number,
            (UnaryOperator.BitNot, SymNumber number) => number with { Term = -number.Term - LinearTerm.Of(1) },
            _ => SymUnknown.Value,
        };
    }

    private SymbolicValue BinaryValue(Binary binary, Path path)
    {
        switch (binary.Operator)
        {
            case BinaryOperator.And:
                return Choose(binary, binary.Left, path)
                    ? IsPython ? Evaluate(binary.Right, path) : new SymTruth(Truth(binary.Right, path))
                    : IsPython ? Evaluate(binary.Left, path) : new SymTruth(Condition.False);

            case BinaryOperator.Or:
                return Choose(binary, binary.Left, path)
                    ? IsPython ? Evaluate(binary.Left, path) : new SymTruth(Condition.True)
                    : IsPython ? Evaluate(binary.Right, path) : new SymTruth(Truth(binary.Right, path));

            case var op when IsComparison(op):
                return new SymTruth(Compare(binary, path));

            case BinaryOperator.In or BinaryOperator.NotIn:
                Evaluate(binary.Left, path);
                Evaluate(binary.Right, path);
                return new SymTruth(Condition.Either);
        }

        var left = Evaluate(binary.Left, path);
        var right = Evaluate(binary.Right, path);
        var numeric = binary.Operator is not BinaryOperator.Add;

        if (right is SymNumber r && left is SymUnknown) left = AsNumber(binary.Left, left, path, !IsPython && r.Whole);
        if (left is SymNumber l && right is SymUnknown) right = AsNumber(binary.Right, right, path, !IsPython && l.Whole);
        if (numeric && left is SymUnknown && right is SymUnknown && VariableOf(binary.Left) is not null && VariableOf(binary.Right) is not null)
        {
            left = AsNumber(binary.Left, left, path, !IsPython);
            right = AsNumber(binary.Right, right, path, !IsPython);
        }

        if (left is SymNumber a && right is SymNumber b) return Arithmetic(binary, a, b, path);

        if (binary.Operator is BinaryOperator.Divide or BinaryOperator.FloorDivide or BinaryOperator.Modulo)
            OutcomeAt("analysis-division-by-zero", binary.Span, binary.Right).Unsure = true;

        if (binary.Operator == BinaryOperator.Add)
        {
            if (left is SymText first && right is SymText second)
                return new SymText(first.Length + second.Length, first.Known is { } x && second.Known is { } y ? x + y : null);
            if (!IsPython && (left is SymText || right is SymText)) return new SymText(ApproximateLength(path));
            if (left is SymSequence { Kind: CollectionKind.List or CollectionKind.Tuple } one && right is SymSequence two && one.Kind == two.Kind)
                return new SymSequence(one.Kind, one.Length + two.Length, one.Items is { } p && two.Items is { } q ? [.. p, .. q] : null);
        }

        return SymUnknown.Value;
    }

    private SymbolicValue Arithmetic(Binary binary, SymNumber a, SymNumber b, Path path)
    {
        var whole = a.Whole && b.Whole;

        switch (binary.Operator)
        {
            case BinaryOperator.Add:
                return new SymNumber(a.Term + b.Term, whole);
            case BinaryOperator.Subtract:
                return new SymNumber(a.Term - b.Term, whole);
            case BinaryOperator.Multiply when a.Term.IsConstant:
                return new SymNumber(b.Term * a.Term.Constant, whole);
            case BinaryOperator.Multiply when b.Term.IsConstant:
                return new SymNumber(a.Term * b.Term.Constant, whole);
            case BinaryOperator.Divide or BinaryOperator.FloorDivide or BinaryOperator.Modulo:
                CheckDivision(binary, a, b, path);
                return Quotient(binary.Operator, a, b, path);
            default:
                return Applied(binary.Operator.ToString(), whole, a.Term, b.Term);
        }
    }

    private void CheckDivision(Binary binary, SymNumber dividend, SymNumber divisor, Path path)
    {
        if (!IsPython && !(dividend.Whole && divisor.Whole))
        {
            OutcomeAt("analysis-division-by-zero", binary.Span, binary.Right).Unsure = true;
            return;
        }

        Check("analysis-division-by-zero", binary.Span, path, binary.Right, [[Constraint.Same(divisor.Term, 0)]], [Constraint.Different(divisor.Term, 0)]);
        if (!Constrain(path, [Constraint.Different(divisor.Term, 0)])) throw new PathEnded();
    }

    /// <summary>
    /// Dividing by a constant stays exact: the quotient q of x // k is a whole number with k·q ≤ x &lt; k·q + k. Java and C#
    /// round towards zero, which is the same only while x is not negative.
    /// </summary>
    private SymbolicValue Quotient(BinaryOperator op, SymNumber dividend, SymNumber divisor, Path path)
    {
        var whole = dividend.Whole && divisor.Whole;
        var trueDivision = op == BinaryOperator.Divide && (IsPython || !whole);

        var name = $"{op}{(trueDivision ? "" : IsPython ? " floor" : " truncate")}";
        if (!divisor.Term.IsConstant || divisor.Term.Constant.IsZero) return Applied(name, !trueDivision && whole, dividend.Term, divisor.Term);

        var k = divisor.Term.Constant;
        if (trueDivision) return new SymNumber(dividend.Term * (Rational.One / k), false);
        if (!whole || k.Sign < 0 || !IsPython && !Solve(path.Constraints.Add(Constraint.Below(dividend.Term, 0))).IsUnsatisfiable)
            return Applied(name, whole, dividend.Term, divisor.Term);

        var quotient = _symbols.New("a quotient", SymbolOrigin.Derived);
        path.Constraints = path.Constraints.AddRange([
            Constraint.AtMost(quotient * k, dividend.Term),
            Constraint.Below(dividend.Term, quotient * k + LinearTerm.Of(k)),
        ]);

        return op == BinaryOperator.Modulo ? new SymNumber(dividend.Term - quotient * k, true) : new SymNumber(quotient, true);
    }

    private SymbolicValue CallValue(Call call, Path path)
    {
        if (MayReturnNull?.Invoke(call) == true)
        {
            if (call.Callee is Member called) Receiver(called, path);
            var passed = call.Arguments.Select(a => Evaluate(a.Value, path)).ToList();
            AfterUnknownCall(call, passed, path);
            return Lookup(call, SymUnknown.Value, null, path);
        }

        if (IsPython && call.Callee is Name { Identifier: var function } && !path.Store.ContainsKey(function) && !IsOwn(function) && Builtins.Contains(function))
            return Builtin(function, call, path);

        // Asking for memory in C can come back with nothing; both ways are followed.
        if (_language is SourceLanguage.C or SourceLanguage.Cpp && call.Callee is Name { Identifier: var asked } &&
            asked is "malloc" or "calloc" or "realloc" or "strdup" or "strndup")
        {
            foreach (var argument in call.Arguments) Evaluate(argument.Value, path);
            return Lookup(call, new SymOther("memory"), null, path);
        }

        if (!IsPython && call.Callee is Member { Target: Name { Identifier: var type }, MemberName: var helper } && !path.Store.ContainsKey(type) && !IsOwn(type) &&
            StaticCall(type, helper, call, path) is { } helped)
            return helped;

        if (IsPython && call.Callee is Member { Target: Name { Identifier: "re" }, MemberName: "match" or "search" or "fullmatch" } && !path.Store.ContainsKey("re"))
        {
            foreach (var argument in call.Arguments) Evaluate(argument.Value, path);
            return Lookup(call, new SymOther("match"), null, path);
        }

        if (!IsPython && call.Callee is Member { Target: Member { Target: Name { Identifier: "System" }, MemberName: "out" }, MemberName: "println" or "print" })
        {
            var shown = call.Arguments.Select(a => Evaluate(a.Value, path)).ToList();
            path.Printed = path.Printed.Add(new SymSequence(CollectionKind.Tuple, shown.Count, shown));
            return SymUnknown.Value;
        }

        if (call.Callee is Member member)
        {
            var receiver = Receiver(member, path, checking: !Failures.NothingCanRunMethods(_language));
            var arguments = call.Arguments.Select(a => Evaluate(a.Value, path)).ToList();

            if (MethodValue(member, receiver, arguments, call, path) is { } known) return known;

            AfterUnknownCall(call, arguments, path);
            return InputValue(member.MemberName, call, path) ?? SymUnknown.Value;
        }

        Evaluate(call.Callee, path);
        var values = call.Arguments.Select(a => Evaluate(a.Value, path)).ToList();
        AfterUnknownCall(call, values, path);
        return SymUnknown.Value;
    }

    /// <summary>
    /// A search or lookup that may find nothing - re.match, a dictionary's get - followed both ways: what it found, or
    /// the default given for nothing, which without one is null.
    /// </summary>
    private SymbolicValue Lookup(Call call, SymbolicValue found, SymbolicValue? missing, Path path)
    {
        if (!Choose(call, Condition.Either, path)) return found;
        path.OutsideDecided = true;
        if (missing is not null) return missing;

        path.Facts = path.Facts.Add($"{Subject(call)} is {NullWord}");
        return SymNull.Value;
    }

    /// <param name="checking">False where the language lets a method run on nothing, so only reading a field is a mistake.</param>
    private SymbolicValue Receiver(Member member, Path path, bool checking = true)
    {
        Record(member.Span);
        var target = Evaluate(member.Target, path);
        if (checking) Dereferenced(member.Span, path, member.Target, target);
        return target;
    }

    /// <summary>A value used as an object: a path where it is null fails here, and every other path shows it reached this safely.</summary>
    private void Dereferenced(SourceSpan span, Path path, Expr target, SymbolicValue value)
    {
        if (value is SymNull) Certain("analysis-null-used", span, path, target);
        OutcomeAt("analysis-null-used", span, target);
    }

    /// <summary>A call into code that is not followed can change anything outside the function, and any collection handed to it.</summary>
    private void AfterUnknownCall(Call call, IReadOnlyList<SymbolicValue> arguments, Path path)
    {
        ForgetOutside(path);

        for (var i = 0; i < call.Arguments.Count && i < arguments.Count; i++)
            if (call.Arguments[i].Value is Name { Identifier: var passed } && arguments[i] is SymSequence sequence)
                Changed(path, passed, new SymSequence(sequence.Kind, ApproximateLength(path)));

        if (call.Callee is Member { Target: Name { Identifier: var owner } } && path.Store.GetValueOrDefault(owner) is SymSequence changed)
            Changed(path, owner, new SymSequence(changed.Kind, ApproximateLength(path)));
    }

    private SymbolicValue Builtin(string function, Call call, Path path)
    {
        var sources = call.Arguments.Where(a => a.Name is null).Select(a => a.Value).ToList();
        var values = sources.Select(source => Evaluate(source, path)).ToList();
        var first = values.Count > 0 ? values[0] : null;

        switch (function)
        {
            case "len" when first is not null:
                if (first is SymUnknown && VariableOf(sources[0]) is not null) first = AsSequence(sources[0], path);
                return first switch
                {
                    SymSequence sequence => new SymNumber(sequence.Length, true),
                    SymText text => new SymNumber(text.Length, true),
                    _ => new SymNumber(ApproximateLength(path), true),
                };

            case "int" or "float" when first is not null:
                var whole = function == "int";
                return first switch
                {
                    SymNumber { Whole: true } number => number with { Whole = whole },
                    SymNumber number when !whole => number,
                    SymText { TypedAt: { } typed } text => Parsed(text, typed, whole, path),
                    SymText { Known: { } text } when whole && long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) =>
                        new SymNumber(parsed, true),
                    _ => Approximate(whole),
                };

            case "str" or "repr" or "format" or "ascii" or "hex" or "bin" or "oct":
                return new SymText(ApproximateLength(path));

            case "chr":
                return new SymText(1);

            case "input":
                return Typed(call.Span.Line, path);

            case "print":
                path.Printed = path.Printed.Add(new SymSequence(CollectionKind.Tuple, values.Count, values));
                return SymNull.Value;

            case "range" when values.Count is >= 1 and <= 3:
                var bounds = sources.Select((source, i) => AsNumber(source, values[i], path, true)).ToList();
                var (start, stop) = bounds.Count == 1 ? (LinearTerm.Of(0), bounds[0].Term) : (bounds[0].Term, bounds[1].Term);
                var step = bounds.Count == 3 ? bounds[2].Term : LinearTerm.Of(1);
                return step.IsConstant && !step.Constant.IsZero ? new SymRange(start, stop, step.Constant) : new SymOther("range");

            case "abs" when first is SymNumber magnitude:
                return Choose(call, Condition.Of(Constraint.AtLeast(magnitude.Term, 0)), path) ? magnitude : magnitude with { Term = -magnitude.Term };

            case "min" or "max" when values is [SymNumber a, SymNumber b]:
                var firstIsSmaller = Choose(call, Condition.Of(Constraint.AtMost(a.Term, b.Term)), path);
                return (function == "min") == firstIsSmaller ? a : b;

            case "bool":
                return new SymTruth(first is null ? Condition.False : TruthOf(first));

            case "list" or "tuple" or "sorted" or "reversed" or "enumerate":
                var kind = function == "tuple" ? CollectionKind.Tuple : CollectionKind.List;
                if (first is null) return new SymSequence(kind, 0, []);
                if (first is SymUnknown && VariableOf(sources[0]) is not null) first = AsSequence(sources[0], path);
                return first switch
                {
                    SymSequence sequence => new SymSequence(kind, sequence.Length, function is "list" or "tuple" ? sequence.Items : null),
                    SymText text => new SymSequence(kind, text.Length),
                    _ => new SymSequence(kind, ApproximateLength(path)),
                };

            case "set" or "frozenset" or "dict":
                var collection = function == "dict" ? CollectionKind.Dictionary : CollectionKind.Set;
                return first is null ? new SymSequence(collection, 0, []) : new SymSequence(collection, ApproximateLength(path));

            case "isinstance" or "callable" or "hasattr" when first is SymNull && !sources.Skip(1).Any(s => IrText.Of(s).Contains("None") || IrText.Of(s).Contains("object")):
                return new SymTruth(Condition.False);

            case "isinstance" or "callable" or "hasattr" or "any" or "all":
                return new SymTruth(Condition.Either);

            case "sum" or "round" or "pow" or "divmod" or "hash" or "id" or "ord":
                return Approximate(function is "round" or "hash" or "id" or "ord");

            case "open":
                return new SymOther("file");

            default:
                return SymUnknown.Value;
        }
    }

    private SymbolicValue? StaticCall(string type, string method, Call call, Path path)
    {
        var values = call.Arguments.Select(a => Evaluate(a.Value, path)).ToList();

        switch (type, method)
        {
            case ("Math", "max" or "Max" or "min" or "Min") when values is [SymNumber a, SymNumber b]:
                var firstIsSmaller = Choose(call, Condition.Of(Constraint.AtMost(a.Term, b.Term)), path);
                return (method is "min" or "Min") == firstIsSmaller ? a : b;

            case ("Math", "abs" or "Abs") when values is [SymNumber magnitude]:
                return Choose(call, Condition.Of(Constraint.AtLeast(magnitude.Term, 0)), path) ? magnitude : magnitude with { Term = -magnitude.Term };

            case ("Integer" or "Long" or "Short" or "int" or "long" or "short" or "Int32" or "Int64" or "Convert", "parseInt" or "parseLong" or "parseShort" or "valueOf" or "Parse" or "ToInt32" or "ToInt64"):
                return values is [SymText { TypedAt: { } typed } typedText, ..] ? Parsed(typedText, typed, true, path) : Approximate(true);

            case ("Double" or "Float" or "double" or "float" or "decimal" or "Convert", "parseDouble" or "parseFloat" or "valueOf" or "Parse" or "ToDouble"):
                return values is [SymText { TypedAt: { } typedReal } real, ..] ? Parsed(real, typedReal, false, path) : Approximate(false);

            case ("fmt", "Println" or "Print" or "Printf"):
                path.Printed = path.Printed.Add(new SymSequence(CollectionKind.Tuple, values.Count, values));
                return SymUnknown.Value;

            case ("Console", "WriteLine" or "Write"):
                path.Printed = path.Printed.Add(new SymSequence(CollectionKind.Tuple, values.Count, values));
                return SymUnknown.Value;

            case ("Console", "ReadLine"):
                return Typed(call.Span.Line, path);

            case ("string" or "String", "IsNullOrEmpty" or "IsNullOrWhiteSpace") when values.Count == 1:
                return new SymTruth(values[0] switch
                {
                    SymNull => Condition.True,
                    SymText text when method == "IsNullOrEmpty" => Condition.Of(Constraint.Same(text.Length, 0)),
                    _ => Condition.Either,
                });

            case ("string" or "String", "Join" or "Format" or "Concat" or "join" or "format" or "valueOf"):
                return new SymText(ApproximateLength(path));

            default:
                return null;
        }
    }

    private SymText Typed(int line, Path path)
    {
        var characters = _symbols.New($"the text typed at line {line}", SymbolOrigin.TextLength, typedAt: line, occurrence: Read(SymbolOrigin.TextLength, line, path));
        path.Constraints = path.Constraints.Add(Constraint.AtLeast(characters, 0));
        return new SymText(characters, null, line);
    }

    /// <summary>Which read of a line this is on the path, counting from 0.</summary>
    private static int Read(SymbolOrigin origin, int line, Path path)
    {
        var occurrence = path.Reads.GetValueOrDefault((origin, line));
        path.Reads = path.Reads.SetItem((origin, line), occurrence + 1);
        return occurrence;
    }

    /// <summary>A typed text read as a number: one symbol per text, whichever way and however often it is read.</summary>
    private SymNumber Parsed(SymText text, int line, bool whole, Path path)
    {
        var read = text.Length.Symbols.ToList() is [var only] ? only : -1;
        if (read >= 0 && path.Parsed.TryGetValue((read, whole), out var known)) return new SymNumber(known, whole);

        var number = _symbols.New($"the number typed at line {line}", SymbolOrigin.Input, whole, typedAt: line, occurrence: read >= 0 ? _symbols[read].Occurrence : 0);
        if (read >= 0) path.Parsed = path.Parsed.SetItem((read, whole), number);
        return new SymNumber(number, whole);
    }

    /// <summary>What a Scanner or reader method returns: what someone types.</summary>
    private SymbolicValue? InputValue(string method, Call call, Path path) => method switch
    {
        "nextInt" or "nextLong" or "nextShort" or "nextByte" =>
            new SymNumber(_symbols.New($"the number typed at line {call.Span.Line}", SymbolOrigin.Input, typedAt: call.Span.Line,
                occurrence: Read(SymbolOrigin.Input, call.Span.Line, path)), true),
        "nextDouble" or "nextFloat" =>
            new SymNumber(_symbols.New($"the number typed at line {call.Span.Line}", SymbolOrigin.Input, isWhole: false, typedAt: call.Span.Line,
                occurrence: Read(SymbolOrigin.Input, call.Span.Line, path)), false),
        "nextLine" or "next" or "readLine" => Typed(call.Span.Line, path),
        _ => null,
    };

    private SymbolicValue? MethodValue(Member member, SymbolicValue receiver, IReadOnlyList<SymbolicValue> arguments, Call call, Path path)
    {
        var method = member.MemberName;
        var owner = VariableOf(member.Target);

        void Update(SymbolicValue changed)
        {
            if (owner is not null) Changed(path, owner, changed);
        }

        switch (receiver)
        {
            case SymSequence sequence:
                var length = sequence.Length;

                if (Growing.Contains(method))
                {
                    if (sequence.Kind is CollectionKind.Set or CollectionKind.Dictionary)
                    {
                        var grown = ApproximateLength(path);
                        path.Constraints = path.Constraints.AddRange([Constraint.AtLeast(grown, length), Constraint.AtMost(grown, length + LinearTerm.Of(1))]);
                        Update(sequence with { Length = grown, Items = null });
                    }
                    else
                    {
                        var items = sequence.Items is { } known && method is "append" or "Add" or "add" or "push" or "Push" or "offer" or "Enqueue" or "addLast" or "AddLast" && arguments.Count == 1
                            ? (IReadOnlyList<SymbolicValue>)[.. known, arguments[0]]
                            : null;
                        Update(sequence with { Length = length + LinearTerm.Of(1), Items = items });
                    }

                    return IsPython ? SymNull.Value : new SymTruth(Condition.Either);
                }

                if (Taking.Contains(method) && arguments.Count == 0)
                {
                    Check("analysis-empty-collection", call.Span, path, member.Target, [[Constraint.Same(length, 0)]], [Constraint.AtLeast(length, 1)], member.Target);
                    if (!Constrain(path, [Constraint.AtLeast(length, 1)])) throw new PathEnded();

                    if (!TakingWithoutRemoving.Contains(method)) Update(sequence with { Length = length - LinearTerm.Of(1), Items = null });
                    return SymUnknown.Value;
                }

                switch (method)
                {
                    case "clear" or "Clear":
                        Update(sequence with { Length = 0, Items = [] });
                        return IsPython ? SymNull.Value : SymUnknown.Value;
                    case "size" or "Count" or "length" or "__len__" when arguments.Count == 0:
                        return new SymNumber(length, true);
                    case "isEmpty" or "IsEmpty" when arguments.Count == 0:
                        return new SymTruth(Condition.Of(Constraint.Same(length, 0)));
                    case "Any" when arguments.Count == 0:
                        return new SymTruth(Condition.Of(Constraint.AtLeast(length, 1)));
                    case "get" when !IsPython && sequence.Kind is CollectionKind.List or CollectionKind.Array && arguments is [SymNumber { Whole: true } index]:
                        CheckIndex(call.Span, length, index.Term, path, call.Arguments[0].Value, member.Target);
                        return sequence.Items is { } listed && Position(index.Term, listed.Count) is { } at ? listed[at] : SymUnknown.Value;
                    case "copy" or "Copy" or "ToList" or "ToArray" or "clone":
                        return sequence;
                    case "keys" or "values" or "items":
                        return new SymSequence(CollectionKind.List, length);
                    case "extend" or "AddRange" or "addAll" or "update" when arguments is [SymSequence added]:
                        Update(sequence.Kind is CollectionKind.List or CollectionKind.Tuple or CollectionKind.Array
                            ? sequence with { Length = length + added.Length, Items = null }
                            : sequence with { Length = ApproximateLength(path), Items = null });
                        return IsPython ? SymNull.Value : SymUnknown.Value;
                    case "sort" or "reverse" or "Sort" or "Reverse":
                        Update(sequence with { Items = null });
                        return IsPython ? SymNull.Value : SymUnknown.Value;
                    case "index" or "count" or "indexOf" or "IndexOf" or "lastIndexOf" or "find":
                        return Approximate(true);
                    case "get" when sequence.Kind == CollectionKind.Dictionary && arguments.Count is 1 or 2:
                        return Lookup(call, SymUnknown.Value, arguments.Count == 2 ? arguments[1] : null, path);
                    case "contains" or "Contains" or "ContainsKey" or "containsKey" or "get" or "GetValueOrDefault" or "getOrDefault" or "TryGetValue":
                        return method is "get" or "GetValueOrDefault" or "getOrDefault" ? SymUnknown.Value : new SymTruth(Condition.Either);
                }

                return null;

            case SymText text:
                switch (method)
                {
                    case "length" or "Length" or "Count" when arguments.Count == 0:
                        return new SymNumber(text.Length, true);
                    case "upper" or "lower" or "capitalize" or "title" or "swapcase" or "casefold" or "toUpperCase" or "toLowerCase" or "ToUpper" or "ToLower"
                        or "ToUpperInvariant" or "ToLowerInvariant":
                        return new SymText(text.Length);
                    case "isEmpty" or "IsEmpty" when arguments.Count == 0:
                        return new SymTruth(Condition.Of(Constraint.Same(text.Length, 0)));
                    case "charAt" when arguments is [SymNumber { Whole: true } index]:
                        CheckIndex(call.Span, text.Length, index.Term, path, call.Arguments[0].Value, member.Target);
                        return new SymNumber(ApproximateLength(path), true);
                    case "toCharArray" or "ToCharArray":
                        return new SymSequence(CollectionKind.List, text.Length);
                    case "split" or "Split" or "splitlines" or "rsplit":
                        return new SymSequence(CollectionKind.List, ApproximateLength(path));
                    case "find" or "rfind" or "index" or "rindex" or "count" or "indexOf" or "lastIndexOf" or "IndexOf" or "LastIndexOf" or "compareTo" or "CompareTo":
                        return Approximate(true);
                    case "encode" or "getBytes":
                        return new SymOther("bytes");
                }

                return method.StartsWith("is", StringComparison.Ordinal) || method.StartsWith("Is", StringComparison.Ordinal) ||
                       method is "equals" or "Equals" or "equalsIgnoreCase" or "startswith" or "endswith" or "startsWith" or "endsWith" or "StartsWith"
                           or "EndsWith" or "contains" or "Contains" or "matches"
                    ? new SymTruth(Condition.Either)
                    : new SymText(ApproximateLength(path));

            default:
                return null;
        }
    }

    private SymbolicValue MemberValue(Member member, Path path)
    {
        var target = Evaluate(member.Target, path);

        if (!(member.MemberName.StartsWith("__", StringComparison.Ordinal) && member.MemberName.EndsWith("__", StringComparison.Ordinal)))
            Dereferenced(member.Span, path, member.Target, target);

        return (member.MemberName, target) switch
        {
            ("length" or "Length" or "Count", SymSequence sequence) => new SymNumber(sequence.Length, true),
            ("Length", SymText text) when !IsPython => new SymNumber(text.Length, true),
            ("Keys" or "Values", SymSequence sequence) => new SymSequence(CollectionKind.List, sequence.Length),
            _ => SymUnknown.Value,
        };
    }

    private SymbolicValue ElementValue(ElementAccess element, Path path)
    {
        var target = Evaluate(element.Target, path);
        Dereferenced(element.Span, path, element.Target, target);

        var key = Evaluate(element.Key, path);

        switch (target)
        {
            case SymSequence { Kind: CollectionKind.List or CollectionKind.Tuple or CollectionKind.Array } sequence:
                if (key is SymUnknown && VariableOf(element.Key) is not null) key = AsNumber(element.Key, key, path, true);
                if (key is not SymNumber { Whole: true } index) return SymUnknown.Value;

                CheckIndex(element.Span, sequence.Length, index.Term, path, element.Key, element.Target);

                return sequence.Items is { } items && Position(index.Term, items.Count, IsPython) is { } at ? items[at] : SymUnknown.Value;

            case SymText text:
                if (key is SymNumber { Whole: true } position) CheckIndex(element.Span, text.Length, position.Term, path, element.Key, element.Target);
                return IsPython ? new SymText(1) : new SymNumber(ApproximateLength(path), true);

            default:
                return SymUnknown.Value;
        }
    }

    /// <summary>A constant index that lands inside a list of known items; Python counts negative ones from the end.</summary>
    private static int? Position(LinearTerm index, int count, bool fromEnd = false)
    {
        if (!index.IsConstant || !index.Constant.IsInteger || BigInteger.Abs(index.Constant.Numerator) > count) return null;

        var at = (int)index.Constant.Numerator;
        if (at < 0 && fromEnd) at += count;
        return at >= 0 && at < count ? at : null;
    }

    private void CheckIndex(SourceSpan span, LinearTerm length, LinearTerm index, Path path, Expr culprit, Expr collection)
    {
        if (!Failures.ReadingPastTheEndFails(_language)) return;

        var lowest = IsPython ? -length : LinearTerm.Of(0);

        Check("analysis-index-out-of-range", span, path, culprit,
            [[Constraint.AtLeast(index, length)], [Constraint.Below(index, lowest)]],
            [Constraint.AtLeast(index, lowest), Constraint.Below(index, length)], collection);

        if (!Constrain(path, [Constraint.AtLeast(index, lowest), Constraint.Below(index, length)])) throw new PathEnded();
    }

    private SymbolicValue SliceValue(Slice slice, Path path)
    {
        var target = Evaluate(slice.Target, path);
        foreach (var bound in new[] { slice.Lower, slice.Upper, slice.Step }.OfType<Expr>()) Evaluate(bound, path);

        var length = ApproximateLength(path);
        return target switch
        {
            SymSequence sequence => Within(new SymSequence(sequence.Kind, length), length, sequence.Length, path),
            SymText text => Within(new SymText(length), length, text.Length, path),
            _ => SymUnknown.Value,
        };
    }

    private static SymbolicValue Within(SymbolicValue value, LinearTerm length, LinearTerm most, Path path)
    {
        path.Constraints = path.Constraints.Add(Constraint.AtMost(length, most));
        return value;
    }

    private SymbolicValue NewValue(NewObject created, Path path)
    {
        var arguments = created.Arguments.Select(a => Evaluate(a.Value, path)).ToList();
        var name = created.Type.Name;

        if (name == "array")
            return arguments is [SymNumber { Whole: true } size] ? new SymSequence(CollectionKind.Array, size.Term) : new SymSequence(CollectionKind.Array, ApproximateLength(path));

        var kind = name switch
        {
            "ArrayList" or "LinkedList" or "List" or "Vector" or "Stack" or "ArrayDeque" or "Queue" or "Collection" or "ObservableCollection" => CollectionKind.List,
            "HashMap" or "TreeMap" or "LinkedHashMap" or "Dictionary" or "SortedDictionary" or "Hashtable" or "ConcurrentDictionary" => CollectionKind.Dictionary,
            "HashSet" or "TreeSet" or "SortedSet" or "LinkedHashSet" => CollectionKind.Set,
            _ => (CollectionKind?)null,
        };

        if (kind is { } collection)
        {
            var copied = arguments.FirstOrDefault(a => a is not SymNumber);
            return copied switch
            {
                null => new SymSequence(collection, 0, []),
                SymSequence source when collection == CollectionKind.List && source.Kind != CollectionKind.Dictionary => new SymSequence(collection, source.Length),
                _ => new SymSequence(collection, ApproximateLength(path)),
            };
        }

        return name is "String" or "string" ? new SymText(ApproximateLength(path)) : new SymOther(name);
    }

    private SymbolicValue CastValue(Cast cast, Path path)
    {
        var value = Evaluate(cast.Value, path);

        return (cast.Type.Name, value) switch
        {
            ("int" or "long" or "short" or "byte" or "char", SymNumber { Whole: true } number) => number,
            ("int" or "long" or "short" or "byte" or "char", SymNumber) => Approximate(true),
            ("double" or "float" or "decimal", SymNumber number) => number with { Whole = false },
            _ => value,
        };
    }

    private SymbolicValue CollectionValue(CollectionLiteral collection, Path path)
    {
        var items = collection.Items.Select(item => Evaluate(item, path)).ToList();
        foreach (var key in collection.Keys ?? []) Evaluate(key, path);

        var kind = collection.Kind == CollectionKind.Array ? CollectionKind.List : collection.Kind;
        if (kind is CollectionKind.Set or CollectionKind.Dictionary && items.Count > 1)
        {
            var distinct = ApproximateLength(path);
            path.Constraints = path.Constraints.AddRange([Constraint.AtLeast(distinct, 1), Constraint.AtMost(distinct, items.Count)]);
            return new SymSequence(kind, distinct);
        }

        return new SymSequence(kind, items.Count, items);
    }

    private SymbolicValue AssignValueOf(AssignValue assigned, Path path)
    {
        var before = assigned.ValueBeforeAssigning ? Evaluate(assigned.Target, path) : null;
        var value = Evaluate(assigned.Value, path);
        Assign(assigned.Target, value, path, assigned.Value);
        return before ?? value;
    }

    private SymbolicValue ItemValue(NextItem next, Path path)
    {
        var name = next.Items is Name { Identifier: var held } ? held : null;
        var items = Evaluate(next.Items, path);
        var index = name is null ? 0 : path.Taken.GetValueOrDefault(name) - 1;

        return items switch
        {
            SymSequence { Items: { } known } when index >= 0 && index < known.Count => known[index],
            SymText => IsPython ? new SymText(1) : new SymNumber(ApproximateLength(path), true),
            SymRange range => new SymNumber(range.Start + LinearTerm.Of(range.Step * Math.Max(index, 0)), true),
            _ => SymUnknown.Value,
        };
    }

    private SymbolicValue OpaqueValue(Opaque opaque, Path path)
    {
        switch (opaque.What)
        {
            case "typed whole number" or "typed number":
                var whole = opaque.What == "typed whole number";
                return new SymNumber(_symbols.New($"the number typed at line {opaque.Span.Line}", SymbolOrigin.Input, whole, typedAt: opaque.Span.Line,
                    occurrence: Read(SymbolOrigin.Input, opaque.Span.Line, path)), whole);

            case "typed text":
                return Typed(opaque.Span.Line, path);

            case "is pattern" when opaque.Parts is [var subject]:
                return new SymTruth(Evaluate(subject, path) is SymNull ? Condition.False : Condition.Either);

            case "not null" when opaque.Parts is [var inner]:
                return Evaluate(inner, path);

            case "nullable value":
                foreach (var part in opaque.Parts) Evaluate(part, path);
                return new SymOther("nullable value");

            case "await" or "yield":
                foreach (var part in opaque.Parts) Evaluate(part, path);
                ForgetOutside(path);
                return SymUnknown.Value;

            case "formatted text":
                foreach (var part in opaque.Parts) Evaluate(part, path);
                return new SymText(ApproximateLength(path));

            case "lambda" or "lambda expression" or "function" or "class" or "generator" or "anonymous class" or "AssertionError":
                return new SymOther(opaque.What);

            case var what when what.StartsWith("module ", StringComparison.Ordinal) || what.StartsWith("caught ", StringComparison.Ordinal):
                return new SymOther(what);

            default:
                foreach (var part in opaque.Parts) Evaluate(part, path);
                return SymUnknown.Value;
        }
    }

    private void Assign(Expr target, SymbolicValue value, Path path, Expr? source)
    {
        switch (target)
        {
            case Name name:
                if (!IsPython && value is SymUnknown && _declared.TryGetValue(name.Identifier, out var type))
                    value = FromType(type, source is null ? $"`{name.Identifier}`" : Subject(source), SymbolOrigin.Outside, path);

                // A whole number kept in a double is a fraction from then on, and dividing by a fraction that is 0 does not fail.
                if (!IsPython && value is SymNumber { Whole: true } whole && _declared.TryGetValue(name.Identifier, out var holding) && holding.IsFloatingPoint)
                    value = whole with { Whole = false };

                // items += more extends a Python list in place, where items = items + more makes a new one - and once
                // lowered the two look alike. For a list other names share, neither is guessed: their lengths are forgotten.
                if (IsPython && source is Binary { Left: Name { Identifier: var extended } } && extended == name.Identifier &&
                    path.Aliases.ContainsKey(name.Identifier) && path.Store.GetValueOrDefault(name.Identifier) is SymSequence { Kind: CollectionKind.List } before)
                {
                    Changed(path, name.Identifier, before with { Length = ApproximateLength(path), Items = null });
                }

                Unalias(path, name.Identifier);
                Set(path, name.Identifier, value);
                path.Reassigned = path.Reassigned.Add(name.Identifier);

                // b = a: both names now hold the one collection, so a change made through either is a change to both.
                if (source is Name { Identifier: var of } && of != name.Identifier && value is SymSequence or SymUnknown && !_volatile.Contains(of))
                    Alias(path, name.Identifier, of);
                break;

            case Member field when FieldName(field) is { } named:
                Unalias(path, named);
                Set(path, named, value);
                break;

            case CollectionLiteral unpacked:
                var items = value is SymSequence { Items: { } known } && known.Count == unpacked.Items.Count ? known : null;
                for (var i = 0; i < unpacked.Items.Count; i++) Assign(unpacked.Items[i], items?[i] ?? SymUnknown.Value, path, null);
                break;

            case Member member:
                Receiver(member, path);
                break;

            case ElementAccess element:
                var container = Evaluate(element.Target, path);
                Dereferenced(element.Span, path, element.Target, container);
                var key = Evaluate(element.Key, path);

                if (container is SymSequence { Kind: CollectionKind.Dictionary } dictionary && VariableOf(element.Target) is { } owner)
                {
                    var grown = ApproximateLength(path);
                    path.Constraints = path.Constraints.AddRange([Constraint.AtLeast(grown, dictionary.Length), Constraint.AtMost(grown, dictionary.Length + LinearTerm.Of(1))]);
                    Changed(path, owner, dictionary with { Length = grown });
                }
                else if (container is SymSequence { Kind: CollectionKind.List or CollectionKind.Array } list && key is SymNumber { Whole: true } index)
                {
                    CheckIndex(element.Span, list.Length, index.Term, path, element.Key, element.Target);
                    if (VariableOf(element.Target) is { } listed && list.Items is not null) Changed(path, listed, list with { Items = null });
                }

                break;
        }
    }
}
