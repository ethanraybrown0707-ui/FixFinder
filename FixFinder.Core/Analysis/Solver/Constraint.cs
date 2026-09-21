namespace FixFinder.Core.Analysis.Solver;

public enum Relation { LessOrEqual, Less, Equal, NotEqual }

/// <summary>A term compared with zero: <c>Term ≤ 0</c>, <c>Term &lt; 0</c>, <c>Term = 0</c> or <c>Term ≠ 0</c>.</summary>
public sealed record Constraint(LinearTerm Term, Relation Relation)
{
    public static Constraint AtMost(LinearTerm left, LinearTerm right) => new(left - right, Relation.LessOrEqual);

    public static Constraint Below(LinearTerm left, LinearTerm right) => new(left - right, Relation.Less);

    public static Constraint AtLeast(LinearTerm left, LinearTerm right) => new(right - left, Relation.LessOrEqual);

    public static Constraint Above(LinearTerm left, LinearTerm right) => new(right - left, Relation.Less);

    public static Constraint Same(LinearTerm left, LinearTerm right) => new(left - right, Relation.Equal);

    public static Constraint Different(LinearTerm left, LinearTerm right) => new(left - right, Relation.NotEqual);

    public Constraint Negated() => Relation switch
    {
        Relation.LessOrEqual => new Constraint(-Term, Relation.Less),
        Relation.Less => new Constraint(-Term, Relation.LessOrEqual),
        Relation.Equal => this with { Relation = Relation.NotEqual },
        _ => this with { Relation = Relation.Equal },
    };

    /// <summary>Whether a constraint without symbols is true; null when it has symbols.</summary>
    public bool? Constant => Term.IsConstant ? Holds(Term.Constant) : null;

    public bool HoldsFor(IReadOnlyDictionary<int, Rational> values) => Holds(Term.Evaluate(values));

    private bool Holds(Rational value) => Relation switch
    {
        Relation.LessOrEqual => value.Sign <= 0,
        Relation.Less => value.Sign < 0,
        Relation.Equal => value.IsZero,
        _ => !value.IsZero,
    };

    public override string ToString() => $"{Term} {Relation switch { Relation.LessOrEqual => "<=", Relation.Less => "<", Relation.Equal => "=", _ => "!=" }} 0";
}
