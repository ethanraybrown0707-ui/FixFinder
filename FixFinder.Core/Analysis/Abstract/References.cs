using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Abstract;

/// <summary>
/// The C++ references a function declares, and what each is another name for. After int&amp; alias = rows, alias and rows
/// are one variable: a value written to either is the value of both, and what is learned of one is true of the other.
/// A reference bound one way or another - int&amp; larger = a &gt; b ? a : b - may be either, so a write to it leaves
/// neither known, and a write to either leaves it unknown.
/// </summary>
/// <remarks>
/// A reference that may be another name for a variable other code can reach - an int&amp; parameter, which is the
/// caller's variable, or a reference to a global - can be changed by any call, as that variable can.
/// </remarks>
public sealed class References
{
    private static readonly IReadOnlySet<string> NoNames = new HashSet<string>();

    private readonly Dictionary<string, HashSet<string>> _same;
    private readonly Dictionary<string, HashSet<string>> _maybe;

    private References(Dictionary<string, HashSet<string>> same, Dictionary<string, HashSet<string>> maybe, IReadOnlySet<string> outside)
    {
        _same = same;
        _maybe = maybe;
        Outside = outside;
    }

    /// <summary>For a function that declares no reference, or a language that has none.</summary>
    public static References None { get; } = new([], [], NoNames);

    /// <summary>The names that may stand for a variable other code can reach, which any call may change.</summary>
    public IReadOnlySet<string> Outside { get; }

    /// <summary>The other names certainly given to the variable <paramref name="name"/> names.</summary>
    public IReadOnlySet<string> Same(string name) => _same.TryGetValue(name, out var same) ? same : NoNames;

    /// <summary>The other names that may be given to the variable <paramref name="name"/> names, or may not.</summary>
    public IReadOnlySet<string> Maybe(string name) => _maybe.TryGetValue(name, out var maybe) ? maybe : NoNames;

    /// <summary>Every other name the variable may go by.</summary>
    public IEnumerable<string> Sharing(string name) => Same(name).Concat(Maybe(name));

    public static References Of(IrFunction function, SourceLanguage language)
    {
        if (language != SourceLanguage.Cpp) return None;

        var parameters = function.Parameters.Where(parameter => parameter.ByReference).Select(parameter => parameter.Name).ToList();
        var declares = IrWalk.Statements(function.Body).OfType<Declare>().ToList();
        var types = function.Parameters.Select(parameter => (parameter.Name, parameter.Type)).Concat(declares.Select(declare => (Name: declare.Variable, declare.Type)))
            .GroupBy(typed => typed.Name, StringComparer.Ordinal)
            .ToDictionary(named => named.Key, named => named.First().Type, StringComparer.Ordinal);

        // const long& wide = count, with count an int, is bound to a long made from count's value, not to count.
        bool Converted(Declare reference, string target) =>
            types.TryGetValue(target, out var targetType) && IsNumber(reference.Type) && IsNumber(targetType) && reference.Type.Name != targetType.Name;

        var bound = declares
            .Where(declare => declare is { Lifetime: Lifetime.Borrowed, Initial: not null })
            .Select(declare => (
                Reference: declare.Variable,
                Certain: Certainly(declare.Initial!) is { } target && !Converted(declare, target) ? target : null,
                Possibly: Possibly(declare.Initial!)))
            .Where(binding => binding.Certain is not null || binding.Possibly.Count > 0)
            .ToList();
        if (bound.Count == 0 && parameters.Count == 0) return None;

        // Names bound to each other, one variable for each group: int& a = x; int& b = a makes a, b and x one.
        var group = new Dictionary<string, string>(StringComparer.Ordinal);
        string Root(string name)
        {
            while (group.TryGetValue(name, out var up) && up != name) name = up;
            return name;
        }

        foreach (var (reference, certain, _) in bound)
            if (certain is not null && Root(reference) is var one && Root(certain) is var other && one != other) group[one] = other;

        var groups = bound.SelectMany(binding => binding.Possibly.Append(binding.Reference).Concat(binding.Certain is { } certain ? [certain] : []))
            .Distinct(StringComparer.Ordinal)
            .GroupBy(Root, StringComparer.Ordinal)
            .ToDictionary(members => members.Key, members => members.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

        var same = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var members in groups.Values.Where(members => members.Count > 1))
            foreach (var member in members)
                same[member] = members.Where(other => other != member).ToHashSet(StringComparer.Ordinal);

        // What is bound one way or another: each name of the reference's variable may be each name of either's.
        var maybe = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void MayBeOne(string first, string second)
        {
            foreach (var one in groups[Root(first)])
                foreach (var other in groups[Root(second)])
                {
                    if (one == other || same.TryGetValue(one, out var certain) && certain.Contains(other)) continue;
                    (maybe.TryGetValue(one, out var known) ? known : maybe[one] = new HashSet<string>(StringComparer.Ordinal)).Add(other);
                    (maybe.TryGetValue(other, out var back) ? back : maybe[other] = new HashSet<string>(StringComparer.Ordinal)).Add(one);
                }
        }

        foreach (var (reference, _, possibly) in bound)
            foreach (var either in possibly)
                MayBeOne(reference, either);

        // Every name of a variable that may be one other code reaches: an int& parameter's, or a global's.
        var locals = IrWalk.LocalNames(function, assigningDeclares: false);
        var reached = parameters.Concat(groups.Values.SelectMany(members => members).Where(name => !locals.Contains(name))).ToList();
        var outside = reached.Concat(reached.SelectMany(name => same.TryGetValue(name, out var others) ? others : []))
            .Concat(reached.SelectMany(name => maybe.TryGetValue(name, out var others) ? others : []))
            .ToHashSet(StringComparer.Ordinal);

        return new References(same, maybe, outside);
    }

    /// <summary>A number type as the C reader names it, which a const reference to another number type is not bound to.</summary>
    private static bool IsNumber(IrType type) =>
        type.Arguments.Count == 0 && type.Name is "int" or "uint" or "long" or "ulong" or "short" or "ushort" or "sbyte" or "byte" or "double" or "bool";

    /// <summary>The variable a reference is certainly bound to: rows for int&amp; r = rows, count for this-&gt;count.</summary>
    private static string? Certainly(Expr initial) => initial switch
    {
        Name { Identifier: var name } when name != "this" => name,
        Member { Target: Name { Identifier: "this" }, MemberName: var field } => field,
        Cast { Value: var value } => Certainly(value),
        _ => null,
    };

    /// <summary>The variables a reference bound one way or another may be bound to: a and b for c ? a : b.</summary>
    private static List<string> Possibly(Expr initial) => initial switch
    {
        Conditional choice => [.. Either(choice.WhenTrue), .. Either(choice.WhenFalse)],
        Cast { Value: var value } => Possibly(value),
        _ => [],
    };

    private static IEnumerable<string> Either(Expr branch) =>
        Certainly(branch) is { } name ? [name] : Possibly(branch);
}
