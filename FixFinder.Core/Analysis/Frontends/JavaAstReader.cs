using System.Text.Json;
using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>Turns one file's javac syntax tree, as JSON from <see cref="JavaAstScript"/>, into the IR.</summary>
internal sealed class JavaAstReader(string file)
{
    private readonly List<IrFunction> _functions = [];
    private readonly List<IrClass> _classes = [];

    public (IReadOnlyList<IrFunction> Functions, IReadOnlyList<IrClass> Classes) ReadUnit(JsonElement unit)
    {
        foreach (var declaration in Items(unit, "typeDecls").Where(IsTypeDeclaration))
            _classes.Add(Class(declaration));

        return (_functions, _classes);
    }

    private static string Kind(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty("kind", out var kind) ? kind.GetString() ?? "" : "";

    private static JsonElement? Field(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    private static IEnumerable<JsonElement> Items(JsonElement node, string name) =>
        Field(node, name) is { ValueKind: JsonValueKind.Array } list ? list.EnumerateArray() : [];

    private static string Text(JsonElement node, string name) =>
        Field(node, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? "" : "";

    private static int Number(JsonElement node, string name) =>
        Field(node, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : 0;

    private SourceSpan Span(JsonElement node) =>
        new(file, Number(node, "line"), Number(node, "column"), Number(node, "endLine"), Number(node, "endColumn"));

    private static bool IsTypeDeclaration(JsonElement node) => Kind(node) is "CLASS" or "INTERFACE" or "ENUM" or "RECORD";

    private static bool HasModifier(JsonElement node, string modifier) =>
        Field(node, "modifiers") is { } modifiers && Items(modifiers, "flags").Any(f => string.Equals(f.GetString(), modifier, StringComparison.OrdinalIgnoreCase));

    private IrClass Class(JsonElement node)
    {
        var name = Text(node, "simpleName");
        var fields = new List<IrField>();
        var methods = new List<IrFunction>();

        foreach (var member in Items(node, "members"))
        {
            switch (Kind(member))
            {
                case "METHOD":
                    methods.Add(Function(member, name));
                    break;
                case "VARIABLE":
                    fields.Add(new IrField(Span(member), Text(member, "name"), TypeOf(Field(member, "type")),
                        Field(member, "initializer") is { } initial ? Expression(initial) : null, HasModifier(member, "STATIC")));
                    break;
                case { } when IsTypeDeclaration(member):
                    _classes.Add(Class(member));
                    break;
            }
        }

        var bases = new[] { Field(node, "extendsClause") }.OfType<JsonElement>().Concat(Items(node, "implementsClause"))
            .Select(t => TypeOf(t).Name).ToList();

        return new IrClass(Span(node), name, bases, fields, methods);
    }

    private IrFunction Function(JsonElement node, string owner)
    {
        var constructor = Text(node, "name") == "<init>";
        var parameters = Items(node, "parameters")
            .Select(p => new IrParameter(Span(p), Text(p, "name"), TypeOf(Field(p, "type"))))
            .ToList();
        var body = Field(node, "body") is { } block ? Block(block) : [];

        return new IrFunction(Span(node), constructor ? owner : Text(node, "name"), owner, parameters,
            constructor ? IrType.Nothing : TypeOf(Field(node, "returnType")), body)
        {
            IsStatic = HasModifier(node, "STATIC"),
            IsConstructor = constructor,
        };
    }

    private IReadOnlyList<Stmt> Block(JsonElement node) => Statements(node).ToList();

    private IReadOnlyList<Stmt> Optional(JsonElement node, string name) => Field(node, name) is { } child ? Block(child) : [];

    private IEnumerable<Stmt> Statements(JsonElement node)
    {
        var span = Span(node);

        switch (Kind(node))
        {
            case "BLOCK":
                foreach (var statement in Items(node, "statements").SelectMany(Statements)) yield return statement;
                break;

            case "VARIABLE":
                yield return new Declare(span, Text(node, "name"), TypeOf(Field(node, "type")),
                    Field(node, "initializer") is { } initial ? Expression(initial) : null);
                break;

            case "EXPRESSION_STATEMENT":
                yield return ExpressionStatement(Field(node, "expression")!.Value);
                break;

            case "IF":
                yield return new If(span, Expression(Field(node, "condition")!.Value), Optional(node, "thenStatement"), Optional(node, "elseStatement"));
                break;

            case "WHILE_LOOP" or "DO_WHILE_LOOP":
                yield return new While(span, Expression(Field(node, "condition")!.Value), Optional(node, "statement"), [],
                    TestsFirst: Kind(node) == "WHILE_LOOP");
                break;

            case "FOR_LOOP":
                yield return new For(span, Items(node, "initializer").SelectMany(Statements).ToList(),
                    Field(node, "condition") is { } condition ? Expression(condition) : null,
                    Items(node, "update").SelectMany(Statements).ToList(), Optional(node, "statement"));
                break;

            case "ENHANCED_FOR_LOOP":
                var variable = Field(node, "variable")!.Value;
                yield return new Declare(Span(variable), Text(variable, "name"), TypeOf(Field(variable, "type")), null);
                yield return new ForEach(span, new Name(Span(variable), Text(variable, "name")), Expression(Field(node, "expression")!.Value),
                    Optional(node, "statement"), []);
                break;

            case "RETURN":
                yield return new Return(span, Field(node, "expression") is { } value ? Expression(value) : null);
                break;

            case "BREAK":
                yield return new Break(span, Text(node, "label") is { Length: > 0 } breakLabel ? breakLabel : null);
                break;

            case "CONTINUE":
                yield return new Continue(span, Text(node, "label") is { Length: > 0 } continueLabel ? continueLabel : null);
                break;

            case "THROW":
                yield return new Throw(span, Expression(Field(node, "expression")!.Value));
                break;

            case "TRY":
                yield return Try(node, span);
                break;

            case "SWITCH":
                yield return new Switch(span, Expression(Field(node, "expression")!.Value), Items(node, "cases").Select(Case).ToList());
                break;

            case "ASSERT":
                yield return new AssertThat(span, Expression(Field(node, "condition")!.Value),
                    Field(node, "detail") is { } detail ? Expression(detail) : null);
                break;

            case "LABELED_STATEMENT":
                yield return new Labeled(span, Text(node, "label"), Optional(node, "statement"));
                break;

            case "SYNCHRONIZED":
                yield return new Using(span, Expression(Field(node, "expression")!.Value), null, Optional(node, "block"));
                break;

            case "EMPTY_STATEMENT":
                break;

            case { } when IsTypeDeclaration(node):
                _classes.Add(Class(node));
                break;

            default:
                yield return new OpaqueStmt(span, Kind(node), [], []);
                break;
        }
    }

    private Stmt ExpressionStatement(JsonElement expression)
    {
        var span = Span(expression);
        var kind = Kind(expression);

        if (kind == "ASSIGNMENT")
            return new Assign(span, Expression(Field(expression, "variable")!.Value), Expression(Field(expression, "expression")!.Value));

        if (CompoundOperator(kind) is { } compound)
            return new Assign(span, Expression(Field(expression, "variable")!.Value), Expression(Field(expression, "expression")!.Value), compound);

        if (kind is "PREFIX_INCREMENT" or "POSTFIX_INCREMENT" or "PREFIX_DECREMENT" or "POSTFIX_DECREMENT")
            return new Assign(span, Expression(Field(expression, "expression")!.Value), new Literal(span, LiteralKind.Integer, 1L),
                kind.Contains("INCREMENT") ? BinaryOperator.Add : BinaryOperator.Subtract);

        return new Evaluate(span, Expression(expression));
    }

    private Stmt Try(JsonElement node, SourceSpan span)
    {
        IReadOnlyList<Stmt> body = Optional(node, "block");

        foreach (var resource in Items(node, "resources").Reverse())
        {
            body = Kind(resource) == "VARIABLE"
                ? [new Using(span, Expression(Field(resource, "initializer")!.Value), new Name(Span(resource), Text(resource, "name")), body)]
                : [new Using(span, Expression(resource), null, body)];
        }

        var handlers = Items(node, "catches").Select(Catch).ToList();
        var cleanup = Optional(node, "finallyBlock");

        return handlers.Count == 0 && cleanup.Count == 0 && body.Count == 1 ? body[0] : new Try(span, body, handlers, [], cleanup);
    }

    private Handler Catch(JsonElement node)
    {
        var parameter = Field(node, "parameter")!.Value;
        var type = Field(parameter, "type");
        var types = type is { } caught && Kind(caught) == "UNION_TYPE"
            ? Items(caught, "typeAlternatives").Select(t => TypeOf(t).Name).ToList()
            : [TypeOf(type).Name];

        return new Handler(Span(node), types, Text(parameter, "name"), Optional(node, "block"));
    }

    private SwitchCase Case(JsonElement node)
    {
        var labels = Items(node, "expressions").Select(Expression).ToList();

        if (Text(node, "caseKind") == "RULE")
        {
            var body = Field(node, "body") is { } rule
                ? Kind(rule) is "BLOCK" or "THROW" or "EXPRESSION_STATEMENT" ? Block(rule) : [ExpressionStatement(rule)]
                : [];
            return new SwitchCase(labels, body, FallsThrough: false);
        }

        return new SwitchCase(labels, Items(node, "statements").SelectMany(Statements).ToList(), FallsThrough: true);
    }

    private Expr Expression(JsonElement node)
    {
        var span = Span(node);
        var kind = Kind(node);

        switch (kind)
        {
            case "PARENTHESIZED":
                return Expression(Field(node, "expression")!.Value);

            case "IDENTIFIER":
                return new Name(span, Text(node, "name"));

            case "INT_LITERAL" or "LONG_LITERAL":
                return Field(node, "value") is { ValueKind: JsonValueKind.Number } whole && whole.TryGetInt64(out var integer)
                    ? new Literal(span, LiteralKind.Integer, integer)
                    : Opaque.Of(span, "number");

            case "FLOAT_LITERAL" or "DOUBLE_LITERAL":
                return Field(node, "value") is { ValueKind: JsonValueKind.Number } real
                    ? new Literal(span, LiteralKind.Real, real.GetDouble())
                    : Opaque.Of(span, "special number");

            case "CHAR_LITERAL":
                return new Literal(span, LiteralKind.Character,
                    Field(node, "value") is { } character && Field(character, "char") is { } code ? ((char)code.GetInt32()).ToString() : "?");

            case "STRING_LITERAL":
                return new Literal(span, LiteralKind.Text, Text(node, "value"));

            case "BOOLEAN_LITERAL":
                return new Literal(span, LiteralKind.Boolean, Field(node, "value") is { ValueKind: JsonValueKind.True });

            case "NULL_LITERAL":
                return new Literal(span, LiteralKind.Null, null);

            case "PREFIX_INCREMENT" or "POSTFIX_INCREMENT" or "PREFIX_DECREMENT" or "POSTFIX_DECREMENT":
                var counted = Expression(Field(node, "expression")!.Value);
                var step = new Binary(span, kind.Contains("INCREMENT") ? BinaryOperator.Add : BinaryOperator.Subtract, counted,
                    new Literal(span, LiteralKind.Integer, 1L));
                return new AssignValue(span, counted, step, ValueBeforeAssigning: kind.StartsWith("POSTFIX", StringComparison.Ordinal));

            case "UNARY_MINUS" or "UNARY_PLUS" or "LOGICAL_COMPLEMENT" or "BITWISE_COMPLEMENT":
                return new Unary(span, kind switch
                {
                    "UNARY_MINUS" => UnaryOperator.Negate,
                    "UNARY_PLUS" => UnaryOperator.Plus,
                    "LOGICAL_COMPLEMENT" => UnaryOperator.Not,
                    _ => UnaryOperator.BitNot,
                }, Expression(Field(node, "expression")!.Value));

            case "ASSIGNMENT":
                return new AssignValue(span, Expression(Field(node, "variable")!.Value), Expression(Field(node, "expression")!.Value));

            case var compound when CompoundOperator(compound) is { } op:
                var assigned = Expression(Field(node, "variable")!.Value);
                return new AssignValue(span, assigned, new Binary(span, op, assigned, Expression(Field(node, "expression")!.Value)));

            case var binary when BinaryOperatorOf(binary) is { } op:
                return new Binary(span, op, Expression(Field(node, "leftOperand")!.Value), Expression(Field(node, "rightOperand")!.Value));

            case "METHOD_INVOCATION":
                return new Call(span, Expression(Field(node, "methodSelect")!.Value),
                    Items(node, "arguments").Select(a => new Argument(null, Expression(a))).ToList());

            case "MEMBER_SELECT":
                return new Member(span, Expression(Field(node, "expression")!.Value), Text(node, "identifier"));

            case "ARRAY_ACCESS":
                return new ElementAccess(span, Expression(Field(node, "expression")!.Value), Expression(Field(node, "index")!.Value));

            case "NEW_CLASS":
                if (Field(node, "classBody") is not null) return Opaque.Of(span, "anonymous class");
                return new NewObject(span, TypeOf(Field(node, "identifier")),
                    Items(node, "arguments").Select(a => new Argument(null, Expression(a))).ToList());

            case "NEW_ARRAY":
                if (Field(node, "initializers") is { ValueKind: JsonValueKind.Array } items)
                    return new CollectionLiteral(span, CollectionKind.Array, items.EnumerateArray().Select(Expression).ToList());
                return new NewObject(span, IrType.Named("array", TypeOf(Field(node, "type"))),
                    Items(node, "dimensions").Take(1).Select(d => new Argument(null, Expression(d))).ToList());

            case "CONDITIONAL_EXPRESSION":
                return new Conditional(span, Expression(Field(node, "condition")!.Value),
                    Expression(Field(node, "trueExpression")!.Value), Expression(Field(node, "falseExpression")!.Value));

            case "TYPE_CAST":
                return new Cast(span, TypeOf(Field(node, "type")), Expression(Field(node, "expression")!.Value));

            case "INSTANCE_OF":
                return Opaque.Of(span, "instanceof", Expression(Field(node, "expression")!.Value));

            case "LAMBDA_EXPRESSION":
                return Opaque.Of(span, "lambda expression");

            case "MEMBER_REFERENCE":
                return Opaque.Of(span, "method reference");

            case "SWITCH_EXPRESSION":
                return Opaque.Of(span, "switch expression", Expression(Field(node, "expression")!.Value));

            default:
                return Opaque.Of(span, kind);
        }
    }

    private static BinaryOperator? CompoundOperator(string kind) => kind switch
    {
        "PLUS_ASSIGNMENT" => BinaryOperator.Add,
        "MINUS_ASSIGNMENT" => BinaryOperator.Subtract,
        "MULTIPLY_ASSIGNMENT" => BinaryOperator.Multiply,
        "DIVIDE_ASSIGNMENT" => BinaryOperator.Divide,
        "REMAINDER_ASSIGNMENT" => BinaryOperator.Modulo,
        "AND_ASSIGNMENT" => BinaryOperator.BitAnd,
        "OR_ASSIGNMENT" => BinaryOperator.BitOr,
        "XOR_ASSIGNMENT" => BinaryOperator.BitXor,
        "LEFT_SHIFT_ASSIGNMENT" => BinaryOperator.ShiftLeft,
        "RIGHT_SHIFT_ASSIGNMENT" or "UNSIGNED_RIGHT_SHIFT_ASSIGNMENT" => BinaryOperator.ShiftRight,
        _ => null,
    };

    private static BinaryOperator? BinaryOperatorOf(string kind) => kind switch
    {
        "PLUS" => BinaryOperator.Add,
        "MINUS" => BinaryOperator.Subtract,
        "MULTIPLY" => BinaryOperator.Multiply,
        "DIVIDE" => BinaryOperator.Divide,
        "REMAINDER" => BinaryOperator.Modulo,
        "LESS_THAN" => BinaryOperator.Less,
        "GREATER_THAN" => BinaryOperator.Greater,
        "LESS_THAN_EQUAL" => BinaryOperator.LessOrEqual,
        "GREATER_THAN_EQUAL" => BinaryOperator.GreaterOrEqual,
        "EQUAL_TO" => BinaryOperator.Equal,
        "NOT_EQUAL_TO" => BinaryOperator.NotEqual,
        "CONDITIONAL_AND" => BinaryOperator.And,
        "CONDITIONAL_OR" => BinaryOperator.Or,
        "AND" => BinaryOperator.BitAnd,
        "OR" => BinaryOperator.BitOr,
        "XOR" => BinaryOperator.BitXor,
        "LEFT_SHIFT" => BinaryOperator.ShiftLeft,
        "RIGHT_SHIFT" or "UNSIGNED_RIGHT_SHIFT" => BinaryOperator.ShiftRight,
        _ => null,
    };

    private static IrType TypeOf(JsonElement? node)
    {
        if (node is not { } type) return IrType.Unknown;

        return Kind(type) switch
        {
            "PRIMITIVE_TYPE" => IrType.Named(Text(type, "primitiveTypeKind").ToLowerInvariant()),
            "IDENTIFIER" => Text(type, "name") is "var" ? IrType.Unknown : IrType.Named(Text(type, "name")),
            "MEMBER_SELECT" => IrType.Named(Text(type, "identifier")),
            "ARRAY_TYPE" => IrType.Named("array", TypeOf(Field(type, "type"))),
            "PARAMETERIZED_TYPE" => IrType.Named(TypeOf(Field(type, "type")).Name, Items(type, "typeArguments").Select(t => TypeOf(t)).ToArray()),
            _ => IrType.Unknown,
        };
    }
}
