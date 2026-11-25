using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Analysis.Abstract;

/// <summary>
/// What every variable could hold at one point in a function, or <see cref="Unreachable"/> if no run gets there - and
/// which variables certainly hold the same object, so that a change made through one is seen through all of them.
/// </summary>
/// <remarks>
/// Aliasing is must-aliasing: after b = a, a and b are the same list until either is given another value. Where two
/// ways through the code meet, two names still share an object only if they do on both. So are the keys a map certainly
/// holds: a key is in it after two ways meet only if it is on both. And so is a variable known to be past a bound.
/// </remarks>
public sealed partial class AbstractState
{
    private readonly ImmutableDictionary<string, AbstractValue> _values;

    /// <summary>For each name that shares its object, the other names sharing it. Always symmetric.</summary>
    private readonly ImmutableDictionary<string, ImmutableHashSet<string>> _aliases;

    /// <summary>
    /// The keys certainly in a map here - after map.put(key, value), or where map.containsKey(key) was true - each as the
    /// map's text and the key's, so that looking the key up is known to find something rather than null.
    /// </summary>
    private readonly ImmutableHashSet<(string Map, string Key)> _keys;

    /// <summary>
    /// The variables certainly past a bound here: after i &lt;= first.length() came out false, i is past first.length(),
    /// though what either is need not be known at all - and a loop that tests i &lt;= first.length() again, with neither
    /// given another value since, can never start. Each is the variable, the bound as IrText writes it, the one variable
    /// the bound is read from, and whether the variable is past it (&gt;) or only not below it (&gt;=).
    /// </summary>
    private readonly ImmutableHashSet<Past> _past;

    /// <summary><see cref="Name"/> &gt; <see cref="Bound"/>, or &gt;= when not <see cref="Strictly"/>; <see cref="BoundName"/> is what the bound is read from.</summary>
    public readonly record struct Past(string Name, string Bound, string BoundName, bool Strictly);

    private AbstractState(
        ImmutableDictionary<string, AbstractValue> values, ImmutableDictionary<string, ImmutableHashSet<string>> aliases, bool reachable,
        ImmutableHashSet<(string Map, string Key)> keys, ImmutableHashSet<Past> past)
    {
        _values = values;
        _aliases = aliases;
        IsReachable = reachable;
        _keys = keys;
        _past = past;
    }

    private static readonly ImmutableHashSet<(string Map, string Key)> NoKeys = ImmutableHashSet<(string Map, string Key)>.Empty;

    private static readonly ImmutableHashSet<Past> NoneKnownPast = ImmutableHashSet<Past>.Empty;

    private static readonly ImmutableDictionary<string, ImmutableHashSet<string>> NoAliases =
        ImmutableDictionary.Create<string, ImmutableHashSet<string>>(StringComparer.Ordinal);

    public static AbstractState Unreachable { get; } = new(ImmutableDictionary<string, AbstractValue>.Empty, NoAliases, false, NoKeys, NoneKnownPast);

    public static AbstractState Start { get; } = new(ImmutableDictionary<string, AbstractValue>.Empty, NoAliases, true, NoKeys, NoneKnownPast);

    public bool IsReachable { get; }

    public IEnumerable<string> Names => _values.Keys;

    public bool Knows(string name) => _values.ContainsKey(name);

    public AbstractValue this[string name] => _values.TryGetValue(name, out var value) ? value : AbstractValue.Unknown;

    /// <summary>The other names that certainly hold the same object as this one.</summary>
    public IEnumerable<string> AliasesOf(string name) => _aliases.TryGetValue(name, out var others) ? others : [];

    public AbstractState With(string name, AbstractValue value)
    {
        if (!IsReachable) return this;
        return value.IsImpossible ? Unreachable : new(_values.SetItem(name, value), _aliases, true, _keys, _past);
    }

    public AbstractState Without(string name) =>
        IsReachable ? new(_values.Remove(name), Unlinked(_aliases, name), true, KeysNotNaming(name), PastNotNaming(name)) : this;

    /// <summary>Whether any variable is known to be past a bound here.</summary>
    public bool KnowsPast => !_past.IsEmpty;

    /// <summary>Whether <paramref name="name"/> is certainly past <paramref name="bound"/> - only strictly, or at least not below it.</summary>
    public bool IsPast(string name, string bound, bool strictly) =>
        _past.Any(known => known.Name == name && known.Bound == bound && (known.Strictly || !strictly));

    /// <summary>A test that came out showing <paramref name="name"/> past <paramref name="bound"/>, read from <paramref name="boundName"/>.</summary>
    public AbstractState Passed(string name, string bound, string boundName, bool strictly) =>
        IsReachable ? new(_values, _aliases, true, _keys, _past.Add(new Past(name, bound, boundName, strictly))) : this;

    /// <summary>A name given another value: nothing is known any more of how it stands to a bound, or a bound read from it.</summary>
    public AbstractState WithoutPastNaming(string name)
    {
        if (!IsReachable || _past.IsEmpty) return this;
        var kept = PastNotNaming(name);
        return kept.Count == _past.Count ? this : new(_values, _aliases, true, _keys, kept);
    }

