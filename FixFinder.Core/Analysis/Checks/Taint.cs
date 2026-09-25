using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Taint: text the person running the program controls - what they type, the program's arguments, its environment -
/// followed through the program to the places where text becomes something that runs: eval and exec, a shell command,
/// a SQL query. Each function's summary says what its return value carries of its parameters, and which of its
/// parameters reach such a place inside it, so the flow is followed across calls.
/// </summary>
/// <remarks>
/// <para>
/// Taint passes through assignment, joining and formatting text, indexing it, and the text methods that keep what they
/// are given - strip(), lower(), replace(). Turning text into a number ends it: int("1; DROP TABLE") fails rather than
/// injecting anything. A query given its values separately - execute("... WHERE name = ?", (name,)) - is safe however
/// tainted the values are, because the database never reads them as SQL; only the query's own text is checked.
/// </para>
/// <para>
/// What code FixFinder cannot see returns is not assumed to carry taint - a finding needs a flow the code plainly shows.
/// </para>
/// </remarks>
internal sealed class Taint(IrProgram program, CallTargets targets)
{
    public const string CodeRule = "analysis-code-injection";
    public const string CommandRule = "analysis-command-injection";
    public const string SqlRule = "analysis-sql-injection";

    /// <summary>How many times summaries are worked out again from each other's; enough for a chain of helpers.</summary>
    private const int Rounds = 4;

    /// <summary>
    /// Where tainted text came from: a source - what was typed, the arguments - or, while a function is summarised,
    /// one of its own parameters.
    /// </summary>
    private sealed record Origin(string Description, SourceSpan At, int Parameter = -1)
    {
        public bool IsParameter => Parameter >= 0;
    }

    /// <summary>
    /// A place where text becomes something that runs, reached by tainted text: the call, and - when the text gets there
    /// through a function of the program's own - that function and the line inside it. <paramref name="Doing"/> says what
    /// the call does with the text, where that is not simply running it.
    /// </summary>
    private sealed record Sink(string Rule, SourceSpan At, Call Call, string? Through = null, int ThroughLine = 0, string? Doing = null);

    /// <summary>
    /// What calling a function does with tainted text: whether what it returns is tainted whatever it is given, which of
    /// its parameters flow into what it returns, and which of them reach a sink inside it.
    /// </summary>
    private sealed class Summary
    {
        public Origin? ReturnsTainted { get; set; }

        public HashSet<int> ReturnsParameters { get; } = [];

        public Dictionary<int, Sink> ParametersReachSinks { get; } = [];
    }

    private static readonly HashSet<string> NumberMakers = new(StringComparer.Ordinal)
    {
        "int", "float", "len", "abs", "round", "bool", "ord", "hash", "id", "parseInt", "parseDouble", "parseLong", "parseFloat", "valueOf",
        "Parse", "TryParse", "ToInt32", "ToInt64", "ToDouble", "ToDecimal", "ToBoolean", "size", "length", "Length", "Count", "isdigit", "isnumeric",
    };

    private static readonly HashSet<string> TextKeepers = new(StringComparer.Ordinal)
    {
        "strip", "lstrip", "rstrip", "lower", "upper", "title", "capitalize", "casefold", "swapcase", "replace", "format", "join", "center", "ljust",
        "rjust", "zfill", "trim", "toLowerCase", "toUpperCase", "concat", "substring", "Trim", "TrimStart", "TrimEnd", "ToLower", "ToUpper",
        "ToLowerInvariant", "ToUpperInvariant", "Replace", "Substring", "Insert", "PadLeft", "PadRight", "ToString", "toString", "Format", "Concat", "Join",
    };

    private readonly Dictionary<IrFunction, Summary> _summaries = new(ReferenceEqualityComparer.Instance);
    private readonly List<AnalysisFinding> _findings = [];
    private readonly HashSet<SourceSpan> _reported = [];

    private SourceLanguage Language => program.Language;

    private bool IsPython => Language == SourceLanguage.Python;

