using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Solver;
using FixFinder.Core.Checking;
using SolverRelation = FixFinder.Core.Analysis.Solver.Relation;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Loops reasoned about with relations between variables rather than each variable on its own, and settled with the
/// constraint solver.
/// <list type="bullet">
/// <item>A counted loop's counter is tied to its limit: inside for i in range(len(a)), i ≤ len(a) - 1 - a relation
/// between two quantities, not a range for one. On the last time round a[i + 1] is therefore a[len(a)], past the end
/// whatever the list holds; a Java loop counting down from a.length reads a[a.length] on its first.</item>
/// <item>A while loop's candidate invariants - its condition's relation, such as lo ≤ hi, and the bounds its starting
/// values give - are kept only when the solver shows them inductive: true as the loop is entered, and kept by every way
/// through its body. Within them, a state where one time round changes nothing is a loop that never ends.</item>
/// </list>
/// </summary>
/// <remarks>
/// Only what the code plainly shows is used: linear arithmetic over whole numbers, lengths nothing in the loop changes,
/// and loop bodies that call nothing able to change things out of sight. Anything else, and the loop is left alone.
/// </remarks>
internal sealed class LoopReasoning(IrFunction function, SourceLanguage language, Action<AnalysisFinding> report)
{
    public const string StuckRule = "analysis-loop-can-get-stuck";

    /// <summary>How many ways through a loop body are followed; a body with more is left alone.</summary>
    private const int MostWays = 64;

    /// <summary>Calls that neither change the loop's variables nor the collections they measure.</summary>
    private static readonly HashSet<string> HarmlessCalls = new(StringComparer.Ordinal)
    {
        "print", "len", "str", "repr", "int", "float", "abs", "min", "max", "sum", "sorted", "list", "tuple", "format", "round",
        "println", "WriteLine", "Write", "printf", "puts", "size", "length", "get", "charAt",
    };

    private readonly ConstraintSolver _solver = new();

    private bool IsPython => language == SourceLanguage.Python;

