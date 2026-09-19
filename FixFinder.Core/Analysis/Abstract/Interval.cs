namespace FixFinder.Core.Analysis.Abstract;

/// <summary>The numbers a value could be, from <see cref="Low"/> to <see cref="High"/> inclusive; either end may be infinite.</summary>
public readonly record struct Interval(double Low, double High)
{
    public static Interval Top { get; } = new(double.NegativeInfinity, double.PositiveInfinity);
    public static Interval Empty { get; } = new(double.PositiveInfinity, double.NegativeInfinity);
    public static Interval NonNegative { get; } = new(0, double.PositiveInfinity);

    public static Interval Exactly(double value) => new(value, value);

    public bool IsEmpty => Low > High;
    public bool IsTop => double.IsNegativeInfinity(Low) && double.IsPositiveInfinity(High);
    public bool IsExact => Low == High;
    public bool HasFiniteEnd => !IsEmpty && (!double.IsInfinity(Low) || !double.IsInfinity(High));

    public bool Contains(double value) => Low <= value && value <= High;

    public Interval Join(Interval other) =>
        IsEmpty ? other : other.IsEmpty ? this : new(Math.Min(Low, other.Low), Math.Max(High, other.High));

    public Interval Meet(Interval other) => new(Math.Max(Low, other.Low), Math.Min(High, other.High));

    /// <summary>Jumps any end that is still moving to infinity, so a loop's analysis always finishes.</summary>
    public Interval Widen(Interval next) =>
        IsEmpty ? next : next.IsEmpty ? this : new(next.Low < Low ? double.NegativeInfinity : Low, next.High > High ? double.PositiveInfinity : High);

    /// <summary>Takes back an infinite end that a later, sharper pass bounded.</summary>
    public Interval Narrow(Interval next) =>
        new(double.IsNegativeInfinity(Low) ? next.Low : Low, double.IsPositiveInfinity(High) ? next.High : High);

    public Interval Negate() => IsEmpty ? this : new(-High, -Low);

    public Interval Add(Interval other) => IsEmpty || other.IsEmpty ? Empty : new(Low + other.Low, High + other.High);

    public Interval Subtract(Interval other) => Add(other.Negate());

    public Interval Multiply(Interval other)
    {
        if (IsEmpty || other.IsEmpty) return Empty;

        var products = new[] { Product(Low, other.Low), Product(Low, other.High), Product(High, other.Low), Product(High, other.High) };
        return new(products.Min(), products.Max());
    }

    private static double Product(double a, double b) => a == 0 || b == 0 ? 0 : a * b;

    /// <summary>True division; anything goes when the divisor could be zero.</summary>
    public Interval Divide(Interval divisor)
    {
        if (IsEmpty || divisor.IsEmpty) return Empty;
        if (divisor.Contains(0)) return Top;

        var quotients = new[] { Low / divisor.Low, Low / divisor.High, High / divisor.Low, High / divisor.High }
            .Select(q => double.IsNaN(q) ? 0 : q).ToArray();
        return new(quotients.Min(), quotients.Max());
    }

    public Interval Floor() => IsEmpty ? this : new(Math.Floor(Low), Math.Floor(High));

    public Interval Truncate() => IsEmpty ? this : new(Math.Truncate(Low), Math.Truncate(High));

    /// <summary>The remainder, where the sign follows the divisor (Python) or the dividend (the C family).</summary>
    public Interval Modulo(Interval divisor, bool signFollowsDivisor)
    {
        if (IsEmpty || divisor.IsEmpty) return Empty;
        if (!divisor.HasFiniteEnd || divisor.Contains(0)) return Top;

        var largest = Math.Max(Math.Abs(divisor.Low), Math.Abs(divisor.High));
        if (signFollowsDivisor)
            return divisor.Low > 0 ? new(0, largest - 1) : divisor.High < 0 ? new(-(largest - 1), 0) : new(-(largest - 1), largest - 1);

        return Low >= 0 ? new(0, Math.Min(largest - 1, High)) : High <= 0 ? new(-(largest - 1), 0) : new(-(largest - 1), largest - 1);
    }

    public override string ToString() => IsEmpty ? "empty" : IsExact ? Show(Low) : $"[{Show(Low)}, {Show(High)}]";

    private static string Show(double value) => double.IsPositiveInfinity(value) ? "inf" : double.IsNegativeInfinity(value) ? "-inf" : value.ToString("0.###");
}
