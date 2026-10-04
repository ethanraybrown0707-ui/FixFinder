using System.Text.Json;
using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>Turns one file's javac syntax tree, as JSON from <see cref="JavaAstScript"/>, into the IR.</summary>
internal sealed class JavaAstReader(string file)
{
    private readonly List<IrFunction> _functions = [];
    private readonly List<IrClass> _classes = [];

    /// <summary>The methods and lambdas being read, innermost on top: a lambda belongs to the one it is written in.</summary>
    private readonly Stack<string> _enclosing = new();

    /// <summary>The classes being read, innermost on top, for a lambda written in a field's initialiser rather than a method.</summary>
    private readonly Stack<string> _owners = new();

    /// <summary>
    /// The variables an instanceof pattern binds - instanceof String text - read in an expression, waiting to be declared
    /// where the statement that tests them starts.
    /// </summary>
    private readonly List<Stmt> _patternVariables = [];

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
        _owners.Push(name);

        foreach (var member in Items(node, "members"))
        {
            switch (Kind(member))
            {
                case "METHOD":
                    methods.Add(Function(member, name));
                    break;
                case "VARIABLE":
                    fields.Add(new IrField(Span(member), Text(member, "name"), TypeOf(Field(member, "type")),
                        Field(member, "initializer") is { } initial ? Expression(initial) : null, HasModifier(member, "STATIC"))
                    {
                        IsVolatile = HasModifier(member, "VOLATILE"),
                    });
                    break;
                case { } when IsTypeDeclaration(member):
                    _classes.Add(Class(member));
                    break;
            }
        }

        var bases = new[] { Field(node, "extendsClause") }.OfType<JsonElement>().Concat(Items(node, "implementsClause"))
            .Select(t => TypeOf(t).Name).ToList();

