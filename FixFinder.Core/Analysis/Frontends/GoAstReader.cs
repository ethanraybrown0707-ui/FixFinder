using System.Globalization;
using System.Text;
using System.Text.Json;
using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>
/// Turns one Go file's go/ast tree into the IR, writing Go's rules in the IR's terms: a panic is a throw, a deferred call
/// runs in a finally around the rest of the function, append joins two lists, len(x) is x's length, and a method's
/// receiver is <c>this</c>. A nil slice is an empty list and a nil map is not known, never null - len, range and reading
/// them are all fine in Go - while a nil pointer, interface or function is null. A variable declared again in an inner
/// block gets a name of its own, so the outer one keeps its value.
/// </summary>
internal sealed class GoAstReader(string file, GoTypes types)
{
    private static readonly HashSet<string> Builtins = new(StringComparer.Ordinal)
    {
        "len", "cap", "append", "make", "new", "panic", "delete", "copy", "min", "max", "clear", "print", "println",
        "complex", "real", "imag", "recover", "close",
    };

    private static readonly HashSet<string> BasicTypes = new(StringComparer.Ordinal)
    {
        "int", "int8", "int16", "int32", "int64", "uint", "uint8", "uint16", "uint32", "uint64", "uintptr", "byte", "rune",
        "float32", "float64", "string", "bool", "complex64", "complex128",
    };

