using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Flow;

/// <summary>Lowers a function's structured statements into basic blocks joined by jumps and branches.</summary>
public static class CfgBuilder
{
    public static ControlFlowGraph Build(IrFunction function) => new Lowering(function).Run();

    private abstract record Region;

    private sealed record LoopRegion(int BreakTarget, int ContinueTarget, string? Label = null) : Region;

    private sealed record SwitchRegion(int BreakTarget) : Region;

    private sealed record LabelRegion(string Label, int BreakTarget) : Region;

    private sealed record CleanupRegion(Action Emit, int HandlerDepth) : Region;

    private sealed class Lowering(IrFunction function)
    {
        private readonly List<BasicBlock> _blocks = [];
        private List<Region> _regions = [];
        private List<IReadOnlyList<int>> _handlers = [];
        private BasicBlock? _current;
        private int _temporaries;
        private string? _labelForNextLoop;
        private bool _testingCases;

        public ControlFlowGraph Run()
        {
            _current = NewBlock();
            Lower(function.Body);
            if (_current is not null) Seal(new Leave(EndOf(function), null));

            var normalExit = NewBlock();
            normalExit.Terminator = new Finish(function.Span);
            var errorExit = NewBlock();
            errorExit.Terminator = new Finish(function.Span);

            return new ControlFlowGraph { Function = function, Blocks = _blocks, NormalExit = normalExit.Id, ErrorExit = errorExit.Id };
        }

        private static SourceSpan EndOf(IrFunction function) =>
            function.Span with { Line = Math.Max(function.Span.EndLine, function.Span.Line), Column = 0 };

        private BasicBlock NewBlock()
        {
            var block = new BasicBlock(_blocks.Count) { ExceptionTargets = _handlers.Count > 0 ? _handlers[^1] : [] };
            _blocks.Add(block);
            return block;
        }

        private BasicBlock Current => _current ??= NewBlock();

        private void Emit(Instruction instruction) => Current.Instructions.Add(instruction);

        private void Seal(Terminator terminator)
        {
            Current.Terminator = terminator;
            _current = null;
        }

        private void JumpTo(BasicBlock target, SourceSpan span)
        {
            if (_current is not null) Seal(new Jump(span, target.Id));
        }

        private Name Temporary(SourceSpan span, string purpose) => new(span, $"${purpose}{_temporaries++}");

        private void Lower(IReadOnlyList<Stmt> statements)
        {
            foreach (var statement in statements) Lower(statement);
        }

