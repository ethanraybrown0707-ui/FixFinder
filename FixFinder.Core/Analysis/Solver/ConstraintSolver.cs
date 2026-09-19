using System.Numerics;

namespace FixFinder.Core.Analysis.Solver;

public enum Verdict { Satisfiable, Unsatisfiable, Unknown }

public sealed record SolverResult(Verdict Verdict, IReadOnlyDictionary<int, Rational>? Model = null)
{
    public static SolverResult Unsatisfiable { get; } = new(Verdict.Unsatisfiable);
    public static SolverResult Unknown { get; } = new(Verdict.Unknown);

    public bool IsSatisfiable => Verdict == Verdict.Satisfiable;
    public bool IsUnsatisfiable => Verdict == Verdict.Unsatisfiable;
}

/// <summary>
/// Decides whether linear constraints over whole and real numbers can all hold, and gives values that make them hold.
/// The simplex solves them over the reals; whole-number symbols are then branched on (x ≤ ⌊v⌋ or x ≥ ⌈v⌉) and each
/// ≠ is split into &lt; or &gt;, within a fixed budget - past it the answer is Unknown, never a guess.
/// </summary>
public sealed class ConstraintSolver
{
    public int MostBranches { get; init; } = 200;

    public int MostPivots { get; init; } = 500;

    public SolverResult Check(IReadOnlyList<Constraint> constraints, Func<int, bool> isWhole)
    {
        var branches = 0;
        return Search(constraints, isWhole, ref branches);
    }

    private SolverResult Search(IReadOnlyList<Constraint> constraints, Func<int, bool> isWhole, ref int branches)
    {
        if (++branches > MostBranches) return SolverResult.Unknown;

        foreach (var constraint in constraints)
            if (constraint.Constant == false) return SolverResult.Unsatisfiable;

        if (constraints.Any(c => c.Relation == Relation.Equal && HasNoWholeSolution(c.Term, isWhole))) return SolverResult.Unsatisfiable;

        var simplex = new Simplex();
        foreach (var constraint in constraints.Where(c => c.Relation != Relation.NotEqual && c.Constant is null))
            if (!simplex.Add(constraint)) return SolverResult.Unsatisfiable;

        switch (simplex.Check(MostPivots))
        {
            case null:
                return SolverResult.Unknown;
            case false:
                return SolverResult.Unsatisfiable;
        }

        var model = simplex.Model();

        foreach (var (symbol, value) in model)
        {
            if (!isWhole(symbol) || value.IsInteger) continue;

            var variable = LinearTerm.Symbol(symbol);
            return Either(constraints, isWhole, ref branches,
                Constraint.AtMost(variable, LinearTerm.Of(value.Floor())),
                Constraint.AtLeast(variable, LinearTerm.Of(value.Ceiling())));
        }

        foreach (var different in constraints.Where(c => c.Relation == Relation.NotEqual && c.Constant is null))
        {
            if (different.HoldsFor(model)) continue;

            return Either(constraints, isWhole, ref branches,
                new Constraint(different.Term, Relation.Less),
                new Constraint(-different.Term, Relation.Less));
        }

        return new SolverResult(Verdict.Satisfiable, model);
    }

    private SolverResult Either(IReadOnlyList<Constraint> constraints, Func<int, bool> isWhole, ref int branches, Constraint first, Constraint second)
    {
        var left = Search([.. constraints, first], isWhole, ref branches);
        if (left.IsSatisfiable) return left;

        var right = Search([.. constraints, second], isWhole, ref branches);
        if (right.IsSatisfiable) return right;

        return left.IsUnsatisfiable && right.IsUnsatisfiable ? SolverResult.Unsatisfiable : SolverResult.Unknown;
    }

    /// <summary>
    /// a1·x1 + ... + c = 0 over whole numbers has a solution only if the gcd of the a's divides c - the case the branching
    /// alone could chase forever, as in 2x - 2y = 1.
    /// </summary>
    private static bool HasNoWholeSolution(LinearTerm term, Func<int, bool> isWhole)
    {
        if (term.IsConstant || term.Symbols.Any(s => !isWhole(s))) return false;

        var scale = term.Coefficients.Values.Append(term.Constant).Aggregate(BigInteger.One, (lcm, r) => lcm / BigInteger.GreatestCommonDivisor(lcm, r.Denominator) * r.Denominator);
        var coefficients = term.Coefficients.Values.Select(r => r.Numerator * (scale / r.Denominator)).ToList();
        var constant = term.Constant.Numerator * (scale / term.Constant.Denominator);

        var divisor = coefficients.Aggregate(BigInteger.Zero, BigInteger.GreatestCommonDivisor);
        return !divisor.IsZero && !BigInteger.Remainder(constant, divisor).IsZero;
    }
}
