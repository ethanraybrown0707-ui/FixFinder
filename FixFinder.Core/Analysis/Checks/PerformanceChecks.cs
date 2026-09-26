using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Work a program does more times than it needs to.
/// </summary>
/// <remarks>
/// Held to a high bar on purpose. A warning that a program might be slow is worth nothing unless the reader can see
/// why, so only patterns whose cost grows with the data are reported - searching a whole list once per item, and the
/// like - and never anything whose benefit is a matter of taste or of a compiler's mood. A slow program that is right
/// is still right, so none of this is an error: what is reported is a suggestion, and it says what was noticed rather
/// than promising a speed-up nobody has measured.
/// <para>
/// A search is only said to go through a collection from the start where the code shows that it does. A set or a
/// dictionary goes straight to what it is asked for, so searching one is never reported; where FixFinder cannot see
/// which kind of collection is searched - a parameter with no type, say - the finding says so and offers no change.
/// C and C++ are left out: FixFinder's reader does not follow C++'s template types, and a member count or find there
/// belongs to a set or a map as often as to a string. Go searches with functions rather than methods, and is left out
/// too.
/// </para>
/// </remarks>
public static class PerformanceChecks
{
    public const string FoundBy = "looking at how the work grows with the data";

    public const string RepeatedSearchRule = "analysis-repeated-search";

    /// <summary>The methods each language's own lists, arrays and strings search with, one item at a time from the start.</summary>
    private static readonly Dictionary<SourceLanguage, HashSet<string>> SearchMethods = new()
    {
        [SourceLanguage.Python] = new(StringComparer.Ordinal) { "index", "count", "find" },
        [SourceLanguage.Java] = new(StringComparer.Ordinal) { "contains", "indexOf", "lastIndexOf" },
        [SourceLanguage.CSharp] = new(StringComparer.Ordinal) { "Contains", "IndexOf", "LastIndexOf", "Find" },
        [SourceLanguage.JavaScript] = new(StringComparer.Ordinal) { "includes", "indexOf", "lastIndexOf", "find" },
    };

    /// <summary>Methods that change the collection they are called on, in any of the languages.</summary>
    internal static readonly HashSet<string> Changing = new(StringComparer.Ordinal)
    {
        "append", "extend", "insert", "remove", "pop", "clear", "sort", "reverse", "add", "discard", "update", "popitem", "setdefault",
        "Add", "AddRange", "Insert", "InsertRange", "Remove", "RemoveAt", "RemoveAll", "RemoveRange", "Clear", "Sort", "Reverse",
        "addAll", "removeAll", "retainAll", "removeIf", "set", "put", "push", "unshift", "shift", "splice", "fill", "copyWithin", "replaceAll",
    };

    /// <summary>A search of a collection the code names: the whole expression, the name, and the method - none for Python's in.</summary>
    internal sealed record Search(Expr Whole, Name Collection, string? Method);

    public static IReadOnlyList<AnalysisFinding> Run(IrProgram program, SourceText source)
    {
        if (!SearchMethods.TryGetValue(program.Language, out var methods)) return [];

        var findings = new List<AnalysisFinding>();

        foreach (var function in program.AllFunctions)
        {
            var evidence = new CollectionEvidence(program, function);

            // Outer loops come first, so a search inside two loops is reported against the outer one when neither changes it.
            foreach (var loop in IrWalk.Statements(function.Body).Where(s => s is ForEach or For or While))
                Inspect(program, function, loop, evidence, methods, source, findings);
        }

        return findings
            .GroupBy(finding => (finding.CheckId, finding.Span.File, finding.Span.Line, finding.Span.Column))
            .Select(group => group.First())
            .ToList();
    }

    internal static IReadOnlyList<Stmt> BodyOf(Stmt loop) => loop switch
    {
        ForEach each => each.Body,
        For counted => counted.Body,
        While repeated => repeated.Body,
        _ => [],
    };

    /// <summary>Every search in a loop's body, however deeply nested: a search inside an if is still a search on every pass.</summary>
    internal static IEnumerable<Search> SearchesIn(IReadOnlyList<Stmt> body, SourceLanguage language) =>
        IrWalk.Statements(body).SelectMany(IrWalk.Expressions).SelectMany(CollectionEvidence.Within)
            .Select(expression => Searched(expression, language)).OfType<Search>();

