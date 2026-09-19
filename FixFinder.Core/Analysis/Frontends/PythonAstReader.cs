using System.Text.Json;
using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>Turns one file's Python syntax tree, as JSON from <see cref="PythonAstScript"/>, into the IR.</summary>
internal sealed class PythonAstReader(string file)
{
    private readonly List<IrFunction> _functions = [];
    private readonly List<IrClass> _classes = [];
    private readonly Stack<(string Name, List<string> OuterNames)> _enclosing = new();

    public List<string> Problems { get; } = [];

    public (IReadOnlyList<IrFunction> Functions, IReadOnlyList<IrClass> Classes) ReadModule(JsonElement module)
    {
        var topLevel = Block(module, "body");
        _functions.Insert(0, new IrFunction(new SourceSpan(file, 1), IrFunction.ModuleBody, null, [], IrType.Nothing, topLevel));
        return (_functions, _classes);
    }

    private static string Kind(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty("_", out var kind) ? kind.GetString() ?? "" : "";

    private static JsonElement? Field(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    private static IEnumerable<JsonElement> Items(JsonElement node, string name) =>
        Field(node, name) is { ValueKind: JsonValueKind.Array } list ? list.EnumerateArray() : [];

    private static string Text(JsonElement node, string name) =>
        Field(node, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? "" : "";

    private static int Number(JsonElement node, string name) =>
        Field(node, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : 0;

    private SourceSpan Span(JsonElement node) =>
        new(file, Number(node, "lineno"), Number(node, "col_offset"), Number(node, "end_lineno"), Number(node, "end_col_offset"));

    private IReadOnlyList<Stmt> Block(JsonElement node, string name) => Items(node, name).SelectMany(Statement).ToList();

    private IEnumerable<Stmt> Statement(JsonElement node)
    {
        var span = Span(node);

        switch (Kind(node))
        {
            case "Expr":
                yield return new Evaluate(span, Expression(Field(node, "value")!.Value));
                break;

            case "Assign":
                var targets = Items(node, "targets").Select(Expression).ToList();
                var value = Expression(Field(node, "value")!.Value);
                yield return new Assign(span, targets[0], value);
                foreach (var target in targets.Skip(1))
                    yield return new Assign(span, target, targets[0] is Name ? targets[0] : value);
                break;

            case "AugAssign":
                yield return new Assign(span, Expression(Field(node, "target")!.Value), Expression(Field(node, "value")!.Value),
                    BinaryOperatorOf(Kind(Field(node, "op")!.Value)));
                break;

            case "AnnAssign":
                var annotated = Expression(Field(node, "target")!.Value);
                var initial = Field(node, "value") is { } given ? Expression(given) : null;
                if (annotated is Name declared)
                    yield return new Declare(span, declared.Identifier, TypeOf(Field(node, "annotation")), initial);
                else if (initial is not null)
                    yield return new Assign(span, annotated, initial);
                break;

            case "Return":
                yield return new Return(span, Field(node, "value") is { } returned ? Expression(returned) : null);
                break;

            case "If":
                yield return new If(span, Expression(Field(node, "test")!.Value), Block(node, "body"), Block(node, "orelse"));
                break;

            case "While":
                yield return new While(span, Expression(Field(node, "test")!.Value), Block(node, "body"), Block(node, "orelse"));
                break;

            case "For" or "AsyncFor":
                yield return new ForEach(span, Expression(Field(node, "target")!.Value), Expression(Field(node, "iter")!.Value),
                    Block(node, "body"), Block(node, "orelse"));
                break;

            case "Break":
                yield return new Break(span);
                break;

            case "Continue":
                yield return new Continue(span);
                break;

            case "Raise":
                yield return new Throw(span, Field(node, "exc") is { } raised ? Expression(raised) : null);
                break;

            case "Try" or "TryStar":
                yield return new Try(span, Block(node, "body"), Items(node, "handlers").Select(Handler).ToList(),
                    Block(node, "orelse"), Block(node, "finalbody"));
                break;

            case "Assert":
                yield return new AssertThat(span, Expression(Field(node, "test")!.Value),
                    Field(node, "msg") is { } message ? Expression(message) : null);
                break;

            case "With" or "AsyncWith":
                yield return With(span, Items(node, "items").ToList(), Block(node, "body"));
                break;

            case "FunctionDef" or "AsyncFunctionDef":
                var nested = Function(node, owner: null);
                _functions.Add(nested);
                yield return new Assign(span, new Name(span, nested.Name), Opaque.Of(span, "function"));
                break;

            case "ClassDef":
                var nestedClass = Class(node);
                _classes.Add(nestedClass);
                yield return new Assign(span, new Name(span, nestedClass.Name), Opaque.Of(span, "class"));
                break;

            case "Import":
                foreach (var alias in Items(node, "names"))
                {
                    var bound = Text(alias, "asname") is { Length: > 0 } asName ? asName : Text(alias, "name").Split('.')[0];
                    yield return new Assign(span, new Name(span, bound), Opaque.Of(span, "module " + Text(alias, "name")));
                }
                break;

            case "ImportFrom":
                foreach (var alias in Items(node, "names").Where(a => Text(a, "name") != "*"))
                {
                    var bound = Text(alias, "asname") is { Length: > 0 } asName ? asName : Text(alias, "name");
                    yield return new Assign(span, new Name(span, bound), Opaque.Of(span, $"{Text(node, "module")}.{Text(alias, "name")}"));
                }
                break;

            case "Global" or "Nonlocal":
                if (_enclosing.Count > 0)
                    _enclosing.Peek().OuterNames.AddRange(Items(node, "names").Select(n => n.GetString() ?? ""));
                break;

            case "Pass" or "TypeAlias":
                break;

            default:
                yield return new OpaqueStmt(span, Kind(node), NamesAssignedIn(node).ToList(), []);
                break;
        }
    }

    private Stmt With(SourceSpan span, List<JsonElement> items, IReadOnlyList<Stmt> body)
    {
        var inner = body;

        for (var i = items.Count - 1; i >= 0; i--)
        {
            var resource = Expression(Field(items[i], "context_expr")!.Value);
            var variable = Field(items[i], "optional_vars") is { } named ? Expression(named) : null;
            var used = new Using(span, resource, variable, inner);
            if (i == 0) return used;
            inner = [used];
        }

        return new OpaqueStmt(span, "with", [], []);
    }

    private Handler Handler(JsonElement node)
    {
        var types = Field(node, "type") switch
        {
            null => ["BaseException"],
            { } type when Kind(type) == "Tuple" => Items(type, "elts").Select(DottedName).ToList(),
            { } type => [DottedName(type)],
        };

        return new Handler(Span(node), types, Text(node, "name") is { Length: > 0 } name ? name : null, Block(node, "body"));
    }

    private static string DottedName(JsonElement node) => Kind(node) switch
    {
        "Name" => Text(node, "id"),
        "Attribute" => Text(node, "attr"),
        _ => "?",
    };

    private IrFunction Function(JsonElement node, string? owner)
    {
        var arguments = Field(node, "args")!.Value;
        var positional = Items(arguments, "posonlyargs").Concat(Items(arguments, "args")).ToList();
        var defaults = Items(arguments, "defaults").ToList();
        var parameters = new List<IrParameter>();

        for (var i = 0; i < positional.Count; i++)
        {
            var defaultIndex = i - (positional.Count - defaults.Count);
            parameters.Add(Parameter(positional[i], defaultIndex >= 0 ? defaults[defaultIndex] : null));
        }

        if (Field(arguments, "vararg") is { } starred)
            parameters.Add(Parameter(starred, null) with { Type = IrType.Named("tuple"), Kind = ParameterKind.Rest });

        var keywordDefaults = Items(arguments, "kw_defaults").ToList();
        var keywordOnly = Items(arguments, "kwonlyargs").ToList();
        for (var i = 0; i < keywordOnly.Count; i++)
            parameters.Add(Parameter(keywordOnly[i], i < keywordDefaults.Count && keywordDefaults[i].ValueKind != JsonValueKind.Null ? keywordDefaults[i] : null)
                with { Kind = ParameterKind.KeywordOnly });

        if (Field(arguments, "kwarg") is { } doubleStarred)
            parameters.Add(Parameter(doubleStarred, null) with { Type = IrType.Named("dict"), Kind = ParameterKind.Keywords });

        var decorators = Items(node, "decorator_list").Select(DottedName).ToList();
        var name = Text(node, "name");
        var fullName = owner is null ? name : $"{owner}.{name}";
        var enclosedBy = _enclosing.Count > 0 ? _enclosing.Peek().Name : IrFunction.ModuleBody;

        var outerNames = new List<string>();
        _enclosing.Push((fullName, outerNames));
        var body = Block(node, "body");
        _enclosing.Pop();

        return new IrFunction(Span(node), name, owner, parameters, TypeOf(Field(node, "returns")), body)
        {
            EnclosedBy = enclosedBy,
            OuterNames = outerNames,
            IsAsync = Kind(node) == "AsyncFunctionDef",
            IsStatic = owner is not null && decorators.Contains("staticmethod"),
            IsConstructor = owner is not null && name == "__init__",
            IsGenerator = Yields(Field(node, "body")!.Value),
            IsDecorated = decorators.Any(d => d is not ("staticmethod" or "classmethod")),
        };
    }

    private IrParameter Parameter(JsonElement argument, JsonElement? defaultValue) =>
        new(Span(argument), Text(argument, "arg"), TypeOf(Field(argument, "annotation")), defaultValue is { } given ? Expression(given) : null);

    private static bool Yields(JsonElement node) => node.ValueKind switch
    {
        JsonValueKind.Array => node.EnumerateArray().Any(Yields),
        JsonValueKind.Object => Kind(node) switch
        {
            "Yield" or "YieldFrom" => true,
            "FunctionDef" or "AsyncFunctionDef" or "Lambda" or "ClassDef" => false,
            _ => node.EnumerateObject().Any(property => Yields(property.Value)),
        },
        _ => false,
    };

    private IrClass Class(JsonElement node)
    {
        var name = Text(node, "name");
        var methods = new List<IrFunction>();
        var fields = new List<IrField>();

        foreach (var member in Items(node, "body"))
        {
            switch (Kind(member))
            {
                case "FunctionDef" or "AsyncFunctionDef":
                    methods.Add(Function(member, name));
                    break;
                case "Assign":
                    foreach (var target in Items(member, "targets").Where(t => Kind(t) == "Name"))
                        fields.Add(new IrField(Span(member), Text(target, "id"), IrType.Unknown, Expression(Field(member, "value")!.Value), IsStatic: true));
                    break;
                case "AnnAssign" when Field(member, "target") is { } target && Kind(target) == "Name":
                    fields.Add(new IrField(Span(member), Text(target, "id"), TypeOf(Field(member, "annotation")),
                        Field(member, "value") is { } value ? Expression(value) : null, IsStatic: false));
                    break;
            }
        }

        foreach (var method in methods)
        {
            if (method.IsStatic || method.Parameters.Count == 0) continue;

            var self = method.Parameters[0].Name;
            foreach (var assigned in AssignedMembers(method.Body, self).Where(f => fields.All(existing => existing.Name != f.Name)))
                fields.Add(new IrField(assigned.Span, assigned.Name, IrType.Unknown, null, IsStatic: false));
        }

        var bases = Items(node, "bases").Select(DottedName).ToList();
        return new IrClass(Span(node), name, bases, fields, methods);
    }

    private static IEnumerable<(SourceSpan Span, string Name)> AssignedMembers(IReadOnlyList<Stmt> body, string self)
    {
        foreach (var statement in body)
        {
            var target = statement switch
            {
                Assign assign => assign.Target,
                _ => null,
            };

            if (target is Member { Target: Name owner } member && owner.Identifier == self)
                yield return (statement.Span, member.MemberName);

            var nested = statement switch
            {
                If branch => branch.Then.Concat(branch.Else),
                While loop => loop.Body,
                ForEach loop => loop.Body,
                Try attempt => attempt.Body.Concat(attempt.Handlers.SelectMany(h => h.Body)).Concat(attempt.Else).Concat(attempt.Finally),
                Using used => used.Body,
                _ => [],
            };

            foreach (var inner in AssignedMembers(nested.ToList(), self)) yield return inner;
        }
    }

    private IrType TypeOf(JsonElement? annotation)
    {
        if (annotation is not { } node) return IrType.Unknown;

        switch (Kind(node))
        {
            case "Name":
                return IrType.Named(Text(node, "id"));
            case "Attribute":
                return IrType.Named(Text(node, "attr"));
            case "Constant" when Field(node, "value") is null:
                return IrType.Nothing;
            case "Constant" when Field(node, "value") is { ValueKind: JsonValueKind.String } forward:
                return IrType.Named(forward.GetString() ?? "?");
            case "Subscript":
                var outer = DottedName(Field(node, "value")!.Value);
                var inside = Field(node, "slice")!.Value;
                var arguments = Kind(inside) == "Tuple" ? Items(inside, "elts").Select(e => TypeOf(e)).ToArray() : [TypeOf(inside)];
                return outer == "Optional" && arguments.Length == 1 ? arguments[0] with { Nullable = true } : IrType.Named(outer.ToLowerInvariant() switch
                {
                    "list" or "dict" or "set" or "tuple" or "frozenset" => outer.ToLowerInvariant(),
                    _ => outer,
                }, arguments);
            case "BinOp" when Kind(Field(node, "op")!.Value) == "BitOr":
                var left = TypeOf(Field(node, "left"));
                var right = TypeOf(Field(node, "right"));
                return right == IrType.Nothing ? left with { Nullable = true } : left == IrType.Nothing ? right with { Nullable = true } : IrType.Unknown;
            default:
                return IrType.Unknown;
        }
    }

    private Expr Expression(JsonElement node)
    {
        var span = Span(node);

        switch (Kind(node))
        {
            case "Name":
                return new Name(span, Text(node, "id"));

            case "Constant":
                return Constant(span, Field(node, "value"));

            case "BinOp":
                return new Binary(span, BinaryOperatorOf(Kind(Field(node, "op")!.Value)),
                    Expression(Field(node, "left")!.Value), Expression(Field(node, "right")!.Value));

            case "BoolOp":
                var logical = Kind(Field(node, "op")!.Value) == "And" ? BinaryOperator.And : BinaryOperator.Or;
                return Items(node, "values").Select(Expression).Aggregate((left, right) => new Binary(span, logical, left, right));

            case "UnaryOp":
                var operand = Expression(Field(node, "operand")!.Value);
                return new Unary(span, Kind(Field(node, "op")!.Value) switch
                {
                    "Not" => UnaryOperator.Not,
                    "USub" => UnaryOperator.Negate,
                    "UAdd" => UnaryOperator.Plus,
                    _ => UnaryOperator.BitNot,
                }, operand);

            case "Compare":
                var operands = new[] { Expression(Field(node, "left")!.Value) }.Concat(Items(node, "comparators").Select(Expression)).ToList();
                var comparisons = Items(node, "ops").Select((op, i) =>
                    (Expr)new Binary(span, BinaryOperatorOf(Kind(op)), operands[i], operands[i + 1])).ToList();
                return comparisons.Aggregate((left, right) => new Binary(span, BinaryOperator.And, left, right));

            case "Call":
                var arguments = Items(node, "args").Select(a => new Argument(null, Expression(a)))
                    .Concat(Items(node, "keywords").Select(k => Text(k, "arg") is { Length: > 0 } keyword
                        ? new Argument(keyword, Expression(Field(k, "value")!.Value))
                        : new Argument(null, Opaque.Of(Span(k), "unpacked", Expression(Field(k, "value")!.Value)))))
                    .ToList();
                return new Call(span, Expression(Field(node, "func")!.Value), arguments);

            case "Attribute":
                return new Member(span, Expression(Field(node, "value")!.Value), Text(node, "attr"));

            case "Subscript":
                var target = Expression(Field(node, "value")!.Value);
                var key = Field(node, "slice")!.Value;
                return Kind(key) == "Slice"
                    ? new Slice(span, target, Optional(key, "lower"), Optional(key, "upper"), Optional(key, "step"))
                    : new ElementAccess(span, target, Expression(key));

            case "List" or "Tuple" or "Set":
                var kind = Kind(node) switch { "List" => CollectionKind.List, "Tuple" => CollectionKind.Tuple, _ => CollectionKind.Set };
                return new CollectionLiteral(span, kind, Items(node, "elts").Select(Expression).ToList());

            case "Dict":
                var keys = Items(node, "keys").ToList();
                var values = Items(node, "values").ToList();
                if (keys.Any(k => k.ValueKind == JsonValueKind.Null))
                    return new Opaque(span, "dictionary with unpacking", values.Select(Expression).ToList());
                return new CollectionLiteral(span, CollectionKind.Dictionary, values.Select(Expression).ToList(), keys.Select(Expression).ToList());

            case "IfExp":
                return new Conditional(span, Expression(Field(node, "test")!.Value),
                    Expression(Field(node, "body")!.Value), Expression(Field(node, "orelse")!.Value));

            case "NamedExpr":
                return new AssignValue(span, Expression(Field(node, "target")!.Value), Expression(Field(node, "value")!.Value));

            case "Await":
                return Opaque.Of(span, "await", Expression(Field(node, "value")!.Value));

            case "Yield" or "YieldFrom":
                return Field(node, "value") is { } yielded ? Opaque.Of(span, "yield", Expression(yielded)) : Opaque.Of(span, "yield");

            case "JoinedStr":
                var formatted = Items(node, "values").Where(v => Kind(v) == "FormattedValue").Select(v => Expression(Field(v, "value")!.Value));
                return new Opaque(span, "formatted text", formatted.ToList());

            case "ListComp" or "SetComp" or "DictComp" or "GeneratorExp":
                var source = Items(node, "generators").Take(1).Select(g => Expression(Field(g, "iter")!.Value));
                return new Opaque(span, Kind(node) switch
                {
                    "ListComp" => "list comprehension",
                    "SetComp" => "set comprehension",
                    "DictComp" => "dictionary comprehension",
                    _ => "generator",
                }, source.ToList());

            case "Lambda":
                return Opaque.Of(span, "lambda");

            case "Starred":
                return Opaque.Of(span, "unpacked", Expression(Field(node, "value")!.Value));

            default:
                return new Opaque(span, Kind(node), []);
        }
    }

    private Expr? Optional(JsonElement node, string name) => Field(node, name) is { } value ? Expression(value) : null;

    private static Expr Constant(SourceSpan span, JsonElement? value) => value switch
    {
        null => new Literal(span, LiteralKind.Null, null),
        { ValueKind: JsonValueKind.True } => new Literal(span, LiteralKind.Boolean, true),
        { ValueKind: JsonValueKind.False } => new Literal(span, LiteralKind.Boolean, false),
        { ValueKind: JsonValueKind.String } text => new Literal(span, LiteralKind.Text, text.GetString()),
        { ValueKind: JsonValueKind.Number } number when IsWholeNumber(number) && number.TryGetInt64(out var whole) =>
            new Literal(span, LiteralKind.Integer, whole),
        { ValueKind: JsonValueKind.Number } number => new Literal(span, LiteralKind.Real, number.GetDouble()),
        { } other => Opaque.Of(span, Kind(other) switch
        {
            "BigInt" => "large whole number",
            "SpecialFloat" => "special number",
            "Bytes" => "bytes",
            "Ellipsis" => "...",
            _ => "constant",
        }),
    };

    private static bool IsWholeNumber(JsonElement number)
    {
        var raw = number.GetRawText();
        return !raw.Contains('.') && !raw.Contains('e') && !raw.Contains('E');
    }

    private static BinaryOperator BinaryOperatorOf(string kind) => kind switch
    {
        "Add" => BinaryOperator.Add,
        "Sub" => BinaryOperator.Subtract,
        "Mult" => BinaryOperator.Multiply,
        "Div" => BinaryOperator.Divide,
        "FloorDiv" => BinaryOperator.FloorDivide,
        "Mod" => BinaryOperator.Modulo,
        "Pow" => BinaryOperator.Power,
        "LShift" => BinaryOperator.ShiftLeft,
        "RShift" => BinaryOperator.ShiftRight,
        "BitOr" => BinaryOperator.BitOr,
        "BitXor" => BinaryOperator.BitXor,
        "BitAnd" => BinaryOperator.BitAnd,
        "MatMult" => BinaryOperator.MatrixMultiply,
        "Eq" => BinaryOperator.Equal,
        "NotEq" => BinaryOperator.NotEqual,
        "Lt" => BinaryOperator.Less,
        "LtE" => BinaryOperator.LessOrEqual,
        "Gt" => BinaryOperator.Greater,
        "GtE" => BinaryOperator.GreaterOrEqual,
        "Is" => BinaryOperator.Is,
        "IsNot" => BinaryOperator.IsNot,
        "In" => BinaryOperator.In,
        "NotIn" => BinaryOperator.NotIn,
        _ => throw new InvalidDataException($"Unknown Python operator {kind}"),
    };

    private static IEnumerable<string> NamesAssignedIn(JsonElement node) => node.ValueKind switch
    {
        JsonValueKind.Array => node.EnumerateArray().SelectMany(NamesAssignedIn),
        JsonValueKind.Object when Kind(node) == "Name" && Field(node, "ctx") is { } context && Kind(context) is "Store" or "Del" =>
            [Text(node, "id")],
        JsonValueKind.Object when Kind(node) is "MatchAs" or "MatchStar" && Text(node, "name") is { Length: > 0 } captured =>
            [captured, .. node.EnumerateObject().SelectMany(p => NamesAssignedIn(p.Value))],
        JsonValueKind.Object => node.EnumerateObject().SelectMany(p => NamesAssignedIn(p.Value)).Distinct(),
        _ => [],
    };
}
