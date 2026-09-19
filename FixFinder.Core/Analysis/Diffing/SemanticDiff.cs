using System.Globalization;
using FixFinder.Core.Analysis.Abstract;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Solver;
using FixFinder.Core.Analysis.Symbolic;

namespace FixFinder.Core.Analysis.Diffing;

/// <summary>What a change does to one function: the inputs whose outcome it changes, and whether that is all of them.</summary>
/// <remarks><see cref="Followed"/> is false when no path of either version could be compared, so nothing can be said.</remarks>
public sealed record FunctionDiff(string Function, IReadOnlyList<string> Differences, bool Complete, bool Followed = true);

/// <summary>
/// Semantic diffing: what a change to a program does, not how its text differs. Each function the change touches is
/// followed path by path in both versions, the same input gets the same symbol in both, and every pair of paths that the
/// same inputs can take is compared - an error in one and not the other, a different result, different output. What is
/// left is in terms of the inputs: "when `values` is empty, it stopped with ZeroDivisionError and now returns 0".
/// Only paths decided by the function's own inputs are compared; anything else makes the answer "could not compare",
/// never "the same".
/// </summary>
public static class SemanticDiff
{
    private const int MostDifferences = 3;

    private static readonly ConstraintSolver Solver = new() { MostBranches = 60, MostPivots = 300 };

    /// <param name="lineAfter">Where a line of the old version is in the new one, so inputs typed on a line still match.</param>
    public static IReadOnlyList<FunctionDiff> Compare(IrProgram before, IrProgram after, Func<int, int> lineAfter)
    {
        var diffs = new List<FunctionDiff>();
        var old = before.AllFunctions.GroupBy(f => f.FullName).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var changed in after.AllFunctions)
        {
            if (!old.TryGetValue(changed.FullName, out var original)) continue;

            var was = CfgBuilder.Build(original);
            var now = CfgBuilder.Build(changed);
            if (IrText.Of(was) == IrText.Of(now)) continue;

            diffs.Add(Compare(before, original, was, after, changed, now, lineAfter));
        }