    private ImmutableHashSet<Past> PastNotNaming(string name) =>
        _past.IsEmpty ? _past : _past.Except(_past.Where(known => known.Name == name || known.BoundName == name));

    /// <summary>Whether <paramref name="key"/> is certainly in <paramref name="map"/> here, each written as IrText writes it.</summary>
    public bool HasKey(string map, string key) => _keys.Contains((map, key));

    /// <summary>After map.put(key, value), or where map.containsKey(key) is true: the key is in the map.</summary>
    public AbstractState WithKey(string map, string key) => IsReachable ? new(_values, _aliases, true, _keys.Add((map, key)), _past) : this;

    /// <summary>Something removed from a map - or a map given away to code that can change it: no key is known to be in it.</summary>
    public AbstractState WithoutKeysOf(string map) =>
        IsReachable && _keys.Any(k => k.Map == map) ? new(_values, _aliases, true, _keys.Except(_keys.Where(k => k.Map == map)), _past) : this;

    /// <summary>
    /// A name given another value: a key written with it - dp.get(i - 1) once i has moved on - is another key now, and a map
    /// reached through it may be another map.
    /// </summary>
    public AbstractState WithoutKeysNaming(string name)
    {
        if (!IsReachable || _keys.IsEmpty) return this;
        var kept = KeysNotNaming(name);
        return kept.Count == _keys.Count ? this : new(_values, _aliases, true, kept, _past);
    }

    private ImmutableHashSet<(string Map, string Key)> KeysNotNaming(string name) =>
        _keys.IsEmpty ? _keys : _keys.Except(_keys.Where(k => NamesIn(k.Map).Contains(name) || NamesIn(k.Key).Contains(name)));

    [GeneratedRegex(@"[A-Za-z_$][\w$]*")]
    private static partial Regex Identifier();

    private static HashSet<string> NamesIn(string text) => Identifier().Matches(text).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>After b = a: b leaves whatever object it shared before and shares a's, with every name already sharing it.</summary>
    public AbstractState Aliasing(string name, string of)
    {
        if (!IsReachable || name == of) return this;

        var aliases = Unlinked(_aliases, name);
        var group = (aliases.TryGetValue(of, out var others) ? others : []).Add(of);

        foreach (var member in group)
            aliases = aliases.SetItem(member, (aliases.TryGetValue(member, out var existing) ? existing : ImmutableHashSet.Create<string>(StringComparer.Ordinal)).Add(name));

        return new(_values, aliases.SetItem(name, group.WithComparer(StringComparer.Ordinal)), true, _keys, _past);
    }

    /// <summary>A name given a new object: it shares nothing with anyone any more.</summary>
    public AbstractState Unaliased(string name) => IsReachable && _aliases.ContainsKey(name) ? new(_values, Unlinked(_aliases, name), true, _keys, _past) : this;

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
        return new(joined, Common(_aliases, other._aliases), true, _keys.Intersect(other._keys), _past.Intersect(other._past));
    }

    public AbstractState Widen(AbstractState next)
    {
        if (!IsReachable) return next;
        if (!next.IsReachable) return this;

        var widened = _values.Keys.Intersect(next._values.Keys)
            .ToImmutableDictionary(name => name, name => _values[name].Widen(next._values[name]));
        return new(widened, Common(_aliases, next._aliases), true, _keys.Intersect(next._keys), _past.Intersect(next._past));
    }

    public AbstractState Narrow(AbstractState next)
    {
        if (!IsReachable || !next.IsReachable) return next;

        var narrowed = _values.Keys.Intersect(next._values.Keys)
            .ToImmutableDictionary(name => name, name => _values[name].Narrow(next._values[name]));
        return new(narrowed, Common(_aliases, next._aliases), true, _keys.Intersect(next._keys), _past.Intersect(next._past));
    }

    public bool SameAs(AbstractState other) =>
        IsReachable == other.IsReachable && _values.Count == other._values.Count &&
        _values.All(pair => other._values.TryGetValue(pair.Key, out var value) && value == pair.Value) &&
        _aliases.Count == other._aliases.Count &&
        _aliases.All(pair => other._aliases.TryGetValue(pair.Key, out var others) && others.SetEquals(pair.Value)) &&
        _keys.SetEquals(other._keys) && _past.SetEquals(other._past);

    public override string ToString()
    {
        if (!IsReachable) return "unreachable";

        var values = string.Join(", ", _values.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}"));
        var shared = _aliases.Where(p => string.CompareOrdinal(p.Key, p.Value.Min()) < 0)
            .OrderBy(p => p.Key)
            .Select(p => $"{p.Key} = {string.Join(" = ", p.Value.Order())}");
        var past = _past.OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Bound, StringComparer.Ordinal)
            .Select(p => $"{p.Name} {(p.Strictly ? ">" : ">=")} {p.Bound}");
        return string.Join("; ", new[] { values }.Concat(shared).Concat(past));
    }
}
