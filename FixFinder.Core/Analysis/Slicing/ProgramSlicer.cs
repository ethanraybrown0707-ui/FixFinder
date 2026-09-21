using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Slicing;

/// <summary>
/// Backward program slicing: the lines that decide a value at one place. Those are the assignments whose values can
/// reach it, the assignments those use in turn, and the conditions that decide whether any of them runs - found from
/// reaching definitions and from control dependence worked out with post-dominators.
/// </summary>
public sealed class ProgramSlicer
{
    /// <summary>One step of the function: an instruction, a block's ending, or a parameter arriving at the start.</summary>
    private sealed record Point(int Block, int Line, IReadOnlySet<string> Defines, IReadOnlySet<string> Uses, IReadOnlyList<Expr> Holds);

    private const int Entry = -1;

    private readonly ControlFlowGraph _graph;
    private readonly List<Point> _points = [];
    private readonly Dictionary<int, List<int>> _pointsOfBlock = [];
    private readonly Dictionary<int, Dictionary<string, HashSet<int>>> _reachingIn = [];
    private readonly Dictionary<int, HashSet<int>> _controllers = [];

    public ProgramSlicer(ControlFlowGraph graph)
    {
        _graph = graph;

        foreach (var parameter in graph.Function.Parameters)
            _points.Add(new Point(Entry, graph.Function.Span.Line, new HashSet<string> { parameter.Name }, new HashSet<string>(), []));

        foreach (var block in graph.Blocks)
        {
            var mine = new List<int>();
            foreach (var instruction in block.Instructions)
            {
                mine.Add(_points.Count);
                _points.Add(PointOf(block.Id, instruction));
            }

            if (TerminatorPoint(block) is { } ending)
            {
                mine.Add(_points.Count);
                _points.Add(ending);
            }

            _pointsOfBlock[block.Id] = mine;
        }

        ReachingDefinitions();
        ControlDependence();
    }

    private static Point PointOf(int block, Instruction instruction) => instruction switch
    {
        AssignInstruction assign => new Point(block, assign.Span.Line,
            Targets(assign.Target).Concat(Assigned(assign.Value)).ToHashSet(StringComparer.Ordinal),
            Read(assign.Value).Concat(assign.Target is Name ? [] : IrWalk.Children(assign.Target).SelectMany(IrWalk.Names)).ToHashSet(StringComparer.Ordinal),
            [assign.Target, assign.Value]),
        EvaluateInstruction evaluate => new Point(block, evaluate.Span.Line, Assigned(evaluate.Value).ToHashSet(StringComparer.Ordinal),
            Read(evaluate.Value).ToHashSet(StringComparer.Ordinal), [evaluate.Value]),
        DeclareInstruction declare => new Point(block, declare.Span.Line, new HashSet<string> { declare.Variable }, new HashSet<string>(), []),
        ForgetInstruction forget => new Point(block, forget.Span.Line, forget.Names.ToHashSet(StringComparer.Ordinal),
            forget.Parts.SelectMany(IrWalk.Names).ToHashSet(StringComparer.Ordinal), forget.Parts),
        ReleaseInstruction release => new Point(block, release.Span.Line, new HashSet<string>(), IrWalk.Names(release.Resource).ToHashSet(StringComparer.Ordinal), [release.Resource]),
        _ => new Point(block, instruction.Span.Line, new HashSet<string>(), new HashSet<string>(), []),
    };

    private static Point? TerminatorPoint(BasicBlock block) => block.Terminator switch
    {
        Branch branch => new Point(block.Id, branch.Span.Line, Assigned(branch.Condition).ToHashSet(StringComparer.Ordinal),
            Read(branch.Condition).ToHashSet(StringComparer.Ordinal), [branch.Condition]),
        Leave { Value: { } value } leave => new Point(block.Id, leave.Span.Line, new HashSet<string>(), Read(value).ToHashSet(StringComparer.Ordinal), [value]),
        Raise { Exception: { } thrown } raise => new Point(block.Id, raise.Span.Line, new HashSet<string>(), Read(thrown).ToHashSet(StringComparer.Ordinal), [thrown]),
        _ => null,
    };

    private static IEnumerable<string> Targets(Expr target) => target switch
    {
        Name name => [name.Identifier],
        Member { Target: Name { Identifier: "this" }, MemberName: var field } => [field],
        CollectionLiteral unpacked => unpacked.Items.SelectMany(Targets),
        _ => [],
    };

    private static IEnumerable<string> Assigned(Expr expression) =>
        expression is AssignValue assigned
            ? [.. Targets(assigned.Target), .. Assigned(assigned.Value)]
            : IrWalk.Children(expression).SelectMany(Assigned);

    /// <summary>The variables an expression reads - this.x counted as the field x, as the analysis follows it.</summary>
    private static IEnumerable<string> Read(Expr expression) => expression switch
    {
        Member { Target: Name { Identifier: "this" }, MemberName: var field } => [field],
        Name name => [name.Identifier],
        AssignValue { Target: Name } assigned => Read(assigned.Value),
        _ => IrWalk.Children(expression).SelectMany(Read),
    };

