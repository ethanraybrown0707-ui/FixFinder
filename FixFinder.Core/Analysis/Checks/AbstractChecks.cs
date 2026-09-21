using FixFinder.Core.Analysis.Abstract;
using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Slicing;
using FixFinder.Core.Analysis.Symbolic;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>Contradictions and unsafe operations, found by running each function on abstract values.</summary>
public static class AbstractChecks
{
    public const string FoundBy = "abstract interpretation";

    /// <summary>How long following paths may add to checking one program; functions past it keep what abstract interpretation found.</summary>
    private static readonly TimeSpan SymbolicBudget = TimeSpan.FromSeconds(5);

    /// <summary>How many times the functions' summaries are worked out again from each other's; enough for a chain of calls.</summary>
    private const int SummaryRounds = 3;

    public static IReadOnlyList<AnalysisFinding> Run(IrProgram program, SourceText source)
    {
        var findings = new List<AnalysisFinding>();
        var symbolic = System.Diagnostics.Stopwatch.StartNew();
        var targets = new CallTargets(program);
        var summaries = new Dictionary<IrFunction, AbstractValue>(ReferenceEqualityComparer.Instance);
        var contracts = new Dictionary<IrFunction, IReadOnlyList<Precondition>>(ReferenceEqualityComparer.Instance);

        var functions = program.AllFunctions.Select(function =>
        {
            var locals = IrWalk.LocalNames(function, assigningDeclares: program.Language == SourceLanguage.Python);
            var evaluator = new Evaluator(program.Language)
            {
                Volatile = Scopes.Volatile(program, function),
                Escaping = Scopes.Escaping(function),
                Locals = locals,
                DeclaredTypes = DeclaredTypes(function),
                CallReturns = call => targets.Resolve(call, function, locals) is { Overridable: false } target && summaries.TryGetValue(target.Function, out var returned)
                    ? returned
                    : null,
            };
            return (Function: function, Evaluator: evaluator, Graph: CfgBuilder.Build(function));
        }).ToList();

        for (var round = 0; round < SummaryRounds; round++)
        {
            var changed = false;
            foreach (var (function, evaluator, graph) in functions.Where(f => Summarised(f.Function)))
            {
                var returned = Returned(graph, Fixpoint.Run(graph, evaluator, StartOf(function, evaluator)), evaluator);
                if (returned is null || summaries.TryGetValue(function, out var known) && known == returned) continue;
                summaries[function] = returned;
                changed = true;
            }

            if (!changed) break;
        }

        foreach (var (function, evaluator, graph) in functions)
        {
            var fixpoint = Fixpoint.Run(graph, evaluator, StartOf(function, evaluator));
            var local = new List<AnalysisFinding>();
            new FunctionChecks(graph, fixpoint, evaluator, source, local, targets, callee => contracts.TryGetValue(callee, out var known) ? known : contracts[callee] = Contracts.Of(callee))
                .Run();
            var refined = symbolic.Elapsed < SymbolicBudget ? SymbolicChecks.Refine(graph, evaluator, local, source) : local;
            findings.AddRange(WithSlices(graph, refined).Select(finding => finding with { Function = function.FullName }));
        }

        findings.AddRange(new Concurrency(program, source).Check());

        return findings
            .GroupBy(f => (f.CheckId, f.Span.File, f.Span.Line))
            .Select(g => g.OrderBy(f => f.Confidence).First())
            .OrderBy(f => f.Span.File).ThenBy(f => f.Span.Line)
            .ToList();
    }

    /// <summary>Functions whose results a summary can stand for: not constructors, generators, async functions or top-level code.</summary>
    private static bool Summarised(IrFunction function) =>
        function is { IsConstructor: false, IsGenerator: false, IsAsync: false } && function.Name != IrFunction.ModuleBody &&
        function.ReturnType.Name != IrType.Nothing.Name;

    /// <summary>
    /// What a function can return, joined over every way out. A Python function that runs off its end returns None - unless
    /// the last thing it does is a call, which may well be one that always raises.
    /// </summary>
    private static AbstractValue? Returned(ControlFlowGraph graph, Fixpoint fixpoint, Evaluator evaluator)
    {
        AbstractValue? joined = null;

        foreach (var block in graph.Blocks)
        {
            if (block.Terminator is not Leave leave) continue;

            var state = fixpoint.EntryOf(block.Id);
            foreach (var instruction in block.Instructions)
            {
                if (!state.IsReachable) break;
                state = evaluator.Apply(state, instruction);
            }

            if (!state.IsReachable) continue;

            AbstractValue value;
            if (leave.Value is { } returned) value = evaluator.Evaluate(returned, state);
            else if (evaluator.Language == SourceLanguage.Python && !MayNotReturn(block.Instructions.LastOrDefault())) value = AbstractValue.Null;
            else continue;

            joined = joined is null ? value : joined.Join(value);
        }

        if (joined is not { IsImpossible: false, IsUnknown: false }) return null;

        return joined.IsNull ? joined : joined.WithoutNull() with { NullnessKnown = false };
    }