        private void Lower(Stmt statement)
        {
            switch (statement)
            {
                case Evaluate evaluate:
                    Emit(new EvaluateInstruction(evaluate.Span, evaluate.Value));
                    break;

                case Assign assign:
                    var value = assign.Compound is { } compound ? new Binary(assign.Span, compound, assign.Target, assign.Value) : assign.Value;
                    Emit(new AssignInstruction(assign.Span, assign.Target, value));
                    break;

                case Declare declare:
                    Emit(new DeclareInstruction(declare.Span, declare.Variable, declare.Type) { Lifetime = declare.Lifetime });
                    if (declare.Initial is { } initial) Emit(new AssignInstruction(declare.Span, new Name(declare.Span, declare.Variable), initial));
                    break;

                case OpaqueStmt opaque:
                    Emit(new ForgetInstruction(opaque.Span, opaque.MayAssign, opaque.Parts));
                    break;

                case If branch:
                    LowerIf(branch);
                    break;

                case While loop:
                    LowerWhile(loop);
                    break;

                case For loop:
                    LowerFor(loop);
                    break;

                case ForEach loop:
                    LowerForEach(loop);
                    break;

                case Switch choice:
                    LowerSwitch(choice);
                    break;

                case Return leave:
                    LowerReturn(leave);
                    break;

                case Break exit:
                    bool Leaves(Region region) => exit.Label is null
                        ? region is LoopRegion or SwitchRegion
                        : region is LoopRegion { Label: var loopLabel } && loopLabel == exit.Label || region is LabelRegion { Label: var name } && name == exit.Label;
                    RunCleanups(Leaves, exit.Span);
                    if (_current is not null && Innermost<Region>(Leaves) is { } left)
                        Seal(new Jump(exit.Span, left switch
                        {
                            LoopRegion loop => loop.BreakTarget,
                            SwitchRegion choice => choice.BreakTarget,
                            _ => ((LabelRegion)left).BreakTarget,
                        }));
                    else if (_current is not null)
                        Seal(new Leave(exit.Span, null));
                    break;

                case Continue next:
                    bool Repeats(Region region) => region is LoopRegion { Label: var loopLabel } && (next.Label is null || loopLabel == next.Label);
                    RunCleanups(Repeats, next.Span);
                    if (_current is not null && Innermost<LoopRegion>(Repeats) is { } enclosing)
                        Seal(new Jump(next.Span, enclosing.ContinueTarget));
                    else if (_current is not null)
                        Seal(new Leave(next.Span, null));
                    break;

                case Labeled labeled when labeled.Body is [While or For or ForEach]:
                    _labelForNextLoop = labeled.Label;
                    Lower(labeled.Body);
                    break;

                case Labeled labeled:
                    var afterLabeled = NewBlock();
                    InRegion(new LabelRegion(labeled.Label, afterLabeled.Id), () => Lower(labeled.Body));
                    JumpTo(afterLabeled, labeled.Span);
                    _current = afterLabeled;
                    break;

                case Throw raise:
                    Seal(new Raise(raise.Span, raise.Exception));
                    break;

                case AssertThat check:
                    var passes = NewBlock();
                    var fails = NewBlock();
                    Condition(check.Condition, passes.Id, fails.Id);
                    _current = fails;
                    Seal(new Raise(check.Span, check.Message is { } message
                        ? Opaque.Of(check.Span, "AssertionError", message)
                        : Opaque.Of(check.Span, "AssertionError")));
                    _current = passes;
                    break;

                case Using used:
                    if (used.Variable is { } variable) Emit(new AssignInstruction(used.Span, variable, used.Resource));
                    else Emit(new EvaluateInstruction(used.Span, used.Resource));
                    var released = used.Variable ?? used.Resource;
                    LowerProtected(used.Span, () => Lower(used.Body), [], [], () => Emit(new ReleaseInstruction(used.Span, released)));
                    break;

                case Try attempt:
                    LowerProtected(attempt.Span, () => Lower(attempt.Body), attempt.Handlers, attempt.Else,
                        attempt.Finally.Count > 0 ? () => Lower(attempt.Finally) : null);
                    break;

                default:
                    throw new NotSupportedException($"No lowering for {statement.GetType().Name}");
            }
        }

        private void LowerIf(If branch)
        {
            var then = NewBlock();
            var otherwise = branch.Else.Count > 0 ? NewBlock() : null;
            var after = NewBlock();

            Condition(branch.Condition, then.Id, (otherwise ?? after).Id);

            _current = then;
            Lower(branch.Then);
            JumpTo(after, branch.Span);

            if (otherwise is not null)
            {
                _current = otherwise;
                Lower(branch.Else);
                JumpTo(after, branch.Span);
            }

            _current = after;
        }

        private void LowerWhile(While loop)
        {
            var head = NewBlock();
            head.IsLoopHead = true;
            var body = NewBlock();
            var otherwise = loop.Else.Count > 0 ? NewBlock() : null;
            var after = NewBlock();

            JumpTo(loop.TestsFirst ? head : body, loop.Span);

            _current = head;
            Condition(loop.Condition, body.Id, (otherwise ?? after).Id);

            _current = body;
            InRegion(new LoopRegion(after.Id, head.Id, TakeLabel()), () => Lower(loop.Body));
            JumpTo(head, loop.Span);

            if (otherwise is not null)
            {
                _current = otherwise;
                Lower(loop.Else);
                JumpTo(after, loop.Span);
            }

            _current = after;
        }

        private string? TakeLabel()
        {
            var label = _labelForNextLoop;
            _labelForNextLoop = null;
            return label;
        }

