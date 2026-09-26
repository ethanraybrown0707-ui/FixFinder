using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Memory safety for C and C++, as a small automaton run over every way through a function: memory that is freed and
/// then used, freed twice, or never freed at all; the address of a local handed back to the caller; and a variable read
/// before anything has been put in it. What is reported is only what every way into a line agrees on - where the ways
/// disagree the finding is possible rather than certain, and where a pointer is handed to other code it is left alone.
/// </summary>
public sealed class MemorySafety(
    ControlFlowGraph graph,
    SourceLanguage language,
    Func<Expr, string> quote,
    Action<string, SourceSpan, string, Severity, Confidence> report)
{
    private static readonly HashSet<string> Allocators = new(StringComparer.Ordinal) { "malloc", "calloc", "realloc", "strdup", "strndup", "aligned_alloc" };

    private enum Phase { Held, Freed, Unset, Gone }

    /// <summary>Where a pointer or variable is, and the line that put it there.</summary>
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

        public bool Join(State other)
        {
            var changed = false;
            foreach (var name in Keys.Union(other.Keys).ToList())
            {
                if (!TryGetValue(name, out var mine)) this[name] = mine = [new Mark(Phase.Gone, 0, SourceSpan.None)];
                var theirs = other.TryGetValue(name, out var found) ? found : [new Mark(Phase.Gone, 0, SourceSpan.None)];
                foreach (var mark in theirs) changed |= mine.Add(mark);
            }

            return changed;
        }
    }

    private readonly HashSet<SourceSpan> _reported = [];

    /// <summary>Pointers handed to other code, stored in something else or given back: whoever has them now owns them.</summary>
    private readonly HashSet<string> _handedOn = HandedOn(graph, ownership: true);

    /// <summary>Variables whose address was taken, which whatever was given the pointer may have filled in.</summary>
    private readonly HashSet<string> _filledIn = HandedOn(graph, ownership: false);

    /// <summary>
    /// The locals whose memory is this call's own and goes when it returns, with their types. A global, a static and a
    /// C++ reference are not among them: their memory outlasts the call or belongs to something else.
    /// </summary>
    private readonly Dictionary<string, IrType> _callsOwn = IrWalk.Statements(graph.Function.Body).OfType<Declare>()
        .Where(declare => declare.Lifetime == Lifetime.Call)
        .GroupBy(declare => declare.Variable, StringComparer.Ordinal)
        .ToDictionary(declared => declared.Key, declared => declared.First().Type, StringComparer.Ordinal);

    public void Check()
    {
        if (language is not (SourceLanguage.C or SourceLanguage.Cpp)) return;

        var entry = new Dictionary<int, State> { [graph.Entry] = Start() };
        var pending = new Queue<int>([graph.Entry]);
        var rounds = 0;

        while (pending.Count > 0 && rounds++ < graph.Blocks.Count * 8)
        {
            var block = graph.Blocks[pending.Dequeue()];
            var state = entry[block.Id].Copy();

            foreach (var instruction in block.Instructions) Step(state, instruction, reporting: false);
            Ends(state, block, reporting: false);

            foreach (var next in graph.Successors(block.Id))
            {
                var arriving = Arriving(state, block, next);

                if (!entry.TryGetValue(next, out var known))
                {
                    entry[next] = arriving;
                    pending.Enqueue(next);
                    continue;
                }

                if (known.Join(arriving)) pending.Enqueue(next);
            }
        }

        foreach (var block in graph.Blocks)
        {
            if (!entry.TryGetValue(block.Id, out var known)) continue;

            var state = known.Copy();
            foreach (var instruction in block.Instructions) Step(state, instruction, reporting: true);
            Ends(state, block, reporting: true);
            Ending(state, block);
        }
    }

    /// <summary>
    /// What the end of a block reads - the condition it branches on, the value it returns, what it throws - which is
    /// code like any other: return node->key after free(node) reads freed memory as surely as a statement would.
    /// </summary>
    private void Ends(State state, BasicBlock block, bool reporting)
    {
        var read = block.Terminator switch
        {
            Branch branch => branch.Condition,
            Leave { Value: { } returned } => returned,
            Raise { Exception: { } raised } => raised,
            _ => null,
        };

        if (read is not null) Uses(state, read, reporting);
    }

    /// <summary>The state as it reaches one successor, with anything that edge proves holds nothing dropped.</summary>
    private static State Arriving(State state, BasicBlock block, int next)
    {
        var arriving = state.Copy();
        if (ProvenEmpty(block, next) is { } name) arriving.Remove(name);
        return arriving;
    }

    /// <summary>The pointer an edge out of this block proves is null, if it proves anything at all.</summary>
    /// <remarks>
    /// <c>if (p == NULL) return 1;</c> is how careful C checks that an allocation worked, and down the branch it takes
    /// there is nothing allocated - that is what the branch established. Carrying the same state down both edges makes
    /// that return look like a way out of the function with memory still held, so the idiom is reported as the very leak
    /// it exists to prevent. Following the condition on the edge is the whole fix: everywhere else the two paths still
    /// join, so a genuine leak down the other branch is still found.
    /// </remarks>
    private static string? ProvenEmpty(BasicBlock block, int next)
    {
        if (block.Terminator is not Branch branch) return null;

        var takenWhenTrue = next == branch.WhenTrue;
        if (!takenWhenTrue && next != branch.WhenFalse) return null;

        return branch.Condition switch
        {
            Binary { Operator: BinaryOperator.Equal } equal when takenWhenTrue => ComparedWithNull(equal),
            Binary { Operator: BinaryOperator.NotEqual } unequal when !takenWhenTrue => ComparedWithNull(unequal),
            Unary { Operator: UnaryOperator.Not, Operand: Name negated } when takenWhenTrue => negated.Identifier,
            Name bare when !takenWhenTrue => bare.Identifier,
            _ => null,
        };
    }

    /// <summary>The name in a comparison against null, whichever side it is written on.</summary>
    private static string? ComparedWithNull(Binary comparison) => (comparison.Left, comparison.Right) switch
    {
        (Name name, Literal { Kind: LiteralKind.Null }) => name.Identifier,
        (Literal { Kind: LiteralKind.Null }, Name name) => name.Identifier,
        _ => null,
    };

    /// <summary>Every local that starts with nothing in it, which reading before writing is a mistake.</summary>
    private State Start()
    {
        var state = new State();

        foreach (var declare in IrWalk.Statements(graph.Function.Body).OfType<Declare>())
        {
            if (declare.Initial is not null || declare.Lifetime != Lifetime.Call || !Plain(declare.Type) || _filledIn.Contains(declare.Variable)) continue;
            state[declare.Variable] = [new Mark(Phase.Unset, declare.Span.Line, declare.Span)];
        }

        return state;
    }

    /// <param name="ownership">True for the pointers other code may now own, false for only the variables whose address was taken.</param>
    /// <summary>
    /// A type FixFinder knows the whole of: a number, a truth value or a pointer. A struct is given its parts one at a
    /// time, and a library type such as va_list is filled in by code FixFinder cannot see, so neither counts as unset.
    /// </summary>
    private static bool Plain(IrType type) =>
        type.Name.EndsWith('*') ||
        type.Name is "int" or "long" or "short" or "sbyte" or "byte" or "ushort" or "uint" or "ulong" or "double" or "float" or "bool" or "char";

    private static HashSet<string> HandedOn(ControlFlowGraph graph, bool ownership)
    {
        var handed = new HashSet<string>(StringComparer.Ordinal);

        void Visit(Expr expression, bool taking)
        {
            switch (expression)
            {
                case Call { CalleeName: "free" or "delete" }:
                    break;
                case Call call when ownership:
                    foreach (var argument in call.Arguments)
                        if (Root(argument.Value) is { } passed) handed.Add(passed);
                    break;
                case Opaque { What: "address of", Parts: [var addressed] } when Root(addressed) is { } pointed:
                    handed.Add(pointed);
                    break;

                // C writes a store as an expression too: node->next = made, list[0] = made.
                case AssignValue { Target: not Name, Value: Name { Identifier: var stored } } when ownership:
                    handed.Add(stored);
                    break;
            }

            foreach (var child in IrWalk.Children(expression)) Visit(child, taking);
        }

        foreach (var block in graph.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                switch (instruction)
                {
                    case AssignInstruction { Target: not Name, Value: Name { Identifier: var stored } } when ownership:
                        handed.Add(stored);
                        break;
                    case AssignInstruction assign:
                        Visit(assign.Value, taking: true);
                        if (assign.Target is not Name) Visit(assign.Target, taking: false);
                        break;
                    case EvaluateInstruction evaluate:
                        Visit(evaluate.Value, taking: true);
                        break;
                }
            }

            // A condition is code too: while (pop(&stack, &value)) hands value's address to pop as surely as a statement would.
            switch (block.Terminator)
            {
                case Branch { Condition: var condition }:
                    Visit(condition, taking: true);
                    break;
                case Leave { Value: { } returning }:
                    Visit(returning, taking: true);
                    break;
                case Raise { Exception: { } raised }:
                    Visit(raised, taking: true);
                    break;
            }

            if (ownership && block.Terminator is Leave { Value: { } returned } && Root(returned) is { } given) handed.Add(given);
        }

        return handed;
    }

    private static string? Root(Expr expression) => expression switch
    {
        Name name => name.Identifier,
        Member member => Root(member.Target),
        ElementAccess element => Root(element.Target),
        Cast cast => Root(cast.Value),
        _ => null,
    };

    /// <summary>
    /// The memory an expression names, as far as it can be told apart: a variable, or a field reached through one - node,
    /// node->next, node->next->key. A field is memory of its own: free(node->key) frees the key and leaves the node alone.
    /// </summary>
    private static string? PathOf(Expr expression) => expression switch
    {
        Name name => name.Identifier,
        Member { MemberName: var field } member when PathOf(member.Target) is { } holder => $"{holder}.{field}",
        Cast cast => PathOf(cast.Value),
        _ => null,
    };

    private static Expr Uncast(Expr expression) => expression is Cast cast ? Uncast(cast.Value) : expression;

    /// <summary>How a field that was freed is written in the code, for the findings about it: vector->items, not vector.items.</summary>
    private readonly Dictionary<string, string> _shown = new(StringComparer.Ordinal);

    /// <summary>
    /// A variable or field as the code writes it. A variable declared again inside a block has a ' after its name in the
    /// IR, which no C name can contain, so taking it off gives back the name the code uses.
    /// </summary>
    private string Shown(string path) => _shown.GetValueOrDefault(path) ?? path.TrimEnd('\'');

    /// <summary>A pointer given a new value: whatever was freed through its old value no longer has anything to do with it.</summary>
    private static void Rewritten(State state, string path)
    {
        foreach (var inner in state.Keys.Where(key => key.StartsWith(path + ".", StringComparison.Ordinal)).ToList()) state.Remove(inner);
    }

    /// <summary>A read through a pointer: every pointer on the way to what is read must still point at memory - node, then node->next.</summary>
    private void ReadingThrough(State state, Expr pointer, SourceSpan span, string what, bool reporting)
    {
        if (PathOf(pointer) is not { } path)
        {
            if (Root(pointer) is { } root) Reading(state, root, span, what, reporting);
            return;
        }

        var steps = path.Split('.');
        for (var taken = 1; taken <= steps.Length; taken++) Reading(state, string.Join('.', steps.Take(taken)), span, what, reporting);
    }

    private void Step(State state, Instruction instruction, bool reporting)
    {
        switch (instruction)
        {
            case AssignInstruction assign:
                Uses(state, assign.Value, reporting);
                if (assign.Target is not Name) Uses(state, assign.Target, reporting);

                if (assign.Target is Name { Identifier: var name })
                {
                    state[name] = Allocated(assign.Value)
                        ? [new Mark(Phase.Held, assign.Span.Line, assign.Span)]
                        : [new Mark(Phase.Gone, assign.Span.Line, assign.Span)];
                    Rewritten(state, name);
                }
                else if (PathOf(assign.Target) is { } field)
                {
                    state[field] = [new Mark(Phase.Gone, assign.Span.Line, assign.Span)];
                    Rewritten(state, field);
                }

                break;

            case EvaluateInstruction evaluate:
                Uses(state, evaluate.Value, reporting);
                break;

            case DeclareInstruction declare:
                // A static starts at zero, or holds what the last call left in it; a reference is bound to what it names.
                state[declare.Variable] = declare.Lifetime != Lifetime.Call || _filledIn.Contains(declare.Variable) || !Plain(declare.Type)
                    ? [new Mark(Phase.Gone, declare.Span.Line, declare.Span)]
                    : [new Mark(Phase.Unset, declare.Span.Line, declare.Span)];
                Rewritten(state, declare.Variable);
                break;

            case ForgetInstruction forget:
                foreach (var forgotten in forget.Names) state[forgotten] = [new Mark(Phase.Gone, 0, SourceSpan.None)];
                break;
        }
    }

    private static bool Allocated(Expr value) => value switch
    {
        Call { Callee: Name { Identifier: var called } } => Allocators.Contains(called),
        NewObject { Type.Name: not "array" } => false,
        Cast cast => Allocated(cast.Value),
        _ => false,
    };

    /// <summary>Every use of a tracked variable inside an expression, in the order they happen.</summary>
    private void Uses(State state, Expr expression, bool reporting)
    {
        switch (expression)
        {
            case Call { Callee: Name { Identifier: "free" or "delete" }, Arguments: [{ Value: var freed }, ..] } call when PathOf(freed) is { } path:
                // Freeing a field reads the pointer that holds it first: free(node->key) goes through node.
                if (Uncast(freed) is Member { Target: var holder } freedField)
                {
                    Uses(state, holder, reporting);
                    _shown[path] = quote(freedField);
                }

                Freeing(state, path, call.Span, reporting);
                return;

            // C writes assignment inside expressions; what is written into is not read, unless it is reached through a pointer.
            case AssignValue { Target: Name written } assigned:
                Uses(state, assigned.Value, reporting);
                state[written.Identifier] = Allocated(assigned.Value)
                    ? [new Mark(Phase.Held, assigned.Span.Line, assigned.Span)]
                    : [new Mark(Phase.Gone, assigned.Span.Line, assigned.Span)];
                Rewritten(state, written.Identifier);
                return;

            case AssignValue assigned:
                Uses(state, assigned.Value, reporting);
                Uses(state, assigned.Target, reporting);
                if (PathOf(assigned.Target) is { } field)
                {
                    state[field] = [new Mark(Phase.Gone, assigned.Span.Line, assigned.Span)];
                    Rewritten(state, field);
                }

                return;

            case Member { MemberName: "*" } through:
                ReadingThrough(state, through.Target, through.Span, $"`{quote(through)}`", reporting);
                break;

            case Member member:
                ReadingThrough(state, member.Target, member.Span, $"`{quote(member)}`", reporting);
                break;

            case ElementAccess element:
                ReadingThrough(state, element.Target, element.Span, $"`{quote(element)}`", reporting);
                break;

            case Name name:
                Reading(state, name.Identifier, name.Span, $"`{name.Identifier}`", reporting, whole: true);
                break;
        }

        foreach (var child in IrWalk.Children(expression)) Uses(state, child, reporting);
    }

    private void Freeing(State state, string name, SourceSpan span, bool reporting)
    {
        if (state.TryGetValue(name, out var marks) && marks.Count > 0 && marks.All(m => m.Phase == Phase.Freed) && reporting && _reported.Add(span))
        {
            var lines = marks.Select(m => m.Line).Distinct().Order().ToList();
            report("analysis-double-free", span,
                $"`{Shown(name)}` was already freed on line {string.Join(" or ", lines)}, so freeing it again is undefined behaviour - it usually stops the program",
                Severity.Error, Confidence.Certain);
        }

        state[name] = [new Mark(Phase.Freed, span.Line, span)];
    }

    /// <param name="whole">Whether the value itself was read, rather than something reached through it.</param>
    private void Reading(State state, string name, SourceSpan span, string what, bool reporting, bool whole = false)
    {
        if (!reporting || !state.TryGetValue(name, out var marks) || marks.Count == 0) return;

        if (marks.All(m => m.Phase == Phase.Freed))
        {
            if (!_reported.Add(span)) return;
            var freedAt = marks.Select(m => m.Line).Distinct().Order().ToList();
            report("analysis-use-after-free", span,
                $"`{Shown(name)}` was freed on line {string.Join(" or ", freedAt)}, so {what} goes through memory that is no longer there - undefined behaviour",
                Severity.Error, Confidence.Certain);
            return;
        }

        if (!whole || !marks.All(m => m.Phase == Phase.Unset)) return;
        if (!_reported.Add(span)) return;

        report("analysis-uninitialised-read", span,
            $"`{Shown(name)}` has not been given a value yet, so {what} reads whatever happened to be in that memory",
            Severity.Error, Confidence.Certain);
    }

    /// <summary>
    /// The local whose own memory an address is in - the variable itself, or an element of an array declared in it - or
    /// null when it may be anywhere else. An element or field reached through a pointer is wherever the pointer points,
    /// and a struct's field is left out too: a typedef can hide the pointer that p-&gt;next goes through.
    /// </summary>
    private string? CallsOwn(Expr addressed) => addressed switch
    {
        Name name when _callsOwn.ContainsKey(name.Identifier) => name.Identifier,
        ElementAccess { Target: Name array } when _callsOwn.TryGetValue(array.Identifier, out var type) && type.Name == "array" => array.Identifier,
        Cast cast => CallsOwn(cast.Value),
        _ => null,
    };

    /// <summary>What is left when a function returns: memory nobody freed, and the address of something that is about to go.</summary>
    private void Ending(State state, BasicBlock block)
    {
        if (block.Terminator is Leave leave)
        {
            if (leave.Value is Opaque { What: "address of", Parts: [var addressed] } && CallsOwn(addressed) is { } local && _reported.Add(leave.Span))
            {
                report("analysis-dangling-pointer", leave.Span,
                    $"`{Shown(local)}` belongs to this function and is gone once it returns, so the address given back points at memory that is no longer there",
                    Severity.Error, Confidence.Certain);
            }

            foreach (var (name, marks) in state)
            {
                if (_handedOn.Contains(name) || marks.Count == 0 || !marks.All(m => m.Phase == Phase.Held)) continue;
                if (!_reported.Add(marks.First().At)) continue;

                report("analysis-memory-leak", marks.First().At,
                    $"the memory `{Shown(name)}` points at is never freed, so it is lost when the function returns",
                    Severity.Warning, Confidence.Likely);
            }
        }
    }
}
