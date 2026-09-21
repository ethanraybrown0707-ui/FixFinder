using FixFinder.Core.Analysis.Ir;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>
/// Turns one file's Roslyn syntax tree into the IR. Lambdas and local functions become functions of their own, enclosed by
/// the one they are written in, because in C# they can change the variables they capture.
/// </summary>
internal sealed class CSharpSyntaxReader(string file)
{
    /// <summary>Value types, where <c>T?</c> is a real nullable value rather than an annotation on a reference.</summary>
    private static readonly HashSet<string> ValueTypes = new(StringComparer.Ordinal)
    {
        "int", "long", "short", "byte", "sbyte", "uint", "ulong", "ushort", "nint", "nuint", "double", "float", "decimal", "bool", "char",
        "DateTime", "DateTimeOffset", "TimeSpan", "Guid", "DateOnly", "TimeOnly",
    };

    private readonly List<IrFunction> _functions = [];
    private readonly List<IrClass> _classes = [];
    private readonly Stack<(string Name, SyntaxNode Member)> _enclosing = new();
    private readonly Stack<Expr> _receivers = new();
    private HashSet<string> _classNullable = new(StringComparer.Ordinal);
    private HashSet<string> _nullableValues = new(StringComparer.Ordinal);
    private string? _class;

    public (IReadOnlyList<IrFunction> Functions, IReadOnlyList<IrClass> Classes) ReadUnit(CompilationUnitSyntax unit)
    {
        var topLevel = unit.Members.OfType<GlobalStatementSyntax>().Select(g => g.Statement).ToList();
        if (topLevel.Count > 0)
        {
            _enclosing.Push((IrFunction.ModuleBody, unit));
            var body = Block(topLevel);
            _enclosing.Pop();
            _functions.Insert(0, new IrFunction(Span(topLevel[0]), IrFunction.ModuleBody, null, [], IrType.Nothing, body));
        }

        Members(unit.Members);
        return (_functions, _classes);
    }

