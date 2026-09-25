using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>A type as C writes it: a name, how many stars follow it, and the sizes of any array brackets.</summary>
internal sealed record CType(string Name, int Pointers, IReadOnlyList<Expr?> Dimensions)
{
    public static CType Unknown { get; } = new("?", 0, []);

    public bool IsPointer => Pointers > 0;

    public IrType Ir =>
        Dimensions.Count > 0 ? IrType.Named("array", IrType.Named(Name))
        : Pointers > 0 ? IrType.Named(Name + new string('*', Pointers))
        : IrType.Named(Name);
}

/// <summary>
/// FixFinder's own C and C++ reader: tokens in, IR out. It reads the shapes ordinary programs are written in -
/// functions, structs, classes, pointers, arrays, loops - and keeps anything it cannot represent exactly, such as a
/// template or an operator overload, as something unknown rather than guessing. A pointer's star is a member read
/// called <c>*</c>, so going through a null pointer is found the same way as reading a field of nothing.
/// </summary>
internal sealed class CParser(string file, IReadOnlyList<CToken> tokens, bool cpp)
{
    private static readonly HashSet<string> TypeWords = new(StringComparer.Ordinal)
    {
        "void", "char", "short", "int", "long", "float", "double", "signed", "unsigned", "_Bool", "bool", "struct", "union", "enum",
        "const", "volatile", "static", "extern", "register", "inline", "restrict", "auto", "_Atomic", "_Thread_local", "constexpr",
        "virtual", "explicit", "mutable", "friend", "typename", "decltype", "noexcept",
    };

    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal)
    {
        "const", "volatile", "static", "extern", "register", "inline", "restrict", "_Atomic", "_Thread_local", "constexpr",
        "virtual", "explicit", "mutable", "friend", "noexcept", "override", "final",
    };

    private readonly List<IrFunction> _functions = [];
    private readonly List<IrClass> _classes = [];
    private readonly HashSet<string> _typedefs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<IrField>> _structs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _constants = new(StringComparer.Ordinal);
    private readonly List<Dictionary<string, CType>> _scopes = [];
    private readonly List<string?> _breaks = [];
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);
    private string? _owner;
    private bool _hasGoto;
    private int _at;
    private int _depth;
    private int _steps;

    public string? Problem { get; private set; }

    public (IReadOnlyList<IrFunction> Functions, IReadOnlyList<IrClass> Classes) Parse()
    {
        _scopes.Add(new Dictionary<string, CType>(StringComparer.Ordinal));

        while (!AtEnd && Reading())
        {
            var before = _at;
            Declaration(owner: null);
            if (_at == before) _at++;
        }

        foreach (var (name, fields) in _structs)
        {
            if (_classes.Any(c => c.Name == name)) continue;
            _classes.Add(new IrClass(SourceSpan.None, name, [], fields, []));
        }

        return (_functions, _classes);
    }

    // ---- Tokens ----

    private bool AtEnd => Current.Kind == CTokenKind.End;

    private CToken Current => tokens[Math.Min(_at, tokens.Count - 1)];

    private CToken? At(int ahead) => _at + ahead < tokens.Count ? tokens[_at + ahead] : null;

    private CToken Take() => tokens[Math.Min(_at++, tokens.Count - 1)];

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

    private bool Is(string text) => Current.Text == text && Current.Kind is CTokenKind.Punctuator or CTokenKind.Keyword;

    private bool IsAhead(int ahead, string text) => At(ahead) is { } token && token.Text == text && token.Kind is CTokenKind.Punctuator or CTokenKind.Keyword;

    private bool Eat(string text)
    {
        if (!Is(text)) return false;
        _at++;
        return true;
    }

    private void Expect(string text)
    {
        if (Eat(text)) return;
        Problem ??= $"line {Current.Line}: expected {text} but found {(AtEnd ? "the end of the file" : Current.Text)}";
        if (!AtEnd) _at++;
    }

    private SourceSpan Span(CToken token) => new(file, token.Line, token.Column, token.EndLine, token.EndColumn);

    /// <summary>From a token to the last one read.</summary>
    /// <remarks>
    /// Clamped at both ends, the way <see cref="Current"/> and <see cref="Take"/> are. Take lets the position run past
    /// the end of the tokens on purpose, so a program that stops half way through a declaration is read to its end
    /// rather than refused - and this was the one read that forgot it, which made exactly those programs throw.
    /// </remarks>
    private SourceSpan From(CToken start)
    {
        var last = tokens[Math.Clamp(_at - 1, 0, tokens.Count - 1)];
        return new SourceSpan(file, start.Line, start.Column, last.EndLine, last.EndColumn);
    }

    private void SkipTo(string text)
    {
        var depth = 0;
        while (!AtEnd)
        {
            if (Is("{") || Is("(") || Is("[")) depth++;
            else if (Is("}") || Is(")") || Is("]")) depth--;
            else if (depth == 0 && Is(text))
            {
                _at++;
                return;
            }

            if (depth < 0) return;
            _at++;
        }
    }

    private void SkipBlock()
    {
        if (!Is("{")) return;

        var depth = 0;
        while (!AtEnd)
        {
            if (Is("{")) depth++;
            else if (Is("}"))
            {
                depth--;
                _at++;
                if (depth == 0) return;
                continue;
            }

            _at++;
        }
    }

    // ---- Names ----

    /// <summary>Gives a name declared again inside a block a name of its own, so the outer one keeps its value.</summary>
    private string Declare(string name, CType type)
    {
        if (_scopes[^1].TryGetValue(name, out _)) return name;

        var ir = name;
        while (!_taken.Add(ir)) ir += "'";

        _scopes[^1][name] = type;
        if (ir != name) _scopes[^1][ir] = type;
        return ir;
    }

    private CType? Known(string name)
    {
        for (var i = _scopes.Count - 1; i >= 0; i--)
            if (_scopes[i].TryGetValue(name, out var type)) return type;
        return null;
    }

    private List<Stmt> InScope(Func<List<Stmt>> read)
    {
        _scopes.Add(new Dictionary<string, CType>(StringComparer.Ordinal));
        try
        {
            return read();
        }
        finally
        {
            _scopes.RemoveAt(_scopes.Count - 1);
        }
    }

    // ---- Declarations ----

    private void Declaration(string? owner)
    {
        if (Eat(";")) return;

        var start = Current;

        if (Is("template"))
        {
            _at++;
            SkipAngles();
            return;
        }

        if (Is("namespace"))
        {
            _at++;
            while (!Is("{") && !AtEnd) _at++;
            if (Eat("{"))
            {
                while (!Is("}") && !AtEnd && Problem is null) Declaration(owner);
                Expect("}");
            }

            return;
        }

        if (Is("using") || Is("public") || Is("private") || Is("protected"))
        {
            if (Is("public") || Is("private") || Is("protected"))
            {
                if (IsAhead(1, ":"))
                {
                    _at += 2;
                    return;
                }
            }

            SkipTo(";");
            return;
        }

        if (Is("extern") && At(1)?.Kind == CTokenKind.Text)
        {
            _at += 2;
            if (Eat("{"))
            {
                while (!Is("}") && !AtEnd && Problem is null) Declaration(owner);
                Expect("}");
            }

            return;
        }

        if (Is("typedef"))
        {
            _at++;
            var underlying = TypeName(out var tag);
            if (tag is not null) ReadStructBody(tag);

            // The names a typedef gives, up to the semicolon.
            while (!Is(";") && !AtEnd)
            {
                if (Current.Kind == CTokenKind.Identifier && (IsAhead(1, ";") || IsAhead(1, ",")))
                {
                    _typedefs.Add(Current.Text);
                    if (tag is not null && _structs.TryGetValue(tag, out var fields)) _structs[Current.Text] = fields;
                }

                _at++;
            }

            Eat(";");
            return;
        }

        if ((Is("struct") || Is("union") || Is("class")) && At(1)?.Kind == CTokenKind.Identifier &&
            (IsAhead(2, "{") || IsAhead(2, ":") && cpp || IsAhead(2, ";")))
        {
            var isClass = Is("class");
            _at++;
            var name = Take().Text;
            _typedefs.Add(name);

            if (Eat(";")) return;

            var bases = new List<string>();
            if (Eat(":"))
            {
                while (!Is("{") && !AtEnd)
                {
                    if (Current.Kind == CTokenKind.Identifier) bases.Add(Current.Text);
                    _at++;
                }
            }

            ReadStructBody(name, bases, isClass || cpp);
            SkipTo(";");
            return;
        }

        if (Is("enum"))
        {
            _at++;
            if (Current.Kind == CTokenKind.Identifier) _typedefs.Add(Take().Text);
            if (Eat("{"))
            {
                long next = 0;
                while (!Is("}") && !AtEnd)
                {
                    if (Current.Kind == CTokenKind.Identifier)
                    {
                        var name = Take().Text;
                        if (Eat("=") && Current.Kind == CTokenKind.Number && Current.Value is long given) next = given;
                        _constants[name] = next++;
                    }

                    if (!Is("}")) _at++;
                }

                Expect("}");
            }

            SkipTo(";");
            return;
        }

        // An ordinary declaration: a type, then names, and possibly a body.
        if (!StartsType())
        {
            SkipTo(";");
            return;
        }

        var type = TypeName(out var structTag);
        if (structTag is not null) ReadStructBody(structTag);

        while (!AtEnd && Reading())
        {
            var stars = Stars();
            if (Is(";"))
            {
                _at++;
                return;
            }

            var nameToken = Current;
            string? memberOf = owner;

            if (Current.Kind is not (CTokenKind.Identifier or CTokenKind.Keyword))
            {
                SkipTo(";");
                return;
            }

            var name = Take().Text;

            // C++ writes a method outside its class as void Counter::add(int n).
            while (cpp && Is("::"))
            {
                _at++;
                memberOf = name;
                name = Current.Kind is CTokenKind.Identifier or CTokenKind.Keyword ? Take().Text : name;
            }

            if (Is("("))
            {
                var parameters = Parameters();
                while (Current.Kind == CTokenKind.Keyword && Ignored.Contains(Current.Text)) _at++;
                if (Is(":") && cpp) SkipTo("{", keep: true); // a constructor's initialiser list

                if (Is("{"))
                {
                    ReadFunction(nameToken, name, memberOf, type with { Pointers = stars }, parameters);
                    return;
                }

                SkipTo(";");
                return;
            }

            var dimensions = Dimensions();
            var variable = type with { Pointers = stars, Dimensions = dimensions };
            _scopes[0][name] = variable;

            if (Eat("=")) Initialiser(variable, Span(nameToken));
            if (Eat(",")) continue;

            SkipTo(";");
            return;
        }
    }

    private void SkipAngles()
    {
        var depth = 0;
        while (!AtEnd)
        {
            if (Is("<")) depth++;
            else if (Is(">"))
            {
                depth--;
                _at++;
                if (depth <= 0) return;
                continue;
            }

            _at++;
        }
    }

    private void SkipTo(string text, bool keep)
    {
        while (!AtEnd && !Is(text)) _at++;
        if (!keep) Eat(text);
    }

    /// <summary>The fields and, in C++, the methods a struct or class holds.</summary>
    private void ReadStructBody(string name, List<string>? bases = null, bool asClass = false)
    {
        if (!Is("{")) return;

        _at++;
        var fields = new List<IrField>();
        var methods = new List<IrFunction>();
        var outer = _owner;
        _owner = name;

        while (!Is("}") && !AtEnd && Reading())
        {
            if (Eat(";")) continue;

            if ((Is("public") || Is("private") || Is("protected")) && IsAhead(1, ":"))
            {
                _at += 2;
                continue;
            }

            var before = _at;
            var start = Current;

            if (Is("template"))
            {
                _at++;
                SkipAngles();
                SkipBlock();
                continue;
            }

            if (!StartsType() && Current.Kind != CTokenKind.Identifier)
            {
                _at++;
                continue;
            }

            // A constructor or destructor has no type of its own.
            var isSpecial = cpp && (Is("~") || Current.Kind == CTokenKind.Identifier && Current.Text == name && IsAhead(1, "("));
            var type = isSpecial ? new CType("void", 0, []) : TypeName(out var nested);
            if (isSpecial) Eat("~");

            while (!Is(";") && !AtEnd && Reading())
            {
                var stars = Stars();
                if (Current.Kind is not (CTokenKind.Identifier or CTokenKind.Keyword)) break;

                var memberToken = Current;
                var memberName = Take().Text;

                if (Is("("))
                {
                    var parameters = Parameters();
                    while (Current.Kind == CTokenKind.Keyword && Ignored.Contains(Current.Text)) _at++;
                    if (Eat("=")) _at++; // = 0 for a pure virtual method
                    if (Is(":")) SkipTo("{", keep: true);

                    if (Is("{"))
                    {
                        var method = ReadFunction(memberToken, memberName, name, type with { Pointers = stars }, parameters, add: false);
                        methods.Add(method);
                    }
                    else
                    {
                        SkipTo(";", keep: true);
                    }

                    break;
                }

                var dimensions = Dimensions();
                Expr? initial = null;
                if (Eat("=")) initial = Assignment();
                fields.Add(new IrField(From(memberToken), memberName, (type with { Pointers = stars, Dimensions = dimensions }).Ir, initial, false));

                if (!Eat(",")) break;
            }

            Eat(";");
            if (_at == before) _at++;
        }

        Expect("}");
        _owner = outer;
        _structs[name] = fields;
        if (methods.Count > 0 || asClass) _classes.Add(new IrClass(SourceSpan.None, name, bases ?? [], fields, methods));
    }

    private bool StartsType()
    {
        if (Current.Kind == CTokenKind.Keyword && TypeWords.Contains(Current.Text)) return true;
        if (Current.Kind != CTokenKind.Identifier) return false;
        if (_typedefs.Contains(Current.Text)) return true;

        // A name followed by another name, a star or a reference is a type: MyType value, MyType *p.
        return At(1) is { Kind: CTokenKind.Identifier } || IsAhead(1, "*") && At(2)?.Kind == CTokenKind.Identifier;
    }

    /// <summary>The name of a type, gathering the words that make it up; the tag of a struct written inline comes back separately.</summary>
    private CType TypeName(out string? structTag)
    {
        structTag = null;
        var words = new List<string>();

        while (!AtEnd)
        {
            if (Current.Kind == CTokenKind.Keyword && Ignored.Contains(Current.Text))
            {
                _at++;
                continue;
            }

            if (Is("struct") || Is("union") || Is("class"))
            {
                _at++;
                if (Current.Kind == CTokenKind.Identifier)
                {
                    var tag = Take().Text;
                    words.Add(tag);
                    _typedefs.Add(tag);
                    if (Is("{")) structTag = tag;
                }
                else if (Is("{"))
                {
                    structTag = $"struct at line {Current.Line}";
                    words.Add(structTag);
                }

                continue;
            }

            if (Current.Kind == CTokenKind.Keyword && TypeWords.Contains(Current.Text))
            {
                words.Add(Take().Text);
                continue;
            }

            if (Current.Kind == CTokenKind.Identifier && words.Count == 0)
            {
                words.Add(Take().Text);
                if (cpp && Is("::"))
                {
                    _at++;
                    if (Current.Kind == CTokenKind.Identifier) words[^1] = Take().Text;
                }

                if (cpp && Is("<")) SkipAngles();
                continue;
            }

            break;
        }

        return new CType(Named(words), 0, []);
    }

    /// <summary>What the IR calls a C type, so its range and kind are known: unsigned char is a byte, size_t a large whole number.</summary>
    private static string Named(IReadOnlyList<string> words)
    {
        if (words.Count == 0) return "?";

        var text = string.Join(" ", words);
        var unsigned = words.Contains("unsigned");

        return text switch
        {
            _ when text.Contains("double") || text.Contains("float") => "double",
            _ when text.Contains("bool") || text.Contains("_Bool") => "bool",
            "void" => "void",
            _ when text.Contains("char") => unsigned ? "byte" : "sbyte",
            _ when text.Contains("short") => unsigned ? "ushort" : "short",
            _ when text.Contains("long") => unsigned ? "ulong" : "long",
            _ when text.Contains("int") => unsigned ? "uint" : "int",
            "size_t" or "uintptr_t" or "uint64_t" => "ulong",
            "ssize_t" or "ptrdiff_t" or "int64_t" => "long",
            "int32_t" => "int",
            "uint32_t" => "uint",
            "int16_t" => "short",
            "uint16_t" => "ushort",
            "int8_t" => "sbyte",
            "uint8_t" => "byte",
            "string" or "wstring" => "string",
            "vector" or "list" or "deque" or "array" => "list",
            "map" or "unordered_map" => "dict",
            "set" or "unordered_set" => "set",
            _ => words[^1],
        };
    }

    private int Stars()
    {
        var stars = 0;
        while (Is("*") || Is("&") || Current.Kind == CTokenKind.Keyword && Ignored.Contains(Current.Text))
        {
            if (Is("*")) stars++;
            _at++;
        }

        return stars;
    }

    /// <summary>Whether what follows is a name wrapped for a function pointer: (*name)(...).</summary>
    private bool FunctionPointer()
    {
        if (!Is("(") || !IsAhead(1, "*")) return false;

        _at += 2;
        while (Is("*")) _at++;
        return true;
    }

    private List<Expr?> Dimensions()
    {
        var dimensions = new List<Expr?>();
        while (Is("["))
        {
            _at++;
            dimensions.Add(Is("]") ? null : Expression());
            Expect("]");
        }

        return dimensions;
    }

    private List<IrParameter> Parameters()
    {
        var parameters = new List<IrParameter>();
        Expect("(");

        while (!Is(")") && !AtEnd && Reading())
        {
            var mark = _at;
            var start = Current;

            if (Eat("..."))
            {
                parameters.Add(new IrParameter(Span(start), "...", IrType.Unknown, null, ParameterKind.Rest));
                break;
            }

            if (!StartsType() && Current.Kind != CTokenKind.Identifier)
            {
                _at++;
                continue;
            }

            var type = TypeName(out _);
            var stars = Stars();
            if (FunctionPointer()) stars++;
            string? name = null;

            if (Current.Kind is CTokenKind.Identifier) name = Take().Text;
            if (stars > 0 && Is(")") && IsAhead(1, "(")) SkipBalanced();

            var dimensions = Dimensions();
            var whole = type with { Pointers = stars + (dimensions.Count > 0 ? 1 : 0), Dimensions = [] };

            if (Eat("=")) Assignment();

            if (name is not null) parameters.Add(new IrParameter(From(start), name, whole.Ir));
            else if (type.Name != "void") parameters.Add(new IrParameter(From(start), $"_{parameters.Count}", whole.Ir));

            if (_at == mark) _at++;
            if (!Eat(",")) break;
        }

        Expect(")");
        return parameters;
    }

    /// <summary>Skips the brackets that finish a function pointer's shape.</summary>
    private void SkipBalanced()
    {
        _at++;
        var depth = 0;
        while (!AtEnd)
        {
            if (Is("(")) depth++;
            else if (Is(")"))
            {
                depth--;
                _at++;
                if (depth == 0) return;
                continue;
            }

            _at++;
        }
    }

    private IrFunction ReadFunction(CToken start, string name, string? owner, CType returns, List<IrParameter> parameters, bool add = true)
    {
        _scopes.Add(new Dictionary<string, CType>(StringComparer.Ordinal));
        foreach (var parameter in parameters) _scopes[^1][parameter.Name] = new CType(parameter.Type.Name.TrimEnd('*'), parameter.Type.Name.Count(c => c == '*'), []);

        var outer = _owner;
        _owner = owner;
        _hasGoto = ContainsGoto();

        var body = Block();

        _owner = outer;
        _scopes.RemoveAt(_scopes.Count - 1);

        var function = new IrFunction(From(start), name, owner, parameters, returns.Ir, body) { IsStatic = owner is null };
        if (add) _functions.Add(function);
        return function;
    }

    /// <summary>Whether the body that starts here jumps with goto, which is read the way C# reads it: the path ends and the label forgets.</summary>
    private bool ContainsGoto()
    {
        var depth = 0;
        for (var i = _at; i < tokens.Count; i++)
        {
            if (tokens[i].Text == "{" && tokens[i].Kind == CTokenKind.Punctuator) depth++;
            else if (tokens[i].Text == "}" && tokens[i].Kind == CTokenKind.Punctuator)
            {
                depth--;
                if (depth == 0) return false;
            }
            else if (tokens[i].Kind == CTokenKind.Keyword && tokens[i].Text == "goto")
            {
                return true;
            }
        }

        return false;
    }

    // ---- Statements ----

    private List<Stmt> Block() => InScope(() =>
    {
        var body = new List<Stmt>();
        if (!Eat("{")) return Statement().ToList();

        while (!Is("}") && !AtEnd && Reading()) body.AddRange(Statement());
        Expect("}");
        return body;
    });

    private IEnumerable<Stmt> Statement()
    {
        if (_depth > 150)
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
                return [new While(span, test, Loop(), [])];

            case "do":
                _at++;
                var body = Loop();
                Expect("while");
                Expect("(");
                var doTest = Expression();
                Expect(")");
                Eat(";");
                return [new While(span, doTest, body, [], TestsFirst: false)];

            case "for":
                _at++;
                return ForStatement(span);

            case "switch":
                _at++;
                return SwitchStatement(span);

            case "return":
                _at++;
                var returned = Is(";") ? null : Expression();
                Eat(";");
                return [new Return(span, returned)];

            case "break":
                _at++;
                Eat(";");
                return [new Break(span, _breaks.Count > 0 ? _breaks[^1] : null)];

            case "continue":
                _at++;
                Eat(";");
                return [new Continue(span, null)];

            case "goto":
                _at++;
                if (Current.Kind == CTokenKind.Identifier) _at++;
                Eat(";");
                return [new Return(span, null)];

            case "throw" when cpp:
                _at++;
                var thrown = Is(";") ? null : Expression();
                Eat(";");
                return [new Throw(span, thrown)];

            case "try" when cpp:
                _at++;
                var attempted = Block();
                var handlers = new List<Handler>();
                while (Eat("catch"))
                {
                    var caught = Span(Current);
                    string? variable = null;
                    if (Eat("("))
                    {
                        if (StartsType()) TypeName(out _);
                        Stars();
                        if (Current.Kind == CTokenKind.Identifier) variable = Take().Text;
                        SkipTo(")");
                    }

                    handlers.Add(new Handler(caught, [], variable, Block()));
                }

                return [new Try(span, attempted, handlers, [], [])];

            case "delete" when cpp:
                _at++;
                if (Is("[")) { _at++; Expect("]"); }
                var freed = Expression();
                Eat(";");
                return [new Evaluate(span, new Call(From(start), new Name(span, "delete"), [new Argument(null, freed)]))];

            case "case":
                _at++;
                var label = Expression();
                Expect(":");
                return [new OpaqueStmt(span, "case", [], [label])];

            case "default" when IsAhead(1, ":"):
                _at += 2;
                return [];
        }

        // A label: a name followed by a colon.
        if (start.Kind == CTokenKind.Identifier && IsAhead(1, ":") && !IsAhead(1, "::"))
        {
            _at += 2;
            List<Stmt> forgets = _hasGoto ? [new OpaqueStmt(span, "goto target", _scopes.SelectMany(s => s.Keys).Distinct().ToList(), [])] : [];
            return [.. forgets, new Labeled(span, start.Text, Statement().ToList())];
        }

        if (StartsType() && !(Current.Kind == CTokenKind.Identifier && IsAhead(1, "("))) return LocalDeclaration(span);

        var expression = Expression();
        Eat(";");
        return [new Evaluate(From(start), expression)];
    }

    private List<Stmt> Loop()
    {
        _breaks.Add(null);
        try
        {
            return Block();
        }
        finally
        {
            _breaks.RemoveAt(_breaks.Count - 1);
        }
    }

    private List<Stmt> LocalDeclaration(SourceSpan span)
    {
        var type = TypeName(out var tag);
        if (tag is not null) ReadStructBody(tag);

        var statements = new List<Stmt>();

        while (!AtEnd && Reading())
        {
            var mark = _at;
            var stars = Stars();
            var holdsFunction = FunctionPointer();
            if (holdsFunction) stars++;
            if (Current.Kind is not (CTokenKind.Identifier or CTokenKind.Keyword)) break;

            var nameToken = Take();

            if (holdsFunction)
            {
                // The rest of a function pointer's shape - ) (int, char *) - says nothing about the variable itself.
                while (!Is(";") && !Is(",") && !AtEnd && Reading()) _at++;
                statements.Add(new Declare(From(nameToken), Declare(nameToken.Text, type with { Pointers = stars }), IrType.Named("func"), null));
                if (!Eat(",")) break;
                continue;
            }
            var dimensions = Dimensions();
            var variable = type with { Pointers = stars, Dimensions = dimensions };
            var name = Declare(nameToken.Text, variable);

            Expr? initial = null;
            if (Eat("=")) initial = Initialiser(variable, Span(nameToken));
            else if (Is("(") && cpp)
            {
                var arguments = Arguments();
                initial = new NewObject(From(nameToken), IrType.Named(variable.Name), arguments);
            }
            else if (dimensions.Count > 0)
            {
                initial = new NewObject(From(nameToken), IrType.Named("array"), dimensions[0] is { } size ? [new Argument(null, size)] : []);
            }

            statements.Add(new Declare(From(nameToken), name, variable.Ir, initial));
            if (_at == mark) _at++;
            if (!Eat(",")) break;
        }

        Eat(";");
        return statements;
    }

    /// <summary>What a variable starts with: a value, or a list of values in braces.</summary>
    private Expr Initialiser(CType type, SourceSpan span)
    {
        if (!Is("{")) return Assignment();

        var start = Take();
        var items = new List<Expr>();

        while (!Is("}") && !AtEnd && Reading())
        {
            var mark = _at;
            if (Eat(",")) continue;
            if (Is(".") || Is("["))
            {
                // A field or position named in the braces: .x = 1 or [2] = 3.
                while (!Is(",") && !Is("}") && !AtEnd) _at++;
                continue;
            }

            items.Add(Initialiser(type with { Dimensions = [] }, span));
            if (_at == mark) _at++;
            if (!Eat(",")) break;
        }

        Expect("}");
        return type.Dimensions.Count > 0 || type.IsPointer
            ? new CollectionLiteral(From(start), CollectionKind.Array, items)
            : new NewObject(From(start), IrType.Named(type.Name), items.Select(i => new Argument(null, i)).ToList());
    }

    private IEnumerable<Stmt> ForStatement(SourceSpan span) => InScope(() =>
    {
        Expect("(");

        // C++ writes for (auto x : items) to walk a collection.
        var save = _at;
        if (cpp && StartsType())
        {
            TypeName(out _);
            Stars();
            if (Current.Kind == CTokenKind.Identifier && IsAhead(1, ":"))
            {
                var item = Take();
                var name = Declare(item.Text, CType.Unknown);
                _at++;
                var items = Expression();
                Expect(")");
                return [new ForEach(span, new Name(Span(item), name), items, Loop(), [])];
            }

            _at = save;
        }

        var setup = Is(";") ? [] : StartsType() ? LocalDeclaration(span) : [new Evaluate(span, Expression())];
        if (setup.Count > 0 && setup[^1] is Evaluate) Eat(";");
        else if (setup.Count == 0) Expect(";");

        var condition = Is(";") ? null : Expression();
        Expect(";");
        var step = Is(")") ? new List<Stmt>() : [new Evaluate(Span(Current), Expression())];
        Expect(")");

        return [new For(span, setup, condition, step, Loop())];
    });

    private IEnumerable<Stmt> SwitchStatement(SourceSpan span)
    {
        Expect("(");
        var subject = Expression();
        Expect(")");

        _breaks.Add(null);
        try
        {
            var body = Block();
            var cases = new List<SwitchCase>();
            var labels = new List<Expr>();
            var current = new List<Stmt>();
            var started = false;

            foreach (var statement in body)
            {
                if (statement is OpaqueStmt { What: "case", Parts: [var label] })
                {
                    if (started && current.Count > 0)
                    {
                        cases.Add(new SwitchCase(labels, current, FallsThrough: true));
                        labels = [];
                        current = [];
                    }

                    labels.Add(label);
                    started = true;
                    continue;
                }

                current.Add(statement);
            }

            if (started || current.Count > 0) cases.Add(new SwitchCase(labels, current, FallsThrough: false));
            return [new Switch(span, subject, cases)];
        }
        finally
        {
            _breaks.RemoveAt(_breaks.Count - 1);
        }
    }

    // ---- Expressions ----

    private Expr Expression()
    {
        var value = Assignment();

        while (Is(","))
        {
            var span = Span(Take());
            value = Opaque.Of(span, "one value then another", value, Assignment());
        }

        return value;
    }

    private Expr Assignment()
    {
        var start = Current;
        var target = Conditional();

        if (Current.Kind == CTokenKind.Punctuator && Current.Text is "=" or "+=" or "-=" or "*=" or "/=" or "%=" or "&=" or "|=" or "^=" or "<<=" or ">>=")
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
                "&=" => BinaryOperator.BitAnd,
                "|=" => BinaryOperator.BitOr,
                "^=" => BinaryOperator.BitXor,
                "<<=" => BinaryOperator.ShiftLeft,
                _ => BinaryOperator.ShiftRight,
            };

            return new AssignValue(span, target, new Binary(span, compound, target, value));
        }

        return target;
    }

    private Expr Conditional()
    {
        var start = Current;
        var test = Chain(0);

        if (!Eat("?")) return test;

        var whenTrue = Is(":") ? test : Expression();
        Expect(":");
        var whenFalse = Conditional();
        return new Conditional(From(start), test, whenTrue, whenFalse);
    }

    private static readonly string[][] Levels =
    [
        ["||"],
        ["&&"],
        ["|"],
        ["^"],
        ["&"],
        ["==", "!="],
        ["<", ">", "<=", ">="],
        ["<<", ">>"],
        ["+", "-"],
        ["*", "/", "%"],
    ];

    private Expr Chain(int level)
    {
        if (level >= Levels.Length) return Unary();

        var start = Current;
        var left = Chain(level + 1);

        while (Current.Kind == CTokenKind.Punctuator && Levels[level].Contains(Current.Text))
        {
            var op = Take().Text;
            var right = Chain(level + 1);
            var span = From(start);

            var known = op switch
            {
                "||" => BinaryOperator.Or,
                "&&" => BinaryOperator.And,
                "|" => BinaryOperator.BitOr,
                "^" => BinaryOperator.BitXor,
                "&" => BinaryOperator.BitAnd,
                "==" => BinaryOperator.Equal,
                "!=" => BinaryOperator.NotEqual,
                "<" => BinaryOperator.Less,
                ">" => BinaryOperator.Greater,
                "<=" => BinaryOperator.LessOrEqual,
                ">=" => BinaryOperator.GreaterOrEqual,
                "<<" => BinaryOperator.ShiftLeft,
                ">>" => BinaryOperator.ShiftRight,
                "+" => BinaryOperator.Add,
                "-" => BinaryOperator.Subtract,
                "*" => BinaryOperator.Multiply,
                "/" => BinaryOperator.Divide,
                _ => BinaryOperator.Modulo,
            };

            left = new Binary(span, known, left, right);
        }

        return left;
    }

    private Expr Unary()
    {
        var start = Current;

        switch (Current.Text)
        {
            case "!" when Current.Kind == CTokenKind.Punctuator:
                _at++;
                return new Unary(From(start), UnaryOperator.Not, Unary());
            case "-" when Current.Kind == CTokenKind.Punctuator:
                _at++;
                return new Unary(From(start), UnaryOperator.Negate, Unary());
            case "+" when Current.Kind == CTokenKind.Punctuator:
                _at++;
                return new Unary(From(start), UnaryOperator.Plus, Unary());
            case "~" when Current.Kind == CTokenKind.Punctuator:
                _at++;
                return new Unary(From(start), UnaryOperator.BitNot, Unary());
            case "*" when Current.Kind == CTokenKind.Punctuator:
                _at++;
                return new Member(From(start), Unary(), "*");
            case "&" when Current.Kind == CTokenKind.Punctuator:
                _at++;
                var addressed = Unary();
                return Opaque.Of(From(start), "address of", addressed);
            case "++" or "--" when Current.Kind == CTokenKind.Punctuator:
                var step = Take().Text;
                var operand = Unary();
                var stepSpan = From(start);
                return new AssignValue(stepSpan, operand,
                    new Binary(stepSpan, step == "++" ? BinaryOperator.Add : BinaryOperator.Subtract, operand, new Literal(stepSpan, LiteralKind.Integer, 1L)));
            case "sizeof":
                _at++;
                if (Is("("))
                {
                    var depth = 0;
                    while (!AtEnd)
                    {
                        if (Is("(")) depth++;
                        else if (Is(")"))
                        {
                            depth--;
                            _at++;
                            if (depth == 0) break;
                            continue;
                        }

                        _at++;
                    }
                }
                else
                {
                    Unary();
                }

                return Opaque.Of(From(start), "a size");
            case "new" when cpp:
                _at++;
                var made = TypeName(out _);
                Stars();
                if (Is("["))
                {
                    _at++;
                    var size = Is("]") ? null : Expression();
                    Expect("]");
                    return new NewObject(From(start), IrType.Named("array"), size is null ? [] : [new Argument(null, size)]);
                }

                var arguments = Is("(") ? Arguments() : [];
                if (Is("{")) SkipBlock();
                return Following(new NewObject(From(start), IrType.Named(made.Name), arguments), start);
            case "delete" when cpp:
                _at++;
                if (Is("[")) { _at++; Expect("]"); }
                return new Call(From(start), new Name(Span(start), "delete"), [new Argument(null, Unary())]);
            case "static_cast" or "dynamic_cast" or "const_cast" or "reinterpret_cast" when cpp:
                _at++;
                SkipAngles();
                var cast = Is("(") ? Arguments() : [];
                return cast.Count == 1 ? cast[0].Value : Opaque.Of(From(start), "a cast");
        }

        // A cast: (type) value, where the type may spell out a whole shape such as void(*)(int).
        if (Is("(") && IsCastAhead())
        {
            _at++;
            var type = TypeName(out _);
            var stars = Stars();
            var shaped = !Is(")");
            while (!Is(")") && !AtEnd && Reading()) _at++;
            Expect(")");
            var value = Unary();
            return stars > 0 || shaped ? Opaque.Of(From(start), "a pointer", value) : new Cast(From(start), type.Ir, value);
        }

        return Postfix();
    }

    /// <summary>Whether the bracket here holds a type, which makes what follows a cast rather than a value in brackets.</summary>
    private bool IsCastAhead()
    {
        var at = _at + 1;
        if (at >= tokens.Count) return false;

        var token = tokens[at];
        var isType = token.Kind == CTokenKind.Keyword && TypeWords.Contains(token.Text) && token.Text is not ("sizeof" or "new" or "delete")
                     || token.Kind == CTokenKind.Identifier && _typedefs.Contains(token.Text);
        if (!isType) return false;

        // Find the closing bracket; a cast is followed by something to convert.
        var depth = 1;
        for (var i = at; i < tokens.Count; i++)
        {
            if (tokens[i].Text == "(" && tokens[i].Kind == CTokenKind.Punctuator) depth++;
            else if (tokens[i].Text == ")" && tokens[i].Kind == CTokenKind.Punctuator)
            {
                depth--;
                if (depth == 0)
                {
                    var next = i + 1 < tokens.Count ? tokens[i + 1] : null;
                    return next is not null && next.Kind != CTokenKind.Punctuator ||
                           next is { Kind: CTokenKind.Punctuator } && next.Text is "(" or "*" or "&" or "-" or "!" or "~";
                }
            }
        }

        return false;
    }

    private Expr Postfix()
    {
        var start = Current;
        return Following(Primary(), start);
    }

    private Expr Following(Expr value, CToken start)
    {
        while (true)
        {
            if (Is("."))
            {
                _at++;
                var name = Current.Kind is CTokenKind.Identifier or CTokenKind.Keyword ? Take().Text : "?";
                value = new Member(From(start), value, name);
                continue;
            }

            if (Is("->"))
            {
                _at++;
                var name = Current.Kind is CTokenKind.Identifier or CTokenKind.Keyword ? Take().Text : "?";
                value = new Member(From(start), value, name);
                continue;
            }

            if (Is("["))
            {
                _at++;
                var key = Expression();
                Expect("]");
                value = new ElementAccess(From(start), value, key);
                continue;
            }

            if (Is("("))
            {
                value = new Call(From(start), value, Arguments());
                continue;
            }

            if (Is("++") || Is("--"))
            {
                var step = Take().Text;
                var span = From(start);
                value = new AssignValue(span, value,
                    new Binary(span, step == "++" ? BinaryOperator.Add : BinaryOperator.Subtract, value, new Literal(span, LiteralKind.Integer, 1L)),
                    ValueBeforeAssigning: true);
                continue;
            }

            if (cpp && Is("::"))
            {
                _at++;
                var name = Current.Kind is CTokenKind.Identifier ? Take().Text : "?";
                value = new Member(From(start), value, name);
                continue;
            }

            return value;
        }
    }

    private List<Argument> Arguments()
    {
        var arguments = new List<Argument>();
        Expect("(");

        while (!Is(")") && !AtEnd && Reading())
        {
            var mark = _at;
            arguments.Add(new Argument(null, Assignment()));
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
            case CTokenKind.Number:
                _at++;
                return start.Value switch
                {
                    long whole => new Literal(span, LiteralKind.Integer, whole),
                    double real => new Literal(span, LiteralKind.Real, real),
                    _ => Opaque.Of(span, "a number"),
                };

            case CTokenKind.Text:
                _at++;
                var text = start.Value as string ?? "";
                while (Current.Kind == CTokenKind.Text) text += Take().Value as string ?? "";
                return new Literal(From(start), LiteralKind.Text, text);

            case CTokenKind.Character:
                _at++;
                return new Literal(span, LiteralKind.Integer, start.Value is long code ? code : 0L);

            case CTokenKind.Identifier:
                _at++;
                if (_constants.TryGetValue(start.Text, out var constant)) return new Literal(span, LiteralKind.Integer, constant);
                return new Name(span, Known(start.Text) is not null ? start.Text : start.Text);
        }

        switch (start.Text)
        {
            case "(":
                _at++;
                var inner = Expression();
                Expect(")");
                return inner;

            case "{":
                return Initialiser(CType.Unknown with { Dimensions = [null] }, span);

            case "true":
                _at++;
                return new Literal(span, LiteralKind.Boolean, true);

            case "false":
                _at++;
                return new Literal(span, LiteralKind.Boolean, false);

            case "nullptr" or "NULL":
                _at++;
                return new Literal(span, LiteralKind.Null, null);

            case "this":
                _at++;
                return new Name(span, "this");

            case "sizeof" or "new" or "delete" or "static_cast" or "dynamic_cast" or "const_cast" or "reinterpret_cast":
                return Unary();
        }

        if (start.Kind == CTokenKind.Keyword)
        {
            _at++;
            return new Name(span, start.Text);
        }

        Problem ??= $"line {start.Line}: FixFinder cannot read `{start.Text}` here";
        _at++;
        return Opaque.Of(span, "value");
    }
}
