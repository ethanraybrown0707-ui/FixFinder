using System.Collections.Immutable;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Solver;

namespace FixFinder.Core.Analysis.Symbolic;

/// <summary>
/// Follows a function one path at a time with symbols for what is not known, asking the solver at every branch which
/// ways are possible and at every division, index, dereference and pop whether it can fail - and, when it can, for the
/// inputs that make it. Budgets on paths, steps and loop turns keep it from exploding; a path cut short marks the report
/// incomplete, so nothing is ever concluded from a search that did not finish.
/// </summary>
public sealed partial class SymbolicExecutor
{
    private const int MostCases = 8;

    private static readonly ImmutableDictionary<Expr, bool> NoChoices = ImmutableDictionary.Create<Expr, bool>(ReferenceEqualityComparer.Instance);

    private readonly ControlFlowGraph _graph;
    private readonly SourceLanguage _language;
    private readonly IReadOnlySet<string> _locals;
    private readonly IReadOnlySet<string> _volatile;
    private readonly IReadOnlyDictionary<string, IrType> _declared;
    private readonly Dictionary<Expr, CountedLoop> _counted;
    private readonly HashSet<string> _numeric;
    private readonly SymbolTable _symbols = new();
    private readonly ConstraintSolver _solver = new() { MostBranches = 60, MostPivots = 300 };
    private readonly Dictionary<string, SolverResult> _solved = [];
    private readonly SymbolicReport _report = new();
    private readonly Stack<Path> _pending = new();
    private readonly HashSet<string> _seen = [];
    private int _steps;
    private readonly System.Diagnostics.Stopwatch _clock = new();

    public SymbolicExecutor(ControlFlowGraph graph, SourceLanguage language, IReadOnlySet<string> locals, IReadOnlySet<string> volatileNames,
        IReadOnlyDictionary<string, IrType> declared)
    {
        _graph = graph;
        _language = language;
        _locals = locals;
        _volatile = volatileNames;
        _declared = declared;
        _counted = LoopBounds.Counted(graph.Function);
        _numeric = NumericNames(graph.Function);
    }