    public IReadOnlyList<AnalysisFinding> Check()
    {
        if (Language is not (SourceLanguage.Python or SourceLanguage.Java or SourceLanguage.CSharp)) return [];

        var graphs = new Dictionary<IrFunction, ControlFlowGraph>(ReferenceEqualityComparer.Instance);
        foreach (var function in program.AllFunctions)
        {
            graphs[function] = CfgBuilder.Build(function);
            _summaries[function] = new Summary();
        }

        for (var round = 0; round < Rounds; round++)
        {
            var changed = false;
            foreach (var function in program.AllFunctions) changed |= Follow(function, graphs[function], reporting: false);
            if (!changed) break;
        }

        foreach (var function in program.AllFunctions) Follow(function, graphs[function], reporting: true);
        return _findings;
    }

    /// <summary>
    /// Follows tainted text forward through one function. Its parameters carry their own markers, so what reaches its
    /// return and its sinks can be put in its summary; the program's own arguments, and each source, are real taint.
    /// </summary>
    private bool Follow(IrFunction function, ControlFlowGraph graph, bool reporting)
    {
        var summary = _summaries[function];
        var before = (summary.ReturnsTainted, summary.ReturnsParameters.Count, summary.ParametersReachSinks.Count);

        var start = new Dictionary<string, HashSet<Origin>>(StringComparer.Ordinal);
        for (var index = 0; index < function.Parameters.Count; index++)
        {
            var parameter = function.Parameters[index];
            start[parameter.Name] = IsProgramArguments(function, parameter)
                ? [new Origin("from the program's arguments", parameter.Span)]
                : [new Origin($"`{parameter.Name}`", parameter.Span, index)];
        }

        var entry = new Dictionary<int, Dictionary<string, HashSet<Origin>>> { [graph.Entry] = start };
        var pending = new Queue<int>([graph.Entry]);
        var visits = 0;

        while (pending.Count > 0 && visits++ < graph.Blocks.Count * 8)
        {
            var block = graph.Blocks[pending.Dequeue()];
            var state = Copy(entry[block.Id]);

            foreach (var instruction in block.Instructions) Step(function, state, instruction, reporting);
            Ending(function, state, block, reporting);

            foreach (var next in graph.Successors(block.Id))
            {
                if (!entry.TryGetValue(next, out var known))
                {
                    entry[next] = Copy(state);
                    pending.Enqueue(next);
                }
                else if (Join(known, state))
                {
                    pending.Enqueue(next);
                }
            }
        }

        return (summary.ReturnsTainted, summary.ReturnsParameters.Count, summary.ParametersReachSinks.Count) != before;
    }

    /// <summary>main's String[] args in Java and C# - what the program was started with, which whoever starts it controls.</summary>
    private bool IsProgramArguments(IrFunction function, IrParameter parameter) =>
        !IsPython && function.IsStatic && function.Name is "main" or "Main" && function.Parameters.Count == 1 && parameter.Type.Name is "array" or "String[]" or "string[]";

    private static Dictionary<string, HashSet<Origin>> Copy(Dictionary<string, HashSet<Origin>> state) =>
        state.ToDictionary(pair => pair.Key, pair => new HashSet<Origin>(pair.Value), StringComparer.Ordinal);

    private static bool Join(Dictionary<string, HashSet<Origin>> into, Dictionary<string, HashSet<Origin>> from)
    {
        var grew = false;
        foreach (var (name, origins) in from)
        {
            if (!into.TryGetValue(name, out var mine)) into[name] = mine = [];
            foreach (var origin in origins) grew |= mine.Add(origin);
        }

        return grew;
    }

