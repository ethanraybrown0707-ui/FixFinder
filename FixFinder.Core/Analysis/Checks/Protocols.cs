using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Temporal logic checking. Two properties about the order things happen in, each checked as a small automaton run over
/// every way through a function:
/// <list type="bullet">
/// <item>□(close → □¬use) - once a file or stream is closed it is never used again;</item>
/// <item>□(acquire → ◇release) - a lock that is taken is always let go before the function is left.</item>
/// </list>
/// Each variable's possible states are carried forward through the graph and joined where ways meet; a violation is
/// reported only where every way into it agrees.
/// </summary>
public sealed class Protocols(ControlFlowGraph graph, SourceLanguage language, Func<Expr, string> quote, Action<string, SourceSpan, string, Severity, Confidence> report)
{
    private enum Phase { Open, Closed, Held, Released, Untracked }

    /// <summary>Where a tracked variable is in its protocol, and the line that put it there.</summary>
    private sealed record Mark(Phase Phase, int Line, SourceSpan At);

    private sealed class State : Dictionary<string, HashSet<Mark>>
    {
        public State() : base(StringComparer.Ordinal) { }

        public State Copy()
        {
            var copy = new State();
            foreach (var (name, marks) in this) copy[name] = [.. marks];
            return copy;
        }

        /// <summary>Joins another way in; a variable tracked on only one of them may be untracked here.</summary>
        public bool Join(State other)
        {
            var changed = false;
            foreach (var name in Keys.Union(other.Keys).ToList())
            {
                if (!TryGetValue(name, out var mine)) this[name] = mine = [new Mark(Phase.Untracked, 0, SourceSpan.None)];
                var theirs = other.TryGetValue(name, out var found) ? found : [new Mark(Phase.Untracked, 0, SourceSpan.None)];
                foreach (var mark in theirs) changed |= mine.Add(mark);
            }

            return changed;
        }
    }

    private bool IsPython => language == SourceLanguage.Python;

    private readonly HashSet<SourceSpan> _reported = [];

    /// <summary>Locks handed to other code - put in a list, passed to a call, returned - which may well release them.</summary>
    private readonly HashSet<string> _handedOn = HandedOn(graph);

    private static HashSet<string> HandedOn(ControlFlowGraph graph)
    {
        var handed = new HashSet<string>(StringComparer.Ordinal);

        void Visit(Expr expression)
        {
            if (expression is Call call)
                foreach (var argument in call.Arguments)
                    if (argument.Value is Name { Identifier: var passed }) handed.Add(passed);

            foreach (var child in IrWalk.Children(expression)) Visit(child);
        }

        foreach (var block in graph.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                if (instruction is AssignInstruction { Value: Name { Identifier: var copied } }) handed.Add(copied);
                if (instruction is AssignInstruction assign) Visit(assign.Value);
                if (instruction is EvaluateInstruction evaluate) Visit(evaluate.Value);
            }

            if (block.Terminator is Leave { Value: Name { Identifier: var returned } }) handed.Add(returned);
        }

