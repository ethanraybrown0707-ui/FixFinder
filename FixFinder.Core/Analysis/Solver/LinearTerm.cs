namespace FixFinder.Core.Analysis.Solver;

/// <summary>c + a1·x1 + a2·x2 + ... over symbols the caller numbers.</summary>
public sealed class LinearTerm : IEquatable<LinearTerm>
{
    private readonly SortedDictionary<int, Rational> _coefficients;

    private LinearTerm(SortedDictionary<int, Rational> coefficients, Rational constant)
    {
        _coefficients = coefficients;
        Constant = constant;
    }

    public Rational Constant { get; }

    public IReadOnlyDictionary<int, Rational> Coefficients => _coefficients;

    public IEnumerable<int> Symbols => _coefficients.Keys;

    public bool IsConstant => _coefficients.Count == 0;

    public static LinearTerm Of(Rational constant) => new([], constant);

    public static LinearTerm Symbol(int symbol) => new(new SortedDictionary<int, Rational> { [symbol] = Rational.One }, Rational.Zero);

    public static LinearTerm operator +(LinearTerm a, LinearTerm b) => Combine(a, b, Rational.One);

    public static LinearTerm operator -(LinearTerm a, LinearTerm b) => Combine(a, b, -Rational.One);

    public static LinearTerm operator *(LinearTerm a, Rational factor)
    {
        if (factor.IsZero) return Of(Rational.Zero);

        var scaled = new SortedDictionary<int, Rational>();
        foreach (var (symbol, coefficient) in a._coefficients) scaled[symbol] = coefficient * factor;
        return new LinearTerm(scaled, a.Constant * factor);
    }

    public static LinearTerm operator -(LinearTerm a) => a * -Rational.One;

    public static implicit operator LinearTerm(long constant) => Of(constant);

    private static LinearTerm Combine(LinearTerm a, LinearTerm b, Rational sign)
    {
        var sum = new SortedDictionary<int, Rational>(a._coefficients);
        foreach (var (symbol, coefficient) in b._coefficients)
        {
            var total = sum.GetValueOrDefault(symbol, Rational.Zero) + coefficient * sign;
            if (total.IsZero) sum.Remove(symbol);
            else sum[symbol] = total;
        }

        return new LinearTerm(sum, a.Constant + b.Constant * sign);
    }

    public Rational Evaluate(IReadOnlyDictionary<int, Rational> values) =>
        _coefficients.Aggregate(Constant, (total, pair) => total + pair.Value * values.GetValueOrDefault(pair.Key, Rational.Zero));

    public bool Equals(LinearTerm? other) =>
        other is not null && Constant == other.Constant && _coefficients.Count == other._coefficients.Count &&
        _coefficients.All(pair => other._coefficients.TryGetValue(pair.Key, out var theirs) && theirs == pair.Value);

    public override bool Equals(object? obj) => obj is LinearTerm other && Equals(other);

    public override int GetHashCode() =>
        _coefficients.Aggregate(Constant.GetHashCode(), (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value));

    public override string ToString()
    {
        var parts = _coefficients.Select(pair => pair.Value == Rational.One ? $"s{pair.Key}" : $"{pair.Value}·s{pair.Key}").ToList();
        if (!Constant.IsZero || parts.Count == 0) parts.Add(Constant.ToString());
        return string.Join(" + ", parts);
    }
}
