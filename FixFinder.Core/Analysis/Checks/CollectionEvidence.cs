using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>How a search of a collection works: one item after another from the start, or straight to the item.</summary>
internal enum Lookup
{
    /// <summary>The code does not show what kind of collection it is - a parameter with no type, say.</summary>
    NotKnown,

    /// <summary>A list, an array, a tuple or a string: a search compares the items one at a time from the start.</summary>
    FromTheStart,

    /// <summary>A set, a dictionary, a map or a range: a search goes straight to where the item would be.</summary>
    Direct,
}

/// <summary>
/// What the code shows about one collection: how a search of it works, what to call it, what it holds, and whether each
/// value it is given is a new collection of its own rather than one some other code also holds.
/// </summary>
/// <param name="Elements">The type of the items, as the language writes it, when every item is known to be of it.</param>
/// <param name="Fresh">Whether every value it is given is made right there - a literal, a copy - and so held by nothing else.</param>
internal sealed record Evidence(Lookup Lookup, string? Kind = null, string? Elements = null, bool Fresh = false)
{
    public static Evidence NotKnown { get; } = new(Lookup.NotKnown);

    public static Evidence Direct { get; } = new(Lookup.Direct);
}

/// <summary>One place a name is given a value: a declaration with its type, an assignment, a parameter.</summary>
internal sealed record Definition(SourceSpan At, IrType? Declared, Expr? Value, bool IsParameter = false, bool Understood = true);

/// <summary>
/// What one function's own code shows about the collections it uses, worked out from how each one is declared and what
/// it is given. Nothing is guessed: a name given a value FixFinder cannot see through is <see cref="Lookup.NotKnown"/>.
/// </summary>
internal sealed class CollectionEvidence(IrProgram program, IrFunction function)
{
    private readonly Dictionary<string, Evidence> _known = new(StringComparer.Ordinal);

    public SourceLanguage Language => program.Language;

    public Evidence Of(string name)
    {
        if (_known.TryGetValue(name, out var known)) return known;

        var local = Definitions(function, name).ToList();
        var evidence = local.Count > 0 ? Combined(local) : Outside(name);
        _known[name] = evidence;
        return evidence;
    }

    /// <summary>Whether the name is given its values in this function, rather than being a parameter, a global or a field.</summary>
    public bool IsOwnLocal(string name) =>
        Definitions(function, name).ToList() is { Count: > 0 } found && found.All(d => !d.IsParameter && d.Understood);

    /// <summary>Every place in the function that gives the name a value, including ones FixFinder cannot see through.</summary>
    public static IEnumerable<Definition> Definitions(IrFunction function, string name)
    {
        foreach (var parameter in function.Parameters.Where(p => p.Name == name))
            yield return new Definition(parameter.Span, parameter.Type.IsUnknown ? null : parameter.Type, null, IsParameter: true);

        foreach (var statement in IrWalk.Statements(function.Body))
        {
            switch (statement)
            {
                case Declare declare when declare.Variable == name:
                    yield return new Definition(declare.Span, declare.Type.IsUnknown ? null : declare.Type, declare.Initial);
                    break;

                case Assign { Target: Name target } assign when target.Identifier == name:
                    yield return new Definition(assign.Span, null, assign.Compound is null ? assign.Value : null, Understood: assign.Compound is null);
                    break;

                case Assign { Target: CollectionLiteral unpacked } assign when Bound(unpacked).Contains(name):
                case ForEach loop when Bound(loop.Target).Contains(name):
                case Using { Variable: { } variable } when Bound(variable).Contains(name):
                case OpaqueStmt opaque when opaque.MayAssign.Contains(name):
                    yield return new Definition(statement.Span, null, null, Understood: false);
                    break;

                case Try attempt when attempt.Handlers.Any(h => h.Variable == name):
                    yield return new Definition(statement.Span, null, null, Understood: false);
                    break;
            }

            foreach (var assigned in IrWalk.Expressions(statement).SelectMany(Within).OfType<AssignValue>())
            {
                if (assigned.Target is Name { Identifier: var identifier } && identifier == name)
                    yield return new Definition(assigned.Span, null, assigned.Value);
            }
        }
    }

    /// <summary>An expression and every expression inside it, however deep.</summary>
    public static IEnumerable<Expr> Within(Expr expression) => [expression, .. IrWalk.Children(expression).SelectMany(Within)];

    private static HashSet<string> Bound(Expr target) => target switch
    {
        Name name => [name.Identifier],
        CollectionLiteral unpacked => unpacked.Items.SelectMany(Bound).ToHashSet(StringComparer.Ordinal),
        _ => [],
    };

