using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Solver;

namespace FixFinder.Core.Analysis.Symbolic;

/// <summary>Where a symbol's value comes from, which decides how a witness describes it.</summary>
public enum SymbolOrigin
{
    /// <summary>A parameter's value.</summary>
    Parameter,

    /// <summary>The number of items in a parameter or other outside collection.</summary>
    Length,

    /// <summary>The number of characters in outside text: a parameter, what someone typed.</summary>
    TextLength,

    /// <summary>What someone types - input(), Scanner.nextInt(), Console.ReadLine().</summary>
    Input,

    /// <summary>What a call FixFinder cannot see into returned, or a field's value.</summary>
    Outside,

    /// <summary>Whether something not known is true - a flag, or a value tested for truth - fixed until it is assigned again.</summary>
    Flag,

    /// <summary>A value derived exactly from others, like the quotient of a division by a constant.</summary>
    Derived,

    /// <summary>A stand-in for something not followed exactly; a witness that depends on it is not trusted.</summary>
    Approximation,
}

public sealed record Symbol(int Id, string Describes, SymbolOrigin Origin, bool IsWhole);

public sealed class SymbolTable
{
    private readonly List<Symbol> _symbols = [];

    public Symbol this[int id] => _symbols[id];

    public LinearTerm New(string describes, SymbolOrigin origin, bool isWhole = true)
    {
        _symbols.Add(new Symbol(_symbols.Count, describes, origin, isWhole));
        return LinearTerm.Symbol(_symbols.Count - 1);
    }

    public bool IsWhole(int id) => _symbols[id].IsWhole;
}

/// <summary>A value on one path through a function, in terms of symbols for what is not known.</summary>
public abstract record SymbolicValue;

public sealed record SymNumber(LinearTerm Term, bool Whole) : SymbolicValue;

public sealed record SymTruth(Condition Condition) : SymbolicValue;

public sealed record SymNull : SymbolicValue
{
    public static SymNull Value { get; } = new();
}

/// <summary>Text of a length; <paramref name="Typed"/> names the input it was read from, if any.</summary>
public sealed record SymText(LinearTerm Length, string? Known = null, string? Typed = null) : SymbolicValue;

public sealed record SymSequence(CollectionKind Kind, LinearTerm Length, IReadOnlyList<SymbolicValue>? Items = null) : SymbolicValue;

public sealed record SymRange(LinearTerm Start, LinearTerm Stop, Rational Step) : SymbolicValue;

/// <summary>Something that is certainly not null, whose value is not followed: an object, a function, a module.</summary>
public sealed record SymOther(string What) : SymbolicValue;

/// <summary>Anything at all - assumed not null, as in the abstract domain, so null only comes from somewhere visible.</summary>
public sealed record SymUnknown : SymbolicValue
{
    public static SymUnknown Value { get; } = new();
}

/// <summary>A truth value in terms of linear constraints, kept as a tree until a branch needs it as a list of cases.</summary>
public abstract record Condition
{
    public static Condition True { get; } = new Known(true);
    public static Condition False { get; } = new Known(false);
    public static Condition Either { get; } = new Undecided();

    public static Condition Of(Constraint constraint) =>
        constraint.Constant is { } constant ? new Known(constant) : new Atom(constraint);

    public Condition Not() => this switch
    {
        Known known => new Known(!known.Value),
        Atom atom => Of(atom.Constraint.Negated()),
        Negation negation => negation.Inner,
        Undecided => this,
        _ => new Negation(this),
    };

    public static Condition And(Condition left, Condition right) => (left, right) switch
    {
        (Known { Value: false }, _) or (_, Known { Value: false }) => False,
        (Known { Value: true }, _) => right,
        (_, Known { Value: true }) => left,
        _ => new AllOf(left, right),
    };

    public static Condition Or(Condition left, Condition right) => (left, right) switch
    {
        (Known { Value: true }, _) or (_, Known { Value: true }) => True,
        (Known { Value: false }, _) => right,
        (_, Known { Value: false }) => left,
        _ => new AnyOf(left, right),
    };

    /// <summary>
    /// The ways the condition can hold, each a list of constraints that must all hold; null when there are more than
    /// <paramref name="most"/>. Undecided parts add no constraint, so both a condition and its opposite can hold.
    /// </summary>
    public List<List<Constraint>>? Cases(int most) => this switch
    {
        Known { Value: true } or Undecided => [[]],
        Known => [],
        Atom atom => [[atom.Constraint]],
        AnyOf either => Concat(either.Left.Cases(most), either.Right.Cases(most), most),
        AllOf both => Product(both.Left.Cases(most), both.Right.Cases(most), most),
        Negation { Inner: AllOf both } => Or(both.Left.Not(), both.Right.Not()).Cases(most),
        Negation { Inner: AnyOf either } => And(either.Left.Not(), either.Right.Not()).Cases(most),
        _ => [[]],
    };

    private static List<List<Constraint>>? Concat(List<List<Constraint>>? left, List<List<Constraint>>? right, int most) =>
        left is null || right is null || left.Count + right.Count > most ? null : [.. left, .. right];

    private static List<List<Constraint>>? Product(List<List<Constraint>>? left, List<List<Constraint>>? right, int most)
    {
        if (left is null || right is null || left.Count * right.Count > most) return null;
        return [.. left.SelectMany(l => right.Select(r => (List<Constraint>)[.. l, .. r]))];
    }
}

public sealed record Known(bool Value) : Condition;

public sealed record Atom(Constraint Constraint) : Condition;

public sealed record AllOf(Condition Left, Condition Right) : Condition;

public sealed record AnyOf(Condition Left, Condition Right) : Condition;

public sealed record Negation(Condition Inner) : Condition;

public sealed record Undecided : Condition;
