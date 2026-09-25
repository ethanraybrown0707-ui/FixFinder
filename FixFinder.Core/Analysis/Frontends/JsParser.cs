using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>
/// FixFinder's own JavaScript reader: tokens in, IR out, one file at a time. It follows the shapes real programs are
/// written in - declarations, classes, arrow functions, destructuring, optional chaining, template literals, modules -
/// and where a piece cannot be represented exactly it is kept as something unknown rather than guessed at. A variable
/// declared again inside a block gets a name of its own, so the outer one keeps its value.
/// </summary>
internal sealed class JsParser(string file, IReadOnlyList<JsToken> tokens)
{
    private readonly List<IrFunction> _functions = [];
    private readonly List<IrClass> _classes = [];
    private readonly List<Dictionary<string, string>> _scopes = [];
    private readonly List<string> _enclosing = [];
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);
    private int _at;
    private int _depth;
    private int _steps;
    private int _temporaries;
    private readonly HashSet<string> _named = new(StringComparer.Ordinal);

    public string? Problem { get; private set; }

    public (IReadOnlyList<IrFunction> Functions, IReadOnlyList<IrClass> Classes) Parse()
    {
        _scopes.Add(new Dictionary<string, string>(StringComparer.Ordinal));
        _enclosing.Add(IrFunction.ModuleBody);

        var body = new List<Stmt>();
        while (!AtEnd && Reading()) body.AddRange(Statement());

        _functions.Insert(0, new IrFunction(At(0) is { } first ? Span(first) : SourceSpan.None, IrFunction.ModuleBody, null, [], IrType.Unknown, body));
        return (_functions, _classes);
    }

    // ---- Tokens ----

    private bool AtEnd => Current.Kind == JsTokenKind.End;

    private JsToken Current => tokens[Math.Min(_at, tokens.Count - 1)];

    private JsToken? At(int ahead) => _at + ahead < tokens.Count ? tokens[_at + ahead] : null;

    private JsToken Take() => tokens[Math.Min(_at++, tokens.Count - 1)];

    /// <summary>A name for a function written inside another; two on the same line are told apart.</summary>
    private string Nested(string what, int line)
    {
        var name = $"{what} at line {line}";
        for (var again = 2; !_named.Add(name); again++) name = $"{what} at line {line} ({again})";
        return name;
    }

    /// <summary>
    /// One more piece read. A reader written by hand meets every shape real code is written in, including ones it was
    /// not taught; rather than go round for ever, it gives up on the file and says so.
    /// </summary>
    private bool Reading()
    {
        if (Problem is not null) return false;
        if (++_steps <= 4_000_000) return true;

        Problem = $"line {Current.Line}: FixFinder could not read this file";
        return false;
    }

    private bool Is(string text) => Current.Text == text && Current.Kind is JsTokenKind.Punctuator or JsTokenKind.Keyword;

    private bool IsAhead(int ahead, string text) => At(ahead) is { } token && token.Text == text && token.Kind is JsTokenKind.Punctuator or JsTokenKind.Keyword;

    private bool Eat(string text)
    {
        if (!Is(text)) return false;
        _at++;
        return true;
    }

    private void Expect(string text)
    {
        if (Eat(text)) return;
        Problem ??= $"line {Current.Line}: expected {text} but found {(Current.Kind == JsTokenKind.End ? "the end of the file" : Current.Text)}";
        if (!AtEnd) _at++;
    }

    private SourceSpan Span(JsToken token) => new(file, token.Line, token.Column, token.EndLine, token.EndColumn);

    /// <summary>From a token to the last one read.</summary>
    /// <remarks>
    /// Clamped at both ends, the way <see cref="Current"/> and <see cref="Take"/> are. Take lets the position run past
    /// the end of the tokens on purpose, so a program that stops half way through a statement is read to its end rather
    /// than refused - and this was the one read that forgot it, which made exactly those programs throw.
    /// </remarks>
    private SourceSpan From(JsToken start)
    {
        var last = tokens[Math.Clamp(_at - 1, 0, tokens.Count - 1)];
        return new SourceSpan(file, start.Line, start.Column, last.EndLine, last.EndColumn);
    }

    private void EndStatement() => Eat(";");

    /// <summary>
    /// A value that is about to be used twice - a ?? b keeps a, a?.b tests a then reads it. Anything bigger than a name
    /// is put in a name of its own first, so the code is read once however long the chain is.
    /// </summary>
    private (Expr First, Expr Again) Once(Expr value, SourceSpan span)
    {
        if (value is Name or Literal) return (value, value);

        var name = new Name(span, $"$v{_temporaries++}");
        return (new AssignValue(span, name, value), name);
    }

    // ---- Names ----

    /// <summary>Gives a name declared again inside a block a name of its own, so the outer one keeps its value.</summary>
    private string Declare(string name)
    {
        if (name == "_") return name;
        if (_scopes[^1].TryGetValue(name, out var already)) return already;

        var ir = name;
        while (!_taken.Add(ir)) ir += "'";

        _scopes[^1][name] = ir;
        return ir;
    }

    private string Resolve(string name)
    {
        for (var i = _scopes.Count - 1; i >= 0; i--)
            if (_scopes[i].TryGetValue(name, out var ir)) return ir;
        return name;
    }

    private List<Stmt> InScope(Func<List<Stmt>> read)
    {
        _scopes.Add(new Dictionary<string, string>(StringComparer.Ordinal));
        try
        {
            return read();
        }
        finally
        {
            _scopes.RemoveAt(_scopes.Count - 1);
        }
    }

    // ---- Statements ----

    private List<Stmt> Block()
    {
        var start = Current;
        if (!Is("{")) return Statement().ToList();

        return InScope(() =>
        {
            Expect("{");
            var body = new List<Stmt>();
            while (!Is("}") && !AtEnd && Reading()) body.AddRange(Statement());
            Expect("}");
            return body;
        });
    }

    private IEnumerable<Stmt> Statement()
    {
        if (_depth > 200)
        {
            Problem ??= $"line {Current.Line}: the code is nested too deeply to read";
            return [];
        }

        _depth++;
        var before = _at;
        try
        {
            var statements = Statements();

            // Whatever happens, a statement moves on: a reader that stands still would never finish.
            if (_at == before && !AtEnd) _at++;
            return statements;
        }
        finally
        {
            _depth--;
        }
    }

    private IEnumerable<Stmt> Statements()
    {
        var start = Current;
        var span = Span(start);

        if (Is("{")) return Block();
        if (Eat(";")) return [];

        switch (start.Text)
        {
            case "var" or "let" or "const" when start.Kind == JsTokenKind.Keyword || start.Text == "var":
                if (start.Text == "let" && !(At(1) is { Kind: JsTokenKind.Identifier or JsTokenKind.Keyword } || IsAhead(1, "[") || IsAhead(1, "{"))) break;
                _at++;
                var declarations = Declarations(span);
                EndStatement();
                return declarations;

            case "function":
                _at++;
                var declared = Function(start, name: null, isAsync: false);
                return [new Declare(span, Declare(declared.Name), IrType.Named("function"), Opaque.Of(span, "function"))];

            case "async" when IsAhead(1, "function"):
                _at += 2;
                var asyncDeclared = Function(start, name: null, isAsync: true);
                return [new Declare(span, Declare(asyncDeclared.Name), IrType.Named("function"), Opaque.Of(span, "function"))];

            case "class":
                _at++;
                var type = Class(start);
                return [new Declare(span, Declare(type.Name), IrType.Named(type.Name), Opaque.Of(span, "class"))];

            case "if":
                _at++;
                Expect("(");
                var condition = Expression();
                Expect(")");
                var then = Block();
                var otherwise = Eat("else") ? Block() : [];
                return [new If(span, condition, then, otherwise)];

            case "while":
                _at++;
                Expect("(");
                var test = Expression();
                Expect(")");
                return [new While(span, test, Block(), [])];

            case "do":
                _at++;
                var doBody = Block();
                Expect("while");
                Expect("(");
                var doTest = Expression();
                Expect(")");
                EndStatement();
                return [new While(span, doTest, doBody, [], TestsFirst: false)];

            case "for":
                _at++;
                return ForStatement(span);

            case "switch":
                _at++;
                return SwitchStatement(span);

            case "try":
                _at++;
                return TryStatement(span);

            case "throw":
                _at++;
                var thrown = Current.NewlineBefore ? null : Expression();
                EndStatement();
                return [new Throw(span, thrown)];

            case "return":
                _at++;
                var returned = Current.NewlineBefore || Is(";") || Is("}") || AtEnd ? null : Expression();
                EndStatement();
                return [new Return(span, returned)];

            case "break" or "continue":
                var isBreak = start.Text == "break";
                _at++;
                var label = !Current.NewlineBefore && Current.Kind == JsTokenKind.Identifier ? Take().Text : null;
                EndStatement();
                return [isBreak ? new Break(span, label) : new Continue(span, label)];

            case "import":
                return Import(span);

            case "export":
                _at++;
                if (Eat("default"))
                {
                    var value = Expression();
                    EndStatement();
                    return [new Evaluate(span, value)];
                }

                if (Is("{") || Is("*"))
                {
                    while (!AtEnd && !Is(";") && !Current.NewlineBefore) _at++;
                    EndStatement();
                    return [];
                }

                return Statement();

            case "debugger":
                _at++;
                EndStatement();
                return [];
        }

        // A label: name followed by a colon.
        if (start.Kind == JsTokenKind.Identifier && IsAhead(1, ":"))
        {
            _at += 2;
            return [new Labeled(span, start.Text, Statement().ToList())];
        }

        var expression = Expression();
        EndStatement();
        return [new Evaluate(From(start), expression)];
    }

    private IEnumerable<Stmt> Import(SourceSpan span)
    {
        // import(...) is a call; every other import brings names in from elsewhere.
        if (IsAhead(1, "(")) return [new Evaluate(span, Expression())];

        var names = new List<Stmt>();
        _at++;
        while (!AtEnd && !Is(";") && !(Current.NewlineBefore && Current.Kind != JsTokenKind.Text))
        {
            if (Current.Kind == JsTokenKind.Identifier && !IsAhead(1, ":")) names.Add(new Declare(Span(Current), Declare(Current.Text), IrType.Unknown, Opaque.Of(Span(Current), "imported")));
            _at++;
        }

        EndStatement();
        return names;
    }

    private List<Stmt> Declarations(SourceSpan span)
    {
        var statements = new List<Stmt>();

        do
        {
            var mark = _at;
            var start = Current;

            if (Is("[") || Is("{"))
            {
                var pattern = Pattern();
                var value = Eat("=") ? Assignment() : Opaque.Of(span, "value");
                foreach (var name in pattern) statements.Add(new Declare(From(start), name, IrType.Unknown, Opaque.Of(From(start), "part of a value", value)));
                continue;
            }

            if (Current.Kind is not (JsTokenKind.Identifier or JsTokenKind.Keyword)) break;

            var declared = Declare(Take().Text);
            var initial = Eat("=") ? Assignment() : new Literal(From(start), LiteralKind.Null, null);
            statements.Add(new Declare(From(start), declared, IrType.Unknown, initial));
            if (_at == mark) _at++;
        }
        while (Eat(","));

        return statements;
    }

    /// <summary>The names a destructuring pattern binds; what each one gets is not followed.</summary>
    private List<string> Pattern()
    {
        var names = new List<string>();
        var depth = 0;

        while (!AtEnd && Reading())
        {
            if (Is("[") || Is("{")) depth++;
            else if (Is("]") || Is("}"))
            {
                depth--;
                if (depth == 0)
                {
                    _at++;
                    break;
                }
            }
            else if (Current.Kind == JsTokenKind.Identifier && !IsAhead(1, ":"))
            {
                names.Add(Declare(Current.Text));
            }

            _at++;
        }

        return names;
    }

    private IEnumerable<Stmt> ForStatement(SourceSpan span) => InScope(() =>
    {
        Expect("(");

        var setup = new List<Stmt>();
        var declaring = Is("var") || Is("let") || Is("const");
        var start = Current;

        if (declaring) _at++;

        // for (x of items) and for (x in object). What is walked into is a name or a pattern, never a whole expression:
        // reading further would swallow the `in` of a for-in head.
        var save = _at;
        if (!Is(";"))
        {
            List<string>? pattern = Is("[") || Is("{") ? Pattern() : null;
            var target = pattern is null ? Postfix() : null;

            if (Is("of") || Is("in"))
            {
                var over = Take().Text == "of";
                var items = Expression();
                Expect(")");
                var body = Block();

                var walked = over ? items : Opaque.Of(span, "the names in an object", items);
                var bound = pattern is not null
                    ? pattern.Count > 0 ? new Name(span, pattern[0]) : null
                    : declaring && target is Name named ? new Name(named.Span, Declare(named.Identifier)) : target;

                return [new ForEach(span, bound ?? new Name(span, "_"), walked, body, [])];
            }

            _at = save;
            setup.AddRange(declaring ? Declarations(span) : [new Evaluate(span, Expression())]);
        }

        Expect(";");
        var condition = Is(";") ? null : Expression();
        Expect(";");
        var step = Is(")") ? [] : new List<Stmt> { new Evaluate(Span(Current), Expression()) };
        Expect(")");

        return [new For(span, setup, condition, step, Block())];
    });

    private IEnumerable<Stmt> SwitchStatement(SourceSpan span)
    {
        Expect("(");
        var subject = Expression();
        Expect(")");
        Expect("{");

        var cases = new List<SwitchCase>();
        while (!Is("}") && !AtEnd && Problem is null)
        {
            var mark = _at;
            var labels = new List<Expr>();
            while ((Is("case") || Is("default")) && Reading())
            {
                if (Eat("case")) labels.Add(Expression());
                else _at++;
                Expect(":");
            }

            var body = InScope(() =>
            {
                var statements = new List<Stmt>();
                while (!Is("case") && !Is("default") && !Is("}") && !AtEnd && Reading()) statements.AddRange(Statement());
                return statements;
            });

            var fallsThrough = body.Count > 0 && body[^1] is not (Break or Return or Continue or Throw);
            cases.Add(new SwitchCase(labels, body, fallsThrough));
            if (_at == mark) _at++;
        }

        Expect("}");
        return [new Switch(span, subject, cases)];
    }

    private IEnumerable<Stmt> TryStatement(SourceSpan span)
    {
        var body = Block();
        var handlers = new List<Handler>();
        var finally_ = new List<Stmt>();

        if (Eat("catch"))
        {
            var caught = Span(Current);
            string? variable = null;
            if (Eat("("))
            {
                variable = Current.Kind == JsTokenKind.Identifier ? Declare(Take().Text) : null;
                if (variable is null) Pattern();
                Expect(")");
            }

            handlers.Add(new Handler(caught, [], variable, Block()));
        }

        if (Eat("finally")) finally_ = Block();

        return [new Try(span, body, handlers, [], finally_)];
    }

    // ---- Functions and classes ----

    private IrFunction Function(JsToken start, string? name, bool isAsync, string? owner = null, bool isConstructor = false, bool isStatic = false,
        bool isGenerator = false)
    {
        isGenerator |= Eat("*");
        var given = name ?? (Current.Kind is JsTokenKind.Identifier or JsTokenKind.Keyword && !Is("(") ? Take().Text : Nested("function", start.Line));

        _scopes.Add(new Dictionary<string, string>(StringComparer.Ordinal));
        _enclosing.Add(owner is null ? given : $"{owner}.{given}");

        var parameters = Parameters();
        var body = Is("{") ? Block() : [new Return(Span(Current), Assignment())];

        _enclosing.RemoveAt(_enclosing.Count - 1);
        _scopes.RemoveAt(_scopes.Count - 1);

        var function = new IrFunction(From(start), given, owner, parameters, IrType.Unknown, body)
        {
            IsAsync = isAsync,
            IsGenerator = isGenerator,
            IsStatic = isStatic,
            IsConstructor = isConstructor,
            EnclosedBy = _enclosing[^1],
        };

        if (owner is null) _functions.Add(function);
        return function;
    }

    private List<IrParameter> Parameters()
    {
        var parameters = new List<IrParameter>();
        Expect("(");

        while (!Is(")") && !AtEnd && Reading())
        {
            var mark = _at;
            var start = Current;

            if (Is("[") || Is("{"))
            {
                var names = Pattern();
                var given = Eat("=") ? Assignment() : null;
                foreach (var name in names) parameters.Add(new IrParameter(From(start), name, IrType.Unknown, given is null ? null : Opaque.Of(From(start), "part of a value", given)));
            }
            else if (Eat("..."))
            {
                if (Current.Kind is JsTokenKind.Identifier or JsTokenKind.Keyword)
                    parameters.Add(new IrParameter(Span(Current), Declare(Take().Text), IrType.Named("list"), null, ParameterKind.Rest));
            }
            else if (Current.Kind is JsTokenKind.Identifier or JsTokenKind.Keyword)
            {
                var name = Declare(Take().Text);
                var given = Eat("=") ? Assignment() : null;
                parameters.Add(new IrParameter(From(start), name, IrType.Unknown, given));
            }
            else
            {
                _at++;
            }

            if (_at == mark) _at++;
            if (!Eat(",")) break;
        }

        Expect(")");
        return parameters;
    }

    private IrClass Class(JsToken start)
    {
        var name = Current.Kind == JsTokenKind.Identifier ? Take().Text : $"class at line {start.Line}";
        var bases = new List<string>();

        if (Eat("extends"))
        {
            var parent = Unary();
            bases.Add(parent is Name named ? named.Identifier : IrText.Of(parent));
        }

        Expect("{");
        var fields = new List<IrField>();
        var methods = new List<IrFunction>();

        while (!Is("}") && !AtEnd && Reading())
        {
            var mark = _at;
            if (Eat(";")) continue;

            var memberStart = Current;
            var isStatic = Is("static") && !IsAhead(1, "(") && !IsAhead(1, "=");
            if (isStatic) _at++;

            var isAsync = Is("async") && !IsAhead(1, "(") && !IsAhead(1, "=");
            if (isAsync) _at++;

            if ((Is("get") || Is("set")) && !IsAhead(1, "(") && !IsAhead(1, "=") && !IsAhead(1, ";")) _at++;

            var generator = Eat("*");
            Eat("#");
            var memberName = Current.Kind is JsTokenKind.Identifier or JsTokenKind.Keyword or JsTokenKind.Text or JsTokenKind.Number
                ? Take().Text
                : Is("[") ? SkipComputedName() : "?";

            if (Is("("))
            {
                methods.Add(Function(memberStart, memberName, isAsync, owner: name, isConstructor: memberName == "constructor", isStatic: isStatic,
                    isGenerator: generator));
                continue;
            }

            var initial = Eat("=") ? Assignment() : null;
            fields.Add(new IrField(From(memberStart), memberName, IrType.Unknown, initial, isStatic));
            EndStatement();
            if (_at == mark) _at++;
        }

        Expect("}");
        var type = new IrClass(From(start), name, bases, fields, methods);
        _classes.Add(type);
        return type;
    }

    private string SkipComputedName()
    {
        Expect("[");
        var depth = 1;
        while (!AtEnd && depth > 0)
        {
            if (Is("[")) depth++;
            else if (Is("]")) depth--;
            _at++;
        }

        return "?";
    }

    // ---- Expressions ----

    private Expr Expression()
    {
        var value = Assignment();

        while (Is(","))
        {
            var span = Span(Take());
            var next = Assignment();
            value = Opaque.Of(span, "one value then another", value, next);
        }

        return value;
    }

    private Expr Assignment()
    {
        if (IsArrowAhead()) return Arrow();

        var start = Current;
        var target = Conditional();

        if (Current.Kind == JsTokenKind.Punctuator && Current.Text is "=" or "+=" or "-=" or "*=" or "/=" or "%=" or "**=" or "<<=" or ">>=" or ">>>=" or "&=" or "|=" or "^=" or "&&=" or "||=" or "??=")
        {
            var op = Take().Text;
            var value = Assignment();
            var span = From(start);

            if (op == "=") return new AssignValue(span, target, value);

            var compound = op switch
            {
                "+=" => BinaryOperator.Add,
                "-=" => BinaryOperator.Subtract,
                "*=" => BinaryOperator.Multiply,
                "/=" => BinaryOperator.Divide,
                "%=" => BinaryOperator.Modulo,
                "<<=" => BinaryOperator.ShiftLeft,
                ">>=" or ">>>=" => BinaryOperator.ShiftRight,
                "&=" => BinaryOperator.BitAnd,
                "|=" => BinaryOperator.BitOr,
                "^=" => BinaryOperator.BitXor,
                _ => (BinaryOperator?)null,
            };

            return compound is { } known
                ? new AssignValue(span, target, new Binary(span, known, target, value))
                : new AssignValue(span, target, Opaque.Of(span, op, target, value));
        }

        return target;
    }

    /// <summary>Whether what follows is an arrow function: a name or a parameter list with => after it.</summary>
    private bool IsArrowAhead()
    {
        var at = _at;
        if (Current.Text == "async" && Current.Kind == JsTokenKind.Keyword && (At(1)?.Kind == JsTokenKind.Identifier || IsAhead(1, "("))) at++;

        var token = At(at - _at);
        if (token is null) return false;

        if (token.Kind is JsTokenKind.Identifier) return At(at - _at + 1) is { Text: "=>" };
        if (token.Text != "(") return false;

        var depth = 0;
        for (var i = at; i < tokens.Count; i++)
        {
            var text = tokens[i].Text;
            if (tokens[i].Kind == JsTokenKind.Punctuator && text is "(" or "[" or "{") depth++;
            else if (tokens[i].Kind == JsTokenKind.Punctuator && text is ")" or "]" or "}")
            {
                depth--;
                if (depth == 0) return tokens.Count > i + 1 && tokens[i + 1].Text == "=>";
            }
            else if (tokens[i].Kind == JsTokenKind.End)
            {
                return false;
            }
        }

        return false;
    }

    private Expr Arrow()
    {
        var start = Current;
        var isAsync = Is("async");
        if (isAsync) _at++;

        var name = Nested("arrow", start.Line);
        _scopes.Add(new Dictionary<string, string>(StringComparer.Ordinal));
        _enclosing.Add(name);

        List<IrParameter> parameters;
        if (Current.Kind == JsTokenKind.Identifier)
        {
            var only = Current;
            parameters = [new IrParameter(Span(only), Declare(Take().Text), IrType.Unknown)];
        }
        else
        {
            parameters = Parameters();
        }

        Expect("=>");
        var body = Is("{") ? Block() : [new Return(Span(Current), Assignment())];

        _enclosing.RemoveAt(_enclosing.Count - 1);
        _scopes.RemoveAt(_scopes.Count - 1);

        var span = From(start);
        var function = new IrFunction(span, name, null, parameters, IrType.Unknown, body)
        {
            IsAsync = isAsync,
            EnclosedBy = _enclosing[^1],
        };

        _functions.Add(function with { OuterNames = Captured(function) });
        return Opaque.Of(span, "lambda expression");
    }

    private static List<string> Captured(IrFunction function)
    {
        var own = IrWalk.LocalNames(function, assigningDeclares: false);
        return IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Names)
            .Where(n => n != "this" && !own.Contains(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private Expr Conditional()
    {
        var start = Current;
        var test = Nullish();

        if (!Eat("?")) return test;

        var whenTrue = Assignment();
        Expect(":");
        var whenFalse = Assignment();
        return new Conditional(From(start), test, whenTrue, whenFalse);
    }

    private Expr Nullish()
    {
        var start = Current;
        var left = Or();

        while (Is("??"))
        {
            _at++;
            var right = Or();
            var span = From(start);

            // a ?? b keeps a unless it is nothing.
            var (first, again) = Once(left, span);
            left = new Conditional(span, new Binary(span, BinaryOperator.NotEqual, first, new Literal(span, LiteralKind.Null, null)), again, right);
        }

        return left;
    }

    private Expr Or() => Chain(And, ["||"]);

    private Expr And() => Chain(BitOr, ["&&"]);

    private Expr BitOr() => Chain(BitXor, ["|"]);

    private Expr BitXor() => Chain(BitAnd, ["^"]);

    private Expr BitAnd() => Chain(Equality, ["&"]);

    private Expr Equality() => Chain(Relational, ["===", "!==", "==", "!="]);

    private Expr Relational() => Chain(Shift, ["<", ">", "<=", ">=", "instanceof", "in"]);

    private Expr Shift() => Chain(Additive, ["<<", ">>", ">>>"]);

    private Expr Additive() => Chain(Multiplicative, ["+", "-"]);

    private Expr Multiplicative() => Chain(Exponent, ["*", "/", "%"]);

    private Expr Exponent()
    {
        var start = Current;
        var left = Unary();
        if (!Is("**")) return left;

        _at++;
        var right = Exponent();
        return new Binary(From(start), BinaryOperator.Power, left, right);
    }

    private Expr Chain(Func<Expr> next, string[] operators)
    {
        var start = Current;
        var left = next();

        while (Current.Kind is JsTokenKind.Punctuator or JsTokenKind.Keyword && operators.Contains(Current.Text))
        {
            var op = Take().Text;
            var right = next();
            var span = From(start);

            var known = op switch
            {
                "||" => BinaryOperator.Or,
                "&&" => BinaryOperator.And,
                "|" => BinaryOperator.BitOr,
                "^" => BinaryOperator.BitXor,
                "&" => BinaryOperator.BitAnd,
                "===" or "==" => BinaryOperator.Equal,
                "!==" or "!=" => BinaryOperator.NotEqual,
                "<" => BinaryOperator.Less,
                ">" => BinaryOperator.Greater,
                "<=" => BinaryOperator.LessOrEqual,
                ">=" => BinaryOperator.GreaterOrEqual,
                "<<" => BinaryOperator.ShiftLeft,
                ">>" or ">>>" => BinaryOperator.ShiftRight,
                "+" => BinaryOperator.Add,
                "-" => BinaryOperator.Subtract,
                "*" => BinaryOperator.Multiply,
                "/" => BinaryOperator.Divide,
                "%" => BinaryOperator.Modulo,
                "in" => BinaryOperator.In,
                _ => (BinaryOperator?)null,
            };

            left = known is { } binary ? new Binary(span, binary, left, right) : Opaque.Of(span, op, left, right);
        }

        return left;
    }

    private Expr Unary()
    {
        var start = Current;

        if (Current.Kind is JsTokenKind.Punctuator or JsTokenKind.Keyword)
        {
            switch (Current.Text)
            {
                case "!":
                    _at++;
                    return new Unary(From(start), UnaryOperator.Not, Unary());
                case "-":
                    _at++;
                    return new Unary(From(start), UnaryOperator.Negate, Unary());
                case "+":
                    _at++;
                    return new Unary(From(start), UnaryOperator.Plus, Unary());
                case "~":
                    _at++;
                    return new Unary(From(start), UnaryOperator.BitNot, Unary());
                case "typeof" or "void" or "delete":
                    var what = Take().Text;
                    return Opaque.Of(From(start), what, Unary());
                case "await":
                    _at++;
                    return Opaque.Of(From(start), "await", Unary());
                case "yield":
                    _at++;
                    Eat("*");
                    return Is(")") || Is("]") || Is("}") || Is(";") || Current.NewlineBefore ? Opaque.Of(From(start), "yield") : Opaque.Of(From(start), "yield", Assignment());
                case "++" or "--":
                    var step = Take().Text;
                    var operand = Unary();
                    var stepSpan = From(start);
                    return new AssignValue(stepSpan, operand,
                        new Binary(stepSpan, step == "++" ? BinaryOperator.Add : BinaryOperator.Subtract, operand, new Literal(stepSpan, LiteralKind.Integer, 1L)));
                case "new":
                    return New();
            }
        }

        return Postfix();
    }

    private Expr New()
    {
        var start = Take();

        if (Is(".")) // new.target
        {
            _at += 2;
            return Opaque.Of(From(start), "new.target");
        }

        var callee = Primary();
        while (Is(".") || Is("["))
        {
            callee = Tail(callee, start);
        }

        var arguments = Is("(") ? Arguments() : [];
        var type = callee switch
        {
            Name name => IrType.Named(name.Identifier),
            Member member => IrType.Named(member.MemberName),
            _ => IrType.Unknown,
        };

        // What is made can be used straight away: new URL(...).toString().
        return Following(new NewObject(From(start), type, arguments), start);
    }

    private Expr Postfix()
    {
        var start = Current;
        return Following(Primary(), start);
    }

    private Expr Following(Expr value, JsToken start)
    {
        while (Reading())
        {
            if (Is(".") || Is("[") || Is("(") || Is("?.") || Current.Kind == JsTokenKind.Template)
            {
                value = Tail(value, start);
                continue;
            }

            if ((Is("++") || Is("--")) && !Current.NewlineBefore)
            {
                var step = Take().Text;
                var span = From(start);
                value = new AssignValue(span, value,
                    new Binary(span, step == "++" ? BinaryOperator.Add : BinaryOperator.Subtract, value, new Literal(span, LiteralKind.Integer, 1L)),
                    ValueBeforeAssigning: true);
                continue;
            }

            return value;
        }

        return value;
    }

    private Expr Tail(Expr value, JsToken start)
    {
        if (Current.Kind == JsTokenKind.Template)
        {
            var tag = Take();
            return Opaque.Of(Span(tag), "formatted text", value);
        }

        if (Eat("?."))
        {
            var span = From(start);
            var (first, again) = Once(value, span);
            var reached = Is("(") ? new Call(span, again, Arguments())
                : Is("[") ? Element(again, start)
                : new Member(span, again, Current.Kind is JsTokenKind.Identifier or JsTokenKind.Keyword ? Take().Text : "?");

            // a?.b.c is nothing when a is nothing: the whole rest of the chain is skipped, not just the next step.
            var rest = Following(reached, start);
            return new Conditional(span, new Binary(span, BinaryOperator.NotEqual, first, new Literal(span, LiteralKind.Null, null)), rest,
                new Literal(span, LiteralKind.Null, null));
        }

        if (Eat("."))
        {
            Eat("#");
            var name = Current.Kind is JsTokenKind.Identifier or JsTokenKind.Keyword ? Take().Text : "?";
            return new Member(From(start), value, name);
        }

        if (Is("[")) return Element(value, start);

        return new Call(From(start), value, Arguments());
    }

    private Expr Element(Expr value, JsToken start)
    {
        Expect("[");
        var key = Expression();
        Expect("]");
        return new ElementAccess(From(start), value, key);
    }

    private List<Argument> Arguments()
    {
        var arguments = new List<Argument>();
        Expect("(");

        while (!Is(")") && !AtEnd && Reading())
        {
            var mark = _at;
            var start = Current;
            if (Eat("...")) arguments.Add(new Argument(null, Opaque.Of(From(start), "the rest of a list", Assignment())));
            else arguments.Add(new Argument(null, Assignment()));

            if (_at == mark) _at++;
            if (!Eat(",")) break;
        }

        Expect(")");
        return arguments;
    }

    private Expr Primary()
    {
        var start = Current;
        var span = Span(start);

        switch (start.Kind)
        {
            case JsTokenKind.Number:
                _at++;
                return start.Value switch
                {
                    long whole => new Literal(span, LiteralKind.Integer, whole),
                    double real => new Literal(span, LiteralKind.Real, real),
                    _ => Opaque.Of(span, "number"),
                };

            case JsTokenKind.Text:
                _at++;
                return new Literal(span, LiteralKind.Text, start.Value as string ?? "");

            case JsTokenKind.Regex:
                _at++;
                return Opaque.Of(span, "pattern");

            case JsTokenKind.Template:
                _at++;
                var parts = (start.Parts ?? []).Select(part => Inside(part, start)).ToArray();
                return Opaque.Of(span, "formatted text", parts);

            case JsTokenKind.Identifier:
                _at++;
                return new Name(span, Resolve(start.Text));
        }

        switch (start.Text)
        {
            case "(":
                _at++;
                var inner = Expression();
                Expect(")");
                return inner;

            case "[":
                _at++;
                var items = new List<Expr>();
                var spread = false;
                while (!Is("]") && !AtEnd && Reading())
                {
                    if (Is(","))
                    {
                        _at++;
                        continue;
                    }

                    var itemStart = Current;
                    spread |= Eat("...");
                    items.Add(Assignment());
                    if (!Eat(",")) break;
                }

                Expect("]");

                // A list built with ... holds however many the lists it was built from held.
                return spread
                    ? Opaque.Of(From(start), "a list", [.. items])
                    : new CollectionLiteral(From(start), CollectionKind.List, items);

            case "{":
                return ObjectLiteral(start);

            case "function":
                _at++;
                var expressionFunction = Function(start, name: null, isAsync: false);
                return Opaque.Of(expressionFunction.Span, "lambda expression");

            case "async" when IsAhead(1, "function"):
                _at += 2;
                var asyncFunction = Function(start, name: null, isAsync: true);
                return Opaque.Of(asyncFunction.Span, "lambda expression");

            case "class":
                _at++;
                var type = Class(start);
                return Opaque.Of(From(start), "class", new Name(span, type.Name));

            case "this" or "super":
                _at++;
                return new Name(span, "this");

            case "true":
                _at++;
                return new Literal(span, LiteralKind.Boolean, true);

            case "false":
                _at++;
                return new Literal(span, LiteralKind.Boolean, false);

            case "null" or "undefined":
                _at++;
                return new Literal(span, LiteralKind.Null, null);

            case "new":
                return New();
        }

        if (start.Kind == JsTokenKind.Keyword)
        {
            _at++;
            return new Name(span, Resolve(start.Text));
        }

        Problem ??= $"line {start.Line}: FixFinder cannot read `{start.Text}` here";
        _at++;
        return Opaque.Of(span, "value");
    }

    /// <summary>The code inside a template's ${...}, read on its own and written down as part of the template.</summary>
    private Expr Inside(string code, JsToken start)
    {
        var lexer = new JsLexer(code);
        var here = lexer.Tokens()
            .Select(token => token with { Line = start.Line, Column = start.Column, EndLine = start.EndLine, EndColumn = start.EndColumn })
            .ToList();

        var inner = new JsParser(file, here);
        inner._scopes.AddRange(_scopes);
        var (functions, _) = inner.Parse();

        var body = functions.FirstOrDefault(f => f.Name == IrFunction.ModuleBody)?.Body ?? [];
        return body.OfType<Evaluate>().FirstOrDefault()?.Value ?? Opaque.Of(Span(start), "value");
    }

    private Expr ObjectLiteral(JsToken start)
    {
        Expect("{");
        var keys = new List<Expr>();
        var values = new List<Expr>();
        var extra = new List<Expr>();
        var spreading = false;

        while (!Is("}") && !AtEnd && Reading())
        {
            var mark = _at;
            if (Eat(",")) continue;

            var memberStart = Current;
            if (Eat("..."))
            {
                spreading = true;
                extra.Add(Assignment());
                continue;
            }

            var isAsync = Is("async") && !IsAhead(1, ":") && !IsAhead(1, "(") && !IsAhead(1, ",");
            if (isAsync) _at++;
            if ((Is("get") || Is("set")) && !IsAhead(1, ":") && !IsAhead(1, ",") && !IsAhead(1, "(") && !IsAhead(1, "}")) _at++;
            var yielding = Eat("*");

            Expr key;
            if (Is("["))
            {
                SkipComputedName();
                key = Opaque.Of(From(memberStart), "name");
            }
            else if (Current.Kind is JsTokenKind.Identifier or JsTokenKind.Keyword)
            {
                key = new Literal(Span(Current), LiteralKind.Text, Take().Text);
            }
            else if (Current.Kind is JsTokenKind.Text or JsTokenKind.Number)
            {
                var literal = Take();
                key = new Literal(Span(literal), LiteralKind.Text, literal.Value?.ToString() ?? literal.Text);
            }
            else
            {
                _at++;
                continue;
            }

            keys.Add(key);

            if (_at == mark) _at++;

            if (Is("("))
            {
                var method = Function(memberStart, key is Literal { Value: string named } ? named : "?", isAsync, isGenerator: yielding);
                values.Add(Opaque.Of(method.Span, "lambda expression"));
            }
            else if (Eat(":"))
            {
                values.Add(Assignment());
            }
            else
            {
                values.Add(key is Literal { Value: string shorthand } ? new Name(From(memberStart), Resolve(shorthand)) : Opaque.Of(From(memberStart), "value"));
            }

            if (!Eat(",")) break;
        }

        Expect("}");

        // An object built with ... holds however many the objects it was built from held.
        return spreading
            ? Opaque.Of(From(start), "an object", [.. values, .. extra])
            : new CollectionLiteral(From(start), CollectionKind.Dictionary, values, keys);
    }
}