    /// <summary>
    /// What all of a name's definitions agree on. A declared type decides the kind in a language that checks it, where
    /// every value has to fit; otherwise every value has to be of the same kind.
    /// </summary>
    private Evidence Combined(IReadOnlyList<Definition> definitions)
    {
        if (definitions.Any(d => !d.Understood)) return Evidence.NotKnown;

        var fresh = definitions.All(d => !d.IsParameter) && definitions.Where(d => d.Value is not null).All(d => IsFresh(d.Value!));

        var declared = definitions.Select(d => d.Declared).OfType<IrType>().Select(FromType).Where(e => e.Lookup != Lookup.NotKnown).ToList();
        if (declared.Count > 0)
        {
            var first = declared[0];
            if (declared.Any(e => e.Lookup != first.Lookup)) return Evidence.NotKnown;
            return first with { Fresh = fresh, Elements = declared.All(e => e.Elements == first.Elements) ? first.Elements : null };
        }

        var valued = definitions.Select(d => d.Value is null ? Evidence.NotKnown : FromValue(d.Value)).ToList();
        if (valued.Any(e => e.Lookup != valued[0].Lookup) || valued[0].Lookup == Lookup.NotKnown) return Evidence.NotKnown;

        var elements = valued.All(e => e.Elements == valued[0].Elements) ? valued[0].Elements : null;
        var kind = valued.All(e => e.Kind == valued[0].Kind) ? valued[0].Kind : valued[0].Lookup == Lookup.FromTheStart ? "collection" : null;
        return valued[0] with { Kind = kind, Elements = elements, Fresh = fresh };
    }

    /// <summary>
    /// A name the function does not give a value to: a global, whose module-level assignments say what it is, or a field,
    /// whose declared type does. Either way other code can reach it, so it is never fresh.
    /// </summary>
    private Evidence Outside(string name)
    {
        var module = program.AllFunctions.FirstOrDefault(f =>
            f.Name == IrFunction.ModuleBody && !ReferenceEquals(f, function) &&
            string.Equals(f.Span.File, function.Span.File, StringComparison.OrdinalIgnoreCase));

        if (module is not null && Definitions(module, name).ToList() is { Count: > 0 } global)
            return Combined(global) with { Fresh = false };

        var field = program.Classes.Where(c => c.Name == function.Owner).SelectMany(c => c.Fields).FirstOrDefault(f => f.Name == name);
        return field is not null ? FromType(field.Type) with { Fresh = false } : Evidence.NotKnown;
    }

    /// <summary>What a declared type says, by the names the languages' own libraries give their collections.</summary>
    public Evidence FromType(IrType type)
    {
        var element = type.Arguments.Count == 1 && !type.Arguments[0].IsUnknown ? type.Arguments[0].Name : null;

        return type.Name switch
        {
            "list" or "List" or "ArrayList" or "LinkedList" or "IList" or "IReadOnlyList" or "Vector" or "ImmutableList" =>
                new Evidence(Lookup.FromTheStart, "list", element),
            "array" => new Evidence(Lookup.FromTheStart, "array", element),
            "tuple" => new Evidence(Lookup.FromTheStart, "tuple"),
            "str" or "string" or "String" => new Evidence(Lookup.FromTheStart, "string"),
            "set" or "frozenset" or "dict" or "range" or "Counter" or "defaultdict" or "OrderedDict" or
            "Set" or "HashSet" or "LinkedHashSet" or "TreeSet" or "SortedSet" or "EnumSet" or "ISet" or "IReadOnlySet" or "ImmutableHashSet" or
            "FrozenSet" or "Map" or "HashMap" or "LinkedHashMap" or "TreeMap" or "Hashtable" or "Dictionary" or "IDictionary" or
            "IReadOnlyDictionary" or "SortedDictionary" or "ConcurrentDictionary" or "FrozenDictionary" => Evidence.Direct,
            _ => Evidence.NotKnown,
        };
    }