    private void Step(IrFunction function, Dictionary<string, HashSet<Origin>> state, Instruction instruction, bool reporting)
    {
        switch (instruction)
        {
            case AssignInstruction { Target: Name { Identifier: var name }, Value: var value }:
                Sinks(function, state, value, reporting);
                var carried = Carried(function, value, state);
                if (carried.Count > 0) state[name] = carried;
                else state.Remove(name);
                break;

            case AssignInstruction { Target: Member { MemberName: "CommandText" }, Value: var query } assign:
                Sinks(function, state, query, reporting);
                if (Carried(function, query, state) is { Count: > 0 } queried)
                    Reach(function, queried, new Sink(SqlRule, assign.Span, new Call(assign.Span, assign.Target, [new Argument(null, query)]), Doing: "makes a SQL command's text from"), reporting);
                break;

            case AssignInstruction assign:
                Sinks(function, state, assign.Value, reporting);
                break;

            case EvaluateInstruction evaluate:
                Sinks(function, state, evaluate.Value, reporting);
                break;

            case DeclareInstruction declare:
                state.Remove(declare.Variable);
                break;
        }
    }

    private void Ending(IrFunction function, Dictionary<string, HashSet<Origin>> state, BasicBlock block, bool reporting)
    {
        if (block.Terminator is Branch branch) Sinks(function, state, branch.Condition, reporting);
        if (block.Terminator is not Leave { Value: { } returned }) return;

        Sinks(function, state, returned, reporting);
        var summary = _summaries[function];

        foreach (var origin in Carried(function, returned, state))
        {
            if (origin.IsParameter) summary.ReturnsParameters.Add(origin.Parameter);
            else summary.ReturnsTainted ??= origin;
        }
    }

    /// <summary>The taint an expression carries: where the text it holds came from, when some of it came from outside.</summary>
    private HashSet<Origin> Carried(IrFunction function, Expr expression, IReadOnlyDictionary<string, HashSet<Origin>> state)
    {
        if (Source(expression) is { } source) return [source];

        switch (expression)
        {
            case Name { Identifier: var name }:
                return state.TryGetValue(name, out var origins) ? [.. origins] : [];

            case Call call when call.CalleeName is { } callee && NumberMakers.Contains(callee):
                return [];

            case Call { Callee: Name { Identifier: "str" or "repr" } } call when IsPython:
                return Union(function, call.Arguments.Select(a => a.Value), state);

            case Call { Callee: Member { Target: var receiver, MemberName: var method } } call when TextKeepers.Contains(method):
                return Union(function, [receiver, .. call.Arguments.Select(a => a.Value)], state);

            case Call call when targets.Resolve(call, function, IrWalk.LocalNames(function, assigningDeclares: IsPython)) is { } target &&
                                _summaries.TryGetValue(target.Function, out var summary):
                var returned = new HashSet<Origin>();
                if (summary.ReturnsTainted is { } always) returned.Add(always);
                foreach (var index in summary.ReturnsParameters)
                    if (Effects.ArgumentFor(call, target, index) is { } argument) returned.UnionWith(Carried(function, argument, state));
                return returned;

            case Call:
            case NewObject:
                return [];

            case Binary { Operator: BinaryOperator.Add } joined:
                return Union(function, [joined.Left, joined.Right], state);

            case Binary { Operator: BinaryOperator.Modulo, Left: Literal { Kind: LiteralKind.Text } } formatted when IsPython:
                return Carried(function, formatted.Right, state);

            case Binary:
            case Unary:
                return [];

            case Opaque { What: "formatted text" } text:
                return Union(function, text.Parts, state);

            case Conditional choice:
                return Union(function, [choice.WhenTrue, choice.WhenFalse], state);

            case ElementAccess element:
                return Carried(function, element.Target, state);

            case Slice slice:
                return Carried(function, slice.Target, state);

            case CollectionLiteral items:
                return Union(function, items.Items, state);

            case AssignValue assigned:
                return Carried(function, assigned.Value, state);

            default:
                return [];
        }
    }

    private HashSet<Origin> Union(IrFunction function, IEnumerable<Expr> parts, IReadOnlyDictionary<string, HashSet<Origin>> state)
    {
        var all = new HashSet<Origin>();
        foreach (var part in parts) all.UnionWith(Carried(function, part, state));
        return all;
    }