        private void LowerFor(For loop)
        {
            var label = TakeLabel();
            Lower(loop.Setup);

            var head = NewBlock();
            head.IsLoopHead = true;
            var body = NewBlock();
            var step = NewBlock();
            var after = NewBlock();

            JumpTo(head, loop.Span);

            _current = head;
            if (loop.Condition is { } condition) Condition(condition, body.Id, after.Id);
            else JumpTo(body, loop.Span);

            _current = body;
            InRegion(new LoopRegion(after.Id, step.Id, label), () => Lower(loop.Body));
            JumpTo(step, loop.Span);

            _current = step;
            Lower(loop.Step);
            JumpTo(head, loop.Span);

            _current = after;
        }

        private void LowerForEach(ForEach loop)
        {
            var label = TakeLabel();
            var items = loop.Items;
            if (items is not (Name or Literal))
            {
                var held = Temporary(loop.Span, "items");
                Emit(new AssignInstruction(loop.Span, held, items));
                items = held;
            }

            var head = NewBlock();
            head.IsLoopHead = true;
            var body = NewBlock();
            var otherwise = loop.Else.Count > 0 ? NewBlock() : null;
            var after = NewBlock();

            JumpTo(head, loop.Span);

            _current = head;
            Seal(new Branch(loop.Span, new MoreItems(loop.Span, items), body.Id, (otherwise ?? after).Id));

            _current = body;
            Emit(new AssignInstruction(loop.Span, loop.Target, new NextItem(loop.Span, items)));
            InRegion(new LoopRegion(after.Id, head.Id, label), () => Lower(loop.Body));
            JumpTo(head, loop.Span);

            if (otherwise is not null)
            {
                _current = otherwise;
                Lower(loop.Else);
                JumpTo(after, loop.Span);
            }

            _current = after;
        }

        private void LowerSwitch(Switch choice)
        {
            var subject = choice.Subject;
            if (subject is not (Name or Literal))
            {
                var held = Temporary(choice.Span, "subject");
                Emit(new AssignInstruction(choice.Span, held, subject));
                subject = held;
            }

            var after = NewBlock();
            var bodies = choice.Cases.Select(_ => NewBlock()).ToList();
            var fallback = choice.Cases.ToList().FindIndex(c => c.Labels.Count == 0);

            for (var i = 0; i < choice.Cases.Count; i++)
            {
                if (choice.Cases[i].Labels.Count == 0) continue;

                var matches = choice.Cases[i].Labels
                    .Select(label => (Expr)new Binary(label.Span, BinaryOperator.Equal, subject, label))
                    .Aggregate((left, right) => new Binary(choice.Span, BinaryOperator.Or, left, right));

                var next = NewBlock();
                _testingCases = true;
                Condition(matches, bodies[i].Id, next.Id);
                _testingCases = false;
                _current = next;
            }

            JumpTo(fallback >= 0 ? bodies[fallback] : after, choice.Span);

            InRegion(new SwitchRegion(after.Id), () =>
            {
                for (var i = 0; i < choice.Cases.Count; i++)
                {
                    _current = bodies[i];
                    Lower(choice.Cases[i].Body);
                    JumpTo(choice.Cases[i].FallsThrough && i + 1 < bodies.Count ? bodies[i + 1] : after, choice.Span);
                }
            });

            _current = after;
        }

        private void LowerReturn(Return leave)
        {
            var value = leave.Value;

            if (value is not null and not (Name or Literal) && _regions.Any(r => r is CleanupRegion))
            {
                var held = Temporary(leave.Span, "result");
                Emit(new AssignInstruction(leave.Span, held, value));
                value = held;
            }

            RunCleanups(_ => false, leave.Span);
            if (_current is not null) Seal(new Leave(leave.Span, value));
        }