        return handed;
    }

    public void Check()
    {
        var entry = new Dictionary<int, State> { [graph.Entry] = new State() };
        var pending = new Queue<int>([graph.Entry]);

        while (pending.Count > 0)
        {
            var block = graph.Blocks[pending.Dequeue()];
            var state = entry[block.Id].Copy();
            State? thrown = null;

            foreach (var instruction in block.Instructions)
            {
                if (MayThrow(instruction))
                {
                    if (thrown is null) thrown = state.Copy();
                    else thrown.Join(state);
                }

                Step(state, instruction, block, reporting: false);
            }

            Ending(state, block, reporting: false);

            foreach (var next in graph.Successors(block.Id))
            {
                var byException = block.ExceptionTargets.Contains(next) && block.Terminator is not Raise;
                if (byException && thrown is null) continue;
                var carried = byException ? thrown! : state;

                if (!entry.TryGetValue(next, out var known))
                {
                    entry[next] = carried.Copy();
                    pending.Enqueue(next);
                }
                else if (known.Join(carried))
                {
                    pending.Enqueue(next);
                }
            }
        }

        foreach (var block in graph.Blocks)
        {
            if (!entry.TryGetValue(block.Id, out var start)) continue;

            var state = start.Copy();
            foreach (var instruction in block.Instructions) Step(state, instruction, block, reporting: true);
            Ending(state, block, reporting: true);
        }
    }

    private void Step(State state, Instruction instruction, BasicBlock block, bool reporting)
    {
        switch (instruction)
        {
            case AssignInstruction { Target: Name { Identifier: var name }, Value: var value } assign:
                Uses(state, value, reporting);
                if (Opens(value)) state[name] = [new Mark(Phase.Open, assign.Span.Line, assign.Span)];
                else state.Remove(name);
                break;

            case AssignInstruction assign:
                Uses(state, assign.Value, reporting);
                Uses(state, assign.Target, reporting);
                break;

            case EvaluateInstruction { Value: var value }:
                if (!Moves(state, value, block, reporting)) Uses(state, value, reporting);
                break;

            case ReleaseInstruction { Resource: Name { Identifier: var released } } release when state.ContainsKey(released):
                state[released] = [new Mark(Phase.Closed, release.Span.Line, release.Span)];
                break;
        }
    }

    private void Ending(State state, BasicBlock block, bool reporting)
    {
        switch (block.Terminator)
        {
            case Branch branch:
                if (branch.Condition is MoreItems { Items: Name { Identifier: var items } } more) Use(state, items, more.Span, $"the loop over `{items}`", reporting);
                Uses(state, branch.Condition, reporting);
                break;

            case Leave leave:
                if (leave.Value is { } value) Uses(state, value, reporting);
                if (reporting) StillHeld(state, leave);
                break;
        }
    }

    /// <summary>Calls that move a variable along its protocol: close, lock and unlock. False when the value is none of them.</summary>
    private bool Moves(State state, Expr value, BasicBlock block, bool reporting)
    {
        switch (value)
        {
            case Call { Callee: Member { Target: var closed, MemberName: "close" or "Close" or "Dispose" } } close when Owner(closed) is { } owner && state.ContainsKey(owner):
                state[owner] = [new Mark(Phase.Closed, close.Span.Line, close.Span)];
                return true;

            case Call { Callee: Member { Target: var taken, MemberName: var method } } call when Acquires(method) && Owner(taken) is { } owner && !_handedOn.Contains(owner):
                state[owner] = [new Mark(Phase.Held, call.Span.Line, call.Span)];
                return true;

            case Call { Callee: Member { Target: Name { Identifier: "Monitor" }, MemberName: "Enter" }, Arguments: [{ Value: Name { Identifier: var guarded } }, ..] } enter:
                state["monitor " + guarded] = [new Mark(Phase.Held, enter.Span.Line, enter.Span)];
                return true;

            case Call { Callee: Member { Target: var released, MemberName: var method } } when Releases(method) && Owner(released) is { } owner && state.ContainsKey(owner):
                state[owner] = [new Mark(Phase.Released, 0, SourceSpan.None)];
                return true;

            case Call { Callee: Member { Target: Name { Identifier: "Monitor" }, MemberName: "Exit" }, Arguments: [{ Value: Name { Identifier: var guarded } }, ..] }:
                state["monitor " + guarded] = [new Mark(Phase.Released, 0, SourceSpan.None)];
                return true;

            case Call call when reporting && block.ExceptionTargets.Count == 0:
                LeftByException(state, call);
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// An instruction an exception can come out of: one that makes a call - other than the protocol's own lock, unlock
    /// and close, which are what is being followed. An exception carries the state from just before it.
    /// </summary>
    private bool MayThrow(Instruction instruction) => instruction switch
    {
        EvaluateInstruction { Value: Call { Callee: Member { MemberName: var method } } } when Acquires(method) || Releases(method) ||
            method is "close" or "Close" or "Dispose" or "Enter" or "Exit" => false,
        AssignInstruction assign => Calls(assign.Value) || Calls(assign.Target),
        EvaluateInstruction evaluate => Calls(evaluate.Value),
        _ => false,
    };

    private static bool Calls(Expr expression) => expression is Call or NewObject || IrWalk.Children(expression).Any(Calls);

    /// <summary>What a lock or file is held under: its own name, or the field's name when it belongs to this object.</summary>
    private static string? Owner(Expr receiver) => receiver switch
    {
        Name name => name.Identifier,
        Member { Target: Name { Identifier: "this" or "self" }, MemberName: var field } => field,
        _ => null,
    };

    /// <summary>
    /// The locks this function releases somewhere. A function that only takes a lock is taking it for whoever called it
    /// - threading's _acquire_restore does exactly that - so being left holding it is not a mistake.
    /// </summary>
    private HashSet<string> Released => _released ??= IrWalk.Statements(graph.Function.Body).SelectMany(IrWalk.Expressions)
        .Select(e => e is Call { Callee: Member { Target: var owner, MemberName: var method } } && Releases(method) ? Owner(owner) : null)
        .OfType<string>()
        .ToHashSet(StringComparer.Ordinal);

    private HashSet<string>? _released;

    private bool Acquires(string method) => IsPython ? method == "acquire" : method is "lock" or "lockInterruptibly" or "Lock" or "RLock";

    private bool Releases(string method) => IsPython ? method == "release" : method is "unlock" or "Unlock" or "RUnlock";

    /// <summary>What opens a file or stream: open(...), new FileReader(...), File.OpenText(...) and their like.</summary>
    private bool Opens(Expr value) => value switch
    {
        Call { Callee: Name { Identifier: "open" } } => IsPython,
        NewObject { Type.Name: "Scanner", Arguments: [{ Value: Member { Target: Name { Identifier: "System" }, MemberName: "in" } }] } => false,
        NewObject { Type.Name: "FileReader" or "FileWriter" or "BufferedReader" or "BufferedWriter" or "FileInputStream" or "FileOutputStream"
            or "PrintWriter" or "InputStreamReader" or "ObjectInputStream" or "ObjectOutputStream" or "Scanner" or "RandomAccessFile"
            or "StreamReader" or "StreamWriter" or "FileStream" or "BinaryReader" or "BinaryWriter" } => !IsPython,
        Call { Callee: Member { Target: Name { Identifier: "File" }, MemberName: "OpenRead" or "OpenWrite" or "OpenText" or "CreateText" or "AppendText" or "Open" or "Create" } } =>
            language == SourceLanguage.CSharp,
        _ => false,
    };

    /// <summary>Every use of a tracked file inside an expression: a method called on it.</summary>
    private void Uses(State state, Expr expression, bool reporting)
    {
        if (expression is Call { Callee: Member { Target: Name { Identifier: var owner }, MemberName: var method } } call)
            Use(state, owner, call.Span, $"`{quote(call)}`", reporting && method is not ("close" or "Close" or "Dispose" or "closed"));

        if (expression is NextItem { Items: Name { Identifier: var items } } next) Use(state, items, next.Span, $"the loop over `{items}`", reporting);

        foreach (var child in IrWalk.Children(expression)) Uses(state, child, reporting);
    }

    private void Use(State state, string name, SourceSpan span, string what, bool reporting)
    {
        if (!reporting || !state.TryGetValue(name, out var marks) || marks.Count == 0 || !marks.All(m => m.Phase == Phase.Closed)) return;
        if (!_reported.Add(span)) return;

        var closedAt = marks.Select(m => m.Line).Distinct().Order().ToList();
        var failure = language switch
        {
            SourceLanguage.Python => "ValueError: I/O operation on closed file",
            SourceLanguage.CSharp => "an ObjectDisposedException",
            _ => "an IOException (Stream closed)",
        };

        report("analysis-used-after-close", span,
            $"`{name}` was closed on line {string.Join(" or ", closedAt)}, so {what} fails with {failure}", Severity.Error, Confidence.Certain);
    }

    private void StillHeld(State state, Leave leave)
    {
        foreach (var (name, marks) in state)
        {
            var lockedFor = name.StartsWith("monitor ", StringComparison.Ordinal) ? name["monitor ".Length..] : name;
            if (!Released.Contains(lockedFor)) continue;

            foreach (var held in marks.Where(m => m.Phase == Phase.Held))
            {
                if (!_reported.Add(held.At)) continue;
                var lockName = name.StartsWith("monitor ", StringComparison.Ordinal) ? name["monitor ".Length..] : name;
                report("analysis-lock-not-released", held.At,
                    $"The lock `{lockName}` taken here is still held when the function returns on line {leave.Span.Line}, so everything else that needs it waits for ever",
                    Severity.Warning, Confidence.Likely);
            }
        }
    }

    /// <summary>A call made while holding a lock, outside any try: if it throws, the unlock after it never runs.</summary>
    private void LeftByException(State state, Call call)
    {
        if (!Failures.CallsCanThrow(language)) return;

        foreach (var (name, marks) in state)
        {
            if (marks.Count == 0 || !marks.All(m => m.Phase == Phase.Held)) continue;

            var held = marks.First();
            if (!_reported.Add(held.At)) continue;

            var lockName = name.StartsWith("monitor ", StringComparison.Ordinal) ? name["monitor ".Length..] : name;
            report("analysis-lock-not-released", held.At,
                $"If `{quote(call)}` throws, the lock `{lockName}` taken here is never released - release it in a finally block",
                Severity.Warning, Confidence.Possible);
        }
    }
}