    private readonly List<IrFunction> _functions = [];
    private readonly List<IrFunction> _methods = [];
    private readonly List<Dictionary<string, Binding>> _scopes = [];
    private readonly List<string?> _breaks = [];
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);
    private Context _context = new() { Name = IrFunction.ModuleBody };
    private int _labels;
    private readonly HashSet<string> _named = new(StringComparer.Ordinal);

    /// <summary>What a Go name stands for in the function being read: its IR name, what sort of value it holds and its type.</summary>
    private sealed record Binding(string Ir, GoKind Kind, IrType Type);

    private sealed class Context
    {
        public required string Name { get; init; }
        public string? Receiver { get; init; }
        public List<Stmt> Hoisted { get; } = [];
        public HashSet<string> AddressTaken { get; } = new(StringComparer.Ordinal);
        public List<GoKind> ResultKinds { get; init; } = [];
        public List<string> ResultNames { get; init; } = [];
        public bool HasGoto { get; init; }
    }

    public (List<IrFunction> Functions, List<IrFunction> Methods) ReadFile(JsonElement tree)
    {
        foreach (var declaration in Json.List(tree, "Decls"))
        {
            if (Json.Kind(declaration) != "FuncDecl" || Json.Prop(declaration, "Body") is null) continue;

            var name = Json.Text(Json.Prop(declaration, "Name"), "Name") ?? "?";
            var receiver = Json.List(Json.Prop(declaration, "Recv"), "List").Select(r => (JsonElement?)r).FirstOrDefault();
            var owner = receiver is { } r ? OwnerName(Json.Prop(r, "Type")) : null;
            var receiverName = receiver is { } named ? Json.List(named, "Names").Select(n => Json.Text(n, "Name")).FirstOrDefault() : null;

            var function = ReadFunction(declaration, name, owner, receiverName, Json.Prop(declaration, "Type"), Json.Prop(declaration, "Body")!.Value,
                enclosedBy: null, nested: false);
            (owner is null ? _functions : _methods).Add(function);
        }

        return (_functions, _methods);
    }

    private static string? OwnerName(JsonElement? type) => Json.Kind(type ?? default) switch
    {
        "StarExpr" or "ParenExpr" or "IndexExpr" or "IndexListExpr" => OwnerName(Json.Prop(type, "X")),
        "Ident" => Json.Text(type, "Name"),
        _ => null,
    };

    private SourceSpan Span(JsonElement element) => Json.Span(file, element);

    private IrFunction ReadFunction(JsonElement node, string name, string? owner, string? receiver, JsonElement? signature, JsonElement body,
        string? enclosedBy, bool nested)
    {
        var outer = _context;
        var outerBreaks = _breaks.ToList();
        _breaks.Clear();

        var results = Json.List(Json.Prop(signature, "Results"), "List").ToList();
        var resultKinds = results.SelectMany(r => Enumerable.Repeat(types.KindOf(Json.Prop(r, "Type")), Math.Max(1, Json.List(r, "Names").Count()))).ToList();

        _context = new Context
        {
            Name = owner is null ? name : $"{owner}.{name}",
            Receiver = nested ? outer.Receiver : receiver is null or "_" ? null : receiver,
            ResultKinds = resultKinds,
            HasGoto = ContainsGoto(body),
        };

        _scopes.Add(new Dictionary<string, Binding>(StringComparer.Ordinal));

        var parameters = new List<IrParameter>();
        var index = 0;
        foreach (var field in Json.List(Json.Prop(signature, "Params"), "List"))
        {
            var type = Json.Prop(field, "Type");
            var names = Json.List(field, "Names").ToList();
            if (names.Count == 0)
            {
                parameters.Add(new IrParameter(Span(field), $"_{index++}", types.TypeOf(type)));
                continue;
            }

            foreach (var parameterName in names)
            {
                var ir = Declare(Json.Text(parameterName, "Name") ?? "_", types.KindOf(type), types.TypeOf(type));
                parameters.Add(new IrParameter(Span(parameterName), ir, types.TypeOf(type)));
                index++;
            }
        }

        var statements = new List<Stmt>();
        foreach (var result in results)
        {
            foreach (var resultName in Json.List(result, "Names"))
            {
                var type = Json.Prop(result, "Type");
                var ir = Declare(Json.Text(resultName, "Name") ?? "_", types.KindOf(type), types.TypeOf(type));
                _context.ResultNames.Add(ir);
                statements.Add(new Declare(Span(resultName), ir, types.TypeOf(type), Zero(type, Span(resultName))));
            }
        }

        statements.AddRange(Statements(Json.List(body, "List"), functionLevel: true));

        if (_context.Hoisted.Count > 0)
        {
            var hoisted = _context.Hoisted.AsEnumerable().Reverse().ToList();
            statements = [new Try(Span(body), statements, [], [], hoisted)];
        }

        _scopes.RemoveAt(_scopes.Count - 1);

        var returnType = resultKinds.Count switch
        {
            0 => IrType.Named("void"),
            1 => types.TypeOf(Json.Prop(results[0], "Type")),
            _ => IrType.Named("tuple"),
        };

        var function = new IrFunction(Span(node), name, owner, parameters, returnType, statements)
        {
            EnclosedBy = enclosedBy,
            IsStatic = false,
            AddressTaken = [.. _context.AddressTaken],
        };

        _context = outer;
        _breaks.Clear();
        _breaks.AddRange(outerBreaks);

        return nested ? function with { OuterNames = Captured(function) } : function;
    }

    /// <summary>The variables a function literal uses from the code around it - which it can also change.</summary>
    private static List<string> Captured(IrFunction function)
    {
        var own = IrWalk.LocalNames(function, assigningDeclares: false);
        return IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Names)
            .Where(n => n != "this" && !own.Contains(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static bool ContainsGoto(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (Json.Kind(element) == "FuncLit") return false;
                if (Json.Kind(element) == "BranchStmt" && Json.Text(element, "Tok") == "goto") return true;
                return element.EnumerateObject().Any(p => ContainsGoto(p.Value));
            case JsonValueKind.Array:
                return element.EnumerateArray().Any(ContainsGoto);
            default:
                return false;
        }
    }

    // ---- Names and scopes ----

    private Binding? Lookup(string name)
    {
        for (var i = _scopes.Count - 1; i >= 0; i--)
            if (_scopes[i].TryGetValue(name, out var binding)) return binding;
        return null;
    }

    /// <summary>Declares a name in the innermost block, with a name of its own when it hides one declared further out.</summary>
    private string Declare(string name, GoKind kind, IrType type)
    {
        if (name == "_") return "_";
        if (_scopes[^1].TryGetValue(name, out var already)) return already.Ir;

        var ir = name;
        while (!_taken.Add(ir)) ir += "'";

        _scopes[^1][name] = new Binding(ir, kind, type);
        return ir;
    }

    private T InScope<T>(Func<T> read)
    {
        _scopes.Add(new Dictionary<string, Binding>(StringComparer.Ordinal));
        try
        {
            return read();
        }
        finally
        {
            _scopes.RemoveAt(_scopes.Count - 1);
        }
    }

    private bool Shadowed(string name) => Lookup(name) is not null;

    private IReadOnlyList<string> VisibleNames() => _scopes.SelectMany(s => s.Values.Select(b => b.Ir)).Where(n => n != "_").Distinct().ToList();

    // ---- Statements ----

    private List<Stmt> Statements(IEnumerable<JsonElement> list, bool functionLevel)
    {
        var items = list.ToList();
        var statements = new List<Stmt>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (Json.Kind(item) == "DeferStmt")
            {
                var span = Span(item);
                var deferred = DeferredCall(item);

                if (functionLevel)
                {
                    var rest = Statements(items.Skip(i + 1), functionLevel: true);
                    statements.Add(new Try(span, rest, [], [], deferred));
                    return statements;
                }

                // A deferred call inside a block still runs when the function ends: it is kept for a finally around the whole function.
                _context.Hoisted.AddRange(deferred);
                statements.Add(new Evaluate(span, Opaque.Of(span, "deferred call")));
                continue;
            }

            statements.AddRange(Statement(item));
        }

        return statements;
    }

    private List<Stmt> DeferredCall(JsonElement defer)
    {
        if (Json.Prop(defer, "Call") is not { } call) return [];

        var span = Span(defer);
        var statements = ExpressionStatement(call, span).ToList();

        // A deferred call that takes a lock takes it as the function ends, for whoever called it - it is not held here.
        return Taken(call)
            ? [new Evaluate(span, Opaque.Of(span, "deferred lock", statements.OfType<Evaluate>().Select(e => e.Value).ToArray()))]
            : statements;
    }

    private static bool Taken(JsonElement call) =>
        Json.Prop(call, "Fun") is { } fun && Json.Kind(fun) == "SelectorExpr" &&
        Json.Text(Json.Prop(fun, "Sel"), "Name") is "Lock" or "RLock";

    private IEnumerable<Stmt> Statement(JsonElement statement)
    {
        var span = Span(statement);

        switch (Json.Kind(statement))
        {
            case "ExprStmt":
                return ExpressionStatement(Json.Prop(statement, "X")!.Value, span);

            case "AssignStmt":
                return AssignStatement(statement, span);

            case "IncDecStmt":
                return [new Assign(span, Expression(Json.Prop(statement, "X")!.Value), new Literal(span, LiteralKind.Integer, 1L),
                    Json.Text(statement, "Tok") == "++" ? BinaryOperator.Add : BinaryOperator.Subtract)];

            case "DeclStmt":
                return Json.Prop(statement, "Decl") is { } declaration ? Declaration(declaration) : [];

            case "ReturnStmt":
                return [ReturnStatement(statement, span)];

            case "IfStmt":
                return InScope(() => IfStatement(statement, span));

            case "ForStmt":
                return InScope(() => ForStatement(statement, span));

            case "RangeStmt":
                return InScope(() => RangeStatement(statement, span));

            case "SwitchStmt":
                return InScope(() => SwitchStatement(statement, span));

            case "TypeSwitchStmt":
                return InScope(() => TypeSwitchStatement(statement, span));

            case "SelectStmt":
                return InScope(() => SelectStatement(statement, span));

            case "BlockStmt":
                return InScope(() => Statements(Json.List(statement, "List"), functionLevel: false));

            case "BranchStmt":
                return BranchStatement(statement, span);

            case "LabeledStmt":
                var label = Json.Text(Json.Prop(statement, "Label"), "Name") ?? "label";
                var inner = Json.Prop(statement, "Stmt") is { } labelled ? Statement(labelled).ToList() : [];
                List<Stmt> target = _context.HasGoto ? [new OpaqueStmt(span, "goto target", VisibleNames(), [])] : [];
                return [.. target, new Labeled(span, label, inner)];

            case "GoStmt":
                return [new Evaluate(span, Opaque.Of(span, "go", Expression(Json.Prop(statement, "Call")!.Value)))];

            case "DeferStmt":
                return DeferredCall(statement);

            case "SendStmt":
                return [new Evaluate(span, Opaque.Of(span, "send", Expression(Json.Prop(statement, "Chan")!.Value), Expression(Json.Prop(statement, "Value")!.Value)))];

            case "EmptyStmt":
                return [];

            default:
                return [new OpaqueStmt(span, Json.Kind(statement) ?? "statement", [], [])];
        }
    }

    private IEnumerable<Stmt> ExpressionStatement(JsonElement expression, SourceSpan span)
    {
        if (Json.Kind(expression) == "CallExpr" && Json.Prop(expression, "Fun") is { } fun)
        {
            var arguments = Json.List(expression, "Args").ToList();

            if (Json.Kind(fun) == "Ident" && Json.Text(fun, "Name") == "panic" && !Shadowed("panic"))
                return [new Throw(span, new NewObject(Span(expression), IrType.Named("panic"), arguments.Select(a => new Argument(null, Expression(a))).ToList()))];

            if (Selector(fun) is ("os", "Exit") or ("log", "Fatal" or "Fatalf" or "Fatalln" or "Panic" or "Panicf" or "Panicln"))
                return [new Throw(span, new NewObject(Span(expression), IrType.Named(Json.Text(Json.Prop(fun, "Sel"), "Name") ?? "exit"),
                    arguments.Select(a => new Argument(null, Expression(a))).ToList()))];

            if (Scanned(expression) is { } scanned) return scanned;
        }

        return [new Evaluate(span, Expression(expression))];
    }

    private static (string Package, string Name)? Selector(JsonElement fun) =>
        Json.Kind(fun) == "SelectorExpr" && Json.Prop(fun, "X") is { } x && Json.Kind(x) == "Ident" && Json.Text(x, "Name") is { } package &&
        Json.Text(Json.Prop(fun, "Sel"), "Name") is { } name
            ? (package, name)
            : null;

    /// <summary>fmt.Scan(&amp;n) and its kin: the call, then what was typed stored in each variable it was given.</summary>
    private List<Stmt>? Scanned(JsonElement call)
    {
        if (Selector(Json.Prop(call, "Fun")!.Value) is not ("fmt", "Scan" or "Scanln" or "Scanf")) return null;

        var span = Span(call);
        var targets = new List<(Expr Target, string What)>();
        var arguments = new List<Argument>();

        foreach (var argument in Json.List(call, "Args"))
        {
            if (Json.Kind(argument) == "UnaryExpr" && Json.Text(argument, "Op") == "&" && Json.Prop(argument, "X") is { } x && Json.Kind(x) == "Ident" &&
                Lookup(Json.Text(x, "Name") ?? "") is { } binding)
            {
                var what = binding.Kind switch
                {
                    GoKind.Whole => "typed whole number",
                    GoKind.Real => "typed number",
                    GoKind.Text => "typed text",
                    _ => "typed value",
                };
                var name = new Name(Span(x), binding.Ir);
                targets.Add((name, what));
                arguments.Add(new Argument(null, Opaque.Of(Span(argument), "address of", name)));
                continue;
            }

            arguments.Add(new Argument(null, Expression(argument)));
        }

        return [new Evaluate(span, new Call(span, Expression(Json.Prop(call, "Fun")!.Value), arguments)),
            .. targets.Select(t => (Stmt)new Assign(span, t.Target, Opaque.Of(span, t.What)))];
    }

    private IEnumerable<Stmt> AssignStatement(JsonElement statement, SourceSpan span)
    {
        var left = Json.List(statement, "Lhs").ToList();
        var right = Json.List(statement, "Rhs").ToList();
        var token = Json.Text(statement, "Tok") ?? "=";

        if (token is not ("=" or ":="))
        {
            var op = token switch
            {
                "+=" => BinaryOperator.Add,
                "-=" => BinaryOperator.Subtract,
                "*=" => BinaryOperator.Multiply,
                "/=" => BinaryOperator.Divide,
                "%=" => BinaryOperator.Modulo,
                "&=" => BinaryOperator.BitAnd,
                "|=" => BinaryOperator.BitOr,
                "^=" => BinaryOperator.BitXor,
                "<<=" => BinaryOperator.ShiftLeft,
                ">>=" => BinaryOperator.ShiftRight,
                _ => (BinaryOperator?)null,
            };

            var target = Expression(left[0]);
            var value = Expression(right[0]);
            return op is { } known ? [new Assign(span, target, value, known)] : [new Assign(span, target, Opaque.Of(span, token, target, value))];
        }

        var statements = new List<Stmt>();
        List<Stmt>? scanned = right.Count == 1 && Json.Kind(right[0]) == "CallExpr" ? Scanned(right[0]) : null;

        // The values are read before any new name is declared: x := x + 1 in an inner block reads the outer x.
        var values = right.Select((r, i) => right.Count == left.Count ? Expression(r, KindOf(left[i])) : Expression(r)).ToList();
        if (scanned is not null) values = [((Evaluate)scanned[0]).Value];

        var targets = new List<Expr>();
        for (var i = 0; i < left.Count; i++)
        {
            var target = left[i];
            if (token == ":=" && Json.Kind(target) == "Ident" && Json.Text(target, "Name") is { } name && name != "_" &&
                !_scopes[^1].ContainsKey(name))
            {
                var kind = right.Count == left.Count ? KindOf(right[i]) : GoKind.Other;
                var ir = Declare(name, kind, IrType.Unknown);

                if (right.Count == left.Count && left.Count == 1)
                {
                    statements.Add(new Declare(span, ir, IrType.Unknown, values[0]));
                    statements.AddRange(scanned?.Skip(1) ?? []);
                    return statements;
                }

                statements.Add(new Declare(Span(target), ir, IrType.Unknown, null));
                targets.Add(new Name(Span(target), ir));
                continue;
            }

            targets.Add(Expression(target));
        }

        if (targets.Count == 1 && values.Count == 1)
            statements.Add(new Assign(span, targets[0], values[0]));
        else
            statements.Add(new Assign(span, new CollectionLiteral(span, CollectionKind.Tuple, targets),
                values.Count == 1 ? values[0] : new CollectionLiteral(span, CollectionKind.Tuple, values)));

        statements.AddRange(scanned?.Skip(1) ?? []);
        return statements;
    }

    private IEnumerable<Stmt> Declaration(JsonElement declaration)
    {
        var token = Json.Text(declaration, "Tok");
        if (token is not ("var" or "const")) return [];

        var statements = new List<Stmt>();
        foreach (var spec in Json.List(declaration, "Specs"))
        {
            var type = Json.Prop(spec, "Type");
            var names = Json.List(spec, "Names").ToList();
            var valueNodes = Json.List(spec, "Values").ToList();
            var declaredKind = types.KindOf(type);
            var irType = types.TypeOf(type);

            if (valueNodes.Count == 0)
            {
                foreach (var name in names)
                {
                    var span = Span(name);
                    var initial = token == "const" ? Opaque.Of(span, "constant") : Zero(type, span);
                    statements.Add(new Declare(span, Declare(Json.Text(name, "Name") ?? "_", declaredKind, irType), irType, initial));
                }

                continue;
            }

            var values = valueNodes.Select(v => Expression(v, type is null ? GoKind.Other : declaredKind)).ToList();

            if (values.Count == names.Count)
            {
                for (var i = 0; i < names.Count; i++)
                {
                    var kind = type is null ? KindOf(valueNodes[i]) : declaredKind;
                    statements.Add(new Declare(Span(names[i]), Declare(Json.Text(names[i], "Name") ?? "_", kind, irType), irType, values[i]));
                }

                continue;
            }

            var targets = names.Select(n =>
            {
                var ir = Declare(Json.Text(n, "Name") ?? "_", declaredKind, irType);
                statements.Add(new Declare(Span(n), ir, irType, null));
                return (Expr)new Name(Span(n), ir);
            }).ToList();

            statements.Add(new Assign(Span(spec), new CollectionLiteral(Span(spec), CollectionKind.Tuple, targets), values[0]));
        }

        return statements;
    }

    private Return ReturnStatement(JsonElement statement, SourceSpan span)
    {
        var results = Json.List(statement, "Results").ToList();

        if (results.Count == 0)
        {
            return _context.ResultNames.Count switch
            {
                0 => new Return(span, null),
                1 => new Return(span, new Name(span, _context.ResultNames[0])),
                _ => new Return(span, new CollectionLiteral(span, CollectionKind.Tuple, _context.ResultNames.Select(n => (Expr)new Name(span, n)).ToList())),
            };
        }

        var values = results.Select((r, i) => Expression(r, i < _context.ResultKinds.Count ? _context.ResultKinds[i] : GoKind.Other)).ToList();
        return new Return(span, values.Count == 1 ? values[0] : new CollectionLiteral(span, CollectionKind.Tuple, values));
    }

    private List<Stmt> IfStatement(JsonElement statement, SourceSpan span)
    {
        var statements = Json.Prop(statement, "Init") is { } init ? Statement(init).ToList() : [];
        var condition = Expression(Json.Prop(statement, "Cond")!.Value);
        var then = Block(Json.Prop(statement, "Body"));

        List<Stmt> otherwise = Json.Prop(statement, "Else") switch
        {
            { } elseIf when Json.Kind(elseIf) == "IfStmt" => InScope(() => IfStatement(elseIf, Span(elseIf))),
            { } block => Block(block),
            null => [],
        };

        statements.Add(new If(span, condition, then, otherwise));
        return statements;
    }

    private List<Stmt> Block(JsonElement? block) => block is { } b ? InScope(() => Statements(Json.List(b, "List"), functionLevel: false)) : [];

    private List<Stmt> Loop(JsonElement? body)
    {
        _breaks.Add(null);
        try
        {
            return Block(body);
        }
        finally
        {
            _breaks.RemoveAt(_breaks.Count - 1);
        }
    }

    private List<Stmt> ForStatement(JsonElement statement, SourceSpan span)
    {
        var setup = Json.Prop(statement, "Init") is { } init ? Statement(init).ToList() : [];
        var condition = Json.Prop(statement, "Cond") is { } test ? Expression(test) : null;
        var step = Json.Prop(statement, "Post") is { } post ? Statement(post).ToList() : [];
        var body = Loop(Json.Prop(statement, "Body"));
        return [new For(span, setup, condition, step, body)];
    }

    /// <summary>
    /// for k, v := range x walks x's items into v; k is a position (or a map's key) FixFinder does not follow. A map's
    /// values are not its items in the IR's sense, so walking one gives values nothing is known about.
    /// </summary>
    private List<Stmt> RangeStatement(JsonElement statement, SourceSpan span)
    {
        var over = Json.Prop(statement, "X")!.Value;
        var items = Expression(over);
        var declaring = Json.Text(statement, "Tok") == ":=";

        Expr? Target(JsonElement? node)
        {
            if (node is not { } n) return null;
            if (Json.Kind(n) == "Ident" && Json.Text(n, "Name") == "_") return null;
            if (declaring && Json.Kind(n) == "Ident") return new Name(Span(n), Declare(Json.Text(n, "Name") ?? "_", GoKind.Other, IrType.Unknown));
            return Expression(n);
        }

        var key = Target(Json.Prop(statement, "Key"));
        var value = Target(Json.Prop(statement, "Value"));
        var walked = KindOf(over) == GoKind.Map ? Opaque.Of(Span(over), "the values of a map", items) : items;

        List<Stmt> body = key is null ? [] : [new Assign(span, key, Opaque.Of(span, "position"))];
        body.AddRange(Loop(Json.Prop(statement, "Body")));

        return [new ForEach(span, value ?? new Name(span, "_"), value is null ? items : walked, body, [])];
    }

    private List<Stmt> SwitchStatement(JsonElement statement, SourceSpan span)
    {
        var statements = Json.Prop(statement, "Init") is { } init ? Statement(init).ToList() : [];
        var clauses = Json.List(Json.Prop(statement, "Body"), "List").ToList();

        if (Json.Prop(statement, "Tag") is { } tag)
        {
            var subject = Expression(tag);
            _breaks.Add(null);
            try
            {
                var cases = clauses.Select(clause =>
                {
                    var labels = Json.List(clause, "List").Select(l => Expression(l)).ToList();
                    var (body, fallsThrough) = CaseBody(clause, "Body");
                    return new SwitchCase(labels, body, fallsThrough);
                }).ToList();

                statements.Add(new Switch(span, subject, cases));
            }
            finally
            {
                _breaks.RemoveAt(_breaks.Count - 1);
            }

            return statements;
        }

        statements.AddRange(Chain(span, clauses.Select(clause =>
        {
            var tests = Json.List(clause, "List").Select(l => Expression(l)).ToList();
            Expr? condition = tests.Count == 0 ? null : tests.Aggregate((a, b) => new Binary(a.Span, BinaryOperator.Or, a, b));
            return (Condition: condition, Read: (Func<List<Stmt>>)(() => CaseBody(clause, "Body").Body));
        }).ToList()));

        return statements;
    }

    private (List<Stmt> Body, bool FallsThrough) CaseBody(JsonElement clause, string property)
    {
        var items = Json.List(clause, property).ToList();
        var fallsThrough = items.Count > 0 && Json.Kind(items[^1]) == "BranchStmt" && Json.Text(items[^1], "Tok") == "fallthrough";
        if (fallsThrough) items.RemoveAt(items.Count - 1);
        return (InScope(() => Statements(items, functionLevel: false)), fallsThrough);
    }

    /// <summary>
    /// A switch with no subject, a type switch or a select, as a chain of ifs taken in order with the default last. A
    /// break inside one leaves it, so the chain is labelled and those breaks name it.
    /// </summary>
    private List<Stmt> Chain(SourceSpan span, IReadOnlyList<(Expr? Condition, Func<List<Stmt>> Read)> clauses)
    {
        var label = $"switch at line {span.Line}#{_labels++}";
        _breaks.Add(label);
        List<(Expr? Condition, List<Stmt> Body)> read;
        try
        {
            read = clauses.Select(c => (c.Condition, c.Read())).ToList();
        }
        finally
        {
            _breaks.RemoveAt(_breaks.Count - 1);
        }

        List<Stmt> chain = read.FirstOrDefault(c => c.Condition is null).Body ?? [];
        foreach (var (condition, body) in read.Where(c => c.Condition is not null).Reverse())
            chain = [new If(condition!.Span, condition, body, chain)];

        return [new Labeled(span, label, chain)];
    }

    private List<Stmt> TypeSwitchStatement(JsonElement statement, SourceSpan span)
    {
        var statements = Json.Prop(statement, "Init") is { } init ? Statement(init).ToList() : [];

        string? bound = null;
        Expr subject = Opaque.Of(span, "type switch");
        if (Json.Prop(statement, "Assign") is { } assign)
        {
            if (Json.Kind(assign) == "AssignStmt")
            {
                bound = Json.List(assign, "Lhs").Select(l => Json.Text(l, "Name")).FirstOrDefault();
                if (Json.List(assign, "Rhs").FirstOrDefault() is { ValueKind: JsonValueKind.Object } asserted) subject = Expression(asserted);
            }
            else if (Json.Prop(assign, "X") is { } asserted)
            {
                subject = Expression(asserted);
            }
        }

        statements.Add(new Evaluate(span, subject));

        statements.AddRange(Chain(span, Json.List(Json.Prop(statement, "Body"), "List").Select(clause =>
        {
            var clauseSpan = Span(clause);
            var cases = Json.List(clause, "List").ToList();
            Expr? condition = cases.Count == 0 ? null : Opaque.Of(clauseSpan, "type test");
            return (condition, (Func<List<Stmt>>)(() => InScope(() =>
            {
                List<Stmt> body = bound is null or "_" ? [] : [new Declare(clauseSpan, Declare(bound, GoKind.Other, IrType.Unknown), IrType.Unknown, Opaque.Of(clauseSpan, "type switch value"))];
                body.AddRange(Statements(Json.List(clause, "Body"), functionLevel: false));
                return body;
            })));
        }).ToList()));

        return statements;
    }

    private List<Stmt> SelectStatement(JsonElement statement, SourceSpan span) =>
        Chain(span, Json.List(Json.Prop(statement, "Body"), "List").Select(clause =>
        {
            var clauseSpan = Span(clause);
            var communication = Json.Prop(clause, "Comm");
            Expr? condition = communication is null ? null : Opaque.Of(clauseSpan, "channel ready");
            return (condition, (Func<List<Stmt>>)(() => InScope(() =>
            {
                var body = communication is { } comm ? Statement(comm).ToList() : [];
                body.AddRange(Statements(Json.List(clause, "Body"), functionLevel: false));
                return body;
            })));
        }).ToList());

    private IEnumerable<Stmt> BranchStatement(JsonElement statement, SourceSpan span)
    {
        var label = Json.Text(Json.Prop(statement, "Label"), "Name");

        return Json.Text(statement, "Tok") switch
        {
            "break" => [new Break(span, label ?? (_breaks.Count > 0 ? _breaks[^1] : null))],
            "continue" => [new Continue(span, label)],
            "goto" => [new Return(span, null)],
            _ => [],
        };
    }

    // ---- Expressions ----

    /// <summary>What sort of value an expression gives, as far as can be told from how it is written.</summary>
    private GoKind KindOf(JsonElement expression) => Json.Kind(expression) switch
    {
        "Ident" => Json.Text(expression, "Name") is { } name && Lookup(name) is { } binding ? binding.Kind : GoKind.Other,
        "SelectorExpr" => Json.Text(Json.Prop(expression, "Sel"), "Name") is { } field ? types.FieldKind(field) : GoKind.Other,
        "CompositeLit" => types.KindOf(Json.Prop(expression, "Type")) is var kind && kind == GoKind.Array ? GoKind.Array : kind,
        "ParenExpr" => KindOf(Json.Prop(expression, "X")!.Value),
        "UnaryExpr" when Json.Text(expression, "Op") == "&" => GoKind.Pointer,
        "FuncLit" => GoKind.Function,
        "CallExpr" when Json.Prop(expression, "Fun") is { } fun && Json.Kind(fun) == "Ident" && Json.Text(fun, "Name") is { } called && !Shadowed(called) =>
            called switch
            {
                "make" => types.KindOf(Json.List(expression, "Args").Select(a => (JsonElement?)a).FirstOrDefault()),
                "append" => GoKind.Slice,
                "new" => GoKind.Pointer,
                "len" or "cap" or "copy" => GoKind.Whole,
                _ when BasicTypes.Contains(called) || types.IsType(called) => types.KindOf(fun),
                _ => GoKind.Other,
            },
        "CallExpr" when Json.Prop(expression, "Fun") is { } fun && Json.Kind(fun) is "ArrayType" or "MapType" => types.KindOf(fun),
        "BasicLit" => Json.Text(expression, "Kind") switch
        {
            "INT" or "CHAR" => GoKind.Whole,
            "FLOAT" => GoKind.Real,
            "STRING" => GoKind.Text,
            _ => GoKind.Other,
        },
        _ => GoKind.Other,
    };

    private Expr Expression(JsonElement expression, GoKind context = GoKind.Other)
    {
        var span = Span(expression);

        switch (Json.Kind(expression))
        {
            case "Ident":
                return Identifier(expression, span, context);

            case "BasicLit":
                return BasicLiteral(expression, span);

            case "CompositeLit":
                return Composite(expression, span);

            case "FuncLit":
                var literal = $"func literal at line {span.Line}";
                for (var again = 2; !_named.Add(literal); again++) literal = $"func literal at line {span.Line} ({again})";
                var function = ReadFunction(expression, literal, null, null, Json.Prop(expression, "Type"),
                    Json.Prop(expression, "Body")!.Value, _context.Name, nested: true);
                _functions.Add(function);
                return Opaque.Of(span, "lambda expression");

            case "ParenExpr":
                return Expression(Json.Prop(expression, "X")!.Value, context);

            case "SelectorExpr":
                return new Member(span, Expression(Json.Prop(expression, "X")!.Value), Json.Text(Json.Prop(expression, "Sel"), "Name") ?? "?");

            case "IndexExpr":
                return new ElementAccess(span, Expression(Json.Prop(expression, "X")!.Value), Expression(Json.Prop(expression, "Index")!.Value));

            case "SliceExpr":
                return new Slice(span, Expression(Json.Prop(expression, "X")!.Value),
                    Json.Prop(expression, "Low") is { } low ? Expression(low) : null,
                    Json.Prop(expression, "High") is { } high ? Expression(high) : null, null);

            case "TypeAssertExpr":
                return Opaque.Of(span, "type assertion", Expression(Json.Prop(expression, "X")!.Value));

            case "CallExpr":
                return CallExpression(expression, span);

            case "StarExpr":
                return new Member(span, Expression(Json.Prop(expression, "X")!.Value), "*");

            case "UnaryExpr":
                return UnaryExpression(expression, span);

            case "BinaryExpr":
                return BinaryExpression(expression, span);

            case "KeyValueExpr":
                return Expression(Json.Prop(expression, "Value")!.Value);

            default:
                return Opaque.Of(span, Json.Kind(expression) ?? "expression");
        }
    }

    private Expr Identifier(JsonElement identifier, SourceSpan span, GoKind context)
    {
        var name = Json.Text(identifier, "Name") ?? "_";

        if (Lookup(name) is { } binding) return new Name(span, binding.Ir);
        if (name == _context.Receiver) return new Name(span, "this");

        return name switch
        {
            "true" => new Literal(span, LiteralKind.Boolean, true),
            "false" => new Literal(span, LiteralKind.Boolean, false),
            "nil" when context is GoKind.Slice or GoKind.Map or GoKind.Channel => Opaque.Of(span, "nil"),
            "nil" => new Literal(span, LiteralKind.Null, null),
            "iota" => Opaque.Of(span, "iota"),
            _ => new Name(span, name),
        };
    }

    private static Expr BasicLiteral(JsonElement literal, SourceSpan span)
    {
        var text = Json.Text(literal, "Value") ?? "";

        switch (Json.Text(literal, "Kind"))
        {
            case "INT":
                return WholeNumber(text) is { } whole ? new Literal(span, LiteralKind.Integer, whole) : Opaque.Of(span, "large number");

            case "FLOAT":
                var cleaned = text.Replace("_", "");
                return !cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                       double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)
                    ? new Literal(span, LiteralKind.Real, real)
                    : Opaque.Of(span, "number");

            case "CHAR":
                var character = Unquote(text[1..^1]);
                return character.Length > 0 ? new Literal(span, LiteralKind.Integer, (long)char.ConvertToUtf32(character, 0)) : Opaque.Of(span, "rune");

            case "STRING":
                return new Literal(span, LiteralKind.Text, text.StartsWith('`') ? text[1..^1].Replace("\r", "") : Unquote(text[1..^1]));

            default:
                return Opaque.Of(span, "number");
        }
    }

    private static long? WholeNumber(string text)
    {
        var digits = text.Replace("_", "");
        var (number, radix) = digits.Length > 1 && digits[0] == '0'
            ? char.ToLowerInvariant(digits[1]) switch
            {
                'x' => (digits[2..], 16),
                'b' => (digits[2..], 2),
                'o' => (digits[2..], 8),
                _ => (digits[1..], 8),
            }
            : (digits, 10);

        try
        {
            return number.Length == 0 ? 0 : Convert.ToInt64(number, radix);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The characters an interpreted Go string or rune literal stands for.</summary>
    private static string Unquote(string body)
    {
        var text = new StringBuilder();
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != '\\' || i + 1 >= body.Length)
            {
                text.Append(body[i]);
                continue;
            }

            var escape = body[++i];
            switch (escape)
            {
                case 'a': text.Append('\a'); break;
                case 'b': text.Append('\b'); break;
                case 'f': text.Append('\f'); break;
                case 'n': text.Append('\n'); break;
                case 'r': text.Append('\r'); break;
                case 't': text.Append('\t'); break;
                case 'v': text.Append('\v'); break;
                case 'x' when i + 2 < body.Length:
                    text.Append((char)Convert.ToInt32(body.Substring(i + 1, 2), 16));
                    i += 2;
                    break;
                case 'u' when i + 4 < body.Length:
                    text.Append(char.ConvertFromUtf32(Convert.ToInt32(body.Substring(i + 1, 4), 16)));
                    i += 4;
                    break;
                case 'U' when i + 8 < body.Length:
                    text.Append(char.ConvertFromUtf32(Convert.ToInt32(body.Substring(i + 1, 8), 16)));
                    i += 8;
                    break;
                case >= '0' and <= '7' when i + 2 < body.Length:
                    text.Append((char)Convert.ToInt32(body.Substring(i, 3), 8));
                    i += 2;
                    break;
                default:
                    text.Append(escape);
                    break;
            }
        }

        return text.ToString();
    }

    private Expr Composite(JsonElement composite, SourceSpan span)
    {
        var type = Json.Prop(composite, "Type");
        var elements = Json.List(composite, "Elts").ToList();
        var keyed = elements.Any(e => Json.Kind(e) == "KeyValueExpr");
        var kind = types.KindOf(type);

        switch (kind)
        {
            case GoKind.Slice:
                return keyed ? new NewObject(span, IrType.Named("array"), []) : new CollectionLiteral(span, CollectionKind.List, elements.Select(e => Expression(e)).ToList());

            case GoKind.Array:
                var length = Json.Prop(type, "Len");
                if (length is { } l && Json.Kind(l) == "Ellipsis" && !keyed) return new CollectionLiteral(span, CollectionKind.List, elements.Select(e => Expression(e)).ToList());
                if (length is { } n && Json.Kind(n) == "BasicLit" && WholeNumber(Json.Text(n, "Value") ?? "") is { } size && size == elements.Count && !keyed)
                    return new CollectionLiteral(span, CollectionKind.List, elements.Select(e => Expression(e)).ToList());
                return new NewObject(span, IrType.Named("array"), length is { } given && Json.Kind(given) != "Ellipsis" ? [new Argument(null, Expression(given))] : []);

            case GoKind.Map:
                var pairs = elements.Where(e => Json.Kind(e) == "KeyValueExpr").ToList();
                return new CollectionLiteral(span, CollectionKind.Dictionary,
                    pairs.Select(p => Expression(Json.Prop(p, "Value")!.Value)).ToList(),
                    pairs.Select(p => Expression(Json.Prop(p, "Key")!.Value)).ToList());

            default:
                return new NewObject(span, types.TypeOf(type), elements.Select(e => Json.Kind(e) == "KeyValueExpr"
                    ? new Argument(Json.Text(Json.Prop(e, "Key"), "Name"), Expression(Json.Prop(e, "Value")!.Value))
                    : new Argument(null, Expression(e))).ToList());
        }
    }

    private Expr CallExpression(JsonElement call, SourceSpan span)
    {
        var fun = Json.Prop(call, "Fun")!.Value;
        var arguments = Json.List(call, "Args").ToList();
        var spread = Json.Flag(call, "Ellipsis");

        if (Json.Kind(fun) == "Ident" && Json.Text(fun, "Name") is { } name && !Shadowed(name))
        {
            if (Builtins.Contains(name) && Builtin(name, arguments, spread, span) is { } builtin) return builtin;
            if ((BasicTypes.Contains(name) || types.IsType(name)) && arguments.Count == 1) return new Cast(span, types.TypeOf(fun), Expression(arguments[0]));
        }

        if (Json.Kind(fun) is "ArrayType" or "MapType" or "ChanType" or "FuncType" or "InterfaceType" && arguments.Count == 1)
            return new Cast(span, types.TypeOf(fun), Expression(arguments[0]));

        if (Json.Kind(fun) == "ParenExpr" && Json.Prop(fun, "X") is { } inner && Json.Kind(inner) is "StarExpr" or "ArrayType" or "MapType" or "FuncType" or "ChanType" &&
            arguments.Count == 1)
            return new Cast(span, types.TypeOf(inner), Expression(arguments[0]));

        return new Call(span, Expression(fun), arguments.Select(a => new Argument(null, Expression(a))).ToList());
    }

    private Expr? Builtin(string name, IReadOnlyList<JsonElement> arguments, bool spread, SourceSpan span)
    {
        var values = new Lazy<List<Expr>>(() => arguments.Select(a => Expression(a)).ToList());

        switch (name)
        {
            case "len" when arguments.Count == 1:
                return new Member(span, values.Value[0], "Length");

            case "append" when arguments.Count >= 1:
                var start = Expression(arguments[0]);
                if (arguments.Count == 1) return start;
                var added = spread && arguments.Count == 2
                    ? Expression(arguments[1])
                    : new CollectionLiteral(span, CollectionKind.List, arguments.Skip(1).Select(a => Expression(a)).ToList());
                return new Binary(span, BinaryOperator.Add, start, added);

            case "make" when arguments.Count >= 1:
                return types.KindOf(arguments[0]) switch
                {
                    GoKind.Slice => new NewObject(span, IrType.Named("array"), arguments.Count > 1 ? [new Argument(null, Expression(arguments[1]))] : []),
                    GoKind.Map => new CollectionLiteral(span, CollectionKind.Dictionary, [], []),
                    _ => Opaque.Of(span, "made " + (Json.Kind(arguments[0]) ?? "value")),
                };

            case "new" when arguments.Count == 1:
                return new NewObject(span, types.TypeOf(arguments[0]), []);

            case "delete" when arguments.Count == 2:
                return new Call(span, new Member(span, Expression(arguments[0]), "Remove"), [new Argument(null, Expression(arguments[1]))]);

            case "clear" when arguments.Count == 1:
                return new Call(span, new Member(span, Expression(arguments[0]), "clear"), []);

            case "min" or "max" when arguments.Count >= 2:
                return new Call(span, new Member(span, new Name(span, "Math"), name == "min" ? "Min" : "Max"), values.Value.Select(v => new Argument(null, v)).ToList());

            case "print" or "println":
                return new Call(span, new Name(span, name), values.Value.Select(v => new Argument(null, v)).ToList());

            default:
                return new Opaque(span, name, values.Value);
        }
    }

    private Expr UnaryExpression(JsonElement unary, SourceSpan span)
    {
        var operandNode = Json.Prop(unary, "X")!.Value;

        switch (Json.Text(unary, "Op"))
        {
            case "&":
                if (Json.Kind(operandNode) == "CompositeLit") return Expression(operandNode);
                var target = Expression(operandNode);
                if (Root(target) is { } root) _context.AddressTaken.Add(root);
                return Opaque.Of(span, "address of", target);

            case "-":
                return new Unary(span, UnaryOperator.Negate, Expression(operandNode));
            case "+":
                return new Unary(span, UnaryOperator.Plus, Expression(operandNode));
            case "!":
                return new Unary(span, UnaryOperator.Not, Expression(operandNode));
            case "^":
                return new Unary(span, UnaryOperator.BitNot, Expression(operandNode));
            case "<-":
                return Opaque.Of(span, "receive", Expression(operandNode));
            default:
                return Opaque.Of(span, "unary", Expression(operandNode));
        }
    }

    /// <summary>The variable a pointer points into: x for &amp;x, &amp;x.field and &amp;x[i].</summary>
    private static string? Root(Expr expression) => expression switch
    {
        Name { Identifier: var name } when name != "this" => name,
        Member member => Root(member.Target),
        ElementAccess element => Root(element.Target),
        _ => null,
    };

    private Expr BinaryExpression(JsonElement binary, SourceSpan span)
    {
        var op = Json.Text(binary, "Op");
        var leftNode = Json.Prop(binary, "X")!.Value;
        var rightNode = Json.Prop(binary, "Y")!.Value;

        var comparingNil = op is "==" or "!=";
        var left = Expression(leftNode, comparingNil ? KindOf(rightNode) : GoKind.Other);
        var right = Expression(rightNode, comparingNil ? KindOf(leftNode) : GoKind.Other);

        if (op == "&^") return new Binary(span, BinaryOperator.BitAnd, left, new Unary(span, UnaryOperator.BitNot, right));

        BinaryOperator? known = op switch
        {
            "+" => BinaryOperator.Add,
            "-" => BinaryOperator.Subtract,
            "*" => BinaryOperator.Multiply,
            "/" => BinaryOperator.Divide,
            "%" => BinaryOperator.Modulo,
            "&" => BinaryOperator.BitAnd,
            "|" => BinaryOperator.BitOr,
            "^" => BinaryOperator.BitXor,
            "<<" => BinaryOperator.ShiftLeft,
            ">>" => BinaryOperator.ShiftRight,
            "&&" => BinaryOperator.And,
            "||" => BinaryOperator.Or,
            "==" => BinaryOperator.Equal,
            "!=" => BinaryOperator.NotEqual,
            "<" => BinaryOperator.Less,
            "<=" => BinaryOperator.LessOrEqual,
            ">" => BinaryOperator.Greater,
            ">=" => BinaryOperator.GreaterOrEqual,
            _ => null,
        };

        return known is { } binaryOperator ? new Binary(span, binaryOperator, left, right) : Opaque.Of(span, op ?? "operator", left, right);
    }

    /// <summary>The value a variable of a type starts with: 0, "", false, an empty slice, nil for a pointer or interface.</summary>
    private Expr Zero(JsonElement? type, SourceSpan span) => types.KindOf(type) switch
    {
        GoKind.Whole => new Literal(span, LiteralKind.Integer, 0L),
        GoKind.Real => new Literal(span, LiteralKind.Real, 0.0),
        GoKind.Text => new Literal(span, LiteralKind.Text, ""),
        GoKind.Truth => new Literal(span, LiteralKind.Boolean, false),
        GoKind.Pointer or GoKind.Interface or GoKind.Function => new Literal(span, LiteralKind.Null, null),
        GoKind.Slice => new CollectionLiteral(span, CollectionKind.List, []),
        GoKind.Array => new NewObject(span, IrType.Named("array"),
            ArrayLength(type) is { } length ? [new Argument(null, Expression(length))] : []),
        GoKind.Map => Opaque.Of(span, "nil map"),
        GoKind.Channel => Opaque.Of(span, "nil channel"),
        GoKind.Struct => new NewObject(span, types.TypeOf(type), []),
        _ => Opaque.Of(span, "zero value"),
    };

    private static JsonElement? ArrayLength(JsonElement? type) =>
        Json.Kind(type ?? default) == "ArrayType" && Json.Prop(type, "Len") is { } length && Json.Kind(length) != "Ellipsis" ? length : null;
}