    /// <summary>Where text the person running the program controls comes in: what they type, the arguments, the environment.</summary>
    private Origin? Source(Expr expression) => expression switch
    {
        Call { Callee: Name { Identifier: "input" } } typed when IsPython => new Origin($"the user typed at line {typed.Span.Line}", typed.Span),
        Call { Callee: Member { Target: Member { Target: Name { Identifier: "sys" }, MemberName: "stdin" } } } read when IsPython =>
            new Origin($"read from the keyboard at line {read.Span.Line}", read.Span),
        Member { Target: Name { Identifier: "sys" }, MemberName: "argv" } arguments when IsPython => new Origin("from the program's arguments", arguments.Span),
        Call { Callee: Member { Target: Name { Identifier: "os" }, MemberName: "getenv" } } environment when IsPython =>
            new Origin($"from the environment, read at line {environment.Span.Line}", environment.Span),
        Member { Target: Name { Identifier: "os" }, MemberName: "environ" } environment when IsPython =>
            new Origin($"from the environment, read at line {environment.Span.Line}", environment.Span),
        Call { Callee: Member { MemberName: "nextLine" or "next" or "readLine" }, Arguments.Count: 0 } read when !IsPython =>
            new Origin($"read in at line {read.Span.Line}", read.Span),
        Call { Callee: Member { Target: Name { Identifier: "Console" }, MemberName: "ReadLine" } } typed =>
            new Origin($"the user typed at line {typed.Span.Line}", typed.Span),
        Call { Callee: Member { Target: Name { Identifier: "System" }, MemberName: "getenv" } } environment =>
            new Origin($"from the environment, read at line {environment.Span.Line}", environment.Span),
        Call { Callee: Member { Target: Name { Identifier: "Environment" }, MemberName: "GetEnvironmentVariable" } } environment =>
            new Origin($"from the environment, read at line {environment.Span.Line}", environment.Span),
        _ => null,
    };

    /// <summary>Every place in an expression where text becomes something that runs, checked for the taint that reaches it.</summary>
    private void Sinks(IrFunction function, IReadOnlyDictionary<string, HashSet<Origin>> state, Expr expression, bool reporting)
    {
        foreach (var call in Effects.CallsIn(expression))
        {
            if (SinkOf(call) is { } sink)
            {
                var carried = Carried(function, sink.Call.Arguments[0].Value, state);
                if (carried.Count > 0) Reach(function, carried, sink, reporting);
            }

            // A function of the program's own that passes one of its parameters on to a sink: tainted text given for it gets there too.
            if (targets.Resolve(call, function, IrWalk.LocalNames(function, assigningDeclares: IsPython)) is { } target &&
                _summaries.TryGetValue(target.Function, out var summary))
            {
                foreach (var (index, inside) in summary.ParametersReachSinks)
                {
                    if (Effects.ArgumentFor(call, target, index) is not { } argument) continue;

                    var carried = Carried(function, argument, state);
                    if (carried.Count == 0) continue;

                    Reach(function, carried, new Sink(inside.Rule, call.Span, call, target.Function.Name, inside.At.Line), reporting);
                }
            }
        }

        foreach (var made in NewObjectsIn(expression))
        {
            if (made is { Type.Name: var type, Arguments: [{ Value: var query }, ..] } && type.EndsWith("Command", StringComparison.Ordinal) && type != "Command" &&
                Carried(function, query, state) is { Count: > 0 } carried)
            {
                Reach(function, carried, new Sink(SqlRule, made.Span, new Call(made.Span, new Name(made.Span, type), made.Arguments), Doing: $"makes a {type} from"), reporting);
            }
        }
    }

    private static IEnumerable<NewObject> NewObjectsIn(Expr expression) =>
        (expression is NewObject made ? [made] : Enumerable.Empty<NewObject>()).Concat(IrWalk.Children(expression).SelectMany(NewObjectsIn));

