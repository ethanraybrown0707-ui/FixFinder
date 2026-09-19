namespace FixFinder.Core.Analysis.Abstract;

[Flags]
public enum ValueKind
{
    Nothing = 0,
    Integer = 1 << 0,
    Real = 1 << 1,
    Boolean = 1 << 2,
    Text = 1 << 3,
    Null = 1 << 4,
    List = 1 << 5,
    Tuple = 1 << 6,
    Dictionary = 1 << 7,
    Set = 1 << 8,
    Range = 1 << 9,
    Object = 1 << 10,
    Function = 1 << 11,
    Module = 1 << 12,
    File = 1 << 13,

    Numeric = Integer | Real | Boolean,
    Sized = Text | List | Tuple | Dictionary | Set | Range,
    AnythingButNull = Integer | Real | Boolean | Text | List | Tuple | Dictionary | Set | Range | Object | Function | Module | File,
}

/// <summary>
/// What a variable could hold at one point: the kinds of value, the numbers (for numeric kinds, or a range's items),
/// and the length (for text, collections and ranges).
/// </summary>
public sealed record AbstractValue(ValueKind Kinds, Interval Number, Interval Length)
{
    /// <summary>A value nothing is known about - assumed not to be null, so null only ever comes from somewhere visible.</summary>
    public static AbstractValue Unknown { get; } = new(ValueKind.AnythingButNull, Interval.Top, Interval.NonNegative) { NullnessKnown = false };

    /// <summary>
    /// False when null was left out only by assumption, so the program's own check for null must still be allowed to succeed.
    /// </summary>
    public bool NullnessKnown { get; init; } = true;

    public static AbstractValue Null { get; } = new(ValueKind.Null, Interval.Empty, Interval.Empty);

    public static AbstractValue Integer(Interval range) => new(ValueKind.Integer, range, Interval.Empty);

    public static AbstractValue Real(Interval range) => new(ValueKind.Real, range, Interval.Empty);

    public static AbstractValue Boolean { get; } = new(ValueKind.Boolean, new Interval(0, 1), Interval.Empty);

    public static AbstractValue Text(Interval length) => new(ValueKind.Text, Interval.Empty, length);

    public static AbstractValue Sized(ValueKind kind, Interval length) => new(kind, Interval.Empty, length);

    public static AbstractValue Of(ValueKind kind) => new(kind,
        (kind & ValueKind.Numeric) != 0 ? Interval.Top : Interval.Empty,
        (kind & ValueKind.Sized) != 0 ? Interval.NonNegative : Interval.Empty);

    public bool IsImpossible => Kinds == ValueKind.Nothing;

    public bool IsUnknown => Kinds == ValueKind.AnythingButNull;

    public bool IsOnly(ValueKind kinds) => Kinds != ValueKind.Nothing && (Kinds & ~kinds) == 0;

    public bool MayBe(ValueKind kinds) => (Kinds & kinds) != 0;

    public bool IsNumber => IsOnly(ValueKind.Numeric);

    public bool IsNull => Kinds == ValueKind.Null;

    public bool MayBeNull => MayBe(ValueKind.Null);

    public AbstractValue Join(AbstractValue other) =>
        new(Kinds | other.Kinds, Number.Join(other.Number), Length.Join(other.Length)) { NullnessKnown = NullnessKnown && other.NullnessKnown };

    public AbstractValue Widen(AbstractValue next) =>
        new(Kinds | next.Kinds, Number.Widen(next.Number), Length.Widen(next.Length)) { NullnessKnown = NullnessKnown && next.NullnessKnown };

    public AbstractValue Narrow(AbstractValue next) =>
        new(Kinds & next.Kinds, Number.Narrow(next.Number), Length.Narrow(next.Length)) { NullnessKnown = NullnessKnown && next.NullnessKnown };

    public AbstractValue WithoutNull() => this with { Kinds = Kinds & ~ValueKind.Null };

    public AbstractValue OnlyNull() => MayBeNull || !NullnessKnown ? Null : this with { Kinds = ValueKind.Nothing };

    public AbstractValue Kept(ValueKind kinds) => this with { Kinds = Kinds & kinds };

    public AbstractValue WithNumber(Interval range)
    {
        var kept = Number.Meet(range);
        return kept.IsEmpty && IsNumber ? this with { Kinds = ValueKind.Nothing, Number = kept } : this with { Number = kept };
    }

    public AbstractValue WithLength(Interval range)
    {
        var kept = Length.Meet(range);
        return kept.IsEmpty && IsOnly(ValueKind.Sized) ? this with { Kinds = ValueKind.Nothing, Length = kept } : this with { Length = kept };
    }

    public override string ToString()
    {
        if (IsUnknown) return "?";
        var parts = new List<string> { Kinds.ToString() };
        if ((Kinds & ValueKind.Numeric) != 0 && !Number.IsTop) parts.Add(Number.ToString());
        if ((Kinds & ValueKind.Sized) != 0 && Length != Interval.NonNegative) parts.Add("len " + Length);
        return string.Join(" ", parts);
    }
}
