using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Symbolic;

/// <summary>Concrete values that make a line fail, in the program's own terms: "`values` is empty", "`b` is 3".</summary>
public sealed record Witness(IReadOnlyList<string> Facts, IReadOnlyList<WitnessValue> Values)
{
    public override string ToString() => Facts.Count == 0 ? "whatever the input" : string.Join(" and ", Facts);
}

public enum WitnessKind { Number, WholeNumber, Items, Characters, Truth, Nothing }

/// <summary>
/// One value of a witness, precise enough to run: a parameter's value, or - with <paramref name="TypedAt"/> - what is
/// typed at the input read on that line.
/// </summary>
public sealed record WitnessValue(string? Parameter, int? TypedAt, WitnessKind Kind, Solver.Rational Value);

/// <summary>What exploring every path found at one place where the program can fail.</summary>
public sealed class Outcome
{
    /// <summary>Some path reaches this place with values that make it fail.</summary>
    public bool CanFail { get; internal set; }

    /// <summary>Some path fails here whatever its inputs are, and nothing about that path was approximated.</summary>
    public bool Forced { get; internal set; }

    /// <summary>Inputs that make it fail, when they can be trusted.</summary>
    public Witness? Witness { get; internal set; }

    /// <summary>What someone types alone can make it fail - dividing by a number read with input(), say.</summary>
    public bool FromInput { get; internal set; }

    /// <summary>The solver ran out of budget on a question about this place, so no conclusion is safe.</summary>
    public bool Unsure { get; internal set; }

    /// <summary>The value that goes wrong: the divisor, the index, the null value, the empty collection.</summary>
    public Expr? Culprit { get; internal set; }
}

public sealed class SymbolicReport
{
    /// <summary>Every path was followed to its end: no loop was cut short and no budget ran out.</summary>
    public bool Complete { get; internal set; } = true;

    public int Paths { get; internal set; }

    public Dictionary<(string Check, SourceSpan Span), Outcome> Outcomes { get; } = [];

    /// <summary>Every expression some path evaluated; one that is missing is on no feasible path.</summary>
    public HashSet<SourceSpan> Evaluated { get; } = [];
}
