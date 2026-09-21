using System.Globalization;
using System.Numerics;

namespace FixFinder.Core.Analysis.Solver;

/// <summary>An exact fraction, so the solver never loses or invents a case through rounding.</summary>
public readonly struct Rational : IEquatable<Rational>, IComparable<Rational>
{
    private readonly BigInteger _numerator;
    private readonly BigInteger _denominator;

    public Rational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero) throw new DivideByZeroException();
        if (denominator.Sign < 0) (numerator, denominator) = (-numerator, -denominator);

        var common = BigInteger.GreatestCommonDivisor(numerator, denominator);
        (_numerator, _denominator) = common.IsOne || common.IsZero ? (numerator, denominator) : (numerator / common, denominator / common);
    }

    public static Rational Zero => new(0, 1);
    public static Rational One => new(1, 1);

    public BigInteger Numerator => _numerator;
    public BigInteger Denominator => _denominator.IsZero ? BigInteger.One : _denominator;

    public bool IsInteger => Denominator.IsOne;
    public bool IsZero => _numerator.IsZero;
    public int Sign => _numerator.Sign;

    public static implicit operator Rational(long value) => new(value, 1);

    /// <summary>The double's exact value: every finite double is a fraction with a power of two below it.</summary>
    public static Rational FromDouble(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));

        var bits = BitConverter.DoubleToInt64Bits(value);
        var negative = bits < 0;
        var exponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xFFFFFFFFFFFFFL;

        if (exponent == 0) exponent++;
        else mantissa |= 1L << 52;
        exponent -= 1075;

        BigInteger numerator = negative ? -mantissa : mantissa;
        return exponent >= 0 ? new Rational(numerator * BigInteger.Pow(2, exponent), 1) : new Rational(numerator, BigInteger.Pow(2, -exponent));
    }

    public static Rational operator +(Rational a, Rational b) => new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static Rational operator -(Rational a, Rational b) => new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static Rational operator *(Rational a, Rational b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
    public static Rational operator /(Rational a, Rational b) => new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    public static Rational operator -(Rational a) => new(-a.Numerator, a.Denominator);

    public static bool operator <(Rational a, Rational b) => a.CompareTo(b) < 0;
    public static bool operator >(Rational a, Rational b) => a.CompareTo(b) > 0;
    public static bool operator <=(Rational a, Rational b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Rational a, Rational b) => a.CompareTo(b) >= 0;
    public static bool operator ==(Rational a, Rational b) => a.Equals(b);
    public static bool operator !=(Rational a, Rational b) => !a.Equals(b);

    public Rational Floor() => new(BigInteger.Divide(Numerator - (Numerator.Sign < 0 ? Denominator - 1 : 0), Denominator), 1);

    public Rational Ceiling() => -(-this).Floor();

    public static Rational Min(Rational a, Rational b) => a <= b ? a : b;

    public double ToDouble() => (double)Numerator / (double)Denominator;

    public int CompareTo(Rational other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public bool Equals(Rational other) => Numerator == other.Numerator && Denominator == other.Denominator;

    public override bool Equals(object? obj) => obj is Rational other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    public override string ToString() =>
        IsInteger ? Numerator.ToString(CultureInfo.InvariantCulture) : ToDouble().ToString("0.######", CultureInfo.InvariantCulture);
}