        private void LowerProtected(SourceSpan span, Action body, IReadOnlyList<Handler> handlers, IReadOnlyList<Stmt> otherwise, Action? cleanup)
        {
            var after = NewBlock();
            var handlerBlocks = handlers.Select(_ => NewBlock()).ToList();
            var rethrow = cleanup is not null ? NewBlock() : null;
            IReadOnlyList<int> targets = [.. handlerBlocks.Select(b => b.Id), .. rethrow is null ? [] : new[] { rethrow.Id }];

            if (cleanup is not null) _regions.Add(new CleanupRegion(cleanup, _handlers.Count));

            _handlers.Add(targets);
            var bodyEntry = NewBlock();
            JumpTo(bodyEntry, span);
            _current = bodyEntry;
            body();
            _handlers.RemoveAt(_handlers.Count - 1);

            if (_current is not null && (otherwise.Count > 0 || cleanup is not null))
            {
                var unguarded = NewBlock();
                JumpTo(unguarded, span);
                _current = unguarded;
            }

            Lower(otherwise);

            if (cleanup is not null) _regions.RemoveAt(_regions.Count - 1);
            if (_current is not null) cleanup?.Invoke();
            JumpTo(after, span);

            for (var i = 0; i < handlers.Count; i++)
            {
                _current = handlerBlocks[i];
                if (rethrow is not null)
                {
                    handlerBlocks[i].ExceptionTargets = [rethrow.Id];
                    _handlers.Add([rethrow.Id]);
                    _regions.Add(new CleanupRegion(cleanup!, _handlers.Count - 1));
                }

                if (handlers[i].Variable is { } variable)
                    Emit(new AssignInstruction(handlers[i].Span, new Name(handlers[i].Span, variable),
                        Opaque.Of(handlers[i].Span, "caught " + string.Join(" or ", handlers[i].ExceptionTypes))));

                Lower(handlers[i].Body);

                if (rethrow is not null)
                {
                    _regions.RemoveAt(_regions.Count - 1);
                    _handlers.RemoveAt(_handlers.Count - 1);
                }

                if (_current is not null) cleanup?.Invoke();
                JumpTo(after, handlers[i].Span);
            }

            if (rethrow is not null)
            {
                _current = rethrow;
                cleanup!();
                if (_current is not null) Seal(new Raise(span, null));
            }

            _current = after;
        }

        private void InRegion(Region region, Action lower)
        {
            _regions.Add(region);
            lower();
            _regions.RemoveAt(_regions.Count - 1);
        }

        private T? Innermost<T>(Func<Region, bool> matches) where T : Region =>
            _regions.LastOrDefault(matches) as T;

        private void RunCleanups(Func<Region, bool> stopAt, SourceSpan span)
        {
            for (var i = _regions.Count - 1; i >= 0 && _current is not null; i--)
            {
                if (stopAt(_regions[i])) return;
                if (_regions[i] is not CleanupRegion cleanup) continue;

                var savedRegions = _regions;
                var savedHandlers = _handlers;
                _regions = savedRegions.Take(i).ToList();
                _handlers = savedHandlers.Take(cleanup.HandlerDepth).ToList();

                cleanup.Emit();

                _regions = savedRegions;
                _handlers = savedHandlers;
            }
        }

        /// <summary>Branches on a condition, splitting and, or and not so every branch tests one simple condition.</summary>
        private void Condition(Expr condition, int whenTrue, int whenFalse)
        {
            switch (condition)
            {
                case Unary { Operator: UnaryOperator.Not } negation:
                    Condition(negation.Operand, whenFalse, whenTrue);
                    break;

                case Binary { Operator: BinaryOperator.And } both:
                    var second = NewBlock();
                    Condition(both.Left, second.Id, whenFalse);
                    _current = second;
                    Condition(both.Right, whenTrue, whenFalse);
                    break;

                case Binary { Operator: BinaryOperator.Or } either:
                    var alternative = NewBlock();
                    Condition(either.Left, whenTrue, alternative.Id);
                    _current = alternative;
                    Condition(either.Right, whenTrue, whenFalse);
                    break;

                default:
                    Seal(new Branch(condition.Span, condition, whenTrue, whenFalse) { TestsACase = _testingCases });
                    break;
            }
        }
    }
}