    /// <summary>Looks through one loop's body for a search of a collection that the loop itself does not change.</summary>
    private static void Inspect(
        IrProgram program, IrFunction function, Stmt loop, CollectionEvidence evidence, HashSet<string> methods, SourceText source,
        List<AnalysisFinding> findings)
    {
        var body = BodyOf(loop);
        var changed = ChangedIn(body);

        foreach (var search in SearchesIn(body, program.Language))
        {
            if (search.Method is { } method && !methods.Contains(method)) continue;

            // A collection the loop itself builds or changes is a different collection each pass, and turning it into a
            // set would change what the program does rather than how long it takes.
            if (changed.Contains(search.Collection.Identifier)) continue;

            var what = evidence.Of(search.Collection.Identifier);
            if (what.Lookup == Lookup.Direct) continue;

            findings.Add(new AnalysisFinding(
                RepeatedSearchRule,
                search.Whole.Span,
                Message(search, what, loop, source, program.Language),
                Severity.Suggestion,
                what.Lookup == Lookup.FromTheStart ? Confidence.Likely : Confidence.Possible,
                FindingKind.Performance,
                FoundBy)
            {
                Function = function.FullName,
                Fix = what.Lookup == Lookup.FromTheStart ? SetBeforeTheLoop.For(program, function, loop, search.Collection.Identifier, what, source) : null,
            });
        }
    }

    private static string Message(Search search, Evidence what, Stmt loop, SourceText source, SourceLanguage language)
    {
        var name = search.Collection.Identifier;
        var code = source.Of(search.Whole.Span);
        var doing = code.Length is > 0 and <= 80 ? $"`{code}`" : $"the search of `{name}`";

        if (what.Lookup == Lookup.FromTheStart)
        {
            return $"`{name}` is a {what.Kind ?? "collection"}, so {doing} looks through it from the start on every pass of the loop " +
                   $"that begins on line {loop.Span.Line} - the work grows with the number of passes times the length of `{name}`";
        }

        var (searched, direct) = language switch
        {
            SourceLanguage.Python => ("a list, a tuple or a string", "a set or a dictionary"),
            SourceLanguage.Java => ("a List or a String", "a Set"),
            SourceLanguage.CSharp => ("a List, an array or a string", "a HashSet"),
            _ => ("an array or a string", "a Set"),
        };

        return $"If `{name}` is {searched}, {doing} looks through it from the start on every pass of the loop that begins on line " +
               $"{loop.Span.Line}, so the work grows with both. FixFinder cannot see what `{name}` is here; if it is {direct}, " +
               "the search goes straight to the item and nothing needs to change";
    }

    /// <summary>The collection a search looks through, when the expression is a search of one by name.</summary>
    private static Search? Searched(Expr expression, SourceLanguage language) => expression switch
    {
        // Python's `x in items`. JavaScript's in asks whether an object has a property, which is no search at all.
        Binary { Operator: BinaryOperator.In or BinaryOperator.NotIn, Right: Name collection } when language == SourceLanguage.Python =>
            new Search(expression, collection, null),

        // items.contains(x), items.IndexOf(x), items.includes(x) and the rest.
        Call { Callee: Member { Target: Name collection, MemberName: var method }, Arguments.Count: 1 } =>
            new Search(expression, collection, method),

        _ => null,
    };

    /// <summary>
    /// Names the loop's own body assigns to or changes: given a new value, changed through one of their items, or
    /// added to and taken from. None of them is the same collection on every pass.
    /// </summary>
    private static HashSet<string> ChangedIn(IReadOnlyList<Stmt> body)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var statement in IrWalk.Statements(body))
        {
            switch (statement)
            {
                case Assign assign:
                    if (Root(assign.Target) is { } assigned) names.Add(assigned);
                    break;

                case Declare declared:
                    names.Add(declared.Variable);
                    break;

                case ForEach loop:
                    names.UnionWith(IrWalk.Names(loop.Target));
                    break;

                case OpaqueStmt opaque:
                    names.UnionWith(opaque.MayAssign);
                    names.UnionWith(opaque.Parts.Select(Root).OfType<string>());
                    break;
            }

            foreach (var expression in IrWalk.Expressions(statement).SelectMany(CollectionEvidence.Within))
            {
                switch (expression)
                {
                    case AssignValue assigned when Root(assigned.Target) is { } target:
                        names.Add(target);
                        break;

                    case Call { Callee: Member { Target: Name changed, MemberName: var method } } when Changing.Contains(method):
                        names.Add(changed.Identifier);
                        break;
                }
            }
        }

        return names;
    }

    /// <summary>The variable an assignment changes: the name itself, or the one whose item, field or slice it sets.</summary>
    internal static string? Root(Expr target) => target switch
    {
        Name name => name.Identifier,
        ElementAccess element => Root(element.Target),
        Member member => Root(member.Target),
        Slice slice => Root(slice.Target),
        _ => null,
    };
}