    /// <summary>
    /// Variables the function does arithmetic with - subtracts, multiplies, divides, negates, counts a range to, indexes
    /// with. Where they are compared with each other they are compared as numbers, so x1 == x2 constrains x2 - x1.
    /// </summary>
    private static HashSet<string> NumericNames(IrFunction function)
    {
        var numeric = new HashSet<string>(StringComparer.Ordinal);

        void Visit(Expr expression)
        {
            switch (expression)
            {
                case Binary { Operator: BinaryOperator.Subtract or BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.FloorDivide
                    or BinaryOperator.Modulo or BinaryOperator.Power } arithmetic:
                    if (arithmetic.Left is Name { Identifier: var left }) numeric.Add(left);
                    if (arithmetic.Right is Name { Identifier: var right }) numeric.Add(right);
                    break;
                case Unary { Operator: UnaryOperator.Negate, Operand: Name { Identifier: var negated } }:
                    numeric.Add(negated);
                    break;
                case ElementAccess { Key: Name { Identifier: var index } }:
                    numeric.Add(index);
                    break;
                case Call { Callee: Name { Identifier: "range" } } range:
                    foreach (var argument in range.Arguments)
                        if (argument.Value is Name { Identifier: var bound }) numeric.Add(bound);
                    break;
            }

            foreach (var child in IrWalk.Children(expression)) Visit(child);
        }

        foreach (var expression in IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions)) Visit(expression);
        return numeric;
    }

    public int MostPaths { get; init; } = 256;

    public int MostSteps { get; init; } = 20_000;

    /// <summary>How long one function may be followed; past it the report is incomplete, like any other budget.</summary>
    public TimeSpan MostTime { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Whether a call runs one of the program's own functions whose summary says it can return null.</summary>
    public Func<Call, bool>? MayReturnNull { get; init; }

    /// <summary>Whether to keep how every path ends, for comparing two versions of a function.</summary>
    public bool KeepEnds { get; init; }

    private readonly Dictionary<string, LinearTerm> _applied = new(StringComparer.Ordinal);

    /// <summary>How many times round a loop is followed when its bound is not known.</summary>
    public int Unrolls { get; init; } = 4;

    /// <summary>The most turns of a loop with a known, fixed bound that are followed exactly.</summary>
    public int MostCountedUnrolls { get; init; } = 64;

    private bool IsPython => _language == SourceLanguage.Python;

    private sealed class Path
    {
        public ImmutableDictionary<string, SymbolicValue> Store = ImmutableDictionary<string, SymbolicValue>.Empty;
        public ImmutableList<Constraint> Constraints = [];
        public ImmutableList<Constraint> Decisions = [];
        public ImmutableDictionary<string, int> Taken = ImmutableDictionary<string, int>.Empty;
        public ImmutableDictionary<int, int> Visits = ImmutableDictionary<int, int>.Empty;
        public ImmutableDictionary<int, int> Limits = ImmutableDictionary<int, int>.Empty;
        public ImmutableDictionary<Expr, bool> Choices = NoChoices;
        public ImmutableList<string> Facts = [];
        public ImmutableList<string> NullParameters = [];
        public ImmutableList<string> NotNullParameters = [];
        public ImmutableList<SymbolicValue> Printed = [];

        /// <summary>The variables given a new value on the path - a parameter among them no longer holds what was passed.</summary>
        public ImmutableHashSet<string> Reassigned = [];

        /// <summary>
        /// For each variable holding the same collection as others - b after b = a - those others. A change made through
        /// one is a change to all of them. Always symmetric.
        /// </summary>
        public ImmutableDictionary<string, ImmutableHashSet<string>> Aliases = ImmutableDictionary.Create<string, ImmutableHashSet<string>>(StringComparer.Ordinal);

        /// <summary>How many times each line has been read from on the path, so each read has its own symbol.</summary>
        public ImmutableDictionary<(SymbolOrigin, int), int> Reads = ImmutableDictionary<(SymbolOrigin, int), int>.Empty;

        /// <summary>The number each typed text was read as, so reading the same text twice gives the same number.</summary>
        public ImmutableDictionary<(int, bool), LinearTerm> Parsed = ImmutableDictionary<(int, bool), LinearTerm>.Empty;
        public ImmutableDictionary<string, LinearTerm> Truths = ImmutableDictionary<string, LinearTerm>.Empty;
        public bool Approximated;

        /// <summary>Whether the path went some way at a loop that could have gone either way - through it a set number of times.</summary>
        public bool ThroughLoop;

        /// <summary>
        /// Whether the path depends on what code FixFinder cannot see returned - two such values can be tied together in
        /// ways no path shows, so a failure on the path is not certain to be reachable.
        /// </summary>
        public bool OutsideDecided;

        public int Block;
        public int Instruction;

        public Path Copy() => (Path)MemberwiseClone();
    }

    /// <summary>An expression whose value depends on a condition the path has not settled yet.</summary>
    private sealed class ChoiceNeeded(Expr at, Condition condition) : Exception
    {
        public Expr At { get; } = at;
        public Condition Condition { get; } = condition;
    }

    /// <summary>The path certainly fails here, so nothing after it runs.</summary>
    private sealed class PathEnded : Exception;

    public SymbolicReport Explore()
    {
        _clock.Start();
        foreach (var start in Starts()) _pending.Push(start);

        while (_pending.Count > 0)
        {
            if (_report.Paths >= MostPaths || _steps >= MostSteps || _clock.Elapsed > MostTime)
            {
                _report.Complete = false;
                break;
            }

            _report.Paths++;
            Run(_pending.Pop());
        }

        _report.Symbols = _symbols;
        return _report;
    }

    private void End(Path path, IReadOnlyList<Constraint> constraints, EndKind kind, SymbolicValue? value, string? detail, IReadOnlyList<Constraint>? failing = null)
    {
        if (!KeepEnds) return;

        _report.Ends.Add(new PathEnd(constraints, kind, value, detail, path.Printed, path.Approximated)
        {
            Decisions = failing is null ? path.Decisions : [.. path.Decisions, .. failing],
            Facts = path.Facts,
            NullParameters = path.NullParameters,
            NotNullParameters = path.NotNullParameters,
            OutsideDecided = path.OutsideDecided,
        });
    }

    private IEnumerable<Path> Starts()
    {
        var function = _graph.Function;
        List<Path> starts = [new Path()];

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var parameter = function.Parameters[i];

            if (i == 0 && IsPython && function.Owner is not null && !function.IsStatic)
            {
                foreach (var start in starts) Set(start, parameter.Name, new SymOther("self"));
                continue;
            }

            foreach (var start in starts) Set(start, parameter.Name, FromType(parameter.Type, $"`{parameter.Name}`", SymbolOrigin.Parameter, start, parameter.Name));

            if (parameter.Default is Literal { Kind: LiteralKind.Null })
            {
                var nulls = starts.Select(start =>
                {
                    var none = start.Copy();
                    Set(none, parameter.Name, SymNull.Value);
                    none.Facts = none.Facts.Add($"`{parameter.Name}` is {NullWord}");
                    none.NullParameters = none.NullParameters.Add(parameter.Name);
                    return none;
                }).ToList();
                starts.AddRange(nulls);
            }
        }

        if (!IsPython && function.Owner is not null && !function.IsStatic)
            foreach (var start in starts) Set(start, "this", Failures.ReceiverCanBeNothing(_language) ? SymUnknown.Value : new SymOther("this"));

        return starts;
    }

    private string NullWord => IsPython ? "None" : "null";

    private string TrueWord => IsPython ? "True" : "true";

    private string FalseWord => IsPython ? "False" : "false";

    private void Run(Path path)
    {
        while (true)
        {
            var block = _graph.Blocks[path.Block];

            if (path.Instruction == 0 && path.Choices.IsEmpty)
            {
                if (!_seen.Add(Fingerprint(path))) return;
                ForkToHandlers(path, block);
            }

            while (path.Instruction < block.Instructions.Count)
            {
                if (++_steps > MostSteps || _clock.Elapsed > MostTime)
                {
                    _report.Complete = false;
                    return;
                }

                var work = path.Copy();
                try
                {
                    Execute(block.Instructions[path.Instruction], work);
                }
                catch (ChoiceNeeded choice)
                {
                    Fork(path, choice);
                    return;
                }
                catch (PathEnded)
                {
                    return;
                }

                path = work;
                path.Instruction++;
                path.Choices = NoChoices;
            }

            var next = path.Copy();
            try
            {
                switch (block.Terminator)
                {
                    case Jump jump:
                        if (!Enter(next, block.Id, jump.Target)) return;
                        next.Choices = NoChoices;
                        path = next;
                        continue;

                    case Branch branch:
                        Take(next, block, branch);
                        return;

                    case Leave leave:
                        var value = leave.Value is { } returned ? Evaluate(returned, next) : null;
                        End(next, next.Constraints, EndKind.Returns, value, null);
                        return;

                    case Raise raise:
                        if (raise.Exception is { } thrown) Evaluate(thrown, next);
                        if (block.ExceptionTargets.Count == 0)
                            End(next, next.Constraints, EndKind.Raises, null, raise.Exception switch
                            {
                                NewObject made => made.Type.Name,
                                Call { CalleeName: { } name } => name,
                                Opaque { What: "AssertionError" } => "AssertionError",
                                _ => "an error",
                            });
                        foreach (var handler in block.ExceptionTargets)
                        {
                            var caught = next.Copy();
                            caught.Choices = NoChoices;
                            if (Enter(caught, block.Id, handler)) _pending.Push(caught);
                        }
                        return;

                    default:
                        return;
                }
            }
            catch (ChoiceNeeded choice)
            {
                Fork(path, choice);
                return;
            }
            catch (PathEnded)
            {
                return;
            }
        }
    }

    /// <summary>Runs the instruction again once for each way the condition can go, remembering the choice.</summary>
    private void Fork(Path path, ChoiceNeeded choice)
    {
        foreach (var holds in new[] { false, true })
        {
            var condition = holds ? choice.Condition : choice.Condition.Not();
            var cases = condition.Cases(MostCases);

            foreach (var @case in cases ?? [[]])
            {
                var forked = path.Copy();
                forked.Choices = forked.Choices.SetItem(choice.At, holds);
                if (cases is null) forked.Approximated = true;
                if (Constrain(forked, @case, decided: true)) _pending.Push(forked);
            }
        }
    }

    /// <summary>An exception anywhere in a guarded block reaches its handlers with whatever the block had assigned so far.</summary>
    private void ForkToHandlers(Path path, BasicBlock block)
    {
        if (block.ExceptionTargets.Count == 0 || block.Instructions.Count == 0) return;

        var assigned = block.Instructions.SelectMany(i => i switch
        {
            AssignInstruction assign => IrWalk.Names(assign.Target).Concat(Assigned(assign.Value)),
            DeclareInstruction declare => [declare.Variable],
            EvaluateInstruction evaluate => Assigned(evaluate.Value),
            _ => [],
        }).ToHashSet(StringComparer.Ordinal);

        foreach (var handler in block.ExceptionTargets)
        {
            var escaped = path.Copy();
            Drop(escaped, assigned);
            escaped.Approximated = true;
            if (Enter(escaped, block.Id, handler)) _pending.Push(escaped);
        }
    }

    private static IEnumerable<string> Assigned(Expr expression) =>
        expression is AssignValue assigned
            ? [.. IrWalk.Names(assigned.Target), .. Assigned(assigned.Value)]
            : IrWalk.Children(expression).SelectMany(Assigned);

    /// <summary>
    /// Moves to a block. Arriving at a loop head from before the loop starts its count afresh; arriving round the back
    /// edge counts one more turn, and a path past the loop's limit stops there - leaving the report incomplete.
    /// </summary>
    private bool Enter(Path path, int from, int target)
    {
        var head = _graph.Blocks[target];

        if (head.IsLoopHead)
        {
            if (from < target)
            {
                path.Visits = path.Visits.SetItem(target, 0);
                if (head.Terminator is Branch { Condition: MoreItems { Items: Name { Identifier: var items } } }) path.Taken = path.Taken.Remove(items);
                path.Limits = path.Limits.SetItem(target, LimitFor(head, path));
            }
            else
            {
                var turns = path.Visits.GetValueOrDefault(target) + 1;
                if (turns > path.Limits.GetValueOrDefault(target, Unrolls))
                {
                    _report.Complete = false;
                    return false;
                }

                path.Visits = path.Visits.SetItem(target, turns);
            }
        }

        path.Block = target;
        path.Instruction = 0;
        return true;
    }

    /// <summary>
    /// Automatic loop bound detection: a for-each over something of known length, or a counted loop whose counter and
    /// limit are known on entry, is followed for exactly as many turns as it takes.
    /// </summary>
    private int LimitFor(BasicBlock head, Path path)
    {
        Rational? turns = null;
        var probe = path.Copy();

        try
        {
            switch (head.Terminator)
            {
                case Branch { Condition: MoreItems more }:
                    turns = Evaluate(more.Items, probe) switch
                    {
                        SymSequence { Length.IsConstant: true } sequence => sequence.Length.Constant,
                        SymText { Length.IsConstant: true } text => text.Length.Constant,
                        SymRange { Start.IsConstant: true, Stop.IsConstant: true } range => Count(range.Start.Constant, range.Stop.Constant, range.Step),
                        _ => null,
                    };
                    break;

                case Branch { Condition: var condition } when _counted.TryGetValue(condition, out var loop):
                    if (Read(loop.Counter, probe) is SymNumber { Term.IsConstant: true } counter && Evaluate(loop.Limit, probe) is SymNumber { Term.IsConstant: true } limit)
                        turns = Turns(counter.Term.Constant, limit.Term.Constant, loop);
                    break;
            }
        }
        catch (Exception ex) when (ex is ChoiceNeeded or PathEnded)
        {
            turns = null;
        }

        return turns is { } known && known <= MostCountedUnrolls ? (int)known.Numerator + 1 : Unrolls;
    }

    private static Rational Count(Rational start, Rational stop, Rational step) =>
        step.Sign > 0 ? Max((stop - start) / step) : step.Sign < 0 ? Max((start - stop) / -step) : Rational.Zero;

    private static Rational Max(Rational turns) => turns.Sign <= 0 ? Rational.Zero : turns.Ceiling();

    private static Rational? Turns(Rational counter, Rational limit, CountedLoop loop) => loop.Comparison switch
    {
        BinaryOperator.Less when loop.Step.Sign > 0 => Max((limit - counter) / loop.Step),
        BinaryOperator.LessOrEqual when loop.Step.Sign > 0 => Max((limit - counter + loop.Step) / loop.Step),
        BinaryOperator.Greater when loop.Step.Sign < 0 => Max((counter - limit) / -loop.Step),
        BinaryOperator.GreaterOrEqual when loop.Step.Sign < 0 => Max((counter - limit - loop.Step) / -loop.Step),
        BinaryOperator.NotEqual when ((limit - counter) / loop.Step) is { IsInteger: true, Sign: >= 0 } exact => exact,
        _ => null,
    };

    private void Take(Path path, BasicBlock block, Branch branch)
    {
        if (branch.Condition is MoreItems more)
        {
            TakeItems(path, block, branch, more);
            return;
        }

        var condition = Truth(branch.Condition, path);
        Follow(path, block.Id, branch.WhenFalse, condition.Not());
        Follow(path, block.Id, branch.WhenTrue, condition);
    }

    private void Follow(Path path, int from, int target, Condition condition)
    {
        var cases = condition.Cases(MostCases);
        var loopDecides = _graph.Blocks[from].IsLoopHead && condition is not Known;

        foreach (var @case in cases ?? [[]])
        {
            var next = path.Copy();
            next.Choices = NoChoices;
            if (cases is null) next.Approximated = true;
            if (loopDecides) next.ThroughLoop = true;
            if (Constrain(next, @case, decided: true) && Enter(next, from, target)) _pending.Push(next);
        }
    }

    private void TakeItems(Path path, BasicBlock block, Branch branch, MoreItems more)
    {
        Record(more.Span);
        var name = more.Items is Name { Identifier: var held } ? held : null;
        var items = Evaluate(more.Items, path);

        if (items is SymUnknown && name is not null) items = AsSequence(more.Items, path);

        Dereferenced(more.Span, path, more.Items, items);

        var position = name is null ? 0 : path.Taken.GetValueOrDefault(name);
        var another = items switch
        {
            SymSequence sequence => Condition.Of(Constraint.AtLeast(sequence.Length, position + 1)),
            SymText text => Condition.Of(Constraint.AtLeast(text.Length, position + 1)),
            SymRange { Step.Sign: > 0 } range => Condition.Of(Constraint.Below(range.Start + LinearTerm.Of(range.Step * position), range.Stop)),
            SymRange { Step.Sign: < 0 } range => Condition.Of(Constraint.Above(range.Start + LinearTerm.Of(range.Step * position), range.Stop)),
            _ => Condition.Either,
        };

        var onward = path.Copy();
        if (name is not null) onward.Taken = onward.Taken.SetItem(name, position + 1);

        Follow(path, block.Id, branch.WhenFalse, another.Not());
        Follow(onward, block.Id, branch.WhenTrue, another);
    }

    private void Execute(Instruction instruction, Path path)
    {
        switch (instruction)
        {
            case AssignInstruction assign:
                var value = Evaluate(assign.Value, path);
                Assign(assign.Target, value, path, assign.Value);
                break;

            case EvaluateInstruction evaluate:
                Evaluate(evaluate.Value, path);
                break;

            case DeclareInstruction declare:
                Unalias(path, declare.Variable);
                Set(path, declare.Variable, SymUnknown.Value);
                path.Reassigned = path.Reassigned.Add(declare.Variable);
                break;

            case ForgetInstruction forget:
                foreach (var part in forget.Parts) Evaluate(part, path);
                Drop(path, forget.Names);
                path.Approximated = true;
                break;

            case ReleaseInstruction:
                ForgetOutside(path);
                break;
        }
    }

    /// <summary>
    /// Adds constraints to a path and keeps it only if they can still all hold. A decision - which way a branch went -
    /// is also remembered as part of how the path got where it is, for describing a witness.
    /// </summary>
    private bool Constrain(Path path, IReadOnlyList<Constraint> constraints, bool decided = false)
    {
        var added = constraints.Where(c => c.Constant != true).ToList();
        if (added.Count == 0) return true;
        if (added.Any(c => c.Constant == false)) return false;

        path.Constraints = path.Constraints.AddRange(added);
        if (decided)
        {
            path.Decisions = path.Decisions.AddRange(added);
            if (added.SelectMany(c => c.Term.Symbols).Any(id => _symbols[id].Origin is SymbolOrigin.Outside or SymbolOrigin.Approximation))
                path.OutsideDecided = true;
        }
        return !Solve(path.Constraints).IsUnsatisfiable;
    }

    private SolverResult Solve(IReadOnlyList<Constraint> constraints)
    {
        if (constraints.Count == 0) return new SolverResult(Verdict.Satisfiable, new Dictionary<int, Rational>());

        var key = string.Join(" & ", constraints.Select(c => c.ToString()).Order(StringComparer.Ordinal));
        if (!_solved.TryGetValue(key, out var result))
        {
            result = _solver.Check(constraints, _symbols.IsWhole);
            _solved[key] = result;
        }

        return result;
    }

    private string Fingerprint(Path path) =>
        $"{path.Block}|{string.Join(";", path.Store.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={Render(p.Value)}"))}" +
        $"|{string.Join("&", path.Constraints.Select(c => c.ToString()).Order(StringComparer.Ordinal))}" +
        $"|{string.Join(",", path.Taken.OrderBy(p => p.Key, StringComparer.Ordinal))}|{string.Join(",", path.Visits.OrderBy(p => p.Key))}" +
        $"|{string.Join(",", path.Limits.OrderBy(p => p.Key))}|{string.Join(",", path.Facts)}|{path.Approximated}" +
        $"|{string.Join(",", path.Truths.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"))}";

    private static string Render(SymbolicValue value) => value switch
    {
        SymNumber number => $"N({number.Term},{number.Whole})",
        SymText text => $"T({text.Length},{text.Known},{text.TypedAt})",
        SymSequence sequence => $"S({sequence.Kind},{sequence.Length},[{string.Join(",", sequence.Items?.Select(Render) ?? [])}])",
        SymRange range => $"R({range.Start},{range.Stop},{range.Step})",
        _ => value.ToString(),
    };

    /// <summary>Notes that some path evaluated this expression.</summary>
    private void Record(SourceSpan span) => _report.Evaluated.Add(span);

    private Outcome OutcomeAt(string check, SourceSpan span, Expr? culprit)
    {
        if (!_report.Outcomes.TryGetValue((check, span), out var outcome))
        {
            outcome = new Outcome { Culprit = culprit };
            _report.Outcomes[(check, span)] = outcome;
        }

        return outcome;
    }

    /// <summary>
    /// Asks whether any of the failing cases can happen on this path, keeping the first trusted witness; and whether the
    /// path fails whatever its inputs are - true when the safe case cannot hold at all.
    /// </summary>
    private void Check(string check, SourceSpan span, Path path, Expr culprit, IReadOnlyList<IReadOnlyList<Constraint>> failing, IReadOnlyList<Constraint> safe,
        Expr? collection = null)
    {
        var outcome = OutcomeAt(check, span, culprit);
        var failed = false;

        if (collection is not null && VariableOf(collection) is { } held && outcome.SharedWith.Count == 0)
        {
            var sharing = (path.Aliases.GetValueOrDefault(held) ?? []).Where(other => !other.StartsWith('$')).Order(StringComparer.Ordinal).ToList();
            if (sharing.Count > 0 && failing.Any(@case => Solve(path.Constraints.AddRange(@case)).IsSatisfiable))
            {
                outcome.Collection = held;
                outcome.SharedWith = sharing;
            }
        }

        foreach (var @case in failing)
        {
            var asked = path.Constraints.AddRange(@case);
            var result = Solve(asked);

            if (result.Verdict == Verdict.Unknown) outcome.Unsure = true;
            if (!result.IsSatisfiable) continue;

            failed = true;
            outcome.CanFail = true;
            End(path, asked, EndKind.Fails, null, $"{check}@{span.Line}", @case);
            if (!Trusted(path, asked)) continue;

            var typed = @case.SelectMany(c => c.Term.Symbols).ToList();
            if (typed.Count > 0 && typed.All(id => _symbols[id].Origin == SymbolOrigin.Input) && !outcome.FromInput)
            {
                outcome.FromInput = true;
                outcome.Witness = Describe(result.Model!, path, @case);
            }

            outcome.Witness ??= Describe(result.Model!, path, @case);
        }

        if (failed && Certainly(path) && Solve(path.Constraints.AddRange(safe)).IsUnsatisfiable) outcome.Forced = true;
    }

    /// <summary>A failure that happens on this path whatever its inputs: a null value used.</summary>
    private void Certain(string check, SourceSpan span, Path path, Expr culprit)
    {
        var outcome = OutcomeAt(check, span, culprit);
        outcome.CanFail = true;
        End(path, path.Constraints, EndKind.Fails, null, $"{check}@{span.Line}");

        if (Trusted(path, path.Constraints) && Solve(path.Constraints) is { IsSatisfiable: true } result)
        {
            outcome.Witness ??= Describe(result.Model!, path, []);
            if (Certainly(path)) outcome.Forced = true;
        }

        throw new PathEnded();
    }

    /// <summary>
    /// A path whose failure can be reported on its own: decided only by parameters, what is typed, and the function's own
    /// values - not by loop turns or by what unseen code returned, which may be tied together in ways no path shows.
    /// </summary>
    private bool Certainly(Path path) => !path.ThroughLoop && !path.OutsideDecided && Trusted(path, path.Constraints);

    private bool Trusted(Path path, IEnumerable<Constraint> constraints) =>
        !path.Approximated && constraints.SelectMany(c => c.Term.Symbols).All(id => _symbols[id].Origin != SymbolOrigin.Approximation);

    /// <summary>
    /// The witness in the program's own terms, naming only what matters: the symbols in the failing case and in the
    /// branch decisions that led here, and those tied to them by the path's constraints.
    /// </summary>
    private Witness Describe(IReadOnlyDictionary<int, Rational> model, Path path, IEnumerable<Constraint> failing)
    {
        var facts = new List<string>(path.Facts);
        var values = path.NullParameters.Select(name => new WitnessValue(name, null, WitnessKind.Nothing, Rational.Zero)).ToList();
        var relevant = failing.Concat(path.Decisions).SelectMany(c => c.Term.Symbols).ToHashSet();

        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var constraint in path.Constraints)
            {
                var symbols = constraint.Term.Symbols.ToList();
                if (symbols.Any(relevant.Contains) && !symbols.All(relevant.Contains))
                {
                    relevant.UnionWith(symbols);
                    grew = true;
                }
            }
        }

        foreach (var id in relevant.Order())
        {
            var symbol = _symbols[id];
            var value = model.GetValueOrDefault(id, Rational.Zero);

            var fact = symbol.Origin switch
            {
                SymbolOrigin.Parameter or SymbolOrigin.Outside or SymbolOrigin.Input => $"{symbol.Describes} is {value}",
                SymbolOrigin.Flag => $"{symbol.Describes} is {(value.IsZero ? FalseWord : TrueWord)}",
                SymbolOrigin.Length when value.IsZero => $"{symbol.Describes} is empty",
                SymbolOrigin.Length => $"{symbol.Describes} has {value} {(value == Rational.One ? "item" : "items")}",
                SymbolOrigin.TextLength when value.IsZero => $"{symbol.Describes} is empty",
                SymbolOrigin.TextLength => $"{symbol.Describes} has {value} {(value == Rational.One ? "character" : "characters")}",
                _ => null,
            };

            if (fact is not null && !facts.Any(known => known.StartsWith(symbol.Describes + " ", StringComparison.Ordinal))) facts.Add(fact);

            var kind = symbol.Origin switch
            {
                SymbolOrigin.Length => WitnessKind.Items,
                SymbolOrigin.TextLength => WitnessKind.Characters,
                SymbolOrigin.Flag => WitnessKind.Truth,
                SymbolOrigin.Parameter or SymbolOrigin.Input => symbol.IsWhole ? WitnessKind.WholeNumber : WitnessKind.Number,
                _ => (WitnessKind?)null,
            };

            if (kind is { } known && (symbol.TypedAt is not null || symbol.Variable is { } variable && IsParameter(variable)) &&
                !values.Any(v => v.Parameter == symbol.Variable && v.TypedAt == symbol.TypedAt))
                values.Add(new WitnessValue(symbol.TypedAt is null ? symbol.Variable : null, symbol.TypedAt, known, value));
        }

        return new Witness(facts.Take(4).ToList(), values);
    }
}