        _owners.Pop();
        return new IrClass(Span(node), name, bases, fields, methods);
    }

    private IrFunction Function(JsonElement node, string owner)
    {
        var constructor = Text(node, "name") == "<init>";
        var name = constructor ? owner : Text(node, "name");
        var parameters = Parameters(node);

        _enclosing.Push($"{owner}.{name}");
        var body = Field(node, "body") is { } block ? Block(block) : [];
        _enclosing.Pop();

        return new IrFunction(Span(node), name, owner, parameters,
            constructor ? IrType.Nothing : TypeOf(Field(node, "returnType")), body)
        {
            IsStatic = HasModifier(node, "STATIC"),
            IsSynchronized = HasModifier(node, "SYNCHRONIZED"),
            IsConstructor = constructor,
            ExpectedToRaise = TestExpects(node),
        };
    }

    /// <summary>The exception JUnit 4's @Test(expected = IndexOutOfBoundsException.class) says the test method raises to pass.</summary>
    private static List<string> TestExpects(JsonElement method) =>
        Field(method, "modifiers") is not { } modifiers
            ? []
            : Items(modifiers, "annotations")
                .Where(annotation => Field(annotation, "annotationType") is { } type && ClassNamed(type) == "Test")
                .SelectMany(annotation => Items(annotation, "arguments"))
                .Where(argument => Kind(argument) == "ASSIGNMENT" && Field(argument, "variable") is { } named && Text(named, "name") == "expected")
                .Select(argument => Field(argument, "expression"))
                .OfType<JsonElement>()
                .Where(literal => Kind(literal) == "MEMBER_SELECT" && Text(literal, "identifier") == "class" && Field(literal, "expression") is not null)
                .Select(literal => ClassNamed(Field(literal, "expression")!.Value))
                .OfType<string>()
                .ToList();

    /// <summary>The class a name or a dotted name ends with: Test for org.junit.Test.</summary>
    private static string? ClassNamed(JsonElement name) => Kind(name) switch
    {
        "IDENTIFIER" => Text(name, "name"),
        "MEMBER_SELECT" => Text(name, "identifier"),
        _ => null,
    };

    private List<IrParameter> Parameters(JsonElement node) =>
        Items(node, "parameters").Select(p => new IrParameter(Span(p), VariableName(p), TypeOf(Field(p, "type")))).ToList();

    /// <summary>
    /// A variable's name, or _ for an unnamed one: javac gives the _ of Java 22's unnamed variables - for (var _ : list),
    /// catch (Exception _), _ -> 0 - no name at all.
    /// </summary>
    private static string VariableName(JsonElement variable) => Text(variable, "name") is { Length: > 0 } name ? name : "_";

    /// <summary>
    /// A lambda as a function of its own, enclosed by the method it is written in - as the C# reader does. The code a
    /// thread runs is usually written as one, so a lambda nobody reads is a thread nobody checks.
    /// </summary>
    private IrFunction Lambda(JsonElement node)
    {
        var span = Span(node);
        var name = $"lambda at line {span.Line}";
        var enclosedBy = _enclosing.Count > 0 ? _enclosing.Peek() : null;
        var owner = enclosedBy ?? (_owners.Count > 0 ? _owners.Peek() : null);

        _enclosing.Push(owner is null ? name : $"{owner}.{name}");
        var patternsBefore = _patternVariables.Count;
        IReadOnlyList<Stmt> body = Field(node, "body") is not { } written ? []
            : Kind(written) == "BLOCK" ? Block(written)
            : [new Return(Span(written), Expression(written))];

        // A pattern in a lambda's expression - x -> x instanceof String s && s.isEmpty() - binds its variables in the lambda.
        body = [.. TakePatternVariables(patternsBefore), .. body];
        _enclosing.Pop();

        var lambda = new IrFunction(span, name, owner, Parameters(node), IrType.Unknown, body) { EnclosedBy = enclosedBy };
        return lambda with { OuterNames = Captured(lambda) };
    }

    /// <summary>The names a lambda uses from the code around it. Java only lets it read the method's variables, never change them.</summary>
    private static List<string> Captured(IrFunction lambda)
    {
        var own = IrWalk.LocalNames(lambda, assigningDeclares: false);
        return IrWalk.Statements(lambda.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Names)
            .Where(n => n != "this" && !own.Contains(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private IReadOnlyList<Stmt> Block(JsonElement node) => Statements(node).ToList();

    private IReadOnlyList<Stmt> Optional(JsonElement node, string name) => Field(node, name) is { } child ? Block(child) : [];

    /// <summary>
    /// The statements a node is, each after the variables an instanceof pattern in it binds - declared with the value the
    /// pattern matched, which nothing more is known of. Statements written inside it declare their own.
    /// </summary>
    private IEnumerable<Stmt> Statements(JsonElement node)
    {
        var patternsBefore = _patternVariables.Count;

        foreach (var statement in StatementsOf(node))
        {
            foreach (var bound in TakePatternVariables(patternsBefore)) yield return bound;
            yield return statement;
        }
    }

    /// <summary>The pattern variables read since this many were waiting, taken off the list to be declared.</summary>
    private List<Stmt> TakePatternVariables(int before)
    {
        if (_patternVariables.Count <= before) return [];

        var taken = _patternVariables.GetRange(before, _patternVariables.Count - before);
        _patternVariables.RemoveRange(before, taken.Count);
        return taken;
    }

    private IEnumerable<Stmt> StatementsOf(JsonElement node)
    {
        var span = Span(node);

        switch (Kind(node))
        {
            case "BLOCK":
                foreach (var statement in Items(node, "statements").SelectMany(Statements)) yield return statement;
                break;

            // An unnamed variable, var _ = ..., keeps nothing: only what its initialiser does is left.
            case "VARIABLE" when Text(node, "name").Length == 0:
                if (Field(node, "initializer") is { } discarded) yield return new Evaluate(span, Expression(discarded));
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
                var element = VariableName(variable);
                yield return new Declare(Span(variable), element, TypeOf(Field(variable, "type")), null);
                yield return new ForEach(span, new Name(Span(variable), element), Expression(Field(node, "expression")!.Value),
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
                yield return new Using(span, Expression(Field(node, "expression")!.Value), null, Optional(node, "block")) { Purpose = UsingPurpose.Lock };
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
                ? [new Using(span, Expression(Field(resource, "initializer")!.Value), new Name(Span(resource), Text(resource, "name")), body) { Purpose = UsingPurpose.Resource }]
                : [new Using(span, Expression(resource), null, body) { Purpose = UsingPurpose.Resource }];
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

        // catch (Exception _) keeps no variable.
        return new Handler(Span(node), types, Text(parameter, "name") is { Length: > 0 } catchVariable ? catchVariable : null, Optional(node, "block"));
    }

    /// <summary>
    /// One case of a switch, as javac's labels give it: constants are compared with what is switched on; a pattern - case
    /// Circle c, case Square(double side) - may or may not match, and its variables are declared at the start of what the
    /// case runs, with its guard - when c.radius() > 10 - holding over it; default, alone or with case null, is the case
    /// taken when no other is. From JDK 21 javac wraps each label in its kind; from 17 to 20 a label was the constant or the
    /// pattern itself; before 17 there were only the constants, and none meant default.
    /// </summary>
    private SwitchCase Case(JsonElement node)
    {
        var labels = new List<Expr>();
        var isDefault = false;
        var patternVariables = new List<Stmt>();

        if (Field(node, "labels") is { ValueKind: JsonValueKind.Array } written)
        {
            foreach (var label in written.EnumerateArray())
            {
                switch (Kind(label))
                {
                    case "DEFAULT_CASE_LABEL":
                        isDefault = true;
                        break;

                    case "CONSTANT_CASE_LABEL":
                        if (Field(label, "constantExpression") is { } constant) labels.Add(Expression(constant));
                        break;

                    case "PATTERN_CASE_LABEL":
                        labels.Add(Pattern(Field(label, "pattern"), Span(label), patternVariables));
                        break;

                    case var kind when IsPattern(kind):
                        labels.Add(Pattern(label, Span(label), patternVariables));
                        break;

                    default:
                        labels.Add(Expression(label));
                        break;
                }
            }
        }
        else
        {
            labels.AddRange(Items(node, "expressions").Select(Expression));
            isDefault = labels.Count == 0;
        }

        if (isDefault) labels.Clear();

        IReadOnlyList<Stmt> body = Text(node, "caseKind") == "RULE"
            ? Field(node, "body") is { } rule
                ? Kind(rule) is "BLOCK" or "THROW" or "EXPRESSION_STATEMENT" ? Block(rule) : [ExpressionStatement(rule)]
                : []
            : Items(node, "statements").SelectMany(Statements).ToList();

        if (Field(node, "guard") is { } guard)
        {
            var patternsBefore = _patternVariables.Count;
            var holds = Expression(guard);
            body = [.. TakePatternVariables(patternsBefore), new If(Span(guard), holds, body, [])];
        }

        return new SwitchCase(labels, [.. patternVariables, .. body], FallsThrough: Text(node, "caseKind") != "RULE");
    }

    /// <summary>Whether a tree is a pattern: what javac from 17 to 20 gave as a case's label, and what an instanceof tests from 16.</summary>
    private static bool IsPattern(string kind) =>
        kind is "BINDING_PATTERN" or "DECONSTRUCTION_PATTERN" or "RECORD_PATTERN" or "PARENTHESIZED_PATTERN" or "GUARDED_PATTERN" or "ANY_PATTERN";

    /// <summary>
    /// A pattern, as a test that may or may not match - nothing is known of what is switched on beyond its type - with each
    /// variable it binds, however deep in a record pattern, added to <paramref name="variables"/> as declared with the value it
    /// matched. The unnamed _ binds nothing.
    /// </summary>
    private Expr Pattern(JsonElement? pattern, SourceSpan span, List<Stmt> variables)
    {
        void Bind(JsonElement? node)
        {
            if (node is not { } inner) return;

            switch (Kind(inner))
            {
                case "BINDING_PATTERN" when Field(inner, "variable") is { } variable && Text(variable, "name") is { Length: > 0 } name:
                    variables.Add(new Declare(Span(variable), name, TypeOf(Field(variable, "type")), Opaque.Of(Span(inner), "the value the pattern matched")));
                    break;

                case "DECONSTRUCTION_PATTERN" or "RECORD_PATTERN":
                    foreach (var nested in Items(inner, "nestedPatterns")) Bind(nested);
                    break;

                case "PARENTHESIZED_PATTERN" or "GUARDED_PATTERN":
                    Bind(Field(inner, "pattern"));
                    break;
            }
        }

        Bind(pattern);
        return Opaque.Of(span, "pattern");
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
                // instanceof String text binds text where it matched; it is declared where the statement testing it starts.
                var tested = Expression(Field(node, "expression")!.Value);
                if (Field(node, "pattern") is { } pattern) Pattern(pattern, span, _patternVariables);
                return Opaque.Of(span, "instanceof", tested);

            case "LAMBDA_EXPRESSION":
                _functions.Add(Lambda(node));
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