    /// <summary>Tainted text reaching a sink: in a summary, the parameters that got there; reporting, the real sources.</summary>
    private void Reach(IrFunction function, HashSet<Origin> carried, Sink sink, bool reporting)
    {
        foreach (var origin in carried.Where(o => o.IsParameter)) _summaries[function].ParametersReachSinks.TryAdd(origin.Parameter, sink);

        if (!reporting || carried.FirstOrDefault(o => !o.IsParameter) is not { } source || !_reported.Add(sink.At)) return;

        _findings.Add(new AnalysisFinding(sink.Rule, sink.At, Message(sink, source), Severity.Warning, Confidence.Likely, FindingKind.Logic, AbstractChecks.FoundBy));
    }

    /// <summary>
    /// A call where text becomes something that runs, with its first argument - the text that does: eval and exec, a shell
    /// command, a SQL query's own text. A query's separate values are its second argument, and are safe.
    /// </summary>
    private Sink? SinkOf(Call call)
    {
        if (call.Arguments.Count == 0) return null;

        var rule = call switch
        {
            { Callee: Name { Identifier: "eval" or "exec" } } when IsPython => CodeRule,
            { Callee: Member { Target: Name { Identifier: "os" }, MemberName: "system" or "popen" } } when IsPython => CommandRule,
            { Callee: Member { Target: Name { Identifier: "subprocess" } } } when IsPython && call.Arguments.Any(a => a is { Name: "shell", Value: Literal { Value: true } }) =>
                CommandRule,
            { Callee: Member { MemberName: "execute" or "executemany" or "executescript" } } when IsPython => SqlRule,
            { Callee: Member { MemberName: "exec" } } when Language == SourceLanguage.Java => CommandRule,
            { Callee: Member { MemberName: "executeQuery" or "executeUpdate" or "execute" or "addBatch" or "prepareStatement" or "prepareCall" } }
                when Language == SourceLanguage.Java => SqlRule,
            { Callee: Member { Target: Name { Identifier: "Process" }, MemberName: "Start" } } when Language == SourceLanguage.CSharp => CommandRule,
            _ => null,
        };

        return rule is null ? null : new Sink(rule, call.Span, call);
    }

    private string Message(Sink sink, Origin source)
    {
        // The line says which call; the callee says what it does - the whole call, rebuilt from the program's own form, is often long.
        var call = $"{IrText.Of(sink.Call.Callee)}(...)";
        var runs = sink.Rule switch
        {
            CodeRule => "as Python code",
            CommandRule => IsPython ? "as a shell command" : "as a command",
            _ => "as SQL",
        };

        var what = sink.Through is { } helper
            ? $"`{call}` passes text {source.Description} to `{helper}`, which runs it {runs} at line {sink.ThroughLine}"
            : sink.Doing is { } doing
                ? $"`{call}` {doing} text {source.Description}"
                : $"`{call}` runs, {runs}, text {source.Description}";

        return sink.Rule switch
        {
            CodeRule => $"{what} - so whatever is typed runs, with every permission the program has. For a number use int() or float(); for a Python literal, ast.literal_eval()",
            CommandRule => Language switch
            {
                SourceLanguage.Python => $"{what} - so typing `;` and another command runs that too. Pass the command as a list to subprocess.run, without shell=True",
                SourceLanguage.Java => $"{what} - so that text becomes part of the command line, and can add arguments of its own choosing. " +
                    "Pass the command and each argument separately, as a String[] or to a ProcessBuilder",
                _ => $"{what} - so that text chooses what runs. Check it against the commands the program means to run before starting one",
            },
            _ => $"{what} - so typing `' OR '1'='1` changes what the query does (SQL injection). " +
                (IsPython
                    ? "Pass the values separately: execute(\"SELECT ... WHERE name = ?\", (name,))"
                    : Language == SourceLanguage.Java
                        ? "Use a PreparedStatement with ? placeholders, and set the values with setString"
                        : "Use parameters: command.Parameters.AddWithValue(\"@name\", name)"),
        };
    }
}
