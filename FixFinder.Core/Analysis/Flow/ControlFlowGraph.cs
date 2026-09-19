using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Flow;

/// <summary>One step inside a basic block: nothing in a block jumps anywhere until its terminator.</summary>
public abstract record Instruction(SourceSpan Span);

public sealed record AssignInstruction(SourceSpan Span, Expr Target, Expr Value) : Instruction(Span);

public sealed record EvaluateInstruction(SourceSpan Span, Expr Value) : Instruction(Span);

public sealed record DeclareInstruction(SourceSpan Span, string Variable, IrType Type) : Instruction(Span);

/// <summary>The names a statement the front end could not represent may have changed.</summary>
public sealed record ForgetInstruction(SourceSpan Span, IReadOnlyList<string> Names, IReadOnlyList<Expr> Parts) : Instruction(Span);

/// <summary>The end of a <c>with</c> or <c>using</c> block, where the resource is closed.</summary>
public sealed record ReleaseInstruction(SourceSpan Span, Expr Resource) : Instruction(Span);

/// <summary>How a block ends.</summary>
public abstract record Terminator(SourceSpan Span);

public sealed record Jump(SourceSpan Span, int Target) : Terminator(Span);

/// <summary>Goes one way when the condition is true and the other when it is false. Conditions never contain and, or or not.</summary>
public sealed record Branch(SourceSpan Span, Expr Condition, int WhenTrue, int WhenFalse) : Terminator(Span);

public sealed record Leave(SourceSpan Span, Expr? Value) : Terminator(Span);

public sealed record Raise(SourceSpan Span, Expr? Exception) : Terminator(Span);

/// <summary>Where the function ends: normally, or with an exception nothing caught.</summary>
public sealed record Finish(SourceSpan Span) : Terminator(Span);

public sealed class BasicBlock(int id)
{
    public int Id { get; } = id;

    public List<Instruction> Instructions { get; } = [];

    public Terminator? Terminator { get; set; }

    /// <summary>The handlers an exception raised anywhere in this block goes to.</summary>
    public IReadOnlyList<int> ExceptionTargets { get; set; } = [];

    public bool IsLoopHead { get; set; }

    public SourceSpan? FirstSpan => Instructions.FirstOrDefault()?.Span ?? Terminator?.Span;

    public IEnumerable<int> NormalSuccessors => Terminator switch
    {
        Jump jump => [jump.Target],
        Branch branch => branch.WhenTrue == branch.WhenFalse ? [branch.WhenTrue] : [branch.WhenTrue, branch.WhenFalse],
        _ => [],
    };
}

/// <summary>A function's blocks and the edges between them.</summary>
public sealed class ControlFlowGraph
{
    public required IrFunction Function { get; init; }

    public required IReadOnlyList<BasicBlock> Blocks { get; init; }

    public int Entry => 0;

    public required int NormalExit { get; init; }

    public required int ErrorExit { get; init; }

    private IReadOnlyList<IReadOnlyList<int>>? _predecessors;

    public IEnumerable<int> Successors(int block)
    {
        var node = Blocks[block];

        var normal = node.Terminator switch
        {
            Leave => [NormalExit],
            Raise => node.ExceptionTargets.Count > 0 ? node.ExceptionTargets : [ErrorExit],
            _ => node.NormalSuccessors,
        };

        return normal.Concat(node.Terminator is Raise ? [] : node.ExceptionTargets).Distinct();
    }

    public IReadOnlyList<int> Predecessors(int block)
    {
        if (_predecessors is null)
        {
            var incoming = Blocks.Select(_ => new List<int>()).ToList();
            foreach (var node in Blocks)
                foreach (var successor in Successors(node.Id))
                    incoming[successor].Add(node.Id);
            _predecessors = incoming;
        }

        return _predecessors[block];
    }

    /// <summary>The blocks the entry can reach; code in any other block can never run.</summary>
    public HashSet<int> Reachable()
    {
        var seen = new HashSet<int> { Entry };
        var pending = new Stack<int>([Entry]);

        while (pending.Count > 0)
            foreach (var next in Successors(pending.Pop()))
                if (seen.Add(next)) pending.Push(next);

        return seen;
    }
}