    public void Check()
    {
        foreach (var statement in IrWalk.Statements(function.Body))
        {
            if (Counted(statement) is { } counted) OutsideTheCollection(counted);
            if (statement is While { TestsFirst: true } loop) Stuck(loop);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Linear terms over the loop's quantities.

    /// <summary>Numbers every quantity reasoned about - a variable, a collection's length, an unknown - once, with how the code writes it.</summary>
    private sealed class Quantities
    {
        private readonly Dictionary<string, int> _numbers = new(StringComparer.Ordinal);
        private readonly List<string> _shown = [];

        /// <summary>What is known of the quantities whatever the code does: a length is never negative, a quotient is what division means.</summary>
        public List<Constraint> Facts { get; } = [];

        public LinearTerm Of(string key, string? shown = null)
        {
            if (!_numbers.TryGetValue(key, out var number))
            {
                _numbers[key] = number = _shown.Count;
                _shown.Add(shown ?? key);
                if (key.StartsWith("length of ", StringComparison.Ordinal)) Facts.Add(Constraint.AtLeast(LinearTerm.Symbol(number), LinearTerm.Of(0)));
            }

            return LinearTerm.Symbol(number);
        }

        public LinearTerm Fresh(string what) => Of($"{what} #{_shown.Count}", what);

        public string ShownAs(int number) => _shown[number];

        public int? NumberOf(string key) => _numbers.TryGetValue(key, out var number) ? number : null;
    }

    /// <summary>
    /// An expression as a linear term, variables looked up with <paramref name="values"/>; null when it is not linear
    /// arithmetic over whole numbers. Floor division by a constant c brings in a fresh whole number q with
    /// c·q ≤ x ≤ c·q + c - 1, which is exactly what it means.
    /// </summary>
    private LinearTerm? Linear(Expr expression, Func<string, LinearTerm?> values, Quantities quantities)
    {
        switch (expression)
        {
            case Literal { Kind: LiteralKind.Integer, Value: var number }:
                return LinearTerm.Of(Convert.ToInt64(number));

            case Name { Identifier: var name }:
                return values(name);

            case Unary { Operator: UnaryOperator.Negate, Operand: var negated }:
                return Linear(negated, values, quantities) is { } inner ? -inner : null;

            case Binary { Operator: BinaryOperator.Add } sum:
                return Linear(sum.Left, values, quantities) is { } left && Linear(sum.Right, values, quantities) is { } right ? left + right : null;

            case Binary { Operator: BinaryOperator.Subtract } difference:
                return Linear(difference.Left, values, quantities) is { } from && Linear(difference.Right, values, quantities) is { } taken ? from - taken : null;

            case Binary { Operator: BinaryOperator.Multiply } product:
                var (a, b) = (Linear(product.Left, values, quantities), Linear(product.Right, values, quantities));
                if (a is null || b is null) return null;
                if (a.IsConstant) return b * a.Constant;
                return b.IsConstant ? a * b.Constant : null;

            case Binary { Operator: BinaryOperator.FloorDivide or BinaryOperator.Divide, Right: Literal { Kind: LiteralKind.Integer, Value: var divisor } } halved
                when Convert.ToInt64(divisor) > 0 && (halved.Operator == BinaryOperator.FloorDivide || !IsPython):
                if (Linear(halved.Left, values, quantities) is not { } dividend) return null;
                var by = new Rational(Convert.ToInt64(divisor), 1);
                var quotient = quantities.Fresh($"({IrText.Of(halved)})");
                quantities.Facts.Add(Constraint.AtMost(quotient * by, dividend));
                quantities.Facts.Add(Constraint.AtMost(dividend, quotient * by + LinearTerm.Of(by - Rational.One)));
                // Java, C and C# round towards zero, which is flooring only for what is not negative.
                if (!IsPython) quantities.Facts.Add(Constraint.AtLeast(dividend, LinearTerm.Of(0)));
                return quotient;

            default:
                return LengthOf(expression) is { } measured ? quantities.Of($"length of {measured}", IrText.Of(expression)) : null;
        }
    }

    /// <summary>The collection an expression measures: len(a), a.length, a.Length, a.size(), a.Count, a.length().</summary>
    private static string? LengthOf(Expr expression) => expression switch
    {
        Call { Callee: Name { Identifier: "len" }, Arguments: [{ Value: Name { Identifier: var measured } }] } => measured,
        Member { Target: Name { Identifier: var measured }, MemberName: "length" or "Length" or "Count" } => measured,
        Call { Callee: Member { Target: Name { Identifier: var measured }, MemberName: "size" or "length" }, Arguments.Count: 0 } => measured,
        _ => null,
    };

    /// <summary>
    /// Settles constraints over whole numbers. A strict a &lt; b is first tightened to a ≤ b - 1, which over whole numbers
    /// says the same, and lets the reals already see a contradiction that branching on fractions would chase for ever.
    /// </summary>
    private SolverResult Solve(IEnumerable<Constraint> constraints) => _solver.Check(constraints.Select(Tightened).ToList(), _ => true);

    private static Constraint Tightened(Constraint constraint) =>
        constraint.Relation == SolverRelation.Less && constraint.Term.Constant.IsInteger && constraint.Term.Coefficients.Values.All(c => c.IsInteger)
            ? new Constraint(constraint.Term + LinearTerm.Of(1), SolverRelation.LessOrEqual)
            : constraint;

    /// <summary>A term in the code's own words: len(a) - 1, n + 2.</summary>
    private static string Shown(LinearTerm term, Quantities quantities)
    {
        var parts = new List<string>();

        foreach (var (symbol, coefficient) in term.Coefficients)
        {
            var name = quantities.ShownAs(symbol);
            var magnitude = coefficient.Sign < 0 ? -coefficient : coefficient;
            var written = magnitude == Rational.One ? name : $"{magnitude} * {name}";
            parts.Add(parts.Count == 0 ? (coefficient.Sign < 0 ? "-" + written : written) : (coefficient.Sign < 0 ? "- " + written : "+ " + written));
        }

        if (!term.Constant.IsZero || parts.Count == 0)
        {
            var constant = term.Constant;
            parts.Add(parts.Count == 0 ? constant.ToString() : constant.Sign < 0 ? $"- {-constant}" : $"+ {constant}");
        }

        return string.Join(" ", parts);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Counted loops: the counter tied to its limit.

    /// <summary>A loop whose counter moves by one through every value from its first to its last, and the statements that see each value.</summary>
    private sealed record CountedLoop(Stmt Loop, string Counter, Expr? First, Expr Limit, bool Inclusive, int Step, IReadOnlyList<Stmt> Body);

    private CountedLoop? Counted(Stmt statement)
    {
        switch (statement)
        {
            // for i in range(stop), range(start, stop), range(start, stop, 1): the counter never reaches stop.
            case ForEach { Target: Name { Identifier: var counter }, Items: Call { Callee: Name { Identifier: "range" }, Arguments: var arguments } } loop when IsPython:
                var (first, stop) = arguments.Count switch
                {
                    1 => ((Expr?)new Literal(loop.Span, LiteralKind.Integer, 0L), arguments[0].Value),
                    2 => (arguments[0].Value, arguments[1].Value),
                    3 when arguments[2].Value is Literal { Kind: LiteralKind.Integer, Value: 1L } => (arguments[0].Value, arguments[1].Value),
                    _ => ((Expr?)null, (Expr?)null),
                };
                return stop is null || Assigned(loop.Body, counter) ? null : new CountedLoop(loop, counter, first, stop, Inclusive: false, Step: 1, loop.Body);

            // for (int i = first; i < limit; i++), and the loops that count down with > and >=.
            case For { Condition: Binary { Left: Name { Identifier: var counter } } condition } loop when Stepping(loop.Step, counter) is { } step:
                var upward = condition.Operator is BinaryOperator.Less or BinaryOperator.LessOrEqual;
                var downward = condition.Operator is BinaryOperator.Greater or BinaryOperator.GreaterOrEqual;
                if (!(upward && step > 0 || downward && step < 0) || Assigned(loop.Body, counter)) return null;

                var start = loop.Setup.Select(s => s switch
                {
                    Declare { Variable: var declared, Initial: { } initial } when declared == counter => initial,
                    Assign { Target: Name { Identifier: var assigned }, Value: var value, Compound: null } when assigned == counter => value,
                    _ => null,
                }).LastOrDefault(e => e is not null);

                var inclusive = condition.Operator is BinaryOperator.LessOrEqual or BinaryOperator.GreaterOrEqual;
                return new CountedLoop(loop, counter, start, condition.Right, inclusive, step, loop.Body);

            // while i < limit: ... i += 1 - the step last in the body, so everything before it sees that time round's value.
            case While { TestsFirst: true, Condition: Binary { Left: Name { Identifier: var counter } } condition } loop
                when loop.Body.Count > 1 && Stepping([loop.Body[^1]], counter) is { } whileStep:
                var whileUpward = condition.Operator is BinaryOperator.Less or BinaryOperator.LessOrEqual;
                var whileDownward = condition.Operator is BinaryOperator.Greater or BinaryOperator.GreaterOrEqual;
                var before = loop.Body.Take(loop.Body.Count - 1).ToList();
                if (!(whileUpward && whileStep > 0 || whileDownward && whileStep < 0) || Assigned(before, counter)) return null;

                var entered = BlockBefore(function.Body, loop)?.LastOrDefault(s => Gives(s, counter)) switch
                {
                    Assign { Compound: null, Value: var value } => value,
                    Declare { Initial: { } initial } => initial,
                    _ => null,
                };

                var whileInclusive = condition.Operator is BinaryOperator.LessOrEqual or BinaryOperator.GreaterOrEqual;
                return new CountedLoop(loop, counter, entered, condition.Right, whileInclusive, whileStep, before);

            default:
                return null;
        }
    }

    /// <summary>The loop's step, when it is the counter moving by exactly one: i++, i--, i += 1, i -= 1, i = i + 1.</summary>
    private static int? Stepping(IReadOnlyList<Stmt> steps, string counter) => steps is [var only] ? only switch
    {
        Assign { Target: Name { Identifier: var name }, Compound: { } op, Value: Literal { Kind: LiteralKind.Integer, Value: 1L } } when name == counter =>
            op == BinaryOperator.Add ? 1 : op == BinaryOperator.Subtract ? -1 : null,
        Assign { Target: Name { Identifier: var name }, Compound: null, Value: Binary { Left: Name { Identifier: var read }, Right: Literal { Kind: LiteralKind.Integer, Value: 1L } } sum }
            when name == counter && read == counter => sum.Operator == BinaryOperator.Add ? 1 : sum.Operator == BinaryOperator.Subtract ? -1 : null,
        _ => null,
    } : null;

    private static bool Assigned(IReadOnlyList<Stmt> body, string name) =>
        IrWalk.Statements(body).Any(s => s switch
        {
            Assign { Target: Name { Identifier: var target } } => target == name,
            Declare { Variable: var declared } => declared == name,
            ForEach { Target: Name { Identifier: var target } } => target == name,
            _ => false,
        }) || IrWalk.Statements(body).SelectMany(IrWalk.Expressions).Any(e => AssignsInside(e, name));

    private static bool AssignsInside(Expr expression, string name) =>
        expression is AssignValue { Target: Name { Identifier: var target } } && target == name || IrWalk.Children(expression).Any(child => AssignsInside(child, name));

    /// <summary>
    /// Indexes in a counted loop that are outside the collection on its first or last time round. Only a loop that always
    /// runs to its end counts - no break, continue or return can cut it short - and only an index the body reads every time
    /// round, into a collection nothing in the loop changes.
    /// </summary>
    private void OutsideTheCollection(CountedLoop loop)
    {
        if (IrWalk.Statements(loop.Body).Any(s => s is Break or Continue or Return or Throw)) return;

        foreach (var statement in loop.Body)
        {
            if (statement is If or While or For or ForEach or Switch or Try) continue;

            foreach (var access in IrWalk.Expressions(statement).SelectMany(Accesses))
            {
                if (Changed(loop.Body, access.Collection) || IsKeyed(access.Collection)) continue;
                Settle(loop, access);
            }
        }
    }

    /// <summary>An index read: the collection, the index expression, and whether it is read by a method - list.get(i) - rather than a[i].</summary>
    private sealed record Access(Expr Whole, string Collection, Expr Index, bool ByMethod);

    private IEnumerable<Access> Accesses(Expr expression)
    {
        switch (expression)
        {
            case Opaque { What: "lambda expression" or "lambda" }:
                yield break;

            case ElementAccess { Target: Name { Identifier: var collection }, Key: var index } element:
                yield return new Access(element, collection, index, ByMethod: false);
                break;

            case Call { Callee: Member { Target: Name { Identifier: var collection }, MemberName: "get" or "charAt" }, Arguments: [{ Value: var index }] } read
                when language == SourceLanguage.Java:
                yield return new Access(read, collection, index, ByMethod: true);
                break;
        }

        foreach (var child in IrWalk.Children(expression))
            foreach (var inner in Accesses(child)) yield return inner;
    }

    /// <summary>Whether anything in the loop could change how long the collection is: a new value, a method that adds or removes, or handing it to code that might.</summary>
    private static bool Changed(IReadOnlyList<Stmt> body, string collection) =>
        Assigned(body, collection) ||
        IrWalk.Statements(body).SelectMany(IrWalk.Expressions).SelectMany(Effects.CallsIn).Any(call =>
        {
            var changesIt = call.Callee is Member { Target: Name { Identifier: var receiver }, MemberName: var method } && receiver == collection &&
                            (Effects.Adding.Contains(method) || Effects.Removing.Contains(method));
            var handsItOn = call.Arguments.Any(a => a.Value is Name { Identifier: var passed } && passed == collection) &&
                            !(call.CalleeName is { } name && HarmlessCalls.Contains(name));
            return changesIt || handsItOn;
        });

    /// <summary>A dictionary or map, indexed by key rather than by position - where a missing key is a different failure.</summary>
    private bool IsKeyed(string collection)
    {
        static bool Keyed(string type) => type.Contains("Map", StringComparison.Ordinal) || type.Contains("Dictionary", StringComparison.Ordinal) || type == "dict";

        return function.Parameters.Any(p => p.Name == collection && Keyed(p.Type.Name)) || IrWalk.Statements(function.Body).Any(s => s switch
        {
            Assign { Target: Name { Identifier: var name }, Value: CollectionLiteral { Kind: CollectionKind.Dictionary } or Call { Callee: Name { Identifier: "dict" } } } =>
                name == collection,
            Declare { Variable: var name, Type.Name: var type } => name == collection && Keyed(type),
            _ => false,
        });
    }

    /// <summary>
    /// Asks the solver whether the index is outside the collection whenever the counter is at its highest - on the last
    /// time round counting up, the first counting down - for every length the collection could have, provided the loop
    /// runs at all; and, in Java and C#, whether it is below zero whenever the counter is at its lowest.
    /// </summary>
    private void Settle(CountedLoop loop, Access access)
    {
        var quantities = new Quantities();
        LinearTerm? Values(string name) => quantities.Of(name);

        if (Linear(access.Index, Values, quantities) is not { } index) return;
        if (quantities.NumberOf(loop.Counter) is not { } counterSymbol || !index.Symbols.Contains(counterSymbol)) return;
        if (Linear(loop.Limit, Values, quantities) is not { } limit) return;

        var counter = LinearTerm.Symbol(counterSymbol);
        var length = quantities.Of($"length of {access.Collection}", LengthShown(access));

        // The counter's value on the loop's last time round: one short of the limit for < and >, the limit itself for <= and >=.
        var last = loop.Inclusive ? limit : limit - LinearTerm.Of(loop.Step);
        var first = loop.First is { } start ? Linear(start, Values, quantities) : null;

        var (highest, lowest) = loop.Step > 0 ? (last, first) : (first, last);
        var (highWhen, lowWhen) = loop.Step > 0 ? ("last", "first") : ("first", "last");
        var runs = first is null ? [] : loop.Step > 0 ? new[] { Constraint.AtMost(first, last) } : [Constraint.AtLeast(first, last)];

        if (highest is not null)
        {
            var atHighest = new List<Constraint>([.. quantities.Facts, .. runs, Constraint.Same(counter, highest)]);
            if (Solve(atHighest).IsSatisfiable && Solve([.. atHighest, Constraint.AtMost(index, length - LinearTerm.Of(1))]).IsUnsatisfiable)
            {
                ReportIndex(loop, access, highest, index, quantities, highWhen, pastTheEnd: true);
                return;
            }
        }

        // A negative index counts back from the end in Python, so only Java, C and C# fail below zero.
        if (!IsPython && lowest is not null)
        {
            var atLowest = new List<Constraint>([.. quantities.Facts, .. runs, Constraint.Same(counter, lowest)]);
            if (Solve(atLowest).IsSatisfiable && Solve([.. atLowest, Constraint.AtLeast(index, LinearTerm.Of(0))]).IsUnsatisfiable)
                ReportIndex(loop, access, lowest, index, quantities, lowWhen, pastTheEnd: false);
        }
    }

    /// <summary>How the language writes the collection's length: len(a), a.length, a.size(), a.Length.</summary>
    private string LengthShown(Access access) => language switch
    {
        SourceLanguage.Python => $"len({access.Collection})",
        SourceLanguage.Java => access.ByMethod ? $"{access.Collection}.size()" : $"{access.Collection}.length",
        _ => $"{access.Collection}.Length",
    };

    private void ReportIndex(CountedLoop loop, Access access, LinearTerm counterValue, LinearTerm index, Quantities quantities, string when, bool pastTheEnd)
    {
        var counterSymbol = quantities.NumberOf(loop.Counter)!.Value;
        var position = index - LinearTerm.Symbol(counterSymbol) * index.Coefficients[counterSymbol] + counterValue * index.Coefficients[counterSymbol];

        var failure = (language, access.ByMethod) switch
        {
            (SourceLanguage.Python, _) => "IndexError",
            (SourceLanguage.Java, true) => "an IndexOutOfBoundsException",
            (SourceLanguage.Java, false) => "an ArrayIndexOutOfBoundsException",
            (SourceLanguage.CSharp, _) => "an IndexOutOfRangeException",
            _ => "undefined behaviour - reading memory outside the array",
        };

        var (side, advice) = pastTheEnd
            ? ($"past the end of `{access.Collection}`", "Stop the loop one sooner, or check the index against the length before reading")
            : ($"before the start of `{access.Collection}`", "Start the loop one later, or check the index is not negative before reading");

        report(new AnalysisFinding("analysis-index-out-of-range", access.Whole.Span,
            $"On the {when} time round the loop, `{loop.Counter}` is `{Shown(counterValue, quantities)}`, so `{IrText.Of(access.Whole)}` asks for position " +
            $"`{Shown(position, quantities)}` - {side} - and fails with {failure}. {advice}",
            Severity.Error, Confidence.Likely, FindingKind.Runtime, AbstractChecks.FoundBy));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // While loops: inductive invariants, and a state one time round leaves unchanged.

    /// <summary>One way through a loop body: the conditions it takes, what each variable ends as, what it does, and whether it leaves the loop.</summary>
    private sealed record Way(List<Constraint> Conditions, Dictionary<string, LinearTerm> Values, List<string> Taken, bool Leaves);

    /// <summary>
    /// A while loop that can come back round with nothing changed. Its body is followed one time round with every variable
    /// a symbol; candidate invariants are kept only if the solver shows them inductive; then the solver looks for a state
    /// the invariants allow, where the condition holds and some way through the body leaves every variable it changes as
    /// it was. Only a loop whose condition reads the function's own variables counts: nothing else can move them on.
    /// </summary>
    private void Stuck(While loop)
    {
        // What the body gives a value - int mid = ... declares a new one every time round, which is giving it a value too.
        var changed = IrWalk.Statements(loop.Body).SelectMany(s => s switch
        {
            Assign { Target: Name { Identifier: var assigned } } => [assigned],
            Declare { Variable: var declared } => new[] { declared },
            _ => [],
        }).Distinct(StringComparer.Ordinal).ToList();
        var conditionNames = IrWalk.Names(loop.Condition).Distinct(StringComparer.Ordinal).ToList();
        var own = IrWalk.LocalNames(function, assigningDeclares: IsPython);

        if (changed.Count == 0 || !conditionNames.Any(changed.Contains) || !conditionNames.All(n => own.Contains(n) || LengthsIn(loop.Condition).Contains(n))) return;
        if (IrWalk.Statements(loop.Body).Any(s => s is While or For or ForEach or Try or Switch)) return;
        if (IrWalk.Statements(loop.Body).SelectMany(IrWalk.Expressions).SelectMany(Effects.CallsIn).Any(c => !HarmlessCalls.Contains(c.CalleeName ?? ""))) return;

        var quantities = new Quantities();
        var start = changed.Concat(conditionNames).Distinct(StringComparer.Ordinal).ToDictionary(n => n, n => quantities.Of(n), StringComparer.Ordinal);
        LinearTerm? AtStart(string name) => start.TryGetValue(name, out var term) ? term : quantities.Of(name);

        if (Condition(loop.Condition, AtStart, quantities) is not { } holds) return;
        if (Ways(loop.Body, quantities, start) is not { Count: > 0 } ways) return;

        var invariants = Invariants(loop, holds, ways, quantities, start, changed);

        foreach (var way in ways.Where(w => !w.Leaves))
        {
            var unchanged = changed.Select(name => Constraint.Same(way.Values[name], start[name])).ToList();
            var stuck = new List<Constraint>([.. quantities.Facts, .. holds, .. invariants, .. way.Conditions, .. unchanged]);

            if (Solve(stuck) is not { IsSatisfiable: true, Model: { } model }) continue;

            ReportStuck(loop, way, model, quantities, conditionNames.Where(changed.Contains).ToList(), invariants, quantities);
            return;
        }
    }

    /// <summary>The collections a condition measures - len(a) - whose lengths the loop body cannot change, having no calls that could.</summary>
    private static HashSet<string> LengthsIn(Expr condition)
    {
        var measured = new HashSet<string>(StringComparer.Ordinal);

        void Visit(Expr expression)
        {
            if (LengthOf(expression) is { } collection) measured.Add(collection);
            foreach (var child in IrWalk.Children(expression)) Visit(child);
        }

        Visit(condition);
        return measured;
    }

    /// <summary>A condition as constraints: a comparison of linear terms, or several joined with and. Null for anything else.</summary>
    private List<Constraint>? Condition(Expr condition, Func<string, LinearTerm?> values, Quantities quantities)
    {
        if (condition is Binary { Operator: BinaryOperator.And } both)
            return Condition(both.Left, values, quantities) is { } left && Condition(both.Right, values, quantities) is { } right ? [.. left, .. right] : null;

        if (condition is not Binary comparison || Linear(comparison.Left, values, quantities) is not { } a || Linear(comparison.Right, values, quantities) is not { } b) return null;

        return comparison.Operator switch
        {
            BinaryOperator.Less => [Constraint.Below(a, b)],
            BinaryOperator.LessOrEqual => [Constraint.AtMost(a, b)],
            BinaryOperator.Greater => [Constraint.Above(a, b)],
            BinaryOperator.GreaterOrEqual => [Constraint.AtLeast(a, b)],
            BinaryOperator.NotEqual => [Constraint.Different(a, b)],
            BinaryOperator.Equal => [Constraint.Same(a, b)],
            _ => null,
        };
    }

    /// <summary>
    /// Every way through a body, each variable's end value a linear term of the start values. A condition that is not
    /// linear leaves both ways open; a value that is not linear is a fresh unknown. Null when the body does something that
    /// cannot be followed like this.
    /// </summary>
    private List<Way>? Ways(IReadOnlyList<Stmt> body, Quantities quantities, IReadOnlyDictionary<string, LinearTerm> start)
    {
        var ways = new List<Way> { new([], new Dictionary<string, LinearTerm>(start, StringComparer.Ordinal), [], false) };

        foreach (var statement in body)
        {
            var next = new List<Way>();

            foreach (var way in ways)
            {
                if (way.Leaves)
                {
                    next.Add(way);
                    continue;
                }

                if (Follow(statement, way, quantities) is not { } followed) return null;
                next.AddRange(followed);
            }

            if (next.Count > MostWays) return null;
            ways = next;
        }

        return ways;
    }

    private List<Way>? Follow(Stmt statement, Way way, Quantities quantities)
    {
        LinearTerm? Current(string name) => way.Values.TryGetValue(name, out var term) ? term : quantities.Of(name);

        switch (statement)
        {
            case Assign { Target: Name { Identifier: var name }, Value: var value, Compound: var compound }:
                var assigned = compound is { } op ? new Binary(statement.Span, op, new Name(statement.Span, name), value) : value;
                var values = new Dictionary<string, LinearTerm>(way.Values, StringComparer.Ordinal)
                {
                    [name] = Linear(assigned, Current, quantities) ?? quantities.Fresh($"`{IrText.Of(value)}`"),
                };
                return [way with { Values = values, Taken = [.. way.Taken, $"`{name} = {IrText.Of(assigned)}`"] }];

            case If branch:
                var test = Condition(branch.Condition, Current, quantities);
                if (Ways(branch.Then, quantities, way.Values) is not { } yes || Ways(branch.Else, quantities, way.Values) is not { } no) return null;

                var result = new List<Way>();
                foreach (var then in yes)
                    result.Add(new Way([.. way.Conditions, .. test ?? [], .. then.Conditions], then.Values,
                        [.. way.Taken, $"`{IrText.Of(branch.Condition)}` being true", .. then.Taken], then.Leaves));

                foreach (var otherwise in no)
                    result.Add(new Way([.. way.Conditions, .. test is [var single] ? [single.Negated()] : Array.Empty<Constraint>(), .. otherwise.Conditions], otherwise.Values,
                        [.. way.Taken, $"`{IrText.Of(branch.Condition)}` being false", .. otherwise.Taken], otherwise.Leaves));
                return result;

            case Declare { Variable: var declared, Initial: { } initial }:
                return Follow(new Assign(statement.Span, new Name(statement.Span, declared), initial), way, quantities);

            case Declare { Variable: var declared }:
                var unset = new Dictionary<string, LinearTerm>(way.Values, StringComparer.Ordinal) { [declared] = quantities.Fresh($"`{declared}`") };
                return [way with { Values = unset }];

            case Break or Return or Throw:
                return [way with { Leaves = true }];

            case Evaluate:
                return [way];

            default:
                return null;
        }
    }

    /// <summary>
    /// Candidate invariants, each kept only if it is inductive: true when the loop is entered, given what the code sets
    /// just before it, and kept by every way through the body that goes round again, from any state where it and the
    /// condition hold. The candidates: the condition's relation relaxed by one step (lo ≤ hi from lo &lt; hi), and each
    /// variable's entry value as a bound below and above - of which only those its updates respect survive.
    /// </summary>
    private List<Constraint> Invariants(While loop, List<Constraint> holds, List<Way> ways, Quantities quantities,
        IReadOnlyDictionary<string, LinearTerm> start, IReadOnlyList<string> changed)
    {
        var entry = EntryValues(loop, quantities, changed);
        var entering = entry.Select(pair => Constraint.Same(start[pair.Key], pair.Value)).ToList();

        var candidates = holds.Select(condition => condition.Relation switch
            {
                SolverRelation.Less => new Constraint(condition.Term, SolverRelation.LessOrEqual),
                SolverRelation.LessOrEqual => new Constraint(condition.Term - LinearTerm.Of(1), SolverRelation.LessOrEqual),
                _ => null,
            })
            .OfType<Constraint>()
            .Concat(entry.SelectMany(pair => new[] { Constraint.AtLeast(start[pair.Key], pair.Value), Constraint.AtMost(start[pair.Key], pair.Value) }))
            .ToList();

        var proven = new List<Constraint>();

        foreach (var candidate in candidates)
        {
            // Every symbol the candidate speaks of must be pinned down on entry, or its holding there cannot be shown.
            if (!Solve([.. quantities.Facts, .. entering, candidate.Negated()]).IsUnsatisfiable) continue;

            var kept = ways.Where(w => !w.Leaves).All(way =>
                Renamed(candidate, way.Values, start) is { } after &&
                Solve([.. quantities.Facts, .. holds, candidate, .. way.Conditions, after.Negated()]).IsUnsatisfiable);

            if (kept) proven.Add(candidate);
        }

        return proven;
    }

    /// <summary>
    /// What the code gives the loop's variables just before it: the last plain assignment in the same block, of a value
    /// that is linear in things the loop does not change - 0, len(a), n - 1.
    /// </summary>
    private Dictionary<string, LinearTerm> EntryValues(While loop, Quantities quantities, IReadOnlyList<string> changed)
    {
        var entry = new Dictionary<string, LinearTerm>(StringComparer.Ordinal);
        if (BlockBefore(function.Body, loop) is not { } before) return entry;

        foreach (var name in changed)
        {
            var value = before.LastOrDefault(s => Gives(s, name)) switch
            {
                Assign { Compound: null, Value: var assigned } => assigned,
                Declare { Initial: { } initial } => initial,
                _ => null,
            };

            if (value is not null && Linear(value, n => changed.Contains(n) ? null : quantities.Of(n), quantities) is { } linear) entry[name] = linear;
        }

        return entry;
    }

    private static bool Gives(Stmt statement, string name) =>
        statement is Assign { Target: Name { Identifier: var target } } && target == name || statement is Declare { Variable: var declared } && declared == name;

    /// <summary>The statements before a loop in the block that holds it - which run, in order, every time the loop is reached.</summary>
    private static IReadOnlyList<Stmt>? BlockBefore(IReadOnlyList<Stmt> block, Stmt loop)
    {
        for (var index = 0; index < block.Count; index++)
        {
            if (ReferenceEquals(block[index], loop)) return block.Take(index).ToList();

            var inner = block[index] switch
            {
                If branch => new[] { branch.Then, branch.Else },
                While whileLoop => [whileLoop.Body, whileLoop.Else],
                For forLoop => [forLoop.Body],
                ForEach eachLoop => [eachLoop.Body, eachLoop.Else],
                Try attempt => [attempt.Body, .. attempt.Handlers.Select(h => h.Body), attempt.Else, attempt.Finally],
                Using used => [used.Body],
                Labeled labeled => [labeled.Body],
                _ => [],
            };

            foreach (var nested in inner)
                if (BlockBefore(nested, loop) is { } found) return found;
        }

        return null;
    }

    /// <summary>A constraint on the start values, restated on what one way through the body ends with.</summary>
    private static Constraint? Renamed(Constraint constraint, IReadOnlyDictionary<string, LinearTerm> ends, IReadOnlyDictionary<string, LinearTerm> start)
    {
        var term = constraint.Term;

        foreach (var (name, begin) in start)
        {
            var symbol = begin.Symbols.Single();
            if (!term.Coefficients.TryGetValue(symbol, out var coefficient)) continue;
            if (!ends.TryGetValue(name, out var end)) return null;
            term = term - begin * coefficient + end * coefficient;
        }

        return new Constraint(term, constraint.Relation);
    }

    private void ReportStuck(While loop, Way way, IReadOnlyDictionary<int, Rational> model, Quantities quantities, IReadOnlyList<string> moved,
        IReadOnlyList<Constraint> invariants, Quantities shown)
    {
        var state = moved
            .Select(name => quantities.NumberOf(name) is { } symbol && model.TryGetValue(symbol, out var value) ? $"`{name}` is {value}" : null)
            .OfType<string>()
            .ToList();

        var when = state.Count > 0 ? $"When {Joined(state)}" : "In some state the loop can reach";
        var path = way.Taken.Count > 0 ? $", going round with {Joined(way.Taken)}" : "";
        var names = Joined(moved.Select(n => $"`{n}`").ToList());
        var kept = invariants.Count > 0
            ? $" That state is one the loop can be in: it keeps {Joined(invariants.Select(i => $"`{InCodeWords(i, shown)}`").ToList())}, which the loop's own code keeps every time round."
            : "";

        report(new AnalysisFinding(StuckRule, loop.Condition.Span,
            $"{when}{path}, one time round the loop leaves {names} exactly as {(moved.Count == 1 ? "it was" : "they were")}, so the loop comes back to the same " +
            $"state and never ends.{kept} Make sure every way through the loop moves {(moved.Count == 1 ? "it" : "them")} closer to ending it",
            Severity.Error, Confidence.Likely, FindingKind.Logic, AbstractChecks.FoundBy));
    }

    /// <summary>An invariant in the code's own words: lo <= hi, lo >= 0.</summary>
    private static string InCodeWords(Constraint constraint, Quantities quantities) =>
        Readable(constraint, quantities) ?? $"{Shown(constraint.Term, quantities)} {(constraint.Relation == SolverRelation.Less ? "<" : "<=")} 0";

    /// <summary>x - y ≤ 0 read as x ≤ y, and x - c ≤ 0 as x ≤ c, where the term is that simple.</summary>
    private static string? Readable(Constraint constraint, Quantities quantities)
    {
        var positive = constraint.Term.Coefficients.Where(c => c.Value == Rational.One).Select(c => quantities.ShownAs(c.Key)).ToList();
        var negative = constraint.Term.Coefficients.Where(c => c.Value == -Rational.One).Select(c => quantities.ShownAs(c.Key)).ToList();
        if (positive.Count + negative.Count != constraint.Term.Coefficients.Count || positive.Count > 1 || negative.Count > 1) return null;

        var sign = constraint.Relation == SolverRelation.Less ? "<" : "<=";
        var left = positive.Count == 1 ? positive[0] : $"{-constraint.Term.Constant}";
        var constant = constraint.Term.Constant;

        if (positive.Count == 1 && negative.Count == 1)
            return constant.IsZero ? $"{positive[0]} {sign} {negative[0]}" : $"{positive[0]} {sign} {negative[0]} {(constant.Sign < 0 ? "+" : "-")} {(constant.Sign < 0 ? -constant : constant)}";
        if (positive.Count == 1) return $"{positive[0]} {sign} {-constant}";
        if (negative.Count == 1) return $"{negative[0]} {(sign == "<" ? ">" : ">=")} {constant}";
        return left;
    }

    private static string Joined(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => "",
        1 => parts[0],
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
    };
}