        return diffs;
    }

    private static FunctionDiff Compare(IrProgram before, IrFunction original, ControlFlowGraph was, IrProgram after, IrFunction changed, ControlFlowGraph now,
        Func<int, int> lineAfter)
    {
        var language = before.Language;
        var first = Explore(before, original, was);
        var second = Explore(after, changed, now);
        var complete = first.Complete && second.Complete;
        var inputs = new Inputs(first.Symbols!, original, second.Symbols!, changed, lineAfter);

        var differences = new List<string>();
        var compared = 0;

        foreach (var oldEnd in first.Ends)
        {
            foreach (var newEnd in second.Ends)
            {
                if (differences.Count >= MostDifferences) break;

                var pair = inputs.Pair(oldEnd, newEnd);
                if (pair is null)
                {
                    complete = false;
                    continue;
                }

                if (pair.Impossible) continue;

                var together = Solver.Check(pair.Constraints, inputs.IsWhole);
                if (together.Verdict == Verdict.Unsatisfiable) continue;
                if (!together.IsSatisfiable)
                {
                    complete = false;
                    continue;
                }

                compared++;

                var (ways, exhaustive) = Differences(oldEnd, newEnd, inputs, lineAfter, language);
                if (!exhaustive) complete = false;

                foreach (var (differs, how) in ways)
                {
                    if (!inputs.Trusted(differs, pair.Constraints))
                    {
                        complete = false;
                        continue;
                    }

                    var asked = differs.Count == 0 ? pair.Constraints : [.. pair.Constraints, .. differs];
                    var found = differs.Count == 0 ? together : Solver.Check(asked, inputs.IsWhole);
                    if (found.Verdict == Verdict.Unknown) complete = false;
                    if (!found.IsSatisfiable) continue;

                    var relevant = Inputs.Closure(pair.Decisions.Concat(differs).SelectMany(c => c.Term.Symbols), asked);
                    if (!relevant.All(inputs.IsExact))
                    {
                        complete = false;
                        continue;
                    }

                    var text = $"{When(found.Model!, relevant, pair.Facts, inputs, language)}: {how(found.Model!)}";
                    if (!differences.Contains(text)) differences.Add(text);
                    break;
                }
            }
        }

        return new FunctionDiff(original.FullName, differences, complete && differences.Count < MostDifferences, compared > 0 || differences.Count > 0);
    }

    private static SymbolicReport Explore(IrProgram program, IrFunction function, ControlFlowGraph graph) =>
        new SymbolicExecutor(graph, program.Language, IrWalk.LocalNames(function, program.Language == SourceLanguage.Python),
            Scopes.Volatile(program, function), new Dictionary<string, IrType>())
        {
            KeepEnds = true,
            MostTime = TimeSpan.FromMilliseconds(400),
        }.Explore();

    /// <summary>Two ends that the same inputs can reach, in shared symbols.</summary>
    private sealed record Pairing(IReadOnlyList<Constraint> Constraints, IReadOnlyList<Constraint> Decisions, IReadOnlyList<string> Facts, bool Impossible = false);

    /// <summary>
    /// The inputs of the two versions under one numbering: a parameter, a parameter's length or truth, or the n-th thing
    /// typed on a line is the same input in both. Every other symbol stays its own.
    /// </summary>
    private sealed class Inputs
    {
        private readonly int[] _old;
        private readonly int[] _new;
        private readonly List<Symbol> _symbols = [];
        private readonly List<string?> _identity = [];
        private readonly List<bool> _trusted = [];
        private readonly List<bool> _exact = [];

        public Inputs(SymbolTable before, IrFunction original, SymbolTable after, IrFunction changed, Func<int, int> lineAfter)
        {
            var keyed = new Dictionary<string, int>(StringComparer.Ordinal);

            int[] Number(SymbolTable table, IrFunction function, Func<int, int> line)
            {
                var ids = new int[table.Count];
                for (var id = 0; id < table.Count; id++)
                {
                    var symbol = table[id];
                    var (key, identity) = Key(symbol, function, line);
                    var trusted = identity is not null || symbol.Origin == SymbolOrigin.Derived;
                    var exact = trusted;

                    // The same operation on the same inputs is the same value in both versions, though nothing more is known of it.
                    if (symbol.Applies is { } applies)
                    {
                        var operands = applies.Operands.Select(term => term.Renamed(operand => ids[operand])).ToList();
                        trusted = operands.SelectMany(term => term.Symbols).All(operand => _trusted[operand]);
                        exact = false;
                        key = $"apply {applies.Operation} {symbol.IsWhole} {string.Join(" | ", operands)}";
                    }

                    if (key is not null && keyed.TryGetValue(key, out var same))
                    {
                        ids[id] = same;
                        continue;
                    }

                    ids[id] = _symbols.Count;
                    _symbols.Add(symbol);
                    _identity.Add(identity);
                    _trusted.Add(trusted);
                    _exact.Add(exact);
                    if (key is not null) keyed[key] = ids[id];
                }

                return ids;
            }

            _old = Number(before, original, lineAfter);
            _new = Number(after, changed, line => line);
        }

        public int Old(int id) => _old[id];

        public int New(int id) => _new[id];

        public Symbol this[int id] => _symbols[id];

        public bool IsWhole(int id) => _symbols[id].IsWhole;

        /// <summary>What makes a symbol the same input in both versions, and which input it is about.</summary>
        private static (string? Key, string? Identity) Key(Symbol symbol, IrFunction function, Func<int, int> lineAfter)
        {
            if (symbol.TypedAt is { } line && symbol.Origin is SymbolOrigin.Input or SymbolOrigin.TextLength)
            {
                var identity = $"typed {lineAfter(line)} {symbol.Occurrence}";
                return ($"{symbol.Origin} {identity} {symbol.IsWhole}", identity);
            }

            if (symbol.Origin is SymbolOrigin.Parameter or SymbolOrigin.Length or SymbolOrigin.TextLength or SymbolOrigin.Flag &&
                symbol.Variable is { } variable && function.Parameters.Any(p => p.Name == variable))
                return ($"{symbol.Origin} parameter {variable} {symbol.IsWhole}", $"parameter {variable}");

            return (null, null);
        }

        /// <summary>A symbol stands for something both versions agree on: an input, or what follows from inputs alone.</summary>
        private bool IsTrusted(int id) => _trusted[id];

        /// <summary>
        /// A symbol whose constraints say all there is to know of it. An operation's result is not: the solver may give it
        /// a value the operation never gives, so it can show two versions agree but never that they differ.
        /// </summary>
        public bool IsExact(int id) => _exact[id];

        /// <summary>Whether what a comparison asks for is in terms of inputs only.</summary>
        public bool Trusted(IReadOnlyList<Constraint> asked, IReadOnlyList<Constraint> constraints) =>
            Closure(asked.SelectMany(c => c.Term.Symbols), constraints).All(IsTrusted);

        /// <summary>
        /// The two ends in shared symbols, or null when they cannot be compared: a path decided by something other than
        /// the inputs, or one input seen two ways that cannot be tied together.
        /// </summary>
        public Pairing? Pair(PathEnd before, PathEnd after)
        {
            if (before.Approximated || after.Approximated || before.OutsideDecided || after.OutsideDecided) return null;

            if (before.NullParameters.Intersect(after.NotNullParameters).Any() || before.NotNullParameters.Intersect(after.NullParameters).Any())
                return new Pairing([], [], [], Impossible: true);

            var constraints = before.Constraints.Select(c => Renamed(c, Old)).Concat(after.Constraints.Select(c => Renamed(c, New))).ToList();
            var decisions = before.Decisions.Select(c => Renamed(c, Old)).Concat(after.Decisions.Select(c => Renamed(c, New))).ToList();
            var nulls = before.NullParameters.Concat(after.NullParameters).Distinct().ToList();

            var used = decisions.SelectMany(c => c.Term.Symbols)
                .Concat(Terms(before.Value, Old)).Concat(Terms(after.Value, New))
                .Concat(before.Printed.SelectMany(p => Terms(p, Old))).Concat(after.Printed.SelectMany(p => Terms(p, New)));
            var relevant = Closure(used, constraints);

            if (!relevant.All(IsTrusted)) return null;

            foreach (var group in relevant.Where(id => _identity[id] is not null).GroupBy(id => _identity[id]!))
            {
                var ids = group.ToList();
                var flags = ids.Where(id => _symbols[id].Origin == SymbolOrigin.Flag).ToList();
                var others = ids.Except(flags).ToList();
                var isNull = group.Key.StartsWith("parameter ", StringComparison.Ordinal) && nulls.Contains(group.Key["parameter ".Length..]);

                // A parameter taken to be null has no length or number; a path that uses one did not see it null.
                if (isNull && others.Count > 0) return null;
                if (isNull)
                {
                    constraints.AddRange(flags.Select(flag => Constraint.Same(LinearTerm.Symbol(flag), 0)));
                    continue;
                }

                if (ids.Count == 1) continue;

                // One input seen two ways - whether it is true, and its length or value - is tied together here, where
                // the path has settled whether it is true; seen any other two ways, the solver could pick values no run can.
                if (flags.Count != 1 || others.Count != 1 || _symbols[others[0]].Origin is not (SymbolOrigin.Parameter or SymbolOrigin.Length or SymbolOrigin.TextLength))
                    return null;

                var flag = LinearTerm.Symbol(flags[0]);
                var measure = LinearTerm.Symbol(others[0]);
                var canBeTrue = Solver.Check([.. constraints, Constraint.AtLeast(flag, 1)], IsWhole);
                var canBeFalse = Solver.Check([.. constraints, Constraint.AtMost(flag, 0)], IsWhole);

                if (canBeTrue.Verdict == Verdict.Unknown || canBeFalse.Verdict == Verdict.Unknown || canBeTrue.IsSatisfiable && canBeFalse.IsSatisfiable) return null;
                if (!canBeTrue.IsSatisfiable && !canBeFalse.IsSatisfiable) return new Pairing([], [], [], Impossible: true);

                constraints.Add(!canBeTrue.IsSatisfiable ? Constraint.Same(measure, 0)
                    : _symbols[others[0]].Origin == SymbolOrigin.Parameter ? Constraint.Different(measure, 0)
                    : Constraint.AtLeast(measure, 1));
            }

            var facts = before.Facts.Concat(after.Facts).Distinct().ToList();
            return new Pairing(constraints, decisions, facts);
        }

        public static Constraint Renamed(Constraint constraint, Func<int, int> rename) => constraint with { Term = constraint.Term.Renamed(rename) };

        private static IEnumerable<int> Terms(SymbolicValue? value, Func<int, int> rename) => value switch
        {
            SymNumber number => number.Term.Symbols.Select(rename),
            SymText text => text.Length.Symbols.Select(rename),
            SymSequence sequence => sequence.Length.Symbols.Select(rename).Concat((sequence.Items ?? []).SelectMany(i => Terms(i, rename))),
            SymTruth truth => Atoms(truth.Condition).SelectMany(c => c.Term.Symbols).Select(rename),
            _ => [],
        };

        private static IEnumerable<Constraint> Atoms(Condition condition) => condition switch
        {
            Atom atom => [atom.Constraint],
            AllOf both => Atoms(both.Left).Concat(Atoms(both.Right)),
            AnyOf either => Atoms(either.Left).Concat(Atoms(either.Right)),
            Negation negation => Atoms(negation.Inner),
            _ => [],
        };

        /// <summary>The symbols given, and every symbol a constraint ties to them.</summary>
        public static HashSet<int> Closure(IEnumerable<int> start, IReadOnlyList<Constraint> constraints)
        {
            var relevant = start.ToHashSet();

            for (var grew = true; grew;)
            {
                grew = false;
                foreach (var constraint in constraints)
                {
                    var symbols = constraint.Term.Symbols.ToList();
                    if (symbols.Count < 2 || !symbols.Any(relevant.Contains) || symbols.All(relevant.Contains)) continue;

                    relevant.UnionWith(symbols);
                    grew = true;
                }
            }

            return relevant;
        }
    }

    /// <summary>
    /// The ways two ends can differ, each as what must hold for them to differ (nothing when they always do) and how to
    /// say it for particular inputs; and whether those are all the ways - false when a value cannot be compared.
    /// </summary>
    private static (IReadOnlyList<(IReadOnlyList<Constraint> Differs, Func<IReadOnlyDictionary<int, Rational>, string> How)> Ways, bool Exhaustive) Differences(
        PathEnd before, PathEnd after, Inputs inputs, Func<int, int> lineAfter, SourceLanguage language)
    {
        string Say(IReadOnlyDictionary<int, Rational> model) =>
            $"before, it {Ending(before, model, inputs.Old, language, past: true)}; now it {Ending(after, model, inputs.New, language, past: false)}";

        if (before.Kind != after.Kind || before.Kind != EndKind.Returns && Moved(before.Detail, lineAfter) != after.Detail) return ([([], Say)], true);
        if (before.Kind != EndKind.Returns) return ([], true);

        var ways = new List<(IReadOnlyList<Constraint>, Func<IReadOnlyDictionary<int, Rational>, string>)>();

        var returned = Unequal(before.Value, after.Value, inputs);
        ways.AddRange(returned.Ways.Select(way => (way, (Func<IReadOnlyDictionary<int, Rational>, string>)Say)));

        if (before.Printed.Count != after.Printed.Count) return ([.. ways, ([], Say)], returned.Exhaustive);

        var exhaustive = returned.Exhaustive;
        for (var i = 0; i < before.Printed.Count; i++)
        {
            var printed = Unequal(before.Printed[i], after.Printed[i], inputs);
            ways.AddRange(printed.Ways.Select(way => (way, (Func<IReadOnlyDictionary<int, Rational>, string>)Say)));
            exhaustive &= printed.Exhaustive;
        }

        return (ways, exhaustive);
    }

    /// <summary>A failure's check and line, with the line as it is in the changed version.</summary>
    private static string? Moved(string? detail, Func<int, int> lineAfter) =>
        detail?.Split('@') is [var check, var line] && int.TryParse(line, out var number) ? $"{check}@{lineAfter(number)}" : detail;

    /// <summary>
    /// The ways two values can differ, each as the constraints under which it does (none: always), and whether there are
    /// no others - so that "no way holds" means "equal".
    /// </summary>
    private static (IReadOnlyList<IReadOnlyList<Constraint>> Ways, bool Exhaustive) Unequal(SymbolicValue? before, SymbolicValue? after, Inputs inputs)
    {
        LinearTerm Old(LinearTerm term) => term.Renamed(inputs.Old);
        LinearTerm New(LinearTerm term) => term.Renamed(inputs.New);

        switch (before, after)
        {
            case (SymNumber a, SymNumber b):
                return ([[Constraint.Different(Old(a.Term), New(b.Term))]], true);

            case (SymNull or null, SymNull or null):
                return ([], true);

            case (SymNull or null, SymNumber or SymText or SymSequence) or (SymNumber or SymText or SymSequence, SymNull or null):
            case (SymNumber, SymText or SymSequence) or (SymText, SymNumber or SymSequence) or (SymSequence, SymNumber or SymText):
                return ([[]], true);

            case (SymText { Known: { } x }, SymText { Known: { } y }):
                return x == y ? ([], true) : ([[]], true);

            case (SymText a, SymText b):
                return ([[Constraint.Different(Old(a.Length), New(b.Length))]], false);

            case (SymSequence { Items: { } x } a, SymSequence { Items: { } y } b) when a.Kind == b.Kind:
                if (x.Count != y.Count) return ([[]], true);

                var ways = new List<IReadOnlyList<Constraint>>();
                var exhaustive = true;
                foreach (var (first, second) in x.Zip(y))
                {
                    var items = Unequal(first, second, inputs);
                    ways.AddRange(items.Ways);
                    exhaustive &= items.Exhaustive;
                }

                return (ways, exhaustive);

            case (SymSequence a, SymSequence b):
                return ([[Constraint.Different(Old(a.Length), New(b.Length))]], false);

            default:
                return ([], false);
        }
    }

    private static string Ending(PathEnd end, IReadOnlyDictionary<int, Rational> model, Func<int, int> rename, SourceLanguage language, bool past) => end.Kind switch
    {
        EndKind.Fails when end.Detail?.Split('@') is [var check, var line] => $"{(past ? "stopped" : "stops")} with {Failure(check, language)} on line {line}",
        EndKind.Fails => past ? "stopped with an error" : "stops with an error",
        EndKind.Raises => $"{(past ? "raised" : "raises")} {end.Detail}",
        _ when end.Value is not null => $"{(past ? "returned" : "returns")} {Value(end.Value, model, rename, language)}",
        _ when end.Printed.Count > 0 => $"{(past ? "printed" : "prints")} {Shown(end.Printed, model, rename, language)}",
        _ => past ? "ran to the end" : "runs to the end",
    };

    private static string Failure(string check, SourceLanguage language) => (check, language) switch
    {
        ("analysis-division-by-zero", SourceLanguage.Python) => "ZeroDivisionError",
        ("analysis-division-by-zero", SourceLanguage.CSharp) => "a DivideByZeroException",
        ("analysis-division-by-zero", _) => "an ArithmeticException",
        ("analysis-index-out-of-range", SourceLanguage.Python) or ("analysis-empty-collection", SourceLanguage.Python) => "IndexError",
        ("analysis-index-out-of-range", _) => "an index out of range exception",
        ("analysis-null-used", SourceLanguage.Python) => "an error from using None",
        ("analysis-null-used", SourceLanguage.CSharp) => "a NullReferenceException",
        ("analysis-null-used", _) => "a NullPointerException",
        _ => "an error",
    };

    private static string Value(SymbolicValue value, IReadOnlyDictionary<int, Rational> model, Func<int, int> rename, SourceLanguage language) => value switch
    {
        SymNumber number => number.Term.Renamed(rename).Evaluate(model).ToString(),
        SymNull => language == SourceLanguage.Python ? "None" : "null",
        SymText { Known: { } text } => $"\"{text}\"",
        SymText text => $"text of {text.Length.Renamed(rename).Evaluate(model)} characters",
        SymSequence { Kind: CollectionKind.Tuple, Items: { } items } => string.Join(" ", items.Select(i => Value(i, model, rename, language))),
        SymSequence sequence => $"{sequence.Length.Renamed(rename).Evaluate(model)} items",
        SymTruth truth => Truth(truth.Condition, model, rename) is { } holds
            ? language == SourceLanguage.Python ? holds ? "True" : "False" : holds ? "true" : "false"
            : "a truth value",
        _ => "a value",
    };

    private static bool? Truth(Condition condition, IReadOnlyDictionary<int, Rational> model, Func<int, int> rename) => condition switch
    {
        Known known => known.Value,
        Atom atom => Inputs.Renamed(atom.Constraint, rename).HoldsFor(model),
        AllOf both => Truth(both.Left, model, rename) is { } l && Truth(both.Right, model, rename) is { } r ? l && r : null,
        AnyOf either => Truth(either.Left, model, rename) is { } l && Truth(either.Right, model, rename) is { } r ? l || r : null,
        Negation negation => Truth(negation.Inner, model, rename) is { } inner ? !inner : null,
        _ => null,
    };

    private static string Shown(IReadOnlyList<SymbolicValue> printed, IReadOnlyDictionary<int, Rational> model, Func<int, int> rename, SourceLanguage language) =>
        printed.Count == 0 ? "nothing" : string.Join(", then ", printed.Select(p => Value(p, model, rename, language)));

    /// <summary>The inputs of a model in the program's terms: "when `values` is empty", "when the number typed at line 1 is 0".</summary>
    private static string When(IReadOnlyDictionary<int, Rational> model, IEnumerable<int> relevant, IReadOnlyList<string> pathFacts, Inputs inputs,
        SourceLanguage language)
    {
        var facts = new List<string>(pathFacts);
        var ids = relevant.Order().ToList();

        foreach (var id in ids)
        {
            var symbol = inputs[id];
            var value = model.GetValueOrDefault(id, Rational.Zero);

            // The number read from a typed text says more than how long the text was.
            if (symbol.Origin == SymbolOrigin.TextLength && symbol.TypedAt is { } typed &&
                ids.Any(other => inputs[other] is { Origin: SymbolOrigin.Input } number && number.TypedAt == typed && number.Occurrence == symbol.Occurrence))
                continue;

            var subject = symbol.Occurrence > 0 ? $"{symbol.Describes} the {Ordinal(symbol.Occurrence + 1)} time" : symbol.Describes;
            var yes = language == SourceLanguage.Python ? "True" : "true";
            var no = language == SourceLanguage.Python ? "False" : "false";

            var fact = symbol.Origin switch
            {
                SymbolOrigin.Parameter or SymbolOrigin.Input => $"{subject} is {value}",
                SymbolOrigin.Length => value.IsZero ? $"{subject} is empty" : $"{subject} has {value} {(value == Rational.One ? "item" : "items")}",
                SymbolOrigin.TextLength => value.IsZero ? $"{subject} is empty" : $"{subject} has {value} {(value == Rational.One ? "character" : "characters")}",
                SymbolOrigin.Flag => $"{subject} is {(value.IsZero ? no : yes)}",
                _ => null,
            };

            if (fact is not null && !facts.Any(f => f.StartsWith(subject + " ", StringComparison.Ordinal))) facts.Add(fact);
        }

        return facts.Count == 0 ? "Every time" : "When " + string.Join(" and ", facts.Take(4));
    }

    private static string Ordinal(int n) => n switch
    {
        2 => "second",
        3 => "third",
        4 => "fourth",
        5 => "fifth",
        _ => $"{n}th",
    };

    /// <summary>What a list of function diffs says about a change, in sentences for the report.</summary>
    public static IReadOnlyList<string> Describe(IReadOnlyList<FunctionDiff> diffs)
    {
        var lines = new List<string>();

        foreach (var diff in diffs.Where(d => d.Followed))
        {
            var name = diff.Function == IrFunction.ModuleBody ? "the program" : $"`{diff.Function}`";

            if (diff.Differences.Count == 0)
            {
                lines.Add(diff.Complete
                    ? $"{char.ToUpper(name[0], CultureInfo.InvariantCulture)}{name[1..]} behaves exactly as before for every input - only the code changes."
                    : $"FixFinder found no input for which {name} behaves differently, but could not compare every input.");
                continue;
            }

            lines.AddRange(diff.Differences.Select(d => d.Replace("before, it", $"before, {name}", StringComparison.Ordinal)));
            lines.Add(diff.Complete ? "For every other input it behaves exactly as before." : "FixFinder could not compare the two versions for every other input.");
        }

        return lines;
    }
}