    /// <summary>A last call that may never come back: a function of the program's that could always raise, or exit.</summary>
    private static bool MayNotReturn(Instruction? last) =>
        last is EvaluateInstruction { Value: Call call } &&
        (call.Callee is Name { Identifier: var called } && !Evaluator.IsBuiltin(called) || call.CalleeName is "exit" or "quit" or "_exit" or "abort");

    /// <summary>Each finding with the lines that decide the value it is about, when more lines than its own do.</summary>
    private static IEnumerable<AnalysisFinding> WithSlices(ControlFlowGraph graph, IEnumerable<AnalysisFinding> findings)
    {
        ProgramSlicer? slicer = null;

        foreach (var finding in findings)
        {
            slicer ??= new ProgramSlicer(graph);
            var lines = slicer.LinesDeciding(finding.Span, Criterion(Find(graph, finding.Span), finding.CheckId));
            yield return lines.Count > 1 ? finding with { Slice = lines } : finding;
        }
    }

    /// <summary>The value a finding is about: the divisor, the thing used as an object, the collection taken from.</summary>
    private static IEnumerable<string> Criterion(Expr? at, string check) => (check, at) switch
    {
        ("analysis-division-by-zero", Binary division) => Variables(division.Right),
        ("analysis-null-used", Member member) => Variables(member.Target),
        ("analysis-null-used", ElementAccess element) => Variables(element.Target),
        ("analysis-null-used", MoreItems more) => Variables(more.Items),
        ("analysis-empty-collection", Call { Callee: Member member }) => Variables(member.Target),
        (_, { } expression) => Variables(expression),
        _ => [],
    };

    private static IEnumerable<string> Variables(Expr expression) => expression switch
    {
        Member { Target: Name { Identifier: "this" }, MemberName: var field } => [field],
        Name name => [name.Identifier],
        _ => IrWalk.Children(expression).SelectMany(Variables),
    };

    private static Expr? Find(ControlFlowGraph graph, SourceSpan at)
    {
        static Expr? Within(Expr expression, SourceSpan span) =>
            expression.Span == span ? expression : IrWalk.Children(expression).Select(child => Within(child, span)).FirstOrDefault(found => found is not null);

        IEnumerable<Expr> held = graph.Blocks.SelectMany(block => block.Instructions.SelectMany(instruction => instruction switch
        {
            AssignInstruction assign => new[] { assign.Target, assign.Value },
            EvaluateInstruction evaluate => [evaluate.Value],
            ForgetInstruction forget => forget.Parts,
            _ => [],
        }).Concat(block.Terminator switch
        {
            Branch branch => [branch.Condition],
            Leave { Value: { } value } => [value],
            Raise { Exception: { } thrown } => [thrown],
            _ => [],
        }));

        return held.Select(expression => Within(expression, at)).FirstOrDefault(found => found is not null);
    }

    /// <summary>Each variable's declared type, where it is declared once with one type.</summary>
    private static Dictionary<string, IrType> DeclaredTypes(IrFunction function) =>
        function.Parameters.Select(p => (p.Name, p.Type))
            .Concat(IrWalk.Statements(function.Body).OfType<Declare>().Select(d => (Name: d.Variable, d.Type)))
            .Where(d => !d.Type.IsUnknown)
            .GroupBy(d => d.Name)
            .Where(g => g.Select(d => d.Type).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Type, StringComparer.Ordinal);

    private static AbstractState StartOf(IrFunction function, Evaluator evaluator)
    {
        var state = AbstractState.Start;

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var parameter = function.Parameters[i];
            var value = i == 0 && function.Owner is not null && !function.IsStatic && evaluator.Language == SourceLanguage.Python
                ? AbstractValue.Of(ValueKind.Object)
                : parameter.Type.IsUnknown ? AbstractValue.Unknown : evaluator.FromType(parameter.Type);

            if (parameter.Default is Literal { Kind: LiteralKind.Null }) value = value.Join(AbstractValue.Null);
            state = evaluator.Store(state, parameter.Name, value);
        }

        if (evaluator.Language != SourceLanguage.Python && function.Owner is not null && !function.IsStatic)
            state = state.With("this", Failures.ReceiverCanBeNothing(evaluator.Language)
                ? AbstractValue.Of(ValueKind.Object) with { NullnessKnown = false }
                : AbstractValue.Of(ValueKind.Object));