    /// <summary>What a value says about the collection it makes, when it plainly makes one.</summary>
    public Evidence FromValue(Expr value) => value switch
    {
        CollectionLiteral { Kind: CollectionKind.List or CollectionKind.Array } list =>
            new Evidence(Lookup.FromTheStart, Language == SourceLanguage.JavaScript ? "array" : list.Kind == CollectionKind.Array ? "array" : "list", ItemsType(list.Items)),
        CollectionLiteral { Kind: CollectionKind.Tuple } tuple => new Evidence(Lookup.FromTheStart, "tuple", ItemsType(tuple.Items)),
        CollectionLiteral { Kind: CollectionKind.Set or CollectionKind.Dictionary } => Evidence.Direct,
        Literal { Kind: LiteralKind.Text } => new Evidence(Lookup.FromTheStart, "string"),
        Opaque { What: "list comprehension" } => new Evidence(Lookup.FromTheStart, "list"),
        Opaque { What: "set comprehension" or "dictionary comprehension" } => Evidence.Direct,
        NewObject made => FromMade(made),
        Call { Callee: Name { Identifier: var called } } call when Language == SourceLanguage.Python => called switch
        {
            "list" or "sorted" => new Evidence(Lookup.FromTheStart, "list"),
            "tuple" => new Evidence(Lookup.FromTheStart, "tuple"),
            "str" or "input" => new Evidence(Lookup.FromTheStart, "string"),
            "set" or "frozenset" or "dict" or "range" or "Counter" or "defaultdict" or "OrderedDict" => Evidence.Direct,
            _ => Evidence.NotKnown,
        },
        Call { Callee: Member { MemberName: "split" or "splitlines" } } when Language == SourceLanguage.Python =>
            new Evidence(Lookup.FromTheStart, "list", "str"),
        Call { Callee: Member { MemberName: "split" } } when Language == SourceLanguage.JavaScript =>
            new Evidence(Lookup.FromTheStart, "array", "string"),
        Call { Callee: Member { Target: Name { Identifier: "Array" }, MemberName: "from" or "of" } } when Language == SourceLanguage.JavaScript =>
            new Evidence(Lookup.FromTheStart, "array"),
        Call { Callee: Member { Target: Name { Identifier: "List" }, MemberName: "of" } or Member { Target: Name { Identifier: "Arrays" }, MemberName: "asList" } } made
            when Language == SourceLanguage.Java => new Evidence(Lookup.FromTheStart, "list", ItemsType(made.Arguments.Select(a => a.Value).ToList())),
        Call { Callee: Member { Target: Name { Identifier: "Set" or "Map" }, MemberName: "of" } } when Language == SourceLanguage.Java => Evidence.Direct,
        _ => Evidence.NotKnown,
    };

    private Evidence FromMade(NewObject made)
    {
        var byType = FromType(made.Type);
        if (byType.Lookup != Lookup.NotKnown) return byType;

        // JavaScript's own collections, which are made with new and have no type written anywhere else.
        return made.Type.Name switch
        {
            "Array" when Language == SourceLanguage.JavaScript => new Evidence(Lookup.FromTheStart, "array"),
            "Set" or "Map" or "WeakSet" or "WeakMap" when Language == SourceLanguage.JavaScript => Evidence.Direct,
            _ => Evidence.NotKnown,
        };
    }

    /// <summary>
    /// A value that makes a collection of its own right there: a literal, a comprehension, or a copy - never a name or a
    /// field, which some other code may also hold and change.
    /// </summary>
    private bool IsFresh(Expr value) => value switch
    {
        CollectionLiteral => true,
        Opaque { What: "list comprehension" } => true,
        NewObject => true,
        Call { Callee: Name { Identifier: "list" or "sorted" or "tuple" } } when Language == SourceLanguage.Python => true,
        Call { Callee: Member { MemberName: "split" or "splitlines" } } when Language is SourceLanguage.Python or SourceLanguage.JavaScript => true,
        Call { Callee: Member { Target: Name { Identifier: "Array" }, MemberName: "from" or "of" } } when Language == SourceLanguage.JavaScript => true,
        Call { Callee: Member { Target: Name { Identifier: "List" }, MemberName: "of" } } when Language == SourceLanguage.Java => true,
        Call { Callee: Member { Target: Name { Identifier: "Arrays" }, MemberName: "asList" } } made when Language == SourceLanguage.Java =>
            made.Arguments.All(a => a.Value is Literal),
        _ => false,
    };

    /// <summary>
    /// The type every item of a literal has, written as the language writes it, or null when they differ or are not
    /// plain values. Python's items only need to be hashable for a set to hold them, so any mix of plain values will do.
    /// </summary>
    private string? ItemsType(IReadOnlyList<Expr> items)
    {
        if (items.Count == 0 || !items.All(item => item is Literal { Kind: not LiteralKind.Null })) return null;

        var kinds = items.Cast<Literal>().Select(literal => literal.Kind).Distinct().ToList();
        if (Language == SourceLanguage.Python) return kinds.Count == 1 ? PythonName(kinds[0]) : "hashable";
        if (kinds.Count != 1) return null;

        return (Language, kinds[0]) switch
        {
            (SourceLanguage.Java, LiteralKind.Text) => "String",
            (SourceLanguage.Java, LiteralKind.Integer) => "Integer",
            (SourceLanguage.Java, LiteralKind.Character) => "Character",
            (SourceLanguage.Java, LiteralKind.Boolean) => "Boolean",
            (SourceLanguage.CSharp, LiteralKind.Text) => "string",
            (SourceLanguage.CSharp, LiteralKind.Integer) => "int",
            (SourceLanguage.CSharp, LiteralKind.Character) => "char",
            (SourceLanguage.CSharp, LiteralKind.Boolean) => "bool",
            (SourceLanguage.JavaScript, _) => "any",
            _ => null,
        };
    }

    private static string PythonName(LiteralKind kind) => kind switch
    {
        LiteralKind.Text => "str",
        LiteralKind.Integer => "int",
        LiteralKind.Real => "float",
        LiteralKind.Boolean => "bool",
        _ => "hashable",
    };
}
