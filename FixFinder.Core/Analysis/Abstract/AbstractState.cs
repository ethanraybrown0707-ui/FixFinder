using System.Collections.Immutable;

namespace FixFinder.Core.Analysis.Abstract;

/// <summary>
/// What every variable could hold at one point in a function, or <see cref="Unreachable"/> if no run gets there - and
/// which variables certainly hold the same object, so that a change made through one is seen through all of them.
/// </summary>
/// <remarks>
/// Aliasing is must-aliasing: after b = a, a and b are the same list until either is given another value. Where two
/// ways through the code meet, two names still share an object only if they do on both.
/// </remarks>
public sealed class AbstractState
{
    private readonly ImmutableDictionary<string, AbstractValue> _values;

    /// <summary>For each name that shares its object, the other names sharing it. Always symmetric.</summary>
    private readonly ImmutableDictionary<string, ImmutableHashSet<string>> _aliases;

    private AbstractState(ImmutableDictionary<string, AbstractValue> values, ImmutableDictionary<string, ImmutableHashSet<string>> aliases, bool reachable)
    {
        _values = values;
        _aliases = aliases;
        IsReachable = reachable;
    }

    private static readonly ImmutableDictionary<string, ImmutableHashSet<string>> NoAliases =
        ImmutableDictionary.Create<string, ImmutableHashSet<string>>(StringComparer.Ordinal);

    public static AbstractState Unreachable { get; } = new(ImmutableDictionary<string, AbstractValue>.Empty, NoAliases, false);

    public static AbstractState Start { get; } = new(ImmutableDictionary<string, AbstractValue>.Empty, NoAliases, true);

    public bool IsReachable { get; }

    public IEnumerable<string> Names => _values.Keys;

    public bool Knows(string name) => _values.ContainsKey(name);

    public AbstractValue this[string name] => _values.TryGetValue(name, out var value) ? value : AbstractValue.Unknown;

    /// <summary>The other names that certainly hold the same object as this one.</summary>
    public IEnumerable<string> AliasesOf(string name) => _aliases.TryGetValue(name, out var others) ? others : [];

    public AbstractState With(string name, AbstractValue value)
    {
        if (!IsReachable) return this;
        return value.IsImpossible ? Unreachable : new(_values.SetItem(name, value), _aliases, true);
    }

    public AbstractState Without(string name) => IsReachable ? new(_values.Remove(name), Unlinked(_aliases, name), true) : this;

    /// <summary>After b = a: b leaves whatever object it shared before and shares a's, with every name already sharing it.</summary>
    public AbstractState Aliasing(string name, string of)
    {
        if (!IsReachable || name == of) return this;

        var aliases = Unlinked(_aliases, name);
        var group = (aliases.TryGetValue(of, out var others) ? others : []).Add(of);

        foreach (var member in group)
            aliases = aliases.SetItem(member, (aliases.TryGetValue(member, out var existing) ? existing : ImmutableHashSet.Create<string>(StringComparer.Ordinal)).Add(name));

        return new(_values, aliases.SetItem(name, group.WithComparer(StringComparer.Ordinal)), true);
    }

    /// <summary>A name given a new object: it shares nothing with anyone any more.</summary>
    public AbstractState Unaliased(string name) => IsReachable && _aliases.ContainsKey(name) ? new(_values, Unlinked(_aliases, name), true) : this;

    private static ImmutableDictionary<string, ImmutableHashSet<string>> Unlinked(ImmutableDictionary<string, ImmutableHashSet<string>> aliases, string name)
    {
        if (!aliases.TryGetValue(name, out var others)) return aliases;

        aliases = aliases.Remove(name);
        foreach (var other in others)
        {
            if (!aliases.TryGetValue(other, out var theirs)) continue;
            var remaining = theirs.Remove(name);
            aliases = remaining.IsEmpty ? aliases.Remove(other) : aliases.SetItem(other, remaining);
        }

        return aliases;
    }

    /// <summary>Two names share an object after two ways meet only if they did on both.</summary>
    private static ImmutableDictionary<string, ImmutableHashSet<string>> Common(
        ImmutableDictionary<string, ImmutableHashSet<string>> mine, ImmutableDictionary<string, ImmutableHashSet<string>> theirs)
    {
        var common = NoAliases;

        foreach (var (name, others) in mine)
        {
            if (!theirs.TryGetValue(name, out var otherSide)) continue;
            var both = others.Intersect(otherSide);
            if (!both.IsEmpty) common = common.SetItem(name, both);
        }

        return common;
    }

    public AbstractState Join(AbstractState other)
    {
        if (!IsReachable) return other;
        if (!other.IsReachable) return this;

        var joined = _values.Keys.Intersect(other._values.Keys)
            .ToImmutableDictionary(name => name, name => _values[name].Join(other._values[name]));
        return new(joined, Common(_aliases, other._aliases), true);
    }

    public AbstractState Widen(AbstractState next)
    {
        if (!IsReachable) return next;
        if (!next.IsReachable) return this;

        var widened = _values.Keys.Intersect(next._values.Keys)
            .ToImmutableDictionary(name => name, name => _values[name].Widen(next._values[name]));
        return new(widened, Common(_aliases, next._aliases), true);
    }

    public AbstractState Narrow(AbstractState next)
    {
        if (!IsReachable || !next.IsReachable) return next;

        var narrowed = _values.Keys.Intersect(next._values.Keys)
            .ToImmutableDictionary(name => name, name => _values[name].Narrow(next._values[name]));
        return new(narrowed, Common(_aliases, next._aliases), true);
    }

    public bool SameAs(AbstractState other) =>
        IsReachable == other.IsReachable && _values.Count == other._values.Count &&
        _values.All(pair => other._values.TryGetValue(pair.Key, out var value) && value == pair.Value) &&
        _aliases.Count == other._aliases.Count &&
        _aliases.All(pair => other._aliases.TryGetValue(pair.Key, out var others) && others.SetEquals(pair.Value));

    public override string ToString()
    {
        if (!IsReachable) return "unreachable";

        var values = string.Join(", ", _values.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}"));
        var shared = _aliases.Where(p => string.CompareOrdinal(p.Key, p.Value.Min()) < 0)
            .OrderBy(p => p.Key)
            .Select(p => $"{p.Key} = {string.Join(" = ", p.Value.Order())}");
        return string.Join("; ", new[] { values }.Concat(shared));
    }
}