    /// <summary>Which assignments can reach the start of each block, by the variable they assign.</summary>
    private void ReachingDefinitions()
    {
        var start = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        for (var i = 0; i < _points.Count && _points[i].Block == Entry; i++)
            foreach (var name in _points[i].Defines) start[name] = [i];

        foreach (var block in _graph.Blocks) _reachingIn[block.Id] = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        Merge(_reachingIn[_graph.Entry], start);

        var pending = new Queue<int>([_graph.Entry]);
        var queued = new HashSet<int> { _graph.Entry };

        while (pending.Count > 0)
        {
            var block = pending.Dequeue();
            queued.Remove(block);

            var reaching = Copy(_reachingIn[block]);
            var anyOfThem = Copy(_reachingIn[block]);
            foreach (var point in _pointsOfBlock[block])
            {
                Apply(reaching, point);
                foreach (var name in _points[point].Defines) Merge(anyOfThem, new Dictionary<string, HashSet<int>> { [name] = [point] });
            }

            foreach (var next in _graph.Successors(block))
            {
                var thrown = _graph.Blocks[block].ExceptionTargets.Contains(next) && _graph.Blocks[block].Terminator is not Raise;
                if (Merge(_reachingIn[next], thrown ? anyOfThem : reaching) && queued.Add(next)) pending.Enqueue(next);
            }
        }
    }

    private void Apply(Dictionary<string, HashSet<int>> reaching, int point)
    {
        foreach (var name in _points[point].Defines) reaching[name] = [point];
    }

    private static Dictionary<string, HashSet<int>> Copy(Dictionary<string, HashSet<int>> reaching) =>
        reaching.ToDictionary(pair => pair.Key, pair => new HashSet<int>(pair.Value), StringComparer.Ordinal);

    private static bool Merge(Dictionary<string, HashSet<int>> into, Dictionary<string, HashSet<int>> from)
    {
        var changed = false;
        foreach (var (name, points) in from)
        {
            if (!into.TryGetValue(name, out var existing)) into[name] = existing = [];
            foreach (var point in points) changed |= existing.Add(point);
        }

        return changed;
    }

    /// <summary>
    /// Block X depends on branch A when some way out of A always goes through X and A itself does not: X post-dominates
    /// a successor of A but not A. Post-dominators are found by the usual fixpoint on the graph turned round.
    /// </summary>
    private void ControlDependence()
    {
        var blocks = _graph.Blocks.Select(b => b.Id).ToList();
        var exits = blocks.Where(b => !_graph.Successors(b).Any()).ToHashSet();
        var all = blocks.ToHashSet();

        var postDominators = blocks.ToDictionary(b => b, b => exits.Contains(b) ? new HashSet<int> { b } : new HashSet<int>(all));

        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var block in blocks.Where(b => !exits.Contains(b)))
            {
                var through = _graph.Successors(block).Select(s => postDominators[s]).Aggregate((HashSet<int>?)null,
                    (common, next) => common is null ? new HashSet<int>(next) : common.Intersect(next).ToHashSet()) ?? [];
                through.Add(block);

                if (!through.SetEquals(postDominators[block]))
                {
                    postDominators[block] = through;
                    changed = true;
                }
            }
        }

        foreach (var block in _graph.Blocks.Where(b => b.Terminator is Branch))
        {
            foreach (var successor in block.NormalSuccessors)
                foreach (var dependent in postDominators[successor].Where(x => !postDominators[block.Id].Contains(x)))
                {
                    if (!_controllers.TryGetValue(dependent, out var controllers)) _controllers[dependent] = controllers = [];
                    controllers.Add(block.Id);
                }
        }
    }

    /// <summary>
    /// The lines that decide the given variables where the expression at <paramref name="at"/> is evaluated, in order,
    /// including that line; empty when nothing at that place is found.
    /// </summary>
    public IReadOnlyList<int> LinesDeciding(SourceSpan at, IEnumerable<string> names)
    {
        var target = _points.FindIndex(p => p.Block != Entry && p.Holds.Any(e => Contains(e, at)));
        if (target < 0) return [];

        var slice = new HashSet<int> { target };
        var pending = new Stack<(int Point, IEnumerable<string> Names)>([(target, names)]);
        var controlledBlocks = new HashSet<int>();

        while (pending.Count > 0)
        {
            var (point, wanted) = pending.Pop();

            foreach (var name in wanted)
                foreach (var definition in ReachingAt(point, name))
                    if (slice.Add(definition)) pending.Push((definition, _points[definition].Uses));

            var block = _points[point].Block;
            if (block == Entry || !controlledBlocks.Add(block)) continue;

            foreach (var controller in _controllers.GetValueOrDefault(block) ?? [])
            {
                var branch = _pointsOfBlock[controller][^1];
                if (slice.Add(branch)) pending.Push((branch, _points[branch].Uses));
                else pending.Push((branch, []));
            }
        }

        return slice.Select(p => _points[p].Line).Where(line => line > 0).Distinct().Order().ToList();
    }

    private IEnumerable<int> ReachingAt(int point, string name)
    {
        var block = _points[point].Block;
        if (block == Entry) return [];

        var reaching = _reachingIn[block].TryGetValue(name, out var incoming) ? new HashSet<int>(incoming) : [];
        foreach (var earlier in _pointsOfBlock[block].TakeWhile(p => p != point))
            if (_points[earlier].Defines.Contains(name)) reaching = [earlier];

        return reaching;
    }

    private static bool Contains(Expr expression, SourceSpan at) =>
        expression.Span == at || IrWalk.Children(expression).Any(child => Contains(child, at));
}
