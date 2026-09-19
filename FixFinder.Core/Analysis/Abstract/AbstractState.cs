using System.Collections.Immutable;

namespace FixFinder.Core.Analysis.Abstract;

/// <summary>What every variable could hold at one point in a function, or <see cref="Unreachable"/> if no run gets there.</summary>
public sealed class AbstractState
{
    private readonly ImmutableDictionary<string, AbstractValue> _values;

    private AbstractState(ImmutableDictionary<string, AbstractValue> values, bool reachable)
    {
        _values = values;
        IsReachable = reachable;
    }

    public static AbstractState Unreachable { get; } = new(ImmutableDictionary<string, AbstractValue>.Empty, false);

    public static AbstractState Start { get; } = new(ImmutableDictionary<string, AbstractValue>.Empty, true);

    public bool IsReachable { get; }

    public IEnumerable<string> Names => _values.Keys;

    public bool Knows(string name) => _values.ContainsKey(name);

    public AbstractValue this[string name] => _values.TryGetValue(name, out var value) ? value : AbstractValue.Unknown;

    public AbstractState With(string name, AbstractValue value)
    {
        if (!IsReachable) return this;
        return value.IsImpossible ? Unreachable : new(_values.SetItem(name, value), true);
    }

    public AbstractState Without(string name) => IsReachable ? new(_values.Remove(name), true) : this;

    public AbstractState Join(AbstractState other)
    {
        if (!IsReachable) return other;
        if (!other.IsReachable) return this;

        var joined = _values.Keys.Intersect(other._values.Keys)
            .ToImmutableDictionary(name => name, name => _values[name].Join(other._values[name]));
        return new(joined, true);
    }

    public AbstractState Widen(AbstractState next)
    {
        if (!IsReachable) return next;
        if (!next.IsReachable) return this;

        var widened = _values.Keys.Intersect(next._values.Keys)
            .ToImmutableDictionary(name => name, name => _values[name].Widen(next._values[name]));
        return new(widened, true);
    }

    public AbstractState Narrow(AbstractState next)
    {
        if (!IsReachable || !next.IsReachable) return next;

        var narrowed = _values.Keys.Intersect(next._values.Keys)
            .ToImmutableDictionary(name => name, name => _values[name].Narrow(next._values[name]));
        return new(narrowed, true);
    }

    public bool SameAs(AbstractState other) =>
        IsReachable == other.IsReachable && _values.Count == other._values.Count &&
        _values.All(pair => other._values.TryGetValue(pair.Key, out var value) && value == pair.Value);

    public override string ToString() =>
        !IsReachable ? "unreachable" : string.Join(", ", _values.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}"));
}
