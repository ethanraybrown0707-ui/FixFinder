using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Abstract;

/// <summary>Runs a function's graph until what each block could see stops changing: the abstract interpretation itself.</summary>
public sealed class Fixpoint
{
    private const int PassesBeforeWidening = 3;
    private const int NarrowingPasses = 2;

    private readonly ControlFlowGraph _graph;
    private readonly Evaluator _evaluator;
    private readonly AbstractState[] _entry;
    private readonly List<int> _order;
    private readonly HashSet<int> _widenAt;

    /// <summary>For each loop's test, the blocks that come to it from before the loop rather than round it again.</summary>
    private readonly Dictionary<int, HashSet<int>> _waysIntoLoops;

    private Fixpoint(ControlFlowGraph graph, Evaluator evaluator)
    {
        _graph = graph;
        _evaluator = evaluator;
        _entry = graph.Blocks.Select(_ => AbstractState.Unreachable).ToArray();
        _order = ReversePostOrder(graph);
        _widenAt = LoopHeads(graph, _order);
        _waysIntoLoops = WaysIntoLoops(graph, _order);
    }

    public static Fixpoint Run(ControlFlowGraph graph, Evaluator evaluator, AbstractState start)
    {
        var fixpoint = new Fixpoint(graph, evaluator);
        fixpoint.Solve(start);
        return fixpoint;
    }

    /// <summary>What every variable could hold when the block starts; unreachable if no run gets there.</summary>
    public AbstractState EntryOf(int block) => _entry[block];

    /// <summary>The state after the block's instructions, before its terminator.</summary>
    public AbstractState ExitOf(int block) =>
        _graph.Blocks[block].Instructions.Aggregate(_entry[block], _evaluator.Apply);

    /// <summary>What flows along one edge: the branch's condition is assumed true or false on its way.</summary>
    public AbstractState Along(int from, int to)
    {
        var block = _graph.Blocks[from];

        if (block.Terminator is Branch test && test.WhenFalse == to && test.WhenTrue != to && _waysIntoLoops.TryGetValue(from, out var ways))
            return LeavingTheLoop(block, test, ways);

        return AlongWithoutLeaving(from, to);
    }

    /// <summary>
    /// What leaves a loop because its test failed, worked out for each way into the test on its own and only then joined.
    /// A loop whose test holds the first time it is made - i = 0 against i &lt; 4, a range(4) about to be walked - cannot
    /// be left without going round at least once, so what the body certainly did is certain after it: a list it added to
    /// is not empty. Joined first, the state from before the loop, when the list was still empty, leaked past the loop.
    /// </summary>
    private AbstractState LeavingTheLoop(BasicBlock head, Branch test, HashSet<int> waysIn)
    {
        var leaving = AbstractState.Unreachable;

        foreach (var from in _graph.Predecessors(head.Id))
        {
            var arriving = head.Instructions.Aggregate(AlongWithoutLeaving(from, head.Id), _evaluator.Apply);
            if (!arriving.IsReachable) continue;

            // Whether there is another item can only be told from the collection before the first one is taken: then it is
            // exactly whether the collection is empty.
            if (waysIn.Contains(from) && test.Condition is MoreItems { Items: var items } && _evaluator.Evaluate(items, arriving).Length.Low >= 1)
                continue;

            leaving = leaving.Join(_evaluator.Assume(arriving, test.Condition, false));
        }

        return leaving;
    }

    private AbstractState AlongWithoutLeaving(int from, int to)
    {
        var block = _graph.Blocks[from];
        var exit = ExitOf(from);

        var normal = block.Terminator switch
        {
            Branch branch when branch.WhenTrue == to && branch.WhenFalse == to => exit,
            Branch branch when branch.WhenTrue == to => _evaluator.Assume(exit, branch.Condition, true),
            Branch branch when branch.WhenFalse == to => _evaluator.Assume(exit, branch.Condition, false),
            Jump jump when jump.Target == to => exit,
            Leave when to == _graph.NormalExit => exit,
            Raise when to == _graph.ErrorExit || block.ExceptionTargets.Contains(to) => exit,
            _ => AbstractState.Unreachable,
        };

        return block.ExceptionTargets.Contains(to) ? normal.Join(_entry[from]).Join(exit) : normal;
    }

    private void Solve(AbstractState start)
    {
        var position = _order.Select((block, index) => (block, index)).ToDictionary(p => p.block, p => p.index);
        var visits = new int[_graph.Blocks.Count];
        var settled = new bool[_graph.Blocks.Count];
        var pending = new SortedSet<int>(Comparer<int>.Create((a, b) => position[a].CompareTo(position[b]))) { _graph.Entry };
        var budget = _graph.Blocks.Count * 60;

        while (pending.Count > 0 && budget-- > 0)
        {
            var block = pending.Min;
            pending.Remove(block);

            var incoming = Incoming(block, start);
            var previous = _entry[block];

            if (_widenAt.Contains(block) && ++visits[block] > PassesBeforeWidening)
                incoming = previous.Widen(previous.Join(incoming));

            if (settled[block] && previous.SameAs(incoming)) continue;

            settled[block] = true;
            _entry[block] = incoming;

            foreach (var next in _graph.Successors(block))
                pending.Add(next);
        }

        for (var pass = 0; pass < NarrowingPasses; pass++)
        {
            foreach (var block in _order)
            {
                var incoming = Incoming(block, start);
                _entry[block] = _widenAt.Contains(block) ? _entry[block].Narrow(incoming) : incoming;
            }
        }
    }

    private AbstractState Incoming(int block, AbstractState start)
    {
        var state = block == _graph.Entry ? start : AbstractState.Unreachable;
        foreach (var from in _graph.Predecessors(block))
            state = state.Join(Along(from, block));
        return state;
    }

    private static List<int> ReversePostOrder(ControlFlowGraph graph)
    {
        var seen = new HashSet<int>();
        var order = new List<int>();

        void Visit(int block)
        {
            if (!seen.Add(block)) return;
            foreach (var next in graph.Successors(block)) Visit(next);
            order.Add(block);
        }

        Visit(graph.Entry);
        foreach (var block in graph.Blocks) Visit(block.Id);
        order.Reverse();
        return order;
    }

    /// <summary>
    /// The blocks each loop test is reached from without going round the loop: those that come before it in the order the
    /// graph is walked, where going round again comes back from after it.
    /// </summary>
    private static Dictionary<int, HashSet<int>> WaysIntoLoops(ControlFlowGraph graph, List<int> order)
    {
        var position = order.Select((block, index) => (block, index)).ToDictionary(p => p.block, p => p.index);

        return graph.Blocks
            .Where(block => block.IsLoopHead && block.Terminator is Branch)
            .ToDictionary(
                head => head.Id,
                head => graph.Predecessors(head.Id).Where(from => position.TryGetValue(from, out var at) && at < position[head.Id]).ToHashSet());
    }

    private static HashSet<int> LoopHeads(ControlFlowGraph graph, List<int> order)
    {
        var position = order.Select((block, index) => (block, index)).ToDictionary(p => p.block, p => p.index);
        var heads = graph.Blocks.Where(b => b.IsLoopHead).Select(b => b.Id).ToHashSet();

        foreach (var block in graph.Blocks)
            foreach (var next in graph.Successors(block.Id))
                if (position[next] <= position[block.Id]) heads.Add(next);

        return heads;
    }
}