        return state;
    }

    private sealed class FunctionChecks(
        ControlFlowGraph graph, Fixpoint fixpoint, Evaluator evaluator, SourceText source, List<AnalysisFinding> findings, CallTargets targets,
        Func<IrFunction, IReadOnlyList<Precondition>> contractsOf)
    {
        private readonly Dictionary<SourceSpan, (bool CanBeTrue, bool CanBeFalse, BasicBlock Block, Branch Branch)> _conditions = [];

        private bool IsPython => evaluator.Language == SourceLanguage.Python;

        public void Run()
        {
            foreach (var block in graph.Blocks)
            {
                var state = fixpoint.EntryOf(block.Id);
                if (!state.IsReachable) continue;

                foreach (var instruction in block.Instructions)
                {
                    foreach (var expression in ExpressionsOf(instruction)) Inspect(expression, state);
                    state = evaluator.Apply(state, instruction);
                    if (!state.IsReachable) break;
                }

                if (!state.IsReachable) continue;

                switch (block.Terminator)
                {
                    case Branch branch:
                        Inspect(branch.Condition, state);
                        RecordCondition(block, branch, state);
                        break;
                    case Leave { Value: { } returned }:
                        Inspect(returned, state);
                        CheckReturnHint(returned, state);
                        break;
                    case Raise { Exception: { } raised }:
                        Inspect(raised, state);
                        break;
                }
            }

            ReportConditions();
            ReportEndlessLoops();
            new Protocols(graph, evaluator.Language, Quote, (id, span, message, severity, confidence) => Report(id, span, message, severity, confidence, FindingKind.Logic)).Check();
            new MemorySafety(graph, evaluator.Language, Quote, (id, span, message, severity, confidence) => Report(id, span, message, severity, confidence)).Check();
        }

        /// <summary>Loops whose condition nothing inside them can change: once they start, they never stop.</summary>
        private void ReportEndlessLoops()
        {
            var locals = (evaluator.Locals ?? new HashSet<string>()).Except(evaluator.Volatile).ToHashSet(StringComparer.Ordinal);

            foreach (var (_, condition) in LoopBounds.NeverEnding(graph.Function, locals))
            {
                var tests = Leaves(condition).ToList();
                var head = graph.Blocks.FirstOrDefault(b => b.IsLoopHead && b.Terminator is Branch branch && tests.Contains(branch.Condition, ReferenceEqualityComparer.Instance));
                if (head is null) continue;

                var entry = fixpoint.EntryOf(head.Id);
                if (!entry.IsReachable || !evaluator.Assume(entry, condition, true).IsReachable) continue;

                var names = string.Join(" or ", IrWalk.Names(condition).Where(locals.Contains).Distinct().Select(n => $"`{n}`"));
                Report("analysis-loop-never-ends", condition.Span, $"Nothing inside the loop changes {names}, so once `{Quote(condition)}` is true the loop never ends",
                    Severity.Error, Confidence.Likely, FindingKind.Logic);
            }
        }

        private static IEnumerable<Expr> Leaves(Expr condition) => condition switch
        {
            Unary { Operator: UnaryOperator.Not } negation => Leaves(negation.Operand),
            Binary { Operator: BinaryOperator.And or BinaryOperator.Or } both => Leaves(both.Left).Concat(Leaves(both.Right)),
            _ => [condition],
        };

        /// <summary>
        /// Names that only ever hold a value written into the code: a switch like debug = False, or a flag set to true
        /// where something is found. Whether such a name holds is the program's own business, not a mistake - and where
        /// a flag is set deep inside loops, following every value cannot always see how it gets there.
        /// </summary>
        private HashSet<string> Switches => _switches ??= IrWalk.Statements(graph.Function.Body)
            .Select(s => s switch
            {
                Assign { Target: Name name } assign => (Name: name.Identifier, Literal: assign is { Value: Literal, Compound: null }),
                Declare declare => (Name: declare.Variable, Literal: declare.Initial is Literal or null),
                _ => (Name: "", Literal: false),
            })
            .GroupBy(s => s.Name)
            .Where(g => g.Key.Length > 0 && g.All(s => s.Literal))
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        private HashSet<string>? _switches;

        /// <summary>Python never enforces a type hint, so checking a hinted parameter's type at run time is sensible, not redundant.</summary>
        private bool ChecksAHint(Expr condition) =>
            IsPython && condition is Call { Callee: Name { Identifier: "isinstance" }, Arguments: [{ Value: Name { Identifier: var checkedName } }, ..] } &&
            graph.Function.Parameters.Any(p => p.Name == checkedName && !p.Type.IsUnknown);

        private static IEnumerable<Expr> ExpressionsOf(Instruction instruction) => instruction switch
        {
            AssignInstruction assign => assign.Target is Name ? [assign.Value] : [assign.Value, assign.Target],
            EvaluateInstruction evaluate => [evaluate.Value],
            ForgetInstruction forget => forget.Parts,
            _ => [],
        };

        private void Report(string id, SourceSpan span, string message, Severity severity, Confidence confidence, FindingKind kind = FindingKind.Runtime) =>
            findings.Add(new AnalysisFinding(id, span, message, severity, confidence, kind, FoundBy));

        private string Quote(Expr expression) => source.Of(expression.Span) is { Length: > 0 } text ? text : IrText.Of(expression);

        /// <param name="asCallee">Whether this is what a call runs, which some languages allow on nothing.</param>
        private void Inspect(Expr expression, AbstractState state, bool asCallee = false)
        {
            switch (expression)
            {
                case Binary { Operator: BinaryOperator.And } both:
                    Inspect(both.Left, state);
                    Inspect(both.Right, evaluator.Assume(state, both.Left, true));
                    return;

                case Binary { Operator: BinaryOperator.Or } either:
                    Inspect(either.Left, state);
                    Inspect(either.Right, evaluator.Assume(state, either.Left, false));
                    return;

                case Conditional choice:
                    Inspect(choice.Test, state);
                    Inspect(choice.WhenTrue, evaluator.Assume(state, choice.Test, true));
                    Inspect(choice.WhenFalse, evaluator.Assume(state, choice.Test, false));
                    return;

                case Binary binary:
                    CheckDivision(binary, state);
                    CheckOperandTypes(binary, state);
                    break;

                case Member member when member.Target is not Literal && !IsSpecialName(member.MemberName):
                    if (!asCallee || !Failures.NothingCanRunMethods(evaluator.Language))
                        CheckNotNull(member.Target, member, state, $"reading `.{member.MemberName}`");
                    break;

                case ElementAccess element:
                    CheckNotNull(element.Target, element, state, "taking an item from it");
                    CheckIndex(element, state);
                    break;

                case Call call:
                    CheckCall(call, state);
                    break;

                case MoreItems more:
                    CheckIterable(more, state);
                    break;
            }

            foreach (var child in Children(expression))
                Inspect(child, state, asCallee: expression is Call call && ReferenceEquals(child, call.Callee));
        }

        private static bool IsSpecialName(string name) => name.Length > 4 && name.StartsWith("__", StringComparison.Ordinal) && name.EndsWith("__", StringComparison.Ordinal);

        private static IEnumerable<Expr> Children(Expr expression) => expression switch
        {
            Unary unary => [unary.Operand],
            Binary binary => [binary.Left, binary.Right],
            Call call => [call.Callee, .. call.Arguments.Select(a => a.Value)],
            Member member => [member.Target],
            ElementAccess element => [element.Target, element.Key],
            Slice slice => new[] { slice.Target, slice.Lower, slice.Upper, slice.Step }.OfType<Expr>(),
            NewObject created => created.Arguments.Select(a => a.Value),
            Cast cast => [cast.Value],
            CollectionLiteral collection => collection.Items.Concat(collection.Keys ?? []),
            AssignValue assigned => assigned.Target is Name ? [assigned.Value] : [assigned.Value, assigned.Target],
            MoreItems more => [more.Items],
            NextItem next => [next.Items],
            Opaque opaque => opaque.Parts,
            _ => [],
        };

        private void CheckDivision(Binary binary, AbstractState state)
        {
            if (binary.Operator is not (BinaryOperator.Divide or BinaryOperator.FloorDivide or BinaryOperator.Modulo)) return;

            var dividend = evaluator.Evaluate(binary.Left, state);
            var divisor = evaluator.Evaluate(binary.Right, state);
            if (!divisor.IsNumber) return;
            if (binary.Operator == BinaryOperator.Modulo && !dividend.IsNumber) return;

            var integersOnly = Failures.WholeNumberDivision(evaluator.Language,
                dividend.IsOnly(ValueKind.Integer | ValueKind.Boolean), divisor.IsOnly(ValueKind.Integer | ValueKind.Boolean),
                dividend.IsOnly(ValueKind.Real), divisor.IsOnly(ValueKind.Real), binary.Left is Literal, binary.Right is Literal);
            if (!IsPython && !integersOnly) return;

            var range = divisor.Number;
            var failure = Failures.DividingByZero(evaluator.Language);

            if (range.IsExact && range.Low == 0)
            {
                Report("analysis-division-by-zero", binary.Span,
                    $"`{Quote(binary.Right)}` is always 0 here, so `{Quote(binary)}` fails with {failure}", Severity.Error, Confidence.Certain);
            }
            else if (range.Contains(0) && !range.IsTop && (range.Low == 0 || range.High == 0) && binary.Right is not Literal && Followable(binary.Right))
            {
                Report("analysis-division-by-zero", binary.Span,
                    $"`{Quote(binary.Right)}` can still be 0 here, so `{Quote(binary)}` can fail with {failure}", Severity.Error, Confidence.Possible);
            }
        }

        /// <summary>Divisors built only from what this analysis can follow - names and sizes of names - so a guard on them is seen.</summary>
        private bool Followable(Expr expression) => expression switch
        {
            Name or Literal => true,
            Call { Callee: Name { Identifier: "len" }, Arguments: [{ Value: Name }] } => true,
            Member { Target: Name sized, MemberName: "length" or "Length" or "Count" } => IsOwnCollection(sized.Identifier),
            Call { Callee: Member { Target: Name sized, MemberName: "length" or "size" or "Count" }, Arguments.Count: 0 } => IsOwnCollection(sized.Identifier),
            Unary unary => Followable(unary.Operand),
            Binary binary => Followable(binary.Left) && Followable(binary.Right),
            _ => false,
        };

        /// <summary>
        /// A collection the caller hands in, which can be empty, or one built here - not a copy of a field, whose size the
        /// class usually keeps above 0 on purpose.
        /// </summary>
        private bool IsOwnCollection(string name) =>
            graph.Function.Parameters.Any(p => p.Name == name) ||
            IrWalk.Statements(graph.Function.Body).Any(s => s switch
            {
                Declare { Initial: NewObject or CollectionLiteral } declare => declare.Variable == name,
                Assign { Target: Name target, Value: NewObject or CollectionLiteral } => target.Identifier == name,
                _ => false,
            });

        private void CheckOperandTypes(Binary binary, AbstractState state)
        {
            if (!IsPython) return;

            var left = evaluator.Evaluate(binary.Left, state);
            var right = evaluator.Evaluate(binary.Right, state);
            var text = left.IsOnly(ValueKind.Text) ? binary.Left : right.IsOnly(ValueKind.Text) ? binary.Right : null;
            var number = left.IsOnly(ValueKind.Integer | ValueKind.Real) ? binary.Left : right.IsOnly(ValueKind.Integer | ValueKind.Real) ? binary.Right : null;

            if (text is null || number is null || text == number) return;

            var action = binary.Operator switch
            {
                BinaryOperator.Add => "add",
                BinaryOperator.Subtract => "subtract",
                BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual => "compare",
                _ => null,
            };

            if (action is null) return;

            Report("analysis-type-mismatch", binary.Span,
                $"`{Quote(text)}` is text and `{Quote(number)}` is a number, and Python cannot {action} them, so `{Quote(binary)}` fails with TypeError",
                Severity.Error, Confidence.Certain);
        }

        private void CheckNotNull(Expr target, Expr use, AbstractState state, string doing)
        {
            if (use is ElementAccess && Failures.NothingCanBeIndexed(evaluator.Language)) return;

            var value = evaluator.Evaluate(target, state);
            var none = Failures.Nothing(evaluator.Language);
            var failure = Failures.UsingNothing(evaluator.Language, takingAnItem: use is ElementAccess);

            if (value.IsNull)
                Report("analysis-null-used", use.Span, $"`{Quote(target)}` is {none} here, so {doing} fails with {failure}", Severity.Error, Confidence.Certain);
            else if (value.MayBeNull)
                Report("analysis-null-used", use.Span, $"`{Quote(target)}` can be {none} here, so {doing} can fail with {failure}", Severity.Error, Confidence.Possible);
        }

        private void CheckIndex(ElementAccess element, AbstractState state)
        {
            var target = evaluator.Evaluate(element.Target, state);
            var key = evaluator.Evaluate(element.Key, state);

            if (!Failures.ReadingPastTheEndFails(evaluator.Language)) return;
            if (!target.IsOnly(ValueKind.List | ValueKind.Tuple | ValueKind.Text) || !key.IsOnly(ValueKind.Integer) || target.Length.IsEmpty) return;

            var length = target.Length;
            var tooBig = key.Number.Low >= length.High;
            var tooSmall = IsPython ? key.Number.High < -length.High : key.Number.High < 0;
            if (!tooBig && !tooSmall) return;

            var size = length.IsExact ? $"has {length.Low} item{(length.Low == 1 ? "" : "s")}" : $"has at most {length.High} items";
            Report("analysis-index-out-of-range", element.Span,
                $"`{Quote(element.Target)}` {size} here, so `{Quote(element)}` asks for a position that does not exist - {Failures.OutsideTheList(evaluator.Language)}",
                Severity.Error, Confidence.Certain);
        }

        private void CheckCall(Call call, AbstractState state)
        {
            if (targets.Resolve(call, graph.Function, evaluator.Locals ?? new HashSet<string>()) is { Function.IsDecorated: false } target &&
                !(target.Function.Name.StartsWith("__", StringComparison.Ordinal) && target.Function.Name != "__init__"))
            {
                if (IsPython) CheckArguments(call, target);
                if (Bind(call, target, state) is { } bound)
                {
                    CheckContract(call, target, bound);
                    if (IsPython) CheckArgumentHints(call, target, bound);
                }
            }

            if (call.Callee is Member { Target: var owner, MemberName: var taking } && call.Arguments.Count == 0 && TakesAnItem(taking) is { } empty &&
                evaluator.Evaluate(owner, state) is var popped && popped.IsOnly(ValueKind.List) && popped.Length is { IsExact: true, Low: 0 })
            {
                Report("analysis-empty-collection", call.Span, $"`{Quote(owner)}` is empty here, so `{Quote(call)}` fails with {empty}",
                    Severity.Error, Confidence.Certain);
            }

            if (!IsPython)
            {
                CheckParse(call);
                return;
            }

            if (call.Callee is not Name { Identifier: var function } || call.Arguments.Count == 0) return;

            var argument = call.Arguments[0].Value;
            var value = evaluator.Evaluate(argument, state);

            if (function == "len" && value.IsOnly(ValueKind.Integer | ValueKind.Real | ValueKind.Boolean))
                Report("analysis-type-mismatch", call.Span, $"`{Quote(argument)}` is a number, which has no length, so `{Quote(call)}` fails with TypeError",
                    Severity.Error, Confidence.Certain);

            if (function is "int" or "float" && argument is Literal { Kind: LiteralKind.Text, Value: string text } &&
                !double.TryParse(text.Trim().Replace("_", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                Report("analysis-not-a-number", call.Span, $"\"{text}\" is not a number, so `{Quote(call)}` fails with ValueError",
                    Severity.Error, Confidence.Certain);
        }

        /// <summary>The error taking an item from an empty collection raises, for the calls that take one.</summary>
        private string? TakesAnItem(string method) => evaluator.Language switch
        {
            SourceLanguage.Python when method == "pop" => Failures.TakingFromEmpty(SourceLanguage.Python),
            SourceLanguage.CSharp when method is "Pop" or "Dequeue" or "Peek" or "First" or "Last" => Failures.TakingFromEmpty(SourceLanguage.CSharp),
            SourceLanguage.Java when method is "pop" or "element" or "getFirst" or "getLast" or "removeFirst" or "removeLast" =>
                "a NoSuchElementException (EmptyStackException for a Stack)",
            _ => null,
        };

        /// <summary>int.Parse("abc") and Integer.parseInt("abc") - text that is written into the program and is not a number.</summary>
        private void CheckParse(Call call)
        {
            if (call.Callee is not Member { Target: Name { Identifier: var type }, MemberName: var method } ||
                call.Arguments is not [{ Value: Literal { Kind: LiteralKind.Text, Value: string text } }, ..]) return;

            var whole = (type, method) is ("int" or "long" or "short" or "Int32" or "Int64", "Parse") or ("Integer", "parseInt" or "valueOf") or ("Long", "parseLong" or "valueOf");
            var real = (type, method) is ("double" or "float" or "decimal" or "Double" or "Decimal", "Parse") or ("Double", "parseDouble" or "valueOf") or ("Float", "parseFloat");
            if (!whole && !real) return;

            var trimmed = evaluator.Language == SourceLanguage.CSharp ? text.Trim() : text;
            var parses = whole
                ? long.TryParse(trimmed, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out _)
                : double.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
            if (parses) return;

            var failure = evaluator.Language == SourceLanguage.CSharp ? "a FormatException" : "a NumberFormatException";
            Report("analysis-not-a-number", call.Span, $"\"{text}\" is not {(whole ? "a whole number" : "a number")}, so `{Quote(call)}` fails with {failure}",
                Severity.Error, Confidence.Certain);
        }

        /// <summary>Cross-function consistency in Python: a call must give a function exactly the arguments it takes.</summary>
        private void CheckArguments(Call call, CallTarget target)
        {
            if (call.Arguments.Any(a => a.Value is Opaque { What: "unpacked" })) return;

            var parameters = target.Parameters;
            var ordered = parameters.Where(p => p.Kind == ParameterKind.Normal).ToList();
            var positional = call.Arguments.Count(a => a.Name is null);
            var named = call.Arguments.Where(a => a.Name is not null).Select(a => a.Name!).ToList();
            var name = CalledName(target);
            var shown = Quote(call);

            if (positional > ordered.Count && parameters.All(p => p.Kind != ParameterKind.Rest))
            {
                var most = ordered.Count == ordered.Count(p => p.Default is null) ? $"{ordered.Count}" : $"at most {ordered.Count}";
                Report("analysis-wrong-arguments", call.Span, $"`{shown}` gives {Plural(positional, "argument")}, but `{name}` takes {most}", Severity.Error, Confidence.Certain);
                return;
            }

            foreach (var keyword in named)
            {
                if (ordered.Take(positional).Any(p => p.Name == keyword))
                {
                    Report("analysis-wrong-arguments", call.Span, $"`{shown}` gives `{keyword}` twice - once in order and once by name", Severity.Error, Confidence.Certain);
                    return;
                }

                if (!parameters.Any(p => p.Name == keyword && p.Kind is ParameterKind.Normal or ParameterKind.KeywordOnly) && parameters.All(p => p.Kind != ParameterKind.Keywords))
                {
                    Report("analysis-wrong-arguments", call.Span, $"`{shown}` names `{keyword}`, which `{name}` does not have", Severity.Error, Confidence.Certain);
                    return;
                }
            }

            var missing = ordered.Skip(positional).Concat(parameters.Where(p => p.Kind == ParameterKind.KeywordOnly))
                .Where(p => p.Default is null && !named.Contains(p.Name)).Select(p => $"`{p.Name}`").ToList();
            if (missing.Count > 0)
                Report("analysis-wrong-arguments", call.Span, $"`{shown}` leaves out {string.Join(" and ", missing)}, which `{name}` needs", Severity.Error, Confidence.Certain);
        }

        private static string CalledName(CallTarget target) =>
            target.Function.Name == "__init__" && target.Function.Owner is { } made ? made : target.Function.Name;

        private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

        /// <summary>The value each parameter of the called function gets, from the caller's own values; null when it cannot be told.</summary>
        private Dictionary<string, AbstractValue>? Bind(Call call, CallTarget target, AbstractState state)
        {
            if (call.Arguments.Any(a => a.Value is Opaque { What: "unpacked" })) return null;

            var parameters = target.Parameters;
            var ordered = parameters.Where(p => p.Kind == ParameterKind.Normal).ToList();
            var bound = new Dictionary<string, AbstractValue>(StringComparer.Ordinal);
            var position = 0;

            foreach (var argument in call.Arguments)
            {
                var name = argument.Name ?? (position < ordered.Count ? ordered[position++].Name : null);
                if (name is null) return null;
                bound[name] = evaluator.Evaluate(argument.Value, state);
            }

            foreach (var parameter in parameters.Where(p => !bound.ContainsKey(p.Name) && p.Kind is ParameterKind.Normal or ParameterKind.KeywordOnly))
                bound[parameter.Name] = parameter.Default is Literal given ? evaluator.Evaluate(given, AbstractState.Start) : AbstractValue.Unknown;

            return bound;
        }

        /// <summary>Contract-based verification: a call whose arguments certainly break what the called function checks for.</summary>
        private void CheckContract(Call call, CallTarget target, Dictionary<string, AbstractValue> bound)
        {
            var preconditions = contractsOf(target.Function);
            if (preconditions.Count == 0) return;

            var callee = new Evaluator(evaluator.Language) { Locals = target.Function.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal) };
            var state = bound.Aggregate(AbstractState.Start, (s, pair) => callee.Store(s, pair.Key, pair.Value));

            foreach (var precondition in preconditions)
            {
                if (callee.Assume(state, precondition.Failure, false).IsReachable) continue;

                Report("analysis-contract-broken", call.Span,
                    $"`{Quote(call)}` gives `{CalledName(target)}` what it refuses: when `{Quote(precondition.Failure)}` it raises {precondition.Raises} (line {precondition.Span.Line})",
                    Severity.Error, Confidence.Certain);
                return;
            }
        }

        /// <summary>Type-driven checking: a Python hint the value given for it can never match.</summary>
        private void CheckArgumentHints(Call call, CallTarget target, Dictionary<string, AbstractValue> bound)
        {
            foreach (var parameter in target.Parameters)
            {
                if (!bound.TryGetValue(parameter.Name, out var value) || call.Arguments.All(a => a.Name != parameter.Name) &&
                    target.Parameters.Where(p => p.Kind == ParameterKind.Normal).Take(call.Arguments.Count(a => a.Name is null)).All(p => p.Name != parameter.Name)) continue;

                if (Clashes(value, parameter.Type) is { } what)
                {
                    Report("analysis-type-hint-broken", call.Span,
                        $"`{Quote(call)}` passes {what} for `{parameter.Name}`, which `{CalledName(target)}` says is `{parameter.Type}`", Severity.Warning, Confidence.Likely, FindingKind.Logic);
                    return;
                }
            }
        }

        private void CheckReturnHint(Expr returned, AbstractState state)
        {
            var hint = graph.Function.ReturnType;
            if (!IsPython || hint.IsUnknown || Clashes(evaluator.Evaluate(returned, state), hint) is not { } what) return;

            Report("analysis-type-hint-broken", returned.Span,
                $"`{graph.Function.Name}` says it returns `{(hint.Name == IrType.Nothing.Name ? "None" : hint)}`, but here it returns {what}", Severity.Warning, Confidence.Likely, FindingKind.Logic);
        }

        /// <summary>What the value is, when it is certainly none of the kinds the hint allows.</summary>
        private static string? Clashes(AbstractValue value, IrType hint)
        {
            ValueKind? allowed = hint.Name switch
            {
                "int" => ValueKind.Integer | ValueKind.Boolean,
                "float" => ValueKind.Integer | ValueKind.Real | ValueKind.Boolean,
                "str" => ValueKind.Text,
                "bool" => ValueKind.Boolean,
                "list" => ValueKind.List,
                "dict" => ValueKind.Dictionary,
                "set" or "frozenset" => ValueKind.Set,
                "tuple" => ValueKind.Tuple,
                "None" => ValueKind.Null,
                _ => null,
            };

            if (allowed is not { } kinds || value.IsUnknown || value.IsImpossible) return null;
            if (hint.Nullable) kinds |= ValueKind.Null;
            if ((kinds & (ValueKind.Integer | ValueKind.Real)) != 0) kinds |= ValueKind.Integer | ValueKind.Real | ValueKind.Boolean;
            if ((value.Kinds & kinds) != 0) return null;

            return value.Kinds switch
            {
                ValueKind.Null => "None",
                ValueKind.Text => "text",
                ValueKind.Integer or ValueKind.Real or (ValueKind.Integer | ValueKind.Real) => "a number",
                ValueKind.Boolean => "True or False",
                ValueKind.List => "a list",
                ValueKind.Dictionary => "a dictionary",
                ValueKind.Tuple => "a tuple",
                ValueKind.Set => "a set",
                _ => null,
            };
        }

        private void CheckIterable(MoreItems more, AbstractState state)
        {
            var items = evaluator.Evaluate(more.Items, state);
            var source = more.Items is Name { Identifier: var held } && held.StartsWith('$') ? null : more.Items;
            var shown = source is null ? "what the loop goes through" : $"`{Quote(source)}`";

            if (items.IsNull && !Failures.NothingCanBeWalked(evaluator.Language))
                Report("analysis-null-used", more.Span, $"{shown} is {Failures.Nothing(evaluator.Language)} here, so the loop cannot go through it",
                    Severity.Error, Confidence.Certain);
            else if (IsPython && items.IsOnly(ValueKind.Integer | ValueKind.Real | ValueKind.Boolean))
                Report("analysis-type-mismatch", more.Span, $"{shown} is a number, and a for loop cannot go through a number - TypeError",
                    Severity.Error, Confidence.Certain);
        }

        private void RecordCondition(BasicBlock block, Branch branch, AbstractState state)
        {
            var canBeTrue = evaluator.Assume(state, branch.Condition, true).IsReachable;
            var canBeFalse = evaluator.Assume(state, branch.Condition, false).IsReachable;

            _conditions[branch.Span] = _conditions.TryGetValue(branch.Span, out var seen)
                ? (seen.CanBeTrue || canBeTrue, seen.CanBeFalse || canBeFalse, seen.Block, seen.Branch)
                : (canBeTrue, canBeFalse, block, branch);
        }

        private void ReportConditions()
        {
            foreach (var (span, (canBeTrue, canBeFalse, block, branch)) in _conditions)
            {
                if (canBeTrue == canBeFalse || IsDeliberateConstant(branch.Condition) ||
                    branch.Condition is Name { Identifier: var flag } && Switches.Contains(flag) || ChecksAHint(branch.Condition)) continue;

                var text = Quote(branch.Condition);
                var failing = graph.Blocks[branch.WhenFalse];
                var isAssert = failing.Instructions.Count == 0 && failing.Terminator is Raise { Exception: Opaque { What: "AssertionError" } };

                if (isAssert)
                {
                    if (!canBeTrue)
                        Report("analysis-assert-always-fails", span, $"`{text}` is never true here, so this assert always fails",
                            Severity.Error, Confidence.Certain, FindingKind.Logic);
                    continue;
                }

                if (branch.Condition is MoreItems) continue;

                if (block.IsLoopHead)
                {
                    if (!canBeTrue)
                        Report("analysis-loop-never-runs", span, $"`{text}` is already false when the loop is reached, so the loop never runs",
                            Severity.Warning, Confidence.Likely, FindingKind.Logic);
                    continue;
                }

                if (!canBeTrue && OnlyThrows(branch.WhenTrue) || canBeTrue && branch.TestsACase) continue;

                if (!canBeTrue)
                    Report("analysis-never-true", span, $"`{text}` can never be true here, so the code it guards never runs",
                        Severity.Warning, Confidence.Likely, FindingKind.Logic);
                else
                    Report("analysis-always-true", span, $"`{text}` is always true here, so checking it changes nothing",
                        Severity.Suggestion, Confidence.Likely, FindingKind.Logic);
            }
        }

        /// <summary>Whether a branch does nothing but throw - a guard against the impossible, written on purpose.</summary>
        private bool OnlyThrows(int block)
        {
            var target = graph.Blocks[block];
            while (target.Instructions.Count == 0 && target.Terminator is Jump jump) target = graph.Blocks[jump.Target];
            return target.Terminator is Raise && target.Instructions.All(i => i is AssignInstruction or EvaluateInstruction { Value: NewObject or Call });
        }

        private static bool IsDeliberateConstant(Expr condition) => condition switch
        {
            Literal => true,
            Name { Identifier: var name } => name.Length > 1 && name.All(c => char.IsUpper(c) || c == '_' || char.IsDigit(c)),
            Unary { Operand: var inner } => IsDeliberateConstant(inner),
            _ => false,
        };
    }
}