    private void Members(IEnumerable<MemberDeclarationSyntax> members)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax space:
                    Members(space.Members);
                    break;
                case TypeDeclarationSyntax type:
                    _classes.Add(Class(type));
                    break;
            }
        }
    }

    private SourceSpan Span(SyntaxNode node) => Span(node.GetLocation());

    private SourceSpan Span(Location location)
    {
        var lines = location.GetLineSpan();
        return new SourceSpan(file, lines.StartLinePosition.Line + 1, lines.StartLinePosition.Character,
            lines.EndLinePosition.Line + 1, lines.EndLinePosition.Character);
    }

    private static bool HasModifier(SyntaxTokenList modifiers, SyntaxKind kind) => modifiers.Any(m => m.IsKind(kind));

    private IrClass Class(TypeDeclarationSyntax type)
    {
        var (outerClass, outerNullable) = (_class, _classNullable);
        var name = type.Identifier.ValueText;
        var fields = new List<IrField>();
        var methods = new List<IrFunction>();

        _class = name;
        _classNullable = new HashSet<string>(StringComparer.Ordinal);

        if (type.ParameterList is { } primary)
            fields.AddRange(primary.Parameters.Select(p => new IrField(Span(p), p.Identifier.ValueText, TypeOf(p.Type), null, false)));

        foreach (var member in type.Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    var fieldType = TypeOf(field.Declaration.Type);
                    var isStatic = HasModifier(field.Modifiers, SyntaxKind.StaticKeyword) || HasModifier(field.Modifiers, SyntaxKind.ConstKeyword);
                    fields.AddRange(field.Declaration.Variables.Select(v =>
                        new IrField(Span(v), v.Identifier.ValueText, fieldType, v.Initializer is { } initial ? Initial(initial.Value, fieldType) : null, isStatic)
                        {
                            IsVolatile = HasModifier(field.Modifiers, SyntaxKind.VolatileKeyword),
                        }));
                    break;

                case PropertyDeclarationSyntax property when IsAutomatic(property):
                    fields.Add(new IrField(Span(property), property.Identifier.ValueText, TypeOf(property.Type),
                        property.Initializer is { } value ? Initial(value.Value, TypeOf(property.Type)) : null, HasModifier(property.Modifiers, SyntaxKind.StaticKeyword)));
                    break;
            }
        }

        foreach (var field in fields.Where(f => f.Type.Nullable)) _classNullable.Add(field.Name);

        foreach (var member in type.Members)
        {
            switch (member)
            {
                case PropertyDeclarationSyntax property when !IsAutomatic(property):
                    methods.AddRange(Accessors(property, property.Identifier.ValueText, [], property.ExpressionBody));
                    break;
                case IndexerDeclarationSyntax indexer:
                    methods.AddRange(Accessors(indexer, "this[]", Parameters(indexer.ParameterList.Parameters), indexer.ExpressionBody));
                    break;
                case BaseMethodDeclarationSyntax method when method.Body is not null || method.ExpressionBody is not null:
                    methods.Add(Method(method, name));
                    break;
                case TypeDeclarationSyntax nested:
                    _classes.Add(Class(nested));
                    break;
            }
        }

        (_class, _classNullable) = (outerClass, outerNullable);

        var bases = type.BaseList?.Types.Select(t => TypeOf(t.Type).Name).ToList() ?? [];
        return new IrClass(Span(type), name, bases, fields, methods);
    }

    private static bool IsAutomatic(PropertyDeclarationSyntax property) =>
        property.ExpressionBody is null && property.AccessorList is { } accessors && accessors.Accessors.All(a => a.Body is null && a.ExpressionBody is null);

    private IrFunction Method(BaseMethodDeclarationSyntax method, string owner)
    {
        var (name, returnType, constructor) = method switch
        {
            MethodDeclarationSyntax named => (named.Identifier.ValueText, TypeOf(named.ReturnType), false),
            ConstructorDeclarationSyntax => (owner, IrType.Nothing, true),
            DestructorDeclarationSyntax => ("~" + owner, IrType.Nothing, false),
            OperatorDeclarationSyntax op => ("operator " + op.OperatorToken.ValueText, TypeOf(op.ReturnType), false),
            ConversionOperatorDeclarationSyntax conversion => ("operator " + TypeOf(conversion.Type).Name, TypeOf(conversion.Type), false),
            _ => ("?", IrType.Unknown, false),
        };

        List<Stmt> chained = method is ConstructorDeclarationSyntax { Initializer: { } initializer }
            ? [new Evaluate(Span(initializer), new Call(Span(initializer), new Name(Span(initializer), initializer.ThisOrBaseKeyword.ValueText), Arguments(initializer.ArgumentList)))]
            : [];

        return Function(method, name, owner, Parameters(method.ParameterList.Parameters), returnType, (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression, chained, nested: false) with
        {
            IsStatic = HasModifier(method.Modifiers, SyntaxKind.StaticKeyword),
            IsConstructor = constructor,
            IsAsync = HasModifier(method.Modifiers, SyntaxKind.AsyncKeyword),
        };
    }

    private IEnumerable<IrFunction> Accessors(BasePropertyDeclarationSyntax property, string name, IReadOnlyList<IrParameter> indexes, ArrowExpressionClauseSyntax? arrow)
    {
        var type = TypeOf(property.Type);
        var isStatic = HasModifier(property.Modifiers, SyntaxKind.StaticKeyword);
        var owner = _class!;

        if (arrow is not null)
            yield return Function(property, "get " + name, owner, indexes, type, arrow.Expression, [], nested: false) with { IsStatic = isStatic };

        foreach (var accessor in property.AccessorList?.Accessors ?? [])
        {
            if (accessor.Body is null && accessor.ExpressionBody is null) continue;

            var gets = accessor.Keyword.IsKind(SyntaxKind.GetKeyword);
            IReadOnlyList<IrParameter> parameters = gets ? indexes : [.. indexes, new IrParameter(Span(accessor), "value", type)];

            yield return Function(accessor, $"{accessor.Keyword.ValueText} {name}", owner, parameters, gets ? type : IrType.Nothing,
                (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression, [], nested: false) with { IsStatic = isStatic };
        }
    }

    private List<IrParameter> Parameters(IEnumerable<ParameterSyntax> parameters) =>
        parameters.Select(p => new IrParameter(Span(p), p.Identifier.ValueText, TypeOf(p.Type), p.Default is { } value ? Expression(value.Value) : null)).ToList();

    private IrFunction Function(
        SyntaxNode node, string name, string? owner, IReadOnlyList<IrParameter> parameters, IrType returnType, SyntaxNode? body, IReadOnlyList<Stmt> prefix, bool nested)
    {
        var fullName = owner is null ? name : $"{owner}.{name}";
        var enclosedBy = nested && _enclosing.Count > 0 ? _enclosing.Peek().Name : null;

        var outerNullable = _nullableValues;
        _nullableValues = new HashSet<string>(nested ? outerNullable : _classNullable, StringComparer.Ordinal);
        foreach (var parameter in parameters.Where(p => p.Type.Nullable)) _nullableValues.Add(parameter.Name);
        _enclosing.Push((fullName, node));

        var statements = new List<Stmt>(prefix);
        switch (body)
        {
            case BlockSyntax block:
                statements.AddRange(Block(block.Statements));
                break;
            case ExpressionSyntax value when returnType.Name == IrType.Nothing.Name:
                statements.Add(ExpressionStatement(value));
                break;
            case ExpressionSyntax value:
                statements.Add(new Return(Span(value), Expression(value)));
                break;
        }

        _enclosing.Pop();
        _nullableValues = outerNullable;

        var function = new IrFunction(Span(node), name, owner, parameters, returnType, statements)
        {
            EnclosedBy = enclosedBy,
            IsGenerator = body is not null && Inside(body).OfType<YieldStatementSyntax>().Any(),
        };

        return nested ? function with { OuterNames = Captured(function) } : function;
    }

    /// <summary>The nodes of a body that belong to it, not to a lambda or local function written inside it.</summary>
    private static IEnumerable<SyntaxNode> Inside(SyntaxNode node) =>
        node.DescendantNodesAndSelf(n => n == node || n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax));

    /// <summary>The variables a lambda or local function uses from the code around it - which it can also change.</summary>
    private static List<string> Captured(IrFunction function)
    {
        var own = IrWalk.LocalNames(function, assigningDeclares: false);
        return IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Names)
            .Where(n => n != "this" && !own.Contains(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private string EnclosingName => _enclosing.Count > 0 ? _enclosing.Peek().Name : _class ?? IrFunction.ModuleBody;

    private IrFunction Lambda(AnonymousFunctionExpressionSyntax lambda)
    {
        IEnumerable<ParameterSyntax> parameters = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => [simple.Parameter],
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters,
            AnonymousMethodExpressionSyntax anonymous => anonymous.ParameterList?.Parameters ?? [],
            _ => [],
        };

        return Function(lambda, $"lambda at line {Span(lambda).Line}", EnclosingName, Parameters(parameters), IrType.Unknown,
            (SyntaxNode?)lambda.Block ?? lambda.ExpressionBody, [], nested: true) with { IsAsync = lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword) };
    }

    private IrFunction LocalFunction(LocalFunctionStatementSyntax local) =>
        Function(local, local.Identifier.ValueText, EnclosingName, Parameters(local.ParameterList.Parameters), TypeOf(local.ReturnType),
            (SyntaxNode?)local.Body ?? local.ExpressionBody?.Expression, [], nested: true) with
        {
            IsStatic = HasModifier(local.Modifiers, SyntaxKind.StaticKeyword),
            IsAsync = HasModifier(local.Modifiers, SyntaxKind.AsyncKeyword),
        };

    private List<Stmt> Block(IReadOnlyList<StatementSyntax> statements)
    {
        var block = new List<Stmt>();

        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is LocalDeclarationStatementSyntax local && local.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
            {
                block.AddRange(Declarations([local.Declaration]));
                block.AddRange(Used(local.Declaration, Span(local), Block(statements.Skip(i + 1).ToList())));
                break;
            }

            block.AddRange(Statements(statements[i]));
        }

        return block;
    }

    private List<Stmt> Body(StatementSyntax statement) => Statements(statement).ToList();

    /// <summary>A using declaration or statement: each variable is declared, then holds its resource until the block ends.</summary>
    private IEnumerable<Stmt> Used(VariableDeclarationSyntax declaration, SourceSpan span, IReadOnlyList<Stmt> body)
    {
        var type = TypeOf(declaration.Type);

        foreach (var variable in declaration.Variables.Reverse())
        {
            var resource = variable.Initializer is { } initial ? Initial(initial.Value, type) : Opaque.Of(Span(variable), "resource");
            body = [new Using(span, resource, new Name(Span(variable.Identifier.GetLocation()), variable.Identifier.ValueText), body)];
        }

        foreach (var variable in declaration.Variables)
            yield return new Declare(Span(variable), variable.Identifier.ValueText, type, null);

        foreach (var used in body) yield return used;
    }

    private IEnumerable<Stmt> Statements(StatementSyntax statement)
    {
        var span = Span(statement);
        var declared = Declarations(Heads(statement)).ToList();

        IEnumerable<Stmt> read = statement switch
        {
            BlockSyntax block => Block(block.Statements),
            LocalDeclarationStatementSyntax local => Declared(local.Declaration),
            ExpressionStatementSyntax expression => [ExpressionStatement(expression.Expression)],
            IfStatementSyntax branch => [new If(span, Expression(branch.Condition), Body(branch.Statement), branch.Else is { } otherwise ? Body(otherwise.Statement) : [])],
            WhileStatementSyntax loop => [new While(span, Expression(loop.Condition), Body(loop.Statement), [])],
            DoStatementSyntax loop => [new While(span, Expression(loop.Condition), Body(loop.Statement), [], TestsFirst: false)],
            ForStatementSyntax loop => [For(loop, span)],
            ForEachStatementSyntax loop =>
            [
                new Declare(Span(loop.Identifier.GetLocation()), loop.Identifier.ValueText, TypeOf(loop.Type), null),
                new ForEach(span, new Name(Span(loop.Identifier.GetLocation()), loop.Identifier.ValueText), Expression(loop.Expression), Body(loop.Statement), []),
            ],
            ForEachVariableStatementSyntax loop => [new ForEach(span, Expression(loop.Variable), Expression(loop.Expression), Body(loop.Statement), [])],
            ReturnStatementSyntax leave => [new Return(span, leave.Expression is { } value ? Expression(value) : null)],
            BreakStatementSyntax => [new Break(span)],
            ContinueStatementSyntax => [new Continue(span)],
            ThrowStatementSyntax raise => [new Throw(span, raise.Expression is { } thrown ? Expression(thrown) : null)],
            TryStatementSyntax attempt =>
            [
                new Try(span, Block(attempt.Block.Statements), attempt.Catches.Select(Catch).ToList(), [], attempt.Finally is { } cleanup ? Block(cleanup.Block.Statements) : []),
            ],
            SwitchStatementSyntax choice => [new Switch(span, Expression(choice.Expression), choice.Sections.Select(Section).ToList())],
            UsingStatementSyntax { Declaration: { } resources } used => Used(resources, span, Body(used.Statement)),
            UsingStatementSyntax used => [new Using(span, used.Expression is { } resource ? Expression(resource) : Opaque.Of(span, "resource"), null, Body(used.Statement))],
            LockStatementSyntax locked => [new Using(span, Expression(locked.Expression), null, Body(locked.Statement))],
            CheckedStatementSyntax checkedBlock => Block(checkedBlock.Block.Statements),
            UnsafeStatementSyntax unsafeBlock => Block(unsafeBlock.Block.Statements),
            FixedStatementSyntax pinned => [.. Declared(pinned.Declaration), .. Body(pinned.Statement)],
            LabeledStatementSyntax labeled => [new OpaqueStmt(span, "goto target", DeclaredIn(_enclosing.Peek().Member), []), .. Body(labeled.Statement)],
            GotoStatementSyntax => [new Return(span, null)],
            YieldStatementSyntax { RawKind: (int)SyntaxKind.YieldBreakStatement } => [new Return(span, null)],
            YieldStatementSyntax yielded => [new Evaluate(span, Opaque.Of(span, "yield", yielded.Expression is { } value ? [Expression(value)] : []))],
            LocalFunctionStatementSyntax local => Registered(LocalFunction(local)),
            EmptyStatementSyntax => [],
            _ => [new OpaqueStmt(span, statement.Kind().ToString(), [], [])],
        };

        return [.. declared, .. read];
    }

    private List<Stmt> Registered(IrFunction function)
    {
        _functions.Add(function);
        return [];
    }

    private List<Stmt> Declared(VariableDeclarationSyntax declaration)
    {
        var type = TypeOf(declaration.Type);
        var statements = new List<Stmt>();

        foreach (var variable in declaration.Variables)
        {
            if (type.Nullable) _nullableValues.Add(variable.Identifier.ValueText);
            statements.Add(new Declare(Span(variable), variable.Identifier.ValueText, type, variable.Initializer is { } initial ? Initial(initial.Value, type) : null));
        }

        return statements;
    }

    private For For(ForStatementSyntax loop, SourceSpan span)
    {
        var setup = loop.Declaration is { } declaration ? Declared(declaration) : [];
        setup.AddRange(loop.Initializers.Select(ExpressionStatement));

        return new For(span, setup, loop.Condition is { } condition ? Expression(condition) : null,
            loop.Incrementors.Select(ExpressionStatement).ToList(), Body(loop.Statement));
    }

    private Handler Catch(CatchClauseSyntax clause) =>
        new(Span(clause), clause.Declaration is { } caught ? [TypeOf(caught.Type).Name] : [],
            clause.Declaration?.Identifier.ValueText is { Length: > 0 } variable ? variable : null, Block(clause.Block.Statements));

    private SwitchCase Section(SwitchSectionSyntax section)
    {
        var labels = section.Labels.Any(l => l is DefaultSwitchLabelSyntax) ? [] : section.Labels.Select(Label).ToList();
        return new SwitchCase(labels, Block(section.Statements), FallsThrough: false);
    }

    private Expr Label(SwitchLabelSyntax label) => label switch
    {
        CaseSwitchLabelSyntax constant => Expression(constant.Value),
        CasePatternSwitchLabelSyntax { Pattern: ConstantPatternSyntax constant, WhenClause: null } => Expression(constant.Expression),
        _ => Opaque.Of(Span(label), "case pattern"),
    };

    /// <summary>The parts of a statement evaluated before anything nested in it, where out and pattern variables are declared.</summary>
    private static IEnumerable<SyntaxNode?> Heads(StatementSyntax statement) => statement switch
    {
        ExpressionStatementSyntax expression => [expression.Expression],
        LocalDeclarationStatementSyntax local when !local.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) => [local.Declaration],
        IfStatementSyntax branch => [branch.Condition],
        WhileStatementSyntax loop => [loop.Condition],
        DoStatementSyntax loop => [loop.Condition],
        ForStatementSyntax loop => [loop.Declaration, .. loop.Initializers, loop.Condition, .. loop.Incrementors],
        ForEachStatementSyntax loop => [loop.Expression],
        ForEachVariableStatementSyntax loop => [loop.Variable, loop.Expression],
        ReturnStatementSyntax leave => [leave.Expression],
        ThrowStatementSyntax raise => [raise.Expression],
        SwitchStatementSyntax choice => [choice.Expression, .. choice.Sections.SelectMany(s => s.Labels)],
        UsingStatementSyntax used => [used.Declaration, used.Expression],
        LockStatementSyntax locked => [locked.Expression],
        YieldStatementSyntax yielded => [yielded.Expression],
        _ => [],
    };

    /// <summary>Variables declared inside an expression - out var n, is string s, var (a, b) - declared as statements of their own.</summary>
    private IEnumerable<Stmt> Declarations(IEnumerable<SyntaxNode?> heads)
    {
        foreach (var node in heads.OfType<SyntaxNode>().SelectMany(Inside))
        {
            var (designation, type) = node switch
            {
                DeclarationExpressionSyntax declaration => (declaration.Designation, TypeOf(declaration.Type)),
                DeclarationPatternSyntax pattern => (pattern.Designation, TypeOf(pattern.Type)),
                VarPatternSyntax pattern => (pattern.Designation, IrType.Unknown),
                RecursivePatternSyntax { Designation: { } named } pattern => (named, TypeOf(pattern.Type)),
                _ => ((VariableDesignationSyntax?)null, IrType.Unknown),
            };

            foreach (var single in Singles(designation))
                yield return new Declare(Span(single), single.Identifier.ValueText, designation is SingleVariableDesignationSyntax ? type : IrType.Unknown, null);
        }
    }

    private static IEnumerable<SingleVariableDesignationSyntax> Singles(VariableDesignationSyntax? designation) => designation switch
    {
        SingleVariableDesignationSyntax single => [single],
        ParenthesizedVariableDesignationSyntax several => several.Variables.SelectMany(Singles),
        _ => [],
    };

    /// <summary>Every name a function binds, forgotten where a goto can arrive from anywhere in it.</summary>
    private static List<string> DeclaredIn(SyntaxNode member) =>
        member.DescendantNodesAndSelf().Select(node => node switch
        {
            ParameterSyntax parameter => parameter.Identifier.ValueText,
            VariableDeclaratorSyntax variable => variable.Identifier.ValueText,
            SingleVariableDesignationSyntax designated => designated.Identifier.ValueText,
            ForEachStatementSyntax loop => loop.Identifier.ValueText,
            CatchDeclarationSyntax caught => caught.Identifier.ValueText,
            _ => "",
        }).Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).ToList();

    private Stmt ExpressionStatement(ExpressionSyntax expression)
    {
        var span = Span(expression);

        switch (expression)
        {
            case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
                return new Assign(span, Expression(assignment.Left), Expression(assignment.Right));

            case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression):
                var held = Expression(assignment.Left);
                return new Assign(span, held, new Conditional(span, NotNull(held, span), held, Expression(assignment.Right)));

            case AssignmentExpressionSyntax assignment when CompoundOperator(assignment.Kind()) is { } compound:
                return new Assign(span, Expression(assignment.Left), Expression(assignment.Right), compound);

            case PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax when Step(expression) is { } step:
                return new Assign(span, Expression(step.Operand), new Literal(span, LiteralKind.Integer, 1L), step.Operator);

            default:
                return new Evaluate(span, Expression(expression));
        }
    }

    private static (ExpressionSyntax Operand, BinaryOperator Operator)? Step(ExpressionSyntax expression) => expression switch
    {
        PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PreIncrementExpression } prefix => (prefix.Operand, BinaryOperator.Add),
        PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PreDecrementExpression } prefix => (prefix.Operand, BinaryOperator.Subtract),
        PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PostIncrementExpression } postfix => (postfix.Operand, BinaryOperator.Add),
        PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PostDecrementExpression } postfix => (postfix.Operand, BinaryOperator.Subtract),
        _ => null,
    };

    private static Binary NotNull(Expr value, SourceSpan span) => new(span, BinaryOperator.NotEqual, value, new Literal(span, LiteralKind.Null, null));

    /// <summary>A value given to a variable of a known type, so <c>new()</c> and <c>[1, 2]</c> can take their type from it.</summary>
    private Expr Initial(ExpressionSyntax value, IrType type) => value switch
    {
        ImplicitObjectCreationExpressionSyntax created => Creation(type, created.ArgumentList, created.Initializer, Span(created)),
        CollectionExpressionSyntax collection => Collection(collection, CollectionKindOf(type.Name) ?? CollectionKind.List),
        _ => Expression(value),
    };

    private Expr Expression(ExpressionSyntax node)
    {
        var span = Span(node);

        switch (node)
        {
            case ParenthesizedExpressionSyntax inner:
                return Expression(inner.Expression);
            case CheckedExpressionSyntax inner:
                return Expression(inner.Expression);
            case RefExpressionSyntax inner:
                return Expression(inner.Expression);

            case IdentifierNameSyntax identifier:
                return new Name(span, identifier.Identifier.ValueText);
            case GenericNameSyntax generic:
                return new Name(span, generic.Identifier.ValueText);
            case PredefinedTypeSyntax predefined:
                return new Name(span, predefined.Keyword.ValueText);
            case QualifiedNameSyntax qualified:
                return new Member(span, Expression(qualified.Left), qualified.Right.Identifier.ValueText);
            case AliasQualifiedNameSyntax alias:
                return new Name(span, alias.Name.Identifier.ValueText);
            case ThisExpressionSyntax:
                return new Name(span, "this");
            case BaseExpressionSyntax:
                return new Name(span, "base");

            case LiteralExpressionSyntax literal:
                return Literal(literal, span);
            case InterpolatedStringExpressionSyntax interpolated:
                return Opaque.Of(span, "formatted text", interpolated.Contents.OfType<InterpolationSyntax>().Select(i => Expression(i.Expression)).ToArray());

            case MemberAccessExpressionSyntax access:
                return MemberAccess(Expression(access.Expression), access.Name.Identifier.ValueText, span);
            case ConditionalAccessExpressionSyntax conditional:
                return ConditionalAccess(conditional, span);
            case MemberBindingExpressionSyntax binding when _receivers.Count > 0:
                return MemberAccess(_receivers.Peek(), binding.Name.Identifier.ValueText, span);
            case ElementBindingExpressionSyntax binding when _receivers.Count > 0:
                return Element(_receivers.Peek(), binding.ArgumentList, span);

            case InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }, ArgumentList.Arguments: [var named] }:
                return new Literal(span, LiteralKind.Text, named.Expression.ToString().Split('.')[^1]);
            case InvocationExpressionSyntax call:
                return new Call(span, Expression(call.Expression), Arguments(call.ArgumentList));
            case ElementAccessExpressionSyntax element:
                return Element(Expression(element.Expression), element.ArgumentList, span);

            case ObjectCreationExpressionSyntax created:
                return Creation(TypeOf(created.Type), created.ArgumentList, created.Initializer, span);
            case ImplicitObjectCreationExpressionSyntax created:
                return Creation(IrType.Unknown, created.ArgumentList, created.Initializer, span);
            case ArrayCreationExpressionSyntax { Initializer: { } items }:
                return Items(CollectionKind.Array, items, span);
            case ArrayCreationExpressionSyntax array:
                var sizes = array.Type.RankSpecifiers[0].Sizes;
                return new NewObject(span, IrType.Named("array", TypeOf(array.Type.ElementType)),
                    sizes is [var size] && size is not OmittedArraySizeExpressionSyntax ? [new Argument(null, Expression(size))] : []);
            case ImplicitArrayCreationExpressionSyntax array:
                return Items(CollectionKind.Array, array.Initializer, span);
            case InitializerExpressionSyntax items:
                return Items(CollectionKind.Array, items, span);
            case CollectionExpressionSyntax collection:
                return Collection(collection, CollectionKind.List);

            case AssignmentExpressionSyntax assignment:
                return AssignmentValue(assignment, span);
            case PrefixUnaryExpressionSyntax prefix:
                return Prefix(prefix, span);
            case PostfixUnaryExpressionSyntax postfix:
                return Postfix(postfix, span);
            case BinaryExpressionSyntax binary:
                return BinaryExpression(binary, span);
            case ConditionalExpressionSyntax choice:
                return new Conditional(span, Expression(choice.Condition), Expression(choice.WhenTrue), Expression(choice.WhenFalse));
            case CastExpressionSyntax cast:
                return new Cast(span, TypeOf(cast.Type), Expression(cast.Expression));
            case IsPatternExpressionSyntax test:
                return Pattern(Expression(test.Expression), test.Pattern, span);

            case AnonymousFunctionExpressionSyntax lambda:
                _functions.Add(Lambda(lambda));
                return Opaque.Of(span, "lambda expression");
            case AwaitExpressionSyntax awaited:
                return Opaque.Of(span, "await", Expression(awaited.Expression));
            case TupleExpressionSyntax tuple:
                return new CollectionLiteral(span, CollectionKind.Tuple, tuple.Arguments.Select(a => Expression(a.Expression)).ToList());
            case DeclarationExpressionSyntax declaration:
                return Designated(declaration.Designation, span);
            case ThrowExpressionSyntax thrown:
                return Opaque.Of(span, "throw expression", Expression(thrown.Expression));
            case SwitchExpressionSyntax choice:
                foreach (var arm in choice.Arms) Expression(arm.Expression);
                return Opaque.Of(span, "switch expression", Expression(choice.GoverningExpression));
            case WithExpressionSyntax copied:
                return Opaque.Of(span, "with expression", [Expression(copied.Expression), .. InitializerValues(copied.Initializer)]);

            default:
                return Opaque.Of(span, node.Kind().ToString(), node.ChildNodes().OfType<ExpressionSyntax>()
                    .Where(e => e is not (PredefinedTypeSyntax or ArrayTypeSyntax or NullableTypeSyntax or PointerTypeSyntax or TupleTypeSyntax))
                    .Select(Expression).ToArray());
        }
    }

    private static Expr Literal(LiteralExpressionSyntax literal, SourceSpan span) => literal.Kind() switch
    {
        SyntaxKind.NumericLiteralExpression => literal.Token.Value switch
        {
            float or double or decimal => new Literal(span, LiteralKind.Real, Convert.ToDouble(literal.Token.Value)),
            ulong big when big > long.MaxValue => new Literal(span, LiteralKind.Integer, (double)big),
            { } whole => new Literal(span, LiteralKind.Integer, Convert.ToInt64(whole)),
            _ => Opaque.Of(span, "number"),
        },
        SyntaxKind.StringLiteralExpression or SyntaxKind.Utf8StringLiteralExpression => new Literal(span, LiteralKind.Text, literal.Token.ValueText),
        SyntaxKind.CharacterLiteralExpression => new Literal(span, LiteralKind.Character, literal.Token.ValueText),
        SyntaxKind.TrueLiteralExpression => new Literal(span, LiteralKind.Boolean, true),
        SyntaxKind.FalseLiteralExpression => new Literal(span, LiteralKind.Boolean, false),
        SyntaxKind.NullLiteralExpression => new Literal(span, LiteralKind.Null, null),
        _ => Opaque.Of(span, "default"),
    };

    /// <summary>
    /// A nullable value type is not a reference: <c>x.HasValue</c> is <c>x != null</c>, <c>x.Value</c> is x, and nothing
    /// read from it throws a NullReferenceException.
    /// </summary>
    private Expr MemberAccess(Expr target, string member, SourceSpan span)
    {
        if (!IsNullableValue(target)) return new Member(span, target, member);

        return member switch
        {
            "HasValue" => NotNull(target, span),
            "Value" => target,
            _ => new Member(span, Opaque.Of(target.Span, "nullable value", target), member),
        };
    }

    private bool IsNullableValue(Expr target) => target switch
    {
        Name name => _nullableValues.Contains(name.Identifier),
        Member { Target: Name { Identifier: "this" }, MemberName: var field } => _classNullable.Contains(field),
        _ => false,
    };

    /// <summary>
    /// <c>a?.b</c> reads b only when a is not null, and is null otherwise. A receiver the analysis cannot narrow by name, like
    /// <c>(x ?? y)?.b</c>, is marked as not null where b is read.
    /// </summary>
    private Expr ConditionalAccess(ConditionalAccessExpressionSyntax conditional, SourceSpan span)
    {
        var receiver = Expression(conditional.Expression);
        var narrowable = receiver is Name || receiver is Member { Target: Name { Identifier: "this" } };

        _receivers.Push(narrowable ? receiver : Opaque.Of(receiver.Span, "not null", receiver));
        var whenNotNull = Expression(conditional.WhenNotNull);
        _receivers.Pop();

        return new Conditional(span, NotNull(receiver, span), whenNotNull, new Literal(span, LiteralKind.Null, null));
    }

    private Expr Element(Expr target, BracketedArgumentListSyntax arguments, SourceSpan span)
    {
        if (arguments.Arguments is not [var only])
            return Opaque.Of(span, "element", [target, .. arguments.Arguments.Select(a => Expression(a.Expression))]);

        return only.Expression is RangeExpressionSyntax range
            ? new Slice(span, target, range.LeftOperand is { } low ? Expression(low) : null, range.RightOperand is { } high ? Expression(high) : null, null)
            : new ElementAccess(span, target, Expression(only.Expression));
    }

    private List<Argument> Arguments(BaseArgumentListSyntax? arguments) =>
        arguments?.Arguments.Select(a => new Argument(a.NameColon?.Name.Identifier.ValueText, ArgumentValue(a))).ToList() ?? [];

    /// <summary>An out or ref argument is a variable the call sets.</summary>
    private Expr ArgumentValue(ArgumentSyntax argument)
    {
        var value = Expression(argument.Expression);
        var setsIt = argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) || argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword);

        return setsIt && value is Name or Member or ElementAccess or CollectionLiteral
            ? new AssignValue(Span(argument), value, Opaque.Of(Span(argument), "set by the call"))
            : value;
    }

    private Expr Creation(IrType type, ArgumentListSyntax? arguments, InitializerExpressionSyntax? initializer, SourceSpan span)
    {
        var given = Arguments(arguments);
        if (initializer is null) return new NewObject(span, type, given);

        if (initializer.IsKind(SyntaxKind.CollectionInitializerExpression) && given.Count == 0 && CollectionKindOf(type.Name) is { } kind)
            return Items(kind, initializer, span);

        return new NewObject(span, type, [.. given, new Argument("initializer", Opaque.Of(Span(initializer), "initializer", InitializerValues(initializer)))]);
    }

    /// <summary>The values an object initializer sets; its member names are not variables of the function.</summary>
    private Expr[] InitializerValues(InitializerExpressionSyntax initializer) =>
        initializer.Expressions.Select(e => e is AssignmentExpressionSyntax set ? Expression(set.Right) : Expression(e)).ToArray();

    private static CollectionKind? CollectionKindOf(string type) => type switch
    {
        "List" or "IList" or "ICollection" or "IEnumerable" or "IReadOnlyList" or "IReadOnlyCollection" or "Collection" or "ObservableCollection"
            or "LinkedList" or "array" => CollectionKind.List,
        "HashSet" or "SortedSet" or "ISet" => CollectionKind.Set,
        "Dictionary" or "SortedDictionary" or "SortedList" or "ConcurrentDictionary" or "IDictionary" or "IReadOnlyDictionary" => CollectionKind.Dictionary,
        _ => null,
    };

    private Expr Items(CollectionKind kind, InitializerExpressionSyntax initializer, SourceSpan span)
    {
        if (kind != CollectionKind.Dictionary)
            return new CollectionLiteral(span, kind, initializer.Expressions.Select(Expression).ToList());

        var keys = new List<Expr>();
        var values = new List<Expr>();

        foreach (var entry in initializer.Expressions)
        {
            var (key, value) = entry switch
            {
                InitializerExpressionSyntax { Expressions: [var k, var v] } => (Expression(k), Expression(v)),
                AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax { ArgumentList.Arguments: [var k] } } set => (Expression(k.Expression), Expression(set.Right)),
                _ => (Opaque.Of(Span(entry), "key"), Expression(entry)),
            };
            keys.Add(key);
            values.Add(value);
        }

        return new CollectionLiteral(span, CollectionKind.Dictionary, values, keys);
    }

    private Expr Collection(CollectionExpressionSyntax collection, CollectionKind kind)
    {
        var span = Span(collection);

        if (collection.Elements.All(e => e is ExpressionElementSyntax) && kind != CollectionKind.Dictionary)
            return new CollectionLiteral(span, kind, collection.Elements.Cast<ExpressionElementSyntax>().Select(e => Expression(e.Expression)).ToList());

        return Opaque.Of(span, "collection", collection.Elements.Select(e => e switch
        {
            ExpressionElementSyntax item => Expression(item.Expression),
            SpreadElementSyntax spread => Expression(spread.Expression),
            _ => Opaque.Of(Span(e), "element"),
        }).ToArray());
    }

    private Expr AssignmentValue(AssignmentExpressionSyntax assignment, SourceSpan span)
    {
        var target = Expression(assignment.Left);

        if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)) return new AssignValue(span, target, Expression(assignment.Right));
        if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
            return new AssignValue(span, target, new Conditional(span, NotNull(target, span), target, Expression(assignment.Right)));

        return CompoundOperator(assignment.Kind()) is { } op
            ? new AssignValue(span, target, new Binary(span, op, target, Expression(assignment.Right)))
            : Opaque.Of(span, assignment.Kind().ToString(), target, Expression(assignment.Right));
    }

    private Expr Prefix(PrefixUnaryExpressionSyntax prefix, SourceSpan span)
    {
        if (Step(prefix) is { } step)
        {
            var counted = Expression(step.Operand);
            return new AssignValue(span, counted, new Binary(span, step.Operator, counted, new Literal(span, LiteralKind.Integer, 1L)));
        }

        var operand = Expression(prefix.Operand);
        return prefix.Kind() switch
        {
            SyntaxKind.UnaryMinusExpression => new Unary(span, UnaryOperator.Negate, operand),
            SyntaxKind.UnaryPlusExpression => new Unary(span, UnaryOperator.Plus, operand),
            SyntaxKind.LogicalNotExpression => new Unary(span, UnaryOperator.Not, operand),
            SyntaxKind.BitwiseNotExpression => new Unary(span, UnaryOperator.BitNot, operand),
            SyntaxKind.IndexExpression => Opaque.Of(span, "from the end", operand),
            _ => Opaque.Of(span, prefix.Kind().ToString(), operand),
        };
    }

    private Expr Postfix(PostfixUnaryExpressionSyntax postfix, SourceSpan span)
    {
        if (Step(postfix) is { } step)
        {
            var counted = Expression(step.Operand);
            return new AssignValue(span, counted, new Binary(span, step.Operator, counted, new Literal(span, LiteralKind.Integer, 1L)), ValueBeforeAssigning: true);
        }

        return postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression)
            ? Opaque.Of(span, "not null", Expression(postfix.Operand))
            : Opaque.Of(span, postfix.Kind().ToString(), Expression(postfix.Operand));
    }

    private Expr BinaryExpression(BinaryExpressionSyntax binary, SourceSpan span)
    {
        switch (binary.Kind())
        {
            case SyntaxKind.CoalesceExpression:
                var value = Expression(binary.Left);
                return new Conditional(span, NotNull(value, span), value, Expression(binary.Right));
            case SyntaxKind.IsExpression:
                return Opaque.Of(span, "is pattern", Expression(binary.Left));
            case SyntaxKind.AsExpression:
                return Opaque.Of(span, "as", Expression(binary.Left));
        }

        return BinaryOperatorOf(binary.Kind()) is { } op
            ? new Binary(span, op, Expression(binary.Left), Expression(binary.Right))
            : Opaque.Of(span, binary.Kind().ToString(), Expression(binary.Left), Expression(binary.Right));
    }

    /// <summary>
    /// <c>x is null</c>, <c>x is not null</c>, <c>x is &gt; 0 and &lt; 10</c> become the comparisons they mean; a type pattern
    /// only says, when it matches, that x is not null.
    /// </summary>
    private Expr Pattern(Expr subject, PatternSyntax pattern, SourceSpan span)
    {
        var nothing = new Literal(span, LiteralKind.Null, null);

        return pattern switch
        {
            ParenthesizedPatternSyntax inner => Pattern(subject, inner.Pattern, span),
            ConstantPatternSyntax constant => new Binary(span, BinaryOperator.Equal, subject, Expression(constant.Expression)),
            RelationalPatternSyntax relation => new Binary(span, RelationOf(relation.OperatorToken.Kind()), subject, Expression(relation.Expression)),
            UnaryPatternSyntax { Pattern: ConstantPatternSyntax constant } => new Binary(span, BinaryOperator.NotEqual, subject, Expression(constant.Expression)),
            UnaryPatternSyntax { Pattern: RelationalPatternSyntax relation } =>
                new Binary(span, Opposite(RelationOf(relation.OperatorToken.Kind())), subject, Expression(relation.Expression)),
            UnaryPatternSyntax negated => new Unary(span, UnaryOperator.Not, Pattern(subject, negated.Pattern, Span(negated.Pattern))),
            BinaryPatternSyntax both => new Binary(span, both.IsKind(SyntaxKind.AndPattern) ? BinaryOperator.And : BinaryOperator.Or,
                Pattern(subject, both.Left, Span(both.Left)), Pattern(subject, both.Right, Span(both.Right))),
            RecursivePatternSyntax { Type: null, PositionalPatternClause: null, PropertyPatternClause.Subpatterns.Count: 0 } =>
                new Binary(span, BinaryOperator.NotEqual, subject, nothing),
            VarPatternSyntax or DiscardPatternSyntax => new Literal(span, LiteralKind.Boolean, true),
            _ => Opaque.Of(span, "is pattern", subject),
        };
    }

    private static BinaryOperator RelationOf(SyntaxKind token) => token switch
    {
        SyntaxKind.LessThanToken => BinaryOperator.Less,
        SyntaxKind.LessThanEqualsToken => BinaryOperator.LessOrEqual,
        SyntaxKind.GreaterThanToken => BinaryOperator.Greater,
        _ => BinaryOperator.GreaterOrEqual,
    };

    private static BinaryOperator Opposite(BinaryOperator op) => op switch
    {
        BinaryOperator.Less => BinaryOperator.GreaterOrEqual,
        BinaryOperator.LessOrEqual => BinaryOperator.Greater,
        BinaryOperator.Greater => BinaryOperator.LessOrEqual,
        _ => BinaryOperator.Less,
    };

    private Expr Designated(VariableDesignationSyntax designation, SourceSpan span) => designation switch
    {
        SingleVariableDesignationSyntax single => new Name(Span(single), single.Identifier.ValueText),
        ParenthesizedVariableDesignationSyntax several =>
            new CollectionLiteral(span, CollectionKind.Tuple, several.Variables.Select(v => Designated(v, Span(v))).ToList()),
        _ => Opaque.Of(span, "discard"),
    };

    private static BinaryOperator? CompoundOperator(SyntaxKind kind) => kind switch
    {
        SyntaxKind.AddAssignmentExpression => BinaryOperator.Add,
        SyntaxKind.SubtractAssignmentExpression => BinaryOperator.Subtract,
        SyntaxKind.MultiplyAssignmentExpression => BinaryOperator.Multiply,
        SyntaxKind.DivideAssignmentExpression => BinaryOperator.Divide,
        SyntaxKind.ModuloAssignmentExpression => BinaryOperator.Modulo,
        SyntaxKind.AndAssignmentExpression => BinaryOperator.BitAnd,
        SyntaxKind.OrAssignmentExpression => BinaryOperator.BitOr,
        SyntaxKind.ExclusiveOrAssignmentExpression => BinaryOperator.BitXor,
        SyntaxKind.LeftShiftAssignmentExpression => BinaryOperator.ShiftLeft,
        SyntaxKind.RightShiftAssignmentExpression or SyntaxKind.UnsignedRightShiftAssignmentExpression => BinaryOperator.ShiftRight,
        _ => null,
    };

    private static BinaryOperator? BinaryOperatorOf(SyntaxKind kind) => kind switch
    {
        SyntaxKind.AddExpression => BinaryOperator.Add,
        SyntaxKind.SubtractExpression => BinaryOperator.Subtract,
        SyntaxKind.MultiplyExpression => BinaryOperator.Multiply,
        SyntaxKind.DivideExpression => BinaryOperator.Divide,
        SyntaxKind.ModuloExpression => BinaryOperator.Modulo,
        SyntaxKind.LessThanExpression => BinaryOperator.Less,
        SyntaxKind.LessThanOrEqualExpression => BinaryOperator.LessOrEqual,
        SyntaxKind.GreaterThanExpression => BinaryOperator.Greater,
        SyntaxKind.GreaterThanOrEqualExpression => BinaryOperator.GreaterOrEqual,
        SyntaxKind.EqualsExpression => BinaryOperator.Equal,
        SyntaxKind.NotEqualsExpression => BinaryOperator.NotEqual,
        SyntaxKind.LogicalAndExpression => BinaryOperator.And,
        SyntaxKind.LogicalOrExpression => BinaryOperator.Or,
        SyntaxKind.BitwiseAndExpression => BinaryOperator.BitAnd,
        SyntaxKind.BitwiseOrExpression => BinaryOperator.BitOr,
        SyntaxKind.ExclusiveOrExpression => BinaryOperator.BitXor,
        SyntaxKind.LeftShiftExpression => BinaryOperator.ShiftLeft,
        SyntaxKind.RightShiftExpression or SyntaxKind.UnsignedRightShiftExpression => BinaryOperator.ShiftRight,
        _ => null,
    };

    private static IrType TypeOf(TypeSyntax? type) => type switch
    {
        null => IrType.Unknown,
        PredefinedTypeSyntax { Keyword.ValueText: "void" } => IrType.Nothing,
        PredefinedTypeSyntax predefined => IrType.Named(predefined.Keyword.ValueText),
        IdentifierNameSyntax { IsVar: true } => IrType.Unknown,
        IdentifierNameSyntax identifier => IrType.Named(identifier.Identifier.ValueText),
        GenericNameSyntax generic => IrType.Named(generic.Identifier.ValueText, generic.TypeArgumentList.Arguments.Select(TypeOf).ToArray()),
        QualifiedNameSyntax qualified => TypeOf(qualified.Right),
        AliasQualifiedNameSyntax alias => TypeOf(alias.Name),
        NullableTypeSyntax nullable => TypeOf(nullable.ElementType) is var inner && ValueTypes.Contains(inner.Name) ? inner with { Nullable = true } : inner,
        ArrayTypeSyntax array => IrType.Named("array", TypeOf(array.ElementType)),
        TupleTypeSyntax => IrType.Named("tuple"),
        RefTypeSyntax reference => TypeOf(reference.Type),
        ScopedTypeSyntax scoped => TypeOf(scoped.Type),
        _ => IrType.Unknown,
    };
}
