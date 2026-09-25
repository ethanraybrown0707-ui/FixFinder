using FixFinder.Core.Analysis.Abstract;
using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// A collection changed while a for-each loop is walking over it, found by what the code means rather than how it is
/// written: through another name for the same collection, or inside a function the loop calls. Java can throw
/// ConcurrentModificationException, C# throws InvalidOperationException, a Python dictionary or set raises RuntimeError,
/// and a Python list quietly skips or repeats items.
/// </summary>
/// <remarks>
/// <para>
/// The loop's collection is known by the hidden copy of it the loop walks, which the abstract state keeps as another name
/// for the same object - so any name the state says holds that object is the loop's collection, whatever it is called. A
/// function the loop calls is known by its <see cref="Effects"/> summary.
/// </para>
/// <para>
/// A change through the loop's own name is left to the pattern check, which says the same and offers the fix. Only a
/// list certainly grows when added to - adding what a set or dictionary already holds changes nothing - and a change
/// followed at once by break or return leaves the loop before it can go wrong.
/// </para>
/// </remarks>
internal sealed class ChangedWhileLooping(
    ControlFlowGraph graph, Evaluator evaluator, IReadOnlyDictionary<SourceSpan, AbstractState> statesBefore, CallTargets targets, Effects effects,
    Func<Expr, string> quote, Action<AnalysisFinding> report)
{
    public const string Rule = "analysis-changed-while-looping";

    private readonly HashSet<int> _reportedLines = [];

    private SourceLanguage Language => evaluator.Language;

    private bool IsPython => Language == SourceLanguage.Python;

    public void Check()
    {
        if (Language is not (SourceLanguage.Python or SourceLanguage.Java or SourceLanguage.CSharp)) return;

        foreach (var loop in IrWalk.Statements(graph.Function.Body).OfType<ForEach>())
        {
            if (Walked(loop) is not { } holder || !statesBefore.TryGetValue(loop.Span, out var entry) || !entry.IsReachable) continue;
            if (WalkedKind(loop.Items, entry) is not { } kind || ToleratesChanges(loop.Items)) continue;

            Inside(loop, loop.Body, holder, kind);
        }
    }

    /// <summary>
    /// The variable the loop walks: its own name when it walks a variable - unless the loop gives that name another
    /// collection, after which the name no longer means what is being walked - or else the hidden copy the loop makes.
    /// </summary>
    private string? Walked(ForEach loop)
    {
        if (loop.Items is not Name { Identifier: var walked }) return Holder(loop);

        var reassigned = IrWalk.Statements(loop.Body).Any(s => s switch
        {
            Assign { Target: Name { Identifier: var assigned }, Compound: null } => assigned == walked,
            Declare { Variable: var declared } => declared == walked,
            _ => false,
        });

        return reassigned ? null : walked;
    }

    /// <summary>The hidden variable a loop over an expression walks: the copy of its value made where the loop starts.</summary>
    private string? Holder(ForEach loop) => graph.Blocks
        .SelectMany(b => b.Instructions)
        .OfType<AssignInstruction>()
        .Where(a => a.Span == loop.Span && a.Target is Name { Identifier: var name } && name.StartsWith("$items", StringComparison.Ordinal))
        .Select(a => ((Name)a.Target).Identifier)
        .FirstOrDefault();

    private static ValueKind? KindOf(AbstractValue walked) =>
        walked.IsOnly(ValueKind.List) ? ValueKind.List
        : walked.IsOnly(ValueKind.Dictionary) ? ValueKind.Dictionary
        : walked.IsOnly(ValueKind.Set) ? ValueKind.Set
        : null;

    /// <summary>
    /// Whether the loop walks a list, a dictionary or a set: what the abstract state says, or - for a field, which the state
    /// does not follow across calls - the type the field is declared with, or what Python code puts in it.
    /// </summary>
    private ValueKind? WalkedKind(Expr items, AbstractState entry)
    {
        if (KindOf(evaluator.Evaluate(items, entry)) is { } known) return known;
        if (FieldOf(items) is not { } field || targets.ClassOf(graph.Function) is not { } owner) return null;
        if (effects.Program.Classes.FirstOrDefault(c => c.Name == owner) is not { } type) return null;

        if (type.Fields.FirstOrDefault(f => f.Name == field) is { Type.IsUnknown: false } declared) return KindOfType(declared.Type.Name);

        // Python gives an object its attributes in its methods: self.items = [] makes items a list - if every assignment agrees.
        var made = type.Methods.SelectMany(m => IrWalk.Statements(m.Body))
            .OfType<Assign>()
            .Where(a => a.Target is Member { Target: Name { Identifier: "self" or "this" }, MemberName: var assigned } && assigned == field)
            .Select(a => KindOfValue(a.Value))
            .Distinct()
            .ToList();

        return made is [{ } only] ? only : null;
    }

    /// <summary>The field an expression names on the loop's own object: self.items, this.items, or a bare field name in Java and C#.</summary>
    private string? FieldOf(Expr expression) => expression switch
    {
        Member { Target: Name { Identifier: "self" or "this" }, MemberName: var named } => named,
        Name { Identifier: var named } when !IsPython && !evaluator.DeclaredTypes.ContainsKey(named) && evaluator.Locals?.Contains(named) == false => named,
        _ => null,
    };

    private static ValueKind? KindOfType(string type) => type switch
    {
        "List" or "ArrayList" or "LinkedList" or "Vector" or "Stack" or "IList" => ValueKind.List,
        "Map" or "HashMap" or "TreeMap" or "LinkedHashMap" or "Hashtable" or "Dictionary" or "SortedDictionary" or "IDictionary" => ValueKind.Dictionary,
        "Set" or "HashSet" or "TreeSet" or "LinkedHashSet" or "SortedSet" or "ISet" => ValueKind.Set,
        _ => null,
    };

    private static ValueKind? KindOfValue(Expr value) => value switch
    {
        CollectionLiteral { Kind: CollectionKind.List } => ValueKind.List,
        CollectionLiteral { Kind: CollectionKind.Dictionary } => ValueKind.Dictionary,
        CollectionLiteral { Kind: CollectionKind.Set } => ValueKind.Set,
        Call { Callee: Name { Identifier: "list" } } => ValueKind.List,
        Call { Callee: Name { Identifier: "dict" } } => ValueKind.Dictionary,
        Call { Callee: Name { Identifier: "set" } } => ValueKind.Set,
        NewObject made => KindOfType(made.Type.Name),
        _ => null,
    };

    /// <summary>Collections made to be changed while they are walked: Java's CopyOnWrite and concurrent ones, C#'s concurrent ones.</summary>
    private bool ToleratesChanges(Expr items) =>
        items is Name { Identifier: var name } && evaluator.DeclaredTypes.TryGetValue(name, out var type) &&
        (type.Name.StartsWith("CopyOnWrite", StringComparison.Ordinal) || type.Name.StartsWith("Concurrent", StringComparison.Ordinal));

    private void Inside(ForEach loop, IReadOnlyList<Stmt> block, string holder, ValueKind kind)
    {
        for (var index = 0; index < block.Count; index++)
        {
            var statement = block[index];
            var leavesAtOnce = index + 1 < block.Count && block[index + 1] is Break or Return;

            if (!leavesAtOnce) Examine(loop, statement, holder, kind);

            foreach (var inner in Blocks(statement)) Inside(loop, inner, holder, kind);
        }
    }

    private static IEnumerable<IReadOnlyList<Stmt>> Blocks(Stmt statement) => statement switch
    {
        If branch => [branch.Then, branch.Else],
        While loop => [loop.Body, loop.Else],
        For loop => [loop.Setup, loop.Body, loop.Step],
        ForEach loop => [loop.Body, loop.Else],
        Try attempt => [attempt.Body, .. attempt.Handlers.Select(h => h.Body), attempt.Else, attempt.Finally],
        Switch choice => choice.Cases.Select(c => c.Body),
        Using used => [used.Body],
        Labeled labeled => [labeled.Body],
        _ => [],
    };

    /// <summary>The calls one statement makes that change the loop's collection, through another name or inside a function.</summary>
    private void Examine(ForEach loop, Stmt statement, string holder, ValueKind kind)
    {
        if (!statesBefore.TryGetValue(statement.Span, out var state) || !state.IsReachable) return;

        var locals = IrWalk.LocalNames(graph.Function, assigningDeclares: IsPython);

        foreach (var call in IrWalk.Expressions(statement).SelectMany(Effects.CallsIn))
        {
            if (call.Callee is Member { Target: var changed, MemberName: var method } && Changes(method, kind) &&
                ThroughAnotherName(changed, loop, holder, state) is { } alias)
            {
                Report(call, loop, kind, method, via: null, alias);
                continue;
            }

            if (targets.Resolve(call, graph.Function, locals) is not { } target) continue;

            foreach (var (reached, change) in effects.Of(target.Function))
            {
                if (!Changes(change.Method, kind) || InCaller(reached, call, target) is not { } passed) continue;
                if (!Denotes(passed, loop, holder, state)) continue;

                Report(call, loop, kind, change.Method, change, NameOf(passed) is { } name && name != NameOf(loop.Items) ? name : null);
                break;
            }
        }
    }

    /// <summary>Whether a method changes a collection of this kind for certain: anything that adds to or removes from a list, only removing from the others.</summary>
    private static bool Changes(string method, ValueKind kind) =>
        Effects.Removing.Contains(method) || kind == ValueKind.List && Effects.Adding.Contains(method);

    /// <summary>
    /// The name an expression changes the loop's collection through, when it is not the loop's own name for it - which
    /// the pattern check already reports. Null when it is some other collection.
    /// </summary>
    private string? ThroughAnotherName(Expr changed, ForEach loop, string holder, AbstractState state)
    {
        if (!Denotes(changed, loop, holder, state)) return null;
        return NameOf(changed) is { } name && name != NameOf(loop.Items) ? name : null;
    }

    /// <summary>
    /// Whether an expression holds the collection the loop walks: a name the state says shares its object, or the same
    /// field of the same object - which the state forgets across calls, but which is the loop's collection all the same.
    /// </summary>
    private bool Denotes(Expr expression, ForEach loop, string holder, AbstractState state)
    {
        if (NameOf(expression) is { } name && (name == holder || state.AliasesOf(name).Contains(holder))) return true;
        if (FieldOf(expression) is { } field && field == FieldOf(loop.Items)) return true;
        return expression is Member && loop.Items is Member && SamePath(expression, loop.Items);
    }

    /// <summary>The variable an expression is, in the terms the abstract state uses: this.items in Java and C# is the field items.</summary>
    private string? NameOf(Expr expression) => expression switch
    {
        Name { Identifier: var name } => name,
        Member { Target: Name { Identifier: "this" }, MemberName: var field } when !IsPython => field,
        _ => null,
    };

    private static bool SamePath(Expr first, Expr second) => (first, second) switch
    {
        (Name a, Name b) => a.Identifier == b.Identifier,
        (Member a, Member b) => a.MemberName == b.MemberName && SamePath(a.Target, b.Target),
        _ => false,
    };

    /// <summary>What a change a function makes is, at the call: the argument it was given, the caller's own field, a module variable.</summary>
    private Expr? InCaller(Effects.Reached reached, Call call, CallTarget target) => reached.Kind switch
    {
        Effects.Reach.Parameter => Effects.ArgumentFor(call, target, reached.Index),
        Effects.Reach.Field when effects.OnSameObject(call) => new Member(call.Span, new Name(call.Span, IsPython ? "self" : "this"), reached.Name),
        Effects.Reach.Module => reached.Name.Split('.') is [var type, var field] ? new Member(call.Span, new Name(call.Span, type), field) : new Name(call.Span, reached.Name),
        _ => null,
    };

    private void Report(Call call, ForEach loop, ValueKind kind, string method, Effects.Change? via, string? alias)
    {
        if (!_reportedLines.Add(call.Span.Line)) return;

        var collection = quote(loop.Items);
        var word = kind switch { ValueKind.Dictionary => "dictionary", ValueKind.Set => "set", _ => "list" };
        var adds = Effects.Adding.Contains(method);
        var shared = alias is not null ? $" (`{alias}` is the same {word} as `{collection}`)" : "";

        var what = via is null
            ? $"`{quote(call)}` {(adds ? "adds to" : "removes from")} `{collection}`{shared}"
            : $"`{quote(call)}` {(adds ? "adds to" : "removes from")} `{collection}` at line {via.At.Line}{shared}";

        var outcome = Language switch
        {
            SourceLanguage.Java => "while the for-each loop is walking over it, so the loop's next step can throw ConcurrentModificationException",
            SourceLanguage.CSharp => "while the foreach loop is walking over it, so the loop's next step throws InvalidOperationException",
            _ when kind == ValueKind.Dictionary => "while the for loop is walking over it, so the loop stops with RuntimeError: dictionary changed size during iteration",
            _ when kind == ValueKind.Set => "while the for loop is walking over it, so the loop stops with RuntimeError: Set changed size during iteration",
            _ when adds => "while the for loop is walking over it, so the loop keeps finding new items and may never finish",
            _ => "while the for loop is walking over it, so the loop skips some of the items",
        };

        report(new AnalysisFinding(Rule, call.Span, $"{what} {outcome}", Severity.Error, Confidence.Likely, FindingKind.Logic, AbstractChecks.FoundBy));
    }
}
